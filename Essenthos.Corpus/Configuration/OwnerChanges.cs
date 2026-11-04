using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Essenthos.Core.Configuration;

/// <summary>
/// The owner's change log: one JSON object per line, appended by his console for every change he
/// makes and never rewritten. A line whose <c>needs</c> names a step — <c>load</c>, <c>images</c>,
/// <c>agent</c> — waits until a later line of section <c>apply</c> says that step <c>succeeded</c>.
/// What waits is not yet in the corpus.
/// </summary>
public static class OwnerChanges
{
    public const string FileName = "owner-changes.jsonl";

    /// <summary>Under the corpus sources, in this project's own folder.</summary>
    public static readonly string DefaultPath = Path.Combine("Essenthos", FileName);

    public const string Apply = "apply";

    public const string Succeeded = "succeeded";

    /// <summary>
    /// The step a decision waits on when it takes effect as an edit of the files the build reads
    /// (DOC-0224 §8.4): an agent applies it with <c>forge owner-apply</c>.
    /// </summary>
    public const string Files = "files";

    /// <summary>The paths of the checkout that hold the owner's decisions, which a release must find committed.</summary>
    public static readonly IReadOnlyList<string> Tracked =
    [
        "Resources/Essenthos",
        "Essenthos.Api/" + SiteSettings.FileName,
        "Essenthos.Forge/Loading/Encyclopedia",
    ];

    /// <summary>How many of the owner's changes wait on each step, from the log at <paramref name="path"/>; empty where there is no log.</summary>
    public static IReadOnlyDictionary<string, int> Waiting(string path)
    {
        var waiting = new Dictionary<string, int>(StringComparer.Ordinal);
        if (!File.Exists(path))
        {
            return waiting;
        }

        var ran = new HashSet<string>(StringComparer.Ordinal);
        foreach (var text in File.ReadAllLines(path, Encoding.UTF8).Reverse())
        {
            if (Parse(text) is not JsonObject line)
            {
                continue;
            }

            if (Text(line, "section") == Apply && Text(line, "after") == Succeeded && Text(line, "action") is { } step)
            {
                ran.Add(step);
            }

            if (Text(line, "needs") is { } needs && !ran.Contains(needs))
            {
                waiting[needs] = waiting.GetValueOrDefault(needs) + 1;
            }
        }

        return waiting;
    }

    /// <summary>The lines still waiting on <paramref name="step"/>, oldest first, each with its line number from 1.</summary>
    public static IReadOnlyList<(int Line, JsonObject Change)> WaitingOn(string path, string step)
    {
        if (!File.Exists(path))
        {
            return [];
        }

        var lines = File.ReadAllLines(path, Encoding.UTF8);
        var waiting = new List<(int, JsonObject)>();
        for (var index = lines.Length - 1; index >= 0; index--)
        {
            if (Parse(lines[index]) is not JsonObject line)
            {
                continue;
            }

            if (Text(line, "section") == Apply && Text(line, "after") == Succeeded && Text(line, "action") == step)
            {
                break;
            }

            if (Text(line, "needs") == step)
            {
                waiting.Add((index + 1, line));
            }
        }

        waiting.Reverse();
        return waiting;
    }

    /// <summary>
    /// Appends the line that says <paramref name="step"/> ran and succeeded, in the shape the console
    /// writes, which clears every earlier line waiting on it.
    /// </summary>
    public static void Applied(string path, string step, string note, DateTimeOffset at)
    {
        var line = new JsonObject
        {
            ["at"] = at.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", System.Globalization.CultureInfo.InvariantCulture),
            ["section"] = Apply,
            ["action"] = step,
            ["target"] = $"step/{step}",
            ["label"] = null,
            ["before"] = null,
            ["after"] = Succeeded,
            ["note"] = note,
            ["needs"] = null,
        };
        var text = line.ToJsonString(new JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
        File.AppendAllText(path, text + "\n", new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    public static string? Text(JsonObject line, string key) =>
        line[key] is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;

    private static JsonNode? Parse(string text)
    {
        if (text.Trim().Length == 0)
        {
            return null;
        }

        try
        {
            return JsonNode.Parse(text);
        }
        catch (JsonException)
        {
            // A line broken by hand is skipped rather than hiding every other change.
            return null;
        }
    }
}
