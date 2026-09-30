using Essenthos.Core.Corpus;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Loading.Encyclopedia;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace Essenthos.Core.Tests;

/// <summary>
/// <c>scripts/records-resting-on-the-dataset.sql</c> lists exactly the records a dataset supplied that
/// the rule does not make ours: what the query reads as resting on the dataset and what the API credits
/// as ours are the two halves of one set.
/// </summary>
[Collection(WitnessDatabaseCollection.Name)]
public sealed class RecordsRestingOnTheDatasetTests : IDisposable
{
    private const string Script = "records-resting-on-the-dataset.sql";

    private readonly AppDbContext _db;

    public RecordsRestingOnTheDatasetTests(WitnessDatabase database)
    {
        _db = database.NewContext();
        _db.Database.ExecuteSqlRaw("DELETE FROM entity");
    }

    public void Dispose()
    {
        _db.Database.ExecuteSqlRaw("DELETE FROM entity");
        _db.Dispose();
    }

    [Fact]
    public async Task TheQueryListsTheDatasetsRecordsTheRuleDoesNotMakeOurs()
    {
        var (byVerse, byTie, byTieInward, placed, tiedByDataset) = (
            Record("by-verse"), Record("by-tie"), Record("by-tie-inward"), Record("placed"), Record("tied-by-dataset"));
        Record("bare");
        var own = Record("own", "Essenthos, a record of our own");
        await _db.SaveChangesAsync();
        Verse(byVerse, "Essenthos, from the words this corpus annotates to the person or the place they name");
        Verse(placed, BibleDataLoader.Source);
        Verse(placed, "OpenBible.info Bible Geocoding, github.com/openbibleinfo/Bible-Geocoding-Data, CC BY 4.0");
        Tie(byTie, own, "Essenthos, read from the verse by a test");
        Tie(own, byTieInward, "Essenthos, read from the verse by a test");
        Tie(tiedByDataset, own, BibleDataLoader.Source);
        await _db.SaveChangesAsync();

        var listed = await Resting();
        var ours = await OursOnlyRecords.Among(
            _db, ["by-verse", "by-tie", "by-tie-inward", "placed", "tied-by-dataset", "bare", "own"], default);

        listed.Should().Equal("bare", "placed", "tied-by-dataset");
        ours.Keys.Should().BeEquivalentTo(["by-verse", "by-tie", "by-tie-inward"]);
    }

    private async Task<List<string>> Resting()
    {
        var sql = await File.ReadAllTextAsync(Path.Combine(Repository(), "scripts", Script));
        var query = sql[sql.IndexOf("SELECT e.slug", StringComparison.Ordinal)..];
        await _db.Database.OpenConnectionAsync();
        await using var command = new NpgsqlCommand(query, (NpgsqlConnection)_db.Database.GetDbConnection());
        await using var reader = await command.ExecuteReaderAsync();
        var slugs = new List<string>();
        while (await reader.ReadAsync())
        {
            slugs.Add(reader.GetString(0));
        }

        return slugs;
    }

    private static string Repository()
    {
        for (var folder = new DirectoryInfo(AppContext.BaseDirectory); folder is not null; folder = folder.Parent)
        {
            if (File.Exists(Path.Combine(folder.FullName, "scripts", Script)))
            {
                return folder.FullName;
            }
        }

        throw new FileNotFoundException($"No scripts/{Script} above {AppContext.BaseDirectory}.");
    }

    private Entity Record(string slug, string source = BibleDataLoader.Source)
    {
        var entity = new Entity
        {
            Kind = EntityKind.Person, Slug = slug, Name = slug, SourceId = slug, Source = source,
            Distinguisher = "a line only BibleData states",
        };
        _db.Entities.Add(entity);
        return entity;
    }

    private void Verse(Entity entity, string source) =>
        _db.EntityVerses.Add(new EntityVerse
        {
            EntityId = entity.Id, CanonicalBook = 1, CanonicalChapter = 22, CanonicalVerse = 14, Source = source,
        });

    private void Tie(Entity from, Entity to, string source) =>
        _db.EntityRelationships.Add(new EntityRelationship
        {
            FromEntityId = from.Id, ToEntityId = to.Id, Type = "son-of", Category = RelationshipCategories.Read,
            CanonicalBook = 1, CanonicalChapter = 22, CanonicalVerse = 14, Method = LinkMethod.Manual, Source = source,
        });
}
