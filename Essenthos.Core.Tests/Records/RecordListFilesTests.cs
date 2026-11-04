using System.Reflection;
using System.Text.Json;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Endpoints;
using Essenthos.Core.Loading.Encyclopedia;
using Essenthos.Core.Products;
using Essenthos.Core.Records;
using FluentAssertions;
using Xunit;

namespace Essenthos.Core.Tests.Records;

/// <summary>
/// The hand files that replace the Forge's embedded lists (withdrawn, held apart, withheld clauses,
/// folds declined, location supplements, vocabulary), the file order every export shares, and the
/// derived relationships made from files.
/// </summary>
public sealed class RecordListFilesTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "essenthos-products", Guid.NewGuid().ToString("N"));

    [Fact]
    public void EveryExportIsSortedByColumnsOfItsOwnFile()
    {
        foreach (var file in RecordFiles.All.Where(f => f.Listed is null))
        {
            file.Order(file.Export).Should().NotBeEmpty(file.Product);
            if (file.ExportLoaded is { } loaded)
            {
                file.Order(loaded).Should().Equal(file.Order(file.Export), file.Product);
            }
        }
    }

    [Fact]
    public void AFileIsSortedByOrdinalWhateverTheServersCollation()
    {
        var file = RecordFiles.All.Single(f => f.Product == "records/held-apart");
        string?[][] rows = [["ab-c", "x", "", ""], ["aba", "x", "", ""], ["Abc", "x", "", ""], ["ab-c", "a", "", ""]];

        file.Sorted(rows, [0, 1]).Select(r => $"{r[0]} {r[1]}").Should().Equal("Abc x", "ab-c a", "ab-c x", "aba x");
    }

    [Fact]
    public void KeysSortByVerseAndTheEmptyLast()
    {
        var file = RecordFiles.All.Single(f => f.Product == "records/facts/relationships");
        string?[][] rows =
        [
            ["a", "son-of", "b", null, "read", null, "manual", null, "x", null],
            ["a", "son-of", "b", "1.10:2", "read", null, "manual", null, "x", null],
            ["a", "son-of", "b", "1.9:30", "read", null, "manual", null, "x", null],
        ];

        file.Sorted(rows, file.Order(file.Export)).Select(r => r[3]).Should().Equal("1.9:30", "1.10:2", null);
    }

    [Fact]
    public void TheWithdrawnFileHoldsEveryEmbeddedRecord()
    {
        var rows = WrittenAndRead("records/withdrawn");

        rows.Select(r => new WithdrawnRecord(r[0]!, RecordFiles.SourceIdFrom(r[1]!, r[2]!), r[3]!, r[4], r[5]))
            .Should().BeEquivalentTo(WithdrawnRecordLoader.Read());
        rows.Should().Contain(r => r[1] == "openbible" && r[2] == "af0ee3a").And.Contain(r => r[1] == "essenthos");
    }

    [Fact]
    public void TheHeldApartFileHoldsEveryEmbeddedPair()
    {
        WrittenAndRead("records/held-apart").Select(r => new HeldApartPair(r[0]!, r[1]!, r[2]!, r[3]!))
            .Should().BeEquivalentTo(SenseReadingFiles.HeldApart().Pairs);
    }

    [Fact]
    public void TheWithheldClausesFileKeysEachClauseByItsCanonicalVerse()
    {
        var list = OwnRelationshipLoader.WithheldClauses();
        var rows = WrittenAndRead("records/facts/withheld-clauses");

        rows.Select(r => (r[0], r[1], r[2], r[3], r[4], r[5])).Should().BeEquivalentTo(
            list.Clauses.Select(c => ((string?)c.Entity, (string?)c.Relation, (string?)c.Target,
                (string?)RecordFiles.CanonicalVerse(c.Reference!), c.Why, (string?)list.DecidedBy)));
        rows.Should().Contain(r => r[0] == "abiel" && r[2] == "kish" && r[3] == "9.9:1");
    }

    [Fact]
    public void TheFoldsDeclinedFileHoldsEveryDeclinedPair()
    {
        using var stream = typeof(RecordFiles).Assembly.GetManifestResourceStream(DuplicateRecordLoader.Resource)!;
        using var json = JsonDocument.Parse(stream);
        var declined = json.RootElement.GetProperty("declined").EnumerateArray()
            .Select(d => (d.GetProperty("strongNumber").GetString(), d.GetProperty("pair")[0].GetString(),
                d.GetProperty("pair")[1].GetString(), d.GetProperty("why").GetString()))
            .ToList();

        WrittenAndRead("records/folds-declined").Select(r => (r[0], r[1], r[2], r[3])).Should().BeEquivalentTo(declined);
    }

    [Fact]
    public void TheLocationSupplementsFileReadsBackAsTheSupplement()
    {
        var rows = WrittenAndRead("records/facts/location-supplements");
        var embedded = LocationSupplement.Read();

        rows.Where(r => r[5] is null).Select(r => new IndependentPoint(r[0]!, r[2]!, double.Parse(r[3]!, System.Globalization.CultureInfo.InvariantCulture),
                double.Parse(r[4]!, System.Globalization.CultureInfo.InvariantCulture), r[1]!, r[6]!))
            .Should().BeEquivalentTo(embedded.Points);
        rows.Where(r => r[5] is not null).Select(r => new IndependentRegion(r[5]!, r[2]!, r[0]!, double.Parse(r[3]!, System.Globalization.CultureInfo.InvariantCulture),
                double.Parse(r[4]!, System.Globalization.CultureInfo.InvariantCulture), r[1]!, r[6]!))
            .Should().BeEquivalentTo(embedded.Regions);
    }

    [Fact]
    public void TheVocabularySaysEveryRelationThePagesPhrase()
    {
        var relations = WrittenAndRead("records/vocabulary").Where(r => r[0] == "relation").Select(r => r[1]).ToList();

        relations.Should().BeEquivalentTo(DescriptorRelations.All);
        foreach (var language in DescriptorPhrasings.Languages)
        {
            DescriptorPhrasings.For(language)!.Keys.Should().BeSubsetOf(relations!, language);
        }
    }

    [Fact]
    public void ARelationshipIsSettledFromTheClauseFilesAndAHeldClauseIsNotWritten()
    {
        var store = new ProductStore(_root);
        Write(store, "records/records",
            ["abiel", "person", null, null, "Abiel", "male", null, null, null, null, null, null],
            ["kish", "person", null, null, "Kish", "male", null, null, null, null, null, null],
            ["saul", "person", null, null, "Saul", "male", null, null, null, null, null, null]);
        Write(store, "records/facts/clauses",
            ["kish", "1", "son-of", "abiel", "9.9:1", null, null, null, "manual", null, "read from Scripture by the owner", null, null],
            ["abiel", "1", "father-of", "kish", "9.9:1", null, null, null, "model-reading", "0.9", "read from Scripture by a model", null, null],
            ["saul", "1", "son-of", "kish", "9.9:2", null, null, null, "model-reading", "0.9", "read from Scripture by b", null, null],
            ["saul", "2", "son-of", "kish", "9.9:2", null, null, null, "model-reading", "0.9", "read from Scripture by a", null, null]);
        Write(store, "records/facts/withheld-clauses", ["abiel", "father-of", "kish", "9.9:1", "held", "the owner"]);

        var (product, rows) = RecordProducers.Relationships(store, store);

        product.Should().Be("records/facts/relationships");
        rows.Should().Be(2);
        using var reader = store.Open(product, RecordFiles.All.Single(f => f.Product == product).Schema());
        reader.ReadAll().Select(r => $"{r[0]} {r[1]} {r[2]} {r[3]} {r[6]} {r[7]} {r[8]}").Should().Equal(
            "kish son-of abiel 9.9:1 manual  read from Scripture by the owner",
            "saul son-of kish 9.9:2 model-reading 0.9 read from Scripture by a");
    }

    /// <summary>The list written as its file, read back, and written again to the same bytes.</summary>
    private List<string?[]> WrittenAndRead(string product)
    {
        var file = RecordFiles.All.Single(f => f.Product == product);
        var first = new ProductStore(Path.Combine(_root, "first"));
        Write(first, product, [.. file.Listed!()]);
        using var reader = first.Open(product, file.Schema());
        var rows = reader.ReadAll().ToList();

        var second = new ProductStore(Path.Combine(_root, "second"));
        Write(second, product, [.. rows]);
        File.ReadAllBytes(second.FullPath(product, file.Kind)).Should().Equal(File.ReadAllBytes(first.FullPath(product, file.Kind)));
        rows.Should().NotBeEmpty();
        return rows;
    }

    private static void Write(ProductStore store, string product, params string?[][] rows)
    {
        var file = RecordFiles.All.Single(f => f.Product == product);
        using var writer = store.Create(product, file.Kind, file.Schema(), ProductHeader.DescribeProducer(RecordFiles.Producer), []);
        writer.WriteAll(rows);
        writer.Complete();
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}
