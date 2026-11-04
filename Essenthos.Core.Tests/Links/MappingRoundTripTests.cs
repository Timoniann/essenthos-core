using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Loading.Links;
using Essenthos.Core.Mapping;
using Essenthos.Core.Products;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Xunit;

namespace Essenthos.Core.Tests.Links;

/// <summary>
/// The links of a database written out as method files, merged, loaded back and written out again: the same rows
/// with the same keys and claims under the ids the final derived, except what the merge rule withdrew, which stays in its
/// file marked.
/// </summary>
[Collection(WitnessDatabaseCollection.Name)]
public sealed class MappingRoundTripTests : IDisposable
{
    private const string Berean = "Berean Standard Bible translation tables";

    private readonly WitnessDatabase _database;
    private readonly AppDbContext _db;
    private readonly string _root = Path.Combine(Path.GetTempPath(), "essenthos-products", Guid.NewGuid().ToString("N"));

    public MappingRoundTripTests(WitnessDatabase database)
    {
        _database = database;
        _db = database.NewContext();
        _db.Database.ExecuteSqlRaw("DELETE FROM text");
        Corpus.Add(_db, "KJV", TextKind.Translation, "eng", (1, 1, ["In", "the", "beginning", "God"]));
        Corpus.Add(_db, "BHSA", TextKind.CriticalEdition, "hbo", (1, 1, ["בְּ", "רֵאשִׁית", "אֱלֹהִים"]));
        _db.SaveChanges();
    }

    public void Dispose()
    {
        _db.Database.ExecuteSqlRaw("DELETE FROM text");
        _db.Dispose();
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, true);
        }
    }

    [Fact]
    public async Task LinksComeBackWithTheirKeysAndClaimsUnderTheFinalsIdsAndAWithdrawnRowStaysInItsFile()
    {
        var kjv = await Words("KJV");
        var bhsa = await Words("BHSA");
        var (from, to) = (await Text("KJV"), await Text("BHSA"));
        await using (var connection = _database.NewConnection())
        {
            await connection.OpenAsync();
            await LinkWriter.Write(connection, null,
            [
                new NewLink(from, to, LinkRelation.Renders, LinkMethod.StatedBySource, null, Berean, null, [kjv[0], kjv[1], kjv[2]], [bhsa[0], bhsa[1]]),
                new NewLink(from, to, LinkRelation.Renders, LinkMethod.StrongNumber, 0.9, "Zefania KJV+ Strong numbers", null, [kjv[3]], [bhsa[2]]),
                new NewLink(from, to, LinkRelation.Renders, LinkMethod.Aligner, 0.98, "SIL.Machine, aligned as written", "a note that stays in the file", [kjv[3]], [bhsa[2]]),
                new NewLink(from, to, LinkRelation.Renders, LinkMethod.Aligner, 0.5, "SIL.Machine, aligned as stems", null, [kjv[3]], [bhsa[1]]),
            ], CancellationToken.None);
            await using var role = new NpgsqlCommand(
                "UPDATE link_word SET role = 'attached', head_word_id = @head WHERE word_id = @word AND side = 'from'", connection);
            role.Parameters.AddWithValue("head", kjv[2]);
            role.Parameters.AddWithValue("word", kjv[1]);
            await role.ExecuteNonQueryAsync();
        }

        var connectionString = _database.NewConnection().ConnectionString;
        var store = new ProductStore(Path.Combine(_root, "first"));
        await MappingExport.Run(connectionString, store, null, 1, NullLogger.Instance, CancellationToken.None);
        var merged = MappingFiles.Merge(store, "KJV-BHSA", force: false);
        var before = await Stored();

        var outcome = await MappingLoad.Run(connectionString, store, NullLogger.Instance, CancellationToken.None);
        var after = await Stored();
        var again = new ProductStore(Path.Combine(_root, "again"));
        await MappingExport.Run(connectionString, again, null, 1, NullLogger.Instance, CancellationToken.None);

        merged.Links.Should().Be(2);
        outcome.Unresolved.Should().Be(0);
        Rows(store, "composed").Should().ContainSingle(r => r.Status == "withdrawn-by strong").Which.To.Should().Be("1.1:1.2");
        after.Should().BeEquivalentTo(before.Where(r => !r.Contains("aligned as stems")),
            "every link the merge kept is loaded with its words, roles and claims");
        using (var final = store.Open(MappingProducts.Of("KJV-BHSA", MappingProducts.Final), FinalRow.Schema))
        {
            (await Ids()).Should().BeEquivalentTo(final.ReadAll().Select(r => r.Id).Distinct(), "a link is loaded under the id its final derived");
        }

        Rows(again, "stated").Should().Equal(Rows(store, "stated"));
        Rows(again, "strong").Should().Equal(Rows(store, "strong"));
        Rows(again, "composed").Should().ContainSingle().Which.Should().Be(Rows(store, "composed").Single(r => r.Status == null) with { Evidence = null },
            "a provenance's note is evidence, kept in the method file and not in the database");
        Rows(store, "stated")[0].Roles.Should().Be("1.1:1.2=attached@1.1:1.3");
    }

    private static List<MappingRow> Rows(ProductStore store, string method)
    {
        using var reader = store.Open(MappingProducts.Of("KJV-BHSA", method), MappingRow.Schema);
        return reader.ReadAll();
    }

    private async Task<List<long>> Ids()
    {
        await using var connection = _database.NewConnection();
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("SELECT id FROM link", connection);
        var ids = new List<long>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            ids.Add(reader.GetInt64(0));
        }

        return ids;
    }

    private async Task<List<string>> Stored()
    {
        await using var connection = _database.NewConnection();
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
            SELECT concat_ws(' | ', l.relation, l.method, l.confidence, p.source, l.fingerprint,
                   (SELECT string_agg(concat_ws(':', w.word_id, w.side, w.role, w.head_word_id), ',' ORDER BY w.side, w.word_id) FROM link_word w WHERE w.link_id = l.id),
                   (SELECT string_agg(concat_ws(':', c.method, c.confidence, cp.source), ',' ORDER BY c.method, cp.source)
                    FROM link_claim c JOIN provenance cp ON cp.id = c.provenance_id WHERE c.link_id = l.id))
            FROM link l JOIN provenance p ON p.id = l.provenance_id
            """, connection);
        var rows = new List<string>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            rows.Add(reader.GetString(0));
        }

        return rows;
    }

    private async Task<int> Text(string slug) => await _db.Texts.Where(t => t.Slug == slug).Select(t => t.Id).SingleAsync();

    private async Task<long[]> Words(string slug) =>
        await _db.Words.Where(w => w.Text!.Slug == slug).OrderBy(w => w.Position).Select(w => w.Id).ToArrayAsync();
}
