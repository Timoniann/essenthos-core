using Essenthos.Core.Products;
using Essenthos.Core.Records;
using FluentAssertions;
using Xunit;

namespace Essenthos.Core.Tests.Records;

public sealed class RecordFilesTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "essenthos-products", Guid.NewGuid().ToString("N"));

    [Fact]
    public void EveryFileIsNamedOnceUnderRecordsOrExternal()
    {
        var products = RecordFiles.All.Select(f => f.Product).ToList();

        products.Should().OnlyHaveUniqueItems();
        products.Should().OnlyContain(p => p.StartsWith("records/", StringComparison.Ordinal) || p.StartsWith("external/", StringComparison.Ordinal));
    }

    [Fact]
    public void NoRecordFileCarriesADatasetSource()
    {
        var records = RecordFiles.All.Single(f => f.Product == "records/records");

        records.Parsed.Select(c => c.Name).Should().NotContain(["source", "source_id", "open_bible_id", "by"]);
    }

    [Fact]
    public void OnlyTheRecordsFileWritesRecords()
    {
        RecordFiles.All.Where(f => f.Load.Contains("INSERT INTO entity ", StringComparison.Ordinal)).Select(f => f.Product)
            .Should().Equal("records/records");
    }

    [Fact]
    public void EveryTableIsEmptiedByOneFileOnly()
    {
        RecordFiles.All.SelectMany(f => f.Tables).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void ColumnsReadTheirTypesAndWhetherTheyMayBeEmpty()
    {
        var columns = RecordColumn.Parse("slug n:int back:bool attributes:json? ref:ref refs:refs? note?");

        columns.Select(c => (c.Name, c.Type, c.Nullable)).Should().Equal(
            ("slug", RecordColumnType.Text, false),
            ("n", RecordColumnType.Integer, false),
            ("back", RecordColumnType.Boolean, false),
            ("attributes", RecordColumnType.Json, true),
            ("ref", RecordColumnType.Ref, false),
            ("refs", RecordColumnType.Refs, true),
            ("note", RecordColumnType.Text, true));
    }

    [Fact]
    public void AHandFileReadsBackWhatWasWritten()
    {
        var file = RecordFiles.All.Single(f => f.Product == "records/facts/titles");
        var store = new ProductStore(_root);
        string?[][] rows =
        [
            ["the-anointed", "jesus", "40.1:16", """{"eng": "Christ"}""", "Essenthos, on the owner's ruling"],
            ["the-creator", "yhvh", "1.1:1", null, "Essenthos, read\twith a tab"],
        ];

        using (var writer = store.Create(file.Product, file.Kind, file.Schema(), ProductHeader.DescribeProducer(RecordFiles.Producer), []))
        {
            writer.WriteAll(rows);
            writer.Complete();
        }

        using var reader = store.Open(file.Product, file.Schema());
        reader.ReadAll().Should().BeEquivalentTo(rows, options => options.WithStrictOrdering());
        store.RelativePath(file.Product, file.Kind).Should().Be("Essenthos/records/facts/titles.tsv");
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}
