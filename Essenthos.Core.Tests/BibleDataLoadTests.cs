using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
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
[Trait(TestCategory.Name, TestCategory.Corpus)]
[Collection(WitnessDatabaseCollection.Name)]
public sealed class BibleDataLoadTests : IClassFixture<BibleDataLoadTests.Encyclopedia>, IDisposable
{
    private readonly AppDbContext _db;
    private readonly IDbContextTransaction _transaction;

    public BibleDataLoadTests(WitnessDatabase database)
    {
        _db = database.NewContext();
        _transaction = _db.Database.BeginTransaction();
    }

    public sealed class Encyclopedia(WitnessDatabase database) : SeededDatabase(database)
    {
        protected override Task Seed(AppDbContext db) =>
            new BibleDataLoader(db, NullLogger<BibleDataLoader>.Instance)
                .Load(Path.GetDirectoryName(TestResources.Path("BibleData2026", "BibleData-Person.csv"))!);
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

    /// <summary>
    /// The book of Jashar is a writing the text cites, not a man; the dataset's record for it is not
    /// written, and the four verses it gave it, which name Jarmuth, go with it.
    /// </summary>
    [Fact]
    public async Task TheBookOfJasharIsNotAPerson()
    {
        (await _db.Entities.AnyAsync(e => e.SourceId == "person:Jashar_1")).Should().BeFalse();
        (await _db.EntityVerses.AnyAsync(v => v.Label == "Jashar")).Should().BeFalse();
    }

    /// <summary>
    /// Jeremiah 36:9 says the ninth month, and the dataset named the fast for Av, the fifth. The name is
    /// this corpus's, says so, and keeps the dataset's in the notes; the date is untouched.
    /// </summary>
    [Fact]
    public async Task TheFastOfJeremiah36IsInTheNinthMonth()
    {
        var fast = await _db.Events.SingleAsync(e => e.Slug == "judahsfastduringav");

        fast.Name.Should().Be("Judah's fast in the ninth month");
        fast.NameSource.Should().Be(EventNames.Generated);
        fast.Notes.Should().StartWith("BibleData names this \"Judah's fast during the month of Av");
        fast.YearFromCreation.Should().Be(3358);
    }

    /// <summary>
    /// Six Levites of Hezekiah's and Nehemiah's day are Levi's descendants, as the dataset holds every
    /// other Levite of those chapters, and not his sons beside Gershon, Kohath and Merari.
    /// </summary>
    [Fact]
    public async Task LevisSonsAreGershonKohathAndMerari()
    {
        var sons = await _db.EntityRelationships
            .Where(r => r.From!.SourceId == "person:Levi_1" && r.Type == "father")
            .Select(r => r.To!.SourceId)
            .ToListAsync();
        sons.Should().BeEquivalentTo(["person:Gershon_1", "person:Kohath_1", "person:Merari_1"]);

        var miniamin = await _db.EntityRelationships
            .SingleAsync(r => r.From!.SourceId == "person:Miniamin_1" && r.To!.SourceId == "person:Levi_1");
        miniamin.Type.Should().Be("descendant");
        miniamin.Notes.Should().StartWith("inferred that these are Levites serving their brothers. Read as descent");
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
        greek.Should().Be(1_160);
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
