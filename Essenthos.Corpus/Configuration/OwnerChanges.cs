using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Essenthos.Core.Configuration;

/// <summary>
/// The owner's change log: one JSON object per line, appended by his console for every change he
/// makes and never rewritten. A line whose <c>needs</c> names a step — <c>load</c>, <c>images</c>,
/// <c>relationships</c>, <c>agent</c> — waits until a later line of section <c>apply</c> says that step
/// <c>succeeded</c>. What waits is not yet in the corpus.
/// </summary>
public static class OwnerChanges
{
    public const string FileName = "owner-changes.jsonl";

    /// <summary>Under the corpus sources, in this project's own folder.</summary>
    public static readonly string DefaultPath = Path.Combine("Essenthos", FileName);

    public const string Apply = "apply";

    public const string Succeeded = "succeeded";

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

    private static string? Text(JsonObject line, string key) =>
        line[key] is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;
}
