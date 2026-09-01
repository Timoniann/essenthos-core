using System.Globalization;

namespace Essenthos.Core.Loading.Frame;

/// <summary>
/// A place in the shared address space: a canonical book ordinal, a chapter and a verse.
///
/// A chapter's title — a psalm's superscription, which Hebrew numbers as a verse and English does
/// not number at all — is verse <see cref="TitleVerse"/>. Storing it as zero keeps the address an
/// integer triple and puts the title before the first verse in every ordering, which is where it
/// belongs.
/// </summary>
internal readonly record struct CanonicalReference(int Book, int Chapter, int Verse)
{
    public const int TitleVerse = 0;

    private const string TitleMarker = "Title";

    public override string ToString() =>
        Verse == TitleVerse ? $"{Book}.{Chapter}:title" : $"{Book}.{Chapter}:{Verse}";

    /// <summary>
    /// Parses one reference of the form <c>Gen.2:25</c>, <c>Psa.51:Title</c> or <c>Exo.28:29!a</c>.
    /// The verse part is dropped: this address space has no parts, and a source verse that renders
    /// only half of a standard verse still belongs at that verse.
    /// </summary>
    public static bool TryParse(string value, out CanonicalReference reference)
    {
        reference = default;
        var text = value.Trim();
        if (text.Length == 0)
        {
            return false;
        }

        var part = text.IndexOf('!');
        if (part >= 0)
        {
            text = text[..part];
        }

        var dot = text.IndexOf('.');
        var colon = text.IndexOf(':', dot + 1);
        if (dot <= 0 || colon <= dot)
        {
            return false;
        }

        if (!BookCodes.TryGetOrdinal(text[..dot], out var book))
        {
            return false;
        }

        if (!int.TryParse(text.AsSpan(dot + 1, colon - dot - 1), NumberStyles.None, CultureInfo.InvariantCulture,
                out var chapter))
        {
            return false;
        }

        var verseText = text[(colon + 1)..];
        if (verseText.Equals(TitleMarker, StringComparison.OrdinalIgnoreCase))
        {
            reference = new CanonicalReference(book, chapter, TitleVerse);
            return true;
        }

        if (!int.TryParse(verseText, NumberStyles.None, CultureInfo.InvariantCulture, out var verse))
        {
            return false;
        }

        reference = new CanonicalReference(book, chapter, verse);
        return true;
    }

    /// <summary>
    /// Parses a whole cell, which may be one reference, a semicolon list, or a range. The first
    /// reference is the primary placement and the rest are further ones; a range within a chapter
    /// is expanded, and a range crossing a chapter boundary yields its two ends, because the verses
    /// between them cannot be named without knowing how long the chapter is.
    /// </summary>
    public static IReadOnlyList<CanonicalReference> ParseAll(string value)
    {
        var references = new List<CanonicalReference>(2);
        foreach (var piece in value.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            AddPiece(references, piece, references.Count > 0 ? references[^1] : default);
        }

        return references;
    }

    private static void AddPiece(List<CanonicalReference> references, string piece, CanonicalReference previous)
    {
        var dash = piece.IndexOf('-');
        if (dash < 0)
        {
            if (TryParseRelative(piece, previous, out var single))
            {
                references.Add(single);
            }

            return;
        }

        if (!TryParseRelative(piece[..dash], previous, out var start) ||
            !TryParseRelative(piece[(dash + 1)..], start, out var end))
        {
            return;
        }

        references.Add(start);
        if (start.Book != end.Book || start.Chapter != end.Chapter)
        {
            references.Add(end);
            return;
        }

        for (var verse = start.Verse + 1; verse <= end.Verse; verse++)
        {
            references.Add(start with { Verse = verse });
        }
    }

    /// <summary>
    /// The second half of a range or list often omits what does not change — <c>1Ki.22:43-44</c>
    /// and <c>Gen.5:32; 6:1</c> — so a bare number is a verse in the same chapter and a bare
    /// <c>chapter:verse</c> is in the same book.
    /// </summary>
    private static bool TryParseRelative(string piece, CanonicalReference previous, out CanonicalReference reference)
    {
        var text = piece.Trim();
        if (TryParse(text, out reference))
        {
            return true;
        }

        if (previous == default)
        {
            return false;
        }

        var part = text.IndexOf('!');
        if (part >= 0)
        {
            text = text[..part];
        }

        var colon = text.IndexOf(':');
        if (colon < 0)
        {
            if (!int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var verse))
            {
                return false;
            }

            reference = previous with { Verse = verse };
            return true;
        }

        if (!int.TryParse(text.AsSpan(0, colon), NumberStyles.None, CultureInfo.InvariantCulture, out var chapter) ||
            !int.TryParse(text.AsSpan(colon + 1), NumberStyles.None, CultureInfo.InvariantCulture, out var number))
        {
            return false;
        }

        reference = previous with { Chapter = chapter, Verse = number };
        return true;
    }
}
