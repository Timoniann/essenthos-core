namespace Essenthos.Core.Products;

/// <summary>
/// A word by its text and its edition's own address: <c>KJV 1.1:1.3</c> is the third word of the
/// King James's Genesis 1:1. The text is a slug; the verse is a <see cref="VerseKey"/>; the position
/// counts from 1 in the edition's print order. It is what every product cites instead of a row id,
/// and it is unique by construction, being today's unique index over the word table written out.
///
/// <para>
/// A key in context may leave out what the context states, from the left: the text inside a file
/// of one text (<c>1.1:1.3</c>), the book inside a book (<c>1:1.3</c>), the chapter inside a chapter
/// (<c>1.3</c>), the verse inside a verse (<c>.3</c>). Each shape is told from the others by its
/// punctuation alone, so a parser given the context accepts any of them. Files written by the
/// product writers never shorten past the text: a line of a file is read on its own in a diff and
/// after a sort, and a line that needs its neighbours to be understood is one nobody can review.
/// </para>
/// </summary>
public readonly record struct WordKey(string Text, VerseKey Verse, int Position) : IComparable<WordKey>
{
    /// <summary>A text slug as the corpus writes them: capitals and digits, as in <c>KJV</c> or <c>NESTLE1904</c>.</summary>
    public static bool IsText(ReadOnlySpan<char> text)
    {
        if (text.IsEmpty)
        {
            return false;
        }

        foreach (var c in text)
        {
            if (!char.IsAsciiLetterUpper(c) && !char.IsAsciiDigit(c))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Reads a key in any of its shapes. <paramref name="context"/> supplies what the key leaves out;
    /// a key that leaves out more than the context states is an error.
    /// </summary>
    public static WordKey Parse(ReadOnlySpan<char> text, WordKey? context = null) =>
        TryParse(text, context, out var key)
            ? key
            : throw new FormatException($"'{text}' is not a word key{(context is null ? "" : $" in the context of {context}")}.");

    /// <summary>Reads a key written inside a file of one text, where the text is never written.</summary>
    public static WordKey Parse(ReadOnlySpan<char> text, string ownText) =>
        TryParse(text, ownText, null, out var key)
            ? key
            : throw new FormatException($"'{text}' is not a word key of {ownText}.");

    public static bool TryParse(ReadOnlySpan<char> text, WordKey? context, out WordKey key) =>
        TryParse(text, context?.Text, context?.Verse, out key);

    private static bool TryParse(ReadOnlySpan<char> text, string? slug, VerseKey? known, out WordKey key)
    {
        key = default;
        var space = text.IndexOf(' ');
        if (space >= 0)
        {
            if (!IsText(text[..space]))
            {
                return false;
            }

            slug = text[..space].ToString();
            text = text[(space + 1)..];
            known = null;
        }

        if (slug is null)
        {
            return false;
        }

        var lastDot = text.LastIndexOf('.');
        if (lastDot < 0 || !VerseKey.TryParseNumber(text[(lastDot + 1)..], out var position) || position < 1)
        {
            return false;
        }

        var address = text[..lastDot];
        VerseKey verse;
        if (address.IsEmpty)
        {
            if (known is null)
            {
                return false;
            }

            verse = known.Value;
        }
        else if (address.IndexOf(':') is var colon && colon >= 0)
        {
            if (address[..colon].Contains('.'))
            {
                if (!VerseKey.TryParse(address, out verse))
                {
                    return false;
                }
            }
            else
            {
                if (known is null
                    || !VerseKey.TryParseNumbered(address[..colon], out var chapter, out var chapterLabel)
                    || !VerseKey.TryParseNumbered(address[(colon + 1)..], out var number, out var label))
                {
                    return false;
                }

                verse = new VerseKey(known.Value.Book, chapter, chapterLabel, number, label);
            }
        }
        else
        {
            if (known is null || !VerseKey.TryParseNumbered(address, out var number, out var label))
            {
                return false;
            }

            verse = new VerseKey(known.Value.Book, known.Value.Chapter, known.Value.ChapterLabel, number, label);
        }

        key = new WordKey(slug, verse, position);
        return true;
    }

    /// <summary>The key without its text, <c>1.1:1.3</c>: how a file of one text writes it.</summary>
    public string Own => $"{Verse}.{Position}";

    public override string ToString() => $"{Text} {Own}";

    /// <summary>The shortest shape a reader standing at <paramref name="context"/> still reads unambiguously.</summary>
    public string ToString(WordKey context)
    {
        if (context.Text != Text)
        {
            return ToString();
        }

        var known = context.Verse;
        if (known == Verse)
        {
            return $".{Position}";
        }

        if (known.Book == Verse.Book && known.Chapter == Verse.Chapter && known.ChapterLabel == Verse.ChapterLabel)
        {
            return $"{Verse.VerseOnly}.{Position}";
        }

        return known.Book == Verse.Book ? $"{Verse.ChapterAndVerse}.{Position}" : Own;
    }

    /// <summary>Text (ordinal), verse, position.</summary>
    public int CompareTo(WordKey other)
    {
        var order = string.CompareOrdinal(Text, other.Text);
        if (order == 0) order = Verse.CompareTo(other.Verse);
        if (order == 0) order = Position.CompareTo(other.Position);
        return order;
    }

    public static bool operator <(WordKey left, WordKey right) => left.CompareTo(right) < 0;
    public static bool operator >(WordKey left, WordKey right) => left.CompareTo(right) > 0;
    public static bool operator <=(WordKey left, WordKey right) => left.CompareTo(right) <= 0;
    public static bool operator >=(WordKey left, WordKey right) => left.CompareTo(right) >= 0;
}
