using System.Reflection;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Loading.Encyclopedia;
using Essenthos.Core.Migrations;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Npgsql;
using Xunit;

namespace Essenthos.Core.Tests;

/// <summary>
/// The corrections the loaders now make on a cold load, made by the migration on a corpus loaded
/// before them — and made to say exactly what the loaders say, because a page must not read one way
/// on a rebuilt corpus and another on the one that was corrected in place.
/// </summary>
[Collection(WitnessDatabaseCollection.Name)]
public sealed class DatasetCorrectionMigrationTests : IDisposable
{
    private const string Dataset = BibleDataLoader.Source;
    private const string WorldSource = "Wikidata, query.wikidata.org, CC0";
    private const string SolomonsTemple = "http://www.wikidata.org/entity/Q223644";

    private readonly WitnessDatabase _database;
    private readonly AppDbContext _db;

    public DatasetCorrectionMigrationTests(WitnessDatabase database)
    {
        _database = database;
        _db = database.NewContext();
        Clear();
    }

    public void Dispose()
    {
        Clear();
        _db.Dispose();
    }

    private void Clear()
    {
        _db.Database.ExecuteSqlRaw("DELETE FROM event");
        _db.Database.ExecuteSqlRaw("DELETE FROM entity");
    }

    [Fact]
    public async Task The_book_of_Jashar_goes_and_its_verses_with_it()
    {
        var jashar = Person("jashar", "person:Jashar_1");
        jashar.Verses.Add(new EntityVerse { CanonicalBook = 6, CanonicalChapter = 10, CanonicalVerse = 3, Label = "Jashar", Source = Dataset });
        await _db.SaveChangesAsync();

        await Migrate();

        (await _db.Entities.AnyAsync(e => e.SourceId == "person:Jashar_1")).Should().BeFalse();
        (await _db.EntityVerses.AnyAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task The_fast_is_renamed_as_the_loader_renames_it()
    {
        var renaming = BibleDataLoader.Misnamed["Judahs_fast_during_Av"];
        _db.Events.Add(new Event
        {
            Slug = "judahsfastduringav", Name = renaming.Was, Notes = "[Ussher date p96]", Source = Dataset,
            Realm = Realms.Scripture,
        });
        await _db.SaveChangesAsync();

        await Migrate();
        await Migrate();

        var fast = await _db.Events.AsNoTracking().SingleAsync();
        fast.Name.Should().Be(renaming.Name);
        fast.NameSource.Should().Be(EventNames.Generated);
        fast.Notes.Should().Be($"{renaming.Why} [Ussher date p96]", "a second run finds nothing left to rename");
    }

    [Fact]
    public async Task Levi_s_six_are_his_descendants_as_the_loader_reads_them()
    {
        var levi = Person("levi", "person:Levi_1");
        var miniamin = Person("miniamin", "person:Miniamin_1");
        var gershon = Person("gershon", "person:Gershon_1");
        await _db.SaveChangesAsync();
        const string note = "inferred that these are Levites serving their brothers";
        Tie(levi, "father", miniamin, note);
        Tie(miniamin, "son", levi, note);
        Tie(levi, "father", gershon, null);
        await _db.SaveChangesAsync();

        await Migrate();

        var rows = await _db.EntityRelationships.AsNoTracking().ToListAsync();
        var expected = BibleDataLoader.Restated[("Levi_1", "father", "Miniamin_1")];
        rows.Single(r => r.FromEntityId == levi.Id && r.ToEntityId == miniamin.Id).Type.Should().Be(expected.Type);
        rows.Single(r => r.FromEntityId == miniamin.Id).Type.Should().Be("descendant");
        rows.Single(r => r.FromEntityId == miniamin.Id).Notes
            .Should().Be($"{BibleDataLoader.Stopped(note)} {expected.Why}");
        rows.Single(r => r.ToEntityId == gershon.Id).Type.Should().Be("father", "Gershon is Levi's son");
    }

    [Fact]
    public async Task Solomon_s_temple_is_one_event_under_two_datasets()
    {
        _db.Events.AddRange(
            new Event
            {
                Slug = "beginfirsttempleconstruction", Name = "First Temple construction began",
                YearFromCreation = 2995, Notes = "[Ussher date p67]", Source = Dataset, Realm = Realms.Scripture,
            },
            new Event
            {
                Slug = "solomonstemple", Name = "Solomon's Temple", YearFromCreation = 2966, Uri = SolomonsTemple,
                Source = WorldSource, Realm = Realms.World,
            });
        await _db.SaveChangesAsync();

        await Migrate();
        await Migrate();

        var left = await _db.Events.AsNoTracking().SingleAsync();
        left.Slug.Should().Be("beginfirsttempleconstruction");
        left.Notes.Should().Be(
            "[Ussher date p67] Wikidata has this event as \"Solomon's Temple\" at 996 BCE (" + SolomonsTemple +
            "), where this row holds 967 BCE. One event under two datasets, 29 years apart — so world history " +
            "draws no second mark for it and the disagreement is here.");
        WorldHistoryLoader.AlreadyInScripture[SolomonsTemple].Should().Be(left.Slug);
    }

    [Fact]
    public async Task A_claim_no_longer_shows_the_dataset_s_row_identifier()
    {
        var david = Person("david", "person:David_1");
        var neco = Person("neco", "person:Pharaoh Neco_1");
        var philip = Person("philip-3", "essenthos:philip3");
        const string stay = ", which is where this record's relationships, verses and descriptors come from and whose they stay";
        david.Claims.Add(new EntityClaim { Method = LinkMethod.StatedBySource, Source = Dataset, Note = "holds this man as person:David_1" + stay });
        neco.Claims.Add(new EntityClaim { Method = LinkMethod.StatedBySource, Source = Dataset, Note = "holds this one as person:Pharaoh Neco_1" + stay });
        philip.Claims.Add(new EntityClaim
        {
            Method = LinkMethod.StatedBySource, Source = Dataset,
            Note = "files LUK 3:1 under person:Philip_2, a record it also gives another man of this name; the verse is this man's.",
        });
        await _db.SaveChangesAsync();

        await Migrate();

        var notes = await _db.EntityClaims.AsNoTracking().Select(c => c.Note!).ToListAsync();
        notes.Should().BeEquivalentTo(
            "holds this man as a record of its own" + stay,
            "holds this one as a record of its own" + stay,
            "files LUK 3:1 under a record it also gives another man of this name; the verse is this man's.");
    }

    /// <summary>The migration's statements, run in one transaction as the migrator runs them.</summary>
    private async Task Migrate()
    {
        var builder = new MigrationBuilder(_db.Database.ProviderName);
        typeof(TheDatasetsCorrectionsReachALoadedCorpus)
            .GetMethod("Up", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(new TheDatasetsCorrectionsReachALoadedCorpus(), [builder]);

        await using var connection = _database.NewConnection();
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        foreach (var operation in builder.Operations.OfType<SqlOperation>())
        {
            await using var command = new NpgsqlCommand(operation.Sql, connection, transaction);
            await command.ExecuteNonQueryAsync();
        }

        await transaction.CommitAsync();
        _db.ChangeTracker.Clear();
    }

    private Entity Person(string slug, string sourceId)
    {
        var person = new Entity { Kind = EntityKind.Person, Slug = slug, Name = slug, SourceId = sourceId, Source = Dataset };
        _db.Entities.Add(person);
        return person;
    }

    private void Tie(Entity from, string type, Entity to, string? notes) =>
        _db.EntityRelationships.Add(new EntityRelationship
        {
            FromEntityId = from.Id, ToEntityId = to.Id, Type = type, Category = "inferred",
            CanonicalBook = 14, CanonicalChapter = 31, CanonicalVerse = 15,
            Method = LinkMethod.StatedBySource, Source = Dataset, Notes = notes,
        });
}
