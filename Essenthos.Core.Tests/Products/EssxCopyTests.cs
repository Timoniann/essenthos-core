using Essenthos.Core.Products;
using Essenthos.Core.Products.Essx;
using Essenthos.Core.Products.Postgres;
using FluentAssertions;
using Npgsql;
using Xunit;

namespace Essenthos.Core.Tests.Products;

/// <summary>
/// The binary load is a claim about what Postgres receives, so it is asked of Postgres: an ESSX
/// file of every column type goes in over several streams, in binary and in text, and comes back
/// row for row, the key spread over its parts and the file's text as a constant column.
/// </summary>
[Collection(WitnessDatabaseCollection.Name)]
public sealed class EssxCopyTests(WitnessDatabase database) : IDisposable
{
    private sealed record Row(WordKey Key, VerseKey? Cited, string Surface, string? Lemma, long Count, bool Flag, bool? Maybe, string? Morphology);

    private static readonly ProductSchema<Row> Schema = new ProductSchema<Row>()
        .OwnWordKey("own", row => row.Key)
        .OptionalVerseKey("cited", row => row.Cited)
        .Text("surface", row => row.Surface)
        .OptionalText("lemma", row => row.Lemma)
        .Integer("count", row => row.Count)
        .Boolean("flag", row => row.Flag)
        .OptionalBoolean("maybe", row => row.Maybe)
        .OptionalJson("morphology", row => row.Morphology)
        .Reads(row => new Row(row.WordKey(0, "KJV"), row.VerseKeyOrNull(1), row.Text(2), row.TextOrNull(3), row.Integer(4), row.Boolean(5), row.BooleanOrNull(6), row.JsonOrNull(7)));

    private static Row Make(int i) => new(
        new WordKey("KJV", new VerseKey(1 + i / 3000, 1 + i / 100 % 30, i % 1000 == 0 ? "a" : "", 1 + i / 10 % 10, i % 7 == 0 ? "a" : ""), 1 + i % 10),
        i % 3 == 0 ? null : new VerseKey(19, 4, 1 + i % 9),
        i % 5 == 0 ? "tab\there \\ \n" : "w" + i % 50,
        i % 4 == 0 ? null : "l" + i % 20,
        (long)i * 7 - 3,
        i % 2 == 0,
        i % 3 == 0 ? null : i % 3 == 1,
        i % 6 == 0 ? null : $$"""{"i": {{i}}}""");

    private readonly string _folder = Path.Combine(Path.GetTempPath(), "essenthos-products", Guid.NewGuid().ToString("N"));

    [Theory]
    [InlineData(CopyMode.Binary, 1)]
    [InlineData(CopyMode.Binary, 4)]
    [InlineData(CopyMode.Text, 3)]
    public async Task EveryRowComesBackThroughCopy(CopyMode mode, int streams)
    {
        Directory.CreateDirectory(_folder);
        var path = Path.Combine(_folder, "words.essx");
        var rows = Enumerable.Range(0, 9_000).Select(Make).ToList();
        using (var writer = new EssxFormat(new EssxOptions { RowGroupRows = 1000 }).Create(path, new ProductHeader("Essenthos/texts/KJV/words.essx", [], "test"), Schema))
        {
            writer.WriteAll(rows);
        }

        var table = $"copied_{mode}_{streams}".ToLowerInvariant();
        await using var connection = database.NewConnection();
        await connection.OpenAsync();
        await Execute(connection, $"DROP TABLE IF EXISTS {table}");
        await Execute(connection, $"""
            CREATE TABLE {table} (
                text text NOT NULL, own_book int NOT NULL, own_chapter int NOT NULL, own_chapter_label text NOT NULL, own_verse int NOT NULL, own_verse_label text NOT NULL, own_position smallint NOT NULL,
                cited_book int, cited_chapter int, cited_chapter_label text, cited_verse int, cited_verse_label text,
                surface text NOT NULL, lemma text, count bigint NOT NULL, flag boolean NOT NULL, maybe boolean, morphology jsonb)
            """);

        using var reader = EssxReader.Open(path);
        var columns = EssxCopy.DefaultColumns(reader.Tables[0], ("text", "KJV"));
        // An opened connection no longer shows its password; the loader opens its own, so it is given the string of a fresh one.
        var result = await EssxCopy.Load(database.NewConnection().ConnectionString, path, table, columns, streams, mode);
        result.Rows.Should().Be(rows.Count);

        var copied = new List<Row>();
        await using (var select = new NpgsqlCommand($"SELECT text, own_book, own_chapter, own_chapter_label, own_verse, own_verse_label, own_position, cited_book, cited_chapter, cited_chapter_label, cited_verse, cited_verse_label, surface, lemma, count, flag, maybe, morphology::text FROM {table} ORDER BY count", connection))
        await using (var data = await select.ExecuteReaderAsync())
        {
            while (await data.ReadAsync())
            {
                data.GetString(0).Should().Be("KJV");
                copied.Add(new Row(
                    new WordKey("KJV", new VerseKey(data.GetInt32(1), data.GetInt32(2), data.GetString(3), data.GetInt32(4), data.GetString(5)), data.GetInt16(6)),
                    data.IsDBNull(7) ? null : new VerseKey(data.GetInt32(7), data.GetInt32(8), data.GetString(9), data.GetInt32(10), data.GetString(11)),
                    data.GetString(12),
                    data.IsDBNull(13) ? null : data.GetString(13),
                    data.GetInt64(14),
                    data.GetBoolean(15),
                    data.IsDBNull(16) ? null : data.GetBoolean(16),
                    data.IsDBNull(17) ? null : data.GetString(17)));
            }
        }

        copied.Should().Equal(rows);
        await Execute(connection, $"DROP TABLE {table}");
    }

    private static async Task Execute(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }
}
