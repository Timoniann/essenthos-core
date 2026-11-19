using Essenthos.Core.Conversion;
using Essenthos.Core.Loading.Frame;
using Essenthos.Core.Loading.Links;
using Essenthos.Core.Mapping;
using Essenthos.Core.Products;
using FluentAssertions;
using Xunit;

namespace Essenthos.Core.Tests.Links;

/// <summary>
/// The answer key a measurement reads from a pair's hand alignment is the alignment as the merge settled it: a stated row
/// the pair's rulings refuse is not in it, the shape a ruling pins in its place is, and a ruling outside the alignment's
/// verses is not. Colossians 1:16 in the Ukrainian interlinear ties «на небі» to the ἐν of ἐν αὐτῷ and «на землі» to the ἐν
/// of ἐν τοῖς οὐρανοῖς.
/// </summary>
public sealed class HandAlignmentTests : IDisposable
{
    private const string Pair = "UBIO-NESTLE1904";
    private const string Other = "Door43 Ohienko alignment, CC0 1.0: Colossians by somebody in translationCore";

    private static readonly string Interlinear = InterlinearLinkLoader.Interlinear("UBIO").Source;

    private readonly string _root = Path.Combine(Path.GetTempPath(), "essenthos-products", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, true);
        }
    }

    [Fact]
    public void A_refused_stated_row_leaves_the_key_and_the_shape_a_ruling_pins_takes_its_place()
    {
        var store = Store();

        MappingFiles.Merge(store, Pair, force: true);
        var key = HandAlignment.Read(store, Pair, Interlinear)
            .Select(link => (string.Join(' ', link.From.Select(k => k.Own)), string.Join(' ', link.To.Select(k => k.Own))))
            .ToList();

        key.Should().BeEquivalentTo(
        [
            ("51.1:16.1", "51.1:16.1 51.1:16.2"),
            ("51.1:16.1", "51.1:16.2"),
            ("51.1:16.4", "51.1:16.6"),
            ("51.1:16.5", "51.1:16.8"),
            ("51.1:16.7", "51.1:16.10"),
            ("51.1:16.8", "51.1:16.12"),
        ], "the swapped prepositions are refused and the right ones pinned; Ним's stated link stands inside the wider one pinned");
        key.Should().NotContain(link => link.Item1 == "51.1:17.1",
            "a ruling in a verse the interlinear does not align is no part of its key");
    }

    private ProductStore Store()
    {
        var store = new ProductStore(_root);
        Words(store, "UBIO", ("51.1:16", ["Ним", "створено", "все", "на", "небі", "й", "на", "землі"]), ("51.1:17", ["І", "Він"]));
        Words(store, "NESTLE1904",
            ("51.1:16", ["ἐν", "αὐτῷ", "ἐκτίσθη", "τὰ", "πάντα", "ἐν", "τοῖς", "οὐρανοῖς", "καὶ", "ἐπὶ", "τῆς", "γῆς"]),
            ("51.1:17", ["καὶ", "αὐτός"]));
        MappingRow Stated(string from, string to, string source) =>
            new(from, to, "renders", MappingMethods.Stated, null, source, null, null, null);
        using (var stated = store.Create(MappingProducts.Of(Pair, MappingMethods.Stated), ProductKind.Machine, MappingRow.Schema, "test", []))
        {
            stated.WriteAll([
                Stated("51.1:16.1", "51.1:16.2", Interlinear),
                Stated("51.1:16.4", "51.1:16.1", Interlinear),
                Stated("51.1:16.5", "51.1:16.8", Interlinear),
                Stated("51.1:16.7", "51.1:16.6", Interlinear),
                Stated("51.1:16.8", "51.1:16.12", Interlinear),
                Stated("51.1:17.2", "51.1:17.2", Other),
            ]);
        }

        using (var rulings = store.Create(MappingProducts.Of(Pair, MappingProducts.Rulings), ProductKind.Hand, RulingRow.Schema, "test", []))
        {
            rulings.WriteAll([
                new RulingRow(RulingRow.Refuse, "51.1:16.4", "51.1:16.1", "", "", "на небі is ἐν τοῖς οὐρανοῖς"),
                new RulingRow(RulingRow.Refuse, "51.1:16.7", "51.1:16.6", "", "", "на землі is ἐπὶ τῆς γῆς"),
                new RulingRow(RulingRow.Accept, "51.1:16.4", "51.1:16.6", "renders", "", "на небі"),
                new RulingRow(RulingRow.Accept, "51.1:16.7", "51.1:16.10", "renders", "", "на землі"),
                new RulingRow(RulingRow.Accept, "51.1:16.1", "51.1:16.1 51.1:16.2", "renders", "", "Ним = ἐν αὐτῷ"),
                new RulingRow(RulingRow.Accept, "51.1:17.1", "51.1:17.1", "renders", "", "І = καὶ"),
            ]);
        }

        return store;
    }

    private static void Words(ProductStore store, string text, params (string Verse, string[] Words)[] verses)
    {
        using (var writer = store.Create(TextWord.ProductOf(text), ProductKind.Machine, TextWord.Schema(text), "test", []))
        {
            writer.WriteAll(verses.SelectMany(verse => verse.Words.Select((surface, n) => new TextWord(
                new WordKey(text, VerseKey.Parse(verse.Verse), n + 1), surface, " ", null, null, null, null, null, null, false, null, null, null))));
        }

        using var frame = store.Create(FrameFile.Product(text), ProductKind.Machine, FrameRow.Schema, "test", []);
        frame.WriteAll([]);
    }
}
