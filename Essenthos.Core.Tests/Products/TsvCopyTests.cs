using Essenthos.Core.Products;
using Essenthos.Core.Products.Tsv;
using FluentAssertions;
using Npgsql;
using Xunit;

namespace Essenthos.Core.Tests.Products;

/// <summary>
/// The claim that a product's body is what <c>COPY</c> reads is a claim about Postgres, so it is
/// asked of Postgres: the body of a written product goes through <c>COPY FROM STDIN</c> as it
/// stands and every value, null and escaped character included, comes back out the same.
/// </summary>
[Collection(WitnessDatabaseCollection.Name)]
public sealed class TsvCopyTests(WitnessDatabase database) : IDisposable
{
    private sealed record Row(string Own, string Surface, string? Lemma, long Count, bool Flag, string? Morphology);

    private static readonly ProductSchema<Row> Schema = new ProductSchema<Row>()
        .Text("own", row => row.Own)
        .Text("surface", row => row.Surface)
        .OptionalText("lemma", row => row.Lemma)
        .Integer("count", row => row.Count)
        .Boolean("flag", row => row.Flag)
        .OptionalJson("morphology", row => row.Morphology)
        .Reads(row => new Row(row.Text(0), row.Text(1), row.TextOrNull(2), row.Integer(3), row.Boolean(4), row.JsonOrNull(5)));

    private static readonly Row[] Rows =
    [
        new("1.1:1.1", "In", "in", 1, true, """{"pos": "prep"}"""),
        new("1.1:1.2", "tab\there", null, -2, false, null),
        new("1.1:1.3", "line\nbreak", "back\\slash", long.MaxValue, true, """{"a": "\\n"}"""),
        new("1.1:1.4", "return\rcarriage", "", 0, false, "[]"),
        new("1.31:50a.1", "בְּרֵאשִׁית", "Ἐν ἀρχῇ", 7, true, """{"he": "א"}"""),
        new("1.1:1.5", @"\N", @"\N", 5, false, null),
    ];

    private readonly string _folder = Path.Combine(Path.GetTempPath(), "essenthos-products", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task TheBodyGoesThroughCopyFromStdinAndComesBackTheSame()
    {
        var path = Path.Combine(_folder, "words.tsv");
        using (var writer = TsvFormat.Instance.Create(path, new ProductHeader("Essenthos/test/words.tsv", [], "test"), Schema))
        {
            writer.WriteAll(Rows);
        }

        await using var connection = database.NewConnection();
        await connection.OpenAsync();
        await using (var create = new NpgsqlCommand(
                         "CREATE TEMP TABLE copied (own text NOT NULL, surface text NOT NULL, lemma text, count bigint NOT NULL, flag boolean NOT NULL, morphology jsonb)",
                         connection))
        {
            await create.ExecuteNonQueryAsync();
        }

        using (var body = TsvFormat.OpenBody(path, out _, out var columns))
        await using (var import = await connection.BeginTextImportAsync($"COPY copied ({string.Join(", ", columns)}) FROM STDIN"))
        {
            await import.WriteAsync(await body.ReadToEndAsync());
        }

        var copied = new List<Row>();
        await using (var select = new NpgsqlCommand("SELECT own, surface, lemma, count, flag, morphology::text FROM copied ORDER BY count", connection))
        await using (var reader = await select.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                copied.Add(new Row(
                    reader.GetString(0),
                    reader.GetString(1),
                    reader.IsDBNull(2) ? null : reader.GetString(2),
                    reader.GetInt64(3),
                    reader.GetBoolean(4),
                    reader.IsDBNull(5) ? null : reader.GetString(5)));
            }
        }

        copied.Should().BeEquivalentTo(Rows, "Postgres read every value as the writer meant it");
    }

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }
}
