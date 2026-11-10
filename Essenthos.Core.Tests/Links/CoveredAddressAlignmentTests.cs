using Essenthos.Core.Loading.Links;
using Essenthos.Core.Products;
using FluentAssertions;
using Xunit;

namespace Essenthos.Core.Tests.Links;

/// <summary>
/// A verse that covers an address beyond its own is read there too, beside the verse the other text
/// stands at it. Where each text has such a verse at the same address, the two meet only because
/// both are there: the verse links join each to the verse standing there and not to one another, so
/// a word pair between them crosses a verse pair nothing joins.
/// </summary>
public sealed class CoveredAddressAlignmentTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "essenthos-align-test", Guid.NewGuid().ToString("N"));
    private readonly ProductStore _store;

    public CoveredAddressAlignmentTests()
    {
        _store = new ProductStore(_root);
        ProductCorpus.Text(_store, "BSB", "eng", ProductCorpus.Genesis(["void"], ["earth"], ["earth"]), ("1.1:3", ["1.1:3", "1.1:2"]));
        ProductCorpus.Text(_store, "KJV", "eng", ProductCorpus.Genesis(["earth"], ["earth"], ["void"]), ("1.1:1", ["1.1:1", "1.1:2"]));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public async Task TwoVersesThatOnlyCoverAnAddressAreNotPairedThere()
    {
        // Each verse is one word, and the two at 1:2 on each side are the same word, so the lines read the same in
        // whichever order the verses come.
        ProductCorpus.Answer(Path.Combine(_root, "Alignments", "BSB-KJV"),
            ProductCorpus.Stems("void", "earth earth", "earth"),
            ProductCorpus.Stems("earth", "earth earth", "void"),
            "0-0:0.9:0.9", "0-0:0.9:0.9 0-1:0.9:0.9 1-0:0.9:0.9 1-1:0.9:0.9", "0-0:0.9:0.9");

        var proposals = await ProductCorpus.Aligner(_store).Proposals("BSB", "KJV", "BSB-KJV", 0.1, selection: Selection.All);

        // A word's place in either text is its verse less one: each verse holds one word.
        proposals.Select(pair => (pair.From + 1, pair.To + 1)).Should().BeEquivalentTo(
        [
            (1L, 1L),
            (2L, 2L),
            (2L, 1L),
            (3L, 2L),
            (3L, 3L),
        ]);
    }
}
