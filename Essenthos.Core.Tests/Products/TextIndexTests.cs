using Essenthos.Core.Conversion;
using Essenthos.Core.Indexes;
using Essenthos.Core.Mapping;
using Essenthos.Core.Products;
using FluentAssertions;
using Xunit;

namespace Essenthos.Core.Tests.Products;

/// <summary>
/// The per-text indexes (TSK-1459, DOC-0224 §10) are functions of the words, the finals and the names: a text's Strong
/// index holds the phrase each numbered word of its edition is rendered by, an index is made again only when an input
/// moved, and the SBLGNT takes its numbers from Nestle's words verse by verse.
/// </summary>
public sealed class TextIndexTests : IDisposable
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

    private static void Words(ProductStore store, string slug, params (string Surface, string? Strong, string? Morphology)[] words)
    {
        using var writer = store.Create(TextWord.ProductOf(slug), ProductKind.Machine, TextWord.Schema(slug), "test", []);
        writer.WriteAll(words.Select((w, at) => new TextWord(new WordKey(slug, new VerseKey(1, 1, 1), at + 1), 100 + at,
            w.Surface, " ", null, w.Strong, null, w.Morphology, w.Surface.ToLowerInvariant(), null, false, null, null, null)));
    }

    private static void Final(ProductStore store, string pair, params FinalRow[] rows)
    {
        using var writer = store.Create(MappingProducts.Of(pair, MappingProducts.Final), ProductKind.Machine, FinalRow.Schema, "test", []);
        writer.WriteAll(rows);
    }

    private static FinalRow Link(long id, string from, string to, string relation = "renders", string method = "stated") =>
        new(id, from, to, relation, null, true, method, null, "test");

    private ProductStore Corpus()
    {
        var store = new ProductStore(_resources);
        Declare(store, "ENG", "eng", "translation");
        Declare(store, "HEB", "hbo", "critical-edition");
        Words(store, "ENG", ("In", null, null), ("The", null, null), ("beginning", null, null), ("God", null, null));
        Words(store, "HEB", ("בְּרֵאשִׁית", "H7225", """{"pos":"subs"}"""), ("אֱלֹהִים", "H430", """{"pos":"subs"}"""), ("וְ", "H9002", """{"pos":"conj"}"""));
        Final(store, "ENG-HEB",
            Link(1, "1.1:1.1 1.1:1.3", "1.1:1.1"),
            Link(2, "1.1:1.4", "1.1:1.2", method: "aligner"),
            Link(3, "1.1:1.2", "", relation: "expands"));
        store.Save();
        return store;
    }

    [Fact]
    public void AStrongIndexHoldsThePhraseEachNumberedWordIsRenderedBy()
    {
        var store = Corpus();
        var context = new IndexContext(store);

        var made = TextIndexes.Strong(context, "ENG", force: false);

        made.Made.Should().BeTrue();
        using var reader = store.Open(IndexProducts.Of("ENG", IndexProducts.Strong), StrongIndexRow.Schema);
        reader.Header!.Notes.Should().Contain(new KeyValuePair<string, string>("hebrew", "HEB"));
        reader.Rows().Should().BeEquivalentTo(
        [
            new StrongIndexRow("H7225", "HEB", "1.1:1.1", "1.1:1.1 1.1:1.3", "in beginning", "1", "stated-by-source"),
            new StrongIndexRow("H430", "HEB", "1.1:1.2", "1.1:1.4", "god", "2", "aligner"),
        ], options => options.WithStrictOrdering());
    }

    [Fact]
    public void AnIndexIsMadeAgainOnlyWhenAnInputMoved()
    {
        var store = Corpus();
        TextIndexes.Strong(new IndexContext(store), "ENG", force: false).Made.Should().BeTrue();
        store.Save();

        TextIndexes.Strong(new IndexContext(new ProductStore(_resources)), "ENG", force: false).Made.Should().BeFalse();
        TextIndexes.Forms(new IndexContext(new ProductStore(_resources)), "ENG", force: false).Made.Should().BeTrue();
        TextIndexes.Forms(new IndexContext(new ProductStore(_resources)), "ENG", force: false).Made.Should().BeFalse();

        var moved = new ProductStore(_resources);
        Final(moved, "ENG-HEB", Link(1, "1.1:1.1", "1.1:1.1"));
        moved.Save();
        TextIndexes.Strong(new IndexContext(new ProductStore(_resources)), "ENG", force: false).Made.Should().BeTrue("the pair's final moved");
        TextIndexes.Forms(new IndexContext(new ProductStore(_resources)), "ENG", force: false).Made.Should().BeFalse("the forms read no final");
    }

    [Fact]
    public void AnEditionsOccurrencesAreCountedByBookAndPartOfSpeech()
    {
        var store = Corpus();
        TextIndexes.Occurrences(new IndexContext(store), "HEB", force: false)!.Rows.Should().Be(3);
        TextIndexes.Occurrences(new IndexContext(store), "ENG", force: false).Should().BeNull("a translation is no edition a count is made over");

        using var reader = store.Open(IndexProducts.Of("HEB", IndexProducts.Occurrences), OccurrenceRow.Schema);
        reader.Rows().Should().Contain(new OccurrenceRow("H9002", 1, "conj", 1));
    }

    [Fact]
    public void AFormIsStemmedHereByTheDatabaseOrNotAtAll()
    {
        TextIndexes.Stem("воскресінням", "ukr").Should().NotBeNull().And.NotBe("воскресінням");
        TextIndexes.Stem("beginning", "eng").Should().BeNull("the serving database's english dictionary stems it at load");
        TextIndexes.Stem("בְּרֵאשִׁית", "hbo").Should().Be("בְּרֵאשִׁית");
    }

    [Fact]
    public void LemmasAreComparedAsTheDatabaseCollatesThem()
    {
        TextIndexes.Collated("Ἱεροβάαλ", "יְרֻבַּעַל").Should().BeNegative("the collation puts Greek before Hebrew, the code points after");
        TextIndexes.Collated("Abram", "Ἀβραάμ").Should().BeNegative();
        TextIndexes.Collated("Ἀβραάμ", "Ἀβραὰμ").Should().NotBe(0);
        TextIndexes.Pos("""{"pos":"verb","person":"3"}""").Should().Be("verb");
        TextIndexes.Pos("""{"person":"3"}""").Should().BeNull();
    }

    [Fact]
    public void TheSblgntTakesItsNumbersFromNestleInOrderThenFromWestcottAndHortThenByAFormStandingOnce()
    {
        static ConvertedWord Word(string form) => new() { Surface = form, Trailer = " ", Normalised = form };
        var key = new VerseKey(43, 1, 1);
        var verse = new ConvertedVerse(key, 1, [Word("εν"), Word("αρχη"), Word("ην"), Word("λογοσ"), Word("ο"), Word("ουτοσ"), Word("γε")]);
        var nestle = new Dictionary<VerseKey, List<(string?, string?)>>
        {
            [key] = [("εν", "G1722"), ("αρχη", "G746"), ("ο", "G3588"), ("ην", "G1510"), ("λογοσ", "G3056")],
        };
        var westcott = new Dictionary<VerseKey, List<(string?, string?)>> { [key] = [("ουτοσ", "G3778")] };

        var outcome = DerivedStrongNumbers.Number([verse], [("NESTLE1904", nestle), ("WH1881", westcott)]);

        verse.Words.Select(w => w.Strong).Should().Equal("G1722", "G746", "G1510", "G3056", "G3588", "G3778", null);
        outcome.InOrder.Should().Equal(("NESTLE1904", 4), ("WH1881", 1));
        outcome.Reordered.Should().Be(1, "ὁ stands once among Nestle's words left in the verse, after ἦν");
        outcome.Unnumbered.Should().Be(1);
    }
}
