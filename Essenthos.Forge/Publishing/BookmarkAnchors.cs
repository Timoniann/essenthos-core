using System.Globalization;
using System.Text;

namespace Essenthos.Core.Publishing;

/// <summary>
/// Whether every reader's bookmark still lands on a verse in the release about to be served.
///
/// Bookmarks are addressed canonically (RUL-0185), so a release cannot move one — but it can lose the
/// verse under it: a text withdrawn, a versification corrected so that a verse number stops existing.
/// A bookmark left pointing at nothing — with somebody's comment on it — has quietly lost its place,
/// and that is found here, before the swap, rather than by the reader.
///
/// The bookmarks live in the environment's own accounts database and the release in its incoming corpus
/// database, on the same server; Postgres does not join across databases, so the addresses are read
/// out of one and checked against the other in batches.
/// </summary>
internal static class BookmarkAnchors
{
    /// <summary>Addresses per query, so the statement stays far below any command-line limit.</summary>
    private const int Batch = 400;

    /// <summary>What an anchor is checked by: the text it is about, or empty for any text, and a verse.</summary>
    internal sealed record Point(string Text, int Book, int Chapter, int Verse);

    /// <summary>The addresses no verse of the incoming release answers to, or null when there are no bookmarks to check.</summary>
    public static async Task<IReadOnlyList<Point>?> Unresolved(TargetHost host, ReleaseTarget target, CancellationToken cancellationToken)
    {
        if (target.AppDatabase is not { } app || !await host.DatabaseExists(app, cancellationToken) ||
            await host.Sql(app, "SELECT 1 FROM pg_tables WHERE schemaname = 'public' AND tablename = 'bookmark'", cancellationToken) != "1")
        {
            return null;
        }

        var points = Parse(await host.Sql(app,
            "SELECT DISTINCT coalesce(text, ''), book, chapter, verse FROM bookmark " +
            "UNION SELECT DISTINCT coalesce(text, ''), book, end_chapter, end_verse FROM bookmark",
            cancellationToken));

        var missing = new List<Point>();
        foreach (var chunk in points.Chunk(Batch))
        {
            missing.AddRange(Parse(await host.Sql(target.Incoming, Query(chunk), cancellationToken)));
        }

        return missing;
    }

    /// <summary>psql's unaligned rows, <c>text|book|chapter|verse</c>, one to a line.</summary>
    internal static List<Point> Parse(string rows) =>
        rows.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(line => line.Split('|'))
            .Where(fields => fields.Length == 4)
            .Select(fields => new Point(
                fields[0],
                int.Parse(fields[1], CultureInfo.InvariantCulture),
                int.Parse(fields[2], CultureInfo.InvariantCulture),
                int.Parse(fields[3], CultureInfo.InvariantCulture)))
            .ToList();

    /// <summary>The addresses among these that no verse carries — of the named text, or of any.</summary>
    internal static string Query(IEnumerable<Point> points)
    {
        var values = new StringBuilder();
        foreach (var point in points)
        {
            values.Append(values.Length == 0 ? "" : ", ")
                .Append(CultureInfo.InvariantCulture,
                    $"({TargetHost.Literal(point.Text)}, {point.Book}, {point.Chapter}, {point.Verse})");
        }

        return
            "SELECT a.text, a.book, a.chapter, a.verse " +
            $"FROM (VALUES {values}) AS a(text, book, chapter, verse) " +
            "WHERE NOT EXISTS (SELECT 1 FROM verse_reference r JOIN verse v ON v.id = r.verse_id JOIN text t ON t.id = v.text_id " +
            "WHERE r.canonical_book = a.book AND r.canonical_chapter = a.chapter AND r.canonical_verse = a.verse " +
            "AND (a.text = '' OR lower(t.slug) = lower(a.text)))";
    }
}
