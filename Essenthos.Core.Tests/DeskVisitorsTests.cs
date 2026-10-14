using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Essenthos.Core.Desk;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Xunit;

namespace Essenthos.Core.Tests;

/// <summary>
/// The console's reading of the simulated visitors, over HTTP as the owner's browser reaches it:
/// runs newest first with how they ended, a session step by step with its report, the screenshots,
/// and nothing served from outside the visitors' folder however a request names it.
/// </summary>
public sealed class DeskVisitorsTests : IAsyncLifetime
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"desk-visitors-{Guid.NewGuid():n}");

    private WebApplication? _app;

    private HttpClient _http = null!;

    private string Workspace => Path.Combine(_root, "workspace");

    private string VisitorsFolder => Path.Combine(Workspace, ".visitors");

    public async Task InitializeAsync()
    {
        Directory.CreateDirectory(Path.Combine(_root, "repository", "Resources", "Essenthos"));
        Directory.CreateDirectory(Path.Combine(Workspace, ".avioniq"));
        Directory.CreateDirectory(Path.Combine(_root, "resources"));

        _app = DeskApplication.Build(
        [
            "--Urls=http://127.0.0.1:0",
            $"--Desk:Repository={Path.Combine(_root, "repository")}",
            $"--Dataset:ResourcesPath={Path.Combine(_root, "resources")}",
            $"--Desk:Workspace={Workspace}",
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
    public async Task NoVisitorsFolderIsNoRunsRatherThanAFailure()
    {
        var response = await _http.GetAsync("/desk-api/visitors");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await Json<VisitorRunsResponse>(response)).Runs.Should().BeEmpty();
        (await _http.GetAsync("/desk-api/visitors/2026-10-15-pilot")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task RunsComeNewestFirstWithHowTheirSessionsEnded()
    {
        WritePilot();
        Write("2026-10-01-trial/run.json", """{ "id": "2026-10-01-trial", "startedAt": "2026-10-01T08:00:00Z", "note": "trial", "sessions": [] }""");
        Write("2026-10-01-trial/s01-oleh/session.json", Session("s01-oleh", "task", "2026-10-01T08:00:00Z", "2026-10-01T08:05:00Z", "gave-up", false));

        var runs = (await Json<VisitorRunsResponse>(await _http.GetAsync("/desk-api/visitors"))).Runs;

        runs.Select(r => r.Id).Should().Equal("2026-10-15-pilot", "2026-10-01-trial");
        var pilot = runs[0];
        (pilot.Sessions, pilot.Reached, pilot.Note).Should().Be((3, 1, "pilot"));
        pilot.Outcomes.Should().BeEquivalentTo(new Dictionary<string, int> { ["goal"] = 1, ["bored"] = 1, ["running"] = 1 });
        (pilot.Aggregate, runs[1].Aggregate).Should().Be((true, false));
    }

    [Fact]
    public async Task ARunListsItsSessionsWithPersonaGoalAndOutcome()
    {
        WritePilot();

        var run = await Json<VisitorRunResponse>(await _http.GetAsync("/desk-api/visitors/2026-10-15-pilot"));

        run.Sessions.Select(s => s.Id).Should().Equal("s01-hanna", "s02-ivan", "s03-maria");
        var hanna = run.Sessions[0];
        (hanna.Model, hanna.Persona!.Name, hanna.Goal!.Id, hanna.Goal.Kind).Should().Be(("sonnet", "Ганна", "psalm-90-funeral", "task"));
        hanna.Goal.Queries.Should().Equal("псалом 90 українською");
        (hanna.Screen, hanna.Minutes, hanna.MinutesUsed, hanna.Steps).Should().Be((new VisitorScreen(1366, 768), 10, 2.5, 2));
        (hanna.EndedBy, hanna.Reached).Should().Be(("goal", true));
        (hanna.Viewport, hanna.Persona.Language).Should().Be((new VisitorScreen(1350, 673), "uk"));
        hanna.Usage.Should().Be(new VisitorUsage(182_400, 41, 312_000));
        hanna.Mood.Should().BeEquivalentTo(new VisitorMood("frustrated", new VisitorFeelingAt(2, new VisitorFeeling("frustrated", 3, null))),
            "each feeling was given once, so the stronger one leads, and the worst moment is the frustration at the end");

        var ivan = run.Sessions[1];
        (ivan.Goal!.Kind, ivan.EndedBy, ivan.Reached).Should().Be(("explore", "bored", (bool?)null), "an exploration has no goal to reach");
        (ivan.MinutesUsed, ivan.Steps).Should().Be((4.0, 0), "with no steps the time comes from the start and the end");
        (ivan.Usage, ivan.Mood).Should().Be(((VisitorUsage?)null, (VisitorMood?)null), "nothing was recorded of either");

        var maria = run.Sessions[2];
        (maria.EndedBy, maria.Reached, maria.EndedAt).Should().Be(((string?)null, (bool?)null, (string?)null), "she is still going");
    }

    [Fact]
    public async Task ASessionComesWithItsDossierEveryStepInOrderAndItsReport()
    {
        WritePilot();

        var read = await Json<VisitorSessionResponse>(await _http.GetAsync("/desk-api/visitors/2026-10-15-pilot/s01-hanna"));

        read.Dossier.Should().Be("# Ганна\nЯ шукаю псалом.\n");
        read.Steps.Select(s => (s.N, s.Action)).Should().Equal((0, "start"), (1, "click"), (2, "end"));
        var click = read.Steps[1];
        (click.Target, click.Thought, click.Screenshot, click.Blocked)
            .Should().Be((new VisitorTarget("12", "Псалми", "link"), "Псалми мені й треба", "001.png", (string?)null));
        read.Steps[2].Screenshot.Should().BeNull("no picture was taken at the end");
        read.Steps[2].Blocked.Should().Be("POST · /v1/suggestions", "a refusal written as an object is said in words");
        click.Feeling.Should().Be(new VisitorFeeling("interest", 2, "о, псалми"));
        click.Click.Should().Be(new VisitorClick(210.5, 96, 210.5, 496, new VisitorBox(180, 84, 61, 24)));
        click.Scroll.Should().Be(new VisitorScroll(400, 0, 2400, 673));
        (click.From, click.LoadMs).Should().Be(("http://localhost:5288/uk", 812));
        read.Steps[2].Feeling.Should().Be(new VisitorFeeling("frustrated", 3, null), "a feeling left as kind:strength is read too");
        read.Steps[0].Should().Match<VisitorStep>(s => s.Feeling == null && s.Click == null && s.Scroll == null && s.From == null && s.LoadMs == null,
            "a step written before these were recorded has none of them");
        read.Session.Mood!.Low!.Step.Should().Be(2);

        var report = read.Report!;
        (report.Reached, report.Summary, report.WouldReturn).Should().Be((true, "Знайшла", "Так"));
        report.Confusing.Should().Equal(new VisitorConfusion("Що таке «свідок»?", "http://localhost:5288/uk/psalms/90", 1, "свідок"));
        report.Missing.Should().Equal("великий шрифт");

        var explored = await Json<VisitorSessionResponse>(await _http.GetAsync("/desk-api/visitors/2026-10-15-pilot/s02-ivan"));
        explored.Report!.Interesting.Should().Equal("карти", "родоводи");
        (explored.Report.LostInterest, explored.Report.WouldComeBackFor).Should().Be(("на третій сторінці таблиць", "нові карти"));
        explored.Dossier.Should().BeNull();
        explored.Steps.Should().BeEmpty();

        (await Json<VisitorSessionResponse>(await _http.GetAsync("/desk-api/visitors/2026-10-15-pilot/s03-maria"))).Report
            .Should().BeNull("she has not written one yet");
    }

    [Fact]
    public async Task AStepCaughtMidWriteIsLeftOutUntilItIsWhole()
    {
        WritePilot();
        Write("2026-10-15-pilot/s03-maria/steps/001.json", """{ "n": 1, "action": "cli""");

        var read = await Json<VisitorSessionResponse>(await _http.GetAsync("/desk-api/visitors/2026-10-15-pilot/s03-maria"));

        read.Steps.Select(s => s.N).Should().Equal(0);
    }

    [Fact]
    public async Task ARunsAggregateIsServedAsWrittenAndIsNotThereUntilItIsWritten()
    {
        WritePilot();
        Write("2026-10-01-trial/s01-oleh/session.json", Session("s01-oleh", "task", "2026-10-01T08:00:00Z", null, null, null));

        var aggregate = await Json<VisitorAggregateResponse>(await _http.GetAsync("/desk-api/visitors/2026-10-15-pilot/aggregate"));

        aggregate.Should().Be(new VisitorAggregateResponse("2026-10-15-pilot", "# Pilot\n\nTwo of three reached it.\n"));
        (await _http.GetAsync("/desk-api/visitors/2026-10-01-trial/aggregate")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await _http.GetAsync("/desk-api/visitors/no-such-run/aggregate")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await _http.GetAsync("/desk-api/visitors/..%2F/aggregate")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public void TheWorstMomentIsTheMostNegativeFeelingByItsStrengthAndTheEarliestOfEqualOnes()
    {
        static VisitorStep Felt(int n, string kind, int? strength) =>
            new(n, null, null, "click", null, null, null, null, null, null, null, new VisitorFeeling(kind, strength, null), null, null, null, null);

        var mood = Visitors.Mood([Felt(1, "confused", 3), Felt(2, "annoyed", 2), Felt(3, "frustrated", 1), Felt(4, "annoyed", 2), Felt(5, "delight", 3)]);

        mood!.Low!.Step.Should().Be(2, "annoyed at 2 is worse than confused at 3 or frustrated at 1, and the earlier of two equal ones counts");
        mood.Dominant.Should().Be("annoyed");
        Visitors.Mood([Felt(1, "delight", 1), Felt(2, "interest", null)])!.Low.Should().BeNull("nothing went badly");
        Visitors.Mood([]).Should().BeNull();
    }

    [Theory]
    [InlineData("2026-10-15-pilot/s01-hanna/steps/001.png", HttpStatusCode.OK)]
    [InlineData("2026-10-15-pilot/s01-hanna/steps/009.png", HttpStatusCode.NotFound)]
    [InlineData("2026-10-15-pilot/s01-hanna/steps/001.json", HttpStatusCode.NotFound)]
    [InlineData("2026-10-15-pilot/s01-hanna/steps/..%2F..%2F..%2Fsecret.png", HttpStatusCode.NotFound)]
    [InlineData("2026-10-15-pilot/..%2F..%2F/steps/secret.png", HttpStatusCode.NotFound)]
    [InlineData("..%2F../s01-hanna/steps/001.png", HttpStatusCode.NotFound)]
    public async Task AScreenshotIsServedFromItsSessionAndFromNowhereElse(string path, HttpStatusCode status)
    {
        WritePilot();
        File.WriteAllText(Path.Combine(Workspace, "secret.png"), "outside the visitors' folder");

        var response = await _http.GetAsync($"/desk-api/visitors/{path}");

        response.StatusCode.Should().Be(status);
        if (status == HttpStatusCode.OK)
        {
            response.Content.Headers.ContentType!.MediaType.Should().Be("image/png");
        }
    }

    [Theory]
    [InlineData("..")]
    [InlineData("a..b")]
    [InlineData("a/b")]
    [InlineData("a\\b")]
    [InlineData("")]
    [InlineData("C:")]
    public void ANameThatCouldLeaveTheFolderIsNotASegment(string segment) =>
        Visitors.IsSegment(segment).Should().BeFalse();

    private void WritePilot()
    {
        const string run = "2026-10-15-pilot";
        Write($"{run}/run.json", """{ "id": "2026-10-15-pilot", "startedAt": "2026-10-15T09:00:00Z", "note": "pilot", "sessions": ["s01-hanna", "s02-ivan", "s03-maria"] }""");

        Write($"{run}/s01-hanna/session.json", Session("s01-hanna", "task", "2026-10-15T09:00:00Z", "2026-10-15T09:03:00Z", "goal", true, steps: 2, usage: true));
        Write($"{run}/aggregate.md", "# Pilot\n\nTwo of three reached it.\n");
        Write($"{run}/s01-hanna/dossier.md", "# Ганна\nЯ шукаю псалом.\n");
        Write($"{run}/s01-hanna/steps/000.json",
            """{ "n": 0, "at": "2026-10-15T09:00:00Z", "elapsedSeconds": 0, "action": "start", "target": null, "value": null, "thought": null, "url": "http://localhost:5288/uk", "title": "Essenthos", "screenshot": "000.png", "blocked": null }""");
        Write($"{run}/s01-hanna/steps/001.json",
            """
            { "n": 1, "at": "2026-10-15T09:01:00Z", "elapsedSeconds": 60, "action": "click", "target": { "ref": 12, "text": "Псалми", "role": "link" }, "value": null, "thought": "Псалми мені й треба", "url": "http://localhost:5288/uk/psalms", "title": "Псалми", "screenshot": "001.png", "blocked": null,
            "feeling": { "kind": "Interest", "strength": 2, "words": "о, псалми" },
            "click": { "x": 210.5, "y": 96, "pageX": 210.5, "pageY": 496, "box": { "x": 180, "y": 84, "width": 61, "height": 24 } },
            "scroll": { "before": 400, "after": 0, "pageHeight": 2400, "viewportHeight": 673 },
            "timing": { "loadMs": 812 }, "from": "http://localhost:5288/uk" }
            """);
        Write($"{run}/s01-hanna/steps/002.json",
            """
            { "n": 2, "at": "2026-10-15T09:02:30Z", "elapsedSeconds": 150, "action": "end", "target": null, "value": null, "thought": "Знайшла", "url": "http://localhost:5288/uk/psalms/90", "title": "Псалом 90", "screenshot": "002.png", "blocked": { "method": "POST", "url": "/v1/suggestions" },
            "feeling": "frustrated:3" }
            """);
        Write($"{run}/s01-hanna/steps/000.png", "png");
        Write($"{run}/s01-hanna/steps/001.png", "png");
        Write($"{run}/s01-hanna/report.json",
            """
            { "reached": true, "summary": "Знайшла", "path": "головна, псалми, 90",
              "understood": ["де псалми"], "confusing": [{ "what": "Що таке «свідок»?", "url": "http://localhost:5288/uk/psalms/90", "step": 1, "quote": "свідок" }],
              "missing": ["великий шрифт"], "wishes": [], "wouldReturn": "Так", "overall": "добре" }
            """);

        Write($"{run}/s02-ivan/session.json", Session("s02-ivan", "explore", "2026-10-15T09:00:00Z", "2026-10-15T09:04:00Z", "bored", null));
        Write($"{run}/s02-ivan/report.json",
            """{ "reached": null, "summary": "Подивився", "interesting": ["карти", "родоводи"], "lostInterest": "на третій сторінці таблиць", "wouldComeBackFor": "нові карти" }""");

        Write($"{run}/s03-maria/session.json", Session("s03-maria", "task", "2026-10-15T09:10:00Z", null, null, null));
        Write($"{run}/s03-maria/steps/000.json", """{ "n": 0, "action": "start", "url": "http://localhost:5288/uk" }""");
    }

    private static string Session(
        string id, string kind, string startedAt, string? endedAt, string? endedBy, bool? reached, int? steps = null, bool usage = false) =>
        JsonSerializer.Serialize(new
        {
            id,
            run = "2026-10-15-pilot",
            model = "sonnet",
            persona = new { id = id[4..], name = id == "s01-hanna" ? "Ганна" : id[4..], summary = "одна", language = "uk" },
            goal = new { id = id == "s01-hanna" ? "psalm-90-funeral" : "browse", kind, text = "знайти", landing = "http://localhost:5288/uk", queries = new[] { "псалом 90 українською" } },
            screen = new { width = 1366, height = 768 },
            language = "uk",
            minutes = 10,
            startedAt,
            endedAt,
            outcome = new { endedBy, reached, steps },
            viewport = new { width = 1350, height = 673 },
            usage = usage ? new { tokens = 182_400, toolUses = 41, durationMs = 312_000 } : null,
        });

    private void Write(string relative, string content)
    {
        var path = Path.Combine(VisitorsFolder, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    private static async Task<T> Json<T>(HttpResponseMessage response)
    {
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<T>(new JsonSerializerOptions(JsonSerializerDefaults.Web)))!;
    }
}
