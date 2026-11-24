using System.Globalization;
using Essenthos.Core.Corpus;

namespace Essenthos.Core.Pages;

/// <summary>
/// Chapters the site once served under one book that are held under another now, read from
/// <c>MovedChapters.tsv</c>. A reader's or a search engine's old address of one leads to the same chapter,
/// and the same verse, where it is held: <c>/uk/verse/ezra/17/1</c> to <c>/uk/verse/nehemiah/7/1</c>.
/// </summary>
internal static class MovedChapters
{
    private const string Resource = "MovedChapters.tsv";

    private static readonly Lazy<IReadOnlyList<Move>> Moves = new(Read);

    private sealed record Move(int Book, int First, int Last, int To, int ToFirst);

    /// <summary>
    /// Where a chapter's or a verse's address leads, its route, verse and anything after kept, or null where
    /// its chapter did not move. <paramref name="segments"/> is the path without the language's prefix.
    /// </summary>
    public static string? Moved(IReadOnlyList<string> segments, Func<string, int?> book)
    {
        if (segments.Count < 3 || segments[0] is not ("read" or "verse") || book(segments[1]) is not { } ordinal
            || !int.TryParse(segments[2], NumberStyles.None, CultureInfo.InvariantCulture, out var chapter))
        {
            return null;
        }

        var move = Moves.Value.FirstOrDefault(move => move.Book == ordinal && chapter >= move.First && chapter <= move.Last);
        return move is null
            ? null
            : "/" + string.Join('/', [segments[0], BookReferences.Slug(move.To),
                (move.ToFirst + chapter - move.First).ToString(CultureInfo.InvariantCulture), .. segments.Skip(3)]);
    }

    private static IReadOnlyList<Move> Read()
    {
        using var stream = typeof(MovedChapters).Assembly.GetManifestResourceStream(Resource)
                           ?? throw new InvalidOperationException($"The API was built without Pages/{Resource}.");
        using var reader = new StreamReader(stream);
        var moves = new List<Move>();
        var number = 0;
        var header = true;
        while (reader.ReadLine() is { } line)
        {
            number++;
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            if (header)
            {
                header = false;
                continue;
            }

            var columns = line.Split('\t');
            var range = columns.Length >= 4 ? columns[1].Split('-') : [];
            if (range.Length != 2 || BookReferences.ResolveOrdinal(columns[0]) is not { } from || BookReferences.ResolveOrdinal(columns[2]) is not { } to
                || !int.TryParse(range[0], NumberStyles.None, CultureInfo.InvariantCulture, out var first)
                || !int.TryParse(range[1], NumberStyles.None, CultureInfo.InvariantCulture, out var last)
                || !int.TryParse(columns[3], NumberStyles.None, CultureInfo.InvariantCulture, out var toFirst))
            {
                throw new FormatException($"{Resource} line {number} is not a book, a chapter range, a book and a chapter.");
            }

            moves.Add(new Move(from, first, last, to, toFirst));
        }

        return moves;
    }
}
