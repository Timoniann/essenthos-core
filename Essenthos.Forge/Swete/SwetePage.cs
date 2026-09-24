using System.Reflection;
using System.Text.Json;

namespace Essenthos.Core.Swete;

/// <summary>
/// Words Swete printed and the transcription lost, read back off the printed page — written out one
/// by one in <c>SwetePage.json</c> by <c>scripts/swete-scans.py</c>, each with the volume and page it
/// was read on, and made the way <see cref="SweteRestorations"/> are.
///
/// <para>
/// The page is the Internet Archive's scan of the three volumes (<c>Resources/SweteScans</c>). A word
/// is put back only where Brenton's Greek and GLAUx both read words at a place the transcription has
/// none, and the page, read by the archive's own OCR or by eye from the image where that OCR could not
/// be trusted, prints words there. What goes in is what the page prints, in its spelling — which is
/// sometimes not the word the witnesses read. Where the page prints the verse as the transcription
/// does, the gap is Vaticanus's reading and nothing is changed; where the page could not be read with
/// certainty, nothing is either.
/// </para>
/// </summary>
internal static class SwetePage
{
    private const string Resource = "Essenthos.Core.Swete.SwetePage.json";

    /// <summary>What the text's row says about these words, on a cold load and on a warm one.</summary>
    public const string Note =
        "Modified: words the transcription lost throughout are restored by Essenthos from the printed page "
        + "(the Internet Archive's scans of Swete's volumes), each where Brenton's Greek and the GLAUx treebank "
        + "read words the transcription lacks and the page prints them, in the page's spelling.";

    private static readonly JsonSerializerOptions Shape = new() { PropertyNameCaseInsensitive = true };

    public static readonly IReadOnlyList<SweteRestoration> All = Read();

    private static IReadOnlyList<SweteRestoration> Read()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(Resource)
                           ?? throw new InvalidOperationException($"{Resource} is not embedded in the Forge assembly.");
        var entries = JsonSerializer.Deserialize<List<Entry>>(stream, Shape) ?? [];

        return [.. entries.Select(entry =>
        {
            var digits = entry.Verse.TakeWhile(char.IsAsciiDigit).Count();
            var where = entry.Page.Length > 0 ? $"p. {entry.Page}" : $"scan leaf {entry.Leaf}";
            return new SweteRestoration(
                entry.Book,
                int.Parse(entry.Chapter),
                int.Parse(entry.Verse[..digits]),
                entry.Digitised,
                entry.Printed,
                $"Printed in Swete, vol. {entry.Volume} (Cambridge, {entry.Printing}), {where}; the transcription lost it",
                entry.Verse[digits..]);
        })];
    }

    private sealed record Entry(
        string Book,
        string Chapter,
        string Verse,
        string Digitised,
        string Printed,
        int Volume,
        string Printing,
        string Page,
        int Leaf,
        string Scan);
}
