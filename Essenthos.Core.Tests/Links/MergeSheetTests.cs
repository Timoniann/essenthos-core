using Essenthos.Core.Conversion;
using Essenthos.Core.Mapping;
using Essenthos.Core.Products;
using FluentAssertions;
using Xunit;

namespace Essenthos.Core.Tests.Links;

/// <summary>The sheet a person reads the merge's withdrawals on, every word with its key.</summary>
public sealed class MergeSheetTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "essenthos-products", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, true);
        }
    }

    [Fact]
    public void Two_words_spelt_alike_are_told_apart_by_their_keys()
    {
        var store = new ProductStore(_root);
        Words(store, "LSG1910", "1.1:1", "que", "dis", "que");
        Words(store, "BHSA", "1.1:1", "כִּי", "אָמַר");
        using (var writer = store.Create(MappingProducts.Of("LSG1910-BHSA", MappingProducts.Statuses), ProductKind.Machine, MappingRow.Schema, "test", []))
        {
            writer.WriteAll([new MappingRow("1.1:1.3", "1.1:1.1", "renders", MappingMethods.Composed, 0.9, "machine", null, null, "withdrawn-by stated")]);
        }

        using (var writer = store.Create(MappingProducts.Of("LSG1910-BHSA", MappingProducts.Final), ProductKind.Machine, FinalRow.Schema, "test", []))
        {
            writer.WriteAll([new FinalRow(1, "1.1:1.1", "1.1:1.1", "renders", null, true, MappingMethods.Stated, null, "hand")]);
        }

        var sheet = MergeSheet.Write(store, "LSG1910-BHSA", "withdrawn-by", 5, 1);

        sheet.Should().Contain("**withdrawn-by stated** (composed 0.9): que (1.1:1.3)  →  כִּי (1.1:1.1)")
            .And.Contain("**stands** (stated): que (1.1:1.1)  →  כִּי (1.1:1.1)")
            .And.Contain("- LSG1910 1.1:1: 1:que 2:dis 3:que");
    }

    private static void Words(ProductStore store, string slug, string verse, params string[] words)
    {
        using var writer = store.Create(TextWord.ProductOf(slug), ProductKind.Machine, TextWord.Schema(slug), "test", []);
        writer.WriteAll(words.Select((word, n) => new TextWord(new WordKey(slug, VerseKey.Parse(verse), n + 1), word, " ", null, null, null, null,
            null, null, false, null, null, null)));
    }
}
