using Essenthos.Core.Products;
using Essenthos.Core.Records;
using Essenthos.Core.Utils;
using FluentAssertions;
using Xunit;

namespace Essenthos.Core.Tests.Records;

/// <summary>
/// The Greek of the record names is composed (NFC): a decomposed perispomeni or breathing is in no subset of the reading
/// face, so the browser drew the mark from another font, off its letter. The Hebrew is left as written, because
/// composing reorders its points.
/// </summary>
public sealed class RecordNameNormalisationTests
{
    [Fact]
    public void EveryGreekNameAndItsTransliterationIsComposed()
    {
        var greek = Array.FindIndex(RecordProducers.File("records/names").Parsed, c => c.Name == "greek");
        greek.Should().BeGreaterThan(0);

        var decomposed = RecordProducers.Read(new ProductStore(TestResources.Shipped(".")), "records/names")
            .Where(row => new[] { row[greek], row[greek + 1] }.Any(text => text is not null && !UnicodeNormalization.IsComposed(text)))
            .Select(row => $"{row[0]} {row[1]}")
            .ToList();

        decomposed.Should().BeEmpty();
    }
}
