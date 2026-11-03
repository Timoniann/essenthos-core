using Essenthos.Core.Products;
using Essenthos.Core.Products.Esw;
using Essenthos.Core.Products.Tools;
using Essenthos.Core.Products.Tsv;
using FluentAssertions;
using Xunit;

namespace Essenthos.Core.Tests.Products;

public sealed class ToolTests : IDisposable
{
    private sealed record Word(WordKey Key, string Surface, string? Lemma, int Position, bool Elided, string? Morphology);

    private static readonly ProductSchema<Word> Schema = new ProductSchema<Word>()
        .OwnWordKey("own", w => w.Key)
        .Text("surface", w => w.Surface)
        .OptionalText("lemma", w => w.Lemma)
        .Integer("pos", w => w.Position)
        .Boolean("elided", w => w.Elided)
        .OptionalJson("morphology", w => w.Morphology)
        .Reads(row => new Word(row.WordKey(0, "KJV"), row.Text(1), row.TextOrNull(2), row.Int32(3), row.Boolean(4), row.JsonOrNull(5)));

    private static readonly Word[] Words =
    [
        new(WordKey.Parse("KJV 1.1:1.1"), "In", "in", 1, false, """{"pos":"prep"}"""),
        new(WordKey.Parse("KJV 1.1:1.2"), "the, \"quoted\"", null, 2, false, null),
        new(WordKey.Parse("KJV 1.1:2.1"), "tab\there", "", 1, true, """{"a":[1,2]}"""),
        new(WordKey.Parse("KJV 1.2:1.1"), "line\nbreak <&>", "x", 1, false, null),
        new(WordKey.Parse("KJV 2.1:1.1"), "בְּרֵאשִׁית", "Ἐν", 1, false, """{"he":"א"}"""),
    ];

    private readonly string _folder = Path.Combine(Path.GetTempPath(), "essenthos-products", Guid.NewGuid().ToString("N"));

    public ToolTests()
    {
        Directory.CreateDirectory(_folder);
    }

    private string File(string name) => Path.Combine(_folder, name);

    private string Esw()
    {
        var path = File("words.esw");
        if (!System.IO.File.Exists(path))
        {
            var header = new ProductHeader("Essenthos/texts/KJV/words.esw", [new ProductInput("bible4u/kjv.zip", new string('1', 64))], "test");
            using var writer = EswFormat.Instance.Create(path, header, Schema);
            writer.WriteAll(Words);
        }

        return path;
    }

    [Theory]
    [InlineData("tsv")]
    [InlineData("csv")]
    [InlineData("jsonl")]
    [InlineData("xml")]
    public void EveryTextShapeRoundTripsBackToTheSameRowsAndTypes(string extension)
    {
        var text = File($"words.{extension}");
        var back = File("back.esw");

        var outward = ProductConversion.Convert(Esw(), text);
        var inward = ProductConversion.Convert(text, back);

        outward.Rows.Should().Be(Words.Length);
        inward.Rows.Should().Be(Words.Length);
        inward.Columns.Should().Equal(
            new Column("own", ColumnType.WordKey),
            new Column("surface", ColumnType.Text),
            new Column("lemma", ColumnType.Text, Nullable: true),
            new Column("pos", ColumnType.Integer),
            new Column("elided", ColumnType.Boolean),
            new Column("morphology", ColumnType.Json, Nullable: true));
        using var reader = EswFormat.Instance.Open(back, Schema);
        reader.ReadAll().Should().Equal(Words, "nulls, empty strings, tabs, quotes, line breaks and scripts survive {0}", extension);
    }

    [Fact]
    public void TheTsvOfAnEswIsTheTsvTheWriterWouldHaveWritten()
    {
        var viaConvert = File("converted.tsv");
        var direct = File("direct.tsv");
        ProductConversion.Convert(Esw(), viaConvert);
        using (var writer = TsvFormat.Instance.Create(direct, new ProductHeader("Essenthos/texts/KJV/words.esw", [new ProductInput("bible4u/kjv.zip", new string('1', 64))], "test"), Schema))
        {
            writer.WriteAll(Words);
        }

        System.IO.File.ReadAllText(viaConvert).Should().Be(System.IO.File.ReadAllText(direct));
    }

    [Fact]
    public void StatedTypesWinOverInferredOnes()
    {
        var csv = File("words.csv");
        ProductConversion.Convert(Esw(), csv);

        var result = ProductConversion.Convert(csv, File("typed.esw"), new ConversionOptions { Types = TextTables.ParseTypes("pos:text,own:text") });

        result.Columns.First(c => c.Name == "pos").Type.Should().Be(ColumnType.Text);
        result.Columns.First(c => c.Name == "own").Type.Should().Be(ColumnType.Text);
    }

    [Fact]
    public void InferenceTellsKeysIntegersBooleansJsonAndTextApart()
    {
        var names = new[] { "w", "v", "i", "b", "j", "t", "z", "empty" };
        var rows = new[]
        {
            new string?[] { "1.1:1.3", "1.31:50a", "-5", "t", "{}", "In", "007", null },
            new string?[] { "19.4a:1.2", "2.1:1", "0", "f", "[1]", "1.1:1", "1", null },
            new string?[] { "1.1:1-10.7", null, "9223372036854775807", null, null, "x", "t", null },
        };

        TextTables.Infer(names, rows).Should().Equal(
            new Column("w", ColumnType.WordKey),
            new Column("v", ColumnType.VerseKey, true),
            new Column("i", ColumnType.Integer),
            new Column("b", ColumnType.Boolean, true),
            new Column("j", ColumnType.Json, true),
            new Column("t", ColumnType.Text),
            new Column("z", ColumnType.Text, false),
            new Column("empty", ColumnType.Text, true));
    }

    [Fact]
    public void ViewHeadVerseWhereAndColumns()
    {
        var head = new StringWriter();
        ProductView.Rows(Esw(), new ViewQuery { Head = 2, Columns = ["own", "surface"] }, head);
        head.ToString().Should().Be("own\tsurface\n1.1:1.1\tIn\n1.1:1.2\tthe, \"quoted\"\n");

        var verse = new StringWriter();
        ProductView.Rows(Esw(), new ViewQuery { Verse = VerseKey.Parse("1.1:2") }, verse);
        verse.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries).Should().HaveCount(2).And.Contain(line => line.StartsWith("1.1:2.1\ttab\\there\t\t1\tt\t"));

        var where = new StringWriter();
        ProductView.Rows(Esw(), new ViewQuery { Where = [("lemma", null)], Columns = ["own"] }, where);
        where.ToString().Should().Be("own\n1.1:1.2\n");

        var tsv = File("words.tsv");
        ProductConversion.Convert(Esw(), tsv);
        var fromTsv = new StringWriter();
        ProductView.Rows(tsv, new ViewQuery { Verse = VerseKey.Parse("2.1:1"), Columns = ["surface"] }, fromTsv);
        fromTsv.ToString().Should().Be("surface\nבְּרֵאשִׁית\n");
    }

    [Fact]
    public void StatsNameEveryStoredColumnWithItsEncodingAndBytes()
    {
        var stats = new StringWriter();
        ProductView.Stats(Esw(), stats);

        var text = stats.ToString();
        text.Should().Contain("table\trows\trows=5");
        text.Should().Contain("column\town.book\twordkey\tint\t");
        text.Should().Contain("column\tsurface\ttext\tdict\t").And.Contain("distinct=5");
        text.Should().Contain("column\tlemma\ttext?\tdict\t").And.Contain("nulls=1");
        text.Should().Contain("# product    Essenthos/texts/KJV/words.esw");
    }

    [Fact]
    public void ValidationPassesAGoodFileAndNamesWhatIsWrongWithABadOne()
    {
        ProductValidation.Validate(Esw()).Should().BeEquivalentTo(new ValidationReport(Esw(), [], Words.Length));

        var bytes = System.IO.File.ReadAllBytes(Esw());
        var damaged = File("damaged.esw");
        bytes[60] ^= 0x01;
        System.IO.File.WriteAllBytes(damaged, bytes);
        var report = ProductValidation.Validate(damaged);
        report.Ok.Should().BeFalse();
        report.Problems.Should().Contain(problem => problem.Contains("SHA-256"));

        var tsv = File("short.tsv");
        System.IO.File.WriteAllText(tsv, "# product    x\n# producer   y\n# rows       3\nown\tsurface\n1.1:1.1\tIn\n");
        ProductValidation.Validate(tsv).Problems.Should().ContainSingle().Which.Should().Contain("says 3 rows and the file holds 1");

        var notAProduct = File("x.bin");
        System.IO.File.WriteAllText(notAProduct, "nothing");
        ProductValidation.Validate(notAProduct).Ok.Should().BeFalse();
    }

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }
}
