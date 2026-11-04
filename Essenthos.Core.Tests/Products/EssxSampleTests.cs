using System.Text.Json;
using System.Text.Json.Nodes;
using Essenthos.Core.Products;
using Essenthos.Core.Products.Essx;
using FluentAssertions;
using Xunit;

namespace Essenthos.Core.Tests.Products;

/// <summary>
/// The sample file in <c>docs/essx-sample/</c>, which any reader of the format (the browser's among them)
/// is tested against: <c>sample.essx</c> must read as <c>sample.json</c> says, and the writer must still
/// write those rows. It has two tables, row groups of four, nulls in every type, a width-8 integer, a
/// text column, deflated and stored chunks, and a dictionary that reaches its cap.
/// </summary>
public sealed class EssxSampleTests : IDisposable
{
    public sealed record Word(WordKey Key, string Surface, string? Lemma, long? Count, bool Flag, bool? Maybe, string? Morphology, string? Note);

    public sealed record Verse(VerseKey Key, VerseKey? Cited, string Gloss);

    public static readonly ProductSchema<Word> Words = new ProductSchema<Word>()
        .OwnWordKey("own", w => w.Key)
        .Text("surface", w => w.Surface)
        .OptionalText("lemma", w => w.Lemma)
        .OptionalInteger("count", w => w.Count)
        .Boolean("flag", w => w.Flag)
        .OptionalBoolean("maybe", w => w.Maybe)
        .OptionalJson("morphology", w => w.Morphology)
        .OptionalText("note", w => w.Note)
        .Reads(_ => throw new NotSupportedException());

    public static readonly ProductSchema<Verse> Verses = new ProductSchema<Verse>()
        .VerseKey("own", v => v.Key)
        .OptionalVerseKey("cited", v => v.Cited)
        .Text("gloss", v => v.Gloss)
        .Reads(_ => throw new NotSupportedException());

    public static readonly EssxOptions Options = new() { RowGroupRows = 4, Codec = EssxLayout.CodecDeflate, CompressFrom = 24, DictionaryLimit = 6 };

    private static readonly string[] Surfaces = ["In", "the", "beginning", "בְּרֵאשִׁית", "Ἐν", "tab\there"];

    private static readonly string[] Glosses = ["light", "life", "light", "life"];

    /// <summary>Writes the sample; the file in the repository is this method's output.</summary>
    public static void Write(string path)
    {
        var header = new ProductHeader("Essenthos/sample/sample.essx", [new ProductInput("sample/source.txt", new string('5', 64))], "EssxSampleTests.Write");
        header.Notes.Add(new("purpose", "the reference file every ESSX reader is tested against"));
        using var file = EssxWriter.Create(path, header, Options);
        var words = file.Table("words", Words);
        for (var i = 0; i < 10; i++)
        {
            var verse = i < 8 ? new VerseKey(1, 1, "", 1 + i / 4, "") : new VerseKey(19, 4, "a", 1, "b");
            words.Write(new Word(
                new WordKey("SAMPLE", verse, 1 + i % 4),
                Surfaces[i % Surfaces.Length],
                i % 3 == 0 ? null : Surfaces[(i + 1) % Surfaces.Length].ToLowerInvariant(),
                i switch { 2 => null, 5 => 5_000_000_000, 6 => -7, _ => i * 10 },
                i % 2 == 0,
                i % 3 == 0 ? null : i % 3 == 1,
                i % 4 == 1 ? null : $$"""{"i":{{i}},"pos":"{{(i % 2 == 0 ? "noun" : "verb")}}"}""",
                i % 5 == 4 ? null : $"a note long enough to be stored as text rather than a dictionary, number {i}"));
        }

        var verses = file.Table("verses", Verses);
        for (var i = 0; i < 12; i++)
        {
            verses.Write(new Verse(
                new VerseKey(43, 3, "", 14 + i, i == 11 ? "a" : ""),
                i % 2 == 0 ? null : new VerseKey(1, 1, "", i, ""),
                i < 4 ? Glosses[i] : $"gloss {i}"));
        }

        file.Complete();
    }

    /// <summary>Every table's columns and values as the JSON beside the sample states them: keys as text, integers as numbers, null as null.</summary>
    public static JsonObject Decode(string path)
    {
        using var reader = EssxReader.Open(path);
        var tables = new JsonArray();
        foreach (var table in reader.Tables)
        {
            var values = new JsonArray();
            foreach (var row in table.Rows())
            {
                var cells = new JsonArray();
                for (var c = 0; c < table.Columns.Count; c++)
                {
                    cells.Add(table.Columns[c].Type switch
                    {
                        _ when row.IsNull(c) => null,
                        ColumnType.Integer => JsonValue.Create(row.Integer(c)),
                        ColumnType.Boolean => JsonValue.Create(row.Boolean(c)),
                        _ => JsonValue.Create(row.Text(c)),
                    });
                }

                values.Add(cells);
            }

            tables.Add(new JsonObject
            {
                ["name"] = table.Name,
                ["rows"] = table.RowCount,
                ["columns"] = new JsonArray([.. table.Columns.Select(column => (JsonNode)new JsonObject
                {
                    ["name"] = column.Name,
                    ["type"] = column.Type.ToString().ToLowerInvariant(),
                    ["nullable"] = column.Nullable,
                })]),
                ["values"] = values,
            });
        }

        var product = reader.Footer.Product;
        return new JsonObject
        {
            ["version"] = reader.Footer.Version,
            ["product"] = new JsonObject
            {
                ["product"] = product.Product,
                ["producer"] = product.Producer,
                ["rows"] = product.Rows,
                ["inputs"] = new JsonArray([.. product.Inputs.Select(input => (JsonNode)new JsonObject { ["path"] = input.Path, ["sha256"] = input.Sha256 })]),
            },
            ["tables"] = tables,
        };
    }

    private static string Folder()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "docs", "essx-format.md")))
        {
            directory = directory.Parent;
        }

        return Path.Combine(directory?.FullName ?? throw new DirectoryNotFoundException("No docs/essx-format.md above the test assembly."), "docs", "essx-sample");
    }

    private readonly string _temp = Path.Combine(Path.GetTempPath(), "essenthos-products", Guid.NewGuid().ToString("N"));

    [Fact]
    public void TheSampleReadsAsItsJsonSaysAndPassesItsChecksum()
    {
        var sample = Path.Combine(Folder(), "sample.essx");
        var expected = JsonNode.Parse(File.ReadAllText(Path.Combine(Folder(), "sample.json")))!;
        JsonNode.DeepEquals(Decode(sample), expected).Should().BeTrue(Decode(sample).ToJsonString());
        using var reader = EssxReader.Open(sample);
        reader.Verify().Should().BeTrue();
    }

    [Fact]
    public void TheSampleHoldsWhatAReaderMustHandle()
    {
        using var reader = EssxReader.Open(Path.Combine(Folder(), "sample.essx"));
        var chunks = reader.Footer.Tables.SelectMany(t => t.RowGroups.SelectMany(g => g.Chunks)).ToList();
        chunks.Should().Contain(c => c.Codec == EssxLayout.CodecDeflate).And.Contain(c => c.Codec == EssxLayout.CodecNone);
        chunks.Should().Contain(c => c.Width == 8).And.Contain(c => c.Width == 0);
        chunks.Should().Contain(c => c.Encoding == "text", "the verses' gloss passes its dictionary's cap");
        reader.Footer.Tables.SelectMany(t => t.Physical).Select(p => p.Encoding).Should().Contain(["int", "bool", "dict", "text"]);
        reader.Footer.Tables[0].Physical.Single(p => p.Name == "own.verse_label").Dictionary!.Count.Should().Be(2);
    }

    [Fact]
    public void TheWriterStillWritesTheSampleRows()
    {
        Directory.CreateDirectory(_temp);
        var path = Path.Combine(_temp, "sample.essx");
        Write(path);
        var expected = JsonNode.Parse(File.ReadAllText(Path.Combine(Folder(), "sample.json")))!;
        JsonNode.DeepEquals(Decode(path), expected).Should().BeTrue();
    }

    public void Dispose()
    {
        if (Directory.Exists(_temp))
        {
            Directory.Delete(_temp, recursive: true);
        }
    }
}
