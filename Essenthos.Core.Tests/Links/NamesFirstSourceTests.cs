using Essenthos.Core.Build;
using Essenthos.Core.Conversion;
using Essenthos.Core.Corpus;
using Essenthos.Core.Loading.Frame;
using Essenthos.Core.Loading.Links;
using Essenthos.Core.Mapping;
using Essenthos.Core.Names;
using Essenthos.Core.Products;
using FluentAssertions;
using Xunit;

namespace Essenthos.Core.Tests.Links;

/// <summary>
/// The names of a translation placed on the original's from files, with no database: whom the original's settled names
/// say its words name, whom the translation's verses say its words name (its consensus file), and
/// the pair's earlier method files, which keep a name off a word already linked.
/// </summary>
public sealed class NamesFirstSourceTests : IDisposable
{
    private const string Name = """{"pos": "nmpr"}""";

    private readonly string _root = Path.Combine(Path.GetTempPath(), "essenthos-products", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, true);
        }
    }

    [Fact]
    public void A_name_both_texts_say_the_word_names_is_placed_on_the_original_s_word()
    {
        var store = Store();

        var rows = Write(store);

        rows.Should().ContainSingle().Which.Should().Match<MappingRow>(row =>
            row.From == "1.12:4.2" && row.To == "1.12:4.2" && row.Method == MappingMethods.Names
            && row.Confidence == NamesFirstPass.Confidence && row.Source == NamesFirstPass.Source && row.Evidence == "entity+letters");
    }

    [Fact]
    public void A_word_an_earlier_method_file_links_takes_no_name()
    {
        var store = Store();
        Method(store, MappingMethods.Stated, new MappingRow("1.12:4.3", "1.12:4.2", "renders", MappingMethods.Stated, null,
            "a test", null, null, null));

        Write(store).Should().BeEmpty("the original's word is already linked in the pair");
    }

    [Fact]
    public void Adonai_beside_the_name_is_weighed_but_neither_is_placed()
    {
        var store = new ProductStore(_root);
        Text(store, "DRA", "eng", ("1.15:2", [("Lord", null), ("God", null), ("Abram", null)]));
        Text(store, "BHSA", "hbo",
            ("1.15:2", [("אֲדֹנָי", NamesFirstPass.Adonai), ("יְהוִה", NamesFirstPass.Yhwh), ("אַבְרָם", "אַבְרָם")]));
        Records(store, ("abram", "person"), ("yhvh", "person"));
        Settled(store, ("1.15:2.1", "yhvh"), ("1.15:2.2", "yhvh"), ("1.15:2.3", "abram"));
        Consensus(store, ("1.15:2.1", "yhvh"), ("1.15:2.2", "yhvh"), ("1.15:2.3", "abram"));

        Write(store).Should().ContainSingle().Which.From.Should().Be("1.15:2.3", "Lord God is a title rendered word by word");
    }

    [Fact]
    public void A_pair_whose_translation_has_no_consensus_file_refuses_to_run()
    {
        var store = Store();
        File.Delete(Path.Combine(store.ResourcesPath, store.Find(NameProducts.Consensus("DRA"))!));
        store.Save();

        var write = () => PairSources.Write(new ProductTexts(new ProductStore(_root)), MappingMethods.Names, "DRA-BHSA", force: true, _ => { });

        write.Should().Throw<InvalidOperationException>().WithMessage("*names/consensus/DRA is missing*",
            "without whom the verses say the words name, the pair would lose most of its names and the build would pass");
    }

    [Fact]
    public void A_consensus_row_naming_a_folded_record_is_read_as_the_record_it_went_into()
    {
        var store = Store();
        Consensus(store, ("1.12:4.2", "abram-old"));
        Folds(store, ("abram-old", "abram"));

        Write(store).Should().ContainSingle().Which.Evidence.Should().Be("entity+letters");
    }

    [Fact]
    public void Every_translation_names_first_places_has_its_consensus_file_in_the_checkout()
    {
        var folder = TestResources.Shipped(Path.Combine(ProductStore.EssenthosFolder, "names", "consensus"));
        var missing = NamesFirstSource.Placed
            .Select(pair => MappingProducts.Split(pair).From)
            .Distinct()
            .Where(text => !File.Exists(Path.Combine(folder, $"{text}.tsv")))
            .Order(StringComparer.Ordinal)
            .ToList();

        missing.Should().BeEmpty("a build from an export of a database built from files would place these texts' names without the consensus");
    }

    private ProductStore Store()
    {
        var store = new ProductStore(_root);
        Text(store, "DRA", "eng", ("1.12:4", [("So", null), ("Abram", null), ("went", null)]));
        Text(store, "BHSA", "hbo", ("1.12:4", [("וַיֵּלֶךְ", "הלך"), ("אַבְרָם", "אַבְרָם")]));
        Records(store, ("abram", "person"));
        Settled(store, ("1.12:4.2", "abram"));
        Consensus(store, ("1.12:4.2", "abram"));
        return store;
    }

    private static List<MappingRow> Write(ProductStore store)
    {
        var outcome = PairSources.Write(new ProductTexts(store), MappingMethods.Names, "DRA-BHSA", force: true, _ => { });
        outcome.Written.Should().NotBeNull();
        var graph = Path.Combine(store.ResourcesPath, ProductStore.EssenthosFolder, "build", "products.tsv");
        Directory.CreateDirectory(Path.GetDirectoryName(graph)!);
        File.Copy(TestResources.Shipped(Path.Combine(ProductStore.EssenthosFolder, "build", "products.tsv")), graph, overwrite: true);
        ProductGraph.Read(store).Check(store.Manifest, _ => []).Should().BeEmpty("every file the source reads is declared on its line");
        using var reader = store.Open(MappingProducts.Of("DRA-BHSA", MappingMethods.Names), MappingRow.Schema);
        return reader.ReadAll();
    }

    private static void Method(ProductStore store, string method, params MappingRow[] rows)
    {
        using var writer = store.Create(MappingProducts.Of("DRA-BHSA", method), ProductKind.Machine, MappingRow.Schema, "test", []);
        writer.WriteAll(rows);
    }

    private sealed record RecordRow(string Slug, string Kind)
    {
        public static readonly ProductSchema<RecordRow> Schema = new ProductSchema<RecordRow>()
            .Text("slug", r => r.Slug)
            .Text("kind", r => r.Kind)
            .OptionalText("another_name_for", _ => null)
            .Reads(row => new RecordRow(row.Text(0), row.Text(1)));
    }

    private static void Records(ProductStore store, params (string Slug, string Kind)[] records)
    {
        using var writer = store.Create(NamesFirstSource.RecordsProduct, ProductKind.Hand, RecordRow.Schema, "test", []);
        writer.WriteAll(records.Select(record => new RecordRow(record.Slug, record.Kind)));
    }

    private static void Settled(ProductStore store, params (string Own, string Record)[] names)
    {
        using var writer = store.Create(NameProducts.Settled("BHSA"), ProductKind.Hand, SettledName.Schema("BHSA"), "test", []);
        writer.WriteAll(names.Select(name =>
            new SettledName(WordKey.Parse(name.Own, "BHSA"), name.Record, NameRoles.Name, "manual", null, "a test", null)));
    }

    private static void Consensus(ProductStore store, params (string Own, string Record)[] names)
    {
        using var writer = store.Create(NameProducts.Consensus("DRA"), ProductKind.Hand, ConsensusName.Schema("DRA"), "test", []);
        writer.WriteAll(names.Select(name => new ConsensusName(WordKey.Parse(name.Own, "DRA"), name.Record)));
    }

    private sealed record FoldRow(string Slug, string Into)
    {
        public static readonly ProductSchema<FoldRow> Schema = new ProductSchema<FoldRow>()
            .Text("slug", r => r.Slug)
            .Text("folded_into", r => r.Into)
            .Reads(row => new FoldRow(row.Text(0), row.Text(1)));
    }

    private static void Folds(ProductStore store, params (string Slug, string Into)[] folds)
    {
        using var writer = store.Create(NamesFirstSource.FoldsProduct, ProductKind.Hand, FoldRow.Schema, "test", []);
        writer.WriteAll(folds.Select(fold => new FoldRow(fold.Slug, fold.Into)));
    }

    /// <param name="verses">Each verse's words: the surface and, for the original, its lemma (a proper noun).</param>
    private static void Text(ProductStore store, string slug, string language, params (string Verse, (string Surface, string? Lemma)[] Words)[] verses)
    {
        using (var declared = store.Create(TextField.ProductOf(slug), ProductKind.Hand, TextField.Schema, "test", []))
        {
            declared.WriteAll([new TextField("slug", slug), new TextField("kind", language == "hbo" ? "critical-edition" : "translation"),
                new TextField("language", language), new TextField("versification", "kjv")]);
        }

        using (var writer = store.Create(TextWord.ProductOf(slug), ProductKind.Machine, TextWord.Schema(slug), "test", []))
        {
            writer.WriteAll(verses.SelectMany(verse => verse.Words.Select((word, n) => new TextWord(
                new WordKey(slug, VerseKey.Parse(verse.Verse), n + 1), word.Surface, " ", word.Lemma, null, null,
                word.Lemma is null ? null : Name, null, null, false, null, null, null))));
        }

        using var frame = store.Create(FrameFile.Product(slug), ProductKind.Machine, FrameRow.Schema, "test", []);
        frame.WriteAll([]);
    }
}
