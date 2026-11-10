using Essenthos.Core.Mapping;
using Essenthos.Core.Products;
using FluentAssertions;
using Xunit;

namespace Essenthos.Core.Tests.Links;

/// <summary>An export from a database never overwrites a method file a producer made from files.</summary>
public sealed class MappingExportKeepsTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "essenthos-products", Guid.NewGuid().ToString("N"));

    private void Write(ProductStore store, string method, string producer)
    {
        using var writer = store.Create(MappingProducts.Of("KJV-BHSA", method), ProductKind.Machine, MappingRow.Schema, producer, []);
        writer.WriteAll([]);
    }

    [Fact]
    public void AProducersFileIsKeptAndAnExportedOneIsWrittenAgain()
    {
        var store = new ProductStore(_root);
        Write(store, "stated", ProductHeader.DescribeProducer("stated-pairs KJV-BHSA"));
        Write(store, "aligner", ProductHeader.DescribeProducer(MappingExport.Producer));

        MappingExport.MadeByProducer(store, MappingProducts.Of("KJV-BHSA", "stated")).Should().BeTrue();
        MappingExport.MadeByProducer(store, MappingProducts.Of("KJV-BHSA", "aligner")).Should().BeFalse();
        MappingExport.MadeByProducer(store, MappingProducts.Of("KJV-BHSA", "composed")).Should().BeFalse("there is nothing to keep");
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}
