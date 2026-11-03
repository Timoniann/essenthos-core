namespace Essenthos.Core.Products;

/// <summary>
/// A run of verses in one text's own numbering, from one key through another: <c>1.1:1..10</c>,
/// <c>1.1:31..2:3</c> across a chapter, <c>1.50:26..2.1:1</c> across a book. Two dots join the
/// ends, never a hyphen: a hyphen is part of a verse label (IRVBEN's Mark 9:43-44 is verse 43
/// with the label <c>-44</c>), so <c>1.1:1-10</c> is one verse. The second end is written as short
/// as the first allows, the way a word key is written in context, and reads back the same.
///
/// <para>
/// Which verses lie between the ends is the edition's print order, which the keys do not carry;
/// <see cref="Contains"/> answers by key order, which is the same thing wherever an edition
/// numbers in order, and a reader that must be exact asks the text's manifest.
/// </para>
/// </summary>
public readonly record struct VerseRange(VerseKey From, VerseKey Through)
{
    public const string Separator = "..";

    public VerseRange(VerseKey single) : this(single, single)
    {
    }

    public bool Single => From == Through;

    public bool Contains(VerseKey key) => key >= From && key <= Through;

    public static VerseRange Parse(ReadOnlySpan<char> text) =>
        TryParse(text, out var range) ? range : throw new FormatException($"'{text}' is not a verse or a run of verses (1.1:1 or 1.1:1..10).");

    public static bool TryParse(ReadOnlySpan<char> text, out VerseRange range)
    {
        range = default;
        var dots = text.IndexOf(Separator);
        if (dots < 0)
        {
            if (!VerseKey.TryParse(text, out var single))
            {
                return false;
            }

            range = new VerseRange(single);
            return true;
        }

        if (!VerseKey.TryParse(text[..dots], out var from) || !TryParseEnd(text[(dots + Separator.Length)..], from, out var through) || through < from)
        {
            return false;
        }

        range = new VerseRange(from, through);
        return true;
    }

    /// <summary>The second end in any of its shapes: <c>10</c>, <c>2:3</c>, <c>2.1:1</c>.</summary>
    private static bool TryParseEnd(ReadOnlySpan<char> text, VerseKey from, out VerseKey through)
    {
        through = default;
        if (text.IsEmpty || text.Contains(' '))
        {
            return false;
        }

        var colon = text.IndexOf(':');
        if (colon < 0)
        {
            if (!VerseKey.TryParseNumbered(text, out var verse, out var label))
            {
                return false;
            }

            through = new VerseKey(from.Book, from.Chapter, from.ChapterLabel, verse, label);
            return true;
        }

        if (text[..colon].Contains('.'))
        {
            return VerseKey.TryParse(text, out through);
        }

        if (!VerseKey.TryParseNumbered(text[..colon], out var chapter, out var chapterLabel)
            || !VerseKey.TryParseNumbered(text[(colon + 1)..], out var number, out var verseLabel))
        {
            return false;
        }

        through = new VerseKey(from.Book, chapter, chapterLabel, number, verseLabel);
        return true;
    }

    public override string ToString()
    {
        if (Single)
        {
            return From.ToString();
        }

        var end = Through.Book != From.Book ? Through.ToString()
            : Through.Chapter != From.Chapter || Through.ChapterLabel != From.ChapterLabel ? Through.ChapterAndVerse
            : Through.VerseOnly;
        return $"{From}{Separator}{end}";
    }
}
