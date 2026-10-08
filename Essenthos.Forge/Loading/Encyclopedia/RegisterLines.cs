using System.Reflection;
using System.Text.Json;

namespace Essenthos.Core.Loading.Encyclopedia;

/// <summary>
/// The line under a register bearer's name, written for the reader.
///
/// <para>
/// The reading pass that wrote the register described each bearer in a sentence, and for the men no
/// verse of ours prints it added what it could not find (<em>no verse of this set attests him</em>,
/// <em>not in this corpus</em>) and, for namesakes, which other bearer of the list he is not
/// (<em>distinct from item 6</em>). None of that tells a reader anything about the man, so the
/// loader writes the line listed here in its place, for the record the register adds and, once, for
/// the one a corpus loaded earlier already holds.
/// </para>
///
/// <para>
/// A correction applies only while the line is still the register's own: a record whose line has
/// since been rewritten by anything else is left alone.
/// </para>
/// </summary>
internal static class RegisterLines
{
    private const string Resource = "Essenthos.Core.Loading.Encyclopedia.RegisterLines.json";

    private static readonly JsonSerializerOptions Shape = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private static readonly Lazy<IReadOnlyList<RegisterLine>> Shipped = new(Read);

    internal static IReadOnlyList<RegisterLine> All => Shipped.Value;

    /// <summary>The line to write for a bearer whose register description is <paramref name="description"/>.</summary>
    internal static string? Of(string sourceId, string? description) =>
        Shipped.Value.FirstOrDefault(line => line.SourceId == sourceId && line.Was == description)?.Line ?? description;

    private static IReadOnlyList<RegisterLine> Read()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(Resource)
                           ?? throw new InvalidOperationException($"{Resource} is not embedded in the Forge assembly.");
        return JsonSerializer.Deserialize<RegisterLinesFile>(stream, Shape)?.Lines ?? [];
    }
}

/// <summary>The embedded list, as a file.</summary>
internal sealed record RegisterLinesFile(string DecidedBy, string Policy, IReadOnlyList<RegisterLine> Lines);

/// <summary>One bearer by his record's source id, the register's description of him and the line that replaces it.</summary>
internal sealed record RegisterLine(string SourceId, string Was, string Line);
