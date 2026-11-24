using System.Globalization;
using Essenthos.Core.Corpus;

namespace Essenthos.Core.Endpoints;

/// <summary>
/// The passages that tell what happened where, read from <c>NarrativePassages.tsv</c> (TSK-1235): the
/// narrative books less their poems, oracles and laws, and the narrative episodes inside the prophets,
/// Job and the letters. A place named with a record in one of these verses is where its story happens;
/// a place named with it in a psalm, an oracle or a letter's greeting is not.
/// </summary>
internal static class NarrativePassages
{
    private const int LastChapter = 999;
    private const int LastVerse = 999;

    /// <summary>One row of the file: a span of verses of one book, story or not, and why.</summary>
    internal sealed record Passage(int Book, string Text, int From, int To, bool Story, string Why)
    {
        public bool Holds(int chapter, int verse) => At(chapter, verse) is var at && at >= From && at <= To;
    }

    private static readonly Lazy<IReadOnlyList<Passage>> Compiled = new(() => Parse(Read()));

    private static readonly Lazy<Dictionary<int, Passage[]>> ByBook = new(() =>
        Compiled.Value.GroupBy(p => p.Book).ToDictionary(g => g.Key, g => g.ToArray()));

    /// <summary>Every row of the compiled file, in its order.</summary>
    public static IReadOnlyList<Passage> All => Compiled.Value;

    /// <summary>Whether a verse tells what happened where: the last row of its book that holds it says.</summary>
    public static bool Tells(int book, int chapter, int verse) =>
        ByBook.Value.TryGetValue(book, out var rows)
        && rows.LastOrDefault(row => row.Holds(chapter, verse)) is { Story: true };

    /// <summary>
    /// The rows of a passages file. A row that cannot be read stops the API rather than quietly
    /// telling no story: a book name it does not know, a passage it cannot parse, a range that runs
    /// backwards, or no reason.
    /// </summary>
    internal static IReadOnlyList<Passage> Parse(string text)
    {
        var rows = new List<Passage>();
        var number = 0;
        foreach (var raw in text.Split('\n'))
        {
            number++;
            var line = raw.TrimEnd('\r');
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            var cells = line.Split('\t');
            if (cells.Length != 4 || cells.Any(string.IsNullOrWhiteSpace))
            {
                throw Bad(number, "needs four cells: book, passage, story or not, and why");
            }

            var book = BookReferences.ResolveOrdinal(cells[0])
                       ?? throw Bad(number, $"names no book the canon knows: \"{cells[0]}\"");
            var (from, to) = Span(cells[1]) ?? throw Bad(number, $"cannot read the passage \"{cells[1]}\"");
            var story = cells[2] switch
            {
                "story" => true,
                "not" => false,
                _ => throw Bad(number, $"says \"{cells[2]}\" where it should say story or not"),
            };
            rows.Add(new Passage(book, cells[1], from, to, story, cells[3]));
        }

        return rows;
    }

    private static int At(int chapter, int verse) => (chapter * (LastVerse + 1)) + verse;

    /// <summary>"all", "36", "36-39", "23:7", "23:7-10" or "36:1-37:21", as the first and last verse it holds.</summary>
    private static (int From, int To)? Span(string passage)
    {
        if (passage == "all")
        {
            return (At(1, 0), At(LastChapter, LastVerse));
        }

        var ends = passage.Split('-');
        if (ends.Length > 2 || Point(ends[0]) is not var (chapter, verse))
        {
            return null;
        }

        var from = At(chapter, verse ?? 0);
        int to;
        if (ends.Length == 1)
        {
            to = At(chapter, verse ?? LastVerse);
        }
        else if (Point(ends[1]) is var (lastChapter, lastVerse))
        {
            // "23:7-10" ends in the same chapter; "36-39" and "36:1-37:21" name their last chapter.
            to = verse is not null && !ends[1].Contains(':')
                ? At(chapter, lastChapter)
                : At(lastChapter, lastVerse ?? LastVerse);
            if (verse is null && lastVerse is not null)
            {
                return null;
            }
        }
        else
        {
            return null;
        }

        return from <= to ? (from, to) : null;
    }

    private static (int Chapter, int? Verse)? Point(string text)
    {
        var parts = text.Split(':');
        if (parts.Length > 2 || !int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var chapter)
                             || chapter is < 1 or > LastChapter)
        {
            return null;
        }

        if (parts.Length == 1)
        {
            return (chapter, null);
        }

        return int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var verse) && verse is >= 1 and <= LastVerse
            ? (chapter, verse)
            : null;
    }

    private static string Read()
    {
        using var stream = typeof(NarrativePassages).Assembly.GetManifestResourceStream("NarrativePassages.tsv")
                           ?? throw new InvalidOperationException("The API was built without Endpoints/NarrativePassages.tsv.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static FormatException Bad(int line, string what) =>
        new($"NarrativePassages.tsv line {line} {what}.");
}
