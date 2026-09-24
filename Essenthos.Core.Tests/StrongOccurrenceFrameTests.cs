using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Endpoints;
using FluentAssertions;
using Microsoft.EntityFrameworkCore.Storage;
using Xunit;

namespace Essenthos.Core.Tests;

/// <summary>
/// That a Strong number's occurrences are answered at the shared frame's address, with the
/// original's own number beside it. BHSA's Malachi 3:19 is the frame's 4:1: sent as 3:19, the page
/// linked to a verse the frame does not have and quoted nothing for it.
/// </summary>
[Collection(WitnessDatabaseCollection.Name)]
public sealed class StrongOccurrenceFrameTests : IDisposable
{
    private const string Number = "H2009";

    private readonly AppDbContext _db;
    private readonly IDbContextTransaction _transaction;
    private readonly Text _hebrew;

    public StrongOccurrenceFrameTests(WitnessDatabase database)
    {
        _db = database.NewContext();
        _transaction = _db.Database.BeginTransaction();

        _hebrew = Corpus.Add(_db, "BHSA", TextKind.CriticalEdition, "hbo",
            (3, 18, ["וְשַׁבְתֶּם", "הִנֵּה"]),
            (3, 19, ["כִּי", "הִנֵּה", "הַיּוֹם"]));
        _db.SaveChanges();
        _db.In(_hebrew, 39);

        foreach (var word in _db.Words.Where(w => w.TextId == _hebrew.Id && w.Surface == "הִנֵּה"))
        {
            word.StrongNumber = Number;
        }

        var placed = _db.VerseReferences.Single(r => r.Verse!.TextId == _hebrew.Id && r.Verse.Number == 19);
        placed.CanonicalChapter = 4;
        placed.CanonicalVerse = 1;
        _db.SaveChanges();
    }

    public void Dispose()
    {
        _transaction.Rollback();
        _transaction.Dispose();
        _db.Dispose();
    }

    [Fact]
    public async Task AnOccurrenceIsAnsweredWhereItsVerseStandsInTheFrame()
    {
        var page = await Page();

        page.Items.Select(item => (item.BookOrdinal, item.Chapter, item.Verse)).Should().Equal((39, 3, 18), (39, 4, 1));
        page.Items[1].Printed.Should().Equal("3:19");
    }

    [Fact]
    public async Task SaysNothingOfTheNumberingWhereTheTwoAgree() =>
        (await Page()).Items[0].Printed.Should().BeNull();

    private Task<StrongOccurrenceListResponse> Page() =>
        StrongEndpoints.OccurrencePage(
            _db, Number, _db.Words.Where(w => w.StrongNumber == Number), null, null, default);
}
