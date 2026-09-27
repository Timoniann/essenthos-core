using Essenthos.Core.Loading;
using Essenthos.Core.Swete;
using FluentAssertions;
using Xunit;

namespace Essenthos.Core.Tests;

/// <summary>
/// The one thing this edition's files do not say plainly: which words have no number of their own.
///
/// The converter that made them carries a current chapter and a current verse and changes neither
/// until a numbered division opens, so a psalm's title — printed inside the psalm and before its
/// first verse — comes out under the last verse number of the psalm before. Eighty psalms, the
/// Psalms of Solomon, the prologue of Lamentations and the heading of Obadiah all arrive that way,
/// and left alone every one of them would claim an address that belongs to another verse.
/// </summary>
[Trait(TestCategory.Name, TestCategory.Corpus)]
public class SweteUnnumberedTests
{
    [Fact]
    public void MaterialPrintedBeforeAChapterFirstVerseOpensThatVerse()
    {
        var book = SweteReader.Read([
            "27.13.7 ἔλεος",
            "27.14.7 Ψαλμὸς",
            "27.14.1 Κύριε",
            "27.14.2 πορευόμενος",
        ]);

        var psalm = book.Chapters.Single(chapter => chapter.Number == 14);
        psalm.Verses.Select(verse => verse.Number).Should().Equal(1, 2);
        psalm.Verses[0]!.Words.Select(word => word.Surface).Should().Equal("Ψαλμὸς", "Κύριε");
    }

    /// <summary>
    /// Zero is what the converter writes where no verse has opened anywhere in the book yet, which
    /// is the same case seen from the top of a book rather than the top of a chapter.
    /// </summary>
    [Fact]
    public void SoDoesMaterialPrintedBeforeTheFirstVerseOfABook()
    {
        var book = SweteReader.Read(["40.1.0 ΡΑΣΙΣ", "40.1.1 Τάδε", "40.1.2 ἀκοὴν"]);

        book.Chapters[0]!.Verses[0]!.Words.Select(word => word.Surface).Should().Equal("ΡΑΣΙΣ", "Τάδε");
    }

    /// <summary>
    /// The other half of the same repair. Once the title has been taken out of the middle of a
    /// psalm, the address it was standing under is free — and the psalm's own verse of that number
    /// is further down the file, so the two blocks have to become one verse rather than two rows
    /// claiming one address.
    /// </summary>
    [Fact]
    public void TwoBlocksAtOneAddressAreOneVerse()
    {
        var book = SweteReader.Read([
            "27.15.5 Στηλογραφία",
            "27.15.1 φύλαξόν",
            "27.15.5 κύριος",
            "27.15.6 σχοινία",
        ]);

        var psalm = book.Chapters[0]!;
        psalm.Verses.Select(verse => verse.Number).Should().Equal(1, 5, 6);
        psalm.Verses[0]!.Words.Select(word => word.Surface).Should().Equal("Στηλογραφία", "φύλαξόν");
        psalm.Verses[1]!.Words.Select(word => word.Surface).Should().Equal("κύριος");
    }

    /// <summary>
    /// A book the edition prints with no chapter division at all — the Epistle of Jeremiah — comes
    /// out as chapter zero, and a chapter zero standing beside numbered chapters is something else
    /// entirely, so the two cannot be read the same way.
    /// </summary>
    [Fact]
    public void ABookWithNoChapterDivisionIsOneChapter()
    {
        var book = SweteReader.Read(["52.0.0 ΑΝΤΙΓΡΑΦΟΝ", "52.0.1 διὰ", "52.0.2 καὶ"]);

        book.Chapters.Should().ContainSingle().Which.Number.Should().Be(1);
        book.Chapters[0]!.Verses.Select(verse => verse.Number).Should().Equal(1, 2);
    }

    /// <summary>
    /// Sirach's preface, which is the other chapter zero: the translator writing about his
    /// grandfather's book, printed before chapter 1 and numbered as neither a chapter nor a verse.
    /// The corpus has no address for a page outside the book's numbering and running it into 1:1
    /// would say the edition prints it there, so it is left out.
    /// </summary>
    [Fact]
    public void AnUnnumberedPrefaceBesideNumberedChaptersIsNotRead()
    {
        var book = SweteReader.Read(["34.0.0 προλοΓοϲ", "34.1.1 ΠΑΣA", "34.1.2 ἄμμον"]);

        book.Chapters.Should().ContainSingle().Which.Number.Should().Be(1);
        book.Chapters[0]!.Verses[0]!.Words.Select(word => word.Surface).Should().Equal("ΠΑΣA");
    }

    [Fact]
    public void ALetterAfterTheNumberIsALabelRatherThanAnotherVerse()
    {
        var book = SweteReader.Read(["19.3.1 ἐν", "19.3.13 καὶ", "19.3.1a τάδε", "19.3.14 τὸ"]);

        book.Chapters[0]!.Verses.Select(verse => (verse.Number, verse.Label))
            .Should().Equal((1, ""), (13, ""), (1, "a"), (14, ""));
    }

    /// <summary>
    /// Punctuation standing on its own is a mark on the sentence and not a word: this edition sets
    /// the Greek question mark off with a space, which would otherwise put 724 words into the
    /// corpus that have no letters in them.
    /// </summary>
    [Fact]
    public void PunctuationStandingAloneBelongsToTheWordBeforeIt()
    {
        var words = SweteReader.Read(["27.14.1 σου", "27.14.1 ;"]).Chapters[0]!.Verses[0]!.Words;

        words.Should().ContainSingle();
        words[0]!.Surface.Should().Be("σου");
        words[0]!.Trailer.Should().Be(" ; ");
    }

    [Fact]
    public void AFileHoldingTwoBooksIsRefused()
    {
        var act = () => SweteReader.Read(["27.1.1 μακάριος", "28.1.1 ᾠδὴ"]);

        act.Should().Throw<InvalidOperationException>().WithMessage("*27*28*");
    }
}

/// <summary>Read once; fifty-two books of half a million tokens is a few seconds.</summary>
public sealed class Swete
{
    internal TextSource Source { get; } = SweteTextSource.Read(TestResources.SweteFolder);

    internal BookDraft Book(int canonical) => Source.Books.Single(book => book.CanonicalOrdinal == canonical);

    internal VerseDraft Verse(int canonical, int chapter, int verse) =>
        Book(canonical).Chapters.Single(c => c.Number == chapter).Verses.First(v => v.Number == verse);

    internal static string Text(VerseDraft verse) =>
        string.Concat(verse.Words.Select(word => word.Surface + word.Trailer)).TrimEnd();
}

/// <summary>
/// Swete as the corpus holds it. The counts are the reader's own and no catalogue states them, so
/// they are what would move if the tokeniser or the repair of the unnumbered material changed its
/// mind — and a text whose word division moved silently is a text whose every future link is built
/// against something else.
/// </summary>
[Trait(TestCategory.Name, TestCategory.Corpus)]
public class SweteCorpusTests(Swete swete) : IClassFixture<Swete>
{
    [Fact]
    public void TheWholeEditionIsRead()
    {
        swete.Source.Books.Should().HaveCount(52);
        swete.Source.Books.Sum(book => book.Chapters.Count).Should().Be(1107);
        swete.Source.Books.Sum(book => book.Chapters.Sum(chapter => chapter.Verses.Count))
            .Should().Be(28533);
        swete.Source.Books
            .SelectMany(book => book.Chapters)
            .SelectMany(chapter => chapter.Verses)
            .Sum(verse => verse.Words.Count)
            .Should().Be(571934);
    }

    /// <summary>
    /// Its order is Swete's, which is the Greek one and not the canon's: the Twelve run Hosea,
    /// Amos, Micah, Joel, and the histories carry 1 Esdras before Esther. So position and ordinal
    /// disagree from the fifteenth book onwards, which is the case the model has a column for.
    /// </summary>
    [Fact]
    public void EveryBookKeepsTheEditionOwnOrderAndTheSharedOrdinal()
    {
        swete.Source.Books.Select(book => book.Position).Should().Equal(Enumerable.Range(1, 52));
        swete.Source.Books.Select(book => book.CanonicalOrdinal).Should().OnlyHaveUniqueItems();
        swete.Book(30).Position.Should().Be(33, "Amos stands second among the Twelve here");
        swete.Book(29).Position.Should().Be(35, "and Joel fourth");
    }

    /// <summary>
    /// No verse of a book claims an address another verse of it already has. The unique index on
    /// the verse table would refuse the load, and the repair of the unnumbered material is exactly
    /// what stands between this edition and 107 collisions.
    /// </summary>
    [Fact]
    public void NoAddressIsClaimedTwice()
    {
        var claimed = swete.Source.Books
            .Where(book => book.Chapters
                .SelectMany(chapter => chapter.Verses
                    .Select(verse => (chapter.Number, verse.Number, verse.Label)))
                .Distinct().Count() != book.Chapters.Sum(chapter => chapter.Verses.Count))
            .Select(book => book.Name);

        claimed.Should().BeEmpty();
    }

    /// <summary>
    /// The check that the repair is right rather than merely consistent. Brenton numbers a psalm's
    /// title as verse 1 and this edition does not number it at all, so after the title is put back
    /// at the head of the psalm the two editions hold the same words at the same address — which no
    /// count could have shown and which is the whole claim.
    /// </summary>
    [Fact]
    public void APsalmTitleStandsAtTheHeadOfItsOwnPsalm()
    {
        var psalm = swete.Verse(19, 14, 1);

        psalm.Words.Select(word => word.Surface).Take(4)
            .Should().Equal("Ψαλμὸς", "τῷ", "Δαυείδ", "Κύριε");
    }

    /// <summary>Psalm 151 is the last chapter of the Psalms here, as it is in Brenton.</summary>
    [Fact]
    public void ThePsalterRunsToAHundredAndFiftyOne() =>
        swete.Book(19).Chapters.Should().HaveCount(151);

    /// <summary>
    /// Esdras B is Ezra and Nehemiah under one heading, twenty-three chapters, and it is split on
    /// load for the reason Brenton's is: kept whole, the last thirteen chapters would have no
    /// address in the shared frame and Nehemiah would be missing from a witness that holds it.
    /// </summary>
    [Fact]
    public void EsdrasBIsEzraAndNehemiah()
    {
        swete.Book(15).Chapters.Should().HaveCount(10);
        swete.Book(16).Chapters.Should().HaveCount(13);
        swete.Book(16).Chapters.Select(chapter => chapter.Number).Should().Equal(Enumerable.Range(1, 13));
    }

    /// <summary>
    /// Esther's Addition A, which stands before chapter 1 and which the file gives a chapter of its
    /// own because there is no chapter yet to hang it on. It is read into chapter 1 with the label
    /// the same file gives Esther's other four additions, so that the one addition it could not
    /// place is held the way it places the rest.
    /// </summary>
    [Fact]
    public void EstherOpensWithTheAdditionSwetePrintsBeforeItsFirstChapter()
    {
        var chapter = swete.Book(17).Chapters[0]!;

        chapter.Verses.Should().HaveCount(39);
        chapter.Verses.Take(17).Should().OnlyContain(verse => verse.Label == "a");
        chapter.Verses[0]!.Words[0]!.Surface.Should().Be("ΕΤΟΥΣ");
        chapter.Verses[17]!.Number.Should().Be(1);
        chapter.Verses[17]!.Label.Should().BeEmpty();
    }

    /// <summary>
    /// The 246 verses the Greek numbers by extending one rather than adding one. They are where the
    /// Septuagint holds material the Hebrew does not — Esther's additions, the long insertions of
    /// 3 Kingdoms, the appendices of Proverbs — and dropping the letter would collide two verses
    /// onto one number.
    /// </summary>
    [Fact]
    public void TheLetteredVersesKeepTheirLetter() =>
        swete.Source.Books
            .SelectMany(book => book.Chapters)
            .SelectMany(chapter => chapter.Verses)
            .Count(verse => verse.Label.Length > 0)
            .Should().Be(246);

    /// <summary>
    /// Sirach's preface is not in the corpus, so chapter 1 opens with chapter 1. The first word
    /// carries a Latin A in the middle of a Greek word, which is the transcription's own damage and
    /// is kept: correcting a source silently is how a corpus stops being a copy of anything.
    /// </summary>
    [Fact]
    public void SirachOpensAtItsFirstChapterRatherThanAtItsPreface() =>
        swete.Verse(72, 1, 1).Words[0]!.Surface.Should().Be("ΠΑΣΑ");

    [Fact]
    public void TheEpistleOfJeremiahIsOneChapter()
    {
        var book = swete.Book(76);

        book.Chapters.Should().ContainSingle().Which.Number.Should().Be(1);
        book.Chapters[0]!.Verses.Should().HaveCount(72);
        book.Chapters[0]!.Verses[0]!.Words[0]!.Surface.Should().Be("ANΤΙΓΡΑΦΟΝ");
    }

    /// <summary>
    /// The verse division of this edition is not Brenton's, which is the reason to hold both. Joel
    /// stands in three chapters here and four in Brenton; Malachi in four here and three there. The
    /// frame decides where each of them sits by asking the edition its own shape, so these are the
    /// two shortest statements of what it will be asked.
    /// </summary>
    [Theory]
    [InlineData(29, 3)]
    [InlineData(39, 4)]
    [InlineData(20, 29)]
    [InlineData(72, 51)]
    public void ItsChapterDivisionIsItsOwn(int ordinal, int chapters) =>
        swete.Book(ordinal).Chapters.Should().HaveCount(chapters);

    /// <summary>
    /// Daniel, Susanna and Bel are Theodotion's, because Vaticanus is what Swete prints and
    /// Vaticanus reads Theodotion in all three. Checked on the opening of Daniel, which is where
    /// the two Greek versions differ most visibly: Theodotion opens Ἐν ἔτει τρίτῳ and the Old Greek
    /// opens with the year.
    /// </summary>
    [Fact]
    public void DanielIsTheodotionsRatherThanTheOldGreek() =>
        swete.Verse(27, 1, 1).Words.Select(word => word.Surface).Take(3)
            .Should().Equal("En", "ἔτει", "τρίτῳ");

    /// <summary>
    /// Every file the folder holds and the edition does not read, so that an absence stays a
    /// decision rather than becoming a book that quietly never arrived. Isaiah is the one that
    /// matters: the file is Ottley's Codex Alexandrinus text of 1904 and not Swete's, and the Isaiah
    /// read is Swete's own, from the transcription of his volume.
    /// </summary>
    [Fact]
    public void WhatIsNotLoadedIsStillOnDisk()
    {
        foreach (var book in SweteTextSource.NotLoaded)
        {
            File.Exists(Path.Combine(TestResources.SweteFolder, SweteTextSource.FileName(book)))
                .Should().BeTrue($"{book} is left out on purpose and the reason needs the file to check");
        }

        Swete.Text(swete.Verse(23, 1, 1)).Should().Contain("Ὀζείου").And.NotContain("Ὀζίου");
    }

    /// <summary>
    /// It arrives with its words and nothing else. A check that it stays that way is what would
    /// catch a later pass writing derived annotation onto it as though the edition had supplied it.
    /// </summary>
    [Fact]
    public void ItCarriesNoAnnotationAtAll() =>
        swete.Source.Books
            .SelectMany(book => book.Chapters)
            .SelectMany(chapter => chapter.Verses)
            .SelectMany(verse => verse.Words)
            .Should().OnlyContain(word =>
                word.Lemma == null && word.StrongNumber == null && word.Gloss == null
                && word.Morphology == null);

    [Fact]
    public void APartialFolderIsRefused()
    {
        var empty = Directory.CreateTempSubdirectory("swete-partial");

        try
        {
            var act = () => SweteTextSource.Read(empty.FullName);

            act.Should().Throw<InvalidOperationException>().WithMessage("*01.Genesis*");
        }
        finally
        {
            empty.Delete(recursive: true);
        }
    }
}

/// <summary>The chapters and verses the transcription numbers as Swete does not.</summary>
[Trait(TestCategory.Name, TestCategory.Corpus)]
public class SweteNumberingTests
{
    private static readonly IReadOnlyList<BookDraft> Books = SweteTextSource.Read(TestResources.SweteFolder).Books;

    [Fact]
    public void WisdomRunsFromOneToNineteenWithTheLordIsKindAtFifteen()
    {
        var wisdom = Books.Single(book => book.CanonicalOrdinal == 75);

        wisdom.Chapters.Select(chapter => chapter.Number).Should().Equal(Enumerable.Range(1, 19));
        wisdom.Chapters.Single(chapter => chapter.Number == 15).Verses[0].Words[0].Surface.Should().Be("Σὺ");
    }

    [Theory]
    [InlineData(13, 16, 39)]
    [InlineData(14, 4, 20)]
    [InlineData(14, 17, 11)]
    public void AVerseWithADigitDoubledStandsAtTheNumberItReadsAs(int book, int chapter, int verse)
    {
        var numbers = Books.Single(b => b.CanonicalOrdinal == book).Chapters
            .Single(c => c.Number == chapter).Verses.Select(v => v.Number).ToList();

        numbers.Should().Contain(verse).And.BeInAscendingOrder().And.OnlyContain(n => n < 100);
    }
}
