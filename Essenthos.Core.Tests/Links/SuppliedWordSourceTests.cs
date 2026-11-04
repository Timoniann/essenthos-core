using Essenthos.Core.Conversion;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Loading.Frame;
using Essenthos.Core.Loading.Links;
using Essenthos.Core.Mapping;
using Essenthos.Core.Products;
using FluentAssertions;
using Xunit;

namespace Essenthos.Core.Tests.Links;

/// <summary>
/// The rows the editions' own marks give a pair, made from the words products and the pair's other method files with no
/// database: each marked run of a verse absent from a witness in the language it translated, a marked word another link
/// renders left rendered unless only the aligner's guess over marked words renders it; the King James's italics one row a
/// word; and what a pair already says read with the two texts' words kept apart.
/// </summary>
public sealed class SuppliedWordSourceTests : IDisposable
{
    private const string Pair = "RUSV-BHSA";
    private const string Synodal = "Russian Synodal, the words its square brackets mark as the translators' own";

    private readonly string _root = Path.Combine(Path.GetTempPath(), "essenthos-products", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, true);
        }
    }

    [Fact]
    public void Each_marked_run_is_absent_from_a_witness_in_the_language_translated_that_has_the_verse()
    {
        var store = Store();

        var rows = new SuppliedWordSource().Rows(new ProductTexts(store), Pair, _ => { }).ToList();

        rows.Select(row => row.From).Should().Equal("1.1:1.2 1.1:1.3", "1.1:1.5");
        rows.Should().AllSatisfy(row =>
        {
            row.To.Should().BeEmpty();
            row.Relation.Should().Be("expands");
            row.Source.Should().Be(Synodal);
        });
    }

    [Fact]
    public void A_marked_word_another_link_renders_stays_rendered_unless_only_the_aligner_guesses_it_over_marked_words()
    {
        var store = Store();
        Method(store, MappingMethods.Strong, new MappingRow(null, "1.1:1.3", "1.1:1.2", "renders", MappingMethods.Strong, 0.9, "numbers", null, null, null));
        Method(store, MappingMethods.Aligner,
            new MappingRow(null, "1.1:1.5", "1.1:1.3", "renders", MappingMethods.Aligner, 0.4, "SIL.Machine ibm4", null, null, null),
            new MappingRow(null, "1.1:1.2", "1.1:1.1", "renders", MappingMethods.Aligner, 0.4, "SIL.Machine ibm4", null, null, null));
        Method(store, MappingMethods.Names, new MappingRow(null, "1.1:1.2", "1.1:1.1", "renders", MappingMethods.Names, null, "names", null, null, null));

        var rows = new SuppliedWordSource().Rows(new ProductTexts(store), Pair, _ => { }).ToList();

        rows.Select(row => row.From).Should().Equal("1.1:1.5");
    }

    [Fact]
    public void The_king_james_italics_are_one_absence_a_word_in_the_new_testament()
    {
        var store = new ProductStore(_root);
        Text(store, "KJV", "translation", "eng",
            ("1.1:1", [("In", null), ("it", 1)]), ("40.1:1", [("The", null), ("book", null), ("is", 1), ("written", 1)]));
        Text(store, "NESTLE1904", "critical-edition", "grc", ("40.1:1", [("Βίβλος", null), ("γενέσεως", null)]));

        var rows = new KingJamesItalicsSource().Rows(new ProductTexts(store), "KJV-NESTLE1904", _ => { }).ToList();

        rows.Select(row => row.From).Should().Equal("40.1:1.3", "40.1:1.4");
        rows.Should().AllSatisfy(row => row.Relation.Should().Be("expands"));
    }

    [Fact]
    public void What_a_pair_says_names_each_texts_words_apart_though_their_places_in_the_products_are_the_same()
    {
        var store = new ProductStore(_root);
        Text(store, "RP2018", "critical-edition", "grc", ("40.1:1", [("Βίβλος", null), ("γενέσεως", null)]));
        Text(store, "TR1894", "printed-edition", "grc", ("40.1:1", [("Βιβλος", null), ("γενεσεως", null)]));
        Method(store, MappingMethods.Strong, new MappingRow(null, "40.1:1.2", "40.1:1.1", "renders", MappingMethods.Strong, 0.9, "numbers", null, null, null));
        var texts = new ProductTexts(store);

        var standing = PairStanding.Read(store, "RP2018-TR1894", [MappingMethods.Strong], texts["RP2018"], texts["TR1894"]);
        var offset = PairStanding.Offset(texts["RP2018"]);

        ByzantineApparatusLinkLoader.Contrary(new ApparatusLink('B', LinkRelation.Expands, [0], []), standing).Should().BeFalse();
        ByzantineApparatusLinkLoader.Contrary(new ApparatusLink('B', LinkRelation.Expands, [1], []), standing).Should().BeTrue();
        ByzantineApparatusLinkLoader.Contrary(new ApparatusLink('B', LinkRelation.Equals, [1], [0 + offset]), standing).Should().BeTrue();
    }

    /// <summary>The Synodal's Genesis 1:1 with two marked runs and its 1:2 with one, BHSA holding only 1:1; a marked word in Matthew too.</summary>
    private ProductStore Store()
    {
        var store = new ProductStore(_root);
        Text(store, "RUSV", "translation", "rus",
            ("1.1:1", [("В", null), ("начале", 1), ("сотворил", 1), ("Бог", null), ("[небо]", 2)]),
            ("1.1:2", [("Земля", null), ("[же]", 1)]),
            ("40.1:1", [("Родословие", null), ("[Иисуса]", 1)]));
        Text(store, "BHSA", "critical-edition", "hbo",
            ("1.1:1", [("בְּ", null), ("רֵאשִׁית", null), ("בָּרָא", null)]),
            ("40.1:1", [("x", null)]));
        return store;
    }

    private static void Text(ProductStore store, string text, string kind, string language,
        params (string Verse, (string Surface, int? Supplied)[] Words)[] verses)
    {
        using (var writer = store.Create(TextWord.ProductOf(text), ProductKind.Machine, TextWord.Schema(text), "test", []))
        {
            writer.WriteAll(verses.SelectMany(verse => verse.Words.Select((word, n) => new TextWord(
                new WordKey(text, VerseKey.Parse(verse.Verse), n + 1), null, word.Surface, " ", null, null, null, null, null, null, false, null,
                word.Supplied, null))));
        }

        using (var frame = store.Create(FrameFile.Product(text), ProductKind.Machine, FrameRow.Schema, "test", []))
        {
            frame.WriteAll([]);
        }

        using var declaration = store.Create(TextField.ProductOf(text), ProductKind.Hand, TextField.Schema, "test", []);
        declaration.WriteAll([new TextField("slug", text), new TextField("kind", kind), new TextField("language", language)]);
    }

    private static void Method(ProductStore store, string method, params MappingRow[] rows)
    {
        var pair = rows[0].From.StartsWith("40.", StringComparison.Ordinal) ? "RP2018-TR1894" : Pair;
        using var writer = store.Create(MappingProducts.Of(pair, method), ProductKind.Machine, MappingRow.Schema, "test", []);
        writer.WriteAll(rows);
    }
}
