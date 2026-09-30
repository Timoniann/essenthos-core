using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Essenthos.Core.Configuration;
using Essenthos.Core.Desk;
using Essenthos.Core.Loading.Encyclopedia;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Essenthos.Core.Tests;

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
    public async Task AnsweringAnOccurrenceWithARecordWritesAReviewedRuleTheLoaderReads()
    {
        WriteThings();
        var key = await OccurrenceKey("boaz-pillar");

        var response = await Put($"/desk-api/review/occurrences/{key}", new { answer = "boaz-pillar", note = "" });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var boaz = Record("boaz-pillar");
        boaz.Occurrences.Should().HaveCount(2);
        var rule = boaz.Occurrences![1];
        (rule.Strong, rule.Reviewed).Should().Be(("H1162", ThingReview.AnswerStamp(JsonFiles.Today())));
        rule.Only.Should().Equal("1KI 7:21", "2CH 3:17");
        rule.Admits(11, 7, 21, 1).Should().BeTrue();
        Occurrences()["entries"]![0]!["decision"]!["applied"]!.GetValue<string>().Should().Be(ThingReview.Written);
        var logged = Logged().Should().ContainSingle().Which;
        (logged.Section, logged.Target, logged.Needs).Should().Be(
            ("occurrences", "objects-and-observances boaz-pillar H1162 1KI 7:21, 2CH 3:17", "load"));
    }

    [Fact]
    public async Task ChangingAnAnswerUndoesWhatTheOldOneWrote()
    {
        WriteThings();
        var key = await OccurrenceKey("boaz-pillar");
        await Put($"/desk-api/review/occurrences/{key}", new { answer = "boaz-pillar", note = "" });

        await Put($"/desk-api/review/occurrences/{key}", new { answer = "none", note = "the man after all" });

        var rule = Record("boaz-pillar").Occurrences.Should().ContainSingle().Which;
        (rule.Strong, rule.Reviewed).Should().Be(("H1162", null));
        rule.Only.Should().Equal("1KI 7");
        rule.Except.Should().Equal("1KI 7:21");
        var decision = Occurrences()["entries"]![0]!["decision"]!;
        (decision["answer"]!.GetValue<string>(), decision["applied"]!.GetValue<string>(), decision["note"]!.GetValue<string>())
            .Should().Be(("none", ThingReview.Nothing, "the man after all"));
    }

    [Fact]
    public async Task AnAnswerAskingForANewRecordWaitsForSomebodyToWriteIt()
    {
        WriteThings();
        var key = await OccurrenceKey("new-moon");

        await Put($"/desk-api/review/occurrences/{key}", new { answer = "a record of its own", note = "" });

        Occurrences()["entries"]![1]!["decision"]!["applied"]!.GetValue<string>().Should().Be(ThingReview.ByHand);
        Record("new-moon").Occurrences.Should().ContainSingle("nothing is written for an answer that needs a new record");
        Logged().Single().Needs.Should().Be("agent");
    }

    /// <summary>
    /// A list written later in the same shape — the Septuagint's placements — is answered on the same
    /// page, by the key its entries carry, and an answer that changes something waits for an agent.
    /// </summary>
    [Fact]
    public async Task AnyListOfTheSameShapeIsAnsweredAndItsOwnKeysAreKept()
    {
        WriteThings();
        File.WriteAllText(Path.Combine(Review, "septuagint-placement.json"),
            """
            {
              "about": "Where the Greek stands beside Hebrew it does not carry.",
              "entries": [
                { "key": "small-swaps", "edition": "both", "references": ["GEN 31:46-52"], "question": "Place them by their words?",
                  "options": ["place them by their words", "show them as printed"] }
              ]
            }
            """);
        File.WriteAllText(Path.Combine(Review, "evidentia-gold-errors.json"), """{ "errors": [] }""");
        File.WriteAllText(Path.Combine(Review, "bibledata-removed.json"), """[{ "id": "1", "why": "a record of a past decision" }]""");

        var read = await Json<ThingQuestionsResponse>(await _http.GetAsync("/desk-api/review/occurrences"));

        read.Lists.Select(l => (l.List, l.Open)).Should().Equal(("objects-and-observances", 2), ("septuagint-placement", 1));
        var entry = read.Entries.Single(e => e.List == "septuagint-placement");
        entry.Key.Should().Be("small-swaps");
        entry.Options.Select(o => o.Effect).Should().Equal(ThingReview.ByHand, ThingReview.Nothing);

        (await Put("/desk-api/review/occurrences/small-swaps", new { answer = "place them by their words", note = "" }))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        JsonFiles.Read(Path.Combine(Review, "septuagint-placement.json"))["entries"]![0]!["decision"]!["answer"]!
            .GetValue<string>().Should().Be("place them by their words");
        Logged().Single().Should().Match<ChangeEntry>(e => e.Needs == "agent" && e.Target == "septuagint-placement small-swaps GEN 31:46-52");
    }

    /// <summary>
    /// The verses the load could not settle between a man and the people named after him are listed
    /// beside the objects' occurrences and answered on the same page; the answer is kept in their own
    /// list, which is where the load reads it back.
    /// </summary>
    [Fact]
    public async Task AnAnswerAboutAPeoplesAncestorIsKeptInTheListTheLoadReads()
    {
        WriteThings();
        MisfiledVerseLoader.ReviewFile.Should().Be(ThingReview.AncestorsFile);
        File.WriteAllText(Path.Combine(Review, ThingReview.AncestorsFile),
            """
            {
              "about": "Verses under a people's ancestor.",
              "entries": [
                { "record": "jacob", "label": "Israel", "strong": "H3478", "references": ["EXO 4:22"],
                  "question": "The man or the Israelites?", "options": ["israelites", "leave as it is"] }
              ]
            }
            """);
        var objects = File.ReadAllText(Path.Combine(Review, ThingReview.QuestionsFile));

        var key = await OccurrenceKey("jacob");
        (await Put($"/desk-api/review/occurrences/{key}", new { answer = "israelites", note = "" }))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        var decision = JsonFiles.Read(Path.Combine(Review, ThingReview.AncestorsFile))["entries"]![0]!["decision"]!;
        decision["answer"]!.GetValue<string>().Should().Be("israelites");
        decision["applied"]!.GetValue<string>().Should().Be(ThingReview.OnLoad, "the load reads the answer back, whichever it is");
        File.ReadAllText(Path.Combine(Review, ThingReview.QuestionsFile)).Should().Be(objects);
        MisfiledVerseLoader.Answers(Path.Combine(Review, ThingReview.AncestorsFile))
            .Should().Contain(new KeyValuePair<(string, string, string), string>(("jacob", "Israel", "EXO 4:22"), "israelites"));
    }

    [Fact]
    public async Task AnAnswerThatIsNotAnOptionIsRefused()
    {
        WriteThings();
        var key = await OccurrenceKey("boaz-pillar");

        (await Put($"/desk-api/review/occurrences/{key}", new { answer = "jachin-pillar", note = "" }))
            .StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task ReviewingARecordStampsItAndItsRulesAndTakingItBackRemovesOnlyThatStamp()
    {
        WriteThings();
        var key = await OccurrenceKey("boaz-pillar");
        await Put($"/desk-api/review/occurrences/{key}", new { answer = "boaz-pillar", note = "" });

        (await Put("/desk-api/review/records/boaz-pillar", new { scope = "all" })).StatusCode.Should().Be(HttpStatusCode.OK);
        var reviewed = Record("boaz-pillar");
        reviewed.Reviewed.Should().Be(ThingReview.RecordStamp(JsonFiles.Today()));
        reviewed.Occurrences!.Should().OnlyContain(rule => rule.Reviewed != null);

        await Put("/desk-api/review/records/boaz-pillar", new { scope = (string?)null });
        var undone = Record("boaz-pillar");
        undone.Reviewed.Should().BeNull();
        undone.Occurrences![0].Reviewed.Should().BeNull();
        undone.Occurrences[1].Reviewed.Should().Be(ThingReview.AnswerStamp(JsonFiles.Today()), "the answer's own rule stays the owner's");

        Logged().Where(e => e.Section == "records").Select(e => (e.Target, e.After?.GetValue<string>(), e.Needs)).Should().Equal(
            ("object/boaz-pillar", "all", "load"), ("object/boaz-pillar", null, "load"));
    }

    [Fact]
    public async Task TheRecordsSayHowFarEachHasBeenReviewed()
    {
        WriteThings();
        await Put("/desk-api/review/records/new-moon", new { scope = "record" });

        var records = await Json<ThingRecordsResponse>(await _http.GetAsync("/desk-api/review/records"));

        records.Records.Single(r => r.Record["slug"]!.GetValue<string>() == "new-moon").Scope.Should().Be(ThingReview.RecordOnly);
        records.Records.Single(r => r.Record["slug"]!.GetValue<string>() == "boaz-pillar").Scope.Should().BeNull();
    }

    /// <summary>
    /// The narrative records are listed under their own file, and one of them, a person with no
    /// subtype, can be reviewed like any other; the log names it as a narrative record.
    /// </summary>
    [Fact]
    public async Task TheNarrativeRecordsAreListedAndReviewedUnderTheirOwnFile()
    {
        WriteThings();
        File.WriteAllText(Path.Combine(Records, ThingReview.NarrativesFile),
            """
            {
              "decidedBy": "a test",
              "records": [
                { "slug": "serpent", "kind": "person", "name": "The serpent", "distinguisher": "the serpent in the garden",
                  "occurrences": [{ "strong": "H5175", "only": ["GEN 3:1"] }] }
              ]
            }
            """);

        await Put("/desk-api/review/records/serpent", new { scope = "all" });
        var records = await Json<ThingRecordsResponse>(await _http.GetAsync("/desk-api/review/records"));

        var serpent = records.Records.Single(r => r.Record["slug"]!.GetValue<string>() == "serpent");
        serpent.File.Should().Be("narratives");
        serpent.Scope.Should().Be(ThingReview.All);
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
    [InlineData("Essenthos.Forge/Loading/Encyclopedia/ObjectRecords.json")]
    [InlineData("Essenthos.Forge/Loading/Encyclopedia/ObservanceRecords.json")]
    [InlineData("Essenthos.Forge/Loading/Encyclopedia/NarrativeRecords.json")]
    [InlineData("Resources/Essenthos/review/objects-and-observances.json")]
    [InlineData("Resources/Essenthos/review/eponym-verses.json")]
    [InlineData("Resources/Essenthos/review/septuagint-placement.json")]
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

    private ThingRecord Record(string slug)
    {
        var shape = new JsonSerializerOptions { PropertyNameCaseInsensitive = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        return new[] { ThingReview.ObjectsFile, ThingReview.ObservancesFile }
            .SelectMany(file => JsonSerializer.Deserialize<ThingFile>(File.ReadAllText(Path.Combine(Records, file)), shape)!.Records)
            .Single(r => r.Slug == slug);
    }

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
