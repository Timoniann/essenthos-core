using Essenthos.Core.Conversion;
using Essenthos.Core.Indexes;
using Essenthos.Core.Mapping;
using Essenthos.Core.Products;
using FluentAssertions;
using Xunit;

namespace Essenthos.Core.Tests.Products;

/// <summary>
/// Every word's one Strong number for marking (TSK-1822): its own, else an original's it renders, else what a translation's word
/// it renders carries by the same rule, written per text as <c>texts/&lt;SLUG&gt;/numbers</c> and made again only when the
/// words, the finals or the numbers of a text it reads moved.
/// </summary>
public sealed class StrongNumberFileTests : IDisposable
{
    private readonly string _resources = Path.Combine(Path.GetTempPath(), "essenthos-products", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_resources))
        {
            Directory.Delete(_resources, recursive: true);
        }
    }

    private static void Declare(ProductStore store, string slug, string language, string kind)
    {
        using var writer = store.Create(TextField.ProductOf(slug), ProductKind.Hand, TextField.Schema, "test", []);
        writer.WriteAll([new TextField("slug", slug), new TextField("kind", kind), new TextField("language", language)]);
    }

    private static void Words(ProductStore store, string slug, params (string Surface, string? Strong)[] words)
    {
        using var writer = store.Create(TextWord.ProductOf(slug), ProductKind.Machine, TextWord.Schema(slug), "test", []);
        writer.WriteAll(words.Select((w, at) => new TextWord(new WordKey(slug, new VerseKey(1, 1, 1), at + 1),
            w.Surface, " ", null, w.Strong, null, null, w.Surface.ToLowerInvariant(), null, false, null, null, null)));
    }

    private static void Final(ProductStore store, string pair, params FinalRow[] rows)
    {
        using var writer = store.Create(MappingProducts.Of(pair, MappingProducts.Final), ProductKind.Machine, FinalRow.Schema, "test", []);
        writer.WriteAll(rows);
    }

    private static FinalRow Link(long id, string from, string to, double? confidence = null, string relation = "renders") =>
        new(id, from, to, relation, null, true, "aligner", confidence, "test");

    /// <summary>
    /// HEB numbers its words; TAG, a translation, tags one of its own; ENG reaches HEB for one word and TAG alone for two:
    /// one TAG word carrying its own tag, one carrying what it renders of HEB.
    /// </summary>
    private ProductStore Corpus()
    {
        var store = new ProductStore(_resources);
        Declare(store, "HEB", "hbo", "critical-edition");
        Declare(store, "TAG", "eng", "translation");
        Declare(store, "ENG", "eng", "translation");
        Words(store, "HEB", ("בְּרֵאשִׁית", "H7225"), ("בָּרָא", "H1254"), ("אֱלֹהִים", "H430"));
        Words(store, "TAG", ("beginning", null), ("created", "H1254"), ("God", "H433"));
        Words(store, "ENG", ("beginning", null), ("made", null), ("God", null), ("then", null));
        Final(store, "TAG-HEB", Link(1, "1.1:1.1", "1.1:1.1"), Link(2, "1.1:1.3", "1.1:1.3"));
        Final(store, "ENG-TAG", Link(3, "1.1:1.1", "1.1:1.1"), Link(4, "1.1:1.2", "1.1:1.2", 0.5));
        Final(store, "ENG-HEB", Link(5, "1.1:1.3", "1.1:1.3", 0.4));
        store.Save();
        return store;
    }

    private static List<StrongNumberRow> Numbers(ProductStore store, string text)
    {
        using var reader = store.Open(StrongNumberFiles.Product(text), StrongNumberRow.Schema(text));
        return [.. reader.Rows()];
    }

    private static WordKey Key(string text, int position) => new(text, new VerseKey(1, 1, 1), position);

    [Fact]
    public void AWordKeepsItsOwnNumberElseAnOriginalsElseWhatATranslationsWordCarries()
    {
        var store = Corpus();
        StrongNumberFiles.Make(new IndexContext(store), force: false, parallel: 2);

        Numbers(store, "TAG").Should().Equal(
            new StrongNumberRow(Key("TAG", 1), "H7225", 1, "HEB 1.1:1.1", null, null, null),
            new StrongNumberRow(Key("TAG", 2), "H1254", 0, null, null, null, null),
            new StrongNumberRow(Key("TAG", 3), "H433", 0, null, null, "H430", "HEB 1.1:1.3"));
        Numbers(store, "ENG").Should().Equal(
            new StrongNumberRow(Key("ENG", 1), "H7225", 2, "TAG 1.1:1.1", null, null, null),
            new StrongNumberRow(Key("ENG", 2), "H1254", 1, "TAG 1.1:1.2", 0.5, null, null),
            new StrongNumberRow(Key("ENG", 3), "H430", 1, "HEB 1.1:1.3", 0.4, null, null));
        Numbers(store, "HEB").Select(r => (r.Number, r.Hops)).Should().Equal(("H7225", 0), ("H1254", 0), ("H430", 0));
    }

    [Fact]
    public void TheDisagreementsAreCountedPerTextWithExamples()
    {
        var store = Corpus();
        StrongNumberFiles.Make(new IndexContext(store), force: false, parallel: 2);

        using var reader = store.Open(StrongNumberFiles.Disagreements, StrongDisagreementRow.Schema);
        reader.Rows().Should().Equal(
            new StrongDisagreementRow("HEB", 3, 0, 0, 0, 1, "1.1:1.3 H430, H433 by TAG 1.1:1.3"),
            new StrongDisagreementRow("TAG", 2, 1, 0, 0, 1, "1.1:1.3 H433, H430 by HEB 1.1:1.3"),
            new StrongDisagreementRow("ENG", 0, 1, 2, 1, 0, "-"));
    }

    [Fact]
    public void AFileIsMadeAgainWhenTheNumbersOfATextItReadsMove()
    {
        var store = Corpus();
        StrongNumberFiles.Make(new IndexContext(store), force: false, parallel: 2);
        StrongNumberFiles.Make(new IndexContext(new ProductStore(_resources)), force: false, parallel: 2)
            .Where(o => o.Made).Should().BeEmpty();

        var relinked = new ProductStore(_resources);
        Final(relinked, "ENG-HEB", Link(5, "1.1:1.3", "1.1:1.3", 0.3));
        relinked.Save();
        StrongNumberFiles.Make(new IndexContext(new ProductStore(_resources)), force: false, parallel: 2)
            .Where(o => o.Made).Select(o => o.Text).Should().BeEquivalentTo(["HEB", "ENG", "-"], "only the pair's two texts read its final");

        var retagged = new ProductStore(_resources);
        Words(retagged, "TAG", ("beginning", null), ("created", "H1254"), ("God", "H430"));
        retagged.Save();
        StrongNumberFiles.Make(new IndexContext(new ProductStore(_resources)), force: false, parallel: 2)
            .Where(o => o.Made).Select(o => o.Text).Should().BeEquivalentTo(["HEB", "TAG", "ENG", "-"],
                "HEB reads TAG's own numbers, and ENG the numbers TAG's file gives");
    }

    /// <summary>
    /// Of several numbers one link gives a word, the one its form carries where it carries one alone, else the rarest: the
    /// noun's of an article and its noun. A surer link wins over both, and a link under the sure line holds no word's own number.
    /// </summary>
    [Fact]
    public void AWordJoinedToSeveralNumberedWordsTakesTheNumberItsFormUsuallyCarriesElseTheRarest()
    {
        var lender = new ChainedNumbers.Lender(true, new Dictionary<string, (string, int)>
        {
            ["1"] = ("G3588", 0), ["2"] = ("G2316", 0), ["3"] = ("G3588", 0), ["4"] = ("H430", 0), ["5"] = ("H5656", 0), ["6"] = ("H430", 0),
        });
        var numbered = ChainedNumbers.Resolve(
            [("a", null, "god"), ("b", null, "the"), ("c", "G2962", "lord"), ("d", null, "神"), ("e", null, "神")],
            [new ChainedNumbers.Joined(["a"], "GRC", ["1", "2"], 0.7), new ChainedNumbers.Joined(["b"], "GRC", ["2"], 0.6),
             new ChainedNumbers.Joined(["b"], "GRC", ["3"], 0.9), new ChainedNumbers.Joined(["c"], "GRC", ["2"], 0.8),
             new ChainedNumbers.Joined(["d"], "GRC", ["4", "5"], null), new ChainedNumbers.Joined(["e"], "GRC", ["6"], null)],
            new Dictionary<string, ChainedNumbers.Lender> { ["GRC"] = lender });

        numbered.Should().Equal(
            new ChainedNumbers.Numbered("a", "G2316", 1, "GRC 2", 0.7, null, null),
            new ChainedNumbers.Numbered("b", "G3588", 1, "GRC 3", 0.9, null, null),
            new ChainedNumbers.Numbered("c", "G2962", 0, null, null, null, null),
            new ChainedNumbers.Numbered("d", "H430", 1, "GRC 4", null, null, null),
            new ChainedNumbers.Numbered("e", "H430", 1, "GRC 6", null, null, null));
    }

    /// <summary>
    /// Editions of an original come before translations, the second text of a pair before the first, and a pair that would
    /// close a cycle is left out, so a text never reads numbers that were worked out from its own.
    /// </summary>
    [Fact]
    public void TextsAreNumberedInAnOrderWithoutCycles()
    {
        var order = ChainedNumbers.Order(
            new Dictionary<string, bool> { ["ENG"] = true, ["KJV"] = true, ["NA"] = false, ["TR"] = false, ["LXX"] = false },
            [("ENG", "KJV"), ("KJV", "NA"), ("NA", "TR"), ("TR", "NA"), ("LXX", "NA"), ("KJV", "LXX")]);

        order.Should().Equal("TR", "NA", "LXX", "KJV", "ENG");
    }
}
