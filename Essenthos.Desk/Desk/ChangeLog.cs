using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Essenthos.Core.Configuration;

namespace Essenthos.Core.Desk;

/// <summary>
/// One change the owner made in his console, as the change log records it.
/// </summary>
/// <param name="Line">Where it stands in the log, from 1, which is stable because the log is only appended to.</param>
/// <param name="At">When, in UTC.</param>
/// <param name="Section">
/// Which part of the console: <c>portraits</c>, <c>pictures</c>, <c>occurrences</c>, <c>records</c>,
/// <c>settings</c>, <c>deploy</c> for a deploy run (logged when it starts and when it ends), or
/// <c>apply</c> for a run. Lines of <c>relationships</c> are from the review of a dataset's
/// relationships the console held until 2026-09-30, and stay in the log as what was decided.
/// </param>
/// <param name="Action">What was done in it: <c>status</c>, <c>brief</c>, <c>upload</c>, <c>decision</c>, …</param>
/// <param name="Target">What it was done to, named the way the corpus is addressed outside itself — a slug, a verse, a file under the images folder — never by a row id.</param>
/// <param name="Label">What it was done to in words, as the owner reads it: a name, not an address. Null where the section names it itself.</param>
/// <param name="Before">What it was, compactly, or null where it was nothing.</param>
/// <param name="After">What it became, or null where it was taken away.</param>
/// <param name="Note">What the owner wrote beside it.</param>
/// <param name="Needs">
/// What has to run before the site shows it: <c>images</c>, <c>load</c>, <c>agent</c> for a change
/// somebody has to make by hand, or null where it took effect when it was saved.
/// </param>
/// <param name="Waiting">Whether what it needs has not run through the console since; read, never stored.</param>
internal sealed record ChangeEntry(
    int Line,
    string At,
    string Section,
    string Action,
    string Target,
    string? Label,
    JsonNode? Before,
    JsonNode? After,
    string? Note,
    string? Needs,
    bool Waiting);

internal sealed record ChangeLogResponse(IReadOnlyList<ChangeEntry> Entries, IReadOnlyDictionary<string, int> Waiting);

/// <summary>
/// Every change the owner makes through the console, one JSON object per line in a tracked file,
/// appended and never rewritten — so that an agent who pulls the checkout can read what he changed,
/// when, and what still has to run before it shows, without reconstructing it from diffs.
///
/// <para>
/// A line is <c>{"at","section","action","target","label","before","after","note","needs"}</c>. A run the
/// console starts is recorded the same way, as section <c>apply</c> with the step it ran as its
/// action and <c>succeeded</c> or <c>failed</c> after, which is how a change that needed that step
/// is known to have had it.
/// </para>
/// </summary>
internal sealed class ChangeLog(DeskPaths paths)
{
    public const string FileName = OwnerChanges.FileName;

    public const string Apply = OwnerChanges.Apply;

    public const string Succeeded = OwnerChanges.Succeeded;

    private static readonly JsonWriterOptions Writing = new()
    {
        Indented = false,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private readonly SemaphoreSlim _gate = new(1, 1);

    public string File => Path.Combine(paths.Owner, FileName);

    public async Task Append(
        string section,
        string action,
        string target,
        JsonNode? before,
        JsonNode? after,
        string? note,
        string? needs,
        string? label = null)
    {
        var line = new JsonObject
        {
            ["at"] = JsonFiles.Now(),
            ["section"] = section,
            ["action"] = action,
            ["target"] = target,
            ["label"] = label,
            ["before"] = before?.DeepClone(),
            ["after"] = after?.DeepClone(),
            ["note"] = string.IsNullOrWhiteSpace(note) ? null : note.Trim(),
            ["needs"] = needs,
        };

        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer, Writing))
        {
            line.WriteTo(writer);
        }

        buffer.WriteByte((byte)'\n');
        await _gate.WaitAsync();
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(File)!);
            await using var stream = new FileStream(File, FileMode.Append, FileAccess.Write, FileShare.Read);
            await stream.WriteAsync(buffer.ToArray());
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Every entry, newest first, each saying whether the step it needs has run since.</summary>
    public ChangeLogResponse Read()
    {
        if (!System.IO.File.Exists(File))
        {
            return new ChangeLogResponse([], new Dictionary<string, int>());
        }

        var lines = System.IO.File.ReadAllLines(File, Encoding.UTF8);
        var entries = new List<ChangeEntry>(lines.Length);
        var ranAfter = new HashSet<string>(StringComparer.Ordinal);
        for (var index = lines.Length - 1; index >= 0; index--)
        {
            if (lines[index].Trim().Length == 0 || Parse(lines[index]) is not JsonObject line)
            {
                continue;
            }

            var section = Text(line, "section") ?? string.Empty;
            var action = Text(line, "action") ?? string.Empty;
            var needs = Text(line, "needs");
            if (section == Apply && Text(line, "after") == Succeeded)
            {
                ranAfter.Add(action);
            }

            entries.Add(new ChangeEntry(
                index + 1,
                Text(line, "at") ?? string.Empty,
                section,
                action,
                Text(line, "target") ?? string.Empty,
                Text(line, "label"),
                line["before"]?.DeepClone(),
                line["after"]?.DeepClone(),
                Text(line, "note"),
                needs,
                needs is not null && !ranAfter.Contains(needs)));
        }

        var waiting = entries.Where(e => e.Waiting).GroupBy(e => e.Needs!).ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);
        return new ChangeLogResponse(entries, waiting);
    }

    private static JsonNode? Parse(string line)
    {
        try
        {
            return JsonNode.Parse(line);
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
