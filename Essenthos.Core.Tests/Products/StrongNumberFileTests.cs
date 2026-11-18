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
    /// The relative שֶׁ is counted with אֲשֶׁר: a word joined in one link to שֶׁ and the verb after it takes the verb's number,
    /// though שֶׁ is the rarer, unless אֲשֶׁר is the number its form most usually carries; a form that renders אֲשֶׁר only
    /// sometimes ('is' of 'which is') still takes the verb's.
    /// </summary>
    [Fact]
    public void TheRelativeParticleIsCountedWithAsherWhenAWordIsJoinedToItAndAVerb()
    {
        var lender = new ChainedNumbers.Lender(true, new Dictionary<string, (string, int)>
        {
            ["1"] = ("H7945", 0), ["2"] = ("H1961", 0), ["3"] = ("H834", 0), ["4"] = ("H834", 0), ["5"] = ("H834", 0), ["6"] = ("H1961", 0),
            ["7"] = ("H2654", 0), ["8"] = ("H7945", 0),
        });
        var numbered = ChainedNumbers.Resolve(
            [("a", null, "preceded"), ("b", null, "which"), ("c", null, "which"), ("d", null, "which"), ("e", null, "is"), ("f", null, "is"),
             ("g", null, "is"), ("h", null, "is")],
            [new ChainedNumbers.Joined(["a", "b"], "HEB", ["1", "2"], null), new ChainedNumbers.Joined(["c"], "HEB", ["3"], null),
             new ChainedNumbers.Joined(["d"], "HEB", ["4"], null), new ChainedNumbers.Joined(["e"], "HEB", ["8", "7"], null),
             new ChainedNumbers.Joined(["f"], "HEB", ["5"], null), new ChainedNumbers.Joined(["g"], "HEB", ["2"], null),
             new ChainedNumbers.Joined(["h"], "HEB", ["6"], null)],
            new Dictionary<string, ChainedNumbers.Lender> { ["HEB"] = lender });

        numbered.Should().Equal(
            new ChainedNumbers.Numbered("a", "H1961", 1, "HEB 2", null, null, null),
            new ChainedNumbers.Numbered("b", "H7945", 1, "HEB 1", null, null, null),
            new ChainedNumbers.Numbered("c", "H834", 1, "HEB 3", null, null, null),
            new ChainedNumbers.Numbered("d", "H834", 1, "HEB 4", null, null, null),
            new ChainedNumbers.Numbered("e", "H2654", 1, "HEB 7", null, null, null),
            new ChainedNumbers.Numbered("f", "H834", 1, "HEB 5", null, null, null),
            new ChainedNumbers.Numbered("g", "H1961", 1, "HEB 2", null, null, null),
            new ChainedNumbers.Numbered("h", "H1961", 1, "HEB 6", null, null, null));
    }

    private static readonly Dictionary<string, WordClass> Classes = new(StringComparer.Ordinal)
    {
        ["H5175"] = WordClass.Content, ["H6086"] = WordClass.Content, ["H559"] = WordClass.Content, ["H413"] = WordClass.Function,
        ["H6440"] = WordClass.Content, ["G846"] = WordClass.Function, ["G1510"] = WordClass.Content, ["G2501"] = WordClass.Content,
        ["G3588"] = WordClass.Function, ["G435"] = WordClass.Content, ["H3130"] = WordClass.Content, ["G1161"] = WordClass.Function,
    };

    private static ChainedNumbers.Lender Hebrew(params (string Key, string Number)[] words) =>
        new(true, words.ToDictionary(w => w.Key, w => (w.Number, 0), StringComparer.Ordinal), Classes, new HashSet<string>(["v"], StringComparer.Ordinal));

    /// <summary>
    /// PRB-1869, the owner's rules inside a group link: «Now the snake» over ו + ה + נחש gives «Now» the conjunction, «the» the
    /// article and «snake» the noun; «the» over a noun alone takes none, and «trees in» over the noun both take it, as one phrase;
    /// «before» over ל + פני takes the noun its form renders elsewhere, never ל; a supplied word takes nothing.
    /// </summary>
    [Fact]
    public void InAGroupAFunctionWordTakesItsOwnPrefixAndThePhraseTheMainNumber()
    {
        var lender = Hebrew(("1", "H9000"), ("2", "H9009"), ("3", "H5175"), ("4", "H6086"), ("5", "H9005"), ("6", "H6440"), ("7", "H9005"),
            ("8", "H6440"), ("9", "H9005"), ("10", "H6440"));
        var numbered = ChainedNumbers.Resolve(
            [("a", null, "now"), ("b", null, "the"), ("c", null, "snake"), ("d", null, "the"), ("e", null, "trees"), ("f", null, "in"),
             ("g", null, "before"), ("h", null, "before"), ("i", null, "before"), ("j", null, "fruit")],
            [new ChainedNumbers.Joined(["a", "b", "c"], "HEB", ["1", "2", "3"], null),
             new ChainedNumbers.Joined(["d", "e", "f", "j"], "HEB", ["4"], null),
             new ChainedNumbers.Joined(["g"], "HEB", ["5", "6"], null), new ChainedNumbers.Joined(["h"], "HEB", ["7", "8"], null),
             new ChainedNumbers.Joined(["i"], "HEB", ["9", "10"], null)],
            new Dictionary<string, ChainedNumbers.Lender> { ["HEB"] = lender },
            new GroupNumbers("eng"), new HashSet<string>(["j"], StringComparer.Ordinal));

        numbered.Select(n => (n.Key, n.Number)).Should().Equal(
            ("a", "H9000"), ("b", "H9009"), ("c", "H5175"), ("e", "H6086"), ("f", "H6086"), ("g", "H6440"), ("h", "H6440"), ("i", "H6440"));
    }

    /// <summary>
    /// The owner's answers (2026-11-18): «he asked» over וַ + יֹּאמֶר + אֶל both take the verb (P3); «who was about to marry her» over
    /// ὁ + ἀνήρ + αὐτῆς: «her» takes αὐτός, which it renders alone elsewhere, and every other word ἀνήρ, the one content word left
    /// (P5). «Joseph» over ὤν is not given εἰμί, its form rendering Ἰωσήφ elsewhere. With P5 off only P3 is left: «he» takes the
    /// finite verb and «asked», a content word left in a group, nothing.
    /// </summary>
    [Fact]
    public void TheWordsLeftInAGroupTakeTheOneContentWordLeft()
    {
        var lender = Hebrew(("1", "H9000"), ("v", "H559"), ("3", "H413"), ("g0", "G3588"), ("g1", "G435"), ("g2", "G846"), ("g3", "G846"),
            ("g4", "G846"), ("g5", "G1510"), ("j1", "G2501"), ("j2", "G2501"), ("j3", "G2501"), ("j4", "G2501"), ("j5", "G2501"), ("s1", "H559"), ("s2", "H559"), ("s3", "H559"), ("h1", "H3130"),
            ("h2", "H3130"), ("h3", "H3130"), ("h4", "H3130"), ("h5", "H3130"), ("h6", "H3130"), ("g6", "G2501"), ("g7", "G1161"));
        List<(string, string?, string)> words =
        [
            ("a", null, "he"), ("b", null, "asked"), ("c", null, "who"), ("d", null, "was"), ("e", null, "about"), ("f", null, "to"),
            ("g", null, "marry"), ("h", null, "her"), ("i", null, "her"), ("k", null, "her"), ("l", null, "joseph"), ("m", null, "usually"),
            ("n", null, "joseph"), ("o", null, "joseph"), ("p", null, "joseph"), ("q", null, "joseph"), ("r", null, "joseph"),
            ("s", null, "he"), ("t", null, "said"), ("u", null, "said"), ("w", null, "said"), ("x", null, "joseph"), ("y", null, "joseph"),
            ("z", null, "joseph"), ("x2", null, "joseph"), ("x3", null, "joseph"), ("x4", null, "joseph"), ("x5", null, "joseph"),
        ];
        List<ChainedNumbers.Joined> links =
        [
            new(["a", "b"], "HEB", ["1", "v", "3"], null), new(["c", "d", "e", "f", "g", "h"], "HEB", ["g0", "g1", "g2"], null),
            new(["i"], "HEB", ["g3"], null), new(["k"], "HEB", ["g4"], null), new(["l", "m"], "HEB", ["g5"], null),
            new(["n"], "HEB", ["j1"], null), new(["o"], "HEB", ["j2"], null), new(["p"], "HEB", ["j3"], null), new(["q"], "HEB", ["j4"], null),
            new(["r"], "HEB", ["j5"], null), new(["s", "t"], "HEB", ["1", "s1"], null), new(["u"], "HEB", ["s2"], null),
            new(["w"], "HEB", ["s3"], null), new(["y"], "HEB", ["h1"], null), new(["z"], "HEB", ["h2"], null), new(["x2"], "HEB", ["h3"], null),
            new(["x3"], "HEB", ["h4"], null), new(["x4"], "HEB", ["h5"], null), new(["x5"], "HEB", ["h6"], null),
            new(["x"], "HEB", ["g6", "g7"], null),
        ];
        var lenders = new Dictionary<string, ChainedNumbers.Lender> { ["HEB"] = lender };
        string[] read = ["a", "b", "c", "d", "e", "f", "g", "h", "l", "m", "s", "t", "x"];

        ChainedNumbers.Resolve(words, links, lenders, new GroupNumbers("eng")).Where(n => read.Contains(n.Key))
            .Select(n => (n.Key, n.Number)).Should().Equal(("a", "H559"), ("b", "H559"), ("c", "G435"), ("d", "G435"), ("e", "G435"),
                ("f", "G435"), ("g", "G435"), ("h", "G846"), ("m", "G1510"), ("s", "H559"), ("t", "H559"), ("x", "G2501"));
        ChainedNumbers.Resolve(words, links, lenders, new GroupNumbers("eng", leftovers: false)).Where(n => n.Key is "a" or "b" or "m")
            .Select(n => (n.Key, n.Number)).Should().Equal(("a", "H559"));
    }

    /// <summary>
    /// «pass» renders עָבַר wherever it stands alone, yet «it came to pass» over הָיָה is one rendering of הָיָה: every word takes it.
    /// Where עָבַר stands in the verse, «pass» is taken to render that word and is given none. «of» over ל + דָּוִד takes ל.
    /// </summary>
    [Fact]
    public void AWordUsuallyRenderingAnotherNumberTakesThePhrasesUnlessTheVerseHoldsThatNumber()
    {
        var lender = Hebrew(("1.1:1.1", "H1961"), ("1.2:1.1", "H5674"), ("1.2:1.2", "H5674"), ("1.2:1.3", "H5674"), ("1.2:1.4", "H5674"),
            ("1.2:1.5", "H5674"), ("1.3:1.1", "H1961"), ("1.3:1.2", "H5674"), ("1.4:1.1", "H9005"), ("1.4:1.2", "H1732"));
        List<(string, string?, string)> words =
        [
            ("a", null, "it"), ("b", null, "came"), ("c", null, "to"), ("d", null, "pass"), ("p1", null, "pass"), ("p2", null, "pass"),
            ("p3", null, "pass"), ("p4", null, "pass"), ("p5", null, "pass"), ("e", null, "came"), ("f", null, "to"), ("g", null, "pass"),
            ("h", null, "of"), ("i", "H1732", "david"),
        ];
        List<ChainedNumbers.Joined> links =
        [
            new(["a", "b", "c", "d"], "HEB", ["1.1:1.1"], null), new(["p1"], "HEB", ["1.2:1.1"], null), new(["p2"], "HEB", ["1.2:1.2"], null),
            new(["p3"], "HEB", ["1.2:1.3"], null), new(["p4"], "HEB", ["1.2:1.4"], null), new(["p5"], "HEB", ["1.2:1.5"], null),
            new(["e", "f", "g"], "HEB", ["1.3:1.1"], null), new(["h", "i"], "HEB", ["1.4:1.1", "1.4:1.2"], null),
        ];

        ChainedNumbers.Resolve(words, links, new Dictionary<string, ChainedNumbers.Lender> { ["HEB"] = lender }, new GroupNumbers("eng"))
            .Where(n => !n.Key.StartsWith('p')).Select(n => (n.Key, n.Number)).Should().Equal(
                ("a", "H1961"), ("b", "H1961"), ("c", "H1961"), ("d", "H1961"), ("e", "H1961"), ("f", "H1961"), ("h", "H9005"), ("i", "H1732"));
    }

    [Fact]
    public void SynonymNumbersAreTheSameNumberInChecks()
    {
        StrongSynonyms.Same("G5210", "G4771").Should().BeTrue();
        StrongSynonyms.Same("G3361", "G3756").Should().BeTrue();
        StrongSynonyms.Same("G2248", "G1473").Should().BeTrue();
        StrongSynonyms.Same("G846", "G1473").Should().BeFalse();

        var lender = new ChainedNumbers.Lender(true, new Dictionary<string, (string, int)> { ["1"] = ("G5210", 0) });
        ChainedNumbers.Resolve([("a", "G4771", "ὑμῶν")], [new ChainedNumbers.Joined(["a"], "TIS", ["1"], null)],
                new Dictionary<string, ChainedNumbers.Lender> { ["TIS"] = lender })
            .Should().Equal(new ChainedNumbers.Numbered("a", "G4771", 0, null, null, null, null));
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
