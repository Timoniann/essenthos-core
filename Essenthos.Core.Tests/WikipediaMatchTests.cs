using System.Text.Json.Nodes;
using Essenthos.Core.Corpus;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Loading.Encyclopedia;
using FluentAssertions;
using Xunit;

namespace Essenthos.Core.Tests;

/// <summary>
/// A record is tied to a Wikidata item only where nothing else could be the item, and a namesake the
/// evidence cannot tell apart is left for the owner. The famous namesake with an article is the
/// dangerous one: the minor man of the same name is mostly an item with none.
/// </summary>
public sealed class WikipediaMatchTests : IDisposable
{
    private readonly string _resources = Path.Combine(Path.GetTempPath(), $"wikipedia-match-{Guid.NewGuid():n}");

    private readonly List<string> _items = [];

    public void Dispose()
    {
        if (Directory.Exists(_resources))
        {
            Directory.Delete(_resources, recursive: true);
        }
    }

    private static JsonObject Labels(string label) => new() { ["en"] = label };

    private void Add(
        string id,
        string label,
        string kind = "person",
        string? description = null,
        string? article = null,
        string[]? aliases = null,
        string[]? seeds = null,
        string? sex = null,
        Dictionary<string, string[]>? claims = null,
        string[]? classes = null)
    {
        var item = new JsonObject
        {
            ["id"] = id,
            ["kind"] = kind,
            ["seeds"] = new JsonArray([.. (seeds ?? ["class"]).Select(s => (JsonNode?)s)]),
            ["labels"] = Labels(label),
            ["descriptions"] = description is null ? new JsonObject() : new JsonObject { ["en"] = description },
            ["aliases"] = aliases is null ? new JsonObject() : new JsonObject { ["en"] = new JsonArray([.. aliases.Select(a => (JsonNode?)a)]) },
            ["sitelinks"] = article is null ? new JsonObject() : new JsonObject { ["enwiki"] = article },
        };

        var claimed = new JsonObject();
        foreach (var (property, values) in claims ?? [])
        {
            claimed[property] = new JsonArray([.. values.Select(v => (JsonNode?)new JsonObject { ["value"] = v })]);
        }

        if (sex is not null)
        {
            claimed["P21"] = new JsonArray(new JsonObject { ["value"] = sex == "male" ? "Q6581097" : "Q6581072" });
        }

        if (classes is not null)
        {
            claimed["P31"] = new JsonArray([.. classes.Select(c => (JsonNode?)new JsonObject { ["value"] = c })]);
        }

        item["claims"] = claimed;
        _items.Add(item.ToJsonString());
    }

    private WikipediaMatcher Matcher(Dictionary<string, string>? identified = null, IEnumerable<string>? referenced = null)
    {
        var folder = Path.Combine(_resources, WikidataItems.Folder);
        Directory.CreateDirectory(folder);
        File.WriteAllLines(Path.Combine(folder, WikidataItems.ItemsFile), _items);
        File.WriteAllLines(Path.Combine(folder, WikidataItems.ReferencedFile), referenced ?? []);
        return new WikipediaMatcher(WikidataItems.Read(_resources), identified ?? []);
    }

    private static WikipediaRecord Record(
        int id,
        string slug,
        string name,
        EntityKind kind = EntityKind.Person,
        string? line = null,
        string? sex = null,
        string? openBibleId = null,
        (int, int, int)[]? verses = null,
        params WikipediaRelative[] relatives) =>
        new(id, slug, kind, name, line, sex, openBibleId,
            new HashSet<string> { NameFolding.Fold(name) },
            (verses ?? []).Select(v => (v.Item1, v.Item2, v.Item3)).ToHashSet(),
            relatives);

    private static WikipediaRelative Kin(string relation, string name) =>
        new(relation, new HashSet<string> { NameFolding.Fold(name) });

    private static string LinkedTo(Dictionary<int, WikipediaMatch> matches, int record) =>
        matches[record] is WikipediaMatch.Linked tied ? $"{tied.Item.Id} by {tied.By}" : matches[record].GetType().Name;

    [Fact]
    public void ThePlainNameOfAManNobodyElseBearsTiesHimToHisItem()
    {
        Add("Q1", "Habakkuk", article: "Habakkuk");

        var matches = Matcher().Match([Record(1, "habakkuk", "Habakkuk")]);

        LinkedTo(matches, 1).Should().Be("Q1 by name");
    }

    [Fact]
    public void TwoRecordsOfOneNameAreNotGivenTheOnlyItemOfIt()
    {
        Add("Q1", "Obadiah", article: "Obadiah");

        var matches = Matcher().Match([Record(1, "obadiah", "Obadiah"), Record(2, "obadiah-2", "Obadiah")]);

        matches[1].Should().BeOfType<WikipediaMatch.Ambiguous>();
        matches[2].Should().BeOfType<WikipediaMatch.Ambiguous>();
    }

    [Fact]
    public void AnItemWithoutAnArticleStillCountsAsANamesakeOfTheOneWithAnArticle()
    {
        Add("Q1", "Zechariah", article: "Zechariah (prophet)");
        Add("Q2", "Zechariah");

        var matches = Matcher().Match([Record(1, "zechariah", "Zechariah")]);

        var ambiguous = matches[1].Should().BeOfType<WikipediaMatch.Ambiguous>().Subject;
        ambiguous.Candidates.Select(c => c.Item.Id).Should().BeEquivalentTo("Q1", "Q2");
    }

    [Fact]
    public void ADescriptionThatCitesAVerseTheRecordIsNamedInTiesThatRecordAndNoOther()
    {
        Add("Q1", "Zechariah", description: "male human biblical figure in 2 Kings 14:29, King of Israel", article: "Zechariah of Israel");
        Add("Q2", "Zechariah", description: "male human biblical figure in Ezra 8:3");

        var matches = Matcher().Match(
        [
            Record(1, "zechariah", "Zechariah", verses: [(12, 14, 29)]),
            Record(2, "zechariah-2", "Zechariah", verses: [(15, 8, 3)]),
            Record(3, "zechariah-3", "Zechariah", verses: [(1, 1, 1)]),
        ]);

        LinkedTo(matches, 1).Should().Be("Q1 by verse");
        LinkedTo(matches, 2).Should().Be("Q2 by verse");
        matches[3].Should().BeOfType<WikipediaMatch.Ambiguous>();
    }

    [Fact]
    public void AVerseRangeTiesTheRecordNamedInAnyVerseOfIt()
    {
        Add("Q1", "Ahimelech", description: "male human biblical figure in 1 Samuel 22:20-23, high priest at Nob");
        Add("Q2", "Ahimelech");

        var matches = Matcher().Match([Record(1, "ahimelech", "Ahimelech", verses: [(9, 22, 22)])]);

        LinkedTo(matches, 1).Should().Be("Q1 by verse");
    }

    [Fact]
    public void ARelativeTheItemStatesTiesTheRecordWhoseRelativeHeIs()
    {
        Add("Q10", "Joash");
        Add("Q1", "Amaziah", claims: new() { ["P22"] = ["Q10"] });
        Add("Q2", "Amaziah");

        var matches = Matcher().Match([Record(1, "amaziah", "Amaziah", relatives: Kin("son-of", "Joash"))]);

        LinkedTo(matches, 1).Should().Be("Q1 by kin");
    }

    [Fact]
    public void AFamilyStatementInTheDescriptionTiesTheRecordWhoseLineStatesTheSame()
    {
        Add("Q1", "Johanan", description: "oldest son of King Josiah");
        Add("Q2", "Johanan", description: "high priest at the time of Solomon");

        var matches = Matcher().Match([Record(1, "johanan", "Johanan", line: "firstborn son of Josiah (1CH 3:15)")]);

        LinkedTo(matches, 1).Should().Be("Q1 by kin");
    }

    [Fact]
    public void AChapterTheItemIsPresentInTiesTheRecordNamedThere()
    {
        Add("Q1", "Ehud", description: "second judge of Israel", claims: new() { ["P1441"] = ["Q900"] });
        Add("Q2", "Ehud");

        var matches = Matcher(referenced: [new JsonObject { ["id"] = "Q900", ["labels"] = Labels("Judges 3") }.ToJsonString()])
            .Match(
            [
                Record(1, "ehud", "Ehud", verses: [(7, 3, 15)]),
                Record(2, "ehud-2", "Ehud", verses: [(13, 8, 6)]),
            ]);

        LinkedTo(matches, 1).Should().Be("Q1 by chapter");
        matches[2].Should().BeOfType<WikipediaMatch.Ambiguous>();
    }

    [Fact]
    public void ARecordWithAStrongerClaimOnAnItemTakesItFromOneWithAWeakerClaim()
    {
        Add("Q1", "Ehud", description: "judge of Israel in Judges 3:15", claims: new() { ["P1441"] = ["Q901"] });
        Add("Q2", "Ehud");

        var matches = Matcher(referenced: [new JsonObject { ["id"] = "Q901", ["labels"] = Labels("1 Chronicles 8") }.ToJsonString()]).Match(
        [
            Record(1, "ehud", "Ehud", verses: [(7, 3, 15)]),
            Record(2, "ehud-2", "Ehud", verses: [(13, 8, 6)]),
        ]);

        LinkedTo(matches, 1).Should().Be("Q1 by verse");
        matches[2].Should().BeOfType<WikipediaMatch.Ambiguous>("the first record has the verse, and the second only shares the item's chapter");
    }

    [Fact]
    public void AnItemOfTheOtherSexIsNotACandidate()
    {
        Add("Q1", "Jehoiada", sex: "female");
        Add("Q2", "Jehoiada", sex: "male", article: "Jehoiada");

        var matches = Matcher().Match([Record(1, "jehoiada", "Jehoiada", sex: "male")]);

        LinkedTo(matches, 1).Should().Be("Q2 by name");
    }

    [Fact]
    public void AnAngelIsNotAManBecauseTheAngelGoesByHisNameAsAnAlias()
    {
        Add("Q1", "Raphael", aliases: ["Rephael"], classes: ["Q178342"], article: "Raphael (archangel)");

        var matches = Matcher().Match(
        [
            Record(1, "rephael", "Rephael", line: "son of Shemaiah (1CH 26:7)"),
            Record(2, "raphael", "Rephael", line: "the angel who healed Tobit"),
        ]);

        matches[1].Should().BeOfType<WikipediaMatch.Unmatched>("the man's line does not say he is an angel");
        LinkedTo(matches, 2).Should().Be("Q1 by name");
    }

    [Fact]
    public void ADisambiguationPageIsNotAnItem()
    {
        Add("Q1", "Alemeth", kind: "place", description: "Wikimedia disambiguation page", article: "Alemeth");

        var matches = Matcher().Match([Record(1, "alemeth", "Alemeth", EntityKind.Place)]);

        matches[1].Should().BeOfType<WikipediaMatch.Unmatched>();
    }

    [Fact]
    public void AnItemWikidataSaysIsDifferentFromAnotherPersonIsNotTiedByNameAlone()
    {
        Add("Q1", "Shuah", article: "Shuah", claims: new() { ["P1889"] = ["Q2"] });
        Add("Q2", "Shuah's namesake");

        var matches = Matcher().Match([Record(1, "shuah", "Shuah")]);

        matches[1].Should().BeOfType<WikipediaMatch.Ambiguous>();
    }

    [Fact]
    public void AnItemWikidataSaysIsTheSameAsAnotherIsLeftForTheOwner()
    {
        Add("Q1", "Bithiah", claims: new() { ["P460"] = ["Q2"] });
        Add("Q2", "Pharaoh's daughter");

        var matches = Matcher().Match([Record(1, "bithiah", "Bithiah")]);

        matches[1].Should().BeOfType<WikipediaMatch.Ambiguous>();
    }

    [Fact]
    public void ADifferentFromStatementAboutSomethingElseDoesNotCount()
    {
        Add("Q1", "Samson", article: "Samson", claims: new() { ["P1889"] = ["Q50"] });
        var matches = Matcher(referenced: [new JsonObject { ["id"] = "Q50", ["labels"] = Labels("Samson (comics)") }.ToJsonString()])
            .Match([Record(1, "samson", "Samson")]);

        LinkedTo(matches, 1).Should().Be("Q1 by name");
    }

    [Fact]
    public void AGazetteersIdentificationTiesAPlaceWhateverItsNameIs()
    {
        Add("Q1", "Kutha", kind: "place", article: "Kutha");

        var matches = Matcher(new() { ["a1"] = "Q1" }).Match([Record(1, "cuth", "Cuth", EntityKind.Place, openBibleId: "a1")]);

        LinkedTo(matches, 1).Should().Be("Q1 by identification");
    }

    [Fact]
    public void AModernSiteOnlyTheGazetteerNamedIsNotTheAncientPlaceUnlessItBearsItsName()
    {
        Add("Q1", "Tell es-Sultan", kind: "place", seeds: ["openbible"], aliases: ["Jericho"], article: "Tell es-Sultan");

        var matches = Matcher().Match([Record(1, "jericho", "Jericho", EntityKind.Place)]);

        matches[1].Should().BeOfType<WikipediaMatch.Unmatched>();
    }

    [Fact]
    public void APlaceOrAThingThatMerelyGoesByAnotherItemsAliasIsNotThatItem()
    {
        Add("Q1", "Iran", kind: "place", aliases: ["Persia"], article: "Iran");
        Add("Q2", "Sunday", kind: "other", aliases: ["The Lord's Day"], article: "Sunday");

        var matches = Matcher().Match(
        [
            Record(1, "persia", "Persia", EntityKind.Place),
            Record(2, "lords-day", "The Lord's Day", EntityKind.Observance),
        ]);

        matches[1].Should().BeOfType<WikipediaMatch.Ambiguous>();
        matches[2].Should().BeOfType<WikipediaMatch.Ambiguous>();
    }

    [Fact]
    public void AMenNamesAreFoldedAcrossScriptsAndSpellings()
    {
        var item = new JsonObject
        {
            ["id"] = "Q1",
            ["kind"] = "person",
            ["seeds"] = new JsonArray("class"),
            ["labels"] = new JsonObject { ["he"] = "אֱלִישָׁע", ["uk"] = "Єлисей" },
            ["descriptions"] = new JsonObject(),
            ["aliases"] = new JsonObject(),
            ["sitelinks"] = new JsonObject { ["ukwiki"] = "Єлисей" },
            ["claims"] = new JsonObject(),
        };
        _items.Add(item.ToJsonString());

        var matches = Matcher().Match(
        [
            new WikipediaRecord(1, "elisha", EntityKind.Person, "Elisha", null, null, null,
                new HashSet<string> { NameFolding.Fold("Elisha"), NameFolding.Fold("אלישע") },
                new HashSet<(int, int, int)>(), []),
        ]);

        LinkedTo(matches, 1).Should().Be("Q1 by name");
    }

    [Theory]
    [InlineData("in 2 Kings 18:2, father of Abijah", 12, 18, 2, 2)]
    [InlineData("biblical character from the Book of Judges 13:1-23 and 14:2-4", 7, 13, 1, 23)]
    [InlineData("woman met by Jesus at the well (John 4:5–26)", 43, 4, 5, 26)]
    [InlineData("Christian woman mentioned by the Apostle Paul in Romans 16:1", 45, 16, 1, 1)]
    public void AVerseIsReadOutOfAnEnglishDescription(string text, int book, int chapter, int first, int last)
    {
        CitedScripture.Find(text).Should().Contain(new CitedSpan(book, chapter, first, last));
    }

    [Fact]
    public void AChapterNamedWithoutAVerseIsAChapter()
    {
        CitedScripture.Find("Genesis 25").Should().Equal(new CitedSpan(1, 25, 0, 0));
        CitedScripture.Find("1 Chronicles 2").Should().Equal(new CitedSpan(13, 2, 0, 0));
    }

    [Fact]
    public void ARelationIsReadOutOfAnEnglishLine()
    {
        CitedScripture.Relations("King of Judah, son of Joash (2KI 12:21); wife of King Ahab")
            .Should().BeEquivalentTo([("son", "joash"), ("wife", "ahab")]);
    }

    [Fact]
    public void TheLineCitesItsVersesInTheFormTheCorpusWritesThem()
    {
        WikipediaLinkLoader.Cited("King of Judah, son of Joash (2KI 12:21); also GEN 4:19-20")
            .Should().BeEquivalentTo([(12, 12, 21), (1, 4, 19), (1, 4, 20)]);
    }

    [Fact]
    public void TheEvidenceKindsAreTheOnesTheTableAllows()
    {
        EntityWikipedia.Evidence.All.Should().BeEquivalentTo("owner", "verse", "kin", "chapter", "name", "identification");
    }
}
