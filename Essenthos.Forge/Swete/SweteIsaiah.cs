using Essenthos.Core.Conversion;

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
/// Letters are left as the transcription reads them — ΙΙλίνθοι, εὐφρρσύνην, κἵλου — since correcting spelling
/// against another edition is how a second witness stops being one. What is changed is a Latin letter standing for the Greek
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

    /// <summary>The book's lines, with the <c>tokens</c>, <c>opens</c> and <c>moves</c> rows of Swete's corrections file addressed to them made.</summary>
    public static IEnumerable<string> Lines(string folder, TextCorrections corrections) =>
        EditionRepairs.Apply(File, Work, First1KGreekReader.Lines(Path.Combine(folder, Folder, File), Work), corrections);
}
