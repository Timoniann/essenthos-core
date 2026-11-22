namespace Essenthos.Core.Swete;

/// <summary>
/// What the text's row says about the repairs its corrections file makes to Swete's transcription, each kind of
/// them in a sentence of the rights note: the CC BY-SA licence asks that a change be indicated.
/// </summary>
internal static class SweteNotes
{
    /// <summary>The words the transcription lost in the genealogies and the tower of Babel of Genesis, restored where Brenton's Greek and GLAUx read the same words.</summary>
    public const string Restored =
        "Modified: words the transcription lost in the genealogies and the tower of Babel of Genesis 5, "
        + "9, 11 and 12 — among them Noah's three hundred and fifty years and Abram's seventy-five — are "
        + "restored by Essenthos where Brenton's Greek and the GLAUx treebank read the same words, in the "
        + "spelling Swete prints beside them. Letters the transcription misread are left as it reads them.";

    /// <summary>Latin letters, margin numbers run into a word, two words run together and a word divided at a line end.</summary>
    public const string Letters =
        "Letters and spaces corrected by Essenthos throughout: a Latin letter the transcription wrote for the "
        + "Greek one it looks like is written as Greek, the chapter or verse number Swete prints in the margin "
        + "is taken out of the word it was run into, and two words run together are divided where Brenton's "
        + "Greek and the GLAUx treebank both read them apart; a word the page divides at a line end, which the "
        + "transcription read as two, is one word again.";

    /// <summary>Words the transcription lost, read back off the printed page where Brenton's Greek and GLAUx read words it lacks.</summary>
    public const string Page =
        "Modified: words the transcription lost throughout are restored by Essenthos from the printed page "
        + "(the Internet Archive's scans of Swete's volumes), each where Brenton's Greek and the GLAUx treebank "
        + "read words the transcription lacks and the page prints them, in the page's spelling.";

    /// <summary>The verse numbers the transcription let into the text in figures.</summary>
    public const string Figures =
        "The verse numbers the transcription let into the text in figures are taken out by Essenthos and the "
        + "words they were glued to kept; where a figure also took a word's first letters, the word is written "
        + "as the verses on either side print it.";

    /// <summary>Verse divisions the transcription lost, opened again where the page shows them.</summary>
    public const string Divisions =
        "Modified: thirty verse divisions the transcription lost are restored by Essenthos — twenty-six "
        + "where Swete's own verse number stands in the text before the verse's first word, and Genesis 15:19, "
        + "20 and 21 and Psalm 91:16 from the printed page, with the two words the transcription lost in Genesis.";

    /// <summary>The Psalms' verse numbers put right against the printed page, and the eight verses of Psalm 118 the transcription lost.</summary>
    public const string Psalms =
        "Modified: in the Psalms, four verses the transcription numbered as the page does not (88:48, 115:6, 118:131 "
        + "and 118:133) are given the page's number, the divisions it lost at 24:14, 47:7, 118:19 and 119:4 are opened "
        + "where the page begins those verses, a διάψαλμα it moved from 88:46 into 88:48 is put back, and 118:28-35, "
        + "which it lost, are restored by Essenthos from the printed page.";

    /// <summary>The Roman chapter numbers the reader takes out, and the chapter opening the transcription kept only the number of.</summary>
    public const string ChapterMarkers =
        "Modified: the Roman chapter numbers Swete prints in the margin, which the transcription let into the "
        + "text at the end of a chapter or the start of the next, are taken out by Essenthos, and a word one was "
        + "run into is kept; Exodus 20:1, of which the transcription kept only the number, is restored from the "
        + "printed page.";

    /// <summary>Verses settled one by one against the printed page.</summary>
    public const string Settled =
        "Modified: where the transcription read Swete's running head or lines of his apparatus into the text "
        + "(Judges 18:8, 1 Samuel 8:2 and 11:11), Essenthos reads the words the printed page has there; the "
        + "opening lines of Numbers 17 and 19 and a word of Numbers 16:50, which the transcription lost, are "
        + "restored from the page; and 3 Kingdoms 16:1, which the transcription ran into 15:34, is divided "
        + "where the page begins it.";

    /// <summary>The verses read off the page for what its margin let in.</summary>
    public const string Margin =
        "Modified: letters of the margin the transcription read into nine verses (Genesis 11:4, Deuteronomy 21:1, "
        + "1 Samuel 25:28, Proverbs 22:17, Wisdom 12:10 and 14:19, Malachi 2:12 and 3:17, Ezekiel 34:12) are "
        + "taken out by Essenthos, and the words of those verses it cut short or misread are written as the "
        + "printed page has them.";
}
