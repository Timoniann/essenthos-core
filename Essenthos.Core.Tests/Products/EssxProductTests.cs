using Essenthos.Core.Products;
using Essenthos.Core.Products.Essx;
using Essenthos.Core.Products.Tsv;
using FluentAssertions;
using Xunit;

namespace Essenthos.Core.Tests.Products;

public sealed class EssxProductTests : IDisposable
{
    private sealed record Row(WordKey Key, VerseKey? Cited, string Surface, string? Lemma, long Count, int? Rank, bool Flag, bool? Maybe, string Json, string? Long);

    private static readonly ProductSchema<Row> Schema = new ProductSchema<Row>()
        .Versioned(3)
        .OwnWordKey("own", row => row.Key)
        .OptionalVerseKey("cited", row => row.Cited)
        .Text("surface", row => row.Surface)
        .OptionalText("lemma", row => row.Lemma)
        .Integer("count", row => row.Count)
        .OptionalInteger("rank", row => row.Rank)
        .Boolean("flag", row => row.Flag)
        .OptionalBoolean("maybe", row => row.Maybe)
        .Json("json", row => row.Json)
        .OptionalText("long", row => row.Long)
        .Reads(row => new Row(
            row.WordKey("own", "KJV"), row.VerseKeyOrNull(1), row.Text(2), row.TextOrNull(3), row.Integer(4), row.Int32OrNull(5),
            row.Boolean(6), row.BooleanOrNull(7), row.Json(8), row.TextOrNull(9)));

    private static readonly string[] Surfaces = ["In", "the", "beginning", "God", "created", "tab\there", "line\nbreak", "בְּרֵאשִׁית", "Ἐν"];

    private static Row Make(int i) => new(
        new WordKey("KJV", new VerseKey(1 + i / 5000, 1 + i / 200 % 50, i % 7919 == 0 ? "a" : "", 1 + i / 10 % 20, i % 13 == 0 ? "a" : ""), 1 + i % 10),
        i % 3 == 0 ? null : new VerseKey(19, 4, i % 2 == 0 ? "a" : "", 1 + i % 9, ""),
        Surfaces[i % Surfaces.Length],
        i % 4 == 0 ? null : Surfaces[(i * 7) % Surfaces.Length].ToLowerInvariant(),
        i % 11 == 0 ? long.MinValue + i : (long)i * 1000 - 500,
        i % 5 == 0 ? null : -i,
        i % 2 == 0,
        i % 3 == 0 ? null : i % 3 == 1,
        $$"""{"i":{{i}}}""",
        i % 6 == 0 ? null : new string((char)('a' + i % 26), 40) + i);

    private static List<Row> Rows(int count) => [.. Enumerable.Range(0, count).Select(Make)];

    private readonly string _folder = Path.Combine(Path.GetTempPath(), "essenthos-products", Guid.NewGuid().ToString("N"));

    public EssxProductTests()
    {
        Directory.CreateDirectory(_folder);
    }

    private string File(string name) => Path.Combine(_folder, name);

    private static ProductHeader Header()
    {
        var header = new ProductHeader("Essenthos/test/rows.essx", [new ProductInput("a/b.txt", new string('0', 64))], "test");
        header.Notes.Add(new("tradition", "test"));
        header.Comments.Add("a free comment");
        return header;
    }

    [Theory]
    [InlineData(EssxLayout.CodecNone, 1 << 16)]
    [InlineData(EssxLayout.CodecDeflate, 1 << 16)]
    [InlineData(EssxLayout.CodecNone, 1000)]
    [InlineData(EssxLayout.CodecDeflate, 1000)]
    public void EveryTypeNullAndEscapedCharacterRoundTripsOverSeveralRowGroups(string codec, int groupRows)
    {
        var path = File("rows.essx");
        var rows = Rows(12_345);
        var format = new EssxFormat(new EssxOptions { Codec = codec, RowGroupRows = groupRows });
        using (var writer = format.Create(path, Header(), Schema))
        {
            writer.WriteAll(rows);
        }

        using var reader = format.Open(path, Schema);
        reader.Header!.Format().Should().Be(Header().Format(rows.Count));
        reader.Columns.Should().Equal(Schema.Columns);
        reader.ReadAll().Should().Equal(rows);

        using var file = EssxReader.Open(path);
        file.Verify().Should().BeTrue();
        file.Tables.Should().ContainSingle().Which.RowGroups.Should().Be((rows.Count + groupRows - 1) / groupRows);
        file.Tables[0].Info.SchemaVersion.Should().Be(3);
        file.Tables[0].Info.RowGroups.Sum(group => group.Rows).Should().Be(rows.Count);
        if (codec == EssxLayout.CodecDeflate)
        {
            file.Tables[0].Info.RowGroups[0].Chunks.Should().Contain(chunk => chunk.Codec == EssxLayout.CodecDeflate);
        }
    }

    [Fact]
    public void TheFileCarriesTheProductHeaderAndAnUntypedReaderGivesTexts()
    {
        var path = File("rows.essx");
        using (var writer = EssxFormat.Instance.Create(path, Header(), Schema))
        {
            writer.WriteAll(Rows(3));
        }

        using var reader = EssxFormat.Instance.Open(path);
        reader.Header!.Product.Should().Be("Essenthos/test/rows.essx");
        reader.Header.Rows.Should().Be(3);
        reader.Header.Notes.Should().BeEquivalentTo(new[] { new KeyValuePair<string, string>("tradition", "test") });
        reader.Header.Comments.Should().Equal("a free comment");
        var rows = reader.ReadAll();
        rows[0].Should().Equal("1.1a:1a.1", null, "In", null, long.MinValue.ToString(), null, "t", null, """{"i":0}""", null);
        rows[1].Should().Equal("1.1:1.2", "19.4:2", "the", Surfaces[7], "500", "-1", "f", "t", """{"i":1}""", new string('b', 40) + "1");
    }

    [Fact]
    public void RepeatedStringsAreOneInstanceAndKeysAreStoredAsParts()
    {
        var path = File("rows.essx");
        using (var writer = EssxFormat.Instance.Create(path, Header(), Schema))
        {
            writer.WriteAll(Rows(2000));
        }

        using var file = EssxReader.Open(path);
        var table = file.Tables[0];
        table.Info.Columns.First(column => column.Name == "own").Physical.Should().Equal("own.book", "own.chapter", "own.chapter_label", "own.verse", "own.verse_label", "own.position");
        table.Info.Physical.First(p => p.Name == "surface").Encoding.Should().Be("dict");
        table.Info.Physical.First(p => p.Name == "long").Encoding.Should().Be("text", "forty-character strings that never repeat are not worth a dictionary");
        table.Info.Physical.First(p => p.Name == "json").Encoding.Should().Be("dict", "short strings go through the dictionary even when distinct");
        table.Dictionary("surface").Should().HaveCount(Surfaces.Length + 1).And.HaveElementAt(0, null);

        var group = table.ReadGroup(0);
        var surface = (DictData)group.Column("surface");
        ReferenceEquals(surface.Text(0), surface.Text(Surfaces.Length)).Should().BeTrue("one string per distinct value");
        var key = (KeyData)group.Column("own");
        key.Book.Values[0].Should().Be(1);
        key.Position!.Values[1].Should().Be(2);
        table.Info.RowGroups[0].Chunks[0].Width.Should().Be(0, "a constant column costs no bytes");
    }

    [Fact]
    public void AFlippedByteIsCaughtByTheChecksumAndAnOutOfRangeChunkByTheReader()
    {
        var path = File("rows.essx");
        using (var writer = EssxFormat.Instance.Create(path, Header(), Schema))
        {
            writer.WriteAll(Rows(500));
        }

        var bytes = System.IO.File.ReadAllBytes(path);
        bytes[100] ^= 0xff;
        System.IO.File.WriteAllBytes(path, bytes);

        using var file = EssxReader.Open(path);
        file.Verify().Should().BeFalse();

        var truncated = File("short.essx");
        System.IO.File.WriteAllBytes(truncated, bytes[..200]);
        var open = () => EssxReader.Open(truncated);
        open.Should().Throw<ProductFormatException>();
    }

    [Fact]
    public void AFileOfSeveralTablesReadsEachByName()
    {
        var path = File("pair.essx");
        var verses = new ProductSchema<(VerseKey Own, int Words)>()
            .VerseKey("own", v => v.Own).Integer("words", v => v.Words)
            .Reads(row => (row.VerseKey(0), row.Int32(1)));
        using (var file = EssxWriter.Create(path, Header()))
        {
            var words = file.Table("words", Schema);
            var manifest = file.Table("verses", verses);
            words.WriteAll(Rows(10));
            manifest.Write((new VerseKey(1, 1, 1), 10));
            file.Complete();
        }

        using var reader = EssxReader.Open(path);
        reader.Tables.Select(table => table.Name).Should().Equal("words", "verses");
        reader.Table("verses").Rows(verses).Should().Equal((new VerseKey(1, 1, 1), 10));
        reader.Table("words").Rows(Schema).Should().Equal(Rows(10));
        reader.Header.Rows.Should().Be(10, "the first table's rows are the product's");
        var missing = () => reader.Table("links");
        missing.Should().Throw<ProductFormatException>().WithMessage("*words, verses*");
    }

    [Fact]
    public void RowGroupStatisticsLetAReaderSkipTheGroupsAVerseIsNotIn()
    {
        var path = File("rows.essx");
        using (var writer = new EssxFormat(new EssxOptions { RowGroupRows = 1000 }).Create(path, Header(), Schema))
        {
            writer.WriteAll(Rows(12_000));
        }

        using var file = EssxReader.Open(path);
        var table = file.Tables[0];
        var groups = table.GroupsWhere("own.book", (min, max) => min <= 2 && max >= 2).ToList();
        groups.Should().Equal([5, 6, 7, 8, 9], "rows 5000-9999 are book 2 and the groups hold 1000 rows");
        var found = groups.Select(table.ReadGroup).SelectMany(group => Enumerable.Range(0, group.Rows).Select(i => group.Column("own").WordKey(i, "KJV")!.Value))
            .Count(key => key.Verse.Book == 2);
        found.Should().Be(5000);
    }

    [Fact]
    public void AWriterDisposedWithoutCompletingLeavesThePreviousFileAndNoTemporary()
    {
        var path = File("rows.essx");
        using (var writer = EssxFormat.Instance.Create(path, Header(), Schema))
        {
            writer.WriteAll(Rows(1));
        }

        using (var writer = EssxFormat.Instance.Create(path, Header(), Schema))
        {
            writer.Write(Make(5));
        }

        Directory.GetFiles(_folder).Should().BeEquivalentTo(new[] { path });
        using var reader = EssxFormat.Instance.Open(path, Schema);
        reader.ReadAll().Should().Equal(Rows(1));
    }

    [Fact]
    public void TheSchemaMismatchIsNamedAsForTsv()
    {
        var path = File("rows.essx");
        var other = new ProductSchema<string>().Text("name", s => s).Reads(row => row.Text(0));
        using (var writer = EssxFormat.Instance.Create(path, Header(), other))
        {
            writer.WriteAll(["a"]);
        }

        var open = () => EssxFormat.Instance.Open(path, Schema);
        open.Should().Throw<ProductFormatException>().WithMessage("*column 1 is 'name' where the schema expects 'own'*");
        Directory.GetFiles(_folder).Should().BeEquivalentTo(new[] { path }, "the handle was released on the failed open");
    }

    [Fact]
    public void TheSameRowsReadTheSameFromTsvAndFromEssx()
    {
        var rows = Rows(3000);
        var tsv = File("rows.tsv");
        var essx = File("rows.essx");
        using (var writer = TsvFormat.Instance.Create(tsv, Header(), Schema)) writer.WriteAll(rows);
        using (var writer = EssxFormat.Instance.Create(essx, Header(), Schema)) writer.WriteAll(rows);

        using var fromTsv = TsvFormat.Instance.Open(tsv);
        using var fromEssx = EssxFormat.Instance.Open(essx);
        fromEssx.ReadAll().Should().BeEquivalentTo(fromTsv.ReadAll(), options => options.WithStrictOrdering());
    }

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }
}
