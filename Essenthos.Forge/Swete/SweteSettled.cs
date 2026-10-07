using System.Reflection;
using System.Text.Json;

namespace Essenthos.Core.Swete;

/// <summary>
/// Verses settled one by one against the printed page — written out in <c>SweteSettled.json</c>, each
/// with the volume, page and scan leaf it was read on — where the transcription took into the text
/// what the page does not print as text, or lost a line the page does.
///
/// <para>
/// Three kinds. A running head read into a verse at a page's turn, standing where the last words of
/// the page were (1 Samuel 8:2). The foot of the page, the apparatus of Alexandrinus that Swete prints
/// under his text, read into the verse that runs over the turn (Judges 18:8, 1 Samuel 11:11). And a
/// chapter's opening line lost with the number printed beside it, so the verse holds the numeral and
/// nothing else (Numbers 17:1 and 19:1), the way Exodus 20:1 did. In each the page is read by the
/// Internet Archive's own OCR of the scan, which owes nothing to the transcription, and what goes in is
/// what the page prints, in the spelling Swete prints elsewhere; Brenton's Greek reads the same words.
/// </para>
///
/// <para>
/// Each is addressed to the verse as <see cref="SweteCorrections"/> leave it, the way
/// <see cref="SwetePage"/>'s are: the figures those take out of Judges 18:8 stood in the apparatus.
/// </para>
/// </summary>
internal static class SweteSettled
{
    private const string Resource = "Essenthos.Core.Swete.SweteSettled.json";

    /// <summary>What the text's row says about these verses, on a cold load and on a warm one.</summary>
    public const string Note =
        "Modified: where the transcription read Swete's running head or lines of his apparatus into the text "
        + "(Judges 18:8, 1 Samuel 8:2 and 11:11), Essenthos reads the words the printed page has there; the "
        + "opening lines of Numbers 17 and 19 and a word of Numbers 16:50, which the transcription lost, are "
        + "restored from the page; and 3 Kingdoms 16:1, which the transcription ran into 15:34, is divided "
        + "where the page begins it.";

    private static readonly JsonSerializerOptions Shape = new() { PropertyNameCaseInsensitive = true };

    public static readonly IReadOnlyList<SweteRestoration> All = Read();

    private static IReadOnlyList<SweteRestoration> Read()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(Resource)
                           ?? throw new InvalidOperationException($"{Resource} is not embedded in the Forge assembly.");
        var entries = JsonSerializer.Deserialize<List<Entry>>(stream, Shape) ?? [];

        return [.. entries.Select(entry => new SweteRestoration(
            entry.Book,
            entry.Chapter,
            entry.Verse,
            entry.Digitised,
            entry.Printed,
            $"Printed in Swete, vol. {entry.Volume} (Cambridge, {entry.Printing}), p. {entry.Page}, scan leaf {entry.Leaf}. {entry.What}"))];
    }

    private sealed record Entry(
        string Book,
        int Chapter,
        int Verse,
        string Digitised,
        string Printed,
        int Volume,
        string Printing,
        string Page,
        int Leaf,
        string Scan,
        string What);
}
