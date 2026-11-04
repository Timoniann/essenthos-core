using Essenthos.Core.Conversion;
using TextRelationEntry = Essenthos.Core.Loading.TextRelationEntry;
using Essenthos.Core.Mapping;
using Essenthos.Core.Names;
using Essenthos.Core.Products;
using FluentAssertions;
using Xunit;

namespace Essenthos.Core.Tests.Products;

/// <summary>
/// The names of a translation are its base's settled names carried over the final mapping by the rules, with its overrides
/// (TSK-1448, DOC-0224 §7): the head of a link takes a name and an article does not, a pronoun keeps a person only where it is
/// clear who it is, a name below the floor is not carried, and an override adds, suppresses or puts the translation's record
/// before the base's.
/// </summary>
public sealed class NamesProjectionTests : IDisposable
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

    private static void Words(ProductStore store, string slug, params string[] surfaces)
    {
        using var writer = store.Create(TextWord.ProductOf(slug), ProductKind.Machine, TextWord.Schema(slug), "test", []);
        writer.WriteAll(surfaces.Select((surface, at) => new TextWord(new WordKey(slug, new VerseKey(2, 3, 4), at + 1), 100 + at,
            surface, " ", null, null, null, null, surface.ToLowerInvariant(), null, false, null, null, null)));
    }

    private static FinalRow Link(long id, string from, string to, double? confidence = null) =>
        new(id, from, to, "renders", null, true, confidence is null ? "stated" : "aligner", confidence, "test");

    private static SettledName Settled(int position, string record, double? confidence = null) =>
        new(new WordKey("BHSA", new VerseKey(2, 3, 4), position), record, NameRoles.Name, "manual", confidence, "test", null);

    /// <summary>A verse of Exodus: the English <em>And the LORD saw Moses and him</em> over three Hebrew words.</summary>
    private ProductStore Corpus(params NameOverride[] overrides)
    {
        var store = new ProductStore(_resources);
        Declare(store, "ENG", "eng", "translation");
        Declare(store, "BHSA", "hbo", "critical-edition");
        Words(store, "ENG", "And", "the", "LORD", "saw", "Moses", "and", "him", "Aaron");
        Words(store, "BHSA", "וַיַּרְא", "יְהוָה", "מֹשֶׁה", "אֹתוֹ", "אַהֲרֹן");
        using (var writer = store.Create(MappingProducts.Of("ENG-BHSA", MappingProducts.Final), ProductKind.Machine, FinalRow.Schema, "test", []))
        {
            writer.WriteAll([
                Link(1, "2.3:4.2 2.3:4.3", "2.3:4.2"),
                Link(2, "2.3:4.4", "2.3:4.1"),
                Link(3, "2.3:4.5", "2.3:4.3", 0.95),
                Link(4, "2.3:4.7", "2.3:4.4", 0.9),
                Link(5, "2.3:4.8", "2.3:4.5", 0.3),
            ]);
        }

        using (var writer = store.Create(NameProducts.Settled("BHSA"), ProductKind.Hand, SettledName.Schema("BHSA"), "test", []))
        {
            writer.WriteAll([Settled(2, "yhvh"), Settled(3, "moses"), Settled(4, "moses", 0.9), Settled(5, "aaron")]);
        }

        using (var writer = store.Create(NameProducts.Rules, ProductKind.Hand, NameRuleRow.Schema, "test", []))
        {
            writer.WriteAll(NameRules.Seed());
        }

        if (overrides.Length > 0)
        {
            using var writer = store.Create(NameProducts.Overrides("ENG"), ProductKind.Hand, NameOverride.Schema("ENG"), "test", []);
            writer.WriteAll(overrides);
        }

        store.Save();
        return store;
    }

    private static string Key(int position) => $"2.3:4.{position}";

    [Fact]
    public void TheHeadOfALinkTakesTheNameAndAPronounBesideAnotherPersonKeepsNobody()
    {
        var projection = new NamesProjection(Corpus());

        var names = projection.Project("ENG");

        names.Names.Select(n => (n.Own.Own, n.Record, n.Origin)).Should().BeEquivalentTo([
            (Key(3), "yhvh", NameOrigins.Projected),
            (Key(5), "moses", NameOrigins.Projected),
        ], "the article before LORD is never a name, him could be the LORD as well as Moses, and Aaron's link is below the floor");
        names.Refused.Should().Contain(new KeyValuePair<(string, string), string>((Key(2), "yhvh"), "never-a-name"));
        names.Refused.Should().Contain(new KeyValuePair<(string, string), string>((Key(7), "moses"), "pronoun"));
        names.Refused.Should().Contain(new KeyValuePair<(string, string), string>((Key(8), "aaron"), "below-the-floor"));
        names.Names.Single(n => n.Own.Own == Key(5)).Through.Should().Be("BHSA 2.3:4.3");
    }

    [Fact]
    public void APronounKeepsThePersonTheVerseNamesWhenNobodyElseItCouldBeIsNamed()
    {
        var store = Corpus();
        using (var writer = store.Create(MappingProducts.Of("ENG-BHSA", MappingProducts.Final), ProductKind.Machine, FinalRow.Schema, "test", []))
        {
            writer.WriteAll([Link(3, "2.3:4.5", "2.3:4.3", 0.95), Link(4, "2.3:4.7", "2.3:4.4", 0.9)]);
        }

        store.Save();

        var names = new NamesProjection(new ProductStore(_resources)).Project("ENG");

        names.Names.Select(n => (n.Own.Own, n.Record)).Should().BeEquivalentTo([(Key(5), "moses"), (Key(7), "moses")]);
    }

    [Fact]
    public void ANameListLinkCarriesANameOnlyFromItsOwnFloorAndEveryOtherMethodFromTheCommonOne()
    {
        var store = Corpus();
        using (var writer = store.Create(MappingProducts.Of("ENG-BHSA", MappingProducts.Final), ProductKind.Machine, FinalRow.Schema, "test", []))
        {
            writer.WriteAll([
                Link(3, "2.3:4.5", "2.3:4.3", 0.85),
                Link(5, "2.3:4.8", "2.3:4.5", 0.85) with { Method = MappingMethods.NameList },
            ]);
        }

        store.Save();

        var names = new NamesProjection(new ProductStore(_resources)).Project("ENG");

        names.Names.Select(n => (n.Own.Own, n.Record)).Should().BeEquivalentTo([(Key(5), "moses")], "an aligner's 0.85 clears 0.7");
        names.Refused.Should().Contain(new KeyValuePair<(string, string), string>((Key(8), "aaron"), "below-the-floor"),
            "a name-list link needs 0.9");
        new NameRules(NameRules.Seed()).FloorOver(MappingMethods.NameList).Should().Be(0.9);
        new NameRules(NameRules.Seed()).FloorOver(MappingMethods.Composed).Should().Be(0.7);
    }

    [Fact]
    public void OverridesAddSuppressAndPutTheTranslationsRecordBeforeTheBases()
    {
        var projection = new NamesProjection(Corpus(
            new NameOverride(new WordKey("ENG", new VerseKey(2, 3, 4), 4), "seeing", OverrideActions.Add, null, NameRoles.Thing, "test", null),
            new NameOverride(new WordKey("ENG", new VerseKey(2, 3, 4), 3), "yhvh", OverrideActions.Suppress, null, NameRoles.Name, "test", null),
            new NameOverride(new WordKey("ENG", new VerseKey(2, 3, 4), 5), "moses-2", OverrideActions.Differs, "moses", NameRoles.Name, "test", null)));

        var names = projection.Project("ENG").Names;

        names.Select(n => (n.Own.Own, n.Record, n.Origin)).Should().BeEquivalentTo([
            (Key(4), "seeing", NameOrigins.Added),
            (Key(5), "moses", NameOrigins.BaseReading),
            (Key(5), "moses-2", NameOrigins.Differs),
        ]);
    }

    [Fact]
    public void AProjectionIsMadeAgainOnlyWhenAnInputMoved()
    {
        var store = Corpus();
        new NamesProjection(store).Write("ENG", force: false).Made.Should().BeTrue();
        store.Save();
        new NamesProjection(new ProductStore(_resources)).Write("ENG", force: false).Made.Should().BeFalse();

        var moved = new ProductStore(_resources);
        using (var writer = moved.Create(NameProducts.Overrides("ENG"), ProductKind.Hand, NameOverride.Schema("ENG"), "test", []))
        {
            writer.WriteAll([new NameOverride(new WordKey("ENG", new VerseKey(2, 3, 4), 8), "aaron", OverrideActions.Add, null, NameRoles.Name, "test", null)]);
        }

        moved.Save();
        new NamesProjection(new ProductStore(_resources)).Write("ENG", force: false).Made.Should().BeTrue("an override was written");
    }

    [Fact]
    public void ABookTakesTheReferenceTextARelationNamesAndTheOthersSpeakForWordsItDoesNotLink()
    {
        var languages = new Dictionary<string, string> { ["DRA"] = "eng", ["VULGCLEM"] = "lat", ["BHSA"] = "hbo" };
        var linked = new Dictionary<(string, string), IReadOnlyDictionary<int, long>>
        {
            [("DRA", "VULGCLEM")] = new Dictionary<int, long> { [1] = 10, [2] = 10 },
            [("DRA", "BHSA")] = new Dictionary<int, long> { [1] = 50, [2] = 50 },
        };

        var bases = NameBases.Seed(languages, linked, [new TextRelationEntry("DRA", "VULGCLEM", "translated-from", null, null, "test")]);

        bases.Should().BeEquivalentTo([
            new NameBase("DRA", "1-2", "VULGCLEM", "translated-from VULGCLEM"),
            new NameBase("DRA", "1-2", "BHSA", "for the words VULGCLEM does not link"),
        ], options => options.WithStrictOrdering());
        NameBases.Of("DRA", bases)[2].Should().Equal("VULGCLEM", "BHSA");
    }
}
