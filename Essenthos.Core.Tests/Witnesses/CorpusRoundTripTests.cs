using System.Diagnostics;
using System.Xml.Linq;
using Essenthos.Core.Bhsa;
using Essenthos.Core.Bhsa.Attributes;
using Essenthos.Core.Loading;
using Essenthos.Core.Nestle;
using Essenthos.Core.XmlBible;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Essenthos.Core.Tests.Witnesses;

/// <summary>
/// The parsers, run over the sources they will be run over in earnest, asserting that the words
/// they produce rebuild the text they were given. This is the check the corpus never had: the two
/// corruptions that reached the database were both invisible to the unit tests, because a unit test
/// feeds a parser the input its author thought of.
/// </summary>
[Trait(TestCategory.Name, TestCategory.Corpus)]
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
    [InlineData("KJV", false)]
    [InlineData("RUSV", false)]
    [InlineData("UKR", true)]
    public void EveryBible4uVerseRebuildsItsStrippedSource(string translation, bool strongTagged)
    {
        var bible = new XmlBibleParser().Parse(File.ReadAllText(TestResources.Bible4u(translation)));

        var failures = new List<string>();
        var verses = 0;
        foreach (var book in bible.Books)
        foreach (var chapter in book.Chapters)
        foreach (var verse in chapter.Verses)
        {
            verses++;
            var words = VerseWords.Parse(verse.Text, strongTagged);
            var failure = VerseRoundTrip.Check(
                $"{translation} {book.BsName} {chapter.CNumber}:{verse.VNumber}",
                VerseRoundTrip.Rebuild(words, w => w.Word, w => w.Trailer),
                VerseWords.StripMarkup(verse.Text, strongTagged));
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
    /// Ohienko's file prints sixteen verses with the Strong tags of a tagged edition left in the text
    /// as bare numbers, 158 of them in 131 runs (PRB-1789). Read as the text's words they showed in the
    /// reader and every quotation: "Пророцтво 4853 про Бегемота 929". They are each run's one number
    /// in Strong's range on the word before it, and no number is a word of any of the three files.
    /// </summary>
    [Theory]
    [InlineData("KJV", 0, 0)]
    [InlineData("RUSV", 0, 0)]
    [InlineData("UKR", 131, 16)]
    public void StrongTagsInTheTextAreNumbersOnTheWordsAndNotWords(string translation, int tagged, int verses)
    {
        var text = Bible4uTextSource.Read(TestResources.Bible4u(translation), translation);
        var all = text.Books
            .SelectMany(book => book.Chapters.SelectMany(chapter => chapter.Verses.Select(verse =>
                (Address: $"{book.CanonicalOrdinal}.{chapter.Number}:{verse.Number}", verse.Words))))
            .ToList();

        all.SelectMany(verse => verse.Words).Where(word => word.Surface.Length > 0 && word.Surface.All(char.IsDigit))
            .Should().BeEmpty();
        all.SelectMany(verse => verse.Words).Count(word => word.StrongNumber is not null).Should().Be(tagged);
        all.Where(verse => verse.Words.Any(word => word.StrongNumber is not null)).Select(verse => verse.Address)
            .Should().HaveCount(verses);

        if (translation == "UKR")
        {
            var isaiah306 = all.Single(verse => verse.Address == "23.30:6").Words;
            isaiah306.Take(4).Select(word => $"{word.Surface} {word.StrongNumber}").Should().Equal(
                "Пророцтво H4853", "про ", "Бегемота H929", "півдня H5045");
            string.Concat(isaiah306.Select(word => word.Surface + word.Trailer)).Should().StartWith(
                "Пророцтво про Бегемота півдня. В краю утиску та переслідування, звідки левиця та лев,");
        }
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

    private static string Nestle1904Path() => TestResources.Nestle1904;
}
