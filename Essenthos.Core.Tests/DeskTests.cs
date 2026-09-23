using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Essenthos.Core.Desk;
using Essenthos.Core.Loading.Encyclopedia;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Xunit;

namespace Essenthos.Core.Tests;

/// <summary>
/// The owner's console, over HTTP as his browser reaches it: that it answers nobody else, that an
/// answer reaches avioniq as his, and that a decision lands in the file the loader or the decide
/// command reads — in the shape they read, and taken back exactly when he takes it back. The
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
        using var request = new HttpRequestMessage(HttpMethod.Get, "/desk-api/questions");
        request.Headers.Host = "attacker.example";

        (await _http.SendAsync(request)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Theory]
    [InlineData("Origin", "http://attacker.example")]
    [InlineData("Sec-Fetch-Site", "cross-site")]
    public async Task AnotherSitesPageCannotActInTheOwnersName(string header, string value)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/desk-api/questions/answer")
        {
            Content = JsonContent.Create(new { entity = "FTR-0001", number = 1, answer = "Yes" }),
        };
        request.Headers.Add(header, value);

        (await _http.SendAsync(request)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        Called().Should().NotContain(call => call.Contains("answer"));
    }

    [Fact]
    public void TheConsoleRefusesToListenBeyondTheLoopback()
    {
        var build = () => DeskApplication.Build(["--Urls=http://0.0.0.0:5281"], contentRoot: _root);

        build.Should().Throw<InvalidOperationException>().WithMessage("*not this machine's loopback*");
    }

    [Fact]
    public async Task TheOpenQuestionsAndTheWorkAwaitingSignOffAreListed()
    {
        var board = await Json<QuestionsResponse>(await _http.GetAsync("/desk-api/questions"));

        board.Available.Should().BeTrue();
        board.Questions.Should().ContainSingle();
        var question = board.Questions[0];
        (question.Entity, question.Number, question.Blocking).Should().Be(("FTR-0001", 2, true));
        question.Options.Should().Equal("Keep it", "Drop it");
        board.SignOffs.Should().ContainSingle().Which.Title.Should().Be("Kings are marked");
    }

    [Fact]
    public async Task AnAnswerReachesAvioniqAsTheOwnersOwn()
    {
        var response = await Post("/desk-api/questions/answer", new { entity = "FTR-0001", number = 2, answer = "Keep it & say \"why\"" });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        Called().Should().Contain(call => call.SequenceEqual(new[] { "answer", "FTR-0001", "2", "Keep it & say \"why\"", "--by", "user" }));
    }

    [Fact]
    public async Task WhatAvioniqRefusesIsSaidInItsOwnWords()
    {
        var response = await Post("/desk-api/questions/answer", new { entity = "PRB-0404", number = 1, answer = "Yes" });

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await Json<AnswerResponse>(response)).Problem.Should().Contain("No open question 1 on PRB-0404");
    }

    [Fact]
    public async Task ARelationshipDecisionIsStoredInTheShapeTheDecideCommandReads()
    {
        WriteRelationships();

        var response = await Put("/desk-api/review/relationships/72185-72186", new { decision = "remove", note = "Абієзер — рід" });
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var decision = Relationships()["decisions"]!["72185-72186"]!;
        decision["decision"]!.GetValue<string>().Should().Be("remove");
        decision["note"]!.GetValue<string>().Should().Be("Абієзер — рід");
        decision["rows"]!.GetValue<string>().Should().Be("72185|72186");
        (decision["a"]!.GetValue<string>(), decision["relation"]!.GetValue<string>(), decision["b"]!.GetValue<string>())
            .Should().Be(("joash", "descendant-of", "iezer"));
        (decision["subset"]!.GetValue<string>(), decision["why"]!.GetValue<string>()).Should().Be(("descent", "not-stated"));
        decision["decidedAt"]!.GetValue<string>().Should().EndWith("Z");
        Relationships()["decisions"]!["70845"]!["decision"]!.GetValue<string>().Should().Be("confirm", "an earlier decision is kept");
        File.ReadAllText(Path.Combine(Review, RelationshipReview.FileName)).Should().Contain("Абієзер", "Cyrillic is written as it is typed");
    }

    [Fact]
    public async Task TakingARelationshipDecisionBackRemovesIt()
    {
        WriteRelationships();
        await Put("/desk-api/review/relationships/72185-72186", new { decision = "confirm", note = "" });

        await Put("/desk-api/review/relationships/72185-72186", new { decision = (string?)null, note = "" });

        Relationships()["decisions"]!.AsObject().ContainsKey("72185-72186").Should().BeFalse();
    }

    [Fact]
    public async Task ABulkDecisionLeavesTheDecidedFactsAsTheyWere()
    {
        WriteRelationships();
        await Put("/desk-api/review/relationships/72531-72532", new { decision = "confirm", note = "" });

        var response = await Post("/desk-api/review/relationships/bulk",
            new { keys = new[] { "72185-72186", "72531-72532", "99999-1" }, decision = "reask" });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var decisions = Relationships()["decisions"]!;
        decisions["72185-72186"]!["decision"]!.GetValue<string>().Should().Be("reask");
        decisions["72531-72532"]!["decision"]!.GetValue<string>().Should().Be("confirm");
        decisions.AsObject().ContainsKey("99999-1").Should().BeFalse("only listed facts are decided");
    }

    [Fact]
    public async Task ADecisionOutsideTheFourIsRefused()
    {
        WriteRelationships();

        (await Put("/desk-api/review/relationships/72185-72186", new { decision = "maybe", note = "" }))
            .StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
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

    [Fact]
    public async Task OnlyTheListedOperationsRunAndNeverOneThatStopsTheCore()
    {
        var operations = await Json<OperationsResponse>(await _http.GetAsync("/desk-api/operations"));

        operations.Operations.Select(o => o.Name).Should().Equal("core-images", "failing", "core-unregistered");
        operations.Operations.Single(o => o.Name == "core-unregistered").Registered.Should().BeFalse();
        operations.Services.Should().ContainSingle().Which.Runnable.Should().BeFalse();

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
    [InlineData("Resources/Essenthos/review/objects-and-observances.json")]
    [InlineData("Resources/Essenthos/review/bibledata-relationships.json")]
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
    public async Task TheRelationshipListCarriesEachVersesAddress()
    {
        File.WriteAllText(Path.Combine(Review, RelationshipReview.FileName),
            """{ "facts": [{ "id": "1|2", "verses": [{ "ref": "JDG 6:11", "text": "And there came an angel" }] }], "decisions": {} }""");

        var list = JsonNode.Parse(await _http.GetStringAsync("/desk-api/review/relationships"))!;

        list["facts"]![0]!["verses"]![0]!["address"]!.GetValue<string>().Should().Be("judges:6:11");
        File.ReadAllText(Path.Combine(Review, RelationshipReview.FileName)).Should().NotContain("address", "the address is shown, not stored");
    }

    [Fact]
    public void EveryOpenOccurrenceHasAKeyOfItsOwn()
    {
        var entries = JsonFiles.Read(Path.Combine(Checkout(), "Resources", "Essenthos", "review", ThingReview.QuestionsFile))["entries"]!
            .AsArray();

        entries.Select(e => ThingReview.KeyOf(e!)).Should().OnlyHaveUniqueItems().And.HaveCount(entries.Count);
    }

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

    private JsonNode Relationships() => JsonFiles.Read(Path.Combine(Review, RelationshipReview.FileName));

    private JsonNode Occurrences() => JsonFiles.Read(Path.Combine(Review, ThingReview.QuestionsFile));

    private ThingRecord Record(string slug)
    {
        var shape = new JsonSerializerOptions { PropertyNameCaseInsensitive = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        return new[] { ThingReview.ObjectsFile, ThingReview.ObservancesFile }
            .SelectMany(file => JsonSerializer.Deserialize<ThingFile>(File.ReadAllText(Path.Combine(Records, file)), shape)!.Records)
            .Single(r => r.Slug == slug);
    }

    private void WriteRelationships() =>
        File.WriteAllText(Path.Combine(Review, RelationshipReview.FileName),
            """
            {
              "about": "BibleData's facts the text did not confirm.",
              "facts": [
                { "id": "72185|72186", "subset": "descent", "why": "not-stated", "relation": "descendant-of",
                  "a": { "slug": "joash", "name": "Joash" }, "b": { "slug": "iezer", "name": "Iezer" },
                  "says": [{ "from": "Iezer", "type": "ancestor", "to": "Joash" }], "verses": [] },
                { "id": "72531|72532", "subset": "explicit", "why": "not-stated", "relation": null,
                  "a": { "slug": "ishbibenob", "name": "Ishbi-benob" }, "b": { "slug": "raphah", "name": "Raphah" },
                  "says": [{ "from": "Ishbi-benob", "type": "son-of", "to": "Raphah" }], "verses": [] }
              ],
              "decisions": {
                "70845": { "a": "yhvh", "b": "adam", "decision": "confirm", "note": "", "rows": "70845" }
              }
            }
            """);

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
    /// avioniq as far as the console uses it: it records every call, answers the board's queries
    /// with a question and a sign-off, refuses one answer, and runs two actions that print on both
    /// streams, one of which fails.
    /// </summary>
    private const string FakeAvioniq =
        """
        const fs = require("fs")
        const path = require("path")
        const args = process.argv.slice(2)
        fs.appendFileSync(path.join(__dirname, "calls.log"), JSON.stringify(args) + "\n")
        const joined = args.join(" ")
        if (joined === "--json questions") {
          console.log(JSON.stringify([{ id: "FTR-0001", title: "Kings", questions: [
            { n: 1, text: "Answered already", options: [], blocking: true, answer: "Yes" },
            { n: 2, asked: "2026-09-20T10:00:00Z", by: "agent", text: "Keep or drop?", options: ["Keep it", "Drop it"], blocking: true }
          ] }]))
        } else if (joined === "--json waiting") {
          console.log(JSON.stringify([
            { id: "FTR-0641", kind: "sign-off", title: "Kings are marked", text: "Finished", since: "2026-09-22T11:03:36Z" },
            { id: "FTR-0001", kind: "question", title: "Kings", text: "Keep or drop?" }
          ]))
        } else if (joined === "--json services list") {
          console.log(JSON.stringify({
            services: [{ name: "core", kind: "service", project: "essenthos-core", description: "The API", state: "running" }],
            actions: [
              { name: "core-images", kind: "action", project: "essenthos-core", description: "Draw the pictures again" },
              { name: "core-clear", kind: "action", project: "essenthos-core", description: "Kill whatever listens on 5279" },
              { name: "failing", kind: "action", project: "essenthos-core", description: "Fails" }
            ]
          }))
        } else if (args[0] === "answer" && args[1] === "PRB-0404") {
          console.error("No open question 1 on PRB-0404.")
          process.exit(1)
        } else if (args[0] === "answer") {
          console.log("answered")
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
