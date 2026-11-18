using Essenthos.Core.Build;
using Essenthos.Core.Conversion;
using Essenthos.Core.Products;
using Essenthos.Core.Verbs;
using FluentAssertions;
using Xunit;

namespace Essenthos.Core.Tests.Products;

/// <summary>The declared graph of the build: a product reading what its line does not declare fails, and the producers run in input order.</summary>
public sealed class ProductGraphTests : IDisposable
{
    private sealed record Row(string Value);

    private static readonly ProductSchema<Row> Schema = new ProductSchema<Row>().Text("value", r => r.Value).Reads(r => new Row(r.Text(0)));

    private readonly string _root = Path.Combine(Path.GetTempPath(), "essenthos-products", Guid.NewGuid().ToString("N"));

    private static readonly ProductGraph Graph = new(
    [
        new("texts/{text}/corrections", ProductMaker.Hand, "by hand", [], [], "none"),
        new("texts/{text}/words", ProductMaker.Build, "convert all", ["Essenthos/texts/{text}/corrections", "bible4u/*"], ["word"], "text"),
        new("mapping/{from}-{to}/final", ProductMaker.Build, "merge --all", ["Essenthos/texts/{from}/words", "Essenthos/texts/{to}/words"], ["link"], "pair"),
        new("texts/{text}/index/strong", ProductMaker.Build, "indexes", ["Essenthos/texts/{text}/words", "Essenthos/mapping/*/final"], ["strong_rendering"], "text"),
    ]);

    public ProductGraphTests()
    {
        Directory.CreateDirectory(Path.Combine(_root, "bible4u"));
        File.WriteAllText(Path.Combine(_root, "bible4u", "kjv.xml"), "<kjv/>");
        File.WriteAllText(Path.Combine(_root, "secret.txt"), "not declared");
    }

    private void Make(ProductStore store, string product, params string[] inputs)
    {
        using var writer = store.Create(product, ProductKind.Machine, Schema, "test", store.Inputs(inputs));
        writer.Write(new Row(product));
        writer.Complete();
    }

    [Fact]
    public void A_product_reading_only_what_its_line_declares_passes()
    {
        var store = new ProductStore(_root);
        Make(store, "texts/KJV/words", "bible4u/kjv.xml");
        Make(store, "texts/BHSA/words", "bible4u/kjv.xml");
        Make(store, "mapping/KJV-BHSA/final", "Essenthos/texts/KJV/words.essx", "Essenthos/texts/BHSA/words.essx");

        Graph.Check(store.Manifest, _ => []).Should().BeEmpty();
    }

    [Fact]
    public void A_product_reading_an_undeclared_input_fails()
    {
        var store = new ProductStore(_root);
        Make(store, "texts/KJV/words", "bible4u/kjv.xml", "secret.txt");

        var found = Graph.Check(store.Manifest, _ => []);

        found.Should().ContainSingle().Which.Input.Should().Be("secret.txt");
    }

    [Fact]
    public void A_placeholder_binds_the_same_text_across_the_line()
    {
        var store = new ProductStore(_root);
        Make(store, "texts/KJV/words", "bible4u/kjv.xml");
        Make(store, "texts/BHSA/words", "bible4u/kjv.xml");
        Make(store, "mapping/KJV-BHSA/final", "Essenthos/texts/KJV/words.essx", "Essenthos/texts/BHSA/words.essx");
        Make(store, "texts/YLT/words", "bible4u/kjv.xml");
        Make(store, "texts/KJV/index/strong", "Essenthos/texts/YLT/words.essx");

        Graph.Check(store.Manifest, _ => []).Should().ContainSingle().Which.Input.Should().Be("Essenthos/texts/YLT/words.essx");
    }

    [Fact]
    public void A_product_no_line_declares_fails()
    {
        var store = new ProductStore(_root);
        Make(store, "names/projected/KJV");

        Graph.Check(store.Manifest, _ => []).Should().ContainSingle().Which.Why.Should().Contain("no line");
    }

    [Fact]
    public void A_text_may_read_its_catalogued_sources()
    {
        var store = new ProductStore(_root);
        var graph = new ProductGraph([new("texts/{text}/words", ProductMaker.Build, "convert all", ["{sources}"], ["word"], "text")]);
        Make(store, "texts/KJV/words", "bible4u/kjv.xml");

        graph.Check(store.Manifest, text => text == "KJV" ? ["bible4u/kjv.xml"] : []).Should().BeEmpty();
        graph.Check(store.Manifest, _ => []).Should().ContainSingle();
    }

    [Fact]
    public void Producers_run_after_what_they_read()
    {
        Graph.Order().Should().Equal("convert all", "merge --all", "indexes");
    }

    [Fact]
    public void A_producer_reading_one_texts_product_runs_after_the_producer_of_that_product()
    {
        var graph = new ProductGraph(
        [
            new("tables/word_strong", ProductMaker.Build, "tables", ["Essenthos/texts/BSB/frame"], ["word_strong"], "all"),
            new("texts/{text}/frame", ProductMaker.Build, "frame-files", ["Essenthos/texts/{text}/words"], ["verse_reference"], "text"),
            new("texts/{text}/words", ProductMaker.Build, "convert all", ["bible4u/*"], ["word"], "text"),
        ]);

        graph.Order().Should().Equal("convert all", "frame-files", "tables");
    }

    [Fact]
    public void Producers_that_read_nothing_of_each_other_need_nothing_of_each_other()
    {
        var graph = new ProductGraph(
        [
            new("texts/{text}/words", ProductMaker.Build, "convert all", ["bible4u/*"], ["word"], "text"),
            new("texts/{text}/frame", ProductMaker.Build, "frame-files", ["Essenthos/texts/{text}/words"], ["verse_reference"], "text"),
            new("tables/word_strong", ProductMaker.Build, "tables", ["Essenthos/texts/BSB/words"], ["word_strong"], "all"),
            new("mapping/{from}-{to}/final", ProductMaker.Build, "merge --all", ["Essenthos/texts/{from}/frame", "Essenthos/tables/word_strong"], ["link"], "pair"),
        ]);

        var needs = graph.Needs();

        needs.Keys.Should().Equal("convert all", "frame-files", "tables", "merge --all");
        needs["convert all"].Should().BeEmpty();
        needs["frame-files"].Should().BeEquivalentTo(["convert all"]);
        needs["tables"].Should().BeEquivalentTo(["convert all"]);
        needs["merge --all"].Should().BeEquivalentTo(["frame-files", "tables"]);
    }

    [Fact]
    public void A_missing_kept_product_the_manifest_records_is_named()
    {
        var store = new ProductStore(_root);
        var graph = new ProductGraph(
        [
            new("mapping/pairs", ProductMaker.Kept, "mapping-export", [], [], "none"),
            new("mapping/{from}-{to}/final", ProductMaker.Build, "merge --all", [], ["link"], "pair"),
            new("mapping/{from}-{to}/*", ProductMaker.Kept, "mapping-export", [], [], "none"),
        ]);
        Make(store, "mapping/pairs");
        Make(store, "mapping/KJV-BHSA/aligner");
        Make(store, "mapping/KJV-BHSA/composed");
        Make(store, "mapping/KJV-BHSA/final");

        graph.Missing(store).Should().BeEmpty();
        graph.Kept(store, "mapping").Should().Equal("mapping/KJV-BHSA/aligner", "mapping/KJV-BHSA/composed", "mapping/pairs");

        File.Delete(store.FullPath("mapping/KJV-BHSA/aligner", ProductKind.Machine));
        File.Delete(store.FullPath("mapping/KJV-BHSA/final", ProductKind.Machine));

        graph.Missing(store).Should().ContainSingle().Which.Should().StartWith("mapping/KJV-BHSA/aligner (kept",
            "a build product is made again; a kept one is not, and the build must not run without it");
    }

    [Fact]
    public void A_kept_product_that_names_one_file_must_be_there_even_with_no_manifest()
    {
        var graph = new ProductGraph(
        [
            new("mapping/pairs", ProductMaker.Kept, "mapping-export", [], [], "none"),
            new("build/kept-once", ProductMaker.Kept, "an-export", [], [], "none"),
        ]);
        var store = new ProductStore(_root);

        graph.Missing(store).Should().HaveCount(2);

        Make(store, "mapping/pairs");
        Directory.CreateDirectory(Path.Combine(_root, "Essenthos", "build"));
        File.WriteAllText(Path.Combine(_root, "Essenthos", "build", "kept-once"), "1000");

        graph.Missing(store).Should().BeEmpty();
    }

    [Fact]
    public void A_served_product_made_under_an_older_line_is_forgotten_and_a_built_one_still_fails()
    {
        var graph = new ProductGraph(
        [
            new("texts/{text}/words", ProductMaker.Build, "convert all", ["bible4u/*"], ["word"], "text"),
            new("panes/{text}", ProductMaker.Serve, "panes", ["Essenthos/texts/{text}/words"], ["pane"], "text"),
        ]);
        var store = new ProductStore(_root);
        Make(store, "texts/KJV/words", "bible4u/kjv.xml", "secret.txt");
        Make(store, "panes/KJV", "Essenthos/texts/KJV/words.essx", "secret.txt");

        graph.ForgetOutdatedServed(store).Should().Equal(store.RelativePath("panes/KJV", ProductKind.Machine));
        store.Manifest.Get(store.RelativePath("panes/KJV", ProductKind.Machine)).Should().BeNull("the serving build makes it again");
        graph.Check(store.Manifest, _ => []).Should().ContainSingle().Which.Product.Should().EndWith("texts/KJV/words.essx");
    }

    [Fact]
    public void A_product_no_line_declares_is_forgotten_once_its_file_is_gone()
    {
        var graph = new ProductGraph([new("mapping/pairs", ProductMaker.Kept, "mapping-export", [], [], "none")]);
        var store = new ProductStore(_root);
        Make(store, "mapping/pairs");
        Make(store, "texts/KJV/ids");

        graph.Forget(store).Should().BeEmpty("a file still there is the graph check's to name");

        File.Delete(store.FullPath("texts/KJV/ids", ProductKind.Machine));
        var relative = store.RelativePath("texts/KJV/ids", ProductKind.Machine);
        graph.Forget(store).Should().Equal(relative);
        store.Manifest.Get(relative).Should().BeNull();
        graph.Check(store.Manifest).Should().BeEmpty();
    }

    [Fact]
    public void The_checked_in_graph_keeps_what_no_producer_makes_and_carries_what_people_edit()
    {
        var graph = ProductGraph.Read(new ProductStore(TestResources.Shipped(".")));

        foreach (var kept in new[] { "mapping/pairs", "mapping/KJV-GRCBRENT/aligner", "mapping/KJV-BHSA/composed", "mapping/KJV-BHSA/hashes",
                     "external/openbible/records" })
        {
            graph.Find(kept)!.Value.Declaration.Maker.Should().Be(ProductMaker.Kept, kept);
        }

        foreach (var hand in new[] { "records/records", "records/facts/clauses", "names/BHSA", "mapping/KJV-BHSA/rulings" })
        {
            graph.Find(hand)!.Value.Declaration.Maker.Should().Be(ProductMaker.Hand, hand);
        }

        foreach (var (made, producer) in new[]
                 {
                     ("mapping/KJV-BHSA/stated", "stated-pairs"), ("records/derived/occupation-words", "occupations"),
                     ("records/facts/person-occupations", "occupations"), ("records/i18n/ukr/occupations", "occupations"),
                     ("records/derived/parallels", "cross-references"), ("external/tsk/cross-references", "cross-references"),
                     ("external/bibledata/topic-references", "topics"),
                 })
        {
            graph.Find(made)!.Value.Declaration.Should().Match<ProductDeclaration>(d => d.Maker == ProductMaker.Build && d.Producer == producer, made);
        }

        graph.Find("mapping/DRA-VULGCLEM/aligner")!.Value.Declaration.Maker.Should().Be(ProductMaker.Build, "its stored answer reproduces the kept rows");
        graph.Declarations.Should().NotContain(d => d.Producer.StartsWith("names-from-database") || d.Producer.StartsWith("names-export"));
        graph.Declarations.Should().NotContain(d => d.Producer.StartsWith("texts ids") || d.Product.EndsWith("/ids", StringComparison.Ordinal),
            "a word's id is computed from its key, never kept");
        graph.Find(WordIdCollision.Product)!.Value.Declaration.Producer.Should().StartWith("convert");
    }

    [Fact]
    public void The_checked_in_graph_reads_and_names_only_verbs_the_forge_has()
    {
        var graph = ProductGraph.Read(new ProductStore(TestResources.Shipped(".")));

        graph.Declarations.Should().NotBeEmpty();
        foreach (var producer in graph.Order())
        {
            ForgeVerbs.Find(producer.Split(' ')[0]).Should().NotBeNull(producer);
        }

        graph.Find("Essenthos/texts/KJV/words.essx")!.Value.Declaration.Tables.Should().Contain("word");
        graph.Find("Essenthos/mapping/KJV-BHSA/final.essx")!.Value.Bound.Should().Contain("from", "KJV").And.Contain("to", "BHSA");
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}
