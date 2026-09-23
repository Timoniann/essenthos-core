using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Essenthos.Core.Desk;

/// <summary>
/// The tracked files a decision is written into, read and written back as they were written: the
/// same indentation, the same line endings, the short lists kept on one line where the file keeps
/// them so, and no escaped Cyrillic or Hebrew, so that the diff a decision makes is the decision and
/// nothing else.
/// </summary>
internal static partial class JsonFiles
{
    private const string Scalar = @"(?:""(?:[^""\\]|\\.)*""|-?[0-9][0-9.eE+-]*|true|false|null)";

    /// <summary>A property holding a non-empty array of plain values, written on one line.</summary>
    [GeneratedRegex(@"""(?<name>[^""\\]+)"": \[" + Scalar + @"(?:, " + Scalar + @")*\]")]
    private static partial Regex InlineArrays();

    /// <summary>A property holding a non-empty array of plain values, one to a line as the writer puts them.</summary>
    [GeneratedRegex(@"""(?<name>[^""\\]+)"": \[(?:\r?\n[ ]+(?<item>" + Scalar + @"),?)+\r?\n[ ]+\]")]
    private static partial Regex ScalarArrays();

    private static readonly JsonDocumentOptions Reading = new()
    {
        AllowTrailingCommas = true,
        CommentHandling = JsonCommentHandling.Skip,
    };

    /// <summary>One writer per file, so two decisions saved together cannot overwrite each other.</summary>
    private static readonly Dictionary<string, SemaphoreSlim> Locks = new(StringComparer.OrdinalIgnoreCase);

    public static JsonNode Read(string path)
    {
        using var stream = File.OpenRead(path);
        return JsonNode.Parse(stream, documentOptions: Reading)
               ?? throw new InvalidDataException($"{path} holds no JSON. Restore it from git before deciding anything in it.");
    }

    /// <summary>
    /// Reads the file, lets <paramref name="change"/> alter it, and writes it back if the change
    /// says it changed anything, holding the file's lock throughout.
    /// </summary>
    public static async Task<T> Change<T>(string path, Func<JsonNode, (bool Changed, T Result)> change)
    {
        var gate = Gate(path);
        await gate.WaitAsync();
        try
        {
            var node = Read(path);
            var (changed, result) = change(node);
            if (changed)
            {
                Write(path, node);
            }

            return result;
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>
    /// Writes <paramref name="node"/> in the layout the file already has — or two-space indentation
    /// and the platform's line ending for a new one — through a temporary file, so a reader never
    /// finds half of it.
    /// </summary>
    public static void Write(string path, JsonNode node)
    {
        var (indent, newLine, trailing, inline) = Layout(path);
        var options = new JsonWriterOptions
        {
            Indented = true,
            IndentSize = indent,
            NewLine = newLine,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };

        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer, options))
        {
            node.WriteTo(writer);
        }

        var text = Encoding.UTF8.GetString(buffer.ToArray());
        if (inline.Count > 0)
        {
            text = ScalarArrays().Replace(text, array => inline.Contains(array.Groups["name"].Value)
                ? $"\"{array.Groups["name"].Value}\": [{string.Join(", ", array.Groups["item"].Captures.Select(c => c.Value))}]"
                : array.Value);
        }

        var temporary = path + ".writing";
        File.WriteAllText(temporary, trailing ? text + newLine : text, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        File.Move(temporary, path, overwrite: true);
    }

    /// <summary>
    /// How the file is laid out: its indentation, its line ending, whether it ends in one, and the
    /// properties whose arrays of plain values it writes on one line, which a hand-written list does
    /// and the writer does not.
    /// </summary>
    private static (int Indent, string NewLine, bool Trailing, HashSet<string> Inline) Layout(string path)
    {
        if (!File.Exists(path))
        {
            return (2, Environment.NewLine, true, []);
        }

        var text = File.ReadAllText(path);
        var newLine = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var second = text.Split('\n').Skip(1).FirstOrDefault() ?? string.Empty;
        var indent = second.TakeWhile(c => c == ' ').Count();
        var inline = InlineArrays().Matches(text).Select(m => m.Groups["name"].Value).ToHashSet(StringComparer.Ordinal);
        return (indent is > 0 and <= 8 ? indent : 2, newLine, text.EndsWith('\n'), inline);
    }

    private static SemaphoreSlim Gate(string path)
    {
        lock (Locks)
        {
            var key = Path.GetFullPath(path);
            if (!Locks.TryGetValue(key, out var gate))
            {
                gate = new SemaphoreSlim(1, 1);
                Locks[key] = gate;
            }

            return gate;
        }
    }

    /// <summary>The date a decision is stamped with, in the form the record files already use.</summary>
    public static string Today() => DateTime.UtcNow.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>The moment a decision was taken, as the review page stamped its own.</summary>
    public static string Now() => DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", System.Globalization.CultureInfo.InvariantCulture);
}
