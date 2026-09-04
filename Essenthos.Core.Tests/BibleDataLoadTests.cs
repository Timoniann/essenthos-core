using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Loading.Encyclopedia;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Essenthos.Core.Tests;

/// <summary>
/// The whole encyclopedia written to a database and read back.
///
/// The other tests measure what the loader builds in memory, which is where every one of these
/// defects could be seen. This one is about what survives the write — the distinguisher is
/// rewritten after the entities have already been saved, and rides back on change tracking, which
/// is exactly the kind of thing that works in a list and does nothing in a table.
/// </summary>
[Collection(WitnessDatabaseCollection.Name)]
public sealed class BibleDataLoadTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly IDbContextTransaction _transaction;

    public BibleDataLoadTests(WitnessDatabase database)
    {
        _db = database.NewContext();
        _transaction = _db.Database.BeginTransaction();

        var folder = Path.GetDirectoryName(TestResources.Path("BibleData2026", "BibleData-Person.csv"))!;
        new BibleDataLoader(_db, NullLogger<BibleDataLoader>.Instance).Load(folder).GetAwaiter().GetResult();
    }

    public void Dispose()
    {
        _transaction.Rollback();
        _transaction.Dispose();
        _db.Dispose();
    }

    [Fact]
    public async Task ADistinguisherIsStoredWithNamesInItRatherThanRowIdentifiers()
    {
        var abdeel = await _db.Entities.SingleAsync(e => e.SourceId == "person:Abdeel_1");

        abdeel.Distinguisher.Should().Be("father of Shelemiah (JER 36:26)");
    }

    [Fact]
    public async Task APlaceIsNamedInTheLanguagesTheTextUses()
    {
        var ararat = await _db.Entities
            .Include(e => e.Names)
            .SingleAsync(e => e.SourceId == "place:Ararat_1");

        ararat.Kind.Should().Be(EntityKind.Place);
        ararat.Names.Should().NotBeEmpty()
            .And.Contain(n => n.Hebrew != null && n.HebrewStrongNumber != null);
    }

    [Fact]
    public async Task BothLanguagesOfAStrongNumberAreStored()
    {
        var both = await _db.EntityNames
            .CountAsync(n => n.HebrewStrongNumber != null && n.GreekStrongNumber != null);
        var greek = await _db.EntityNames.CountAsync(n => n.GreekStrongNumber != null);

        both.Should().BeGreaterThan(800);
        greek.Should().Be(1_161);
    }

    /// <summary>
    /// The names are searched as well as the headword, so a title on the wrong entity is a search
    /// that answers with the wrong person: Christ returned the Antichrist, two prophets and the
    /// God of Israel, and not Jesus.
    /// </summary>
    [Fact]
    public async Task SearchingForATitleOfTheSonFindsJesus()
    {
        var found = await _db.Entities
            .Where(e => e.Names.Any(n => n.Label == "Christ" || n.Label == "Son of Man"))
            .Select(e => e.SourceId)
            .ToListAsync();

        found.Should().Equal("essenthos:jesus");
    }
}
