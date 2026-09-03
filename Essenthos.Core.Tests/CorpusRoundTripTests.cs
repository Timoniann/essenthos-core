using System.Diagnostics;
using System.Xml.Linq;
using Essenthos.Core.Bhsa;
using Essenthos.Core.Bhsa.Attributes;
using Essenthos.Core.Loading;
using Essenthos.Core.Nestle;
using Essenthos.Core.XmlBible;
using Essenthos.Core.Zefania;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Essenthos.Core.Tests;

/// <summary>
/// The parsers, run over the sources they will be run over in earnest, asserting that the words
/// they produce rebuild the text they were given. This is the check the corpus never had: the two
/// corruptions that reached the database were both invisible to the unit tests, because a unit test
/// feeds a parser the input its author thought of.
/// </summary>
public class CorpusRoundTripTests(ITestOutputHelper output)
{
    /// <summary>How many failures to name before giving up; a broken parser fails everywhere.</summary>
    private const int FailuresToReport = 5;

    /// <summary>
    /// Measured on the corpus as loaded. A parser that starts losing words moves these, which is a
    /// cheaper signal than any assertion about the words themselves.
    /// </summary>
    private const int NestleWordCount = 137_779;

    private const int BhsaWordCount = 426_590;

    /// <summary>Measured: 6,464 of these are the elided article, the rest six other parts of speech.</summary>
    private const int BhsaWordsWithoutSurfaceText = 6_488;

    private const int BhsaElidedArticles = 6_464;

    /// <summary>
    /// The Synodal's bracketed spans, measured over its file: 3,363 in the Old Testament and 884
    /// in the New, none nested and none unbalanced.
    /// </summary>
    private const int SynodalSuppliedSpans = 4_247;

    /// <summary>The words those spans cover — 3,577 of the spans are a single word.</summary>
    private const int SynodalSuppliedWords = 5_054;

    private const int SynodalVersesWithSupplied = 3_708;

    /// <summary>Measured over all 31,102 verses: 925 commas, 401 stops, 243 colons, and the rest.</summary>
    private const int ZefaniaVersesLosingPunctuation = 1_755;

    private const int ZefaniaPunctuationDropped = 1_906;

    /// <summary>
    /// Every Greek word carries its own punctuation, so the round trip is per word and exact: the
    /// word and its trailer are the element's own text with the separating space appended. The
    /// tokeniser used to slice one character short, and 19,740 words lost the case ending that the
    /// last character of a Greek word is.
    /// </summary>
    [Fact]
    public void EveryNestleWordRebuildsItsSourceElement()
    {
        var document = XDocument.Load(Nestle1904Path());
        var elements = document.Root!.Elements("w").ToList();
        var words = new NestleParser().Parse(File.ReadAllText(Nestle1904Path()), glossText: null);

        words.Should().HaveCount(elements.Count);
        words.Should().HaveCount(NestleWordCount);

        var failures = new List<string>();
        for (var i = 0; i < words.Count && failures.Count < FailuresToReport; i++)
        {
            var failure = VerseRoundTrip.Check(words[i].OsisId, words[i].Word + words[i].Trailer,
                elements[i].Value + " ");
            if (failure is not null)
            {
                failures.Add(failure.Describe());
            }
        }

        failures.Should().BeEmpty(string.Join(Environment.NewLine, failures));
    }

    /// <summary>
    /// The bible4u sources carry editorial markup inside the verse text — a psalm's Hebrew
    /// numbering as "(22-1)", a superscription marked with "^^" — which is removed once, before
    /// tokenising. So the round trip is against the stripped verse, exactly, and it holds for all
    /// three translations.
    /// </summary>
    [Theory]
    [InlineData("KJV")]
    [InlineData("RUSV")]
    [InlineData("UKR")]
    public void EveryBible4uVerseRebuildsItsStrippedSource(string translation)
    {
        var bible = new XmlBibleParser().Parse(File.ReadAllText(TestResources.Bible4u(translation)));

        var failures = new List<string>();
        var verses = 0;
        foreach (var book in bible.Books)
        foreach (var chapter in book.Chapters)
        foreach (var verse in chapter.Verses)
        {
            verses++;
            var words = VerseWords.Parse(verse.Text);
            var failure = VerseRoundTrip.Check(
                $"{translation} {book.BsName} {chapter.CNumber}:{verse.VNumber}",
                VerseRoundTrip.Rebuild(words, w => w.Word, w => w.Trailer),
                VerseWords.StripMarkup(verse.Text));
            if (failure is not null && failures.Count < FailuresToReport)
            {
                failures.Add(failure.Describe());
            }
        }

        output.WriteLine($"{translation}: {verses} verses");
        verses.Should().BeGreaterThan(30_000);
        failures.Should().BeEmpty(string.Join(Environment.NewLine, failures));
    }

    /// <summary>
    /// The Synodal's square brackets, counted over the file rather than assumed from the
    /// convention. They are not the Septuagint readings they are usually said to be: 3,577 of the
    /// 4,247 spans are one word, the longest is seven, and 884 stand in the New Testament, where
    /// nothing is being compared with the Masoretic. They are the words the edition supplies.
    ///
    /// Pinned here because the parser now decides where a span starts, and two brackets side by
    /// side are two spans — 79 places where a per-word flag would count one.
    /// </summary>
    [Theory]
    [InlineData("RUSV", SynodalSuppliedSpans, SynodalSuppliedWords, SynodalVersesWithSupplied)]
    [InlineData("KJV", 0, 0, 0)]
    [InlineData("UKR", 0, 0, 0)]
    public void TheEditionsOwnMarkupIsSpansAndNotCharacters(
        string translation,
        int expectedSpans,
        int expectedWords,
        int expectedVerses)
    {
        var bible = new XmlBibleParser().Parse(File.ReadAllText(TestResources.Bible4u(translation)));

        var spans = 0;
        var supplied = 0;
        var verses = 0;
        var brackets = 0;
        foreach (var book in bible.Books)
        foreach (var chapter in book.Chapters)
        foreach (var verse in chapter.Verses)
        {
            var words = VerseWords.Parse(verse.Text);
            var inVerse = words.Where(w => w.SuppliedSpan is not null).ToList();
            if (inVerse.Count > 0)
            {
                verses++;
                spans += inVerse.Select(w => w.SuppliedSpan).Distinct().Count();
                supplied += inVerse.Count;
            }

            brackets += words.Sum(w => w.Word.Count(IsBracket) + w.Trailer.Count(IsBracket));
        }

        output.WriteLine($"{translation}: {spans} spans over {supplied} words in {verses} verses");
        spans.Should().Be(expectedSpans);
        supplied.Should().Be(expectedWords);
        verses.Should().Be(expectedVerses);
        brackets.Should().Be(0);
    }

    private static bool IsBracket(char c) => c is '[' or ']';

    /// <summary>
    /// The Zefania King James interleaves Strong-tagged elements and styled spans with plain text.
    /// Every word survives — the sequence of words is the source's, verse for verse, and that is
    /// what the Greek mapping reads this file for. Punctuation does not: see the test below.
    /// </summary>
    [Fact]
    public void EveryZefaniaVerseRebuildsEveryWordOfItsSource()
    {
        var (parsed, sourceVerses) = ZefaniaKingJames();

        parsed.Should().HaveCount(sourceVerses.Count);

        var failures = new List<string>();
        for (var i = 0; i < parsed.Count && failures.Count < FailuresToReport; i++)
        {
            var (book, chapter, verse) = parsed[i];
            var failure = VerseRoundTrip.Check(
                $"KJV+ {book.ShortName} {chapter.Number}:{verse.Number}",
                LettersAndDigits(VerseRoundTrip.Rebuild(verse.Words, w => w.Text, w => w.Trailer)),
                LettersAndDigits(sourceVerses[i]));
            if (failure is not null)
            {
                failures.Add(failure.Describe());
            }
        }

        output.WriteLine($"KJV+: {parsed.Count} verses");
        failures.Should().BeEmpty(string.Join(Environment.NewLine, failures));
    }

    /// <summary>
    /// A defect this reader has today, pinned to its measured size so that changing it is a
    /// decision rather than an accident: punctuation standing between a styled or Strong-tagged
    /// span and the next word is dropped, because only a Strong-tagged span hands its trailing
    /// punctuation to the word before it. Nothing stored in the corpus comes from this reader — it
    /// is the Greek mapping's second view of the King James, matched word by word — so the loss is
    /// confined to trailers nobody keeps. It has to be repaired before this file ever becomes a
    /// text of its own.
    ///
    /// Nothing is ever gained, and a further 88 verses differ only in spaces the source itself puts
    /// inside its markup — "is Zoar )" — which this reader collapses and which are not a loss.
    /// </summary>
    [Fact]
    public void TheZefaniaReaderDropsPunctuationAfterAStyledSpan()
    {
        var (parsed, sourceVerses) = ZefaniaKingJames();

        var affected = 0;
        var dropped = 0;
        for (var i = 0; i < parsed.Count; i++)
        {
            var rebuilt = VerseRoundTrip.Rebuild(parsed[i].Verse.Words, w => w.Text, w => w.Trailer);
            var lost = Punctuation(sourceVerses[i]).Count - Punctuation(rebuilt).Count;
            if (lost <= 0)
            {
                continue;
            }

            affected++;
            dropped += lost;
        }

        affected.Should().Be(ZefaniaVersesLosingPunctuation);
        dropped.Should().Be(ZefaniaPunctuationDropped);
    }

    /// <summary>
    /// BHSA has no verse text of its own — a verse is its words — so what the parse owes is that no
    /// word is lost between the flat word list and the verses. The comparison against a source
    /// string belongs to the load, where the database is the other side of it.
    /// </summary>
    [Fact]
    public void EveryBhsaWordBelongsToExactlyOneVerse()
    {
        var started = Stopwatch.StartNew();
        var project = BhsaProject.Load(TestResources.Etcbc);
        output.WriteLine($"BHSA parsed in {started.Elapsed}");

        project.Words.Should().HaveCount(BhsaWordCount);
        project.Verses.Sum(v => v.Words.Count).Should().Be(project.Words.Count);
        project.Verses.SelectMany(v => v.Words).Select(w => w.SlotId).Distinct()
            .Should().HaveCount(project.Words.Count);
    }

    /// <summary>
    /// Thousands of BHSA words have no surface text at all, and they are real words: Hebrew elides
    /// the definite article into the preposition before it, and BHSA still records the article as
    /// its own slot so that the grammar stays sayable. A loader that skips a word with no text
    /// loses exactly the word a translation's "the" corresponds to, so the count is pinned here.
    /// </summary>
    [Fact]
    public void BhsaWordsWithNoSurfaceTextAreMostlyTheElidedArticle()
    {
        var project = BhsaProject.Load(TestResources.Etcbc);

        var empty = project.Words.Where(w => w.TextUtf8.Length == 0).ToList();

        empty.Should().HaveCount(BhsaWordsWithoutSurfaceText);
        empty.Count(w => w.PartOfSpeech == PartOfSpeech.Article).Should().Be(BhsaElidedArticles);
    }

    private (List<(ZefaniaBook Book, ZefaniaChapter Chapter, ZefaniaVerse Verse)> Parsed, List<string> Source)
        ZefaniaKingJames()
    {
        var content = File.ReadAllText(TestResources.ZefaniaKingJames);
        var bible = new ZefaniaParser().Parse(content);
        var document = XDocument.Parse(content);

        var source = document.Root!.Elements("BIBLEBOOK")
            .SelectMany(b => b.Elements("CHAPTER").SelectMany(c => c.Elements("VERS")))
            .Select(v => v.Value)
            .ToList();

        var parsed = bible.Books
            .SelectMany(b => b.Chapters.SelectMany(c => c.Verses.Select(v => (b, c, v))))
            .ToList();

        return (parsed, source);
    }

    private static string LettersAndDigits(string value) =>
        new([.. value.Where(char.IsLetterOrDigit)]);

    private static List<char> Punctuation(string value) =>
        [.. value.Where(c => !char.IsLetterOrDigit(c) && !char.IsWhiteSpace(c))];

    private static string Nestle1904Path() => TestResources.Nestle1904;
}
