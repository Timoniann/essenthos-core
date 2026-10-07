namespace Essenthos.Core.Swete;

/// <summary>
/// Verses the transcription ran into the verse before them, opened again where the page shows the
/// division — the way <see cref="SweteIsaiah"/> opens Isaiah's.
///
/// <para>
/// Swete prints every verse number in the margin, and where a verse begins inside a line he prints
/// its number small in the text as well. The transcription let that figure into the text, glued to
/// the word after it, <c>⁸καὶ</c>, or standing on its own, and in twenty-six places did not open the
/// verse, so the file's verse holds two and the address of the second stands empty. The figure names
/// a verse the file has nowhere else, which is the page's own evidence of where it begins; each was
/// also read against Brenton, who begins the verse at the same words or, where he divides the book
/// otherwise, near them.
/// </para>
///
/// <para>
/// Four verses have no figure left, or one only, and are read off the page itself. In Genesis 15
/// (vol. 1, 1901, p. 24) Swete prints 19 before τοὺς Κεναίους, 20 before καὶ τοὺς Χετταίους, and 21
/// before καὶ τοὺς Ἀμορραίους, and the transcription lost the divisions with two of the words — the
/// καὶ before τοὺς Ἀμορραίους and the τοὺς before Εὐαίους — which the page prints and Brenton reads.
/// In Psalm 91 (vol. 2, 1896, p. 337) he prints 16 before τοῦ ἀναγγεῖλαι, where Brenton begins it too,
/// and without it the psalm's last verse is its fifteenth. And 3 Kingdoms 16:1 opens a chapter: the
/// transcription ran its one line into 15:34 and kept only the chapter's number for the verse itself.
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
        "Modified: thirty verse divisions the transcription lost are restored by Essenthos — twenty-six "
        + "where Swete's own verse number stands in the text before the verse's first word, and Genesis 15:19, "
        + "20 and 21 and Psalm 91:16 from the printed page, with the two words the transcription lost in Genesis.";

    private const string Figure = "Swete's own number for the verse stands in the text before the word that opens it";

    private const string Page = "Swete, vol. 1 (Cambridge, 1901), p. 24";

    private const string PsalterPage = "Swete, vol. 2 (Cambridge, 1896), p. 337";

    private const string KingdomsPage = "Swete, vol. 1 (Cambridge, 1901), p. 718";

    private static readonly Dictionary<string, IReadOnlyList<EditionRepair>> Repairs = new()
    {
        ["01.Genesis"] =
        [
            EditionRepair.Divide(15, "18", "τούς Κεναίους", "τούς Κεναίους", "19",
                $"{Page}, prints 19 before these words; Brenton begins 15:19 at them"),
            EditionRepair.Divide(15, "19", "20 καὶ τοὺς Χετταίους", "καὶ τοὺς Χετταίους", "20",
                $"{Figure}; {Page}, prints 20 here"),
            EditionRepair.Divide(15, "20", "τοὺς Ἀμορραίους", "καὶ τοὺς Ἀμορραίους", "21",
                $"{Page}, prints 21 and καὶ before these words; the transcription lost both, and Brenton reads καὶ"),
            EditionRepair.Replace(15, "21", "καὶ Εὑοίους", "καὶ τοὺς Εὑοίους",
                $"{Page}, prints τοὺς before the name, as before every other name of the list; Brenton reads it"),
        ],
        ["12.Regnorum_II"] =
        [
            EditionRepair.Divide(19, "42", "καὶ ἀπεκρίθη ἀνὴρ", "καὶ ἀπεκρίθη ἀνὴρ", "43", Figure),
        ],
        ["13.Regnorum_III"] =
        [
            EditionRepair.DivideInto(15, "34", "καὶ ἐγένετο λόγος Κυρίου", "καὶ ἐγένετο λόγος Κυρίου", 16, "1",
                $"{KingdomsPage}, prints these words on the line its number XVI stands beside, between 15:34 and 16:2; "
                + "the transcription closes 15:34 with that number and holds nothing else in 16:1, and Brenton begins 16:1 at them"),
        ],
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
        ["27.Psalmi"] =
        [
            EditionRepair.Divide(91, "15", "τοῦ ἀναγγεῖλαι", "τοῦ ἀναγγεῖλαι", "16",
                $"{PsalterPage}, prints 16 before these words; Brenton begins 91:16 at them"),
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
