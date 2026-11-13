using System.Globalization;
using System.Text;

namespace Essenthos.Core.Corpus;

/// <summary>One place an original stands behind a spelling: where in the shared frame, and the word printed there.</summary>
/// <param name="Position">The word's position in its verse, which decides between two words of one place.</param>
internal readonly record struct RenderingPlace(int Book, int Chapter, int Verse, int Position, string Surface)
{
    public (int, int, int) At => (Book, Chapter, Verse);
}

/// <summary>
/// The text forms the counted renderings are kept in, written by the build and read by the API: the places an original
/// stands at, its links by method, and the links another spelling shares.
///
/// <para>
/// Places are written in order, one entry each, separated by <see cref="Entry"/>. An entry is the place, the word's
/// position and the word printed, separated by <see cref="Field"/>: the place as <c>book.chapter:verse</c>, or
/// <c>chapter:verse</c> where the book is the entry before's, or <c>verse</c> where the chapter is too; the word left out,
/// with its separator, where it is the entry before's. A list of places is long and read rarely, so it is kept short.
/// </para>
/// </summary>
internal static class RenderingPlaces
{
    public const char Entry = '\u001e';

    public const char Field = '\u001f';

    /// <summary>The places quoted under each original before a reader asks for more.</summary>
    public const int Samples = 3;

    /// <summary>How many places a written list holds.</summary>
    public static int Count(string places) => places.Length == 0 ? 0 : places.Count(c => c == Entry) + 1;

    /// <summary>The first <paramref name="count"/> places of a written list, itself a written list.</summary>
    public static string First(string places, int count)
    {
        var at = -1;
        for (var i = 0; i < count; i++)
        {
            at = places.IndexOf(Entry, at + 1);
            if (at < 0)
            {
                return places;
            }
        }

        return places[..at];
    }

    public static string Write(IEnumerable<RenderingPlace> places)
    {
        var text = new StringBuilder();
        var (book, chapter) = (-1, -1);
        var surface = string.Empty;
        foreach (var place in places)
        {
            if (text.Length > 0)
            {
                text.Append(Entry);
            }

            if (place.Book != book)
            {
                text.Append(CultureInfo.InvariantCulture, $"{place.Book}.{place.Chapter}:{place.Verse}");
            }
            else if (place.Chapter != chapter)
            {
                text.Append(CultureInfo.InvariantCulture, $"{place.Chapter}:{place.Verse}");
            }
            else
            {
                text.Append(place.Verse.ToString(CultureInfo.InvariantCulture));
            }

            text.Append(Field).Append(place.Position.ToString(CultureInfo.InvariantCulture));
            if (place.Surface != surface)
            {
                text.Append(Field).Append(place.Surface);
            }

            (book, chapter, surface) = (place.Book, place.Chapter, place.Surface);
        }

        return text.ToString();
    }

    public static List<RenderingPlace> Read(string? places)
    {
        var read = new List<RenderingPlace>();
        if (string.IsNullOrEmpty(places))
        {
            return read;
        }

        var (book, chapter) = (0, 0);
        var surface = string.Empty;
        foreach (var entry in places.Split(Entry))
        {
            var fields = entry.Split(Field);
            var at = fields[0];
            var dot = at.IndexOf('.');
            var colon = at.IndexOf(':');
            if (dot >= 0)
            {
                book = int.Parse(at.AsSpan(0, dot), CultureInfo.InvariantCulture);
            }

            if (colon >= 0)
            {
                chapter = int.Parse(at.AsSpan(dot + 1, colon - dot - 1), CultureInfo.InvariantCulture);
            }

            if (fields.Length > 2)
            {
                surface = fields[2];
            }

            read.Add(new RenderingPlace(
                book,
                chapter,
                int.Parse(at.AsSpan(colon + 1), CultureInfo.InvariantCulture),
                int.Parse(fields[1], CultureInfo.InvariantCulture),
                surface));
        }

        return read;
    }

    /// <summary>How many links each method made, as <c>method count</c> pairs in method order.</summary>
    public static string WriteMethods(IEnumerable<(string Method, int Links)> methods) =>
        string.Join(' ', methods.OrderBy(m => m.Method, StringComparer.Ordinal).Select(m => $"{m.Method} {m.Links.ToString(CultureInfo.InvariantCulture)}"));

    public static IEnumerable<(string Method, int Links)> ReadMethods(string? methods)
    {
        if (string.IsNullOrEmpty(methods))
        {
            yield break;
        }

        var parts = methods.Split(' ');
        for (var i = 0; i + 1 < parts.Length; i += 2)
        {
            yield return (parts[i], int.Parse(parts[i + 1], CultureInfo.InvariantCulture));
        }
    }

    /// <summary>Links by id with their methods, as <c>id method</c> pairs in id order; null where there are none.</summary>
    public static string? WriteLinks(IEnumerable<(long Id, string Method)> links)
    {
        var written = string.Join(' ', links.OrderBy(l => l.Id).Select(l => $"{l.Id.ToString(CultureInfo.InvariantCulture)} {l.Method}"));
        return written.Length == 0 ? null : written;
    }

    public static IEnumerable<(long Id, string Method)> ReadLinks(string? links)
    {
        if (string.IsNullOrEmpty(links))
        {
            yield break;
        }

        var parts = links.Split(' ');
        for (var i = 0; i + 1 < parts.Length; i += 2)
        {
            yield return (long.Parse(parts[i], CultureInfo.InvariantCulture), parts[i + 1]);
        }
    }
}
