using Essenthos.Core.Sword;

namespace Essenthos.Core.Loading.CrossReferences;

/// <summary>
/// The verses of the King James's numbering, which the frame calls English and every reference set here is written in:
/// OpenBible's, the Treasury's and Nave's. A reference to a verse outside it names nothing a reader can open, so the
/// loaders leave it out. The frame holds all of these and more (the Septuagint's own verses), never fewer.
/// </summary>
internal static class EnglishVerses
{
    private static readonly int[][] Chapters = SwordModule.Chapters("KJV");

    public static bool Has(VerseAddress verse) =>
        HasChapter(verse.Book, verse.Chapter) && verse.Verse >= 1 && verse.Verse <= Chapters[verse.Book - 1][verse.Chapter - 1];

    public static bool HasChapter(int book, int chapter) =>
        book >= 1 && book <= Chapters.Length && chapter >= 1 && chapter <= Chapters[book - 1].Length;

    /// <summary>Whether a reference names verses that are all there: its verse, and the last of its run where it has one.</summary>
    public static bool Has(CrossReferenceRow row) =>
        Has(row.From) && Has(row.To) && (row.End is not { } end || Has(end));
}
