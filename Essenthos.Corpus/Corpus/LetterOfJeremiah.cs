namespace Essenthos.Core.Corpus;

/// <summary>
/// The Letter of Jeremiah, which the Greek, Slavonic and Ethiopic Bibles print as a book of its own
/// and the Latin and English Bibles as the sixth chapter of Baruch. It is one passage under two
/// names, verse for verse in the standard numbering.
/// </summary>
internal static class LetterOfJeremiah
{
    public const int Book = 76;

    public const int Chapter = 1;

    public const int Baruch = 67;

    /// <summary>The chapter of Baruch the Latin and English Bibles print the letter as.</summary>
    public const int InBaruch = 6;
}

/// <summary>
/// The Prayer of Azariah and the Song of the Three, which the Greek and Latin Bibles print inside
/// Daniel 3 and the King James as a book of its own. In the standard's sixty-eight verses, which the
/// frame holds in Daniel 3 after the chapter's thirty.
/// </summary>
internal static class SongOfTheThreeBook
{
    public const int Book = 93;

    public const int Chapter = 1;

    public const int Verses = 68;

    public const int Daniel = 27;

    public const int InDaniel = 3;

    /// <summary>The verses Daniel 3 has in the standard numbering, after which the song stands.</summary>
    public const int DanielVerses = 30;
}

/// <summary>
/// Passages the Bibles print under two names: the Letter of Jeremiah, a book of its own and Baruch 6;
/// and the Song of the Three, inside Daniel 3 and a book of its own. A verse of one stands primarily
/// where its own edition prints it and covers the same verse under the other name, so a reader asking
/// for either name opens it beside every text that holds it.
/// </summary>
internal static class TwinPassages
{
    /// <param name="Joined">The name the two are joined under, verse by verse.</param>
    /// <param name="Other">The other name.</param>
    /// <param name="Shift">What is added to a verse number under the joined name to give it under the other.</param>
    private sealed record Passage(
        (int Book, int Chapter) Joined,
        (int Book, int Chapter) Other,
        int First,
        int Last,
        int Shift);

    private static readonly Passage[] All =
    [
        new((LetterOfJeremiah.Book, LetterOfJeremiah.Chapter), (LetterOfJeremiah.Baruch, LetterOfJeremiah.InBaruch),
            1, 73, 0),
        new((SongOfTheThreeBook.Book, SongOfTheThreeBook.Chapter),
            (SongOfTheThreeBook.Daniel, SongOfTheThreeBook.InDaniel), 1, SongOfTheThreeBook.Verses,
            SongOfTheThreeBook.DanielVerses),
    ];

    /// <summary>The same verse under the passage's other name, or null for a verse of no such passage.</summary>
    public static (int Book, int Chapter, int Verse)? Other((int Book, int Chapter, int Verse) address)
    {
        foreach (var passage in All)
        {
            if ((address.Book, address.Chapter) == passage.Joined
                && address.Verse >= passage.First && address.Verse <= passage.Last)
            {
                return (passage.Other.Book, passage.Other.Chapter, address.Verse + passage.Shift);
            }

            if ((address.Book, address.Chapter) == passage.Other
                && address.Verse - passage.Shift >= passage.First && address.Verse - passage.Shift <= passage.Last)
            {
                return (passage.Joined.Book, passage.Joined.Chapter, address.Verse - passage.Shift);
            }
        }

        return null;
    }

    /// <summary>
    /// The address a verse of such a passage is joined at, whichever name its edition prints it under,
    /// so that two texts printing it under different names meet; any other address is itself.
    /// </summary>
    public static (int Book, int Chapter, int Verse) Joined((int Book, int Chapter, int Verse) address)
    {
        foreach (var passage in All)
        {
            if ((address.Book, address.Chapter) == passage.Other
                && address.Verse - passage.Shift >= passage.First && address.Verse - passage.Shift <= passage.Last)
            {
                return (passage.Joined.Book, passage.Joined.Chapter, address.Verse - passage.Shift);
            }
        }

        return address;
    }

    /// <summary>
    /// Where else a chapter's verses are printed: for each passage the chapter holds under one name, the
    /// chapter it is under the other, the verses of that chapter it takes, and what is added to one of
    /// their numbers to give its row here.
    /// </summary>
    public static IEnumerable<(int Book, int Chapter, int First, int Last, int Shift)> NamedElsewhere(
        int book,
        int chapter)
    {
        foreach (var passage in All)
        {
            if ((book, chapter) == passage.Joined)
            {
                yield return (passage.Other.Book, passage.Other.Chapter, passage.First + passage.Shift,
                    passage.Last + passage.Shift, -passage.Shift);
            }
            else if ((book, chapter) == passage.Other)
            {
                yield return (passage.Joined.Book, passage.Joined.Chapter, passage.First, passage.Last, passage.Shift);
            }
        }
    }

    /// <summary>The books whose verses may stand under another book's name.</summary>
    public static IReadOnlySet<int> Books { get; } =
        All.SelectMany(passage => new[] { passage.Joined.Book, passage.Other.Book }).ToHashSet();

    /// <summary>The book a passage's verses are joined under, for a book that holds one of them.</summary>
    public static int? JoinedBook(int book) =>
        All.FirstOrDefault(passage => passage.Other.Book == book || passage.Joined.Book == book)?.Joined.Book;
}
