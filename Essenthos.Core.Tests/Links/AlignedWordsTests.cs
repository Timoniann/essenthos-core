using Essenthos.Core.Loading.Links;
using Essenthos.Core.Mapping;
using Essenthos.Core.Products;
using FluentAssertions;
using Xunit;

namespace Essenthos.Core.Tests.Links;

/// <summary>
/// Several verses of one text can stand at one address — the lettered verses the Greek prints after a numbered one, a verse
/// covering the address beside the verse standing there — and the aligner reads them as one sentence, read from the text's
/// words, verses and frame. Each is read whole, in the order the edition prints them, or the sentence is the first word of
/// each verse, then the second of each, and the lines no longer match the answers stored for them.
/// </summary>
public sealed class AlignedWordsTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "essenthos-align-test", Guid.NewGuid().ToString("N"));
    private readonly ProductStore _store;

    public AlignedWordsTests() => _store = new ProductStore(_root);

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public void LetteredVersesAreReadWholeInTheOrderTheEditionPrintsThem()
    {
        // The edition prints 2b before 2a, which key order would not.
        ProductCorpus.Text(_store, "LXX", "eng",
            [("1.1:1", ["in", "the", "beginning"]), ("1.1:2", ["and", "the", "earth"]), ("1.1:2b", ["and", "darkness"]),
                ("1.1:2a", ["was", "without", "form"])],
            ("1.1:2a", ["1.1:2"]), ("1.1:2b", ["1.1:2"]));

        Read(lettered: true)[(1, 1, 2)].Select(word => word.Text).Should()
            .Equal("and", "the", "earth", "and", "darkness", "was", "without", "form");
        Read(lettered: false)[(1, 1, 2)].Select(word => word.Text).Should().Equal("and", "the", "earth");
    }

    [Fact]
    public void AVerseCoveringTheAddressIsReadWholeBesideTheVerseStandingThereAndSaysWhereItStands()
    {
        ProductCorpus.Text(_store, "LXX", "eng", ProductCorpus.Genesis(["in", "the", "beginning"], ["and", "the", "earth"]),
            ("1.1:1", ["1.1:1", "1.1:2"]));

        var at = Read(lettered: true)[(1, 1, 2)];

        at.Select(word => word.Text).Should().Equal("in", "the", "beginning", "and", "the", "earth");
        at.Select(word => word.Aside).Should().Equal(new (int, int, int)?[] { (1, 1, 1), (1, 1, 1), (1, 1, 1), null, null, null });
    }

    [Fact]
    public void ALetteredVerseTheFrameStandsAloneAtAHebrewVerseIsReadThereAndNoOtherIs()
    {
        // 1:3a is the Hebrew's 2:1 (Proverbs 24:22f at 30:1); 1:3b stands beside the numbered 1:4 (Joshua 24:31a); 1:3c is at
        // its own address; 1:3d stands where the Hebrew has no verse (Esther's additions).
        ProductCorpus.Text(_store, "LXX", "eng",
            [("1.1:3", ["and", "god", "said"]), ("1.1:3a", ["words", "of", "agur"]), ("1.1:3b", ["knives", "of", "stone"]),
                ("1.1:3c", ["a", "plus"]), ("1.1:3d", ["mordecai", "dreamed"]), ("1.1:4", ["they", "buried", "him"])],
            ("1.1:3a", ["1.2:1"]), ("1.1:3b", ["1.1:4"]), ("1.1:3d", ["1.11:2"]));
        var hebrew = new HashSet<(int, int, int)> { (1, 1, 3), (1, 1, 4), (1, 2, 1) };

        var read = AlignedWords.Read(new ProductTexts(_store)["LXX"], AlignmentPipeline.Written, lettered: false, hebrew: hebrew);

        read[(1, 2, 1)].Select(word => word.Text).Should().Equal("words", "of", "agur");
        read[(1, 1, 3)].Select(word => word.Text).Should().Equal("and", "god", "said");
        read[(1, 1, 4)].Select(word => word.Text).Should().Equal("they", "buried", "him");
        read.Should().NotContainKey((1, 11, 2));
        AlignedWords.HebrewLettered(new ProductTexts(_store)["LXX"], hebrew).Should().Equal(VerseKey.Parse("1.1:3a"));
        AlignedWords.PlacesLettered(_store, "LXX").Should().BeTrue();
    }

    private Dictionary<(int, int, int), List<AlignmentPipeline.Word>> Read(bool lettered) =>
        AlignedWords.Read(new ProductTexts(_store)["LXX"], AlignmentPipeline.Written, lettered);
}
