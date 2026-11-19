
using Essenthos.Core.Conversion;

namespace Essenthos.Core.Swete;

/// <summary>
/// Ottley's Isaiah — Codex Alexandrinus as Richard Rusden Ottley printed it at Cambridge in 1904 —
/// read from First1KGreek's TEI, which is the file its licence is stated in.
///
/// <para>
/// The transcription is rougher than Swete's: a misread letter, a word the page prints and the file
/// lost, a line set down out of its place, are all things only the page can settle, and the page does
/// (the page's rows of the corrections file). The rows before them repair what the file shows about itself.
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
/// **The book is read against the printed page, page by page**, each row of the corrections file citing
/// the page of Ottley's second volume and the leaf of the Internet Archive's scan it was read on: the letters, accents and breathings the transcription misreads
/// throughout — εἷς for εἰς, ὃ for ὁ, τὸ. for τὰ — and the words it lost, doubled or set in the verse
/// beside. What goes in is what the page prints, its own misprints included.
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
        + "transcription set down out of its place in 2:20 is read in its place.";

    /// <summary>What the text's row says about the book read against the printed page.</summary>
    public const string PageNote =
        "Modified: Essenthos reads the book against Ottley's printed page, page by page: the letters, accents and "
        + "breathings the transcription misread are written as the page prints them, the page's own misprints "
        + "included; words it lost are restored; and words it doubled, or set in the verse beside, are taken out "
        + "or put in the verse the page prints them in.";

    /// <summary>The book's lines, with the <c>tokens</c> and <c>opens</c> rows of Ottley's corrections file made.</summary>
    public static IEnumerable<string> Lines(string folder, TextCorrections corrections) =>
        EditionRepairs.Apply(File, SweteIsaiah.Work,
            First1KGreekReader.Lines(Path.Combine(folder, SweteIsaiah.Folder, File), SweteIsaiah.Work), corrections);
}
