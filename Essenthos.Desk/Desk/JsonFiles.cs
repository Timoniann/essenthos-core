using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Essenthos.Core.Desk;

/// <summary>
/// The tracked files a decision is written into, read and written back as they were written: the
/// same indentation, the same line endings and no escaped Cyrillic or Hebrew, so that the diff a
/// decision makes is the decision and nothing else.
/// </summary>
internal static class JsonFiles
{
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
        var (indent, newLine, trailing) = Layout(path);
        var options = new JsonWriterOptions
        {
            Indented = true,
            IndentSize = indent,
            NewLine = newLine,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };

        var temporary = path + ".writing";
        using (var stream = File.Create(temporary))
        {
            using (var writer = new Utf8JsonWriter(stream, options))
            {
                node.WriteTo(writer);
            }

            if (trailing)
            {
                stream.Write(Encoding.UTF8.GetBytes(newLine));
            }
        }

        File.Move(temporary, path, overwrite: true);
    }

    private static (int Indent, string NewLine, bool Trailing) Layout(string path)
    {
        if (!File.Exists(path))
        {
            return (2, Environment.NewLine, true);
        }

        var text = File.ReadAllText(path);
        var newLine = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var second = text.Split('\n').Skip(1).FirstOrDefault() ?? string.Empty;
        var indent = second.TakeWhile(c => c == ' ').Count();
        return (indent is > 0 and <= 8 ? indent : 2, newLine, text.EndsWith('\n'));
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
