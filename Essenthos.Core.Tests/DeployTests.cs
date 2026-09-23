using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Essenthos.Core.Desk;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Essenthos.Core.Tests;

/// <summary>
/// The console's deployment section, against two real git checkouts in a temporary folder, a stand-in
/// server that answers as a deployed API does, and stand-ins for pwsh and dotnet that record what they
/// were asked. Nothing here reaches a server: the addresses are documentation ones or this machine.
/// </summary>
public sealed class DeployTests : IAsyncLifetime
{
    private const string Origin = "http://localhost:5280";

    private const string Release = "20260921a";

    private readonly string _root = Path.Combine(Path.GetTempPath(), $"deploy-{Guid.NewGuid():n}");

    private WebApplication? _server;

    private WebApplication? _app;

    private HttpClient _http = null!;

    private string _deployed = null!;

    private string _coreHead = null!;

    private string _webHead = null!;

    private string Repository => Path.Combine(_root, "core");

    private string WebRepository => Path.Combine(_root, "web");

    private string Releases => Path.Combine(_root, "releases");

    private string Calls => Path.Combine(_root, "calls.log");

    public async Task InitializeAsync()
    {
        Directory.CreateDirectory(Path.Combine(_root, "workspace", ".avioniq"));
        Directory.CreateDirectory(Path.Combine(_root, "resources"));
        File.WriteAllText(Path.Combine(_root, "fake-program.js"), FakeProgram);

        _deployed = Commit(Repository, "The first");
        _coreHead = Commit(Repository, "The second");
        Git(Repository, "update-ref", "refs/remotes/origin/main", _coreHead);
        _webHead = Commit(WebRepository, "The reader");
        Git(WebRepository, "update-ref", "refs/remotes/origin/main", _webHead);

        Directory.CreateDirectory(Releases);
        File.WriteAllText(Path.Combine(Releases, $"{Release}.json"),
            $$"""{ "Name": "{{Release}}", "BuiltAt": "2026-09-21T08:00:00+00:00", "ForgeVersion": "1.0.0+abc", "Bytes": 376650794 }""");

        // A deployed API and reader, as far as the console asks them anything.
        _server = WebApplication.CreateSlimBuilder(["--urls=http://127.0.0.1:0"]).Build();
        _server.MapGet("/v1/health/ready", () => Results.Text("""{ "status": "ready" }""", "application/json"));
        _server.MapGet("/v1/health/version", () => Results.Text(
            $$"""{ "commit": "{{_deployed[..12]}}", "builtAt": "2026-09-20T09:00:00Z", "release": { "name": "20260918a", "builtAt": "2026-09-18T07:00:00Z" } }""",
            "application/json"));
        _server.MapGet("/version.json", () => Results.Text($$"""{ "commit": "{{_webHead}}" }""", "application/json"));
        await _server.StartAsync();
        var site = _server.Urls.First();

        _app = DeskApplication.Build(
        [
            "--Urls=http://127.0.0.1:0",
            $"--Desk:Repository={Repository}",
            $"--Desk:WebRepository={WebRepository}",
            $"--Dataset:ResourcesPath={Path.Combine(_root, "resources")}",
            $"--Desk:Workspace={Path.Combine(_root, "workspace")}",
            $"--Desk:Origins:0={Origin}",
            "--Desk:Deploy:PowerShell:0=node",
            $"--Desk:Deploy:PowerShell:1={Path.Combine(_root, "fake-program.js")}",
            "--Desk:Deploy:PowerShell:2=pwsh",
            "--Desk:Deploy:Dotnet:0=node",
            $"--Desk:Deploy:Dotnet:1={Path.Combine(_root, "fake-program.js")}",
            "--Desk:Deploy:Dotnet:2=dotnet",
            $"--Desk:Deploy:Local={site}",
            $"--Desk:Deploy:Environments:dev:Site={site}",
            "--Desk:Deploy:Environments:dev:Target=dev",
            // Nothing listens on port 1: production does not answer yet.
            "--Desk:Deploy:Environments:prod:Site=http://127.0.0.1:1",
            "--Desk:Deploy:Environments:prod:Target=prod",
            "--Desk:Deploy:Environments:staging:Site=http://127.0.0.1:1",
            $"--Publish:ReleasesPath={Releases}",
            "--Publish:Targets:dev:Ssh=deploy@192.0.2.1",
            "--Publish:Targets:dev:Password=not-a-password",
            "--Publish:Targets:prod:Ssh=deploy@192.0.2.1",
            "--Publish:Targets:prod:Password=not-a-password",
            "--Publish:Targets:prod:After=dev",
            "--Database:ConnectionString=Host=localhost;Port=5437;Database=unused;Username=unused",
            "--Database:Password=unused",
        ], contentRoot: _root);
        await _app.StartAsync();
        _http = new HttpClient { BaseAddress = new Uri(_app.Urls.First()) };
    }

    public async Task DisposeAsync()
    {
        _http.Dispose();
        foreach (var app in new[] { _app, _server })
        {
            if (app is not null)
            {
                await app.StopAsync();
                await app.DisposeAsync();
            }
        }

        foreach (var file in Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories))
        {
            // git writes its objects read-only, which Windows will not delete as they are.
            File.SetAttributes(file, FileAttributes.Normal);
        }

        Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public async Task EachEnvironmentSaysWhatItRunsAndWhatTheBranchHasThatItDoesNot()
    {
        var state = await Read();

        var dev = state.Environments.Single(e => e.Key == "dev");
        (dev.Reach, dev.Configured, dev.Release).Should().Be(("up", true, "20260918a"));
        dev.Commit!.Sha.Should().Be(_deployed, "a short hash the server reports is filled in from the checkout");
        dev.Commit.Subject.Should().Be("The first");
        dev.WebCommit!.Sha.Should().Be(_webHead);
        dev.Behind.Select(b => (b.Repository, b.Commits)).Should().Equal(("core", 1), ("web", 0));
        dev.Setup.Should().OnlyContain(s => s.Done == true);

        var local = state.Environments.Single(e => e.Key == Deployment.Local);
        local.Actions.Select(a => a.Kind).Should().Equal("release");

        var prod = state.Environments.Single(e => e.Key == "prod");
        prod.Reach.Should().Be("down");
        prod.Setup.Single(s => s.Key == "answering").Done.Should().BeFalse();
        prod.Actions.Single(a => a.Kind == "corpus").Blocked.Should().Be("devFirst", "the release has not been to dev");

        var core = state.Repositories.Single(r => r.Key == "core");
        (core.Head!.Sha, core.Pushed!.Sha, core.Unpushed).Should().Be((_coreHead, _coreHead, 0));
        state.Releases.Should().ContainSingle().Which.Name.Should().Be(Release);
    }

    [Fact]
    public async Task AnEnvironmentThisMachineHasNoAddressForSaysSoAndOffersNothing()
    {
        var staging = (await Read()).Environments.Single(e => e.Key == "staging");

        staging.Configured.Should().BeFalse();
        staging.Setup.Where(s => s.Done == false).Select(s => s.Key).Should().Contain(["address", "password", "answering"]);
        staging.Actions.Should().OnlyContain(a => !a.Enabled && a.Blocked == "address");
    }

    [Fact]
    public async Task ADryRunShipsNothingAndIsLoggedAsOne()
    {
        var code = Plan(await Read(), "dev", "code");

        var started = await Start(new
        {
            action = "code", environment = "dev", dryRun = true,
            commit = code.Commit!.Sha, webCommit = code.WebCommit!.Sha,
        });
        var finished = await Finished(started.Run!.Id);

        finished.Run.State.Should().Be("succeeded");
        finished.Lines.Should().Contain("pretending to deploy");
        Called().Should().ContainSingle().Which.Should().Equal(
            "pwsh", "-NoProfile", "-File", Path.Combine(Repository, "scripts", "deploy.ps1"),
            "-Server", "deploy@192.0.2.1", "-Environment", "dev", "-Commit", _coreHead, "-WebCommit", _webHead, "-WhatIf");

        var logged = await Logged(2);
        logged.Select(e => (e.Section, e.Action, e.Target)).Should().AllBeEquivalentTo(("deploy", "code", "environment/dev"));
        logged.Select(e => e.After!["state"]!.GetValue<string>()).Should().Equal("started", "succeeded");
        logged.Should().OnlyContain(e => e.After!["dryRun"]!.GetValue<bool>());
        logged[0].Before!["commit"]!.GetValue<string>().Should().Be(_deployed, "what it ran before is kept beside what it was given");

        var after = await Read();
        after.History.Should().ContainSingle().Which.Should().Match<DeployHistoryEntry>(h =>
            h.State == "succeeded" && h.DryRun && h.Commit == _coreHead && h.Environment == "dev");
        after.Environments.Single(e => e.Key == "dev").Last.Code.Should().BeNull("a dry run deployed nothing");
    }

    [Fact]
    public async Task ProductionIsChangedOnlyWhenItsNameIsTyped()
    {
        var code = Plan(await Read(), "prod", "code");
        object Request(string? confirm) => new
        {
            action = "code", environment = "prod", dryRun = false, confirm,
            commit = code.Commit!.Sha, webCommit = code.WebCommit!.Sha,
        };

        (await Send(Request(null))).StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await Send(Request("dev"))).StatusCode.Should().Be(HttpStatusCode.Conflict);
        Called().Should().BeEmpty();

        var started = await Start(Request("prod"));
        (await Finished(started.Run!.Id)).Run.State.Should().Be("succeeded");
        Called().Should().ContainSingle().Which.Should().ContainInOrder("-Environment", "prod").And.NotContain("-WhatIf");

        var prod = (await Read()).Environments.Single(e => e.Key == "prod");
        prod.Last.Code!.Commit.Should().Be(_coreHead);
    }

    [Fact]
    public async Task WhatWasConfirmedMustStillBeWhatWouldShip()
    {
        var response = await Send(new
        {
            action = "code", environment = "dev", dryRun = false,
            commit = _deployed, webCommit = _webHead,
        });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await response.Content.ReadAsStringAsync()).Should().Contain("no longer what was confirmed");
        Called().Should().BeEmpty();
    }

    [Fact]
    public async Task ACorpusIsPublishedByForgeWithTheReleaseThatWasConfirmed()
    {
        var corpus = Plan(await Read(), "dev", "corpus");
        corpus.Release.Should().Be(Release);

        var started = await Start(new { action = "corpus", environment = "dev", dryRun = true, release = Release });
        await Finished(started.Run!.Id);

        Called().Should().ContainSingle().Which.Should().Equal(
            "dotnet", "run", "--project", "Essenthos.Forge", "-c", "Release", "-v", "q", "--",
            "publish", "--to", "dev", "--release", Release, "--dry-run");
    }

    [Fact]
    public async Task AFailedRunIsRecordedAsFailed()
    {
        var started = await Start(new { action = "rollback", environment = "dev", dryRun = false, note = "fail" });

        var finished = await Finished(started.Run!.Id);

        (finished.Run.State, finished.Run.ExitCode).Should().Be(("failed", 4));
        var history = (await Read()).History.Should().ContainSingle().Which;
        (history.Action, history.State, history.ExitCode, history.Note).Should().Be(("rollback", "failed", 4, "fail"));
    }

    private static DeployPlan Plan(DeploymentResponse state, string environment, string kind) =>
        state.Environments.Single(e => e.Key == environment).Actions.Single(a => a.Kind == kind).Plan!;

    private async Task<DeploymentResponse> Read() => await Json<DeploymentResponse>(await _http.GetAsync("/desk-api/deploy"));

    private async Task<RunStarted> Start(object body)
    {
        var response = await Send(body);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return await Json<RunStarted>(response);
    }

    private Task<HttpResponseMessage> Send(object body)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/desk-api/deploy/runs") { Content = JsonContent.Create(body) };
        request.Headers.Add("Origin", Origin);
        return _http.SendAsync(request);
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

    /// <summary>The change log in the order written, once it holds <paramref name="count"/> lines; the end of a run is written after it ends.</summary>
    private async Task<IReadOnlyList<ChangeEntry>> Logged(int count)
    {
        var log = new ChangeLog(DeskPaths.Read(_app!.Services.GetRequiredService<IConfiguration>()));
        for (var attempt = 0; attempt < 50 && log.Read().Entries.Count < count; attempt++)
        {
            await Task.Delay(100);
        }

        return log.Read().Entries.Reverse().ToList();
    }

    private static async Task<T> Json<T>(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<T>(new JsonSerializerOptions(JsonSerializerDefaults.Web)))!;

    private List<string[]> Called() =>
        File.Exists(Calls)
            ? File.ReadAllLines(Calls).Select(line => JsonSerializer.Deserialize<string[]>(line)!).ToList()
            : [];

    private static string Commit(string folder, string message)
    {
        if (!Directory.Exists(Path.Combine(folder, ".git")))
        {
            Directory.CreateDirectory(folder);
            Git(folder, "init", "-q", "-b", "main");
        }

        File.AppendAllText(Path.Combine(folder, "file.txt"), message + "\n");
        Git(folder, "add", "file.txt");
        Git(folder, "-c", "user.name=Test", "-c", "user.email=test@example.org", "commit", "-q", "-m", message);
        return Git(folder, "rev-parse", "HEAD");
    }

    private static string Git(string folder, params string[] arguments)
    {
        var start = new ProcessStartInfo("git") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        start.ArgumentList.Add("-C");
        start.ArgumentList.Add(folder);
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        return process.ExitCode == 0 ? output.Trim() : throw new InvalidOperationException($"git {string.Join(' ', arguments)}: {error}");
    }

    /// <summary>
    /// pwsh and dotnet as far as the console runs them: every call recorded with the program's name
    /// first, a line of output, and a failure where the change log note asks for one.
    /// </summary>
    private const string FakeProgram =
        """
        const fs = require("fs")
        const path = require("path")
        const args = process.argv.slice(2)
        fs.appendFileSync(path.join(__dirname, "calls.log"), JSON.stringify(args) + "\n")
        console.log("pretending to deploy")
        process.exit(args.includes("rollback") && !args.includes("--dry-run") ? 4 : 0)
        """;
}
