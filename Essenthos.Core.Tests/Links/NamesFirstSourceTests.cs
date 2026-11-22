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

    public NamesFirstSourceTests() => ShippedStrongNumbers.CopyInto(_root);

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

    /// <summary>
    /// An aligner the pair's floors refuse keeps no name off a word (PRB-1957): GNV1599-BHSA, given a model it holds refused, lost
    /// Beth-hashittah and Ebrewes to guesses on the wrong Hebrew word. An aligner the floors admit still does.
    /// </summary>
    [Fact]
    public void A_word_only_a_refused_aligner_links_takes_its_name()
    {
        var store = Store();
        Method(store, MappingMethods.Aligner, new MappingRow("1.12:4.2", "1.12:4.1", "renders", MappingMethods.Aligner, 0.98,
            "SIL.Machine ibm4", null, null, null));
        Write(store).Should().BeEmpty("an aligner the floors admit is a link");

        Floors(store, "aligner\trefused\t0.53\t40\t21 of 40 right");

        Write(store).Should().ContainSingle().Which.To.Should().Be("1.12:4.2");
    }

    /// <summary>
    /// A composed row the pair's hand floors refuse keeps no name off its word either (PRB-1957): JPS1917-BHSA's redraw covered 46
    /// names with links only through the King James, which its floors refuse, and the words were left bare.
    /// </summary>
    [Fact]
    public void A_word_only_a_refused_composed_row_links_takes_its_name()
    {
        var store = Store();
        Method(store, MappingMethods.Composed, new MappingRow("1.12:4.2", "1.12:4.2", "renders", MappingMethods.Composed, 0.9,
            "SIL.Machine, aligned through KJV", null, null, null));
        Write(store).Should().BeEmpty("a composed link the floors admit is a link");

        Floors(store, "through kjv\trefused\t0.5\t40\t20 of 40 right");

        Write(store).Should().ContainSingle().Which.To.Should().Be("1.12:4.2");
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
        Consensus(store, "DRA", ("1.15:2.1", "yhvh"), ("1.15:2.2", "yhvh"), ("1.15:2.3", "abram"));

        Write(store).Should().ContainSingle().Which.From.Should().Be("1.15:2.3", "Lord God is a title rendered word by word");
    }

    /// <summary>
    /// A creature or a god named by a proper noun is placed like a person (PRB-1769): the Ohienko Bible's «Рагава» of Psalm
    /// 89:10 had no link to BHSA's רַהַב. A creature the original names by a common noun is no name of the verse and is not placed.
    /// </summary>
    [Fact]
    public void A_being_named_by_a_proper_noun_is_placed_and_one_named_by_a_common_noun_is_not()
    {
        var store = new ProductStore(_root);
        Text(store, "DRA", "eng", ("19.89:10", [("Thou", null), ("hast", null), ("humbled", null), ("Rahab", null)]));
        Text(store, "BHSA", "hbo", ("19.89:10", [("דִכִּאתָ", "דכא"), ("רַהַב", "רַהַב")]));
        Records(store, ("rahab-sea-monster", "being"));
        Settled(store, ("19.89:10.2", "rahab-sea-monster"));
        Consensus(store, "DRA", ("19.89:10.4", "rahab-sea-monster"));

        Write(store).Should().ContainSingle().Which.Should().Match<MappingRow>(row => row.From == "19.89:10.4" && row.To == "19.89:10.2");

        Text(store, "DRA", "eng", ("19.22:21", [("the", null), ("unicorns", null)]));
        Text(store, "BHSA", "hbo", ("19.22:21", [("רֵמִים", null)]));
        Records(store, ("reem", "being"));
        Settled(store, ("19.22:21.1", "reem"));
        Consensus(store, "DRA", ("19.22:21.2", "reem"));

        Write(store).Should().BeEmpty("a creature named by a common noun is no name of the verse");
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
        Consensus(store, "DRA", ("1.12:4.2", "abram-old"));
        Folds(store, ("abram-old", "abram"));

        Write(store).Should().ContainSingle().Which.Evidence.Should().Be("entity+letters");
    }

    [Fact]
    public void Pairs_of_one_run_place_what_each_places_alone()
    {
        var store = Store();
        Text(store, "KJV", "eng", ("1.12:4", [("So", null), ("Abram", null), ("departed", null)]));
        Consensus(store, "KJV", ("1.12:4.2", "abram"));
        string[] pairs = ["DRA-BHSA", "KJV-BHSA"];
        var source = new NamesFirstSource();
        var alone = pairs.Select(pair => source.Rows(new ProductTexts(store), pair, _ => { }).ToList()).ToList();

        var texts = new ProductTexts(store);
        var together = pairs.Select(pair => source.Rows(texts, pair, _ => { }).ToList()).ToList();

        alone.Should().AllSatisfy(rows => rows.Should().ContainSingle());
        together.Should().BeEquivalentTo(alone, options => options.WithStrictOrdering(),
            "what the pairs of a reference share is the original's side, never one translation's");
    }

    private ProductStore Store()
    {
        var store = new ProductStore(_root);
        Text(store, "DRA", "eng", ("1.12:4", [("So", null), ("Abram", null), ("went", null)]));
        Text(store, "BHSA", "hbo", ("1.12:4", [("וַיֵּלֶךְ", "הלך"), ("אַבְרָם", "אַבְרָם")]));
        Records(store, ("abram", "person"));
        Settled(store, ("1.12:4.2", "abram"));
        Consensus(store, "DRA", ("1.12:4.2", "abram"));
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

    private static void Floors(ProductStore store, string line) =>
        File.WriteAllLines(Path.Combine(store.ResourcesPath, ProductStore.EssenthosFolder, MappingProducts.Folder, "DRA-BHSA", "floors.tsv"),
            ["# producer   by hand", "readings\tfrom\tright\tscored\tbasis", line]);

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

    private static void Consensus(ProductStore store, string slug, params (string Own, string Record)[] names)
    {
        using var writer = store.Create(NameProducts.Consensus(slug), ProductKind.Hand, ConsensusName.Schema(slug), "test", []);
        writer.WriteAll(names.Select(name => new ConsensusName(WordKey.Parse(name.Own, slug), name.Record)));
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
