using Essenthos.Core.Conversion;
using Essenthos.Core.Loading.Frame;
using Essenthos.Core.Mapping;
using Essenthos.Core.Products;
using FluentAssertions;
using Xunit;

namespace Essenthos.Core.Tests.Links;

/// <summary>
/// Two witnesses' method files made from their words products, with no database: Greek editions paired by their Strong
/// numbers with the leftovers matched by place, and Greek editions laid against each other by their letters.
/// </summary>
public sealed class WitnessPairSourceTests : IDisposable
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
    public void Numbers_pair_as_strong_the_leftovers_by_place_as_lexical_and_every_absence_is_written()
    {
        var store = new ProductStore(_root);
        Words(store, "WH1881", "grc", ("40.1:10", [("ὁ", "G3588"), ("δὲ", "G1161"), ("αμως", "G301")]));
        Words(store, "TR1894", "grc", ("40.1:10", [("ὁ", "G3588"), ("αμων", "G300"), ("γαρ", "G1063"), ("δε", "G1161")]));
        var texts = new ProductTexts(store);

        var strong = new GreekWitnessSource(MappingMethods.Strong).Rows(texts, "WH1881-TR1894", _ => { }).ToList();
        var lexical = new GreekWitnessSource(MappingMethods.Lexical).Rows(texts, "WH1881-TR1894", _ => { }).ToList();

        strong.Select(r => (r.From, r.To, r.Relation, r.Confidence)).Should().BeEquivalentTo(
            [
                ("40.1:10.1", "40.1:10.1", "equals", 0.95), ("40.1:10.2", "40.1:10.4", "equals", 0.95),
                (string.Empty, "40.1:10.3", "omits", 0.85),
            ],
            "γαρ is the second edition's third word as αμως is the first edition's, and αμως being matched must not hide it");
        lexical.Should().ContainSingle().Which.Should().Match<MappingRow>(r =>
            r.From == "40.1:10.3" && r.To == "40.1:10.2" && r.Relation == "renders" && r.Confidence == 0.6);
    }

    [Fact]
    public void An_address_holding_two_verses_of_one_number_is_read_verse_after_verse()
    {
        var store = new ProductStore(_root);
        Words(store, "SWETEOG", "grc", ("27.6:12", [("Μήδων", null), ("καὶ", null)]), ("27.6:12a", [("Περσῶν", null), ("οὐ", null)]));
        Words(store, "SWETE", "grc", ("27.6:12", [("Μήδων", null), ("καὶ", null), ("Περσῶν", null), ("οὐ", null)]));

        var rows = new GreekLetterSource().Rows(new ProductTexts(store), "SWETEOG-SWETE", _ => { }).ToList();

        rows.Select(r => (r.From, r.To, r.Relation)).Should().BeEquivalentTo(
        [
            ("27.6:12.1", "27.6:12.1", "equals"), ("27.6:12.2", "27.6:12.2", "equals"),
            ("27.6:12a.1", "27.6:12.3", "equals"), ("27.6:12a.2", "27.6:12.4", "equals"),
        ]);
    }

    private static void Words(ProductStore store, string text, string language, params (string Verse, (string Surface, string? Strong)[] Words)[] verses)
    {
        using (var writer = store.Create(TextWord.ProductOf(text), ProductKind.Machine, TextWord.Schema(text), "test", []))
        {
            writer.WriteAll(verses.SelectMany(verse => verse.Words.Select((word, n) => new TextWord(
                new WordKey(text, VerseKey.Parse(verse.Verse), n + 1), null, word.Surface, " ", null, word.Strong, null, null, null, null,
                false, null, null, null))));
        }

        using (var frame = store.Create(FrameFile.Product(text), ProductKind.Machine, FrameRow.Schema, "test", []))
        {
            frame.WriteAll([]);
        }

        using var fields = store.Create(TextField.ProductOf(text), ProductKind.Machine, TextField.Schema, "test", []);
        fields.WriteAll([new TextField("language", language), new TextField("kind", "critical-edition")]);
    }
}
