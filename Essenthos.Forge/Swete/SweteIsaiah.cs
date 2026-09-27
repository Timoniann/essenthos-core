namespace Essenthos.Core.Swete;

/// <summary>
/// Swete's Isaiah, read from First1KGreek's TEI because the one-token-per-line file of the same
/// number holds Ottley's Codex Alexandrinus instead (see <see cref="Loading.SweteTextSource.NotLoaded"/>).
///
/// <para>
/// The transcription is First1KGreek's reading of volume 3 of 1905, and in Isaiah it loses verse
/// divisions rather than words. Swete numbers every verse in the margin; in eleven places the
/// transcription let the figure into the text, glued to the next word — <c>20oὐ</c>, <c>¹¹σὺ</c>,
/// <c>8Νῦν</c> — and did not open the verse, so two verses read as one. Those are divided where the
/// figure stands, which is the page's own evidence. Twice there is no figure left, and the verse is
/// divided where Brenton and Ottley both begin it. It gives the end of 31:9 a division of its own,
/// named <c>head</c>, at the top of chapter 32, and it numbers 38:16 and 38:17 as 15 and 16 and
/// then runs on to 18.
/// </para>
///
/// <para>
/// Letters are left as the transcription reads them — ΙΙλίνθοι, εὐφρρσύνην, κἵλου — for the reason
/// <see cref="SweteRestorations"/> gives. What is changed is a Latin letter standing for the Greek
/// one it looks exactly like, <c>Aἴγυπτον</c> for Αἴγυπτον: the page shows the same glyph either
/// way, and the Latin one makes the word unsearchable and unlinkable while changing nothing a reader
/// sees.
/// </para>
/// </summary>
internal static class SweteIsaiah
{
    /// <summary>The TEI files this project keeps beside the edition, under names no fetch writes.</summary>
    public const string Folder = "First1KGreek";

    /// <summary>First1KGreek's <c>tlg0527.tlg048.1st1K-grc1.xml</c>, at the commit the licence notes name.</summary>
    public const string File = "isaiah-swete-1905.xml";

    /// <summary>The work's number in the TLG catalogue of the Septuagint.</summary>
    public const int Work = 48;

    /// <summary>What the text's row says about this book.</summary>
    public const string Note =
        "Isaiah is read from First1KGreek's own encoding of volume 3, where the machine-readable edition "
        + "holds Ottley's Codex Alexandrinus under Swete's name. Thirteen verses its transcription ran into "
        + "the verse before are divided again by Essenthos — eleven at the verse number Swete prints, which "
        + "the transcription let into the text, two where Brenton and Ottley both begin the verse — two "
        + "misnumbered verses of chapter 38 are renumbered, the end of 31:9 is given back to it, and Latin "
        + "letters standing for the Greek ones they look like are written as Greek; a verse number standing in "
        + "the text at 11:16 is taken out, and a figure written for the breathing of Ἀμὼς at 13:1 is the breathing.";

    private const string Figure = "Swete's own verse number stands in the text before the word that opens the verse";

    private const string BothBegin = "No number is left; Brenton and Ottley both begin the verse at these words";

    private const string Latin = "A Latin letter for the Greek one it looks exactly like";

    public static IReadOnlyList<string> Lines(string folder) =>
        EditionRepairs.Apply(File, First1KGreekReader.Lines(Path.Combine(folder, Folder, File), Work), Repairs);

    public static readonly IReadOnlyList<EditionRepair> Repairs =
    [
        EditionRepair.Replace(1, "1", "OPAΣΙΣ", "ΟΡΑΣΙΣ", Latin),
        EditionRepair.Replace(5, "8", "Oὑαὶ", "Οὑαὶ", Latin),
        EditionRepair.Replace(5, "20", "Oὑαὶ", "Οὑαὶ", Latin),
        EditionRepair.Replace(7, "11", "Aἴτησαι", "Αἴτησαι", Latin),
        EditionRepair.Replace(9, "1", "Nεφιθαλείμ,", "Νεφιθαλείμ,", Latin),
        EditionRepair.Divide(9, "9", "10 ΙΙλίνθοι", "ΙΙλίνθοι", "10", Figure),
        EditionRepair.Replace(10, "26", "Aἴγυπτον.", "Αἴγυπτον.", Latin),
        EditionRepair.Replace(11, "16", "¹6", "", "Swete's own number for the verse, standing in the text before it"),
        EditionRepair.Replace(13, "1", "¹Αμὼς", "Ἀμὼς",
            "A figure where the name's breathing stands; the book prints Ἀμὼς five times"),
        EditionRepair.Divide(13, "19", "20oὐ", "οὐ", "20", $"{Figure}, whose omicron is a Latin o"),
        EditionRepair.Replace(19, "1", "Aἴγυπτον,", "Αἴγυπτον,", Latin),
        EditionRepair.Replace(19, "4", "Aἴγυπτον", "Αἴγυπτον", Latin),
        EditionRepair.Replace(19, "13", "Aἴγυπτον", "Αἴγυπτον", Latin),
        EditionRepair.Replace(19, "14", "Aἴγυπτον", "Αἴγυπτον", Latin),
        EditionRepair.Replace(19, "23", "Tῇ", "Τῇ", Latin),
        EditionRepair.Divide(22, "23", "24καὶ", "καὶ", "24", Figure),
        EditionRepair.Divide(22, "24", "²⁵τῇ", "τῇ", "25", Figure),
        EditionRepair.Renumber(32, "head", 31, "9",
            "The transcription heads chapter 32 with an unnumbered division that is the end of 31:9, "
            + "as Brenton and Ottley print it; the 1 of 32:1 is glued to its last word"),
        EditionRepair.Replace(31, "9", "1Ἰερουσαλήμ.", "Ἰερουσαλήμ.",
            "The number of 32:1, glued to the last word of 31:9"),
        EditionRepair.Divide(35, "3", "παρακαλέσατε,", "παρακαλέσατε,", "4", BothBegin),
        EditionRepair.Replace(36, "6", "Aἴγυπτον·", "Αἴγυπτον·", Latin),
        EditionRepair.Divide(37, "10", "¹¹σὺ", "σὺ", "11", Figure),
        EditionRepair.Divide(37, "14", "15καὶ", "καὶ", "15", Figure),
        EditionRepair.Renumber(38, "16", 38, "17",
            "The transcription numbers 38:14, 15, 16, 18: its 16 is the verse Brenton and Ottley number 17"),
        EditionRepair.Renumber(38, "15", 38, "16",
            "Its 15 is the verse Brenton and Ottley number 16; Ottley, printed at Cambridge in the same "
            + "years, gives 15 no verse, its words standing at the end of 14 as they do here"),
        EditionRepair.Divide(43, "18", "¹9ἰδοὺ", "ἰδοὺ", "19", Figure),
        EditionRepair.Divide(47, "7", "8Νῦν", "Νῦν", "8", Figure),
        EditionRepair.Replace(48, "20", "20Ἔξελθε", "Ἔξελθε", "The verse's own number, glued to its first word"),
        EditionRepair.Divide(49, "12", "¹³εὐφραίνεσθε,", "εὐφραίνεσθε,", "13", Figure),
        EditionRepair.Divide(52, "2", "ὅτι τάδε", "ὅτι τάδε", "3", BothBegin),
        EditionRepair.Replace(60, "8", "oἵδε", "οἵδε", Latin),
        EditionRepair.Divide(65, "20", "²¹καὶ", "καὶ", "21", Figure),
        EditionRepair.Divide(65, "21", "²²οὐ", "οὐ", "22", Figure),
    ];
}
