namespace Essenthos.Core.Products;

/// <summary>
/// A verse by its edition's own address: <c>book.chapter[label]:verse[label]</c>, as in
/// <c>1.31:50a</c> (the Septuagint's Genesis 31:50a) or <c>19.4a:1</c> (Swete's Ode 4a). The book is
/// the canonical ordinal shared by every text; chapter and verse are the numbers the edition itself
/// prints, with the letter or range it prints after them. Where the verse stands in the shared frame
/// is another matter (the text's frame file), and a key never says it.
///
/// <para>
/// A label is what follows the number: letters, digits and hyphens, never starting with a digit, so
/// that <c>50a</c>, <c>1-10</c> (a verse printed as a range) and <c>5a1</c> all parse one way.
/// </para>
/// </summary>
public readonly record struct VerseKey(int Book, int Chapter, string ChapterLabel, int Verse, string VerseLabel)
    : IComparable<VerseKey>
{
    public VerseKey(int book, int chapter, int verse, string verseLabel = "")
        : this(book, chapter, string.Empty, verse, verseLabel)
    {
    }

    public static VerseKey Parse(ReadOnlySpan<char> text) =>
        TryParse(text, out var key) ? key : throw new FormatException($"'{text}' is not a verse key (book.chapter[label]:verse[label]).");

    public static bool TryParse(ReadOnlySpan<char> text, out VerseKey key)
    {
        key = default;
        var colon = text.IndexOf(':');
        if (colon < 0)
        {
            return false;
        }

        var dot = text[..colon].IndexOf('.');
        if (dot < 0
            || !TryParseNumber(text[..dot], out var book) || book < 1
            || !TryParseNumbered(text[(dot + 1)..colon], out var chapter, out var chapterLabel)
            || !TryParseNumbered(text[(colon + 1)..], out var verse, out var verseLabel))
        {
            return false;
        }

        key = new VerseKey(book, chapter, chapterLabel, verse, verseLabel);
        return true;
    }

    /// <summary>A number with its optional label, as a chapter or a verse writes it.</summary>
    internal static bool TryParseNumbered(ReadOnlySpan<char> text, out int number, out string label)
    {
        number = 0;
        label = string.Empty;
        var digits = 0;
        while (digits < text.Length && char.IsAsciiDigit(text[digits]))
        {
            digits++;
        }

        if (digits == 0 || !TryParseNumber(text[..digits], out number) || !IsLabel(text[digits..]))
        {
            return false;
        }

        label = text[digits..].ToString();
        return true;
    }

    internal static bool TryParseNumber(ReadOnlySpan<char> text, out int number) =>
        int.TryParse(text, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out number);

    /// <summary>Empty, or letters, digits and hyphens not starting with a digit.</summary>
    public static bool IsLabel(ReadOnlySpan<char> text)
    {
        if (text.IsEmpty)
        {
            return true;
        }

        if (char.IsAsciiDigit(text[0]))
        {
            return false;
        }

        foreach (var c in text)
        {
            if (!char.IsAsciiLetterOrDigit(c) && c != '-')
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>The address without the book, <c>31:50a</c>, as a key written inside a known book has it.</summary>
    public string ChapterAndVerse => $"{Chapter}{ChapterLabel}:{Verse}{VerseLabel}";

    /// <summary>The verse alone, <c>50a</c>, as a key written inside a known chapter has it.</summary>
    public string VerseOnly => $"{Verse}{VerseLabel}";

    public override string ToString() => $"{Book}.{ChapterAndVerse}";

    /// <summary>
    /// Book, chapter, chapter label, verse, verse label; labels ordinal with the empty one first. This
    /// is a stable order for files, not the edition's print order, which its manifest's <c>seq</c> holds.
    /// </summary>
    public int CompareTo(VerseKey other)
    {
        var order = Book.CompareTo(other.Book);
        if (order == 0) order = Chapter.CompareTo(other.Chapter);
        if (order == 0) order = string.CompareOrdinal(ChapterLabel, other.ChapterLabel);
        if (order == 0) order = Verse.CompareTo(other.Verse);
        if (order == 0) order = string.CompareOrdinal(VerseLabel, other.VerseLabel);
        return order;
    }

    public static bool operator <(VerseKey left, VerseKey right) => left.CompareTo(right) < 0;
    public static bool operator >(VerseKey left, VerseKey right) => left.CompareTo(right) > 0;
    public static bool operator <=(VerseKey left, VerseKey right) => left.CompareTo(right) <= 0;
    public static bool operator >=(VerseKey left, VerseKey right) => left.CompareTo(right) >= 0;
}
