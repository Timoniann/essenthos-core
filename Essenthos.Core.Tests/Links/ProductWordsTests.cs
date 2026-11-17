using Essenthos.Core.Build;
using Essenthos.Core.Mapping;
using Essenthos.Core.Products;
using FluentAssertions;
using Xunit;

namespace Essenthos.Core.Tests.Links;

/// <summary>A final's key resolves from the words products alone: to its key's id where the text holds the word, to nothing where it does not.</summary>
public sealed class ProductWordsTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "essenthos-products", Guid.NewGuid().ToString("N"));

    public ProductWordsTests() => Directory.CreateDirectory(_root);

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public void AKeyNamesTheWordItsIdIsAndATextIsNumberedAsTheTextLoadNumbersIt()
    {
        var store = new ProductStore(_root);
        ProductCorpus.Text(store, "KJV", "eng", ProductCorpus.Genesis(["In", "the"]));
        ProductCorpus.Text(store, "BHSA", "hbo", ProductCorpus.Genesis(["בְּ"]));
        var words = ProductWords.Read(store);

        words.TextId("BHSA").Should().Be(TextLoad.Slugs(store).ToList().IndexOf("BHSA") + 1);
        var kjv = words.TextId("KJV")!.Value;
        words.Id(kjv, WordKey.Parse("1.1:1.2", "KJV")).Should().Be(WordIds.Of(WordKey.Parse("1.1:1.2", "KJV")));
        words.Id(kjv, WordKey.Parse("1.1:1.3", "KJV")).Should().BeNull("KJV prints two words there");
        words.Id(words.TextId("BHSA")!.Value, WordKey.Parse("1.1:1.2", "KJV")).Should().BeNull("a key of one text names no word of another");
        words.VerseOf(kjv, WordIds.Of(WordKey.Parse("1.1:1.2", "KJV"))).Should().Be(
            TextLoad.NumberedVerses(store, "KJV", kjv).Single(v => v.Row.Own == new VerseKey(1, 1, 1, "")).Id,
            "a word's verse is the id the text load gives its verse");
    }
}
