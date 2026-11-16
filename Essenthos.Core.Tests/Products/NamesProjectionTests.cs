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
        writer.WriteAll(surfaces.Select((surface, at) => new TextWord(new WordKey(slug, new VerseKey(2, 3, 4), at + 1),
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
                Link(5, "2.3:4.8", "2.3:4.5", 0.85) with { Method = "name-list" },
            ]);
        }

        store.Save();

        var names = new NamesProjection(new ProductStore(_resources)).Project("ENG");

        names.Names.Select(n => (n.Own.Own, n.Record)).Should().BeEquivalentTo([(Key(5), "moses")], "an aligner's 0.85 clears 0.7");
        names.Refused.Should().Contain(new KeyValuePair<(string, string), string>((Key(8), "aaron"), "below-the-floor"),
            "a name-list link needs 0.9");
        new NameRules(NameRules.Seed()).FloorOver("name-list").Should().Be(0.9);
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

    /// <summary>A store of ENG over BHSA in one verse: ENG's words as <c>surface|trailer|strong</c>, the final's links and BHSA's settled names.</summary>
    private ProductStore Verse(string[] english, string[] hebrew, FinalRow[] links, SettledName[] settled)
    {
        var store = new ProductStore(_resources);
        Declare(store, "ENG", "eng", "translation");
        Declare(store, "BHSA", "hbo", "critical-edition");
        using (var writer = store.Create(TextWord.ProductOf("ENG"), ProductKind.Machine, TextWord.Schema("ENG"), "test", []))
        {
            writer.WriteAll(english.Select((word, at) => word.Split('|')).Select((parts, at) => new TextWord(new WordKey("ENG", new VerseKey(2, 3, 4), at + 1),
                parts[0], parts.Length > 1 ? parts[1] : " ", null, parts.Length > 2 ? parts[2] : null, null, null, parts[0].ToLowerInvariant(), null, false, null, null, null)));
        }

        Words(store, "BHSA", hebrew);
        using (var writer = store.Create(MappingProducts.Of("ENG-BHSA", MappingProducts.Final), ProductKind.Machine, FinalRow.Schema, "test", []))
        {
            writer.WriteAll(links);
        }

        using (var writer = store.Create(NameProducts.Settled("BHSA"), ProductKind.Hand, SettledName.Schema("BHSA"), "test", []))
        {
            writer.WriteAll(settled);
        }

        using (var writer = store.Create(NameProducts.Rules, ProductKind.Hand, NameRuleRow.Schema, "test", []))
        {
            writer.WriteAll(NameRules.Seed().Select(r => r.Rule == NameRules.Head ? r with { Value = NameRules.HeadLastAndCapitals } : r));
        }

        store.Save();
        return store;
    }

    private static FinalRow Strong(long id, string from, string to, double confidence) =>
        new(id, from, to, "renders", null, true, MappingMethods.Strong, confidence, "test");

    [Fact]
    public void ARepeatedNumberPairedInOrderCarriesTheNameWhenEveryOccurrenceNamesTheSamePerson()
    {
        var store = Verse(
            ["Abraham| |G11", "begat| |G1080", "Isaac|; |G2464", "and", "Isaac|; |G2464", "begat|; |G1080", "Jacob|; |G2384", "and|; |G1161", "Jacob| |G2384"],
            ["אַבְרָהָם", "הוֹלִיד", "יִצְחָק", "יִצְחָק", "הוֹלִיד", "יַעֲקֹב", "רְאוּבֵן"],
            [
                Strong(1, Key(3), Key(3), 0.7), Strong(2, Key(5), Key(4), 0.7),
                Strong(3, Key(7), Key(6), 0.7), Strong(4, Key(9), Key(7), 0.7),
            ],
            [Settled(3, "isaac", 0.99), Settled(4, "isaac", 0.99), Settled(6, "jacob", 0.99), Settled(7, "reuben", 0.99)]);

        var names = new NamesProjection(store).Project("ENG");

        names.Names.Select(n => (n.Own.Own, n.Record)).Should().BeEquivalentTo([(Key(3), "isaac"), (Key(5), "isaac")],
            "both Isaacs name Isaac whichever way the pair goes; the two Jacobs are paired with words naming two people");
        names.Names.Single(n => n.Own.Own == Key(3)).Confidence.Should().BeApproximately(0.9 * 0.99, 1e-9);
        names.Refused.Should().Contain(new KeyValuePair<(string, string), string>((Key(7), "jacob"), "below-the-floor"));
    }

    [Fact]
    public void APersonOfALinkNamingSeveralGoesOnlyToAWordAnotherLinkJoinsToHim()
    {
        var store = Verse(
            ["to", "Jerusalem", "to", "Bethphage"],
            ["εἰς", "Ἰεροσόλυμα", "εἰς", "Βηθφαγή"],
            [Link(1, Key(2), Key(2), 0.95), Link(3, Key(4), Key(4), 0.95), Link(2, $"{Key(1)} {Key(2)} {Key(3)} {Key(4)}", $"{Key(1)} {Key(2)} {Key(3)} {Key(4)}")],
            [Settled(2, "jerusalem"), Settled(4, "bethphage")]);

        var names = new NamesProjection(store).Project("ENG");

        names.Names.Select(n => (n.Own.Own, n.Record)).Should().BeEquivalentTo([(Key(2), "jerusalem"), (Key(4), "bethphage")]);
        names.Refused.Should().Contain(new KeyValuePair<(string, string), string>((Key(4), "jerusalem"), "a-link-of-several-people"));
        names.Refused.Should().Contain(new KeyValuePair<(string, string), string>((Key(2), "bethphage"), "a-link-of-several-people"));
    }

    [Fact]
    public void ALinkOfSeveralWordsNamingSeveralPeopleAloneNamesNobody()
    {
        var store = Verse(
            ["Aquila", "and", "Priscilla"],
            ["Πρίσκιλλα", "καὶ", "Ἀκύλας"],
            [Link(1, $"{Key(1)} {Key(2)} {Key(3)}", $"{Key(1)} {Key(2)} {Key(3)}")],
            [Settled(1, "priscilla"), Settled(3, "aquila")]);

        var names = new NamesProjection(store).Project("ENG");

        names.Names.Should().BeEmpty("the link says both are there, not which word is which");
        names.Refused.Should().Contain(new KeyValuePair<(string, string), string>((Key(3), "aquila"), "a-link-of-several-people"));
    }

    [Fact]
    public void APersonsNameGoesToTheWordWrittenAsANameNotToTheLastWordOfThePhrase()
    {
        var store = Verse(
            ["and", "he", "gave", "Zipporah", "to", "Moses", "in", "marriage"],
            ["וַיִּתֵּן", "צִפֹּרָה", "לְמֹשֶׁה"],
            [Link(1, Key(4), Key(2)), Link(2, $"{Key(5)} {Key(6)} {Key(7)} {Key(8)}", Key(3))],
            [Settled(2, "zipporah"), Settled(3, "moses")]);

        var names = new NamesProjection(store).Project("ENG");

        names.Names.Select(n => (n.Own.Own, n.Record)).Should().BeEquivalentTo([(Key(4), "zipporah"), (Key(6), "moses")]);
        names.Refused.Should().Contain(new KeyValuePair<(string, string), string>((Key(8), "moses"), "not-the-head"));
    }

    private sealed record PersonRow(string Slug, string Kind);

    private static readonly ProductSchema<PersonRow> PersonSchema = new ProductSchema<PersonRow>()
        .Text("slug", r => r.Slug).Text("kind", r => r.Kind).Text("name", r => r.Slug).OptionalText("sex", _ => null).OptionalText("sex_source", _ => null)
        .Reads(row => new PersonRow(row.Text(0), row.Text(1)));

    /// <summary>
    /// Jeremiah 46:25 as the King James prints it, <em>the multitude of No</em>, with twenty people named by name around it, a
    /// <em>satan</em> the text writes in lower case, and a <em>king</em> rendering a common noun annotated with whom it means.
    /// </summary>
    [Fact]
    public void APersonsNameIsNotCarriedFromAProperNounOntoAWordTheTextPrintsAsNoName()
    {
        var store = new ProductStore(_resources);
        Declare(store, "ENG", "eng", "translation");
        Declare(store, "BHSA", "hbo", "critical-edition");
        string[] english = ["punish", "the", "multitude", "of", "No", .. Enumerable.Range(0, 20).Select(i => $"Name{(char)('a' + i)}"), "satan", "the", "king",
            .. Enumerable.Repeat("multitude", 10)];
        using (var writer = store.Create(TextWord.ProductOf("ENG"), ProductKind.Machine, TextWord.Schema("ENG"), "test", []))
        {
            writer.WriteAll(english.Select((surface, at) => new TextWord(new WordKey("ENG", new VerseKey(2, 3, 4), at + 1), surface, " ",
                null, null, null, null, surface.ToLowerInvariant(), null, false, null, null, null)));
        }

        // BHSA: אָמוֹן, twenty names and שָׂטָן marked as proper nouns, and the common noun מֶלֶךְ.
        const string properNoun = "{\"pos\": \"nmpr\"}";
        var hebrew = Enumerable.Range(1, 22).Select(at => (Surface: $"שם{at}", Morphology: properNoun)).Append((Surface: "מֶלֶךְ", Morphology: "{\"pos\": \"subs\"}")).ToList();
        using (var writer = store.Create(TextWord.ProductOf("BHSA"), ProductKind.Machine, TextWord.Schema("BHSA"), "test", []))
        {
            writer.WriteAll(hebrew.Select((word, at) => new TextWord(new WordKey("BHSA", new VerseKey(2, 3, 4), at + 1), word.Surface, " ",
                word.Surface, null, null, word.Morphology, null, null, false, null, null, null)));
        }

        var records = new[] { "amon" }.Concat(Enumerable.Range(0, 20).Select(i => $"person-{i}")).Append("satan").Append("pharaoh").ToList();
        using (var writer = store.Create(MappingProducts.Of("ENG-BHSA", MappingProducts.Final), ProductKind.Machine, FinalRow.Schema, "test", []))
        {
            var links = new List<FinalRow> { Link(1, Key(3), Key(1)) };
            links.AddRange(Enumerable.Range(0, 20).Select(i => Link(2 + i, Key(6 + i), Key(2 + i))));
            links.Add(Link(30, Key(26), Key(22)));
            links.Add(Link(31, Key(28), Key(23)));
            writer.WriteAll(links);
        }

        using (var writer = store.Create(NameProducts.Settled("BHSA"), ProductKind.Hand, SettledName.Schema("BHSA"), "test", []))
        {
            writer.WriteAll(records.Select((record, at) => Settled(at + 1, record)));
        }

        using (var writer = store.Create(NamesProjection.RecordsProduct, ProductKind.Hand, PersonSchema, "test", []))
        {
            writer.WriteAll(records.Select(record => new PersonRow(record, "person")));
        }

        using (var writer = store.Create(NameProducts.Rules, ProductKind.Hand, NameRuleRow.Schema, "test", []))
        {
            writer.WriteAll(NameRules.Seed());
        }

        store.Save();

        var names = new NamesProjection(new ProductStore(_resources)).Project("ENG");

        names.Refused.Should().Contain(new KeyValuePair<(string, string), string>((Key(3), "amon"), "a-name-the-text-does-not-print"),
            "the text writes multitude eleven times and gives it a person once");
        names.Names.Select(n => n.Record).Should().Contain(["satan", "pharaoh", "person-0", "person-19"],
            "satan is the only way the text writes that name, and the king renders a common noun, not a name");
        names.Names.Should().NotContain(n => n.Record == "amon");
    }

    [Fact]
    public void APossessivesLoneSIsNeverTheName()
    {
        var store = Verse(
            ["Is", "the", "LORD| '", "S", "hand"],
            ["הֲיַד", "יְהוָה"],
            [Link(1, $"{Key(3)} {Key(4)}", Key(2)), Link(2, Key(4), Key(2))],
            [Settled(2, "yhvh")]);

        var names = new NamesProjection(store).Project("ENG");

        names.Names.Select(n => (n.Own.Own, n.Record)).Should().BeEquivalentTo([(Key(3), "yhvh")]);
    }
}
