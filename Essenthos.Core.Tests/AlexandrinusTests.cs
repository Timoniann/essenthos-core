using Essenthos.Core.Alexandrinus;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Loading;
using Essenthos.Core.Swete;
using FluentAssertions;
using Xunit;

namespace Essenthos.Core.Tests;

/// <summary>Codex Alexandrinus as one witness: INTF's New Testament and the printings of its Old.</summary>
[Trait(TestCategory.Name, TestCategory.Corpus)]
public class AlexandrinusTests
{
    private const int Genesis = 1;

    private const int Isaiah = 23;

    private const int Matthew = 40;

    private const int John = 43;

    private const int SecondCorinthians = 47;

    private const int FirstTimothy = 54;

    private const int Hebrews = 58;

    private const int ThirdJohn = 64;

    private static readonly Lazy<TextSource> Source =
        new(() => AlexandrinusTextSource.Read(TestResources.Folder(".")));

    private static string Folder => TestResources.Folder(AlexandrinusTextSource.Folder);

    private static BookDraft Book(int ordinal) => Source.Value.Books.Single(book => book.CanonicalOrdinal == ordinal);

    private static VerseDraft? Verse(int ordinal, int chapter, int verse) =>
        Book(ordinal).Chapters.SingleOrDefault(one => one.Number == chapter)?.Verses.SingleOrDefault(one => one.Number == verse);

    private static string Text(int ordinal, int chapter, int verse) =>
        string.Concat(Verse(ordinal, chapter, verse)!.Words.Select(word => word.Surface + word.Trailer)).Trim();

    private static IEnumerable<(int Book, int Chapter, int Verse)> Addresses(IEnumerable<BookDraft> books) =>
        books.SelectMany(book => book.Chapters.SelectMany(chapter =>
            chapter.Verses.Select(verse => (book.CanonicalOrdinal, chapter.Number, verse.Number))));

    [Fact]
    public void ItIsAManuscriptWithEveryPartCredited()
    {
        var definition = AlexandrinusTextSource.Definition;

        definition.Validate();
        definition.Slug.Should().Be("ALEX");
        definition.Kind.Should().Be(TextKind.ManuscriptTradition);
        definition.Redistribution.Should().Be(Redistribution.ShareAlike);
        definition.PartSources.Select(part => part.Licence).Should().Equal("CC-BY-4.0", "CC-BY-SA-4.0", "CC-BY-SA-4.0");
        TextCorpus.Slugs.Should().Contain("ALEX");
    }

    /// <summary>
    /// CNTR transcribed the same manuscript independently and keys each verse cleanly, so its keys are
    /// the answer to which verses the codex still has — including the ones INTF labels in another form.
    /// </summary>
    [Fact]
    public void TheNewTestamentHasExactlyTheVersesAnIndependentTranscriptionHas()
    {
        var independent = File.ReadLines(Path.Combine(Folder, "cntr-02.txt"))
            .Where(line => line.Length > 8 && line[9..].Trim() != NotInTheManuscript)
            .Select(line => (int.Parse(line[..2]), int.Parse(line[2..5]), int.Parse(line[5..8])))
            .ToHashSet();

        var read = Addresses(Source.Value.Books.Where(book => book.CanonicalOrdinal is >= Matthew and <= 66)).ToList();

        read.Should().HaveCount(6787).And.OnlyHaveUniqueItems();
        independent.Except(read).Order().Should().Equal(FirstHandLacks);
        read.ToHashSet().IsSubsetOf(independent).Should().BeTrue();
        Source.Value.Books.Count(book => book.CanonicalOrdinal is >= Matthew and <= 66).Should().Be(27);
    }

    /// <summary>
    /// The verses CNTR has words for and the first hand does not: John 5:12 and the opening of 8:52,
    /// which only a corrector wrote. The eighteen verses the manuscript does not have at all, which
    /// CNTR keys with a dash, are absent from both.
    /// </summary>
    private static readonly (int, int, int)[] FirstHandLacks = [(John, 5, 12), (John, 8, 52)];

    private const string NotInTheManuscript = "-";

    [Fact]
    public void WhatTheCodexHasLostIsAbsentAndNotFilled()
    {
        Verse(Matthew, 25, 5).Should().BeNull();
        Text(Matthew, 25, 6).Should().StartWith("εξερχεσθε");
        Verse(John, 7, 1).Should().BeNull();
        Verse(SecondCorinthians, 5, 1).Should().BeNull();
        Book(Matthew).Chapters.Min(chapter => chapter.Number).Should().Be(25);
    }

    [Fact]
    public void VersesLabelledInAnotherFormStandInTheirOwnBooks()
    {
        Text(ThirdJohn, 1, 1).Should().StartWith("πρεσβυτερος γαιω");
        Text(FirstTimothy, 1, 1).Should().StartWith("παυλος αποστολος");
        Verse(Hebrews, 13, 25).Should().NotBeNull();
        Book(Hebrews).Chapters.Single(chapter => chapter.Number == 1).Verses.Should().HaveCount(14);
    }

    [Fact]
    public void TheFirstHandIsTheTextAndTheCorrectionANote()
    {
        var verse = Verse(Matthew, 25, 16)!;

        verse.Words.Select(word => word.Surface).Should().Contain("εποιησεν").And.NotContain("εκερδησεν");
        verse.Notes.Select(note => note.Content).Should().Contain("εποιησεν] εκερδησεν corr.");
        Text(Matthew, 25, 11).Should().Contain("κε· κε");
    }

    [Fact]
    public void GenesisIsAlexandrinusOnlyUpToWhereVaticanusBegins()
    {
        var last = Book(Genesis).Chapters[^1];
        last.Number.Should().Be(46);
        last.Verses[^1].Number.Should().Be(28);
        last.Verses[^1].Words[^1].Surface.Should().Be("Ἡρώων");
    }

    [Fact]
    public void WhatSweteSuppliedForTheCodexIsLeftOut()
    {
        Verse(Genesis, 14, 15).Should().BeNull();
        Verse(Genesis, 15, 3).Should().BeNull();
        Verse(Genesis, 16, 7).Should().BeNull();
        Verse(Genesis, 15, 17).Should().BeNull();
        Text(Genesis, 15, 20).Should().StartWith("καὶ τοὺς Χετταίους");
        Text(Genesis, 15, 6).Should().StartWith("καὶ ἐπίστευσεν");
        Text(Genesis, 14, 14).Should().EndWith("κατεδίωξεν");
        Text(Genesis, 14, 17).Should().StartWith("βασιλέων");

        // Bracketed as lost in the codex: 1:21 keeps κήτη and loses τὰ ὕδατα κατὰ γένη αὐτῶν.
        var words = Verse(Genesis, 1, 21)!.Words.Select(word => word.Surface).ToList();
        words.Should().Contain("κήτη").And.NotContain("γένη");
        Text(Genesis, 1, 1).Should().Be("ΕΝ ΑΡΧΗ ἐποίησεν ὁ θεὸς τὸν οὐρανὸν καὶ τὴν γῆν.");
    }

    /// <summary>
    /// Maccabees is read from First1KGreek's encoding, which is where the files Swete is loaded from
    /// were converted from, so both must address the same verses.
    /// </summary>
    [Theory]
    [InlineData(23, "23.Machabaeorum_i")]
    [InlineData(24, "24.Machabaeorum_ii")]
    [InlineData(25, "25.Machabaeorum_iii")]
    [InlineData(26, "26.Machabaeorum_iv")]
    public void MaccabeesAddressesTheVersesSweteDoes(int work, string file)
    {
        var fromTei = SweteAlexandrinus.Read(Folder, work);
        var fromSwete = SweteReader.Read(File.ReadLines(Path.Combine(TestResources.SweteFolder, file + ".txt"))).Chapters;

        fromTei.SelectMany(chapter => chapter.Verses.Select(verse => (chapter.Number, verse.Number, verse.Label)))
            .Should().Equal(fromSwete.SelectMany(chapter => chapter.Verses.Select(verse => (chapter.Number, verse.Number, verse.Label))));
        fromTei.Sum(chapter => chapter.Verses.Sum(verse => verse.Words.Count))
            .Should().BeCloseTo(fromSwete.Sum(chapter => chapter.Verses.Sum(verse => verse.Words.Count)), 50);
    }

    [Fact]
    public void IsaiahIsOttleysAndTheBooksStandInTheCodexsOrder()
    {
        Book(Isaiah).Chapters.Sum(chapter => chapter.Verses.Sum(verse => verse.Words.Count)).Should().Be(27162);
        Source.Value.Books.Select(book => book.Position).Should().BeInAscendingOrder().And.OnlyHaveUniqueItems();
        Source.Value.Books.Take(6).Select(book => book.CanonicalOrdinal).Should().Equal(1, 23, 73, 74, 80, 81);
    }

    [Fact]
    public void ALabelNeitherFormNorRepairIsRefused()
    {
        var act = () => NtvmrTranscription.Address("Rom.1.1", NtvmrTranscription.Alexandrinus);

        act.Should().Throw<InvalidOperationException>().WithMessage("*CNTR*");
        NtvmrTranscription.Address("B02KInscriptioV0", NtvmrTranscription.Alexandrinus).Should().BeNull();
        NtvmrTranscription.Address("Heb.subscriptio", NtvmrTranscription.Alexandrinus).Should().BeNull();
    }
}
