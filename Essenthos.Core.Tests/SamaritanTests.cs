using System.Text.Json;
using Essenthos.Core.Loading;
using Essenthos.Core.Samaritan;
using FluentAssertions;
using Xunit;

namespace Essenthos.Core.Tests;

/// <summary>
/// The Samaritan Pentateuch as it comes off disk.
///
/// Two things here would go wrong silently and are therefore what is checked. The dataset's slot is
/// the letter rather than the word, so every verse reaches its words through the signs it covers
/// and an off-by-one there produces verses that are almost right; and it writes shin and sin as
/// single Unicode presentation forms, which render correctly, match nothing, and are dropped
/// altogether by the consonantal folding the links are made on.
/// </summary>
public sealed class SamaritanTests
{
    private static readonly Lazy<SamaritanProject> Project =
        new(() => SamaritanProject.Load(TestResources.Samaritan));

    private const int Books = 5;
    private const int Chapters = 187;
    private const int Verses = 5_841;
    private const int Words = 114_889;

    /// <summary>
    /// The presentation forms the dataset writes and no other Hebrew source in the corpus uses.
    /// Nothing that has been read may still carry one.
    /// </summary>
    private const char ShinWithShinDot = 'שׁ';

    private const char ShinWithSinDot = 'שׂ';

    private static IEnumerable<SamaritanVerse> AllVerses() =>
        Project.Value.Books.SelectMany(b => b.Chapters).SelectMany(c => c.Verses);

    [Fact]
    public void TheWholePentateuchIsRead()
    {
        var project = Project.Value;

        project.Books.Should().HaveCount(Books);
        project.Books.Select(b => b.Name).Should()
            .Equal("Genesis", "Exodus", "Leviticus", "Numbers", "Deuteronomy");
        project.Books.Sum(b => b.Chapters.Count).Should().Be(Chapters);
        AllVerses().Should().HaveCount(Verses);
        AllVerses().Sum(v => v.Words.Count).Should().Be(Words);
    }

    /// <summary>
    /// Which release was read, taken from the files rather than from the folder they sit in. The
    /// version is the only thing that distinguishes two downloads of the same repository, and the
    /// path can be anything.
    /// </summary>
    [Fact]
    public void TheReleaseSaysWhichOneItIs() =>
        Project.Value.Version.Should().StartWith("7.");

    /// <summary>
    /// Every chapter is numbered as the tradition numbers it, and holds every verse from one to its
    /// last. A verse reached through the wrong signs lands in the wrong chapter, and nothing
    /// downstream would notice: it would simply be beside the wrong Masoretic verse for ever.
    /// </summary>
    [Fact]
    public void EveryChapterHoldsEveryVerseFromOneToItsLast()
    {
        foreach (var book in Project.Value.Books)
        {
            book.Chapters.Select(c => c.Number).Should()
                .Equal(Enumerable.Range(1, book.Chapters.Count), because: $"{book.Name} is numbered from one");

            foreach (var chapter in book.Chapters)
            {
                chapter.Verses.Select(v => v.Number).Order().Should()
                    .OnlyHaveUniqueItems(because: $"{book.Name} {chapter.Number} numbers each verse once");
            }
        }
    }

    /// <summary>
    /// The two places the Samaritan does not number a verse the Masoretic numbers, and they are not
    /// losses. Exodus 30:1-10 is the altar of incense, which this tradition sets after Exodus 26:35
    /// instead — Exodus 26 carries 883 words here against the Masoretic 739, and Exodus 30 carries
    /// 462 against 642. Deuteronomy 34:2-3 is written as part of 34:1.
    ///
    /// It is asserted rather than left to be discovered because a missing address reads as a hole,
    /// and a reader told the Samaritan omits the altar of incense would have been told the opposite
    /// of what is true.
    /// </summary>
    [Fact]
    public void TheOnlyUnnumberedVersesAreTheTwoTranspositions()
    {
        var gaps = Project.Value.Books
            .SelectMany(book => book.Chapters.Select(chapter => (book, chapter)))
            .Select(entry => (entry.book, entry.chapter, missing: Enumerable
                .Range(1, entry.chapter.Verses.Max(v => v.Number))
                .Except(entry.chapter.Verses.Select(v => v.Number))
                .ToList()))
            .Where(entry => entry.missing.Count > 0)
            .ToList();

        gaps.Select(entry => (entry.book.Name, entry.chapter.Number)).Should()
            .Equal(("Exodus", 30), ("Deuteronomy", 34));
        gaps[0].missing.Should().Equal(Enumerable.Range(1, 10));
        gaps[1].missing.Should().Equal(2, 3);
    }

    /// <summary>
    /// The verses are kept in the order the manuscript writes them, and in one chapter that is not
    /// the order they are numbered in: Exodus 29:21 stands after 29:28. It is the only place in the
    /// Pentateuch where the two disagree, and it is a property of this witness rather than an error
    /// in the reading — so it is asserted rather than sorted away.
    /// </summary>
    [Fact]
    public void OneChapterWritesItsVersesOutOfNumericalOrder()
    {
        var chapters = Project.Value.Books
            .SelectMany(book => book.Chapters.Select(chapter => (book, chapter)))
            .Where(entry => !entry.chapter.Verses.Select(v => v.Number).SequenceEqual(
                entry.chapter.Verses.Select(v => v.Number).Order()))
            .ToList();

        var only = chapters.Should().ContainSingle().Which;
        only.book.Name.Should().Be("Exodus");
        only.chapter.Number.Should().Be(29);
        only.chapter.Verses.SkipWhile(v => v.Number != 28).Skip(1).First().Number.Should().Be(21);
    }

    /// <summary>
    /// The first verse, morpheme for morpheme. It is the same segmentation BHSA uses — the
    /// preposition, the article and the noun are separate words in both — which is the whole reason
    /// the two texts can be compared word for word rather than only verse by verse.
    /// </summary>
    [Fact]
    public void TheFirstVerseIsReadAsMorphemes()
    {
        var verse = Project.Value.Books[0].Chapters[0].Verses[0];

        verse.Words.Select(w => w.Consonants).Should().Equal(
            "ב", "ראשׁית", "ברא", "אלהים", "את", "ה", "שׁמים", "ו", "את", "ה", "ארץ");
    }

    /// <summary>
    /// Genesis 1:11, where the Samaritan writes <em>and a tree bearing fruit</em> and the Masoretic
    /// writes <em>a tree bearing fruit</em>. The first plus in the first chapter of the first book,
    /// and the shape of difference this text was loaded for.
    /// </summary>
    [Fact]
    public void TheSamaritanPlusInTheFirstChapterIsThere()
    {
        var verse = Project.Value.Books[0].Chapters[0].Verses
            .Single(v => v.Number == 11);

        var words = verse.Words.Select(w => w.Consonants).ToList();
        var tree = words.IndexOf("עץ");

        tree.Should().BeGreaterThan(0);
        words[tree - 1].Should().Be("ו");
    }

    /// <summary>
    /// No presentation form survives the read. They render as ordinary Hebrew and are not ordinary
    /// Hebrew: while they were passing through, 12,507 words could not match their Masoretic
    /// counterparts and the consonantal folding threw them away as if they were punctuation.
    /// </summary>
    [Fact]
    public void NoWordKeepsAPresentationForm()
    {
        var offending = AllVerses()
            .SelectMany(v => v.Words)
            .Where(w => w.Consonants.Contains(ShinWithShinDot) || w.Consonants.Contains(ShinWithSinDot))
            .ToList();

        offending.Should().BeEmpty();
    }

    /// <summary>
    /// The shin is still there after the rewriting. A mapping that dropped the letter instead of
    /// respelling it would satisfy the check above and lose a fifth of the alphabet.
    /// </summary>
    [Fact]
    public void TheShinSurvivesAsAnOrdinaryLetter() =>
        AllVerses().SelectMany(v => v.Words).Count(w => w.Consonants.Contains('ש'))
            .Should().BeGreaterThan(10_000);

    /// <summary>
    /// The dataset states which words it parsed from the Masoretic text rather than from this one.
    /// It is the difference between annotation that is evidence about this witness and annotation
    /// that is evidence about the other, so it is carried rather than flattened away.
    /// </summary>
    [Fact]
    public void TheParsingSaysWhereItCameFrom()
    {
        var words = AllVerses().SelectMany(v => v.Words).ToList();

        words.Count(w => w.ParsedFromMasoretic).Should().BeGreaterThan(0);
        words.Count(w => !w.ParsedFromMasoretic).Should().BeGreaterThan(0);
    }

    /// <summary>
    /// A verse read back by concatenating its words is the verse the source writes. The words are
    /// morphemes and the trailer is what stands between them, so a lost trailer is a text that
    /// reads as one long word and a doubled one is a text nobody can search.
    /// </summary>
    [Fact]
    public void AVerseRebuildsFromItsWords()
    {
        var verse = Project.Value.Books[0].Chapters[0].Verses[0];
        var rebuilt = string.Concat(verse.Words.Select(w => w.Consonants + w.Trailer));

        rebuilt.Should().Be("בראשׁית ברא אלהים את השׁמים ואת הארץ ");
    }

    /// <summary>
    /// What the text says about itself before a single word of it is written. A text with no
    /// licence and no redistribution decision is refused by the loader; this is the same refusal,
    /// asked of the definition rather than of a database.
    /// </summary>
    [Fact]
    public void TheTextSaysWhatItIsHeldUnder()
    {
        var definition = SamaritanTextSource.Definition;

        definition.Invoking(d => d.Validate()).Should().NotThrow();
        definition.Licence.Should().Be("CC-BY-NC-4.0");
        definition.TextualFamily.Should().Be("Samaritan");
        definition.RightsNote.Should().Contain("ShareAlike");
        definition.Citation.Should().Contain("zenodo");
    }

    /// <summary>
    /// The text as the corpus loader will take it: canonical ordinals one to five, the words in the
    /// order the source writes them, and the annotation in the keys the rest of the corpus uses.
    /// </summary>
    [Fact]
    public void TheSourceIsBuiltIntoTheFirstFiveBooks()
    {
        var source = SamaritanTextSource.Build(Project.Value);

        source.Books.Select(b => b.CanonicalOrdinal).Should().Equal(1, 2, 3, 4, 5);
        source.Books.SelectMany(b => b.Chapters).SelectMany(c => c.Verses).Sum(v => v.Words.Count)
            .Should().Be(Words);
        source.Definition.Edition.Should().Contain(Project.Value.Version);
    }

    /// <summary>
    /// The annotation answers in the vocabulary BHSA answers in, because a reader asking one
    /// question of two Hebrew texts should not have to know which of them said "Hebrew" and which
    /// said "hbo".
    /// </summary>
    [Fact]
    public void TheAnnotationSpeaksTheCorpusVocabulary()
    {
        var source = SamaritanTextSource.Build(Project.Value);
        var first = source.Books[0].Chapters[0].Verses[0].Words[0];

        var features = JsonSerializer.Deserialize<Dictionary<string, string>>(first.Morphology!)!;

        features["language"].Should().Be("hbo");
        features.Should().ContainKey("pos");
    }
}
