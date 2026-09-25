using System.Globalization;
using System.Reflection;

namespace Essenthos.Core.BetaMasaheft;

/// <param name="Book">The Ge'ez book as this corpus numbers it: Messale is 91, Tägsas 92.</param>
/// <param name="From">The Ge'ez verses, in the book's own chapter and verse.</param>
/// <param name="To">
/// The canonical addresses the Greek holds the same passage at, in the frame's book. Empty where the
/// Ge'ez has a passage the Greek does not.
/// </param>
/// <param name="Confidence">How sure the reading is: 0.9 where the two plainly say the same thing.</param>
internal sealed record GeezVerseMapLine(
    int Book,
    IReadOnlyList<(int Chapter, int Verse)> From,
    IReadOnlyList<(int Book, int Chapter, int Verse)> To,
    double Confidence);

/// <summary>
/// Which verses of the Ge'ez books divided their own way hold the passage a verse of the Greek
/// holds: Esther, Wisdom, the Letter of Jeremiah, and Messale and Tägsas against Proverbs.
///
/// <para>
/// **Nobody states this.** No versification scheme knows the church's division of these books, and
/// the frame places them at their own numbers, which for Esther and Wisdom are the Greek's numbers
/// for other passages. So the correspondence was worked out by reading the Ge'ez against the
/// Greek, verse by verse, and it is written as what it is: a reading, with a confidence, by a
/// language model. Every line of <c>GeezVerseMap.tsv</c> is one statement, and the file's head
/// says how it was made.
/// </para>
///
/// <para>
/// The map is to the frame's rows rather than to one Greek text's verses, because a row is what
/// every text of the corpus stands on. It was read against the Greek that stands at those rows as
/// the frame means them: Brenton's, and in Proverbs Swete's, since Brenton gathers Proverbs
/// 30:1-14 at 24:22 where Swete stands at the Hebrew's rows.
/// </para>
/// </summary>
internal static class GeezVerseMap
{
    public const string Resource = "Essenthos.Core.BetaMasaheft.GeezVerseMap.tsv";

    /// <summary>What a verse link drawn from the map says about where it came from.</summary>
    public const string Source =
        "read verse by verse against the Greek, Brenton's or in Proverbs Swete's, by Claude, a language " +
        "model, in 2026: the church's " +
        "division of this book is not in any versification scheme, so no source states which Greek " +
        "verse a verse of it answers";

    /// <summary>The Ge'ez books the map is the only placement for: a verse of them it has no line for is joined to nothing.</summary>
    public static IReadOnlySet<int> Books { get; } = new HashSet<int> { 17, 75, 76, 91, 92 };

    public static IReadOnlyList<GeezVerseMapLine> Lines { get; } = Read();

    /// <summary>The canonical addresses each mapped Ge'ez verse is read against, by its own book, chapter and verse.</summary>
    public static IReadOnlyDictionary<(int Book, int Chapter, int Verse), IReadOnlyList<(int Book, int Chapter, int Verse)>>
        Addresses { get; } = Lines
        .SelectMany(line => line.From.Select(verse => (Key: (line.Book, verse.Chapter, verse.Verse), line.To)))
        .GroupBy(entry => entry.Key)
        .ToDictionary(
            group => group.Key,
            group => (IReadOnlyList<(int, int, int)>)[.. group.SelectMany(entry => entry.To).Distinct()]);

    private static List<GeezVerseMapLine> Read()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(Resource)
                           ?? throw new InvalidOperationException(
                               $"{Resource} is not embedded in the Forge; it is listed as an EmbeddedResource in its project file.");
        using var reader = new StreamReader(stream);
        return Parse(reader.ReadToEnd());
    }

    /// <summary>
    /// One statement per line, tab-separated: the Ge'ez book, its verses, the frame's book, the Greek
    /// addresses, the confidence. Verses are <c>3:4</c>, <c>3:4-7</c>, or several of those joined by
    /// <c>;</c>; the Greek is <c>-</c> where there is none.
    /// </summary>
    internal static List<GeezVerseMapLine> Parse(string text)
    {
        var lines = new List<GeezVerseMapLine>();
        var number = 0;
        foreach (var raw in text.Split('\n'))
        {
            number++;
            var line = raw.TrimEnd('\r');
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            var fields = line.Split('\t');
            if (fields.Length < 5)
            {
                throw new FormatException(
                    $"GeezVerseMap.tsv line {number} has {fields.Length} fields where five are expected: the Ge'ez " +
                    "book, its verses, the frame's book, the Greek verses or -, and a confidence, separated by tabs.");
            }

            var book = int.Parse(fields[0], CultureInfo.InvariantCulture);
            var toBook = int.Parse(fields[2], CultureInfo.InvariantCulture);
            lines.Add(new GeezVerseMapLine(
                book,
                Verses(fields[1], number),
                fields[3] == "-" ? [] : [.. Verses(fields[3], number).Select(verse => (toBook, verse.Chapter, verse.Verse))],
                double.Parse(fields[4], CultureInfo.InvariantCulture)));
        }

        return lines;
    }

    private static List<(int Chapter, int Verse)> Verses(string written, int line)
    {
        var verses = new List<(int, int)>();
        foreach (var part in written.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var colon = part.IndexOf(':');
            if (colon < 0)
            {
                throw new FormatException(
                    $"GeezVerseMap.tsv line {line}: \"{part}\" is not chapter:verse or chapter:first-last.");
            }

            var chapter = int.Parse(part[..colon], CultureInfo.InvariantCulture);
            var range = part[(colon + 1)..].Split('-');
            var first = int.Parse(range[0], CultureInfo.InvariantCulture);
            var last = range.Length > 1 ? int.Parse(range[1], CultureInfo.InvariantCulture) : first;
            for (var verse = first; verse <= last; verse++)
            {
                verses.Add((chapter, verse));
            }
        }

        return verses;
    }
}
