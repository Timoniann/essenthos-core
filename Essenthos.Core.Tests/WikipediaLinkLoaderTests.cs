using System.Text.Json.Nodes;
using Essenthos.Core.Configuration;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Loading.Encyclopedia;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Essenthos.Core.Tests;

/// <summary>
/// The Wikipedia links of the records: written for the languages an item has an article in, left
/// exactly as they were by a second run, decided by the owner's answers over the matching, and never
/// lost to a load that could not read the Wikidata items.
/// </summary>
[Collection(WitnessDatabaseCollection.Name)]
public sealed class WikipediaLinkLoaderTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly string _resources = Path.Combine(Path.GetTempPath(), $"wikipedia-links-{Guid.NewGuid():n}");
    private readonly string _owner;
    private readonly string _review;

    public WikipediaLinkLoaderTests(WitnessDatabase database)
    {
        _db = database.NewContext();
        Clear();
        _owner = _db.Database.GetDbConnection().Database;
        _review = Path.Combine(_resources, "Essenthos", "review", WikipediaReviewList.FileName);
        Directory.CreateDirectory(Path.GetDirectoryName(_review)!);
        Directory.CreateDirectory(Path.Combine(_resources, WikidataItems.Folder));
        File.WriteAllLines(Path.Combine(_resources, WikidataItems.Folder, WikidataItems.ItemsFile),
        [
            Item("Q1", "Habakkuk", "prophet", """{"enwiki":"Habakkuk","ukwiki":"Авакум","dewiki":"Habakuk"}"""),
            Item("Q2", "Zechariah", "King of Israel", """{"enwiki":"Zechariah of Israel"}"""),
            Item("Q3", "Zechariah", "prophet", """{"enwiki":"Zechariah (prophet)","eswiki":"Zacarías"}"""),
            Item("Q4", "Zechariah", "priest", "{}"),
        ]);
    }

    public void Dispose()
    {
        Clear();
        _db.Dispose();
        if (Directory.Exists(_resources))
        {
            Directory.Delete(_resources, recursive: true);
        }
    }

    private void Clear()
    {
        _db.Database.ExecuteSqlRaw("DELETE FROM entity");
    }

    private static string Item(string id, string label, string description, string sitelinks) =>
        new JsonObject
        {
            ["id"] = id,
            ["kind"] = "person",
            ["seeds"] = new JsonArray("class"),
            ["labels"] = new JsonObject { ["en"] = label },
            ["descriptions"] = new JsonObject { ["en"] = description },
            ["aliases"] = new JsonObject(),
            ["sitelinks"] = JsonNode.Parse(sitelinks),
            ["claims"] = new JsonObject(),
        }.ToJsonString();

    private WikipediaLinkLoader Loader(string? ownerDatabase = null) =>
        new(_db, new ReviewLists(ownerDatabase ?? _owner), NullLogger<WikipediaLinkLoader>.Instance);

    private Entity Person(string slug, string name, string line = "")
    {
        var entity = new Entity
        {
            Kind = EntityKind.Person, Slug = slug, Name = name, Distinguisher = line, SourceId = $"test:{slug}", Source = "test",
        };
        _db.Entities.Add(entity);
        _db.SaveChanges();
        return entity;
    }

    private async Task<List<string>> Held() =>
        await _db.EntityWikipedia.AsNoTracking().OrderBy(w => w.Entity!.Slug).ThenBy(w => w.Language)
            .Select(w => $"{w.Entity!.Slug} {w.Language} {w.Title} {w.Qid} {w.MatchedBy}").ToListAsync();

    private JsonObject Review() => (JsonObject)JsonNode.Parse(File.ReadAllText(_review))!;

    private void Answer(string slug, string answer, string? note = null)
    {
        var root = File.Exists(_review) ? Review() : new JsonObject { ["entries"] = new JsonArray() };
        var entries = (JsonArray)root["entries"]!;
        var entry = entries.OfType<JsonObject>().FirstOrDefault(e => (string?)e["record"] == slug);
        if (entry is null)
        {
            entries.Add(entry = new JsonObject { ["record"] = slug, ["candidates"] = new JsonArray() });
        }

        entry["decision"] = new JsonObject { ["answer"] = answer, ["note"] = note, ["at"] = "2026-10-08T10:00:00.000Z" };
        File.WriteAllText(_review, root.ToJsonString());
    }

    [Fact]
    public async Task ARecordTiedToAnItemIsLinkedInEveryLanguageTheItemHasAnArticleIn()
    {
        Person("habakkuk", "Habakkuk");

        var outcome = await Loader().Load(_resources);

        (await Held()).Should().Equal(
            "habakkuk de Habakuk Q1 name",
            "habakkuk en Habakkuk Q1 name",
            "habakkuk uk Авакум Q1 name");
        outcome.Rows.Should().Be(3);
        outcome.Linked.Should().Equal(new Dictionary<string, int> { ["name"] = 1 });
    }

    [Fact]
    public async Task ASecondRunLeavesEveryRowAndIdAsItWas()
    {
        Person("habakkuk", "Habakkuk");
        await Loader().Load(_resources);
        var ids = await _db.EntityWikipedia.AsNoTracking().OrderBy(w => w.Id).Select(w => w.Id).ToListAsync();

        var again = await Loader().Load(_resources);

        again.Written.Should().Be(0);
        again.Removed.Should().Be(0);
        (await _db.EntityWikipedia.AsNoTracking().OrderBy(w => w.Id).Select(w => w.Id).ToListAsync()).Should().Equal(ids);
    }

    [Fact]
    public async Task ANamesakeTheEvidenceCannotTellApartIsNotLinkedAndIsListedForTheOwner()
    {
        Person("zechariah", "Zechariah", "son of Jeroboam (2KI 14:29)");

        var outcome = await Loader().Load(_resources);

        (await Held()).Should().BeEmpty();
        outcome.Ambiguous.Should().Be(1);
        outcome.Questions.Should().Be(1);
        var entry = ((JsonArray)Review()["entries"]!).OfType<JsonObject>().Single();
        ((string)entry["record"]!).Should().Be("zechariah");
        ((JsonArray)entry["candidates"]!).Select(c => (string)c!["qid"]!).Should().BeEquivalentTo("Q2", "Q3");
        ((int)entry["others"]!).Should().Be(1, "the third namesake has no article to offer");
        ((JsonArray)entry["references"]!).Select(r => (string)r!).Should().Equal("2KI 14:29");
    }

    private void ZechariahTiedByAVerse() =>
        File.WriteAllLines(Path.Combine(_resources, WikidataItems.Folder, WikidataItems.ItemsFile),
        [
            Item("Q5", "Zechariah", "male human biblical figure in 2 Kings 14:29, King of Israel", """{"enwiki":"Zechariah of Israel"}"""),
            Item("Q3", "Zechariah", "prophet", """{"enwiki":"Zechariah (prophet)","eswiki":"Zacarías"}"""),
        ]);

    [Fact]
    public async Task ARecordTheMatchingTiedOnItsOwnIsListedWithTheTieAndTheOtherItemsItMightBe()
    {
        ZechariahTiedByAVerse();
        Person("zechariah", "Zechariah", "son of Jeroboam (2KI 14:29)");

        var outcome = await Loader().Load(_resources);

        (await Held()).Should().Equal("zechariah en Zechariah of Israel Q5 verse");
        outcome.Questions.Should().Be(0, "nothing is left undecided");
        outcome.Ties.Should().Be(1);
        var entry = ((JsonArray)Review()["entries"]!).OfType<JsonObject>().Single();
        ((string)entry["tied"]!["qid"]!).Should().Be("Q5");
        ((string)entry["tied"]!["by"]!).Should().Be("verse");
        ((JsonArray)entry["candidates"]!).Select(c => (string)c!["qid"]!).Should().Equal("Q5", "Q3");
    }

    [Fact]
    public async Task ATieTheOwnerSetsToAnotherItemIsLinkedByHisWordAndHisListKeepsBothTheTieAndTheAnswer()
    {
        ZechariahTiedByAVerse();
        Person("zechariah", "Zechariah", "son of Jeroboam (2KI 14:29)");
        await Loader().Load(_resources);
        Answer("zechariah", "Q3", "owner in chat: the prophet");

        await Loader().Load(_resources);
        var again = await Loader().Load(_resources);

        (await Held()).Should().Equal("zechariah en Zechariah (prophet) Q3 owner", "zechariah es Zacarías Q3 owner");
        again.Written.Should().Be(0);
        var entry = ((JsonArray)Review()["entries"]!).OfType<JsonObject>().Single();
        ((string)entry["decision"]!["answer"]!).Should().Be("Q3");
        ((string)entry["tied"]!["qid"]!).Should().Be("Q5");
        ((JsonArray)entry["candidates"]!).Select(c => (string)c!["qid"]!).Should().Contain("Q3");
    }

    [Fact]
    public async Task ATieTheOwnerTakesBackWithNoneLeavesTheRecordUnlinkedOnEveryLoad()
    {
        ZechariahTiedByAVerse();
        Person("zechariah", "Zechariah", "son of Jeroboam (2KI 14:29)");
        await Loader().Load(_resources);
        Answer("zechariah", WikipediaReviewList.None, "owner in chat: not him");

        await Loader().Load(_resources);
        await Loader().Load(_resources);

        (await Held()).Should().BeEmpty();
    }

    [Fact]
    public async Task TheOwnersChoiceLinksTheRecordToTheItemHeChose()
    {
        Person("zechariah", "Zechariah", "son of Jeroboam (2KI 14:29)");
        await Loader().Load(_resources);
        Answer("zechariah", "Q2", "owner in chat: the king");

        var outcome = await Loader().Load(_resources);

        (await Held()).Should().Equal("zechariah en Zechariah of Israel Q2 owner");
        outcome.Decided.Should().Be(1);
        (await _db.EntityWikipedia.SingleAsync()).Confidence.Should().Be(1);
        Review()["entries"]!.AsArray().OfType<JsonObject>().Single()["decision"]!["answer"]!.GetValue<string>()
            .Should().Be("Q2", "the load keeps his answer in the list");
    }

    [Fact]
    public async Task NoneAsTheAnswerLeavesTheRecordWithoutALinkEvenWhereTheMatchingWouldHaveGivenOne()
    {
        Person("habakkuk", "Habakkuk");
        await Loader().Load(_resources);
        (await Held()).Should().NotBeEmpty();
        Answer("habakkuk", WikipediaReviewList.None);

        var outcome = await Loader().Load(_resources);

        (await Held()).Should().BeEmpty();
        outcome.Removed.Should().Be(3);
        outcome.Decided.Should().Be(1);
    }

    [Fact]
    public async Task ATakenBackAnswerReturnsTheRecordToTheMatching()
    {
        Person("habakkuk", "Habakkuk");
        Answer("habakkuk", WikipediaReviewList.None);
        await Loader().Load(_resources);
        (await Held()).Should().BeEmpty();

        var root = Review();
        root["entries"]!.AsArray().OfType<JsonObject>().Single().Remove("decision");
        File.WriteAllText(_review, root.ToJsonString());
        await Loader().Load(_resources);

        (await Held()).Should().HaveCount(3);
    }

    [Fact]
    public async Task AnItemTheOwnerChoseThatWikidataNoLongerHoldsLeavesTheRecordUnlinked()
    {
        Person("zechariah", "Zechariah", "son of Jeroboam (2KI 14:29)");
        Answer("zechariah", "Q999");

        var outcome = await Loader().Load(_resources);

        (await Held()).Should().BeEmpty();
        outcome.Decided.Should().Be(1);
    }

    [Fact]
    public async Task ALoadThatCannotReadWikidataLeavesTheLinksAsTheyWere()
    {
        Person("habakkuk", "Habakkuk");
        await Loader().Load(_resources);
        File.Delete(Path.Combine(_resources, WikidataItems.Folder, WikidataItems.ItemsFile));

        var outcome = await Loader().Load(_resources);

        outcome.Skipped.Should().BeTrue();
        (await Held()).Should().HaveCount(3);
    }

    [Fact]
    public async Task ARecordTheCorpusNoLongerHoldsLosesItsLinks()
    {
        var habakkuk = Person("habakkuk", "Habakkuk");
        await Loader().Load(_resources);

        _db.Entities.Remove(habakkuk);
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();

        (await _db.EntityWikipedia.CountAsync()).Should().Be(0, "a link belongs to its record");
    }

    [Fact]
    public async Task ALoadIntoAnotherDatabaseReadsTheOwnersAnswersAndLeavesHisListAsItWas()
    {
        Person("zechariah", "Zechariah", "son of Jeroboam (2KI 14:29)");
        Answer("zechariah", "Q3");
        var his = File.ReadAllText(_review);
        var elsewhere = new ReviewLists("essenthos_owner")
            .For(_resources, WikipediaReviewList.FileName, _db.Database.GetDbConnection().Database);

        try
        {
            var outcome = await Loader("essenthos_owner").Load(_resources);

            outcome.Decided.Should().Be(1);
            (await Held()).Should().Equal("zechariah en Zechariah (prophet) Q3 owner", "zechariah es Zacarías Q3 owner");
            File.ReadAllText(_review).Should().Be(his);
            File.Exists(elsewhere).Should().BeTrue();
            elsewhere.Should().NotStartWith(_resources);
        }
        finally
        {
            File.Delete(elsewhere);
        }
    }
}
