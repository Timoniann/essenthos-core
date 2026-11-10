using Essenthos.Core.Loading.Links;
using Essenthos.Core.Mapping;
using Essenthos.Core.Products;
using FluentAssertions;
using Xunit;

namespace Essenthos.Core.Tests.Links;

/// <summary>
/// A composed answer names a verse of the target only where the frame joins it to the source's.
/// The middle text's statement may cross a verse boundary on its own account — the Berean's
/// Philippians 1:16 and 1:17 stand in the critical text's order — and carried onto a text that
/// orders them the other way, the crossing names the wrong verse.
/// </summary>
public sealed class ComposedVerseJoinTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "essenthos-compose-test", Guid.NewGuid().ToString("N"));
    private readonly ProductStore _store;

    public ComposedVerseJoinTests()
    {
        _store = new ProductStore(_root);
        ProductCorpus.Text(_store, "TYN1534", "eng", [("1.1:16", ["The", "one"]), ("1.1:17", ["The", "other"]), ("1.1:18", ["What", "then"])]);
        ProductCorpus.Text(_store, "NESTLE1904", "grc", [("1.1:16", ["οἱ", "δὲ"]), ("1.1:17", ["οἱ", "μὲν"]), ("1.1:18", ["τί", "γάρ"])]);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public void AWordReachesItsOwnVerseAndNotTheNextOne()
    {
        var joins = Joins();

        joins.Joins(Word(16), Word(16)).Should().BeTrue();
        joins.Joins(Word(16), Word(17)).Should().BeFalse();
    }

    [Fact]
    public void AVerseLinkJoinsTwoVersesAtDifferentAddressesEitherWayItIsStored()
    {
        using (var writer = _store.Create(MappingProducts.Of("NESTLE1904-TYN1534", VerseLinkFiles.Name), ProductKind.Machine, VerseLinkRow.Schema,
                   "test", []))
        {
            writer.WriteAll([new VerseLinkRow("1.1:18", "1.1:17", "renders", "stated-by-source", null, "a test", null)]);
        }

        var joins = Joins();

        joins.Joins(Word(17), Word(18)).Should().BeTrue();
        joins.Joins(Word(16), Word(18)).Should().BeFalse();
    }

    [Fact]
    public void ACompositionDropsTheAnswerItsMiddleTextCarriedAcrossVerses()
    {
        var middle = 9_000_000L;
        var composed = CompositionPipeline.Compose(
            [(Word(16), middle, 0.9)],
            new (long Bridge, long To, double Confidence)[] { (middle, Word(17), 1.0), (middle, Word(16), 0.5) }
                .ToLookup(row => row.Bridge, row => (row.To, row.Confidence)),
            Joins());

        composed.Should().ContainSingle().Which.To.Should().Be(Word(16));
    }

    private VerseJoins Joins()
    {
        var texts = new ProductTexts(_store);
        return VerseJoins.Of(_store, texts["TYN1534"], texts["NESTLE1904"]);
    }

    /// <summary>The first word of a verse: both texts print two words a verse from 1:16.</summary>
    private static long Word(int verse) => (verse - 16) * 2;
}
