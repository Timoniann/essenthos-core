using Essenthos.Core.ClearBible;
using Essenthos.Core.Corpus;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Loading;
using Essenthos.Core.Loading.Frame;
using Essenthos.Core.Loading.Links;
using Essenthos.Core.Usfm;
using FluentAssertions;
using Xunit;

namespace Essenthos.Core.Tests;

/// <summary>
/// What the Segond, the Van Dyck and the Indian Revised Version brought into the reader that no
/// earlier eBible text had: publishers' headings over the text, cross references printed in the
/// running text, a footnote broken across two lines, and scripts whose words end in a vowel sign.
/// </summary>
public class UsfmWorldTextTests
{
    private const string Headed =
        """
        \id GEN
        \c 1
        \s1 Création du monde
        \r V. 1: cf. Né 9:6.
        \p
        \v 1 Au commencement, Dieu créa les cieux et la terre.
        """;

    [Fact]
    public void AnEditorsHeadingIsDroppedWhereTheEditionSaysSo()
    {
        var words = UsfmReader.Read(Headed, editorialHeadings: true).Chapters[0].Verses[0].Words;

        words.Select(word => word.Surface).Should().Equal("Au", "commencement", "Dieu", "créa", "les", "cieux", "et", "la", "terre");
    }

    /// <summary>The older files write the text's own subscription there, and it stays.</summary>
    [Fact]
    public void AHeadingIsTheTextsOwnWhereTheEditionSaysNothing() =>
        UsfmReader.Read(Headed).Chapters[0].Verses[0].Words.Select(word => word.Surface)
            .Should().StartWith(["Création", "du", "monde", "Au"]);

    [Fact]
    public void ABracketedCrossReferenceIsANoteAndNotWords()
    {
        var verse = UsfmReader.Read(
            """
            \id GEN
            \c 1
            \p
            \v 1 आदि में परमेश्वर ने आकाश और पृथ्वी की सृष्टि की। \bdit (इब्रा. 1:10, इब्रा. 11:3) \bdit*
            """).Chapters[0].Verses[0];

        verse.Words.Select(word => word.Surface).Should().Equal("आदि", "में", "परमेश्वर", "ने", "आकाश", "और", "पृथ्वी", "की", "सृष्टि", "की");
        verse.Notes.Should().ContainSingle().Which.Should().Be(new UsfmNote(UsfmNoteKind.CrossReference, "इब्रा. 1:10, इब्रा. 11:3", 10));
    }

    /// <summary>
    /// Devanagari writes a vowel after a consonant as a mark, and Arabic its case endings: the word
    /// ends in it, and it is not the punctuation after the word.
    /// </summary>
    [Theory]
    [InlineData("\\v 1 दाऊद का भजन।", new[] { "दाऊद", "का", "भजन" })]
    [InlineData("\\v 1 مَزْمُورٌ لِدَاوُدَ.", new[] { "مَزْمُورٌ", "لِدَاوُدَ" })]
    public void AWordKeepsTheVowelItEndsIn(string line, string[] words) =>
        UsfmReader.Read($"\\id PSA\n\\c 23\n{line}").Chapters[0].Verses[0].Words.Select(word => word.Surface)
            .Should().Equal(words);

    /// <summary>
    /// A psalm's title stands before its first verse and is given to it; the verse says so, which is
    /// what lets the frame place it beside the Hebrew's title verse as well as its own.
    /// </summary>
    [Fact]
    public void AVerseKnowsItOpensWithTheTitle()
    {
        var verses = UsfmReader.Read(
            """
            \id PSA
            \c 3
            \d مَزْمُورٌ لِدَاوُدَ
            \q1
            \v 1 يَا رَبُّ
            \v 2 كَثِيرُونَ
            """).Chapters[0].Verses;

        verses.Select(verse => verse.MarksASuperscription).Should().Equal(true, false);
        verses[0].Words.Select(word => word.Surface).Should().Equal("مَزْمُورٌ", "لِدَاوُدَ", "يَا", "رَبُّ");
    }

    [Fact]
    public void AFootnoteBrokenAcrossTwoLinesIsOneNote()
    {
        var verse = UsfmReader.Read(
            """
            \id NEH
            \c 7
            \p
            \v 4 नगर तो लम्बा चौड़ा था, \it परन्तु उसमें लोग थोड़े थे\f + \fr 7:4 \fq परन्तु: \ft जो इस्राएली
            \fp \ft के साथ लौटे थे \f*\it*, और घर नहीं बने थे।
            """).Chapters[0].Verses[0];

        verse.Words.Select(word => word.Surface).Should().EndWith(["थे", "और", "घर", "नहीं", "बने", "थे"]);
        verse.Notes.Should().ContainSingle().Which.Content.Should().EndWith("के साथ लौटे थे");
    }
}

/// <summary>Read once; three complete Bibles in USFM is a few seconds.</summary>
public sealed class WorldTexts
{
    internal TextSource Segond { get; } = EbibleTextSource.Read(TestResources.EbibleFolder("Segond1910"));

    internal TextSource VanDyck { get; } = EbibleTextSource.Read(TestResources.EbibleFolder("VanDyck1865"));

    internal TextSource IrvHindi { get; } = EbibleTextSource.Read(TestResources.EbibleFolder("IrvHindi2019"));

    internal static IEnumerable<(int Book, int Chapter, VerseDraft Verse)> Verses(TextSource source) =>
        from book in source.Books
        from chapter in book.Chapters
        from verse in chapter.Verses
        select (book.CanonicalOrdinal, chapter.Number, verse);

    internal static string Text(TextSource source, int book, int chapter, int verse) => string.Concat(
        Verses(source).Single(v => (v.Book, v.Chapter, v.Verse.Number) == (book, chapter, verse))
            .Verse.Words.Select(word => word.Surface + word.Trailer)).Trim();
}

public class EbibleWorldTextsTests(WorldTexts texts) : IClassFixture<WorldTexts>
{
    /// <summary>
    /// The catalogue's counts, checked: the Segond numbers its Old Testament as the Hebrew does and
    /// so holds 66 more verses than the English count, and the other two print 3 John 1:15 and one
    /// more New Testament verse apart.
    /// </summary>
    [Theory]
    [InlineData(EbibleTextSource.Segond, 23211 + 7959, 722591)]
    [InlineData(EbibleTextSource.VanDyck, 23145 + 7959, 434591)]
    [InlineData(EbibleTextSource.IrvHindi, 23145 + 7959, 772320)]
    public void EachIsAWholeBible(string slug, int verses, int words)
    {
        var all = WorldTexts.Verses(Source(slug)).ToList();

        Source(slug).Books.Should().HaveCount(66);
        all.Should().HaveCount(verses);
        all.Sum(verse => verse.Verse.Words.Count).Should().Be(words);
    }

    [Fact]
    public void TheArabicReadsRightToLeft()
    {
        texts.VanDyck.Definition.Direction.Should().Be(TextDirection.RightToLeft);
        texts.Segond.Definition.Direction.Should().Be(TextDirection.LeftToRight);
    }

    /// <summary>
    /// The Hindi is the one text here that is not public domain, and the row says what it is, so a
    /// licence field is never a memory of what somebody assumed.
    /// </summary>
    [Fact]
    public void TheHindiCarriesItsShareAlikeLicence()
    {
        texts.IrvHindi.Definition.Licence.Should().Be("CC-BY-SA-4.0");
        texts.IrvHindi.Definition.Redistribution.Should().Be(Redistribution.ShareAlike);
        texts.IrvHindi.Definition.RightsHolder.Should().Contain("Bridge Connectivity Solutions");
    }

    /// <summary>
    /// eBible's Segond of 2026 carries 705,728 Strong tags and names nobody for them; none is loaded.
    /// It is a decision, so it is pinned: a pass that read them back off the file would undo it.
    /// </summary>
    [Fact]
    public void TheSegondsTaggingIsNotLoaded() =>
        WorldTexts.Verses(texts.Segond).SelectMany(verse => verse.Verse.Words)
            .Should().OnlyContain(word => word.StrongNumber == null);

    [Fact]
    public void TheHeadingsAndIntroductionsAreNotTheText()
    {
        WorldTexts.Text(texts.Segond, 1, 1, 1).Should().Be("Au commencement, Dieu créa les cieux et la terre.");
        WorldTexts.Text(texts.VanDyck, 1, 1, 1).Should().StartWith("فِي ٱلْبَدْءِ خَلَقَ ٱللهُ");
        WorldTexts.Text(texts.IrvHindi, 1, 1, 1).Should().Be("आदि में परमेश्वर ने आकाश और पृथ्वी की सृष्टि की।");
    }

    /// <summary>The Hindi's bracketed cross references, all 2,519, become notes.</summary>
    [Fact]
    public void TheHindiCrossReferencesAreNotes() =>
        WorldTexts.Verses(texts.IrvHindi).Sum(verse => verse.Verse.Notes.Count(note => note.Kind == VerseNoteKind.CrossReference))
            .Should().Be(2519);

    /// <summary>
    /// Every verse of the three is placed where the Hebrew and the Greek words Clear Bible's
    /// alignments pair it with are placed: the Segond as the Hebrew numbers it, with its own division
    /// of the end of Job and of Ecclesiastes 11-12, and the other two as the English does.
    /// </summary>
    [Theory]
    [InlineData(EbibleTextSource.Segond, 18, 39, 1, 18, 38, 39)]
    [InlineData(EbibleTextSource.Segond, 18, 39, 34, 18, 40, 1)]
    [InlineData(EbibleTextSource.Segond, 18, 40, 20, 18, 41, 1)]
    [InlineData(EbibleTextSource.Segond, 18, 40, 28, 18, 41, 9)]
    [InlineData(EbibleTextSource.Segond, 18, 41, 25, 18, 41, 34)]
    [InlineData(EbibleTextSource.Segond, 21, 12, 1, 21, 11, 9)]
    [InlineData(EbibleTextSource.Segond, 21, 12, 16, 21, 12, 14)]
    [InlineData(EbibleTextSource.Segond, 2, 7, 26, 2, 8, 1)]
    [InlineData(EbibleTextSource.VanDyck, 2, 8, 1, 2, 8, 1)]
    [InlineData(EbibleTextSource.IrvHindi, 18, 41, 1, 18, 41, 1)]
    public void EachVerseIsPlacedBesideTheWordsItRenders(
        string slug, int book, int chapter, int verse, int canonicalBook, int canonicalChapter, int canonicalVerse)
    {
        var source = Source(slug);
        var frame = TvtmsReader.Read(TestResources.Tvtms).Frame(
            source.Definition.Versification,
            EditionShape.Of(WorldTexts.Verses(source).Select(v =>
                (v.Book, v.Chapter, v.Verse.Number, v.Verse.Label, v.Verse.Words.Sum(word => word.Surface.Length)))));

        frame.Resolve(book, chapter, verse)[0]
            .Should().Be(new CanonicalReference(canonicalBook, canonicalChapter, canonicalVerse));
    }

    private TextSource Source(string slug) => slug switch
    {
        EbibleTextSource.Segond => texts.Segond,
        EbibleTextSource.VanDyck => texts.VanDyck,
        EbibleTextSource.IrvHindi => texts.IrvHindi,
        _ => throw new ArgumentOutOfRangeException(nameof(slug), slug, "That is not one of the three."),
    };
}

/// <summary>
/// The Segond's records count French words over a division of the text the release's own token file
/// does not follow, so the loader counts them itself; and the Arabic is searched without its vowels.
/// </summary>
public class ClearBibleRetokenisedTests
{
    [Theory]
    [InlineData("l’un: ", new[] { "l’", "un", ":" })]
    [InlineData("Jésus-Christ, ", new[] { "Jésus-Christ", "," })]
    [InlineData("aujourd’hui, ", new[] { "aujourd’hui", "," })]
    [InlineData("«Je ", new[] { "«", "Je" })]
    [InlineData("qu’il ", new[] { "qu’", "il" })]
    public void AWordIsCountedTheWayTheRecordsCountIt(string written, string[] pieces) =>
        ClearBibleLinkLoader.Pieces(written).Select(piece => piece.Text).Should().Equal(pieces);

    /// <summary>
    /// Matthew 8:9 as the records count it: <em>τούτῳ</em> on <em>un</em>, the twenty-fourth piece,
    /// which is a colon where the text keeps <em>l’un</em> whole.
    /// </summary>
    [Fact]
    public void AVerseWhereARecordFallsOnPunctuationIsRefusedWhole()
    {
        var tokens = new[]
        {
            new ClearBibleToken("40008009001", "dis", false, null),
            new ClearBibleToken("40008009002", ":", true, null),
            new ClearBibleToken("40008010001", "Jésus", false, null),
        };
        var records = new[]
        {
            new ClearBibleRecord(["n40008009001"], ["40008009001"]),
            new ClearBibleRecord(["n40008009002"], ["40008009002"]),
            new ClearBibleRecord(["n40008010001"], ["40008010001"]),
            new ClearBibleRecord(["n40008010002"], ["40008010004"]),
        };

        ClearBibleLinkLoader.Misnumbered(records, tokens).Should().BeEquivalentTo([40008009, 40008010]);
    }

    [Theory]
    [InlineData("فِي", "في")]
    [InlineData("ٱلْبَدْءِ", "البدء")]
    [InlineData("وَٱلْأَرْضَ", "والأرض")]
    public void ArabicIsSearchedWithoutItsVowels(string surface, string folded) =>
        WordFolding.Fold(surface, "arb").Should().Be(folded);
}
