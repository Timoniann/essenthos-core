namespace Essenthos.Core.Corpus;

/// <summary>
/// The Letter of Jeremiah, which the Greek, Slavonic and Ethiopic Bibles print as a book of its own
/// and the Latin and English Bibles as the sixth chapter of Baruch. It is one passage under two
/// names, verse for verse in the standard numbering, so it stands at both: a verse of the letter
/// stands primarily where its own edition prints it and covers the same verse under the other name,
/// and a reader asking for either opens it beside every text that holds it.
/// </summary>
internal static class LetterOfJeremiah
{
    public const int Book = 76;

    public const int Chapter = 1;

    public const int Baruch = 67;

    /// <summary>The chapter of Baruch the Latin and English Bibles print the letter as.</summary>
    public const int InBaruch = 6;

    /// <summary>
    /// The other name of a chapter that is the letter, or null for any other chapter.
    /// </summary>
    public static (int Book, int Chapter)? Twin(int book, int chapter) =>
        (book, chapter) switch
        {
            (Book, Chapter) => (Baruch, InBaruch),
            (Baruch, InBaruch) => (Book, Chapter),
            _ => null,
        };

    /// <summary>
    /// The address a verse of the letter is joined at, whichever name its edition prints it under,
    /// so that two texts printing it under different names meet; any other address is itself.
    /// </summary>
    public static (int Book, int Chapter, int Verse) Joined((int Book, int Chapter, int Verse) address) =>
        (address.Book, address.Chapter) == (Baruch, InBaruch) ? (Book, Chapter, address.Verse) : address;
}
