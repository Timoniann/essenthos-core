namespace Essenthos.Core.Loading.Frame;

/// <summary>
/// The Prayer of Azariah and the Song of the Three, which the Greek and the Latin print inside Daniel 3
/// as verses 24 to 90 and number the rest of the chapter on from there, so that the Hebrew's 3:24-30
/// are their 3:91-97. The versification data gives the song the standard numbering of the English
/// Apocrypha, which prints it as a book of its own, <c>S3Y</c> 1:1-68, and places 3:91-97 at the
/// Hebrew's verses.
///
/// Here the song stays in Daniel, as every edition of the corpus that holds it prints it, after the
/// thirty verses the chapter has in the standard numbering: the standard's verse 1:1 is Daniel 3:31
/// and 1:68 is Daniel 3:98. Which standard verse an edition's verse is, and so its order, is the
/// data's, and no Hebrew or English verse stands at those addresses.
/// </summary>
internal static class SongOfTheThree
{
    private const string Code = "S3Y";

    private const string PrefixInDaniel = "Dan.";

    private const int Daniel = 27;

    private const int Chapter = 3;

    /// <summary>The verses Daniel 3 has in the standard numbering, the Hebrew's 3:31-33 being its 4:1-3.</summary>
    private const int ChapterVerses = 30;

    /// <summary>Where the standard's verse of the song stands.</summary>
    public static CanonicalReference Verse(int verse) => new(Daniel, Chapter, ChapterVerses + verse);

    /// <summary>
    /// The places a cell of the data's standard column names in the song, <c>S3Y.1:29-30</c>, or false
    /// where the cell is about something else. A range is counted in the song's own verses before it
    /// is placed, so its end is never read as a verse of Daniel.
    /// </summary>
    public static bool TryParseAll(string cell, out IReadOnlyList<CanonicalReference> references)
    {
        references = [];
        var text = cell.Trim();
        if (!text.StartsWith(Code + ".", StringComparison.Ordinal))
        {
            return false;
        }

        references =
        [
            .. CanonicalReference.ParseAll(PrefixInDaniel + text[(Code.Length + 1)..])
                .Where(reference => reference.Chapter == 1)
                .Select(reference => Verse(reference.Verse)),
        ];
        return references.Count > 0;
    }
}
