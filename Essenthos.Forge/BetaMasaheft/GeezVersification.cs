using System.Globalization;
using System.Reflection;
using Essenthos.Core.Loading.Frame;

namespace Essenthos.Core.BetaMasaheft;

/// <param name="Book">The canonical book, the same in the Ge'ez and in the Greek.</param>
/// <param name="Chapter">The Ge'ez verse's chapter, as the edition numbers it.</param>
/// <param name="Verse">The Ge'ez verse's number, as the edition numbers it.</param>
/// <param name="Greek">
/// The rows of the frame the Greek verses it answers stand at, the one it stands at first. Empty where
/// the Greek has nothing for it.
/// </param>
/// <param name="Share">
/// Of its words that match either the Greek verses named or the verse at its own number, how much
/// matches the ones named; null where none of its words matched anything.
/// </param>
internal sealed record GeezVersificationLine(
    int Book,
    int Chapter,
    int Verse,
    IReadOnlyList<(int Chapter, int Verse)> Greek,
    string Relation,
    double? Share,
    int Words);

/// <summary>
/// Where the verses of the Ge'ez stand against the Greek in the chapters it numbers otherwise, as
/// <c>Resources/Essenthos/versification/geez.tsv</c> reads them.
///
/// <para>
/// The edition prints the church's division, and a third of its chapters shared with the Greek divide
/// or number their verses otherwise: its 1 Chronicles 7 runs to 44 where the Greek runs to 40, and its
/// 7:34 is the Greek 7:30. Nothing states which Greek verse each answers, so the file is a reading of
/// the corpus itself, made by setting each chapter against the Greek in order by the words the two
/// share; its head says how. Each verse it lists is placed in the frame at the row of the Greek verse
/// it answers, and joined to the texts read beside it as that reading, with the reading's confidence,
/// rather than as a number the frame states.
/// </para>
///
/// <para>
/// It leaves alone the books the church divides as no other edition does: <see cref="GeezVerseMap"/>
/// reads those, and the frame keeps them at their own numbers.
/// </para>
/// </summary>
internal static class GeezVersification
{
    public const string Resource = "Essenthos.Core.BetaMasaheft.GeezVersification.tsv";

    /// <summary>What a verse link drawn from the file says about where it came from.</summary>
    public const string Source =
        "Essenthos, the Ge'ez set against the Greek verse by verse by the words the two share: the edition " +
        "divides these chapters otherwise than the Greek, and no source states which Greek verse each " +
        "verse answers (Resources/Essenthos/versification/geez.tsv)";

    public const string NoCounterpart = "none";

    public static IReadOnlyList<GeezVersificationLine> Lines { get; } = Read();

    /// <summary>Each listed verse by its own book, chapter and verse.</summary>
    public static IReadOnlyDictionary<(int Book, int Chapter, int Verse), GeezVersificationLine> ByVerse { get; } =
        Lines.ToDictionary(line => (line.Book, line.Chapter, line.Verse));

    /// <summary>
    /// Where the frame stands each listed verse: the rows of the Greek verses it answers, the first its
    /// own; empty for a verse the Greek has nothing for, which stands with the verse before it.
    /// </summary>
    public static IReadOnlyDictionary<(int Book, int Chapter, int Verse), IReadOnlyList<CanonicalReference>> Placements
    {
        get;
    } = Lines.ToDictionary(
        line => (line.Book, line.Chapter, line.Verse),
        line => (IReadOnlyList<CanonicalReference>)
            [.. line.Greek.Select(row => new CanonicalReference(line.Book, row.Chapter, row.Verse))]);

    /// <summary>
    /// How sure a verse link over these verses is: the lowest share among them, and for a verse none of
    /// whose words matched anything, the share of the nearest listed verse before it in its chapter,
    /// which is what placed it.
    /// </summary>
    public static double Confidence(IEnumerable<GeezVersificationLine> lines) =>
        lines.Select(Share).DefaultIfEmpty(0).Min();

    private static double Share(GeezVersificationLine line)
    {
        if (line.Share is { } share)
        {
            return share;
        }

        var before = Lines
            .Where(other => other.Book == line.Book && other.Chapter == line.Chapter && other.Verse < line.Verse
                            && other.Share is not null)
            .OrderByDescending(other => other.Verse)
            .FirstOrDefault();
        var after = Lines
            .Where(other => other.Book == line.Book && other.Chapter == line.Chapter && other.Verse > line.Verse
                            && other.Share is not null)
            .OrderBy(other => other.Verse)
            .FirstOrDefault();
        return (before ?? after)?.Share ?? 0;
    }

    private static List<GeezVersificationLine> Read()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(Resource)
                           ?? throw new InvalidOperationException(
                               $"{Resource} is not embedded in the Forge; Resources/Essenthos/versification/geez.tsv " +
                               "is listed as an EmbeddedResource in its project file.");
        using var reader = new StreamReader(stream);
        return Parse(reader.ReadToEnd());
    }

    /// <summary>
    /// One verse per line, tab-separated: the book, the Ge'ez verse, the Greek rows (<c>7:30</c>,
    /// <c>7:9-10</c>, several joined by <c>;</c>, or <c>-</c>), the relation, the share or <c>-</c>,
    /// and how many words matched.
    /// </summary>
    internal static List<GeezVersificationLine> Parse(string text)
    {
        var lines = new List<GeezVersificationLine>();
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
            if (fields.Length < 6)
            {
                throw new FormatException(
                    $"geez.tsv line {number} has {fields.Length} fields where six are expected: the book, the " +
                    "Ge'ez verse, the Greek rows or -, the relation, the share or -, and the number of words matched.");
            }

            var (chapter, verse) = Address(fields[1], number);
            var greek = fields[2] == "-" ? [] : Rows(fields[2], number);
            if ((greek.Count == 0) != (fields[3] == NoCounterpart))
            {
                throw new FormatException(
                    $"geez.tsv line {number}: a verse is '{NoCounterpart}' exactly when it names no Greek verse.");
            }

            lines.Add(new GeezVersificationLine(
                int.Parse(fields[0], CultureInfo.InvariantCulture),
                chapter,
                verse,
                greek,
                fields[3],
                fields[4] == "-" ? null : double.Parse(fields[4], CultureInfo.InvariantCulture),
                int.Parse(fields[5], CultureInfo.InvariantCulture)));
        }

        return lines;
    }

    private static (int Chapter, int Verse) Address(string written, int line)
    {
        var colon = written.IndexOf(':');
        if (colon < 0)
        {
            throw new FormatException($"geez.tsv line {line}: \"{written}\" is not chapter:verse.");
        }

        return (int.Parse(written[..colon], CultureInfo.InvariantCulture),
            int.Parse(written[(colon + 1)..], CultureInfo.InvariantCulture));
    }

    private static List<(int Chapter, int Verse)> Rows(string written, int line)
    {
        var rows = new List<(int, int)>();
        foreach (var part in written.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var colon = part.IndexOf(':');
            if (colon < 0)
            {
                throw new FormatException(
                    $"geez.tsv line {line}: \"{part}\" is not chapter:verse or chapter:first-last.");
            }

            var chapter = int.Parse(part[..colon], CultureInfo.InvariantCulture);
            var range = part[(colon + 1)..].Split('-');
            var first = int.Parse(range[0], CultureInfo.InvariantCulture);
            var last = range.Length > 1 ? int.Parse(range[1], CultureInfo.InvariantCulture) : first;
            for (var verse = first; verse <= last; verse++)
            {
                rows.Add((chapter, verse));
            }
        }

        return rows;
    }
}
