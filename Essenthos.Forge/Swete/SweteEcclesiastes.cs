using Essenthos.Core.Conversion;

namespace Essenthos.Core.Swete;

/// <summary>
/// Swete's Ecclesiastes, read from First1KGreek's TEI because the one-token-per-line edition has no file for it.
///
/// <para>
/// First1KGreek transcribed the book from volume 2 of 1896 (pages 480 to 505) and committed it in September 2017
/// as <c>tlg0527.tlg030.opp-grc2.xml</c>; a merge two days after its last correction dropped the file, leaving only the work's catalogue
/// entry, so every edition built from the repository since, Nathan D. Smith's among them, lacks the book. The file
/// is kept here as it stood at the last commit that changed it.
/// </para>
///
/// <para>
/// Swete prints the other numbering of a verse in brackets where his division differs from it — <c>(17)</c> at the
/// top of a page that continues 1:17, <c>(12)</c> inside 7:1, <c>(1)</c> before the first verse of chapters 8 and 9
/// — and his signs § and ¶ where another uncial begins or ends; the transcription let both into the text, and the
/// corrections file takes them out, with the drop capital's Latin letters and the one verse number left in a line.
/// </para>
/// </summary>
internal static class SweteEcclesiastes
{
    /// <summary>First1KGreek's <c>tlg0527.tlg030.opp-grc2.xml</c> at commit 2d023b8335, kept under a name no fetch writes.</summary>
    public const string File = "ecclesiastes-swete-1896.xml";

    /// <summary>The work's number in the TLG catalogue of the Septuagint.</summary>
    public const int Work = 30;

    /// <summary>What the text's row says about this book.</summary>
    public const string Note =
        "Ecclesiastes is read from First1KGreek's own encoding of volume 2, which the repository dropped in 2017 and "
        + "so no edition built from it has; Essenthos takes out the bracketed figures of the other verse numbering and "
        + "Swete's signs for where another manuscript begins, which the transcription let into the text, writes the "
        + "Latin letters of the opening word as Greek, opens 1:6 at the verse number the page prints, and writes "
        + "sixteen words and seven stops it misread as the printed page has them.";

    /// <summary>The book's lines, with the <c>tokens</c>, <c>opens</c> and <c>moves</c> rows of Swete's corrections file addressed to them made.</summary>
    public static IEnumerable<string> Lines(string folder, TextCorrections corrections) =>
        EditionRepairs.Apply(File, Work, First1KGreekReader.Lines(Path.Combine(folder, SweteIsaiah.Folder, File), Work), corrections);
}
