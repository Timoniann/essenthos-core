using System.Reflection;
using System.Text.Json;

namespace Essenthos.Core.Swete;

/// <summary>
/// Verses settled one by one against the printed page — written out in <c>SweteSettled.json</c>, each
/// with the volume, page and scan leaf it was read on — where the transcription took into the text
/// what the page does not print as text, lost what the page prints, or misread a word the page shows.
///
/// <para>
/// Four kinds. A running head read into a verse at a page's turn, standing where the last words of
/// the page were (1 Samuel 8:2). The foot of the page, the apparatus of Alexandrinus that Swete prints
/// under his text, read into the verse that runs over the turn (Judges 18:8, 1 Samuel 11:11). A
/// chapter's opening line lost with the number printed beside it, so the verse holds the numeral and
/// nothing else (Numbers 17:1 and 19:1), the way Exodus 20:1 did. And what the margin prints beside a
/// line — a manuscript's siglum, a letter of the chapter's numeral — read into it as a word or onto
/// the front of one, with the letters the page prints otherwise in the same verses: a word cut short
/// at a line's end, a breathing, a Latin letter for the Greek one beside it (Genesis 11:4,
/// Deuteronomy 21:1, Malachi 2:12). In each what goes in is what the page prints, read off the scan
/// by the Internet Archive's own OCR or a reading of the image that owes nothing to the transcription.
/// </para>
///
/// <para>
/// Each is addressed to the verse as <see cref="SweteCorrections"/> leave it, the way
/// <see cref="SwetePage"/>'s are: the figures those take out of Judges 18:8 stood in the apparatus.
/// An entry marked <c>sameWord</c> puts a word's letters right, one word for one, so a corpus that
/// already holds the verse keeps the word's row.
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

    /// <summary>What the text's row says about the verses read off the page for what its margin let in.</summary>
    public const string MarginNote =
        "Modified: letters of the margin the transcription read into nine verses (Genesis 11:4, Deuteronomy 21:1, "
        + "1 Samuel 25:28, Proverbs 22:17, Wisdom 12:10 and 14:19, Malachi 2:12 and 3:17, Ezekiel 34:12) are "
        + "taken out by Essenthos, and the words of those verses it cut short or misread are written as the "
        + "printed page has them.";

    private static readonly JsonSerializerOptions Shape = new() { PropertyNameCaseInsensitive = true };

    private static readonly IReadOnlyList<Entry> Entries = Read();

    public static readonly IReadOnlyList<SweteRestoration> All = [.. Entries.Select(Restoration)];

    /// <summary>The verses the first pass settled, before the margin's letters were read off the page.</summary>
    public static readonly IReadOnlyList<SweteRestoration> First =
        [.. Entries.Where(entry => entry.Pass == 1).Select(Restoration)];

    private static IReadOnlyList<Entry> Read()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(Resource)
                           ?? throw new InvalidOperationException($"{Resource} is not embedded in the Forge assembly.");
        return JsonSerializer.Deserialize<List<Entry>>(stream, Shape) ?? [];
    }

    private static SweteRestoration Restoration(Entry entry) => new(
        entry.Book,
        entry.Chapter,
        entry.Verse,
        entry.Digitised,
        entry.Printed,
        $"Printed in Swete, vol. {entry.Volume} ({Printing(entry)}), p. {entry.Page}, scan leaf {entry.Leaf} of {entry.Scan}. {entry.What}",
        SameWord: entry.SameWord);

    /// <summary>Where the scan's printing is not stated, the scan's name and leaf are what identify the page.</summary>
    private static string Printing(Entry entry) =>
        entry.Printing is { Length: > 0 } year ? $"Cambridge, {year}" : "Cambridge";

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
        string What,
        bool SameWord = false,
        int Pass = 1);
}
