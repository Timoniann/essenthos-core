using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Loading;
using Essenthos.Core.Usfm;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Essenthos.Core.Tests;

/// <summary>
/// Beginning a verse of Brenton's Greek at another word: the words that change sides, named as the
/// file prints them, moved and nothing else.
/// </summary>
public class BrentonDivisionTests
{
    private const int Nehemiah = 16;

    private static WordDraft Word(string surface, TextBreak? opening = null) =>
        new(surface, " ", Break: opening);

    private static ChapterDraft Chapter(int number, params (int Verse, WordDraft[] Words)[] verses) =>
        new(number, [.. verses.Select(v => new VerseDraft(v.Verse, v.Words))]);

    private static BrentonDivision Division(int chapter, string verse, string previous, string? begins = null, string? ends = null) =>
        new(Nehemiah, chapter, verse, previous, begins, ends, ["BRENTON"], null);

    private static string Read(IReadOnlyList<ChapterDraft> chapters, int chapter, int verse) =>
        BrentonDivisions.Printed(chapters.Single(c => c.Number == chapter).Verses.Single(v => v.Number == verse).Words);

    [Fact]
    public void WordsTheFilePrintsAtAVersesEndBeginTheNext()
    {
        var chapters = new[]
        {
            Chapter(12,
                (17, [Word("τῷ"), Word("Φελετὶ,"), Word("τῷ"), Word("βαλγὰς,")]),
                (18, [Word("Τῷ"), Word("Ἰωαρὶβ,")])),
        };

        var divided = BrentonDivisions.Apply(Nehemiah, chapters, [Division(12, "18", "12:17", begins: "τῷ βαλγὰς,")]);

        Read(divided, 12, 17).Should().Be("τῷ Φελετὶ,");
        Read(divided, 12, 18).Should().Be("τῷ βαλγὰς, Τῷ Ἰωαρὶβ,");
    }

    [Fact]
    public void WordsTheFilePrintsAtAVersesHeadEndTheOneBeforeItAcrossAChapter()
    {
        var chapters = new[]
        {
            Chapter(7, (17, [Word("τελευτήσεις,")])),
            Chapter(8, (1, [Word("οὕτως"), Word("ἔδειξε·"), Word("Καὶ"), Word("ἰδοὺ")])),
        };

        var divided = BrentonDivisions.Apply(Nehemiah, chapters, [Division(8, "1", "7:17", ends: "οὕτως ἔδειξε·")]);

        Read(divided, 7, 17).Should().Be("τελευτήσεις, οὕτως ἔδειξε·");
        Read(divided, 8, 1).Should().Be("Καὶ ἰδοὺ");
    }

    /// <summary>The divisions of one verse after another are made in turn, each on what the one before left.</summary>
    [Fact]
    public void ARunOfDivisionsIsMadeInOrder()
    {
        var chapters = new[]
        {
            Chapter(12,
                (17, [Word("τῷ"), Word("Φελετὶ,"), Word("τῷ"), Word("βαλγὰς,")]),
                (18, [Word("Τῷ"), Word("Ἰωαρὶβ,")]),
                (19, [Word("Ματθαναΐ")])),
        };

        var divided = BrentonDivisions.Apply(Nehemiah, chapters,
        [
            Division(12, "18", "12:17", begins: "τῷ βαλγὰς,"),
            Division(12, "19", "12:18", begins: "Τῷ Ἰωαρὶβ,"),
        ]);

        Read(divided, 12, 18).Should().Be("τῷ βαλγὰς,");
        Read(divided, 12, 19).Should().Be("Τῷ Ἰωαρὶβ, Ματθαναΐ");
    }

    [Fact]
    public void AParagraphOpenedBeforeTheVerseStaysAtItsHead()
    {
        var chapters = new[]
        {
            Chapter(4,
                (1, [Word("λέγων,"), Word("λάλησον,")]),
                (2, [Word("καὶ", TextBreak.Paragraph), Word("ἐρεῖς")])),
        };

        var divided = BrentonDivisions.Apply(Nehemiah, chapters, [Division(4, "2", "4:1", begins: "λάλησον,")]);

        var verse = divided[0].Verses.Single(v => v.Number == 2).Words;
        verse[0].Surface.Should().Be("λάλησον,");
        verse[0].Break.Should().Be(TextBreak.Paragraph);
        verse[1].Break.Should().BeNull();
    }

    /// <summary>A mark standing on its own belongs to the word before it and moves with it.</summary>
    [Fact]
    public void AMarkStandingAloneIsNotAWord()
    {
        new BrentonDivision(Nehemiah, 1, "2", "1:1", "ἐπιλάθῃ —", null, [], null).Count.Should().Be(1);
    }

    [Fact]
    public void AVerseThatDoesNotReadAsTheListSaysStopsTheRead()
    {
        var chapters = new[] { Chapter(12, (17, [Word("τῷ"), Word("Φελετὶ,")]), (18, [Word("Τῷ")])) };

        var read = () => BrentonDivisions.Apply(Nehemiah, chapters, [Division(12, "18", "12:17", begins: "Σαμουέ·")]);

        read.Should().Throw<InvalidOperationException>().WithMessage("*Nehemiah 12:18*BrentonDivisions.json*");
    }

    [Fact]
    public void TheListSaysWhatItMovesAndWhoDividesSo()
    {
        BrentonDivisions.All.Should().HaveCount(235);
        BrentonDivisions.Note.Should().Contain($"{BrentonDivisions.All.Count} verses");
        BrentonDivisions.All.Should().OnlyContain(d => (d.Begins == null) != (d.Ends == null) && d.Count > 0);
        BrentonDivisions.All.Should().OnlyContain(d => d.Witnesses.Count > 0);
        BrentonDivisions.All.Select(d => (d.Book, d.Address)).Should().OnlyHaveUniqueItems();
    }
}

/// <summary>Brenton's Greek as the reader divides it, against the files on this disk.</summary>
[Trait(TestCategory.Name, TestCategory.Corpus)]
public class BrentonDividedReadTests(Brenton brenton) : IClassFixture<Brenton>
{
    private const int Nehemiah = 16;

    private const int Amos = 30;

    private string Read(int book, int chapter, int verse) => BrentonDivisions.Printed(
        brenton.Book(book).Chapters.Single(c => c.Number == chapter).Verses.Single(v => v.Number == verse && v.Label.Length == 0).Words);

    [Fact]
    public void NehemiahTwelveReadsAsBrentonTranslatedIt()
    {
        Read(Nehemiah, 12, 17).Should().Be("Τῷ Ἀβιὰ, Ζεχρί· τῷ Μιαμὶν, Μααδαί· τῷ Φελετὶ,");
        Read(Nehemiah, 12, 18).Should().Be("τῷ βαλγὰς, Σαμουέ· τῷ Σεμία, Ἰωνάθαν·");
        Read(Nehemiah, 12, 19).Should().Be("Τῷ Ἰωαρὶβ, Ματθαναΐ τῷ Ἐδίῳ, Ὀζί.");
    }

    /// <summary>
    /// Amos 8:2 begins with the question, as Brenton's English and the Greek Wikisource text begin it;
    /// the vision's opening words stay at the end of 7:17, where Brenton's English has them too.
    /// </summary>
    [Fact]
    public void AmosEightBeginsTheQuestionAtVerseTwo()
    {
        Read(Amos, 8, 1).Should().Be("Καὶ ἰδοὺ ἄγγος ἰξευτοῦ.");
        Read(Amos, 8, 2).Should().StartWith("Καὶ εἶπε, τί σὺ βλέπεις Ἀμώς;");
        Read(Amos, 7, 17).Should().EndWith("οὕτως ἔδειξέ μοι Κύριος Κύριος.");
    }

    /// <summary>Only verses change: every book holds the words its file prints, in the file's order.</summary>
    [Fact]
    public void NoWordIsLostOrAdded()
    {
        var files = Directory.GetFiles(TestResources.SeptuagintFolder, "*.usfm")
            .Select(path => UsfmReader.Read(File.ReadAllText(path)))
            .ToDictionary(book => book.Book);

        // The words the file repeats or lost are BrentonEdits', and read here as the file prints them.
        var unedited = SeptuagintTextSource.Read(TestResources.SeptuagintFolder, edited: false);
        foreach (var code in (string[])["NUM", "PSA", "ISA", "1ES"])
        {
            var printed = files[code].Chapters.SelectMany(c => c.Verses).SelectMany(v => v.Words).Select(w => w.Surface + w.Trailer);
            var read = unedited.Books.Single(b => b.CanonicalOrdinal == SeptuagintTextSource.Canonical(code)).Chapters
                .SelectMany(c => c.Verses).SelectMany(v => v.Words).Select(w => w.Surface + w.Trailer);

            read.Should().Equal(printed, code);
        }
    }

    [Fact]
    public void TheRowSaysTheDivisionsAreOurs() =>
        SeptuagintTextSource.Definition().RightsNote.Should().Be($"{BrentonDivisions.Note} {BrentonEdits.Note}");
}

/// <summary>The divisions made in place on a corpus that loaded Brenton's Greek before them.</summary>
[Collection(WitnessDatabaseCollection.Name)]
public sealed class BrentonDivisionLoadTests : IDisposable
{
    private const int Nehemiah = 16;

    private readonly AppDbContext _db;
    private readonly BrentonDivisionLoader _loader;

    public BrentonDivisionLoadTests(WitnessDatabase database)
    {
        _db = database.NewContext();
        Clear();
        _loader = new BrentonDivisionLoader(_db, NullLogger<BrentonDivisionLoader>.Instance);
    }

    public void Dispose()
    {
        Clear();
        _db.Dispose();
    }

    private void Clear() => _db.Database.ExecuteSqlRaw("DELETE FROM text");

    /// <summary>Nehemiah 12:17-19 as eBible's file divides it.</summary>
    private Text Loaded()
    {
        var text = new Text { Slug = SeptuagintTextSource.Slug, Name = "Brenton", Kind = TextKind.PrintedEdition, Language = "grc" };
        _db.Texts.Add(text);
        _db.AddBook(text, Nehemiah, "Nehemiah",
            (12, 17, ["Τῷ", "Ἀβιὰ,", "Ζεχρί·", "τῷ", "Μιαμὶν,", "Μααδαί·", "τῷ", "Φελετὶ,", "τῷ", "βαλγὰς,", "Σαμουέ·", "τῷ", "Σεμία,", "Ἰωνάθαν·"]),
            (12, 18, ["Τῷ", "Ἰωαρὶβ,"]),
            (12, 19, ["Ματθαναΐ", "τῷ", "Ἐδίῳ,", "Ὀζί."]));
        _db.SaveChanges();

        // The reader ends every word with the space after it, the verse's last word too.
        _db.Database.ExecuteSqlRaw("UPDATE word SET trailer = ' ' WHERE text_id = {0}", text.Id);
        return text;
    }

    private string Read(Text text, int verse) =>
        string.Concat(_db.Words.AsNoTracking()
            .Where(w => w.TextId == text.Id && w.Verse!.Number == verse)
            .OrderBy(w => w.Position)
            .Select(w => w.Surface + w.Trailer)
            .ToList()).TrimEnd();

    [Fact]
    public async Task TheWordsMoveWithTheirRowsAndLeaveTheirLinks()
    {
        var text = Loaded();
        var other = Corpus.Add(_db, "BRENTON", TextKind.Translation, "en", (12, 17, ["Pheleti"]), (12, 18, ["Balgas"]));
        _db.SaveChanges();
        var balgas = _db.WordAt(text, 12, 17, 10);
        var pheleti = _db.WordAt(text, 12, 17, 8);
        balgas.Surface.Should().Be("βαλγὰς,");
        balgas.Lemma = "Βαλγᾶς";
        _db.SaveChanges();
        var moved = Link(text, other, balgas, _db.WordAt(other, 12, 17, 1));
        var stayed = Link(text, other, pheleti, _db.WordAt(other, 12, 17, 1));

        var outcome = await _loader.Load();

        outcome.Divisions.Should().Be(2);
        outcome.Words.Should().Be(8);
        Read(text, 17).Should().Be("Τῷ Ἀβιὰ, Ζεχρί· τῷ Μιαμὶν, Μααδαί· τῷ Φελετὶ,");
        Read(text, 18).Should().Be("τῷ βαλγὰς, Σαμουέ· τῷ Σεμία, Ἰωνάθαν·");
        Read(text, 19).Should().Be("Τῷ Ἰωαρὶβ, Ματθαναΐ τῷ Ἐδίῳ, Ὀζί.");

        var row = await _db.Words.AsNoTracking().Include(w => w.Verse).SingleAsync(w => w.Id == balgas.Id);
        row.Verse!.Number.Should().Be(18);
        row.Position.Should().Be(2);
        row.Lemma.Should().Be("Βαλγᾶς", "the word keeps its row and what stands on it");
        (await _db.Links.AnyAsync(l => l.Id == moved.Id)).Should().BeFalse();
        (await _db.Links.AnyAsync(l => l.Id == stayed.Id)).Should().BeTrue();
        (await _db.Texts.AsNoTracking().SingleAsync(t => t.Id == text.Id)).RightsNote.Should().Be(BrentonDivisions.Note);
    }

    [Fact]
    public async Task ASecondRunFindsNothingToDo()
    {
        var text = Loaded();
        await _loader.Load();

        var again = await _loader.Load();

        again.Divisions.Should().Be(0);
        Read(text, 18).Should().Be("τῷ βαλγὰς, Σαμουέ· τῷ Σεμία, Ἰωνάθαν·");
    }

    [Fact]
    public async Task AVerseReadingAsNeitherStopsThePass()
    {
        var text = Loaded();
        _db.WordAt(text, 12, 17, 14).Surface = "Ἰωνάθαμ·";
        _db.SaveChanges();

        var load = () => _loader.Load();

        await load.Should().ThrowAsync<InvalidOperationException>().WithMessage("*Nehemiah 12:18*Nothing was changed*");
        Read(text, 18).Should().Be("Τῷ Ἰωαρὶβ,");
    }

    private Link Link(Text from, Text to, Word fromWord, Word toWord)
    {
        var link = new Link
        {
            FromTextId = from.Id, ToTextId = to.Id, Relation = LinkRelation.Equals, Method = LinkMethod.Lexical,
            Confidence = 0.9, Provenance = new() { Source = "a test" },
        };
        _db.Links.Add(link);
        _db.LinkWords.Add(new LinkWord { Link = link, Word = fromWord, Side = LinkSide.From });
        _db.LinkWords.Add(new LinkWord { Link = link, Word = toWord, Side = LinkSide.To });
        _db.SaveChanges();
        return link;
    }
}
