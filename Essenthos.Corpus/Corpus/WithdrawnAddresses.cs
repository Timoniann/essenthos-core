using System.Text.Json;

namespace Essenthos.Core.Corpus;

/// <summary>
/// The addresses of records withdrawn from the encyclopedia, and where each now leads.
///
/// <para>
/// A withdrawn record leaves no row behind, but its page was crawled and linked while it stood, so
/// the address is still asked for. Where the word or the verses it stood for are held by another
/// record (<em>the children of Gibbar</em> on the man Gibbar, not on a place of that name), the old
/// address leads there, as a folded record's does; where nothing took its place it is gone.
/// </para>
///
/// <para>
/// The list is <c>WithdrawnRecords.json</c>, embedded here so that the Forge, which withdraws the
/// records, and the API, which answers their addresses, read the same file.
/// </para>
/// </summary>
internal static class WithdrawnAddresses
{
    /// <summary>The embedded list's resource name.</summary>
    public const string Resource = "WithdrawnRecords.json";

    private static readonly Lazy<IReadOnlyDictionary<string, string?>> Listed = new(Read);

    /// <summary>The list as it is embedded, for a reader that needs more of it than the addresses.</summary>
    public static Stream Open() =>
        typeof(WithdrawnAddresses).Assembly.GetManifestResourceStream(Resource)
        ?? throw new InvalidOperationException($"{Resource} is not embedded in the Corpus assembly.");

    /// <summary>Whether this was the address of a record the encyclopedia withdrew.</summary>
    public static bool IsWithdrawn(string slug) => Listed.Value.ContainsKey(slug);

    /// <summary>The record a withdrawn address now leads to, or null where it leads nowhere or was never withdrawn.</summary>
    public static string? LeadsTo(string slug) => Listed.Value.GetValueOrDefault(slug);

    private static IReadOnlyDictionary<string, string?> Read()
    {
        using var stream = Open();
        using var document = JsonDocument.Parse(stream);
        var addresses = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var record in document.RootElement.GetProperty("records").EnumerateArray())
        {
            addresses[record.GetProperty("slug").GetString()!] =
                record.TryGetProperty("leadsTo", out var leads) ? leads.GetString() : null;
        }

        return addresses;
    }
}
