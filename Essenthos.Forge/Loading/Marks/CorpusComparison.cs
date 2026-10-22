using System.Collections.Concurrent;
using System.Text;
using Npgsql;

namespace Essenthos.Core.Loading.Marks;

/// <summary>
/// Two corpora compared table by table, without changing either: each table's rows summed twice, once
/// whole (ids and times included, as <see cref="TableFingerprints"/> sums them) and once without the
/// columns a rebuild is expected to number or stamp differently — its own key, the columns that point
/// at another table's key, and its times. A table that differs only in the first is the same corpus
/// numbered again; one that differs in the second holds something else.
/// </summary>
internal static class CorpusComparison
{
    public static async Task<IReadOnlyList<TableDifference>> Compare(
        string reference,
        string candidate,
        int sessions,
        CancellationToken cancellationToken)
    {
        var referenceSums = await Sum(reference, sessions, cancellationToken);
        var candidateSums = await Sum(candidate, sessions, cancellationToken);
        return
        [
            .. referenceSums.Keys.Union(candidateSums.Keys)
                .Where(table => !TableFingerprints.Unwatched.Contains(table))
                .Order(StringComparer.Ordinal)
                .Select(table => new TableDifference(
                    table,
                    referenceSums.GetValueOrDefault(table),
                    candidateSums.GetValueOrDefault(table))),
        ];
    }

    /// <summary>Every table's sums, whole and without keys and times, read-only, over several sessions.</summary>
    private static async Task<IReadOnlyDictionary<string, Sums>> Sum(string connectionString, int sessions, CancellationToken cancellationToken)
    {
        var tables = new List<(string Table, long Size, string Content)>();
        await using (var connection = await Open(connectionString, cancellationToken))
        {
            await using var command = new NpgsqlCommand(
                """
                SELECT c.relname, pg_table_size(c.oid),
                       string_agg(
                           CASE WHEN NOT (
                                   a.attnum = ANY (coalesce(pk.conkey, '{}'))
                                   OR EXISTS (SELECT 1 FROM pg_constraint f
                                              WHERE f.conrelid = c.oid AND f.contype = 'f' AND a.attnum = ANY (f.conkey))
                                   OR a.atttypid IN ('timestamp'::regtype, 'timestamptz'::regtype, 'date'::regtype))
                                THEN format('coalesce(%I::text, ''\N'')', a.attname) END,
                           ' || ''|'' || ' ORDER BY a.attnum)
                FROM pg_class c
                JOIN pg_namespace n ON n.oid = c.relnamespace
                JOIN pg_attribute a ON a.attrelid = c.oid AND a.attnum > 0 AND NOT a.attisdropped
                LEFT JOIN pg_constraint pk ON pk.conrelid = c.oid AND pk.contype = 'p'
                WHERE n.nspname = 'public' AND c.relkind = 'r'
                GROUP BY c.relname, c.oid
                """,
                connection);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                tables.Add((reader.GetString(0), reader.GetInt64(1), reader.IsDBNull(2) ? "''" : reader.GetString(2)));
            }
        }

        var sums = new ConcurrentDictionary<string, Sums>(StringComparer.Ordinal);
        var queue = new ConcurrentQueue<(string Table, long Size, string Content)>(tables.OrderByDescending(table => table.Size));
        await Task.WhenAll(Enumerable.Range(0, Math.Max(1, sessions)).Select(async _ =>
        {
            await using var connection = await Open(connectionString, cancellationToken);
            while (queue.TryDequeue(out var table))
            {
                var quoted = "\"" + table.Table.Replace("\"", "\"\"") + "\"";
                await using var command = new NpgsqlCommand(
                    $"""
                     SELECT count(*), coalesce(sum(hashtextextended(x::text, 0)), 0)::text,
                            coalesce(sum(hashtextextended({table.Content}, 0)), 0)::text
                     FROM public.{quoted} x
                     """,
                    connection);
                await using var reader = await command.ExecuteReaderAsync(cancellationToken);
                await reader.ReadAsync(cancellationToken);
                sums[table.Table] = new Sums(reader.GetInt64(0), reader.GetString(1), reader.GetString(2));
            }
        }));
        return sums;
    }

    private static async Task<NpgsqlConnection> Open(string connectionString, CancellationToken cancellationToken)
    {
        var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var readOnly = new NpgsqlCommand("SET default_transaction_read_only = on", connection);
        await readOnly.ExecuteNonQueryAsync(cancellationToken);
        return connection;
    }

    /// <summary>The comparison as lines: every table that differs, and how.</summary>
    public static string Describe(IReadOnlyList<TableDifference> differences)
    {
        var text = new StringBuilder();
        var content = differences.Where(difference => difference.Kind == DifferenceKind.Content).ToList();
        var numbered = differences.Where(difference => difference.Kind == DifferenceKind.Numbering).ToList();
        var missing = differences.Where(difference => difference.Kind == DifferenceKind.Missing).ToList();
        text.AppendLine($"{differences.Count} tables compared: {differences.Count - content.Count - numbered.Count - missing.Count} the same, "
                        + $"{numbered.Count} the same but for ids and times, {content.Count} different, {missing.Count} in one only.");
        foreach (var difference in missing.Concat(content).Concat(numbered))
        {
            text.AppendLine($"  {difference}");
        }

        return text.ToString();
    }

    /// <param name="Rows">How many rows the table holds.</param>
    /// <param name="Whole">The sum of every row's hash, ids and times included.</param>
    /// <param name="Content">The sum without the table's key, the columns pointing at other keys, and its times.</param>
    internal sealed record Sums(long Rows, string Whole, string Content);

    internal enum DifferenceKind
    {
        Same,
        Numbering,
        Content,
        Missing,
    }

    /// <summary>One table in both corpora.</summary>
    internal sealed record TableDifference(string Table, Sums? Reference, Sums? Candidate)
    {
        public DifferenceKind Kind =>
            Reference is null || Candidate is null ? DifferenceKind.Missing
            : Reference == Candidate ? DifferenceKind.Same
            : Reference.Rows == Candidate.Rows && Reference.Content == Candidate.Content ? DifferenceKind.Numbering
            : DifferenceKind.Content;

        public override string ToString() => Kind switch
        {
            DifferenceKind.Missing => $"{Table}: only in the {(Reference is null ? "rebuilt" : "reference")} corpus",
            DifferenceKind.Numbering => $"{Table}: {Reference!.Rows:N0} rows, the same but for ids and times",
            DifferenceKind.Content => $"{Table}: {Reference!.Rows:N0} rows in the reference, {Candidate!.Rows:N0} rebuilt, and they differ",
            _ => $"{Table}: the same",
        };
    }
}
