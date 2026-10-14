using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Essenthos.Core.Desk;

/// <param name="Language">The language the persona thinks, writes and reports in.</param>
internal sealed record VisitorPersona(string Id, string? Name, string? Summary, string? Language);

/// <param name="Kind"><c>task</c>, with a target to reach, or <c>explore</c>, with none.</param>
/// <param name="Landing">Where the visitor was put down: the home page, or a page a search would have led to.</param>
/// <param name="Queries">What such a person would have typed into a search engine to arrive there.</param>
internal sealed record VisitorGoal(string Id, string Kind, string? Text, string? Landing, IReadOnlyList<string> Queries);

internal sealed record VisitorScreen(int Width, int Height);

/// <param name="Tokens">What the visitor's agent spent, as its runner reported it.</param>
internal sealed record VisitorUsage(long? Tokens, int? ToolUses, long? DurationMs);

/// <param name="Kind"><c>delight</c>, <c>interest</c>, <c>neutral</c>, <c>confused</c>, <c>frustrated</c>, <c>annoyed</c> or <c>bored</c>.</param>
/// <param name="Strength">1 to 3, or null where none was given.</param>
/// <param name="Words">The persona's own few words for it.</param>
internal sealed record VisitorFeeling(string Kind, int? Strength, string? Words);

/// <param name="Step">The step the visitor felt it at.</param>
internal sealed record VisitorFeelingAt(int Step, VisitorFeeling Feeling);

/// <param name="Dominant">The feeling the visitor gave most often.</param>
/// <param name="Low">The worst moment: the most negative feeling at its strongest, or null where there was none.</param>
internal sealed record VisitorMood(string Dominant, VisitorFeelingAt? Low);

/// <param name="Minutes">The time the visitor was given.</param>
/// <param name="MinutesUsed">How long it actually took, from its start to its last step.</param>
/// <param name="Steps">How many steps it took after landing.</param>
/// <param name="EndedBy"><c>goal</c>, <c>gave-up</c>, <c>bored</c>, <c>time</c>, <c>steps</c>, <c>error</c>, or null while it is still going.</param>
/// <param name="Reached">Whether it reached its goal; null while it is still going, and for an exploration, which has none.</param>
/// <param name="Viewport">What the window left the page, which the screenshots are the size of.</param>
/// <param name="Usage">What the visitor's agent spent, where its runner recorded it.</param>
/// <param name="Mood">How the visitor felt across its steps, where it said; not read for the list of runs.</param>
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
    bool? Reached,
    VisitorScreen? Viewport,
    VisitorUsage? Usage,
    VisitorMood? Mood);

/// <param name="Outcomes">How many sessions ended each way, by <c>endedBy</c>; <c>running</c> for one still going.</param>
/// <param name="Aggregate">Whether the run's findings across its sessions have been written up.</param>
internal sealed record VisitorRun(
    string Id,
    string? StartedAt,
    string? Note,
    int Sessions,
    int Reached,
    IReadOnlyDictionary<string, int> Outcomes,
    bool Aggregate);

/// <param name="Markdown">The aggregate as its author wrote it.</param>
internal sealed record VisitorAggregateResponse(string Run, string Markdown);

internal sealed record VisitorRunsResponse(IReadOnlyList<VisitorRun> Runs);

internal sealed record VisitorRunResponse(VisitorRun Run, IReadOnlyList<VisitorSession> Sessions);

internal sealed record VisitorTarget(string? Ref, string? Text, string? Role);

internal sealed record VisitorBox(double X, double Y, double Width, double Height);

/// <param name="X">Where in the viewport the pointer went down, in the screenshots' pixels.</param>
/// <param name="PageX">The same point measured from the top of the page.</param>
/// <param name="Box">The element clicked, in the screenshots' pixels.</param>
internal sealed record VisitorClick(double? X, double? Y, double? PageX, double? PageY, VisitorBox? Box);

/// <param name="Before">How far down the page was scrolled before the action, in pixels.</param>
/// <param name="After">And after it.</param>
internal sealed record VisitorScroll(double? Before, double? After, double? PageHeight, double? ViewportHeight);

/// <param name="Screenshot">The picture's file name beside the step, or null where none was taken.</param>
/// <param name="Blocked">What the browser refused at this step, in words, or null.</param>
/// <param name="From">The address before the action; <c>Url</c> is the one after it.</param>
/// <param name="LoadMs">How long the page the action led to took to load, where it led to one.</param>
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
    string? Blocked,
    VisitorFeeling? Feeling,
    VisitorClick? Click,
    VisitorScroll? Scroll,
    string? From,
    double? LoadMs);

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

    /// <summary>The findings across a run's sessions, written once the run is over.</summary>
    public const string AggregateFile = "aggregate.md";

    /// <summary>
    /// Where each feeling sits between delight and frustration, so the worst moment of a visit can
    /// be told from the rest. A feeling not listed counts as neutral.
    /// </summary>
    private static readonly Dictionary<string, int> Valence = new(StringComparer.Ordinal)
    {
        ["delight"] = 3,
        ["interest"] = 2,
        ["neutral"] = 0,
        ["bored"] = -1,
        ["confused"] = -1,
        ["annoyed"] = -2,
        ["frustrated"] = -3,
    };

    /// <summary>The strength counted for a feeling given without one: the middle of 1 to 3.</summary>
    private const int UnsaidStrength = 2;

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
            .Select(id => Summary(id!, Sessions(id!, withMood: false)))
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

        var sessions = Sessions(run, withMood: true);
        return new VisitorRunResponse(Summary(run, sessions), sessions);
    }

    public VisitorSessionResponse? Session(string run, string session)
    {
        if (Folder(run, session) is not { } folder)
        {
            return null;
        }

        var steps = Steps(folder);
        if (ReadSession(session, folder, steps) is not { } read)
        {
            return null;
        }

        var dossier = Path.Combine(folder, DossierFile);
        return new VisitorSessionResponse(
            run,
            read,
            File.Exists(dossier) ? Text(dossier) : null,
            steps,
            Read(Path.Combine(folder, ReportFile)) is JsonObject report ? Report(report) : null);
    }

    /// <summary>The run's aggregate as written, or null where the run or its aggregate is not there.</summary>
    public VisitorAggregateResponse? Aggregate(string run)
    {
        if (Folder(run) is not { } folder)
        {
            return null;
        }

        var path = Path.Combine(folder, AggregateFile);
        return File.Exists(path) ? new VisitorAggregateResponse(run, Text(path)) : null;
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

    /// <param name="withMood">Whether to read every step for the sessions' feelings, which the list of runs does not show.</param>
    private List<VisitorSession> Sessions(string run, bool withMood)
    {
        var folder = Path.Combine(Root, run);
        return Directory.EnumerateDirectories(folder)
            .Where(d => IsSegment(Path.GetFileName(d)))
            .Order(StringComparer.Ordinal)
            .Select(d => ReadSession(Path.GetFileName(d), d, withMood ? Steps(d) : null))
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
            outcomes,
            File.Exists(Path.Combine(Root, id, AggregateFile)));
    }

    /// <param name="steps">The session's steps where they were read, for its mood; null leaves the mood out.</param>
    private VisitorSession? ReadSession(string id, string folder, IReadOnlyList<VisitorStep>? steps = null)
    {
        if (Read(Path.Combine(folder, SessionFile)) is not JsonObject session)
        {
            return null;
        }

        var files = StepFiles(folder);
        var last = files.Count > 0 ? Read(files[^1]) as JsonObject : null;
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
                ? new VisitorPersona(
                    String(persona["id"]) ?? "", String(persona["name"]), String(persona["summary"]), String(persona["language"]))
                : null,
            goal is not null
                ? new VisitorGoal(String(goal["id"]) ?? "", kind, String(goal["text"]), String(goal["landing"]), Strings(goal["queries"]))
                : null,
            Screen(session["screen"]),
            String(session["language"]),
            Integer(session["minutes"]),
            MinutesUsed(startedAt, endedAt ?? String(last?["at"]), Number(last?["elapsedSeconds"])),
            startedAt,
            endedAt,
            Integer(outcome?["steps"]) ?? Math.Max(files.Count - 1, 0),
            endedBy,
            endedBy is null || kind == Explore ? null : Boolean(outcome?["reached"]) ?? endedBy == GoalReached,
            Screen(session["viewport"]),
            session["usage"] is JsonObject usage ? Usage(usage) : null,
            steps is null ? null : Mood(steps));
    }

    private static VisitorScreen? Screen(JsonNode? node) =>
        node is JsonObject screen && Integer(screen["width"]) is { } width && Integer(screen["height"]) is { } height
            ? new VisitorScreen(width, height)
            : null;

    private static VisitorUsage? Usage(JsonObject usage)
    {
        var read = new VisitorUsage(Whole(usage["tokens"]), Integer(usage["toolUses"]), Whole(usage["durationMs"]));
        return read is { Tokens: null, ToolUses: null, DurationMs: null } ? null : read;
    }

    /// <summary>
    /// The feeling given most often, ties going to the one felt more strongly, and the worst moment:
    /// the most negative feeling by its strength, the earliest where two are as bad. Null where the
    /// visitor named no feeling at all, as every visit recorded before feelings were asked for.
    /// </summary>
    public static VisitorMood? Mood(IReadOnlyList<VisitorStep> steps)
    {
        var felt = steps.Where(s => s.Feeling is not null).Select(s => (s.N, Feeling: s.Feeling!)).ToList();
        if (felt.Count == 0)
        {
            return null;
        }

        var dominant = felt
            .GroupBy(f => f.Feeling.Kind, StringComparer.Ordinal)
            .OrderByDescending(g => g.Count())
            .ThenByDescending(g => g.Sum(f => f.Feeling.Strength ?? UnsaidStrength))
            .ThenBy(g => g.Min(f => f.N))
            .First()
            .Key;
        var low = felt
            .Select(f => (f.N, f.Feeling, Score: ValenceOf(f.Feeling.Kind) * (f.Feeling.Strength ?? UnsaidStrength)))
            .Where(f => f.Score < 0)
            .OrderBy(f => f.Score)
            .ThenBy(f => f.N)
            .Select(f => new VisitorFeelingAt(f.N, f.Feeling))
            .FirstOrDefault();
        return new VisitorMood(dominant, low);
    }

    private static int ValenceOf(string kind) => Valence.GetValueOrDefault(kind);

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
            Words(step["blocked"]),
            Feeling(step["feeling"]),
            Click(step),
            step["scroll"] is JsonObject scroll
                ? new VisitorScroll(Number(scroll["before"]), Number(scroll["after"]), Number(scroll["pageHeight"]), Number(scroll["viewportHeight"]))
                : null,
            String(step["from"]),
            Number((step["timing"] as JsonObject)?["loadMs"]));
    }

    /// <summary>A feeling as the visit command writes it, or as <c>kind:strength</c> where it was left in words.</summary>
    private static VisitorFeeling? Feeling(JsonNode? node)
    {
        switch (node)
        {
            case JsonObject feeling when String(feeling["kind"]) is { Length: > 0 } kind:
                return new VisitorFeeling(kind.Trim().ToLowerInvariant(), Strength(Integer(feeling["strength"])), String(feeling["words"]));
            case JsonValue when String(node) is { Length: > 0 } text:
                var parts = text.Split(':', 2, StringSplitOptions.TrimEntries);
                return new VisitorFeeling(
                    parts[0].ToLowerInvariant(),
                    parts.Length > 1 && int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var strength) ? Strength(strength) : null,
                    null);
            default:
                return null;
        }
    }

    private static int? Strength(int? strength) => strength is { } given ? Math.Clamp(given, 1, 3) : null;

    /// <summary>Where the pointer went down; the box and the page point are read beside it too, where they were written there.</summary>
    private static VisitorClick? Click(JsonObject step)
    {
        if (step["click"] is not JsonObject click)
        {
            return null;
        }

        var box = (click["box"] ?? step["box"]) is JsonObject b
            && Number(b["x"]) is { } x && Number(b["y"]) is { } y && Number(b["width"]) is { } width && Number(b["height"]) is { } height
                ? new VisitorBox(x, y, width, height)
                : null;
        var read = new VisitorClick(
            Number(click["x"]),
            Number(click["y"]),
            Number(click["pageX"] ?? step["pageX"]),
            Number(click["pageY"] ?? step["pageY"]),
            box);
        return read is { X: null, Y: null, Box: null } ? null : read;
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

    private static long? Whole(JsonNode? node) =>
        node is JsonValue value && value.TryGetValue<double>(out var number) ? (long)number : null;

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
