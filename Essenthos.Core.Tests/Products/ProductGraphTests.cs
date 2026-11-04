using Essenthos.Core.Build;
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
