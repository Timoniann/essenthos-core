using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Essenthos.Core.Desk;

internal sealed record VisitorPersona(string Id, string? Name, string? Summary);

/// <param name="Kind"><c>task</c>, with a target to reach, or <c>explore</c>, with none.</param>
/// <param name="Landing">Where the visitor was put down: the home page, or a page a search would have led to.</param>
/// <param name="Queries">What such a person would have typed into a search engine to arrive there.</param>
internal sealed record VisitorGoal(string Id, string Kind, string? Text, string? Landing, IReadOnlyList<string> Queries);

internal sealed record VisitorScreen(int Width, int Height);

/// <param name="Minutes">The time the visitor was given.</param>
/// <param name="MinutesUsed">How long it actually took, from its start to its last step.</param>
/// <param name="Steps">How many steps it took after landing.</param>
/// <param name="EndedBy"><c>goal</c>, <c>gave-up</c>, <c>bored</c>, <c>time</c>, <c>steps</c>, <c>error</c>, or null while it is still going.</param>
/// <param name="Reached">Whether it reached its goal; null while it is still going, and for an exploration, which has none.</param>
internal sealed record VisitorSession(
    string Id,
    string? Model,
    VisitorPersona? Persona,
    VisitorGoal? Goal,
    VisitorScreen? Screen,
    string? Language,
    int? Minutes,
    double? MinutesUsed,
    string? StartedAt,
    string? EndedAt,
    int Steps,
    string? EndedBy,
    bool? Reached);

/// <param name="Outcomes">How many sessions ended each way, by <c>endedBy</c>; <c>running</c> for one still going.</param>
internal sealed record VisitorRun(
    string Id,
    string? StartedAt,
    string? Note,
    int Sessions,
    int Reached,
    IReadOnlyDictionary<string, int> Outcomes);

internal sealed record VisitorRunsResponse(IReadOnlyList<VisitorRun> Runs);

internal sealed record VisitorRunResponse(VisitorRun Run, IReadOnlyList<VisitorSession> Sessions);

internal sealed record VisitorTarget(string? Ref, string? Text, string? Role);

/// <param name="Screenshot">The picture's file name beside the step, or null where none was taken.</param>
/// <param name="Blocked">What the browser refused at this step, in words, or null.</param>
internal sealed record VisitorStep(
    int N,
    string? At,
    double? ElapsedSeconds,
    string Action,
    VisitorTarget? Target,
    string? Value,
    string? Thought,
    string? Url,
    string? Title,
    string? Screenshot,
    string? Blocked);

/// <param name="Step">The step it happened at, which the console links to.</param>
/// <param name="Quote">The words on the page that confused it.</param>
internal sealed record VisitorConfusion(string What, string? Url, int? Step, string? Quote);

/// <param name="Interesting">What caught an exploring visitor's interest.</param>
/// <param name="LostInterest">Where an exploring visitor lost interest, and why.</param>
/// <param name="WouldComeBackFor">What would bring an exploring visitor back.</param>
internal sealed record VisitorReport(
    bool? Reached,
    string? Summary,
    string? Path,
    IReadOnlyList<string> Understood,
    IReadOnlyList<VisitorConfusion> Confusing,
    IReadOnlyList<string> Missing,
    IReadOnlyList<string> Wishes,
    string? WouldReturn,
    string? Overall,
    IReadOnlyList<string> Interesting,
    string? LostInterest,
    string? WouldComeBackFor);

/// <param name="Dossier">What the visitor wrote to prepare itself, as written, or null.</param>
/// <param name="Report">Its own account at the end, or null while it has not written one.</param>
internal sealed record VisitorSessionResponse(
    string Run,
    VisitorSession Session,
    string? Dossier,
    IReadOnlyList<VisitorStep> Steps,
    VisitorReport? Report);

/// <summary>
/// The simulated visitors' sessions, read where the visit command writes them: one folder per run
/// under the control folder's <c>.visitors</c>, one folder per session under it, holding the
/// session, the dossier, every step with its screenshot and the visitor's report. The console only
/// reads them. A session is written as it goes, so a file caught mid-write is left out of this
/// reading and read on the next.
/// </summary>
internal sealed partial class Visitors(DeskPaths paths, ILogger<Visitors> logger)
{
    public const string RunFile = "run.json";

    public const string SessionFile = "session.json";

    public const string DossierFile = "dossier.md";

    public const string ReportFile = "report.json";

    public const string StepsFolder = "steps";

    /// <summary>The outcome counted for a session that has not ended.</summary>
    public const string Running = "running";

    /// <summary>The goal outcome, the one that reaches it.</summary>
    private const string GoalReached = "goal";

    /// <summary>A goal with a target; the other kind, <c>explore</c>, has none.</summary>
    public const string Task = "task";

    public const string Explore = "explore";

    private const string Screenshot = ".png";

    private static readonly JsonDocumentOptions Reading = new()
    {
        AllowTrailingCommas = true,
        CommentHandling = JsonCommentHandling.Skip,
    };

    /// <summary>What a run, a session or a file in a request may be called: no separators, no climbing.</summary>
    [GeneratedRegex("^[A-Za-z0-9._-]+$")]
    private static partial Regex SegmentPattern();

    public string Root => paths.Visitors;

    public static bool IsSegment(string? segment) =>
        segment is { Length: > 0 and <= 200 } && SegmentPattern().IsMatch(segment) && !segment.Contains("..", StringComparison.Ordinal);

    public VisitorRunsResponse Runs()
    {
        if (!Directory.Exists(Root))
        {
            return new VisitorRunsResponse([]);
        }

        var runs = Directory.EnumerateDirectories(Root)
            .Select(Path.GetFileName)
            .Where(IsSegment)
            .Select(id => Summary(id!, Sessions(id!)))
            .OrderByDescending(r => r.StartedAt ?? "", StringComparer.Ordinal)
            .ThenByDescending(r => r.Id, StringComparer.Ordinal)
            .ToList();
        return new VisitorRunsResponse(runs);
    }

    public VisitorRunResponse? Run(string run)
    {
        if (Folder(run) is null)
        {
            return null;
        }

        var sessions = Sessions(run);
        return new VisitorRunResponse(Summary(run, sessions), sessions);
    }

    public VisitorSessionResponse? Session(string run, string session)
    {
        if (Folder(run, session) is not { } folder || ReadSession(session, folder) is not { } read)
        {
            return null;
        }

        var dossier = Path.Combine(folder, DossierFile);
        return new VisitorSessionResponse(
            run,
            read,
            File.Exists(dossier) ? Text(dossier) : null,
            Steps(folder),
            Read(Path.Combine(folder, ReportFile)) is JsonObject report ? Report(report) : null);
    }

    /// <summary>A step's screenshot, or null where the names are not a session's picture.</summary>
    public string? Picture(string run, string session, string file)
    {
        if (!IsSegment(file) || !file.EndsWith(Screenshot, StringComparison.OrdinalIgnoreCase)
            || Folder(run, session) is not { } folder)
        {
            return null;
        }

        var path = DeskPaths.Under(Path.Combine(folder, StepsFolder), file);
        return path is not null && File.Exists(path) ? path : null;
    }

    private string? Folder(params string[] segments)
    {
        if (!segments.All(IsSegment))
        {
            return null;
        }

        var folder = DeskPaths.Under(Root, Path.Combine(segments));
        return folder is not null && Directory.Exists(folder) ? folder : null;
    }

    private List<VisitorSession> Sessions(string run)
    {
        var folder = Path.Combine(Root, run);
        return Directory.EnumerateDirectories(folder)
            .Where(d => IsSegment(Path.GetFileName(d)))
            .Order(StringComparer.Ordinal)
            .Select(d => ReadSession(Path.GetFileName(d), d))
            .OfType<VisitorSession>()
            .ToList();
    }

    private VisitorRun Summary(string id, IReadOnlyList<VisitorSession> sessions)
    {
        var run = Read(Path.Combine(Root, id, RunFile)) as JsonObject;
        var outcomes = sessions
            .GroupBy(s => s.EndedBy ?? Running)
            .OrderBy(g => g.Key, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Count());
        return new VisitorRun(
            id,
            String(run?["startedAt"]) ?? sessions.Select(s => s.StartedAt).Where(s => s is not null).Min(StringComparer.Ordinal),
            String(run?["note"]),
            sessions.Count,
            sessions.Count(s => s.Reached == true),
            outcomes);
    }

    private VisitorSession? ReadSession(string id, string folder)
    {
        if (Read(Path.Combine(folder, SessionFile)) is not JsonObject session)
        {
            return null;
        }

        var steps = StepFiles(folder);
        var last = steps.Count > 0 ? Read(steps[^1]) as JsonObject : null;
        var outcome = session["outcome"] as JsonObject;
        var endedBy = String(outcome?["endedBy"]);
        var startedAt = String(session["startedAt"]);
        var goal = session["goal"] as JsonObject;
        var kind = String(goal?["kind"]) == Explore ? Explore : Task;
        var endedAt = String(session["endedAt"]);

        return new VisitorSession(
            String(session["id"]) ?? id,
            String(session["model"]),
            session["persona"] is JsonObject persona
                ? new VisitorPersona(String(persona["id"]) ?? "", String(persona["name"]), String(persona["summary"]))
                : null,
            goal is not null
                ? new VisitorGoal(String(goal["id"]) ?? "", kind, String(goal["text"]), String(goal["landing"]), Strings(goal["queries"]))
                : null,
            session["screen"] is JsonObject screen && Integer(screen["width"]) is { } width && Integer(screen["height"]) is { } height
                ? new VisitorScreen(width, height)
                : null,
            String(session["language"]),
            Integer(session["minutes"]),
            MinutesUsed(startedAt, endedAt ?? String(last?["at"]), Number(last?["elapsedSeconds"])),
            startedAt,
            endedAt,
            Integer(outcome?["steps"]) ?? Math.Max(steps.Count - 1, 0),
            endedBy,
            endedBy is null || kind == Explore ? null : Boolean(outcome?["reached"]) ?? endedBy == GoalReached);
    }

    /// <summary>From the last step's own clock where it kept one, otherwise from the times the session was started and ended.</summary>
    private static double? MinutesUsed(string? startedAt, string? endedAt, double? elapsedSeconds)
    {
        if (elapsedSeconds is { } seconds)
        {
            return Math.Round(seconds / 60, 1);
        }

        return Time(startedAt) is { } start && Time(endedAt) is { } end && end >= start
            ? Math.Round((end - start).TotalMinutes, 1)
            : null;
    }

    private static List<string> StepFiles(string folder)
    {
        var steps = Path.Combine(folder, StepsFolder);
        return Directory.Exists(steps)
            ? Directory.EnumerateFiles(steps, "*.json").Order(StringComparer.Ordinal).ToList()
            : [];
    }

    private List<VisitorStep> Steps(string folder) =>
        StepFiles(folder)
            .Select(file => (File: file, Node: Read(file) as JsonObject))
            .Where(s => s.Node is not null)
            .Select(s => Step(s.File, s.Node!))
            .OrderBy(s => s.N)
            .ToList();

    private static VisitorStep Step(string file, JsonObject step)
    {
        var picture = String(step["screenshot"]) ?? Path.ChangeExtension(Path.GetFileName(file), Screenshot);
        var target = step["target"] as JsonObject;
        return new VisitorStep(
            Integer(step["n"]) ?? (int.TryParse(Path.GetFileNameWithoutExtension(file), out var n) ? n : 0),
            String(step["at"]),
            Number(step["elapsedSeconds"]),
            String(step["action"]) ?? "",
            target is null ? null : new VisitorTarget(String(target["ref"]), String(target["text"]), String(target["role"])),
            String(step["value"]),
            String(step["thought"]),
            String(step["url"]),
            String(step["title"]),
            IsSegment(picture) && File.Exists(Path.Combine(Path.GetDirectoryName(file)!, picture)) ? picture : null,
            Words(step["blocked"]));
    }

    private static VisitorReport Report(JsonObject report) =>
        new(
            Boolean(report["reached"]),
            String(report["summary"]),
            String(report["path"]),
            Strings(report["understood"]),
            (report["confusing"] as JsonArray ?? [])
                .Select(c => c switch
                {
                    JsonObject point => new VisitorConfusion(
                        String(point["what"]) ?? "", String(point["url"]), Integer(point["step"]), String(point["quote"])),
                    _ => String(c) is { } what ? new VisitorConfusion(what, null, null, null) : null,
                })
                .OfType<VisitorConfusion>()
                .ToList(),
            Strings(report["missing"]),
            Strings(report["wishes"]),
            String(report["wouldReturn"]),
            String(report["overall"]),
            Strings(report["interesting"]),
            Words(report["lostInterest"]),
            Words(report["wouldComeBackFor"]));

    private JsonNode? Read(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            return JsonNode.Parse(Text(path), documentOptions: Reading);
        }
        catch (Exception exception) when (exception is JsonException or IOException)
        {
            logger.LogWarning(exception, "Could not read {File}", path);
            return null;
        }
    }

    /// <summary>Read while the visit command may still be writing it, which a plain read would refuse.</summary>
    private static string Text(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static string? String(JsonNode? node) =>
        node is JsonValue value
            ? value.TryGetValue<string>(out var text) ? text
            : value.TryGetValue<double>(out var number) ? number.ToString(CultureInfo.InvariantCulture)
            : value.TryGetValue<bool>(out var flag) ? (flag ? "true" : "false")
            : null
            : null;

    /// <summary>A value said in words wherever it was written as something else: an object's fields joined, a list's items.</summary>
    private static string? Words(JsonNode? node) =>
        node switch
        {
            null => null,
            JsonValue => String(node),
            JsonArray list => list.Select(Words).Where(w => !string.IsNullOrEmpty(w)) is var items && items.Any() ? string.Join("; ", items) : null,
            JsonObject fields => fields.Select(f => Words(f.Value)).Where(w => !string.IsNullOrEmpty(w)) is var values && values.Any()
                ? string.Join(" · ", values)
                : null,
            _ => null,
        };

    private static int? Integer(JsonNode? node) =>
        node is JsonValue value && value.TryGetValue<double>(out var number) ? (int)number
        : String(node) is { } text && int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed
        : null;

    private static double? Number(JsonNode? node) =>
        node is JsonValue value && value.TryGetValue<double>(out var number) ? number : null;

    private static bool? Boolean(JsonNode? node) =>
        node is JsonValue value && value.TryGetValue<bool>(out var flag) ? flag : null;

    private static List<string> Strings(JsonNode? node) =>
        node switch
        {
            JsonArray list => list.Select(Words).OfType<string>().ToList(),
            JsonValue => String(node) is { } one ? [one] : [],
            _ => [],
        };

    private static DateTimeOffset? Time(string? text) =>
        DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var time) ? time : null;
}
