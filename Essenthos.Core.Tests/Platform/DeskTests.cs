using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Essenthos.Core.Configuration;
using Essenthos.Core.Desk;
using Essenthos.Core.Loading.Encyclopedia;
using Essenthos.Core.Products;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Essenthos.Core.Tests.Platform;

/// <summary>
/// The owner's console, over HTTP as his browser reaches it: that it answers nobody else, that a
/// decision lands in the file the loader or the decide command reads — in the shape they read, and
/// taken back exactly when he takes it back — and that every change is in the change log. The
/// files are temporary copies and avioniq is a stand-in that records what it was asked.
/// </summary>
public sealed class DeskTests : IAsyncLifetime
{
    private const string Origin = "http://localhost:5280";

    private readonly string _root = Path.Combine(Path.GetTempPath(), $"desk-{Guid.NewGuid():n}");

    private WebApplication? _app;

    private HttpClient _http = null!;

    private string Repository => Path.Combine(_root, "repository");

    private string Review => Path.Combine(Repository, "Resources", "Essenthos", "review");

    private string Records => Path.Combine(Repository, "Essenthos.Forge", "Loading", "Encyclopedia");

    private string Calls => Path.Combine(_root, "calls.log");

    public async Task InitializeAsync()
    {
        Directory.CreateDirectory(Review);
        Directory.CreateDirectory(Records);
        Directory.CreateDirectory(Path.Combine(_root, "workspace", ".avioniq"));
        Directory.CreateDirectory(Path.Combine(_root, "resources", "Images", "generated"));
        File.WriteAllText(Path.Combine(_root, "resources", "Images", "generated", "a.png"), "not really a picture");
        File.WriteAllText(Path.Combine(_root, "secret.png"), "outside the images folder");
        File.WriteAllText(Path.Combine(_root, "fake-avioniq.js"), FakeAvioniq);

        _app = DeskApplication.Build(
        [
            "--Urls=http://127.0.0.1:0",
            $"--Desk:Repository={Repository}",
            $"--Dataset:ResourcesPath={Path.Combine(_root, "resources")}",
            $"--Desk:Workspace={Path.Combine(_root, "workspace")}",
            "--Desk:Avioniq:0=node",
            $"--Desk:Avioniq:1={Path.Combine(_root, "fake-avioniq.js")}",
            $"--Desk:Origins:0={Origin}",
            "--Desk:Operations:0=core-images",
            "--Desk:Operations:1=core-clear",
            "--Desk:Operations:2=failing",
            "--Desk:Operations:3=core-unregistered",
            "--Database:ConnectionString=Host=localhost;Port=5437;Database=unused;Username=unused",
            "--Database:Password=unused",
        ], contentRoot: _root);
        await _app.StartAsync();
        _http = new HttpClient { BaseAddress = new Uri(_app.Urls.First()) };
    }

    public async Task DisposeAsync()
    {
        _http.Dispose();
        if (_app is not null)
        {
            await _app.StopAsync();
            await _app.DisposeAsync();
        }

        Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public async Task ARequestAddressedByAnotherNameIsRefused()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/desk-api/history");
        request.Headers.Host = "attacker.example";

        (await _http.SendAsync(request)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Theory]
    [InlineData("Origin", "http://attacker.example")]
    [InlineData("Sec-Fetch-Site", "cross-site")]
    public async Task AnotherSitesPageCannotActInTheOwnersName(string header, string value)
    {
        using var request = new HttpRequestMessage(HttpMethod.Put, "/desk-api/settings/naveTopics")
        {
            Content = JsonContent.Create(new { value = false, note = "" }),
        };
        request.Headers.Add(header, value);

        (await _http.SendAsync(request)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        File.Exists(SiteSettingsFile).Should().BeFalse();
        File.Exists(ChangeLogFile).Should().BeFalse();
    }

    [Fact]
    public void TheConsoleRefusesToListenBeyondTheLoopback()
    {
        var build = () => DeskApplication.Build(["--Urls=http://0.0.0.0:5281"], contentRoot: _root);

        build.Should().Throw<InvalidOperationException>().WithMessage("*not this machine's loopback*");
    }

    [Fact]
    public async Task ASwitchForTheSiteIsWrittenWhereTheApiReadsItAndLogged()
    {
        var before = await Json<SiteSwitchesResponse>(await _http.GetAsync("/desk-api/settings"));
        before.Settings.Select(s => s.Key).Should().Equal(SiteSettings.Catalogue.Select(s => s.Key));
        before.Settings.Should().OnlyContain(s => s.Value == s.Default, "nothing is written until the owner sets a switch");

        (await Put("/desk-api/settings/naveTopics", new { value = false, note = "до кінця розгляду" })).StatusCode
            .Should().Be(HttpStatusCode.OK);

        var read = SiteSettings.Read(SiteSettingsFile);
        (read[SiteSettings.NaveTopics], read[SiteSettings.GeneratedImages]).Should().Be((false, true));
        var logged = Logged().Should().ContainSingle().Which;
        (logged.Section, logged.Action, logged.Target, logged.Needs).Should().Be(("settings", "switch", "setting/naveTopics", null));
        (logged.Before!.GetValue<bool>(), logged.After!.GetValue<bool>(), logged.Note).Should().Be((true, false, "до кінця розгляду"));
        File.ReadAllText(ChangeLogFile).Should().Contain("до кінця розгляду", "Cyrillic is written as it is typed");

        (await Put("/desk-api/settings/nothing", new { value = true, note = "" })).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task TheApiServesWhatTheConsoleSetOnTheNextRequest()
    {
        var served = new Essenthos.Core.Endpoints.SiteSettingsFile(
            SiteSettingsFile, Microsoft.Extensions.Logging.Abstractions.NullLogger<Essenthos.Core.Endpoints.SiteSettingsFile>.Instance);
        served.Is(SiteSettings.GeneratedImages).Should().BeTrue("with no file every switch is at its default");

        await Put("/desk-api/settings/generatedImages", new { value = false, note = "" });
        served.Is(SiteSettings.GeneratedImages).Should().BeFalse();

        await Put("/desk-api/settings/generatedImages", new { value = true, note = "" });
        File.SetLastWriteTimeUtc(SiteSettingsFile, DateTime.UtcNow.AddSeconds(1));
        served.Is(SiteSettings.GeneratedImages).Should().BeTrue();
    }

    [Fact]
    public async Task TheLicenceOfOurOwnWorkIsChosenOnceLoggedAndServedByTheApi()
    {
        var served = new Essenthos.Core.Endpoints.SiteSettingsFile(
            SiteSettingsFile, Microsoft.Extensions.Logging.Abstractions.NullLogger<Essenthos.Core.Endpoints.SiteSettingsFile>.Instance);
        var before = await Json<SiteSwitchesResponse>(await _http.GetAsync("/desk-api/settings"));
        (before.Licence.Value, before.Licence.Default).Should().Be((OwnWorkLicences.Undecided, OwnWorkLicences.Undecided));
        before.Licence.Options.Select(o => o.Id).Should().Contain(["cc-by-4.0", "cc0-1.0", "all-rights-reserved"]);
        served.OwnWorkLicence.Should().BeNull("while the owner has not decided, the site states no licence");

        var chosen = await Json<LicenceSetting>(await Put("/desk-api/settings/licence", new { value = "cc-by-sa-4.0", note = "поки що" }));

        chosen.Value.Should().Be("cc-by-sa-4.0");
        served.OwnWorkLicence.Should().Be(new OwnWorkLicence("cc-by-sa-4.0", "CC BY-SA 4.0", "https://creativecommons.org/licenses/by-sa/4.0/"));
        SiteSettings.Read(SiteSettingsFile).Should().BeEquivalentTo(SiteSettings.Defaults(), "a choice leaves the switches as they were");
        var logged = Logged().Should().ContainSingle().Which;
        (logged.Section, logged.Action, logged.Target, logged.Label, logged.Needs)
            .Should().Be(("settings", "choice", "setting/ownWorkLicence", "CC BY-SA 4.0", null));
        (logged.Before!.GetValue<string>(), logged.After!.GetValue<string>(), logged.Note).Should().Be((OwnWorkLicences.Undecided, "cc-by-sa-4.0", "поки що"));

        (await Put("/desk-api/settings/licence", new { value = "mit", note = "" })).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        SiteSettings.ReadChoices(SiteSettingsFile)[SiteSettings.OwnWorkLicence].Should().Be("cc-by-sa-4.0");

        await Put("/desk-api/settings/licence", new { value = OwnWorkLicences.Undecided, note = "" });
        File.SetLastWriteTimeUtc(SiteSettingsFile, DateTime.UtcNow.AddSeconds(1));
        served.OwnWorkLicence.Should().BeNull();
        Logged().Should().HaveCount(2);
    }

    [Fact]
    public void ALicenceTheSiteDoesNotOfferIsReadAsUndecided() =>
        SiteSettings.ChoicesFrom(JsonNode.Parse("""{ "settings": { "ownWorkLicence": "GPL" } }"""))[SiteSettings.OwnWorkLicence]
            .Should().Be(OwnWorkLicences.Undecided);

    [Fact]
    public void ASwitchTheFileDoesNotNameIsAtItsDefaultAndOneTheSiteDoesNotKnowIsIgnored()
    {
        var file = JsonNode.Parse("""{ "settings": { "naveTopics": false, "somethingNew": true, "generatedImages": "yes" } }""");

        var read = SiteSettings.From(file);

        read.Keys.Should().BeEquivalentTo(SiteSettings.Catalogue.Select(s => s.Key));
        (read[SiteSettings.NaveTopics], read[SiteSettings.GeneratedImages]).Should().Be((false, true));
        SiteSettings.Read(Path.Combine(_root, "absent.json")).Should().BeEquivalentTo(SiteSettings.Defaults());
    }

    /// <summary>
    /// The log is appended to and never rewritten, one object a line, and a change that needs the
    /// pictures loaded again waits until a run of that step from the console has succeeded after it.
    /// </summary>
    [Fact]
    public async Task AChangeWaitsInTheLogUntilTheStepItNeedsHasRun()
    {
        var log = new ChangeLog(DeskPaths.Read(_app!.Services.GetRequiredService<IConfiguration>()));
        await log.Append("pictures", "choice", "jerusalem openbible/a.jpg", null, new JsonObject { ["hidden"] = true }, null, "images");
        await log.Append("occurrences", "answer", "objects-and-observances boaz-pillar H1162 1KI 7:21", null, null, null, "load");

        var started = await Json<RunStarted>(await _http.PostAsync("/desk-api/operations/core-images/runs", null));
        await Finished(started.Run!.Id);
        await Task.Delay(200);
        await log.Append("pictures", "choice", "jerusalem openbible/b.jpg", null, null, null, "images");

        var read = await Json<ChangeLogResponse>(await _http.GetAsync("/desk-api/history"));

        read.Entries.Select(e => (e.Line, e.Section, e.Waiting)).Should().Equal(
            (4, "pictures", true), (3, "apply", false), (2, "occurrences", true), (1, "pictures", false));
        read.Waiting.Should().BeEquivalentTo(new Dictionary<string, int> { ["images"] = 1, ["load"] = 1 });
        File.ReadAllLines(ChangeLogFile).Should().HaveCount(4).And.OnlyContain(line => line.StartsWith("{\"at\":"));
    }

    [Fact]
    public async Task TheWikipediaListIsEmptyUntilALoadHasWrittenIt()
    {
        var read = await Json<WikipediaQuestionsResponse>(await _http.GetAsync("/desk-api/review/wikipedia"));

        (read.Written, read.Open).Should().Be((false, 0));
        read.Entries.Should().BeEmpty();
    }

    [Fact]
    public async Task AnsweringARecordWithAnItemWritesTheChoiceWhereTheLoadReadsItAndLogsIt()
    {
        WriteWikipedia();

        var response = await Put("/desk-api/review/wikipedia/zechariah-5", new { answer = "Q3", note = "the prophet" });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var entry = JsonFiles.Read(Path.Combine(Review, WikipediaReview.FileName))["entries"]![0]!;
        var decision = entry["decision"]!;
        (decision["answer"]!.GetValue<string>(), decision["note"]!.GetValue<string>()).Should().Be(("Q3", "the prophet"));
        decision["at"]!.GetValue<string>().Should().MatchRegex(@"^\d{4}-\d\d-\d\dT");
        var logged = Logged().Should().ContainSingle().Which;
        (logged.Section, logged.Action, logged.Target, logged.Needs, logged.Note).Should().Be(
            ("wikipedia", "match", "person/zechariah-5", "load", "the prophet"));
        logged.Before.Should().BeNull();
        logged.After!.GetValue<string>().Should().Be("Q3 Zechariah");

        var read = await Json<WikipediaQuestionsResponse>(await _http.GetAsync("/desk-api/review/wikipedia"));
        (read.Written, read.Open, read.Answered).Should().Be((true, 0, 1));
        (read.Files, read.Unapplied, read.Entries.Single().OnSite).Should().Be((false, 0, null), "the site's file is not in this checkout");
        var summary = await Json<SummaryResponse>(await _http.GetAsync("/desk-api/summary"));
        summary.Counts.Single(c => c.Key == "wikipedia").Count.Should().Be(0, "the one record is answered");
    }

    [Fact]
    public async Task ARecordTheMatchingTiedOnItsOwnIsListedApartFromTheOpenOnesAndCanBeSetToAnotherItem()
    {
        File.WriteAllText(Path.Combine(Review, WikipediaReview.FileName),
            """
            {
              "entries": [
                {
                  "record": "immanuel", "kind": "person", "label": "Immanuel", "line": "the son of Isaiah (ISA 8:2)",
                  "references": ["ISA 8:2"],
                  "candidates": [
                    { "qid": "Q7", "label": "Maher-shalal-hash-baz", "description": "son of Isaiah", "articles": { "en": "Maher-Shalal-Hash-Baz" }, "evidence": ["kin"] },
                    { "qid": "Q8", "label": "Immanuel", "description": "sign", "articles": { "en": "Immanuel" }, "evidence": [] }
                  ],
                  "others": 0,
                  "tied": { "qid": "Q7", "by": "kin" }
                }
              ]
            }
            """);

        var read = await Json<WikipediaQuestionsResponse>(await _http.GetAsync("/desk-api/review/wikipedia"));
        (read.Open, read.Tied, read.Answered).Should().Be((0, 1, 0));
        read.Entries.Single().Tied.Should().NotBeNull().And.Subject.Should().BeEquivalentTo(new { Qid = "Q7", By = "kin" });
        var summary = await Json<SummaryResponse>(await _http.GetAsync("/desk-api/summary"));
        summary.Counts.Single(c => c.Key == "wikipedia").Count.Should().Be(0, "a tie of the matching is not waiting for an answer");

        (await Put("/desk-api/review/wikipedia/immanuel", new { answer = "Q8", note = "the sign, not the child" })).StatusCode
            .Should().Be(HttpStatusCode.OK);

        var after = await Json<WikipediaQuestionsResponse>(await _http.GetAsync("/desk-api/review/wikipedia"));
        (after.Open, after.Tied, after.Answered).Should().Be((0, 0, 1));
        var logged = Logged().Should().ContainSingle().Which;
        logged.After!.GetValue<string>().Should().Be("Q8 Immanuel");
    }

    [Fact]
    public async Task NoneIsAnAnswerAndTakingAnAnswerBackRemovesIt()
    {
        WriteWikipedia();

        (await Put("/desk-api/review/wikipedia/zechariah-5", new { answer = "none", note = "" })).StatusCode.Should().Be(HttpStatusCode.OK);
        (await Put("/desk-api/review/wikipedia/zechariah-5", new { answer = (string?)null, note = "" })).StatusCode.Should().Be(HttpStatusCode.OK);

        JsonFiles.Read(Path.Combine(Review, WikipediaReview.FileName))["entries"]![0]!["decision"].Should().BeNull();
        Logged().Select(l => (l.Before?.GetValue<string>(), l.After?.GetValue<string>())).Should().Equal(
            ((string?)null, "none"), ("none", null));
    }

    [Fact]
    public async Task TheWikipediaPageSaysWhatTheSitesFileLinksAndWhichAnswersItDoesNotShowYet()
    {
        WriteWikipedia();
        var list = JsonFiles.Read(Path.Combine(Review, WikipediaReview.FileName));
        list["entries"]![0]!["tied"] = new JsonObject { ["qid"] = "Q2", ["by"] = "name" };
        JsonFiles.Write(Path.Combine(Review, WikipediaReview.FileName), list);
        var store = new ProductStore(Path.Combine(_root, "resources"));
        KnownForFile(store, "records/facts/wikipedia", ["zechariah-5", "en", "Zechariah of Israel", "Q2", "name", "0.8"]);

        var read = await Json<WikipediaQuestionsResponse>(await _http.GetAsync("/desk-api/review/wikipedia"));

        (read.Files, read.Unapplied, read.Entries.Single().OnSite).Should().Be((true, 0, "Q2"));

        (await Put("/desk-api/review/wikipedia/zechariah-5", new { answer = "Q3", note = "" })).StatusCode.Should().Be(HttpStatusCode.OK);
        read = await Json<WikipediaQuestionsResponse>(await _http.GetAsync("/desk-api/review/wikipedia"));
        read.Unapplied.Should().Be(1, "the file shows the king until the build makes it again from the answer");

        KnownForFile(store, "records/facts/wikipedia", ["zechariah-5", "en", "Zechariah (prophet)", "Q3", "owner", "1"]);
        read = await Json<WikipediaQuestionsResponse>(await _http.GetAsync("/desk-api/review/wikipedia"));
        (read.Unapplied, read.Entries.Single().OnSite).Should().Be((0, "Q3"));

        (await Put("/desk-api/review/wikipedia/zechariah-5", new { answer = "none", note = "" })).StatusCode.Should().Be(HttpStatusCode.OK);
        read = await Json<WikipediaQuestionsResponse>(await _http.GetAsync("/desk-api/review/wikipedia"));
        read.Unapplied.Should().Be(1, "the file still links the record he unlinked");

        (await Put("/desk-api/review/wikipedia/zechariah-5", new { answer = (string?)null, note = "" })).StatusCode.Should().Be(HttpStatusCode.OK);
        read = await Json<WikipediaQuestionsResponse>(await _http.GetAsync("/desk-api/review/wikipedia"));
        read.Unapplied.Should().Be(1, "an answer taken back gives the record the matching's tie again at the next build");
    }

    [Fact]
    public async Task AnItemTheRecordWasNotOfferedIsRefusedAndNothingIsWrittenOrLogged()
    {
        WriteWikipedia();
        var before = File.ReadAllText(Path.Combine(Review, WikipediaReview.FileName));

        (await Put("/desk-api/review/wikipedia/zechariah-5", new { answer = "Q999", note = "" })).StatusCode
            .Should().Be(HttpStatusCode.UnprocessableEntity);
        (await Put("/desk-api/review/wikipedia/nobody", new { answer = "none", note = "" })).StatusCode
            .Should().Be(HttpStatusCode.UnprocessableEntity);

        File.ReadAllText(Path.Combine(Review, WikipediaReview.FileName)).Should().Be(before);
        Logged().Should().BeEmpty();
    }

    [Fact]
    public async Task TheKnownForListIsEmptyUntilItIsWritten()
    {
        var read = await Json<KnownForReviewResponse>(await _http.GetAsync("/desk-api/review/known-for"));

        (read.Written, read.Open).Should().Be((false, 0));
        read.Entries.Should().BeEmpty();
    }

    [Fact]
    public async Task ApprovingWhatARecordIsKnownForWritesTheAnswerBesideItsEntryAndLogsItForTheFiles()
    {
        WriteKnownFor();

        var read = await Json<KnownForReviewResponse>(await _http.GetAsync("/desk-api/review/known-for"));
        (read.Written, read.Open, read.Approved, read.Rejected).Should().Be((true, 2, 0, 0));
        var sodom = read.Entries.Single(e => e.Slug == "sodom");
        sodom.Clauses.Single().Addresses.Should().Equal("genesis:19:24");
        read.Declined.Single().Reason.Should().Be("only listed");

        var response = await Put("/desk-api/review/known-for/sodom", new { answer = "approved", note = "так" });
        (await Put("/desk-api/review/known-for/bethel", new { answer = "rejected", note = "" })).StatusCode
            .Should().Be(HttpStatusCode.OK);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        KnownForReviewList.Approved(Path.Combine(Review, KnownForReview.FileName)).Records
            .Select(r => (r.Slug, r.Clauses.Single().English))
            .Should().Equal(("sodom", "was overthrown when the LORD rained brimstone and fire on it"));
        var logged = Logged();
        logged.Select(l => (l.Section, l.Action, l.Target, l.Needs, l.Label, l.After?.GetValue<string>())).Should().Equal(
            ("known-for", "verdict", "place/sodom", "files", "Sodom", "approved"),
            ("known-for", "verdict", "place/bethel", "files", "Bethel", "rejected"));
        logged[0].Note.Should().Be("так");
        var summary = await Json<SummaryResponse>(await _http.GetAsync("/desk-api/summary"));
        summary.Counts.Single(c => c.Key == "known-for").Count.Should().Be(0, "both records are answered");
        summary.Waiting["files"].Should().Be(2, "an agent writes both into the build's files");
        read = await Json<KnownForReviewResponse>(await _http.GetAsync("/desk-api/review/known-for"));
        (read.Files, read.Unapplied).Should().Be((false, 0), "the build's files are not in this checkout");
        read.Entries.Select(e => e.Shown).Should().AllSatisfy(shown => shown.Should().BeNull());
    }

    [Fact]
    public async Task TheKnownForPageShowsWhatTheBuildsFilesHoldAndNeverWritesThem()
    {
        WriteKnownFor();
        var resources = Path.Combine(_root, "resources");
        var store = new ProductStore(resources);
        KnownForFile(store, "records/facts/known-for", ["sodom", "0", "a model, approved by the owner"]);
        KnownForFile(store, "records/facts/known-for-references", ["sodom", "0", "0", "1.19:24", "19", "25"]);
        KnownForFile(store, "records/i18n/eng/known-for", ["sodom", "0", "was overthrown, as an agent corrected it in the file"]);
        KnownForFile(store, "records/i18n/ukr/known-for", ["sodom", "0", "був зруйнований"]);
        string Files() => string.Join("\n", Directory.GetFiles(Path.Combine(resources, "Essenthos", "records"), "*", SearchOption.AllDirectories)
            .Order(StringComparer.Ordinal).Select(f => f + "\n" + File.ReadAllText(f)));
        var files = Files();

        var read = await Json<KnownForReviewResponse>(await _http.GetAsync("/desk-api/review/known-for"));

        (read.Files, read.Unapplied).Should().Be((true, 0));
        var sodom = read.Entries.Single(e => e.Slug == "sodom");
        sodom.Shown.Should().BeTrue();
        var clause = sodom.Clauses.Single();
        (clause.English, clause.Ukr, clause.Deu).Should().Be(("was overthrown, as an agent corrected it in the file", "був зруйнований", null),
            "what is shown is what the files hold, not the list's proposal");
        clause.References.Should().Equal("Gen 19:24-25");
        clause.Addresses.Should().Equal("genesis:19:24");
        read.Entries.Single(e => e.Slug == "bethel").Shown.Should().BeFalse();

        (await Put("/desk-api/review/known-for/sodom", new { answer = "rejected", note = "ні" })).StatusCode.Should().Be(HttpStatusCode.OK);
        (await Put("/desk-api/review/known-for/bethel", new { answer = "approved", note = "" })).StatusCode.Should().Be(HttpStatusCode.OK);

        read = await Json<KnownForReviewResponse>(await _http.GetAsync("/desk-api/review/known-for"));
        read.Unapplied.Should().Be(2, "a rejection the files still show and an approval they do not, until an agent applies them");

        Files().Should().Be(files, "the console never writes the build's files");
        Logged().Select(l => l.Needs).Should().Equal("files", "files");

        KnownForFile(store, "records/folds", ["bethel", "luz", "Bethel", null, "manual", null, "a test", "one place"]);
        read = await Json<KnownForReviewResponse>(await _http.GetAsync("/desk-api/review/known-for"));
        read.Entries.Single(e => e.Slug == "bethel").FoldedInto.Should().Be("luz");
        read.Unapplied.Should().Be(1, "a record folded into another shows that record's own line, so its approval has nothing to write");
    }

    private static void KnownForFile(ProductStore store, string product, string?[] row)
    {
        var file = Essenthos.Core.Records.RecordFiles.All.Single(f => f.Product == product);
        using var writer = store.Create(file.Product, file.Kind, file.Schema(), "test", []);
        writer.WriteAll([row]);
    }

    [Fact]
    public async Task AKnownForAnswerCanBeTakenBackAndAnythingButApprovedOrRejectedIsRefused()
    {
        WriteKnownFor();
        var before = File.ReadAllText(Path.Combine(Review, KnownForReview.FileName));

        (await Put("/desk-api/review/known-for/sodom", new { answer = "maybe", note = "" })).StatusCode
            .Should().Be(HttpStatusCode.UnprocessableEntity);
        (await Put("/desk-api/review/known-for/nobody", new { answer = "approved", note = "" })).StatusCode
            .Should().Be(HttpStatusCode.UnprocessableEntity);
        File.ReadAllText(Path.Combine(Review, KnownForReview.FileName)).Should().Be(before);
        Logged().Should().BeEmpty();

        (await Put("/desk-api/review/known-for/sodom", new { answer = "approved", note = "" })).StatusCode.Should().Be(HttpStatusCode.OK);
        (await Put("/desk-api/review/known-for/sodom", new { answer = (string?)null, note = "" })).StatusCode.Should().Be(HttpStatusCode.OK);

        JsonFiles.Read(Path.Combine(Review, KnownForReview.FileName))["records"]![0]!["decision"].Should().BeNull();
        KnownForReviewList.Approved(Path.Combine(Review, KnownForReview.FileName)).Records.Should().BeEmpty();
        Logged().Select(l => (l.Before?.GetValue<string>(), l.After?.GetValue<string>())).Should().Equal(
            ((string?)null, "approved"), ("approved", null));
    }

    private void WriteKnownFor() =>
        File.WriteAllText(Path.Combine(Review, KnownForReview.FileName),
            """
            {
              "about": "a test",
              "source": "written by a model",
              "records": [
                {
                  "slug": "sodom", "kind": "place", "name": "Sodom", "sex": null, "verses": 50,
                  "clauses": [
                    { "english": "was overthrown when the LORD rained brimstone and fire on it", "ukr": "був зруйнований", "deu": "wurde zerstört", "spa": "fue destruida", "references": ["GEN 19:24-25"], "renderingsChanged": null }
                  ],
                  "readings": { "first": { "reason": null, "doubts": [] }, "second": [{ "verdict": "right", "why": null }], "note": null }
                },
                {
                  "slug": "bethel", "kind": "place", "name": "Bethel", "sex": null, "verses": 71,
                  "clauses": [
                    { "english": "was named by Jacob", "ukr": "був названий Яковом", "deu": "wurde von Jakob benannt", "spa": "fue nombrada por Jacob", "references": ["GEN 28:19"], "renderingsChanged": null }
                  ],
                  "readings": { "first": { "reason": null, "doubts": [] }, "second": [{ "verdict": "right", "why": null }], "note": null }
                }
              ],
              "declined": [{ "slug": "ashbel", "kind": "person", "name": "Ashbel", "verses": 6, "reason": "only listed" }],
              "unfinished": []
            }
            """);

    [Fact]
    public async Task TheAgentsProposalIsShownBesideTheEntryAndOneTheEntryNoLongerOffersIsDropped()
    {
        WriteWikipedia();
        WriteProposals(
            """{ "record": "zechariah-5", "answer": "Q2", "confidence": "medium", "agreed": false, "why": "the king of 2KI 14:29", "other": "Q999", "otherWhy": "gone" }""",
            """{ "record": "nobody", "answer": "none", "confidence": "high", "agreed": true, "why": "not in the list" }""");

        var read = await Json<WikipediaQuestionsResponse>(await _http.GetAsync("/desk-api/review/wikipedia"));

        read.Entries.Single().Proposal.Should().BeEquivalentTo(
            new WikipediaProposal("Q2", "medium", false, "the king of 2KI 14:29", null, "gone"));
    }

    [Fact]
    public async Task AcceptingTheAgreedProposalsAnswersOnlyOpenRecordsWhoseTwoReadingsAgreeWithHighConfidence()
    {
        File.WriteAllText(Path.Combine(Review, WikipediaReview.FileName),
            """
            {
              "entries": [
                { "record": "a", "kind": "person", "label": "A", "references": [], "candidates": [ { "qid": "Q1", "label": "A", "articles": {}, "evidence": [] } ], "others": 0 },
                { "record": "b", "kind": "person", "label": "B", "references": [], "candidates": [ { "qid": "Q2", "label": "B", "articles": {}, "evidence": [] } ], "others": 0 },
                { "record": "c", "kind": "person", "label": "C", "references": [], "candidates": [ { "qid": "Q3", "label": "C", "articles": {}, "evidence": [] } ], "others": 0 },
                { "record": "d", "kind": "place", "label": "D", "references": [], "candidates": [ { "qid": "Q4", "label": "D", "articles": {}, "evidence": [] } ], "others": 0, "tied": { "qid": "Q4", "by": "name" } },
                { "record": "e", "kind": "person", "label": "E", "references": [], "candidates": [ { "qid": "Q5", "label": "E", "articles": {}, "evidence": [] } ], "others": 0, "decision": { "answer": "Q5", "note": "mine", "at": "2026-10-01T00:00:00Z" } }
              ]
            }
            """);
        WriteProposals(
            """{ "record": "a", "answer": "Q1", "confidence": "high", "agreed": true, "why": "" }""",
            """{ "record": "b", "answer": "none", "confidence": "high", "agreed": true, "why": "" }""",
            """{ "record": "c", "answer": "Q3", "confidence": "medium", "agreed": true, "why": "" }""",
            """{ "record": "d", "answer": "none", "confidence": "high", "agreed": true, "why": "" }""",
            """{ "record": "e", "answer": "none", "confidence": "high", "agreed": true, "why": "" }""");

        var response = await _http.PostAsync("/desk-api/review/wikipedia/accept-agreed", null);

        (await Json<WikipediaAcceptResponse>(response)).Accepted.Should().Be(2);
        var read = await Json<WikipediaQuestionsResponse>(await _http.GetAsync("/desk-api/review/wikipedia"));
        read.Entries.Select(e => (e.Record, e.Decision?.Answer)).Should().Equal(
            ("a", "Q1"), ("b", "none"), ("c", null), ("d", null), ("e", "Q5"));
        Logged().Select(l => (l.Target, l.After?.GetValue<string>(), l.Needs)).Should().Equal(
            ("person/a", "Q1 A", "load"), ("person/b", "none", "load"));
    }

    private void WriteProposals(params string[] entries) =>
        File.WriteAllText(Path.Combine(Review, WikipediaReview.ProposalsFileName),
            $$"""{ "entries": [ {{string.Join(", ", entries)}} ] }""");

    private void WriteWikipedia() =>
        File.WriteAllText(Path.Combine(Review, WikipediaReview.FileName),
            """
            {
              "about": "Records whose article could not be told from a namesake's.",
              "entries": [
                {
                  "record": "zechariah-5",
                  "kind": "person",
                  "label": "Zechariah",
                  "line": "son of Jeroboam (2KI 14:29)",
                  "references": ["2KI 14:29"],
                  "candidates": [
                    { "qid": "Q2", "label": "Zechariah", "description": "King of Israel", "articles": { "en": "Zechariah of Israel" }, "evidence": [] },
                    { "qid": "Q3", "label": "Zechariah", "description": "prophet", "articles": { "en": "Zechariah (prophet)", "uk": "Захарія" }, "evidence": ["chapter"] }
                  ],
                  "others": 2
                }
              ]
            }
            """);

    /// <summary>
    /// The verses to decide and the objects and feasts take no answers since 2026-11-05 (NOT-0219): the owner gives a
    /// correction to the lead in chat. A PUT to either is not a route of the console, and nothing is written or logged.
    /// </summary>
    [Theory]
    [InlineData("/desk-api/review/occurrences/{0}", "boaz-pillar")]
    [InlineData("/desk-api/review/records/boaz-pillar", null)]
    public async Task TheVersesToDecideAndTheObjectsAndFeastsTakeNoAnswers(string path, string? occurrenceOf)
    {
        WriteThings();
        var target = occurrenceOf is null ? path : string.Format(path, await OccurrenceKey(occurrenceOf));
        var files = new[]
        {
            Path.Combine(Review, ThingReview.QuestionsFile), Path.Combine(Records, ThingReview.ObjectsFile),
            Path.Combine(Records, ThingReview.ObservancesFile),
        };
        var before = files.Select(File.ReadAllText).ToList();

        var response = await Put(target, new { answer = "boaz-pillar", scope = "all", note = "" });

        response.StatusCode.Should().BeOneOf(HttpStatusCode.NotFound, HttpStatusCode.MethodNotAllowed);
        files.Select(File.ReadAllText).Should().Equal(before);
        File.Exists(ChangeLogFile).Should().BeFalse("nothing is logged");
    }

    /// <summary>
    /// Each question shows the edits the build's correction files hold for it: a row of
    /// <c>Essenthos.Forge/Records/OwnerRows/*.tsv</c> names its list and entry (<c>objects:0</c> is the first entry of
    /// the objects' list), and its word key is shown as a verse and the word's number.
    /// </summary>
    [Fact]
    public async Task EachQuestionShowsTheEditsTheCorrectionFilesHoldForIt()
    {
        WriteThings();
        var rows = Path.Combine(Repository, "Essenthos.Forge", "Records", "OwnerRows");
        Directory.CreateDirectory(rows);
        File.WriteAllText(Path.Combine(rows, "2026-11-05-review-lists.tsv"),
            "entry\tproduct\top\tat\trecord\tfrom\trole\tstrong\tlabel\twhy\n" +
            "objects:0\tnames/BHSA\tremove\t11.7:21.33\tboaz\t\\N\t\\N\tH1162\t\\N\tthe pillar, not the man\n" +
            "objects:0\tnames/BHSA\tadd\t11.7:21.33\tboaz-pillar\t\\N\tthing\tH1162\t\\N\tthe pillar, not the man\n");

        var read = await Json<ThingQuestionsResponse>(await _http.GetAsync("/desk-api/review/occurrences"));

        read.Lists.Should().ContainSingle().Which.Should().Be(
            new ThingList("objects-and-observances", "Occurrences a reading could not settle.", 2, 2, 1));
        var boaz = read.Entries.Single(e => e.RecordSlug == "boaz-pillar");
        boaz.Rows.Select(r => (r.Op, r.RecordSlug, r.Record?.Name, r.Reference, r.Word, r.Product, r.Why)).Should().Equal(
            ("remove", "boaz", (string?)null, "1 Kgs 7:21", (int?)33, "names/BHSA", "the pillar, not the man"),
            ("add", "boaz-pillar", "Boaz", "1 Kgs 7:21", 33, "names/BHSA", "the pillar, not the man"));
        read.Entries.Single(e => e.RecordSlug == "new-moon").Rows.Should().BeEmpty();
    }

    [Theory]
    [InlineData("11.7:21.33", "1 Kgs 7:21", 33)]
    [InlineData("13.19:5", "1 Chr 19:5", null)]
    [InlineData("not a key", "not a key", null)]
    public void AKeyOfTheBuildsFilesIsShownAsAVerse(string at, string reference, int? word) =>
        ThingReview.Point(at).Should().Be((reference, word));

    /// <summary>
    /// A list written later in the same shape — the Septuagint's placements — is listed on the same page by the key
    /// its entries carry, with an answer given before shown; files of the review folder of another shape are not lists.
    /// </summary>
    [Fact]
    public async Task AnyListOfTheSameShapeIsListedAndItsOwnKeysAreKept()
    {
        WriteThings();
        File.WriteAllText(Path.Combine(Review, "septuagint-placement.json"),
            """
            {
              "about": "Where the Greek stands beside Hebrew it does not carry.",
              "entries": [
                { "key": "small-swaps", "edition": "both", "references": ["GEN 31:46-52"], "question": "Place them by their words?",
                  "options": ["place them by their words", "show them as printed"],
                  "decision": { "answer": "place them by their words", "note": "", "decidedAt": "2026-10-01T00:00:00Z", "applied": "by-hand" } }
              ]
            }
            """);
        File.WriteAllText(Path.Combine(Review, "evidentia-gold-errors.json"), """{ "errors": [] }""");
        File.WriteAllText(Path.Combine(Review, "bibledata-removed.json"), """[{ "id": "1", "why": "a record of a past decision" }]""");

        var read = await Json<ThingQuestionsResponse>(await _http.GetAsync("/desk-api/review/occurrences"));

        read.Lists.Select(l => (l.List, l.Open)).Should().Equal(("objects-and-observances", 2), ("septuagint-placement", 0));
        var entry = read.Entries.Single(e => e.List == "septuagint-placement");
        entry.Key.Should().Be("small-swaps");
        entry.Decision!["answer"]!.GetValue<string>().Should().Be("place them by their words");
        entry.Options.Select(o => o.Text).Should().Equal("place them by their words", "show them as printed");
    }

    [Fact]
    public async Task TheRecordsSayHowFarEachWasReviewed()
    {
        WriteThings();
        var objects = JsonFiles.Read(Path.Combine(Records, ThingReview.ObjectsFile));
        objects["records"]![0]!["reviewed"] = "the project owner, 2026-10-01";
        JsonFiles.Write(Path.Combine(Records, ThingReview.ObjectsFile), objects);

        var records = await Json<ThingRecordsResponse>(await _http.GetAsync("/desk-api/review/records"));

        records.Records.Single(r => r.Record["slug"]!.GetValue<string>() == "boaz-pillar").Scope.Should().Be("record");
        records.Records.Single(r => r.Record["slug"]!.GetValue<string>() == "new-moon").Scope.Should().BeNull();
    }

    /// <summary>The narrative records are listed under their own file.</summary>
    [Fact]
    public async Task TheNarrativeRecordsAreListedUnderTheirOwnFile()
    {
        WriteThings();
        File.WriteAllText(Path.Combine(Records, ThingReview.NarrativesFile),
            """
            {
              "decidedBy": "a test",
              "records": [
                { "slug": "serpent", "kind": "person", "name": "The serpent", "distinguisher": "the serpent in the garden",
                  "reviewed": "the project owner, 2026-10-01",
                  "occurrences": [{ "strong": "H5175", "only": ["GEN 3:1"], "reviewed": "the project owner, 2026-10-01" }] }
              ]
            }
            """);

        var records = await Json<ThingRecordsResponse>(await _http.GetAsync("/desk-api/review/records"));

        var serpent = records.Records.Single(r => r.Record["slug"]!.GetValue<string>() == "serpent");
        (serpent.File, serpent.Scope, serpent.Rules, serpent.ReviewedRules).Should().Be(("narratives", "all", 1, 1));
        records.Records.Select(r => r.File).Should().Contain(["objects", "observances"]);
    }

    [Fact]
    public async Task OnlyTheListedOperationsRunAndNeverOneThatStopsTheCore()
    {
        var operations = await Json<OperationsResponse>(await _http.GetAsync("/desk-api/operations"));

        operations.Steps.Select(o => (o.Name, o.Step, o.Registered)).Should().Equal(
            ("core-images", "images", true), ("failing", "failing", true), ("core-unregistered", "core-unregistered", false));

        (await _http.PostAsync("/desk-api/operations/core-clear/runs", null)).StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await _http.PostAsync("/desk-api/operations/core-unregistered/runs", null)).StatusCode.Should().Be(HttpStatusCode.Conflict);
        Called().Should().NotContain(call => call.Contains("core-clear"));
    }

    [Fact]
    public async Task ARunsOutputIsReadAsItArrivesAndItsEndIsReported()
    {
        var started = await Json<RunStarted>(await _http.PostAsync("/desk-api/operations/core-images/runs", null));
        started.Run.Should().NotBeNull();

        var log = await Finished(started.Run!.Id);

        log.Run.State.Should().Be("succeeded");
        log.Lines.Should().Contain(["drawing the pictures", "a warning on the other stream"]);
        Called().Should().Contain(call => call.SequenceEqual(new[] { "services", "run", "core-images" }));

        var failed = await Json<RunStarted>(await _http.PostAsync("/desk-api/operations/failing/runs", null));
        (await Finished(failed.Run!.Id)).Run.Should().Match<OperationRun>(r => r.State == "failed" && r.ExitCode == 3);

        await Task.Delay(200);
        Logged().Where(e => e.Section == ChangeLog.Apply).Select(e => (e.Action, e.After!.GetValue<string>())).Should()
            .BeEquivalentTo([("images", "succeeded"), ("failing", "failed")]);
    }

    [Fact]
    public async Task APictureChoiceIsWrittenWhereTheLoaderReadsItAndTheLeadMovesToIt()
    {
        var choices = new PictureChoices(DeskPaths.Read(_app!.Services.GetRequiredService<IConfiguration>()),
            new ChangeLog(DeskPaths.Read(_app.Services.GetRequiredService<IConfiguration>())));
        await choices.Choose("jerusalem", new PictureChoiceRequest("openbible/a.jpg", false, true, "", null), "Єрусалим");
        await choices.Choose("jerusalem", new PictureChoiceRequest("openbible/b.jpg", false, true, " Мури зі сходу ", null), "Єрусалим");
        await choices.Choose("jerusalem", new PictureChoiceRequest("openbible/c.jpg", true, false, null, "не те місто"), "Єрусалим");

        var read = ImageChoices.Read(PictureChoicesFile);
        read.Images.Should().BeEquivalentTo(
        [
            new ImageChoice("jerusalem", "openbible/b.jpg", null, true, "Мури зі сходу"),
            new ImageChoice("jerusalem", "openbible/c.jpg", true),
        ], "a.jpg lost the lead to b.jpg and, with nothing else chosen about it, left the file");
        (await choices.Choose("jerusalem", new PictureChoiceRequest("../../secret.png", true, false, "", null))).Should().BeNull();
        Logged().Should().HaveCount(3).And.OnlyContain(e => e.Section == "pictures" && e.Needs == "images" && e.Label == "Єрусалим");
    }

    [Theory]
    [InlineData("generated/a.png", HttpStatusCode.OK)]
    [InlineData("..%2F..%2Fsecret.png", HttpStatusCode.NotFound)]
    [InlineData("generated/missing.png", HttpStatusCode.NotFound)]
    public async Task APictureIsServedFromTheImagesFolderAndFromNowhereAboveIt(string file, HttpStatusCode status) =>
        (await _http.GetAsync($"/desk-api/images/{file}")).StatusCode.Should().Be(status);

    /// <summary>
    /// The record files are tracked, and a decision's diff has to be the decision. Read and written
    /// back unchanged, each file the console writes into must come back byte for byte.
    /// </summary>
    [Theory]
    [InlineData("Resources/Essenthos/review/wikipedia-matches.json")]
    [InlineData("Essenthos.Api/site-settings.json")]
    public void AFileWrittenBackUnchangedIsTheSameFile(string tracked)
    {
        var source = Path.Combine(Checkout(), tracked);
        var copy = Path.Combine(_root, Path.GetFileName(tracked));
        File.Copy(source, copy);

        JsonFiles.Write(copy, JsonFiles.Read(copy));

        File.ReadAllBytes(copy).Should().Equal(File.ReadAllBytes(source));
    }

    [Theory]
    [InlineData("1KI 7:21", "1-kings:7:21")]
    [InlineData("NUM 8:2#2", "numbers:8:2")]
    [InlineData("JDG 6:11", "judges:6:11")]
    [InlineData("LEV 23", null)]
    [InlineData("EXO 25:10-22", null)]
    [InlineData("XYZ 1:1", null)]
    public void AVerseIsAddressedAsTheReadingApiTakesIt(string reference, string? address) =>
        Addresses.Of(reference).Should().Be(address);

    /// <summary>A page reading's progress is read from its pipeline folder; a folder with none, or one caught mid-write, is left out.</summary>
    [Fact]
    public async Task APageReadingShowsHowFarItHasGot()
    {
        var readingsFolder = Path.Combine(Repository, "Resources", "Essenthos", "page-readings");
        Directory.CreateDirectory(Path.Combine(readingsFolder, "pages-ottley"));
        Directory.CreateDirectory(Path.Combine(readingsFolder, "pages-swete"));
        Directory.CreateDirectory(Path.Combine(readingsFolder, "pages-half"));
        File.WriteAllText(Path.Combine(readingsFolder, "pages-ottley", "progress.json"), """
            {"title": "Ottley", "action": "pages-ottley", "state": "limit", "started": "2026-10-10T09:00:00+00:00",
             "updated": "2026-10-10T10:00:00+00:00", "pages": {"total": 102, "done": 40, "failed": 0},
             "items": {"total": 1008, "read": 400, "right": 380, "wrong": 15, "neither": 5},
             "note": "Codex stopped at its usage limit.", "after": null}
            """);
        File.WriteAllText(Path.Combine(readingsFolder, "pages-half", "progress.json"), """{"title": "Ha""");

        var readings = await Json<List<PageReading>>(await _http.GetAsync("/desk-api/texts/pages"));

        readings.Should().ContainSingle();
        readings[0].Should().BeEquivalentTo(new
        {
            Folder = "pages-ottley", Action = "pages-ottley", State = "limit",
            Pages = new PageCount(102, 40, 0), Items = new ItemCount(1008, 400, 380, 15, 5),
        });
    }

    [Fact]
    public void EveryOpenOccurrenceHasAKeyOfItsOwn()
    {
        var entries = JsonFiles.Read(Path.Combine(Checkout(), "Resources", "Essenthos", "review", ThingReview.QuestionsFile))["entries"]!
            .AsArray();

        entries.Select(e => ThingReview.KeyOf(e!)).Should().OnlyHaveUniqueItems().And.HaveCount(entries.Count);
    }

    private string SiteSettingsFile => Path.Combine(Repository, "Essenthos.Api", SiteSettings.FileName);

    private string ChangeLogFile => Path.Combine(Repository, "Resources", "Essenthos", ChangeLog.FileName);

    private string PictureChoicesFile => Path.Combine(Repository, "Resources", "Essenthos", PictureChoices.FileName);

    private IReadOnlyList<ChangeEntry> Logged() =>
        new ChangeLog(DeskPaths.Read(_app!.Services.GetRequiredService<IConfiguration>())).Read().Entries.Reverse().ToList();

    private async Task<RunLog> Finished(string id)
    {
        for (var attempt = 0; attempt < 100; attempt++)
        {
            var log = await Json<RunLog>(await _http.GetAsync($"/desk-api/operations/runs/{id}"));
            if (log.Run.State != "running")
            {
                return log;
            }

            await Task.Delay(100);
        }

        throw new TimeoutException("The run did not finish in ten seconds.");
    }

    private async Task<string> OccurrenceKey(string record) =>
        (await Json<ThingQuestionsResponse>(await _http.GetAsync("/desk-api/review/occurrences")))
        .Entries.First(e => e.RecordSlug == record).Key;

    private Task<HttpResponseMessage> Post(string path, object body) => Send(HttpMethod.Post, path, body);

    private Task<HttpResponseMessage> Put(string path, object body) => Send(HttpMethod.Put, path, body);

    private Task<HttpResponseMessage> Send(HttpMethod method, string path, object body)
    {
        var request = new HttpRequestMessage(method, path) { Content = JsonContent.Create(body) };
        request.Headers.Add("Origin", Origin);
        return _http.SendAsync(request);
    }

    private static async Task<T> Json<T>(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<T>(new JsonSerializerOptions(JsonSerializerDefaults.Web)))!;

    private List<string[]> Called() =>
        File.Exists(Calls)
            ? File.ReadAllLines(Calls).Select(line => JsonSerializer.Deserialize<string[]>(line)!).ToList()
            : [];

    private JsonNode Occurrences() => JsonFiles.Read(Path.Combine(Review, ThingReview.QuestionsFile));

    private void WriteThings()
    {
        File.WriteAllText(Path.Combine(Review, ThingReview.QuestionsFile),
            """
            {
              "about": "Occurrences a reading could not settle.",
              "entries": [
                { "record": "boaz-pillar", "strong": "H1162", "references": ["1KI 7:21", "2CH 3:17"],
                  "question": "The pillar or the man?", "options": ["boaz-pillar", "none"] },
                { "record": "new-moon", "strong": "H2320", "references": ["NUM 28:14"],
                  "question": "The New Moon or a month?", "options": ["new-moon", "a record of its own", "none"] }
              ]
            }
            """);
        File.WriteAllText(Path.Combine(Records, ThingReview.ObjectsFile),
            """
            {
              "decidedBy": "a test",
              "records": [
                { "slug": "boaz-pillar", "kind": "object", "subtype": "pillar", "name": "Boaz", "distinguisher": "the left pillar",
                  "occurrences": [{ "strong": "H1162", "only": ["1KI 7"], "except": ["1KI 7:21"] }] }
              ]
            }
            """);
        File.WriteAllText(Path.Combine(Records, ThingReview.ObservancesFile),
            """
            {
              "decidedBy": "a test",
              "records": [
                { "slug": "new-moon", "kind": "observance", "subtype": "new-moon", "name": "New Moon", "distinguisher": "the first of the month",
                  "occurrences": [{ "strong": "H2320", "only": ["NUM 10:10"] }] }
              ]
            }
            """);
    }

    /// <summary>The checkout this suite was built from, found by walking up from the test assembly.</summary>
    private static string Checkout()
    {
        for (var folder = new DirectoryInfo(AppContext.BaseDirectory); folder is not null; folder = folder.Parent)
        {
            if (File.Exists(Path.Combine(folder.FullName, "Essenthos.Core.sln")))
            {
                return folder.FullName;
            }
        }

        throw new DirectoryNotFoundException("The tests were not built inside an essenthos-core checkout.");
    }

    /// <summary>
    /// avioniq as far as the console uses it: it records every call, lists its actions, and runs two
    /// of them that print on both streams, one of which fails.
    /// </summary>
    private const string FakeAvioniq =
        """
        const fs = require("fs")
        const path = require("path")
        const args = process.argv.slice(2)
        fs.appendFileSync(path.join(__dirname, "calls.log"), JSON.stringify(args) + "\n")
        const joined = args.join(" ")
        if (joined === "--json services list") {
          console.log(JSON.stringify({
            services: [{ name: "core", kind: "service", project: "essenthos-core", description: "The API", state: "running" }],
            actions: [
              { name: "core-images", kind: "action", project: "essenthos-core", description: "Draw the pictures again" },
              { name: "core-clear", kind: "action", project: "essenthos-core", description: "Kill whatever listens on 5279" },
              { name: "failing", kind: "action", project: "essenthos-core", description: "Fails" }
            ]
          }))
        } else if (args[0] === "services" && args[1] === "run") {
          console.log("drawing the pictures")
          console.error("a warning on the other stream")
          process.exit(args[2] === "failing" ? 3 : 0)
        } else {
          console.error("unknown: " + joined)
          process.exit(2)
        }
        """;
}
