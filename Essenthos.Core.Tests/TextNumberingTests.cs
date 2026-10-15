using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Endpoints;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Essenthos.Core.Tests;

/// <summary>
/// How a text's own numbering of a book stands against the shared frame: the Synodal prints Psalm 90
/// at what the frame lays out as 91, Brenton numbers it so in his file, and Ohienko as the frame does.
/// </summary>
[Collection(WitnessDatabaseCollection.Name)]
public sealed class TextNumberingTests : IDisposable
{
    private const int Psalms = 19;

    private readonly AppDbContext _db;

    public TextNumberingTests(WitnessDatabase database)
    {
        _db = database.NewContext();
        Clear();
    }

    public void Dispose()
    {
        Clear();
        _db.Dispose();
    }

    [Fact]
    public async Task APrintedAddressIsTheTextsOwnAndListedWhereItLandsElsewhere()
    {
        var synodal = Psalter("RUSV");
        Stated(synodal, 90, 1, 89, 1);
        Stated(synodal, 91, 1, 90, 1);

        var numbering = await TextNumbering.Of(_db, synodal.Id, Psalms, default);

        numbering.Should().Equal(
            new NumberedAddressResponse(89, 1, 90, 1),
            new NumberedAddressResponse(90, 1, 91, 1));
    }

    [Fact]
    public async Task AFileNumberingTheBookItsOwnWayIsReadFromTheFile()
    {
        var brenton = Psalter("BRENTON");
        Placed(brenton, 91, 1, 92, 1);
        Placed(brenton, 90, 1, 91, 1);
        Placed(brenton, 89, 1, 90, 1);

        var numbering = await TextNumbering.Of(_db, brenton.Id, Psalms, default);

        numbering.Should().Equal(
            new NumberedAddressResponse(89, 1, 90, 1),
            new NumberedAddressResponse(90, 1, 91, 1),
            new NumberedAddressResponse(91, 1, 92, 1));
    }

    [Fact]
    public async Task ATextNumberingTheBookAsTheFrameDoesListsNothing()
    {
        var ohienko = Psalter("UBIO");

        (await TextNumbering.Of(_db, ohienko.Id, Psalms, default)).Should().BeEmpty();
    }

    private Text Psalter(string slug)
    {
        var text = Corpus.Add(_db, slug, TextKind.Translation, "rus", (1, 1, ["слово"]));
        _db.AddBook(text, Psalms, "Psalms", (89, 1, ["а"]), (90, 1, ["б"]), (91, 1, ["в"]));
        _db.SaveChanges();
        return text;
    }

    private Verse Verse(Text text, int chapter, int verse) =>
        _db.Verses.Single(v => v.TextId == text.Id && v.Book!.CanonicalOrdinal == Psalms
                               && v.ChapterNumber == chapter && v.Number == verse);

    private void Stated(Text text, int chapter, int verse, int statedChapter, int statedVerse)
    {
        _db.StatedVerseNumbers.Add(new StatedVerseNumber
        {
            VerseId = Verse(text, chapter, verse).Id, Position = 1, ChapterNumber = statedChapter, Number = statedVerse,
        });
        _db.SaveChanges();
    }

    /// <summary>Moves the text's own verse to another place in the frame, one at a time and highest first, so no two meet.</summary>
    private void Placed(Text text, int chapter, int verse, int frameChapter, int frameVerse)
    {
        var id = Verse(text, chapter, verse).Id;
        _db.VerseReferences
            .Where(r => r.VerseId == id && r.IsPrimary)
            .ExecuteUpdate(set => set.SetProperty(r => r.CanonicalChapter, frameChapter)
                .SetProperty(r => r.CanonicalVerse, frameVerse));
    }

    private void Clear()
    {
        _db.Database.ExecuteSqlRaw("DELETE FROM text");
    }
}
