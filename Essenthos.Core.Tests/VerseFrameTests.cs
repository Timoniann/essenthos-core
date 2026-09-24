using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Endpoints;
using FluentAssertions;
using Microsoft.EntityFrameworkCore.Storage;
using Xunit;

namespace Essenthos.Core.Tests;

/// <summary>
/// That <c>/v1/verses</c> answers an address in the shared frame with the verse placed there, and
/// says what the text itself numbers it.
///
/// The addresses come from the encyclopedia, which speaks the frame. A text that numbers a chapter
/// its own way — the Reina-Valera, the Synodal — was asked for its own row of that number and quoted
/// the verse beside the one the page was about, which reads as a fact about the page.
/// </summary>
[Collection(WitnessDatabaseCollection.Name)]
public sealed class VerseFrameTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly IDbContextTransaction _transaction;
    private readonly Text _text;

    public VerseFrameTests(WitnessDatabase database)
    {
        _db = database.NewContext();
        _transaction = _db.Database.BeginTransaction();

        _text = Corpus.Add(_db, "RV", TextKind.Translation, "spa",
            (12, 4, ["Y", "se", "fue", "Abram"]),
            (12, 5, ["Y", "tomó", "Abram", "a", "Sarai"]),
            (12, 6, ["Y", "pasó", "Abram"]));
        _db.SaveChanges();
    }

    public void Dispose()
    {
        _transaction.Rollback();
        _transaction.Dispose();
        _db.Dispose();
    }

    [Fact]
    public async Task QuotesTheVersePlacedAtTheAddressRatherThanTheOneNumberedSo()
    {
        Place(5, 7);
        Place(6, 8);
        _db.SaveChanges();

        var found = await VerseEndpoints.Read(_db, _text.Id, [Key(12, 7)], null, default);

        found.Should().ContainSingle();
        found[0].Chapter.Should().Be(12);
        found[0].Verse.Should().Be(7);
        found[0].Text.Should().Be("Y tomó Abram a Sarai");
        found[0].Printed.Should().Equal("12:5");
    }

    [Fact]
    public async Task AnswersNothingAtAnAddressTheTextPlacesNoVerseAt()
    {
        Place(5, 7);
        _db.SaveChanges();

        (await VerseEndpoints.Read(_db, _text.Id, [Key(12, 5)], null, default)).Should().BeEmpty();
    }

    [Fact]
    public async Task SaysNothingOfTheNumberingWhereTheTwoAgree()
    {
        var found = await VerseEndpoints.Read(_db, _text.Id, [Key(12, 4)], null, default);

        found.Should().ContainSingle().Which.Printed.Should().BeNull();
    }

    /// <summary>What the edition prints in the verse is its own number, and wins over the row's.</summary>
    [Fact]
    public async Task SendsTheNumberTheEditionPrints()
    {
        _db.StatedVerseNumbers.Add(new StatedVerseNumber { VerseId = VerseAt(4).Id, Position = 1, ChapterNumber = 11, Number = 9 });
        _db.StatedVerseNumbers.Add(new StatedVerseNumber { VerseId = VerseAt(4).Id, Position = 2, ChapterNumber = 11, Number = 10 });
        _db.SaveChanges();

        var found = await VerseEndpoints.Read(_db, _text.Id, [Key(12, 4)], null, default);

        found.Should().ContainSingle().Which.Printed.Should().Equal("11:9", "11:10");
    }

    /// <summary>
    /// A verse that runs on into the next address answers for it only where nothing is placed there,
    /// so a text that joins two verses prints the joined one once and not the next one twice.
    /// </summary>
    [Fact]
    public async Task PrefersTheVersePlacedAtAnAddressToOneRunningOnIntoIt()
    {
        _db.VerseReferences.Add(new VerseReference
        {
            VerseId = VerseAt(4).Id, CanonicalBook = 1, CanonicalChapter = 12, CanonicalVerse = 5, IsPrimary = false,
        });
        _db.VerseReferences.Add(new VerseReference
        {
            VerseId = VerseAt(6).Id, CanonicalBook = 1, CanonicalChapter = 12, CanonicalVerse = 9, IsPrimary = false,
        });
        _db.SaveChanges();

        var found = await VerseEndpoints.Read(_db, _text.Id, [Key(12, 5), Key(12, 9)], null, default);

        found.Select(verse => (verse.Verse, verse.Text)).Should().Equal(
            (5, "Y tomó Abram a Sarai"),
            (9, "Y pasó Abram"));
        found[1].Printed.Should().Equal("12:6");
    }

    private static int Key(int chapter, int verse) =>
        VerseEndpoints.Address.BookStride + (chapter * VerseEndpoints.Address.ChapterStride) + verse;

    private Verse VerseAt(int number) =>
        _db.Verses.Single(v => v.TextId == _text.Id && v.ChapterNumber == 12 && v.Number == number);

    private void Place(int stored, int canonical) =>
        _db.VerseReferences.Single(r => r.VerseId == VerseAt(stored).Id && r.IsPrimary).CanonicalVerse = canonical;
}
