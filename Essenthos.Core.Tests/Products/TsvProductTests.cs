using Essenthos.Core.Products;
using Essenthos.Core.Products.Tsv;
using FluentAssertions;
using Xunit;

namespace Essenthos.Core.Tests.Products;

public sealed class TsvProductTests : IDisposable
{
    private sealed record Row(string Name, string? Note, long Count, int? Rank, bool Flag, bool? Maybe, string Json, string? Extra);

    private static readonly ProductSchema<Row> Schema = new ProductSchema<Row>()
        .Text("name", row => row.Name)
        .OptionalText("note", row => row.Note)
        .Integer("count", row => row.Count)
        .OptionalInteger("rank", row => row.Rank)
        .Boolean("flag", row => row.Flag)
        .OptionalBoolean("maybe", row => row.Maybe)
        .Json("json", row => row.Json)
        .OptionalJson("extra", row => row.Extra)
        .Reads(row => new Row(
            row.Text("name"), row.TextOrNull("note"), row.Integer("count"), row.Int32OrNull(3),
            row.Boolean("flag"), row.BooleanOrNull(5), row.Json(6), row.JsonOrNull(7)));

    private static readonly Row[] Rows =
    [
        new("plain", "a note", 1, 2, true, false, """{"a":1}""", null),
        new("tab\there", null, -5, null, false, null, "[]", """{"b":"two"}"""),
        new("line\nbreak and \\ slash", "", long.MaxValue, 0, true, true, "{}", ""),
        new("return\rcarriage", "\\N", 0, -1, false, false, "null", "\t"),
        new("בְּרֵאשִׁית", "Ἐν ἀρχῇ", 7, 7, true, true, """{"he":"א"}""", "ü"),
    ];

    private readonly string _folder = Path.Combine(Path.GetTempPath(), "essenthos-products", Guid.NewGuid().ToString("N"));

    public TsvProductTests()
    {
        Directory.CreateDirectory(_folder);
    }

    private string File(string name) => Path.Combine(_folder, name);

    private static ProductHeader Header() => new("Essenthos/test/rows.tsv", [new ProductInput("a/b.txt", new string('0', 64))], "test");

    [Fact]
    public void EveryTypeNullAndEveryEscapedCharacterRoundTrips()
    {
        var path = File("rows.tsv");
        using (var writer = TsvFormat.Instance.Create(path, Header(), Schema))
        {
            writer.WriteAll(Rows);
            writer.Rows.Should().Be(Rows.Length);
        }

        using var reader = TsvFormat.Instance.Open(path, Schema);
        reader.Header!.Rows.Should().Be(Rows.Length);
        reader.Header.Product.Should().Be("Essenthos/test/rows.tsv");
        reader.Columns.Should().Equal(Schema.Columns);
        reader.ReadAll().Should().Equal(Rows);
    }

    [Fact]
    public void TheFileIsWhatCopyReadsOneLinePerRowWithBackslashEscapesAndNAsNull()
    {
        var path = File("rows.tsv");
        using (var writer = TsvFormat.Instance.Create(path, Header(), Schema))
        {
            writer.WriteAll(Rows.Take(2));
        }

        var text = System.IO.File.ReadAllText(path);
        var lines = text.Split('\n');
        text.Should().NotContain("\r", "lines end in LF alone");
        text.Should().NotStartWith("\uFEFF", "no byte-order mark");
        lines[0].Should().Be("# product    Essenthos/test/rows.tsv");
        lines[3].Should().Be("# rows                  2");
        lines[4].Should().Be("name\tnote\tcount\trank\tflag\tmaybe\tjson\textra");
        lines[5].Should().Be("plain\ta note\t1\t2\tt\tf\t{\"a\":1}\t\\N");
        lines[6].Should().Be("tab\\there\t\\N\t-5\t\\N\tf\t\\N\t[]\t{\"b\":\"two\"}");
        lines[7].Should().BeEmpty("the last line ends in LF too");
        lines.Should().HaveCount(8);
    }

    [Fact]
    public void TheEscapingIsPostgresCopyTextFormat()
    {
        TsvEscaping.Escape("a\tb").Should().Be(@"a\tb");
        TsvEscaping.Escape("a\nb").Should().Be(@"a\nb");
        TsvEscaping.Escape("a\rb").Should().Be(@"a\rb");
        TsvEscaping.Escape(@"a\b").Should().Be(@"a\\b");
        TsvEscaping.Escape(null).Should().Be(@"\N");
        TsvEscaping.Escape(@"\N").Should().Be(@"\\N", "the letters backslash-N are a value, not null");
        TsvEscaping.Escape("").Should().Be("");

        TsvEscaping.Unescape(@"\N").Should().BeNull();
        TsvEscaping.Unescape(@"\\N").Should().Be(@"\N");
        TsvEscaping.Unescape("").Should().Be("");
        TsvEscaping.Unescape(@"a\tb\nc\rd\\e\bf\fg\vh\qi").Should().Be("a\tb\nc\rd\\e\bf\fg\vhqi", "COPY reads any other escaped character as itself");
        TsvEscaping.Unescape(@"trailing\").Should().Be(@"trailing\");
    }

    [Fact]
    public void TheUntypedReaderGivesEveryValueAsTextWithNulls()
    {
        var path = File("rows.tsv");
        using (var writer = TsvFormat.Instance.Create(path, Header(), Schema))
        {
            writer.WriteAll(Rows.Take(2));
        }

        using var reader = TsvFormat.Instance.Open(path);
        reader.Columns.Select(column => column.Name).Should().Equal("name", "note", "count", "rank", "flag", "maybe", "json", "extra");
        var rows = reader.ReadAll();
        rows[0].Should().Equal("plain", "a note", "1", "2", "t", "f", """{"a":1}""", null);
        rows[1].Should().Equal("tab\there", null, "-5", null, "f", null, "[]", """{"b":"two"}""");
    }

    [Fact]
    public void TheBodyReaderStartsAtTheFirstRow()
    {
        var path = File("rows.tsv");
        using (var writer = TsvFormat.Instance.Create(path, Header(), Schema))
        {
            writer.WriteAll(Rows.Take(1));
        }

        using var body = TsvFormat.OpenBody(path, out var header, out var columns);
        header!.Rows.Should().Be(1);
        columns.Should().StartWith("name");
        body.ReadToEnd().Should().Be("plain\ta note\t1\t2\tt\tf\t{\"a\":1}\t\\N\n");
    }

    [Fact]
    public void AWriterDisposedWithoutCompletingLeavesThePreviousFileAndNoTemporary()
    {
        var path = File("rows.tsv");
        using (var writer = TsvFormat.Instance.Create(path, Header(), Schema))
        {
            writer.WriteAll(Rows.Take(1));
        }

        using (var writer = TsvFormat.Instance.Create(path, Header(), Schema))
        {
            writer.Write(Rows[1]);
            writer.Write(Rows[2]);
        }

        Directory.GetFiles(_folder).Should().BeEquivalentTo(new[] { path }, "the temporary file is gone");
        using var reader = TsvFormat.Instance.Open(path, Schema);
        reader.ReadAll().Should().Equal(Rows.Take(1));
    }

    [Fact]
    public void ANullInAColumnThatIsNotNullableIsRefusedAtWriting()
    {
        var path = File("rows.tsv");
        using var writer = TsvFormat.Instance.Create(path, Header(), Schema);

        var write = () => writer.Write(Rows[0] with { Name = null! });

        write.Should().Throw<ProductFormatException>().WithMessage("*'name'*");
    }

    [Fact]
    public void AFileWhoseColumnsAreNotTheSchemasIsRefusedNamingTheDifference()
    {
        var path = File("rows.tsv");
        System.IO.File.WriteAllText(path, "name\tnote\tcount\n");

        var open = () => TsvFormat.Instance.Open(path, Schema);

        open.Should().Throw<ProductFormatException>().WithMessage("*column 4 is '(end)' where the schema expects 'rank'*");
    }

    [Fact]
    public void ALineWithTheWrongFieldCountIsRefusedByLineNumber()
    {
        var path = File("rows.tsv");
        System.IO.File.WriteAllText(path, "# product    x\n# producer   y\nname\tnote\tcount\trank\tflag\tmaybe\tjson\textra\na\tb\n");

        using var reader = TsvFormat.Instance.Open(path, Schema);
        var read = () => reader.ReadAll();

        read.Should().Throw<ProductFormatException>().WithMessage("*line 4: 2 fields where 8 columns*");
    }

    [Fact]
    public void AHeaderThatDisagreesWithTheRowCountIsRefusedAtTheEnd()
    {
        var path = File("rows.tsv");
        System.IO.File.WriteAllText(path, "# product    x\n# producer   y\n# rows       2\nname\tnote\tcount\trank\tflag\tmaybe\tjson\textra\na\t\\N\t1\t\\N\tt\t\\N\t{}\t\\N\n");

        using var reader = TsvFormat.Instance.Open(path, Schema);
        var read = () => reader.ReadAll();

        read.Should().Throw<ProductFormatException>().WithMessage("*says 2 rows and the file holds 1*");
    }

    [Fact]
    public void AFileWrittenByHandWithNoHeaderStillReads()
    {
        var path = File("hand.tsv");
        System.IO.File.WriteAllText(path, "name\tnote\tcount\trank\tflag\tmaybe\tjson\textra\na\t\\N\t1\t\\N\tt\t\\N\t{}\t\\N\n");

        using var reader = TsvFormat.Instance.Open(path, Schema);
        reader.Header.Should().BeNull();
        reader.ReadAll().Should().Equal(new Row("a", null, 1, null, true, null, "{}", null));
    }

    [Fact]
    public void ATypedValueThatDoesNotParseIsRefusedNamingTheColumn()
    {
        var path = File("rows.tsv");
        System.IO.File.WriteAllText(path, "name\tnote\tcount\trank\tflag\tmaybe\tjson\textra\na\t\\N\tmany\t\\N\tt\t\\N\t{}\t\\N\n");

        using var reader = TsvFormat.Instance.Open(path, Schema);
        var read = () => reader.ReadAll();

        read.Should().Throw<ProductFormatException>().WithMessage("*'count' holds 'many'*");
    }

    [Fact]
    public void AtomicFileReplacesTheWholeFileOrNothing()
    {
        var path = File("atomic.txt");
        AtomicFile.WriteAllText(path, "first");
        using (var file = AtomicFile.Create(path))
        {
            file.Stream.Write("second"u8);
        }

        System.IO.File.ReadAllText(path).Should().Be("first");
        using (var file = AtomicFile.Create(path))
        {
            file.Stream.Write("third"u8);
            file.Commit();
        }

        System.IO.File.ReadAllText(path).Should().Be("third");
        Directory.GetFiles(_folder).Should().Equal(path);
    }

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }
}
