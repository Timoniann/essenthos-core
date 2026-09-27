namespace Essenthos.Core.Swete;

/// <summary>
/// Verses the transcription ran into the verse before them, opened again where the page shows the
/// division — the way <see cref="SweteIsaiah"/> opens Isaiah's.
///
/// <para>
/// Swete prints every verse number in the margin, and where a verse begins inside a line he prints
/// its number small in the text as well. The transcription let that figure into the text, glued to
/// the word after it, <c>⁸καὶ</c>, or standing on its own, and in twenty-five places did not open the
/// verse, so the file's verse holds two and the address of the second stands empty. The figure names
/// a verse the file has nowhere else, which is the page's own evidence of where it begins; each was
/// also read against Brenton, who begins the verse at the same words or, where he divides the book
/// otherwise, near them.
/// </para>
///
/// <para>
/// Made on the file's lines before anything else, so that the words' other corrections are addressed
/// to the verse the words stand in once it is opened. A corpus loaded before these holds the verses
/// run together, and has to load the text again to have them.
/// </para>
/// </summary>
internal static class SweteDivisions
{
    /// <summary>What the text's row says about these divisions.</summary>
    public const string Note =
        "Modified: twenty-five verse divisions the transcription lost are restored by Essenthos where Swete's "
        + "own verse number stands in the text before the verse's first word.";

    private const string Figure = "Swete's own number for the verse stands in the text before the word that opens it";

    private static readonly Dictionary<string, IReadOnlyList<EditionRepair>> Repairs = new()
    {
        ["15.Paralipomenon_I"] =
        [
            EditionRepair.Divide(12, "7", "⁸καὶ", "καὶ", "8", Figure),
            EditionRepair.Divide(20, "2", "³καὶ", "καὶ", "3", Figure),
            EditionRepair.Replace(24, "23", "τέταρτος.²⁴Οζειὴλ", "τέταρτος. Οζειὴλ", Figure),
            EditionRepair.Divide(24, "23", "Οζειὴλ", "Οζειὴλ", "24", Figure),
            EditionRepair.Divide(27, "6", "⁷ὁ", "ὁ", "7", Figure),
            EditionRepair.Divide(28, "2", "³καὶ", "καὶ", "3", Figure),
        ],
        ["16.Paralipomenon_II"] =
        [
            EditionRepair.Divide(11, "2", "3Εἰπὸν", "Εἰπὸν", "3", Figure),
            EditionRepair.Divide(11, "15", "¹⁶καὶ", "καὶ", "16", Figure),
        ],
        ["17.Esdras_A"] =
        [
            EditionRepair.Divide(4, "18", "¹⁹καὶ", "καὶ", "19", Figure),
        ],
        ["18.Esdras_B"] =
        [
            EditionRepair.Divide(2, "21", "²²υἱοὶ", "υἱοὶ", "22", Figure),
            EditionRepair.Divide(5, "4", "⁵καὶ", "καὶ", "5", Figure),
            EditionRepair.Divide(6, "19", "20ὅτι", "ὅτι", "20", Figure),
            EditionRepair.Divide(10, "43", "⁴⁴πάντες", "πάντες", "44", Figure),
            EditionRepair.Divide(14, "10", "¹¹καὶ", "καὶ", "11", Figure),
            EditionRepair.Divide(18, "2", "³καὶ", "καὶ", "3", Figure),
        ],
        ["21.Tobias"] =
        [
            EditionRepair.Divide(1, "7", "⁸καὶ", "καὶ", "8", Figure),
        ],
        ["23.Machabaeorum_i"] =
        [
            EditionRepair.Divide(4, "7", "⁸καὶ", "καὶ", "8", Figure),
        ],
        ["24.Machabaeorum_ii"] =
        [
            EditionRepair.Divide(11, "29", "30τοῖς", "τοῖς", "30", Figure),
        ],
        ["26.Machabaeorum_iv"] =
        [
            EditionRepair.Divide(1, "16", "¹⁷αὕτη", "αὕτη", "17", Figure),
            EditionRepair.Divide(7, "16", "¹⁷ἴσως", "ἴσως", "17", Figure),
        ],
        ["44.Sophonias"] =
        [
            EditionRepair.Divide(3, "12", "¹³οἱ", "οἱ", "13", Figure),
        ],
        ["49.Jeremias"] =
        [
            EditionRepair.Divide(52, "8", "⁹καὶ", "καὶ", "9", Figure),
        ],
        ["53.Ezechiel"] =
        [
            EditionRepair.Divide(7, "9", "¹0᾿δοὺ", "Ἰδοὺ", "10",
                $"{Figure}, over the capital whose breathing is left; Ἰδοὺ as the book prints it thirty-one times"),
            EditionRepair.Divide(8, "1", "²καὶ", "καὶ", "2", Figure),
            EditionRepair.Divide(23, "2", "3καὶ", "καὶ", "3", Figure),
        ],
        ["57.Daniel_Theodotionis_versio"] =
        [
            EditionRepair.Divide(10, "7", "8", "", "8", Figure),
        ],
    };

    /// <summary>A file's lines with its lost divisions opened; any other book's pass through unchanged.</summary>
    public static IEnumerable<string> Lines(string book, IEnumerable<string> lines) =>
        Repairs.TryGetValue(book, out var repairs) ? EditionRepairs.Apply(book, lines, repairs) : lines;
}
