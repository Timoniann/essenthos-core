using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Essenthos.Core.Desk;

/// <param name="Sha">The full hash where it is known here, otherwise as the environment reported it.</param>
/// <param name="Subject">The first line of its message, where this machine has the commit.</param>
internal sealed record CommitInfo(string Sha, string? Subject, string? At);

/// <param name="Key"><c>core</c> or <c>web</c>.</param>
/// <param name="Head">The tip of the branch that is deployed from, here.</param>
/// <param name="Pushed">That branch as GitHub last had it when this machine pushed or fetched, which is what CI builds images from.</param>
/// <param name="Unpushed">Commits on the branch here that GitHub does not have yet.</param>
internal sealed record RepositoryState(string Key, bool Found, CommitInfo? Head, CommitInfo? Pushed, int? Unpushed);

internal sealed record ReleasePlace(string Target, string At);

/// <summary>A corpus release <c>forge release</c> made on this machine, and where it has been published.</summary>
internal sealed record ReleaseBuilt(string Name, string BuiltAt, long Bytes, string ForgeVersion, IReadOnlyList<ReleasePlace> PublishedTo);

/// <param name="Deployed">The commit the environment runs, or null where it cannot be told.</param>
/// <param name="Commits">How many commits the branch here has that it does not, or null where that cannot be counted.</param>
internal sealed record Behind(string Repository, string? Deployed, int? Commits);

/// <param name="Done">Whether it is in place, or null where the console cannot tell from here.</param>
internal sealed record SetupStep(string Key, bool? Done);

/// <summary>Exactly what an action would do, which the confirmation repeats and the start checks again.</summary>
internal sealed record DeployPlan(string? Server, CommitInfo? Commit, CommitInfo? WebCommit, string? Release, string? ReleaseBuiltAt, long? ReleaseBytes);

/// <param name="Kind"><c>code</c>, <c>codeBack</c>, <c>corpus</c>, <c>rollback</c> or <c>release</c>.</param>
/// <param name="Blocked">Why it cannot run now, as a key the page words: <c>address</c>, <c>password</c>, <c>notPushed</c>, <c>noRelease</c>, <c>devFirst</c>, <c>noPrevious</c>, <c>running</c>.</param>
internal sealed record DeployAction(string Kind, bool Enabled, string? Blocked, DeployPlan? Plan);

/// <param name="Code">The last code deploy that succeeded, from the change log.</param>
/// <param name="Corpus">The last corpus publication or rollback that succeeded, from the change log.</param>
internal sealed record DeployMarks(DeployMark? Code, DeployMark? Corpus);

internal sealed record DeployMark(string At, string? Commit, string? WebCommit, string? Release);

/// <param name="Key"><c>local</c>, <c>dev</c> or <c>prod</c>.</param>
/// <param name="Site">The address it is reached at.</param>
/// <param name="Configured">Whether this machine knows how to reach the server over ssh; always true for this machine.</param>
/// <param name="Reach"><c>up</c>, <c>empty</c> (it answers and has no corpus), <c>locked</c> (behind the dev password), <c>down</c>, or <c>unknown</c> where there is no address to ask.</param>
internal sealed record EnvironmentState(
    string Key,
    string? Site,
    bool Configured,
    string Reach,
    CommitInfo? Commit,
    string? BuiltAt,
    CommitInfo? WebCommit,
    string? Release,
    string? ReleaseBuiltAt,
    DeployMarks Last,
    IReadOnlyList<Behind> Behind,
    IReadOnlyList<SetupStep> Setup,
    IReadOnlyList<DeployAction> Actions);

/// <param name="State"><c>succeeded</c>, <c>failed</c>, <c>running</c>, or <c>interrupted</c> where the console stopped before the run did.</param>
internal sealed record DeployHistoryEntry(
    int Line,
    string At,
    string Action,
    string Environment,
    string State,
    bool DryRun,
    string? Commit,
    string? WebCommit,
    string? Release,
    int? ExitCode,
    string? Note);

internal sealed record DeploymentResponse(
    IReadOnlyList<EnvironmentState> Environments,
    IReadOnlyList<RepositoryState> Repositories,
    IReadOnlyList<ReleaseBuilt> Releases,
    IReadOnlyList<DeployHistoryEntry> History,
    OperationRun? Running);

/// <param name="Confirm">The environment's name as the owner typed it; production takes nothing else.</param>
/// <param name="Commit">The core commit the owner confirmed, which must still be what would ship.</param>
/// <param name="WebCommit">The web commit the owner confirmed.</param>
/// <param name="Release">The corpus release the owner confirmed.</param>
internal sealed record DeployRequest(
    string Action,
    string Environment,
    bool DryRun,
    string? Confirm,
    string? Commit,
    string? WebCommit,
    string? Release,
    string? Note);

/// <summary>
/// Where the site runs and what reaches it: this machine, dev and production, what code and which
/// corpus each has, what the branch here holds that they do not, and the runs that move code and
/// corpus to them — <c>scripts/deploy.ps1</c> and <c>forge release | publish | rollback</c>, each
/// the same command a person would type, started one at a time through the console's runner and
/// recorded in the change log.
///
/// <para>
/// A server's address and database password are read from the same configuration Forge reads
/// (<c>Publish:Targets:&lt;name&gt;</c>, in user secrets) and only their presence is ever reported.
/// The addresses the site is reached at are under <c>Desk:Deploy:Environments</c>.
/// </para>
/// </summary>
internal sealed class Deployment(
    DeskPaths paths,
    IConfiguration configuration,
    Operations operations,
    ChangeLog log,
    ILogger<Deployment> logger) : IDisposable
{
    public const string Section = "deploy";

    public const string Local = "local";

    public const string Production = "prod";

    /// <summary>The step the runner files a deploy run under.</summary>
    public const string Step = "deploy";

    private const string CodeAction = "code";

    private const string CodeBackAction = "codeBack";

    private const string CorpusAction = "corpus";

    private const string RollbackAction = "rollback";

    private const string ReleaseAction = "release";

    private const string Started = "started";

    /// <summary>How long a server may take to answer before it is reported as not answering.</summary>
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(4);

    /// <summary>How long git may take over one question about a checkout.</summary>
    private static readonly TimeSpan GitPatience = TimeSpan.FromSeconds(15);

    private readonly HttpClient _http = new(new SocketsHttpHandler { AllowAutoRedirect = false }) { Timeout = Patience };

    private string Branch => configuration["Desk:Deploy:Branch"] is { Length: > 0 } branch ? branch : "main";

    private string Remote => configuration["Desk:Deploy:Remote"] is { Length: > 0 } remote ? remote : "origin";

    private string WebRepository => configuration["Desk:WebRepository"] is { Length: > 0 } web
        ? Path.GetFullPath(Path.Combine(paths.Repository, web))
        : Path.Combine(paths.Workspace, "essenthos-web");

    private string Releases => configuration["Publish:ReleasesPath"] is { Length: > 0 } releases
        ? Path.GetFullPath(Path.Combine(paths.Repository, releases))
        : Path.Combine(paths.Repository, ".releases");

    public void Dispose() => _http.Dispose();

    public async Task<DeploymentResponse> Read(CancellationToken cancellationToken)
    {
        var core = RepositoryState("core", paths.Repository, cancellationToken);
        var web = RepositoryState("web", WebRepository, cancellationToken);
        var repositories = await Task.WhenAll(core, web);
        var releases = ReadReleases();
        var history = History();
        var running = operations.Current();

        var environments = await Task.WhenAll(
            new[] { Local }.Concat(RemoteNames()).Select(name =>
                Environment(name, repositories[0], repositories[1], releases, history, running, cancellationToken)));

        return new DeploymentResponse(environments, repositories, releases, history, running);
    }

    public async Task<RunStarted> Start(DeployRequest request, CancellationToken cancellationToken)
    {
        var state = await Read(cancellationToken);
        var environment = state.Environments.FirstOrDefault(e => e.Key == request.Environment);
        var action = environment?.Actions.FirstOrDefault(a => a.Kind == request.Action);
        if (environment is null || action is null)
        {
            return new RunStarted(null, "There is no such action for that environment.");
        }

        if (!action.Enabled)
        {
            return new RunStarted(null, $"It cannot run now: {Refusal(action.Blocked)}");
        }

        if (environment.Key == Production && !request.DryRun && request.Confirm?.Trim() != Production)
        {
            return new RunStarted(null, $"Production is changed only when its name, {Production}, is typed to confirm.");
        }

        var plan = action.Plan;
        static bool Confirmed(string? planned, string? confirmed) => planned is null || planned == confirmed;
        if (!Confirmed(plan?.Commit?.Sha, request.Commit)
            || !Confirmed(plan?.WebCommit?.Sha, request.WebCommit)
            || !Confirmed(plan?.Release, request.Release))
        {
            return new RunStarted(null,
                "What would ship is no longer what was confirmed: the branch or the releases changed meanwhile. Look again and confirm again.");
        }

        var start = Command(action, environment.Key, request.DryRun);
        var target = $"environment/{environment.Key}";
        var kind = action.Kind == CodeBackAction ? CodeAction : action.Kind;
        var before = Before(kind, environment);
        var begun = JsonFiles.Now();

        // Written before the process starts, so the line saying it began is always above the one saying
        // how it ended, and a console stopped mid-run leaves a run the history shows as interrupted.
        await log.Append(Section, kind, target, before, Mark(plan, request.DryRun, begun, Started, null), request.Note, null);
        var launched = operations.Launch(
            $"{kind}-{environment.Key}",
            Step,
            start,
            finished => Append(kind, target, before, Mark(plan, request.DryRun, begun, finished.State, finished.ExitCode), request.Note));
        if (launched.Run is null)
        {
            Append(kind, target, before, Mark(plan, request.DryRun, begun, "failed", null), launched.Problem);
            return launched;
        }

        logger.LogInformation("{Kind} for {Environment} started{DryRun}", kind, environment.Key, request.DryRun ? " as a dry run" : "");
        return launched;
    }

    private IEnumerable<string> RemoteNames() =>
        configuration.GetSection("Desk:Deploy:Environments").GetChildren().Select(c => c.Key);

    private async Task<EnvironmentState> Environment(
        string name,
        RepositoryState core,
        RepositoryState web,
        IReadOnlyList<ReleaseBuilt> releases,
        IReadOnlyList<DeployHistoryEntry> history,
        OperationRun? running,
        CancellationToken cancellationToken)
    {
        var last = new DeployMarks(
            LastMark(history, name, e => e.Action == CodeAction),
            LastMark(history, name, e => e.Action is CorpusAction or RollbackAction));

        if (name == Local)
        {
            var api = configuration["Desk:Deploy:Local"] is { Length: > 0 } local ? local : "http://localhost:5279";
            var probe = await Probe(api, null, web: false, cancellationToken);
            var commit = await Known(paths.Repository, probe.Commit, cancellationToken);
            var behind = new List<Behind>
            {
                new("core", commit?.Sha ?? probe.Commit, await Count(paths.Repository, commit?.Sha, cancellationToken)),
            };
            var releaseAction = new DeployAction(ReleaseAction, running is null, running is null ? null : "running", null);
            return new EnvironmentState(
                name, api, true, probe.Reach, commit, probe.BuiltAt, null, probe.Release, probe.ReleaseBuiltAt,
                last, behind, [], [releaseAction]);
        }

        var section = configuration.GetSection($"Desk:Deploy:Environments:{name}");
        var site = section["Site"];
        var targetName = section["Target"] is { Length: > 0 } t ? t : name;
        var target = configuration.GetSection($"Publish:Targets:{targetName}");
        var ssh = target["Ssh"] is { Length: > 0 } address ? address : null;
        var hasPassword = target["Password"] is { Length: > 0 };
        var credentials = section["User"] is { Length: > 0 } user && section["Password"] is { Length: > 0 } secret
            ? new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"{user}:{secret}")))
            : null;

        var remote = site is null ? new ProbeResult("unknown", null, null, null, null, null) : await Probe(site, credentials, web: true, cancellationToken);
        var coreCommit = await Known(paths.Repository, remote.Commit ?? last.Code?.Commit, cancellationToken);
        var webCommit = await Known(WebRepository, remote.WebCommit ?? last.Code?.WebCommit, cancellationToken);
        var behindList = new List<Behind>
        {
            new("core", coreCommit?.Sha ?? remote.Commit, await Count(paths.Repository, coreCommit?.Sha, cancellationToken)),
            new("web", webCommit?.Sha ?? remote.WebCommit, await Count(WebRepository, webCommit?.Sha, cancellationToken)),
        };

        var newest = releases.LastOrDefault();
        var after = target["After"] is { Length: > 0 } a ? a : null;
        var answering = remote.Reach is "up" or "empty" or "locked";
        var setup = new List<SetupStep>
        {
            new("address", ssh is not null),
            new("password", hasPassword),
            new("answering", site is null ? null : answering),
            new("pushed", core.Pushed is not null),
            new("corpus", remote.Reach == "up" ? remote.Release is not null : remote.Reach == "empty" ? false : null),
        };

        string? Blocked(params (bool Failing, string Reason)[] checks) =>
            running is not null ? "running" : checks.FirstOrDefault(c => c.Failing).Reason;

        var codePlan = new DeployPlan(ssh, core.Pushed, web.Pushed, null, null, null);
        var previous = PreviousCode(history, name);
        var backPlan = previous is null
            ? null
            : new DeployPlan(ssh, await Known(paths.Repository, previous.Commit, cancellationToken),
                previous.WebCommit is null ? null : await Known(WebRepository, previous.WebCommit, cancellationToken), null, null, null);
        var corpusPlan = new DeployPlan(ssh, null, null, newest?.Name, newest?.BuiltAt, newest?.Bytes);
        var actions = new List<DeployAction>
        {
            Action(CodeAction, codePlan, Blocked((ssh is null, "address"), (core.Pushed is null, "notPushed"))),
            Action(CorpusAction, corpusPlan, Blocked(
                (ssh is null, "address"),
                (!hasPassword, "password"),
                (newest is null, "noRelease"),
                (after is not null && newest is not null && newest.PublishedTo.All(p => p.Target != after), "devFirst"))),
            Action(RollbackAction, new DeployPlan(ssh, null, null, null, null, null), Blocked((ssh is null, "address"))),
            Action(CodeBackAction, backPlan, Blocked((ssh is null, "address"), (backPlan?.Commit is null, "noPrevious"))),
        };

        return new EnvironmentState(
            name, site, ssh is not null, remote.Reach, coreCommit ?? Unknown(remote.Commit), remote.BuiltAt,
            webCommit ?? Unknown(remote.WebCommit), remote.Release, remote.ReleaseBuiltAt, last, behindList, setup, actions);
    }

    private static DeployAction Action(string kind, DeployPlan? plan, string? blocked) =>
        new(kind, blocked is null, blocked, plan);

    private static CommitInfo? Unknown(string? sha) => sha is null ? null : new CommitInfo(sha, null, null);

    private ProcessStartInfo Command(DeployAction action, string environment, bool dryRun)
    {
        var plan = action.Plan;
        var target = configuration[$"Desk:Deploy:Environments:{environment}:Target"] is { Length: > 0 } t ? t : environment;
        List<string> arguments;
        string[] program;
        switch (action.Kind)
        {
            case CodeAction or CodeBackAction:
                program = Program("Desk:Deploy:PowerShell", "pwsh");
                arguments =
                [
                    "-NoProfile", "-File", Path.Combine(paths.Repository, "scripts", "deploy.ps1"),
                    "-Server", plan!.Server!, "-Environment", environment, "-Commit", plan.Commit!.Sha,
                    .. plan.WebCommit is { } web ? new[] { "-WebCommit", web.Sha } : [],
                    .. dryRun ? new[] { "-WhatIf" } : [],
                ];
                break;
            default:
                program = Program("Desk:Deploy:Dotnet", "dotnet");
                arguments =
                [
                    "run", "--project", "Essenthos.Forge", "-c", "Release", "-v", "q", "--",
                    .. action.Kind switch
                    {
                        CorpusAction => new[] { "publish", "--to", target, "--release", plan!.Release! },
                        RollbackAction => ["rollback", "--to", target],
                        _ => ["release"],
                    },
                    .. dryRun ? new[] { "--dry-run" } : [],
                ];
                break;
        }

        var start = new ProcessStartInfo(program[0])
        {
            WorkingDirectory = paths.Repository,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var argument in program.Skip(1).Concat(arguments))
        {
            start.ArgumentList.Add(argument);
        }

        start.Environment["NO_COLOR"] = "1";
        start.Environment["DOTNET_NOLOGO"] = "1";
        return start;
    }

    /// <summary>A program configured as one command or as a command and its first arguments, as <c>Desk:Avioniq</c> is.</summary>
    private string[] Program(string key, string fallback) =>
        configuration.GetSection(key).Get<string[]>() is { Length: > 0 } several
            ? several
            : configuration[key] is { Length: > 0 } one ? [one] : [fallback];

    private static JsonObject Mark(DeployPlan? plan, bool dryRun, string started, string state, int? exitCode) => new()
    {
        ["state"] = state,
        ["dryRun"] = dryRun,
        ["started"] = started,
        ["commit"] = plan?.Commit?.Sha,
        ["webCommit"] = plan?.WebCommit?.Sha,
        ["release"] = plan?.Release,
        ["exitCode"] = exitCode,
    };

    private static JsonObject? Before(string kind, EnvironmentState environment) => kind switch
    {
        CodeAction when environment.Commit is not null || environment.WebCommit is not null => new JsonObject
        {
            ["commit"] = environment.Commit?.Sha,
            ["webCommit"] = environment.WebCommit?.Sha,
        },
        CorpusAction or RollbackAction when environment.Release is not null => new JsonObject { ["release"] = environment.Release },
        _ => null,
    };

    private void Append(string kind, string target, JsonNode? before, JsonNode after, string? note) =>
        _ = log.Append(Section, kind, target, before, after, note, null)
            .ContinueWith(
                task => logger.LogError(task.Exception, "The end of a {Kind} run could not be written to the change log", kind),
                TaskContinuationOptions.OnlyOnFaulted);

    /// <summary>Every deploy run the change log holds, newest first, each once with how it ended.</summary>
    private IReadOnlyList<DeployHistoryEntry> History()
    {
        var running = operations.Current() is { Step: Step } current ? current : null;
        var lines = log.Read().Entries.Where(e => e.Section == Section && e.After is JsonObject).ToList();
        static string Key(ChangeEntry entry) => $"{entry.Target}|{Text((JsonObject)entry.After!, "started") ?? entry.At}";
        var ended = lines.Where(e => Text((JsonObject)e.After!, "state") != Started).Select(Key).ToHashSet(StringComparer.Ordinal);
        var entries = new List<DeployHistoryEntry>();
        var newestStart = true;
        foreach (var entry in lines)
        {
            var after = (JsonObject)entry.After!;
            var state = Text(after, "state") ?? "failed";
            if (state == Started)
            {
                if (ended.Contains(Key(entry)))
                {
                    continue;
                }

                // One run at a time, so only the newest run without an end can still be going.
                state = newestStart && running?.Name == $"{entry.Action}-{EnvironmentOf(entry.Target)}" ? "running" : "interrupted";
                newestStart = false;
            }

            entries.Add(new DeployHistoryEntry(
                entry.Line,
                entry.At,
                entry.Action,
                EnvironmentOf(entry.Target),
                state,
                after["dryRun"] is JsonValue dry && dry.TryGetValue<bool>(out var isDry) && isDry,
                Text(after, "commit"),
                Text(after, "webCommit"),
                Text(after, "release"),
                after["exitCode"] is JsonValue code && code.TryGetValue<int>(out var exit) ? exit : null,
                entry.Note));
        }

        return entries;
    }

    private static string EnvironmentOf(string target) =>
        target.StartsWith("environment/", StringComparison.Ordinal) ? target["environment/".Length..] : target;

    private static DeployMark? LastMark(IReadOnlyList<DeployHistoryEntry> history, string environment, Func<DeployHistoryEntry, bool> which) =>
        history.FirstOrDefault(e => e.Environment == environment && e is { State: "succeeded", DryRun: false } && which(e)) is { } found
            ? new DeployMark(found.At, found.Commit, found.WebCommit, found.Release)
            : null;

    /// <summary>The code deployed before the last one, which is what undoing a code deploy runs again.</summary>
    private static DeployMark? PreviousCode(IReadOnlyList<DeployHistoryEntry> history, string environment) =>
        history
            .Where(e => e.Environment == environment && e is { State: "succeeded", DryRun: false, Action: CodeAction } && e.Commit is not null)
            .Skip(1)
            .Select(e => new DeployMark(e.At, e.Commit, e.WebCommit, e.Release))
            .FirstOrDefault();

    private IReadOnlyList<ReleaseBuilt> ReadReleases()
    {
        if (!Directory.Exists(Releases))
        {
            return [];
        }

        var releases = new List<ReleaseBuilt>();
        foreach (var file in Directory.GetFiles(Releases, "*.json").Order(StringComparer.Ordinal))
        {
            try
            {
                if (JsonNode.Parse(File.ReadAllText(file)) is not JsonObject record || Text(record, "Name") is not { } name)
                {
                    continue;
                }

                var ledger = Path.Combine(Releases, $"{name}.published");
                var places = File.Exists(ledger)
                    ? File.ReadAllLines(ledger)
                        .Select(line => line.Split('\t'))
                        .Where(parts => parts.Length == 2 && parts[0].Length > 0)
                        .GroupBy(parts => parts[0], StringComparer.Ordinal)
                        .Select(published => new ReleasePlace(published.Key, published.Last()[1]))
                        .ToList()
                    : [];
                releases.Add(new ReleaseBuilt(
                    name,
                    Text(record, "BuiltAt") ?? string.Empty,
                    record["Bytes"] is JsonValue bytes && bytes.TryGetValue<long>(out var size) ? size : 0,
                    Text(record, "ForgeVersion") ?? string.Empty,
                    places));
            }
            catch (Exception exception) when (exception is IOException or JsonException)
            {
                logger.LogWarning(exception, "The release record {File} could not be read", file);
            }
        }

        return releases;
    }

    private string Refusal(string? blocked) => blocked switch
    {
        "address" => "this machine does not know the server's address.",
        "password" => "the database password the publication checks the release with is not set on this machine.",
        "notPushed" => "the branch is not on GitHub, so there is no image built from it.",
        "noRelease" => "no corpus release has been made on this machine.",
        "devFirst" => "production only takes a release dev has already accepted.",
        "noPrevious" => "there is no earlier code deploy to return to.",
        "running" => "another run has not finished.",
        _ => "it is not available.",
    };

    // --- the servers -------------------------------------------------------------------------------

    private sealed record ProbeResult(string Reach, string? Commit, string? BuiltAt, string? Release, string? ReleaseBuiltAt, string? WebCommit);

    private async Task<ProbeResult> Probe(string site, AuthenticationHeaderValue? credentials, bool web, CancellationToken cancellationToken)
    {
        var root = site.TrimEnd('/');
        var ready = await Ask($"{root}/v1/health/ready", credentials, cancellationToken);
        var reach = ready switch
        {
            null => "down",
            { StatusCode: HttpStatusCode.OK } => "up",
            { StatusCode: HttpStatusCode.ServiceUnavailable } => "empty",
            { StatusCode: HttpStatusCode.Unauthorized } => "locked",
            _ => "down",
        };
        ready?.Dispose();
        if (reach is "down" or "locked")
        {
            return new ProbeResult(reach, null, null, null, null, null);
        }

        string? commit = null, builtAt = null, release = null, releaseBuiltAt = null, webCommit = null;
        using (var version = await Ask($"{root}/v1/health/version", credentials, cancellationToken))
        {
            if (version is { IsSuccessStatusCode: true } && await Json(version, cancellationToken) is JsonObject body)
            {
                commit = Text(body, "commit");
                builtAt = Text(body, "builtAt");
                if (body["release"] is JsonObject label)
                {
                    release = Text(label, "name");
                    releaseBuiltAt = Text(label, "builtAt");
                }
            }
        }

        if (web)
        {
            using var file = await Ask($"{root}/version.json", credentials, cancellationToken);
            if (file is { IsSuccessStatusCode: true } && await Json(file, cancellationToken) is JsonObject body)
            {
                webCommit = Text(body, "commit") is { Length: > 0 } sha ? sha : null;
            }
        }

        return new ProbeResult(reach, commit, builtAt, release, releaseBuiltAt, webCommit);
    }

    private async Task<HttpResponseMessage?> Ask(string url, AuthenticationHeaderValue? credentials, CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Authorization = credentials;
            return await _http.SendAsync(request, cancellationToken);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or UriFormatException)
        {
            return null;
        }
    }

    private static async Task<JsonNode?> Json(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            return JsonNode.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        }
        catch (JsonException)
        {
            return null;
        }
    }

    // --- the checkouts -----------------------------------------------------------------------------

    private async Task<RepositoryState> RepositoryState(string key, string folder, CancellationToken cancellationToken)
    {
        if (!Directory.Exists(folder) || await Git(folder, ["rev-parse", "--git-dir"], cancellationToken) is null)
        {
            return new RepositoryState(key, false, null, null, null);
        }

        var head = await Describe(folder, Branch, cancellationToken);
        var pushed = await Describe(folder, $"{Remote}/{Branch}", cancellationToken);
        var unpushed = pushed is null ? null : await Count(folder, pushed.Sha, cancellationToken);
        return new RepositoryState(key, true, head, pushed, unpushed);
    }

    /// <summary>A commit named by a ref or a hash, where the checkout has it.</summary>
    private async Task<CommitInfo?> Describe(string folder, string name, CancellationToken cancellationToken)
    {
        var line = await Git(folder, ["log", "-1", "--format=%H%x09%cI%x09%s", $"{name}^{{commit}}", "--"], cancellationToken);
        if (line is null || line.Split('\t', 3) is not [var sha, var at, var subject])
        {
            return null;
        }

        return new CommitInfo(sha, subject, at);
    }

    /// <summary>The commit an environment reported, filled in from the checkout where it has it.</summary>
    private async Task<CommitInfo?> Known(string folder, string? sha, CancellationToken cancellationToken) =>
        sha is null || !IsHash(sha) ? null : await Describe(folder, sha, cancellationToken);

    /// <summary>How many commits the branch has that <paramref name="sha"/> does not; null where either is unknown here.</summary>
    private async Task<int?> Count(string folder, string? sha, CancellationToken cancellationToken) =>
        sha is null || !IsHash(sha)
            ? null
            : await Git(folder, ["rev-list", "--count", $"{sha}..{Branch}", "--"], cancellationToken) is { } count
              && int.TryParse(count, NumberStyles.None, CultureInfo.InvariantCulture, out var commits)
                ? commits
                : null;

    private static bool IsHash(string value) => value.Length is >= 7 and <= 40 && value.All(char.IsAsciiHexDigit);

    private async Task<string?> Git(string folder, IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        var start = new ProcessStartInfo("git")
        {
            WorkingDirectory = folder,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
        };
        start.ArgumentList.Add("-C");
        start.ArgumentList.Add(folder);
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        using var process = new Process { StartInfo = start };
        try
        {
            process.Start();
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or FileNotFoundException)
        {
            logger.LogWarning(exception, "git could not be started");
            return null;
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(GitPatience);
        try
        {
            var output = process.StandardOutput.ReadToEndAsync(timeout.Token);
            _ = process.StandardError.ReadToEndAsync(timeout.Token);
            await process.WaitForExitAsync(timeout.Token);
            return process.ExitCode == 0 ? (await output).Trim() : null;
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            return null;
        }
    }

    private static string? Text(JsonObject node, string key) =>
        node[key] is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;
}
