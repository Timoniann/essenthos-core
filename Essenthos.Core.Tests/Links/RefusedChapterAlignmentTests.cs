using Essenthos.Core.Corpus;
using Essenthos.Core.Loading.Links;
using Essenthos.Core.Products;
using FluentAssertions;
using Xunit;

namespace Essenthos.Core.Tests.Links;

/// <summary>
/// The Synodal is joined to the King James in the books it gains only in the chapters the two print
/// alike, and in a chapter they print otherwise its verses belong to no verse link. The aligner does
/// not meet such a verse at an address it only covers: the King James's Tobit 7:15 stands at 7:13 and
/// covers 7:14, where the Synodal's 7:14 stands, and a word paired between the two crossed a verse pair
/// nothing joins.
/// </summary>
public sealed class RefusedChapterAlignmentTests : IDisposable
{
    private const int Tobit = 70;

    private readonly string _root = Path.Combine(Path.GetTempPath(), "essenthos-align-test", Guid.NewGuid().ToString("N"));
    private readonly ProductStore _store;

    public RefusedChapterAlignmentTests()
    {
        _store = new ProductStore(_root);
        ProductCorpus.Text(_store, Sources.SynodalSlug, "eng", [("70.7:1", ["ate"]), ("70.7:2", ["called"]), ("70.7:3", ["began"])]);

        // The King James's 7:3 stands at 7:2 and covers 7:3, and it prints a verse the Synodal does not.
        ProductCorpus.Text(_store, Sources.KingJamesSlug, "eng",
            [("70.7:1", ["ate"]), ("70.7:2", ["called"]), ("70.7:3", ["called"]), ("70.7:4", ["wept"])],
            ("70.7:3", ["70.7:2", "70.7:3"]));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public void TheChapterTheTwoPrintOtherwiseIsRefused()
    {
        var aligner = ProductCorpus.Aligner(_store);

        aligner.Refused(Sources.SynodalSlug, Sources.KingJamesSlug).Should().BeEquivalentTo([(Tobit, 7)]);
        aligner.Refused(Sources.KingJamesSlug, Sources.SynodalSlug).Should().BeEmpty("the King James is not declared against the Synodal");
    }

    [Fact]
    public async Task AVerseInARefusedChapterIsNotMetAtAnAddressItOnlyCovers()
    {
        // 7:3 is left to the Synodal alone, so the two share 7:1 and 7:2. The King James's two verses at 7:2 are one word,
        // so the line reads the same in whichever order they come.
        ProductCorpus.Answer(Path.Combine(_root, "Alignments", $"{Sources.SynodalSlug}-{Sources.KingJamesSlug}"),
            ProductCorpus.Stems("ate", "called"), ProductCorpus.Stems("ate", "called called"), "0-0:0.9:0.9", "0-0:0.9:0.9 0-1:0.9:0.9");

        var proposals = await ProductCorpus.Aligner(_store).Proposals(
            Sources.SynodalSlug, Sources.KingJamesSlug, $"{Sources.SynodalSlug}-{Sources.KingJamesSlug}", 0.1, selection: Selection.All);

        // A word's place in either text is its verse less one: each verse holds one word.
        proposals.Select(pair => (pair.From + 1, pair.To + 1)).Should().BeEquivalentTo([(1L, 1L), (2L, 2L), (2L, 3L)]);
    }

    [Fact]
    public void OnlyTheWordsOfAVerseStandingInARefusedChapterAreLeftOut()
    {
        var words = new Dictionary<(int, int, int), List<AlignmentPipeline.Word>>
        {
            [(Tobit, 7, 3)] = [new(1, "began", null), new(2, "wept", null, Aside: (Tobit, 7, 2))],
            [(Tobit, 8, 1)] = [new(3, "rose", null, Aside: (Tobit, 7, 18))],
            [(Tobit, 9, 1)] = [new(4, "sent", null, Aside: (Tobit, 8, 21))],
        };

        var met = AlignmentPipeline.Unmet(words, new HashSet<(int, int)> { (Tobit, 7) });

        met.Should().BeEquivalentTo(new Dictionary<(int, int, int), List<AlignmentPipeline.Word>>
        {
            [(Tobit, 7, 3)] = [new(1, "began", null)],
            [(Tobit, 9, 1)] = [new(4, "sent", null, Aside: (Tobit, 8, 21))],
        });
    }
}
