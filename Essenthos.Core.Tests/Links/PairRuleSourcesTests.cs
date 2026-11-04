using Essenthos.Core.Build;
using Essenthos.Core.Conversion;
using Essenthos.Core.Loading.Frame;
using Essenthos.Core.Mapping;
using Essenthos.Core.Products;
using FluentAssertions;
using Xunit;

namespace Essenthos.Core.Tests.Links;

/// <summary>
/// The rules over a pair's links remade from files, with no database: the possessive beside a word linked to a suffixed
/// Hebrew word, and the names of a verse paired by spelling and order, each reading the pair's earlier method files.
/// </summary>
public sealed class PairRuleSourcesTests : IDisposable
{
    private const string Suffix = """{"pos": "subs", "suffixPerson": "p3", "suffixNumber": "sg", "suffixGender": "m"}""";

    private readonly string _root = Path.Combine(Path.GetTempPath(), "essenthos-products", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, true);
        }
    }

    [Fact]
    public void A_possessive_beside_a_word_linked_to_a_suffixed_word_it_agrees_with_is_linked_to_that_word()
    {
        var store = new ProductStore(_root);
        Plain(store, "RUSV", "rus", "translation", ("1.1:1", ["и", "сына", "своего"]), ("1.1:2", ["сына", "моего"]),
            ("1.1:3", ["сына", "его"]));
        Text(store, "BHSA", "hbo", "critical-edition", ("1.1:1", [("וַ", null), ("בְּנֹו", Suffix)]), ("1.1:2", [("בְּנֹו", Suffix)]),
            ("1.1:3", [("בְּנֹו", Suffix), ("אֹתֹו", null)]));
        Method(store, "RUSV-BHSA", MappingMethods.Composed,
            Row("1.1:1.2", "1.1:1.2"), Row("1.1:2.1", "1.1:2.1"), Row("1.1:3.1", "1.1:3.1"), Row("1.1:3.2", "1.1:3.2"));

        var rows = Write(store, "RUSV-BHSA", MappingMethods.Possessive);

        rows.Should().ContainSingle().Which.Should().Match<MappingRow>(row =>
            row.From == "1.1:1.3" && row.To == "1.1:1.2" && row.Method == MappingMethods.Possessive && row.Confidence == 0.8);
    }

    [Fact]
    public void The_names_of_a_verse_no_link_joins_are_paired_where_nothing_states_the_translation_s_name()
    {
        var store = new ProductStore(_root);
        Plain(store, "ASV", "eng", "translation", ("1.12:4", ["So", "Abram", "went"]), ("1.12:5", ["And", "Abram", "took"]));
        Text(store, "BHSA", "hbo", "critical-edition",
            ("1.12:4", [("וַיֵּלֶךְ", null), ("אַבְרָם", """{"pos": "nmpr", "consonantal": "אברם"}""")]),
            ("1.12:5", [("וַיִּקַּח", null), ("אַבְרָם", """{"pos": "nmpr", "consonantal": "אברם"}""")]));
        Method(store, "ASV-BHSA", MappingMethods.Composed, Row("1.12:4.3", "1.12:4.1"), Row("1.12:5.3", "1.12:5.1"));
        Method(store, "ASV-BHSA", MappingMethods.Stated, Row("1.12:5.2", "1.12:5.1"));

        var rows = Write(store, "ASV-BHSA", MappingMethods.NameList);

        rows.Should().ContainSingle("a name another method answers for is left to it").Which.Should().Match<MappingRow>(row =>
            row.From == "1.12:4.2" && row.To == "1.12:4.2" && row.Method == MappingMethods.NameList && row.Confidence == 0.9);
    }

    private static List<MappingRow> Write(ProductStore store, string pair, string method)
    {
        var outcome = PairSources.Write(new ProductTexts(store), method, pair, force: true, _ => { });
        outcome.Written.Should().NotBeNull();
        var graph = Path.Combine(store.ResourcesPath, ProductStore.EssenthosFolder, "build", "products.tsv");
        Directory.CreateDirectory(Path.GetDirectoryName(graph)!);
        File.Copy(TestResources.Shipped(Path.Combine(ProductStore.EssenthosFolder, "build", "products.tsv")), graph, overwrite: true);
        ProductGraph.Read(store).Check(store.Manifest, _ => []).Should().BeEmpty("every file the source reads is declared on its line");
        using var reader = store.Open(MappingProducts.Of(pair, method), MappingRow.Schema);
        return reader.ReadAll();
    }

    private static MappingRow Row(string from, string to) =>
        new(null, from, to, "renders", MappingMethods.Composed, 0.5, "SIL.Machine, aligned through KJV", null, null, null);

    private static void Method(ProductStore store, string pair, string method, params MappingRow[] rows)
    {
        using var writer = store.Create(MappingProducts.Of(pair, method), ProductKind.Machine, MappingRow.Schema, "test", []);
        writer.WriteAll(rows.Select(row => row with { Method = method }));
    }

    private static void Plain(ProductStore store, string slug, string language, string kind, params (string Verse, string[] Words)[] verses) =>
        Text(store, slug, language, kind, [.. verses.Select(verse => (verse.Verse, verse.Words.Select(word => (word, (string?)null)).ToArray()))]);

    private static void Text(ProductStore store, string slug, string language, string kind,
        params (string Verse, (string Surface, string? Morphology)[] Words)[] verses)
    {
        using (var declared = store.Create(TextField.ProductOf(slug), ProductKind.Hand, TextField.Schema, "test", []))
        {
            declared.WriteAll([new TextField("slug", slug), new TextField("kind", kind), new TextField("language", language),
                new TextField("versification", "kjv")]);
        }

        using (var writer = store.Create(TextWord.ProductOf(slug), ProductKind.Machine, TextWord.Schema(slug), "test", []))
        {
            writer.WriteAll(verses.SelectMany(verse => verse.Words.Select((word, n) => new TextWord(
                new WordKey(slug, VerseKey.Parse(verse.Verse), n + 1), null, word.Surface, " ", null, null, null, word.Morphology,
                null, null, false, null, null, null))));
        }

        using var frame = store.Create(FrameFile.Product(slug), ProductKind.Machine, FrameRow.Schema, "test", []);
        frame.WriteAll([]);
    }
}
