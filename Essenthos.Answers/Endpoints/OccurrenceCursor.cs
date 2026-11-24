using System.Globalization;

namespace Essenthos.Core.Endpoints;

/// <summary>
/// Where a list of a number's places stands, as the list orders them: the text, the word's place in the shared frame, its
/// position in its verse, and its id. Written <c>KJV:19.119.1.3.4503599627370495</c>; a page asked for <c>after</c> one starts
/// at the next place, and one asked for <c>before</c> ends at the place before it, however deep in the list it is.
/// </summary>
internal sealed record OccurrenceCursor(string Corpus, int Book, int Chapter, int Verse, int Position, long Id)
{
    public override string ToString() =>
        string.Create(CultureInfo.InvariantCulture, $"{Corpus}:{Book}.{Chapter}.{Verse}.{Position}.{Id}");

    public static bool TryParse(string? text, out OccurrenceCursor? cursor)
    {
        cursor = null;
        if (text is null)
        {
            return true;
        }

        var colon = text.LastIndexOf(':');
        var parts = colon > 0 ? text[(colon + 1)..].Split('.') : [];
        if (parts.Length != 5
            || !int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var book)
            || !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var chapter)
            || !int.TryParse(parts[2], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var verse)
            || !int.TryParse(parts[3], NumberStyles.None, CultureInfo.InvariantCulture, out var position)
            || !long.TryParse(parts[4], NumberStyles.None, CultureInfo.InvariantCulture, out var id))
        {
            return false;
        }

        cursor = new OccurrenceCursor(text[..colon], book, chapter, verse, position, id);
        return true;
    }

    public static OccurrenceCursor Of(StrongOccurrenceResponse item) =>
        new(item.Corpus, item.BookOrdinal, item.Chapter, item.Verse, item.Position, item.WordId);
}

/// <summary>
/// Which page of a list of places to answer: <paramref name="Take"/> places after <paramref name="After"/>, before
/// <paramref name="Before"/>, or from the <paramref name="Skip"/>th on where neither is given. Counting places off from the
/// start reads every place before the page, so <c>skip</c> is kept only for addresses written before the cursors.
/// </summary>
internal sealed record OccurrencePaging(int Skip, int Take, OccurrenceCursor? After = null, OccurrenceCursor? Before = null);
