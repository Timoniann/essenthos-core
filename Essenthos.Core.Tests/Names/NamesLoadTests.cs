using Essenthos.Core.Conversion;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Names;
using Essenthos.Core.Products;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace Essenthos.Core.Tests.Names;

/// <summary>
/// The names projection into <c>word_entity</c> (DOC-0224 §7.3, D5): every row with its role and origin, the base's
/// reading beside a differs on the same word kept as a row of its own, and a carried name's note naming its word.
/// </summary>
[Collection(WitnessDatabaseCollection.Name)]
public sealed class NamesLoadTests : IDisposable
{
    private readonly WitnessDatabase _database;
    private readonly AppDbContext _db;
    private readonly string _resources = Path.Combine(Path.GetTempPath(), "essenthos-products", Guid.NewGuid().ToString("N"));

    public NamesLoadTests(WitnessDatabase database)
    {
        _database = database;
        _db = database.NewContext();
        Clear();
    }

    public void Dispose()
    {
        Clear();
        _db.Dispose();
        if (Directory.Exists(_resources))
        {
            Directory.Delete(_resources, recursive: true);
        }
    }

    [Fact]
    public async Task EveryRowKeepsItsRoleAndOriginAndTheBaseReadingStandsBesideTheDiffers()
    {
        Corpus.Add(_db, "RUSV", TextKind.Translation, "rus", (1, 11, ["Иоакима", "Израиля"]));
        Corpus.Add(_db, "BHSA", TextKind.CriticalEdition, "hbo", (1, 11, ["יכניה", "ישראל"]));
        foreach (var slug in new[] { "eliakim-2", "jehoiachin", "israelites" })
        {
            _db.Entities.Add(new Entity { Kind = EntityKind.Person, Slug = slug, Name = slug });
        }

        await _db.SaveChangesAsync();
        var store = new ProductStore(_resources);
        var russian = await Words(store, "RUSV");
        var hebrew = await Words(store, "BHSA");
        var first = new WordKey("RUSV", new VerseKey(1, 1, 11), 1);
        var second = new WordKey("RUSV", new VerseKey(1, 1, 11), 2);
        using (var writer = store.Create(NameProducts.Projected("RUSV"), ProductKind.Machine, ProjectedName.Schema("RUSV"), "test", []))
        {
            writer.WriteAll([
                new ProjectedName(first, "jehoiachin", NameRoles.Name, NameOrigins.BaseReading, "BHSA 1.1:11.1", "strong-number", 0.9, "a test", "stated"),
                new ProjectedName(first, "eliakim-2", NameRoles.Name, NameOrigins.Differs, null, "manual", null, "a test"),
                new ProjectedName(second, "israelites", NameRoles.People, NameOrigins.Projected, "BHSA 1.1:11.2", "strong-number", 0.9, "a test", "stated"),
            ]);
        }

        var outcome = await NamesLoad.Run(_database.NewConnection().ConnectionString, store, ["RUSV"], CancellationToken.None);

        outcome.Rows.Should().Be(3);
        outcome.ReadBack!.MissingFromTable.Should().Be(0);
        (await Rows()).Should().BeEquivalentTo([
            (russian[0], "jehoiachin", "name", "base-reading", $"through BHSA word {hebrew[0]}"),
            (russian[0], "eliakim-2", "name", "differs", (string?)null),
            (russian[1], "israelites", "people", "projected", $"through BHSA word {hebrew[1]}"),
        ]);
        (await _db.WordEntities.Select(a => a.Entity!.Slug).ToListAsync()).Should().BeEquivalentTo(["eliakim-2", "israelites"],
            "a word's names are its own; the base's reading is asked for apart");
        (await _db.WordEntities.IgnoreQueryFilters().CountAsync()).Should().Be(3);
    }

    [Fact]
    public async Task ReplacingOneTextsNamesKeepsEveryOtherTexts()
    {
        Corpus.Add(_db, "RUSV", TextKind.Translation, "rus", (1, 11, ["Иоакима", "Израиля"]));
        Corpus.Add(_db, "BHSA", TextKind.CriticalEdition, "hbo", (1, 11, ["יכניה", "ישראל"]));
        foreach (var slug in new[] { "jehoiachin", "israelites" })
        {
            _db.Entities.Add(new Entity { Kind = EntityKind.Person, Slug = slug, Name = slug });
        }

        await _db.SaveChangesAsync();
        var store = new ProductStore(_resources);
        var russian = await Words(store, "RUSV");
        var hebrew = await Words(store, "BHSA");
        void Project(string text, params ProjectedName[] names)
        {
            using var writer = store.Create(NameProducts.Projected(text), ProductKind.Machine, ProjectedName.Schema(text), "test", []);
            writer.WriteAll(names);
        }

        WordKey Key(string text, int position) => new(text, new VerseKey(1, 1, 11), position);
        Project("BHSA", new ProjectedName(Key("BHSA", 1), "jehoiachin", NameRoles.Name, NameOrigins.Settled, null, "manual", null, "a test"));
        Project("RUSV", new ProjectedName(Key("RUSV", 1), "jehoiachin", NameRoles.Name, NameOrigins.Projected, null, "manual", null, "a test"));
        var connection = _database.NewConnection().ConnectionString;
        await NamesLoad.Run(connection, store, ["BHSA", "RUSV"], CancellationToken.None);

        Project("RUSV", new ProjectedName(Key("RUSV", 2), "israelites", NameRoles.People, NameOrigins.Projected, null, "manual", null, "a test"));
        var outcome = await NamesLoad.Replace(connection, store, ["RUSV"], CancellationToken.None);

        outcome.Rows.Should().Be(1);
        (await Rows()).Select(r => (r.Word, r.Record)).Should().BeEquivalentTo([(hebrew[0], "jehoiachin"), (russian[1], "israelites")],
            "only the moved text's rows are replaced (PRB-1484)");
    }

    /// <summary>The text's words as its words product, each word renumbered in the database with the id its key gives it, as a build does.</summary>
    private async Task<long[]> Words(ProductStore store, string slug)
    {
        var words = await _db.Words.Where(w => w.Text!.Slug == slug).OrderBy(w => w.Position).AsNoTracking().ToListAsync();
        var keys = words.Select((_, at) => new WordKey(slug, new VerseKey(1, 1, 11), at + 1)).ToList();
        using (var writer = store.Create(TextWord.ProductOf(slug), ProductKind.Machine, TextWord.Schema(slug), "test", []))
        {
            writer.WriteAll(words.Select((word, at) => new TextWord(keys[at], word.Surface, " ", null, null, null, null, null, null, false, null, null, null)));
        }

        for (var at = 0; at < words.Count; at++)
        {
            await _db.Database.ExecuteSqlRawAsync("UPDATE word SET id = {0} WHERE id = {1}", WordIds.Of(keys[at]), words[at].Id);
        }

        return [.. keys.Select(key => WordIds.Of(key))];
    }

    private async Task<List<(long Word, string Record, string? Role, string? Origin, string? Note)>> Rows()
    {
        await using var connection = _database.NewConnection();
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "SELECT a.word_id, e.slug, a.role, a.origin, a.note FROM word_entity a JOIN entity e ON e.id = a.entity_id", connection);
        await using var reader = await command.ExecuteReaderAsync();
        var rows = new List<(long, string, string?, string?, string?)>();
        while (await reader.ReadAsync())
        {
            rows.Add((reader.GetInt64(0), reader.GetString(1), reader.IsDBNull(2) ? null : reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3), reader.IsDBNull(4) ? null : reader.GetString(4)));
        }

        return rows;
    }

    private void Clear()
    {
        _db.Database.ExecuteSqlRaw("DELETE FROM text");
        _db.Database.ExecuteSqlRaw("DELETE FROM entity");
    }
}
