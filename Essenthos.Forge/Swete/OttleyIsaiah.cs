namespace Essenthos.Core.Swete;

/// <summary>
/// Ottley's Isaiah — Codex Alexandrinus as Richard Rusden Ottley printed it at Cambridge in 1904 —
/// read from First1KGreek's TEI, which is the file its licence is stated in.
///
/// <para>
/// The transcription is rougher than Swete's and most of what is wrong with it is left as it is: a
/// misread letter, a word the page prints and the file lost, a line set down out of its place, are
/// all things only the page can settle. What is repaired here is what the file shows about itself.
/// Ottley prints the verse number in the margin of the line a verse begins on, and the file keeps
/// those numbers as line marks even where it failed to open the verse, so the last verse of a
/// chapter often runs into the one before it with its own number standing beside it; those are
/// divided, at the words Swete and Brenton both begin the verse with. Two tokens are the
/// transcription's own placeholders rather than words: one is taken out, and the other stands where
/// a word was lost that is put back below. Three Greek words carry a
/// Latin letter that looks exactly like the Greek one. And the manuscript's title and colophon,
/// which Ottley prints above 1:1 and below 66:24, are a title and a colophon, not words of either
/// verse.
/// </para>
///
/// <para>
/// **A word the file lost is put back only where the verse is left without it and Ottley himself
/// says the manuscript has it.** His foot-notes record where the other uncials differ from
/// Alexandrinus, and the TEI keeps them. Where the file leaves an article or a preposition with
/// nothing after it, Brenton, Swete and GLAUx all read the same word there, and the note on that
/// verse records no manuscript differing from A at that word, the word is A's and the transcription
/// lost it: βραχίων at 53:1, καταπάτημα at 5:5, the end of παραλελυμένα at 35:3. Where the verse
/// reads whole without it, only the page puts it back.
/// </para>
///
/// <para>
/// **Seven verses are read against the printed page** (<see cref="Page"/>), each entry citing the page
/// of Ottley's second volume and the leaf of the Internet Archive's scan it was read on: ἡμᾶς at the end
/// of 35:4, a καὶ the file doubles at 34:11, and the letters the transcription misreads in those verses
/// — εἷς for εἰς, ὃ for ὁ, οὗ for οὐ. An entry that prints as many words as it replaces puts their
/// letters right one for one, so a corpus that already holds the verse keeps the words' rows. The same
/// misreadings elsewhere in the book are left as the transcription reads them.
/// </para>
///
/// <para>
/// **Two verses have no number and are not given one.** Ottley numbers by the Hebrew, and the Greek
/// has nothing that answers to the Hebrew's 38:15 or 40:7: the words other editions number 38:15 stand
/// at the end of his 38:14, and 40:7's are the Hebrew's 40:8, which the Greek has once where the
/// Hebrew has it twice. Neither page nor file marks a 15 or a 7, where every division repaired below
/// is marked.
/// </para>
/// </summary>
internal static class OttleyIsaiah
{
    /// <summary>First1KGreek's <c>tlg0527.tlg048.1st1K-grc2.xml</c>, at the commit the licence notes name.</summary>
    public const string File = "isaiah-ottley-1904.xml";

    /// <summary>What the text's row says about the repairs.</summary>
    public const string Note =
        "Modified: ten verse divisions the transcription lost are restored by Essenthos where Ottley's "
        + "own verse number stands in the margin and Swete and Brenton begin the verse at the same words; "
        + "three words it lost where the verse is left without them are put back where Brenton, Swete and "
        + "GLAUx read them and Ottley's own apparatus records no manuscript differing, and letters of a foot-note it read into a word are taken out; "
        + "a placeholder the transcription left for what it could not read is taken out; three Latin "
        + "letters standing for the Greek ones they look like are written as Greek; the manuscript's title "
        + "above 1:1 and colophon below 66:24 are not counted as words of those verses; and one line the "
        + "transcription set down out of its place in 2:20 is read in its place. Letters the transcription "
        + "misread are left as it reads them.";

    /// <summary>What the text's row says about the verses read against the printed page.</summary>
    public const string PageNote =
        "Modified: seven verses (2:19, 5:5, 34:11, 35:3, 35:4, 35:9, 53:1) are read by Essenthos against "
        + "Ottley's printed page: the letters the transcription misread there are written as the page prints "
        + "them, ἡμᾶς is restored at the end of 35:4, and a καὶ the transcription doubled at 34:11 is taken out.";

    private const string Margin =
        "Ottley's own number for the verse stands in the margin of this line; Swete and Brenton begin the verse at these words";

    private const string Latin = "A Latin letter for the Greek one it looks exactly like";

    private const string Lost =
        "Brenton, Swete and GLAUx all read the word here, where the file leaves the verse without it, and "
        + "Ottley's apparatus on the verse records no manuscript differing from A at this word";

    public static IReadOnlyList<string> Lines(string folder) => Lines(folder, Repairs);

    /// <summary>The file's lines with one set of repairs made rather than all of them.</summary>
    public static IReadOnlyList<string> Lines(string folder, IReadOnlyList<EditionRepair> repairs) =>
        EditionRepairs.Apply(File,
            First1KGreekReader.Lines(Path.Combine(folder, SweteIsaiah.Folder, File), SweteIsaiah.Work), repairs);

    /// <summary>What the file shows about itself, then what the page settles, in that order.</summary>
    public static readonly IReadOnlyList<EditionRepair> Repairs = [.. Transcription, .. Page];

    /// <summary>The repairs the file's own evidence settles, which a corpus loaded before the page was read holds.</summary>
    public static IReadOnlyList<EditionRepair> Transcription =>
    [
        EditionRepair.Replace(1, "1", "προφήτης ιγ΄", "",
            "The manuscript's title, Ἠσαΐας προφήτης ιγ΄ — Isaiah, the thirteenth prophet, after the Twelve — "
            + "which Ottley prints above the first verse and Swete's apparatus records as A's inscription"),
        EditionRepair.Divide(2, "19", "τῇ ἡμέρᾳ ἐκείνῃ ἐκβαλεῖ", "τῇ ἡμέρᾳ ἐκείνῃ ἐκβαλεῖ", "20", Margin),
        EditionRepair.Replace(2, "20",
            "προστοῦ εἰσελθεῖν εἷς τὰς κυνεῖν, τοῖς ματαίοις καὶ ταῖς νυκτερίσιν,",
            "προσκυνεῖν, τοῖς ματαίοις καὶ ταῖς νυκτερίσιν, τοῦ εἰσελθεῖν εἷς τὰς",
            "The line ending προσ- and the line after it are interleaved: τοῦ εἰσελθεῖν εἷς τὰς, which opens "
            + "2:21 and runs on into τρώγλας, is set down inside προσκυνεῖν. Every word is the file's own; "
            + "only their order is the page's"),
        EditionRepair.Divide(2, "20", "τοῦ εἰσελθεῖν", "τοῦ εἰσελθεῖν", "21", Margin),
        EditionRepair.Divide(3, "25", "καὶ πενθήσουσιν", "καὶ πενθήσουσιν", "26", Margin),
        EditionRepair.Replace(5, "5", "x003E;", "καταπάτημα",
            $"A character reference the transcription left undecoded where the verse ends on a preposition. {Lost}: "
            + "the note on 5:5 records only οἶκον for τοῖχον; καταπάτημα as Ottley prints it at 7:25"),
        EditionRepair.Divide(12, "4", "ὑμνήσατε τὸ ὄνομα Κυρίου,", "ὑμνήσατε τὸ ὄνομα Κυρίου,", "5", Margin),
        EditionRepair.Divide(12, "5", "ἀγαλλιᾶσθε", "ἀγαλλιᾶσθε", "6", Margin),
        EditionRepair.Divide(22, "24", "πέν τῇ ἡμέρᾳ.", "πέν τῇ ἡμέρᾳ.", "25", Margin),
        EditionRepair.Divide(25, "11", "τὸ ὕψος τῆς καταφυγῆς", "τὸ ὕψος τῆς καταφυγῆς", "12", Margin),
        EditionRepair.Replace(30, "7", "Αἰγύπτιοi", "Αἰγύπτιοι", Latin),
        EditionRepair.Replace(34, "11", "ABBREV", "",
            "The transcription's placeholder for an abbreviation it did not expand, where the page prints no "
            + $"word: {Printed(53, 435)}"),
        EditionRepair.Replace(35, "3", "παραλεφοβεῖσθε·", "παραλελυμένα",
            $"παραλε- is the head of παραλελυμένα, run into φοβεῖσθε· from the end of the line 35:4 prints whole. {Lost}: "
            + "the notes on chapter 35 pass from 2 to 4"),
        EditionRepair.Replace(35, "9", "πορεύευρον σονται", "πορεύσονται",
            "πορεύσονται runs over the page, and ευρον, the first letters of the foot-note printed beneath it "
            + "(\"14 γαρ] ευροντες Β\"), was read into the word; the note stands between its halves in the file"),
        EditionRepair.Divide(35, "9", "καὶ συνηγμένοι", "καὶ συνηγμένοι", "10", Margin),
        EditionRepair.Divide(38, "21", "καὶ εἶπεν Ἑζεκίας", "καὶ εἶπεν Ἑζεκίας", "22", Margin),
        EditionRepair.Divide(44, "27", "ὁ λέγων Κύρῳ", "ὁ λέγων Κύρῳ", "28", Margin),
        EditionRepair.Replace(53, "1", "Kύριε,", "Κύριε,", Latin),
        EditionRepair.Replace(53, "1", "καὶ ὃ Κυρίου", "καὶ ὃ βραχίων Κυρίου",
            $"The article stands with nothing to name. {Lost}: the notes on chapter 53 open at verse 2; "
            + "βραχίων as Ottley prints it at 40:10"),
        EditionRepair.Replace(59, "1", "Mὴ", "Μὴ", Latin),
        EditionRepair.Replace(66, "24", "σαρκί. ΗΣΑΙΑΣ ΠΡΟΦΗΤΗΣ.", "σαρκί.",
            "The manuscript's colophon, Ἠσαΐας προφήτης, which Ottley prints below the last verse"),
    ];

    /// <summary>
    /// The verses read against Ottley's printed page, each addressed to the verse as
    /// <see cref="Transcription"/> leaves it.
    /// </summary>
    public static IReadOnlyList<EditionRepair> Page =>
    [
        EditionRepair.Replace(2, "19", "εἷς τὸ. σπήλαια", "εἰς τὰ σπήλαια", $"{Printed(4, 386)}: {Breathing}, and τὰ"),
        EditionRepair.Replace(2, "19", "εἷς τὰς σχισμὸς", "εἰς τὰς σχισμὰς", $"{Printed(4, 386)}: {Breathing}, and σχισμὰς"),
        EditionRepair.Replace(2, "19", "εἷς τὰς τρώγλας", "εἰς τὰς τρώγλας", $"{Printed(4, 386)}: {Breathing}"),
        EditionRepair.Replace(5, "5", "εἷς διαρπαγήν,", "εἰς διαρπαγήν,", $"{Printed(7, 389)}: {Breathing}"),
        EditionRepair.Replace(5, "5", "εἷς καταπάτημα", "εἰς < καταπάτημα>.",
            $"{Printed(7, 389)}: {Breathing}, and καταπάτημα in Ottley's angle brackets"),
        EditionRepair.Replace(34, "11", "καὶ καὶ κατοικήσονται", "καὶ κατοικήσονται", $"{Printed(53, 435)}: one καὶ"),
        EditionRepair.Replace(35, "3", "παραλελυμένα", "παραλελυμένα.", $"{Printed(54, 436)}: the verse ends with the stop"),
        EditionRepair.Replace(35, "4", "ὃ", "ὁ", $"{Printed(54, 436)}: the article, with the smooth breathing"),
        EditionRepair.Replace(35, "4", "σώσει", "σώσει ἡμᾶς.", $"{Printed(54, 436)}: ἡμᾶς, which the transcription lost"),
        EditionRepair.Replace(35, "9", "οὔκ", "οὐκ", $"{Printed(54, 436)}: οὐκ, unaccented"),
        EditionRepair.Replace(35, "9", "οὗ", "οὐ", $"{Printed(54, 436)}: the negative, with the smooth breathing"),
        EditionRepair.Replace(53, "1", "ὃ", "ὁ", $"{Printed(84, 466)}: the article, with the rough breathing"),
    ];

    private const string Breathing = "εἰς, with the smooth breathing";

    private static string Printed(int page, int leaf) =>
        $"Printed in Ottley, vol. 2 (Cambridge, 1904), p. {page}, scan leaf {leaf} of IsaiahAccordingToTheSeptuagint";
}
