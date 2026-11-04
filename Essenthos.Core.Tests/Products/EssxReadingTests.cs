using Essenthos.Core.Products;
using Essenthos.Core.Products.Essx;
using Essenthos.Core.Products.Tools;
using Essenthos.Core.Products.Tsv;
using FluentAssertions;
using Xunit;

namespace Essenthos.Core.Tests.Products;

/// <summary>Reading only what is asked for, the dictionary's cap, the typed and one-pass conversions, and checking a product's bytes.</summary>
public sealed class EssxReadingTests : IDisposable
{
    private sealed record Word(WordKey Key, string Surface, string? Note, long Count);

    private static readonly ProductSchema<Word> Schema = new ProductSchema<Word>()
        .OwnWordKey("own", w => w.Key)
        .Text("surface", w => w.Surface)
        .OptionalText("note", w => w.Note)
        .Integer("count", w => w.Count)
        .Reads(row => new Word(row.WordKey(0, "KJV"), row.Text(1), row.TextOrNull(2), row.Integer(3)));

    private static readonly string[] Surfaces = ["In", "the", "beginning", "God"];

    private static Word Make(int i) => new(
        new WordKey("KJV", new VerseKey(1 + i / 400, 1 + i / 40 % 10, "", 1 + i / 4 % 10, i % 50 == 0 ? "a" : ""), 1 + i % 4),
        Surfaces[i % 4],
        i % 3 == 0 ? null : $"note {i}",
        i * 7L - 100);

    private static List<Word> Words(int count) => [.. Enumerable.Range(0, count).Select(Make)];

    private readonly string _folder = Path.Combine(Path.GetTempPath(), "essenthos-products", Guid.NewGuid().ToString("N"));

    public EssxReadingTests()
    {
        Directory.CreateDirectory(_folder);
    }

    private string File(string name) => Path.Combine(_folder, name);

    private string Write(string name, IEnumerable<Word> words, EssxOptions? options = null)
    {
        var path = File(name);
        using var writer = new EssxFormat(options ?? new EssxOptions { RowGroupRows = 100 }).Create(path, new ProductHeader("Essenthos/test/words.essx", [], "test"), Schema);
        writer.WriteAll(words);
        return path;
    }

    [Fact]
    public void AProjectedGroupDecodesOnlyTheNamedColumnsAndAColumnLeftOutSaysSo()
    {
        var path = Write("words.essx", Words(250));
        using var file = EssxReader.Open(path);
        var table = file.Tables[0];
        var group = table.ReadGroup(1, ["surface", "count"]);
        group.Columns.Select(column => column is not null).Should().Equal(false, true, false, true);
        group.Column("surface").Text(0).Should().Be(Make(100).Surface);
        group.Column("count").Integer(5).Should().Be(Make(105).Count);
        var act = () => group.Column("note");
        act.Should().Throw<ProductFormatException>().WithMessage("*'note' was not read*");
        table.Rows(["count"]).Select(row => row.Integer(3)).Should().Equal(Words(250).Select(w => w.Count));
        var unknown = () => table.ReadGroup(0, ["nothing"]);
        unknown.Should().Throw<ArgumentException>().WithMessage("No column 'nothing'*");
    }

    [Fact]
    public void ADictionaryColumnPastItsCapWritesTheRestAsTextAndReadsTheSame()
    {
        var words = Words(1000);
        var path = Write("capped.essx", words, new EssxOptions { RowGroupRows = 100, DictionaryLimit = 150 });
        using (var file = EssxReader.Open(path))
        {
            file.Footer.Version.Should().Be(EssxLayout.Version);
            var table = file.Tables[0];
            var note = table.Info.Physical.Single(p => p.Name == "note");
            note.Encoding.Should().Be("dict");
            note.Dictionary!.Count.Should().BeLessThanOrEqualTo(150);
            var index = table.Info.Physical.IndexOf(note);
            var encodings = table.Info.RowGroups.Select(group => group.Chunks[index].Encoding).ToList();
            encodings[0].Should().BeNull();
            encodings[^1].Should().Be("text");
            encodings.SkipWhile(encoding => encoding is null).Should().OnlyContain(encoding => encoding == "text", "once capped the column never goes back");
            table.Info.Physical.Single(p => p.Name == "surface").Encoding.Should().Be("dict", "four strings never reach the cap");
        }

        using var reader = EssxFormat.Instance.Open(path, Schema);
        reader.ReadAll().Should().Equal(words);
        ProductValidation.Validate(path).Ok.Should().BeTrue();
    }

    [Fact]
    public void AColumnOverTheCapFromItsFirstGroupIsPlainText()
    {
        var words = Words(300);
        var path = Write("text.essx", words, new EssxOptions { RowGroupRows = 100, DictionaryLimit = 10 });
        using (var file = EssxReader.Open(path))
        {
            var physical = file.Tables[0].Info.Physical.Single(p => p.Name == "note");
            physical.Encoding.Should().Be("text");
            physical.Dictionary.Should().BeNull();
        }

        using var reader = EssxFormat.Instance.Open(path, Schema);
        reader.ReadAll().Should().Equal(words);
    }

    [Fact]
    public void ABinaryCopyIsTypedAndGivesTheSameBytesAndTheSameTsv()
    {
        var words = Words(1000);
        var source = Write("source.essx", words, new EssxOptions { RowGroupRows = 256 });
        var copy = File("copy.essx");
        ProductConversion.Convert(source, copy, new ConversionOptions { Essx = new EssxOptions { RowGroupRows = 256 } }).Rows.Should().Be(1000);
        System.IO.File.ReadAllBytes(copy).Should().Equal(System.IO.File.ReadAllBytes(source));

        var tsv = File("copy.tsv");
        ProductConversion.Convert(source, tsv);
        var direct = File("direct.tsv");
        using (var writer = TsvFormat.Instance.Create(direct, new ProductHeader("Essenthos/test/words.essx", [], "test"), Schema))
        {
            writer.WriteAll(words);
        }

        System.IO.File.ReadAllText(tsv).Should().Be(System.IO.File.ReadAllText(direct));
    }

    [Fact]
    public void ATsvIsTypedFromItsFirstRowsAndALaterContradictionStartsAgainFromTheWholeFile()
    {
        var path = File("late.tsv");
        var lines = new List<string> { "# producer   by hand", "own\tvalue" };
        lines.AddRange(Enumerable.Range(1, 50).Select(i => $"1.1:{i}.1\t{i}"));
        lines.Add("1.2:1.1\tnot a number");
        System.IO.File.WriteAllText(path, string.Join('\n', lines) + "\n");

        var result = ProductConversion.Convert(path, File("late.essx"), new ConversionOptions { InferFrom = 10 });
        result.Columns.Select(column => column.Type).Should().Equal(ColumnType.WordKey, ColumnType.Text);
        result.Rows.Should().Be(51);
        using var file = EssxReader.Open(File("late.essx"));
        file.Tables[0].Rows().Last().Text(1).Should().Be("not a number");

        var stated = () => ProductConversion.Convert(path, File("stated.essx"),
            new ConversionOptions { InferFrom = 10, Types = TextTables.ParseTypes("own:wordkey,value:integer") });
        stated.Should().Throw<ProductFormatException>().WithMessage("*line 53*not integer*");
        System.IO.File.Exists(File("stated.essx")).Should().BeFalse();
    }

    [Fact]
    public void AOnePassTsvConversionReadsBackTheRowsTheTsvHolds()
    {
        var words = Words(700);
        var tsv = File("words.tsv");
        using (var writer = TsvFormat.Instance.Create(tsv, new ProductHeader("Essenthos/test/words.tsv", [], "test"), Schema))
        {
            writer.WriteAll(words);
        }

        ProductConversion.Convert(tsv, File("words.essx"), new ConversionOptions { InferFrom = 64, Essx = new EssxOptions { RowGroupRows = 100 } });
        using var reader = EssxFormat.Instance.Open(File("words.essx"), Schema);
        reader.ReadAll().Should().Equal(words);
        reader.Header!.Product.Should().Be("Essenthos/test/words.tsv");
    }

    [Fact]
    public void AVerseViewFindsItsRowsByTheKeyPartsAndShowsOnlyTheColumnsAsked()
    {
        var path = Write("verse.essx", Words(2000));
        var output = new StringWriter();
        ProductView.Rows(path, new ViewQuery { Verse = VerseKey.Parse("2.3:6"), Columns = ["own", "count"] }, output);
        var expected = Words(2000).Where(w => w.Key.Verse == VerseKey.Parse("2.3:6")).ToList();
        expected.Should().NotBeEmpty();
        output.ToString().Should().Be("own\tcount\n" + string.Concat(expected.Select(w => $"{w.Key.Own}\t{w.Count}\n")));

        var labelled = new StringWriter();
        ProductView.Rows(path, new ViewQuery { Verse = VerseKey.Parse("1.2:3a"), Where = [("surface", "In")] }, labelled);
        labelled.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries).Should().HaveCount(1 + Words(2000).Count(w => w.Key.Verse == VerseKey.Parse("1.2:3a") && w.Surface == "In"));
    }

    [Fact]
    public void ProductBytesChangedInPlaceAreCaughtAlthoughSizeAndWriteTimeHold()
    {
        var root = File("Resources");
        var store = new ProductStore(root);
        using (var writer = store.Create("texts/KJV/words", ProductKind.Machine, Schema, "test", []))
        {
            writer.WriteAll(Words(500));
        }

        store.Save();
        ProductIntegrity.CheckEssx(new ProductStore(root), 4).Should().ContainSingle().Which.Problem.Should().BeNull();

        var full = store.FullPath("texts/KJV/words", ProductKind.Machine);
        var written = System.IO.File.GetLastWriteTimeUtc(full);
        var bytes = System.IO.File.ReadAllBytes(full);
        bytes[100] ^= 0x40;
        System.IO.File.WriteAllBytes(full, bytes);
        System.IO.File.SetLastWriteTimeUtc(full, written);

        var trusting = new ProductStore(root);
        trusting.NeedsBuild("texts/KJV/words", ProductKind.Machine, []).Should().BeFalse("the memory trusts size and write time");
        var found = ProductIntegrity.CheckEssx(trusting, 4).Should().ContainSingle().Subject;
        found.Problem.Should().Contain("SHA-256");
        trusting.NeedsBuild("texts/KJV/words", ProductKind.Machine, []).Should().BeTrue("the check remembered the hash it read");
        trusting.Save();
        new ProductStore(root).NeedsBuild("texts/KJV/words", ProductKind.Machine, []).Should().BeTrue();
    }

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }
}
