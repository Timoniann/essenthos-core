using System.Collections.Concurrent;
using System.Diagnostics;
using Npgsql;

namespace Essenthos.Core.Loading.Marks;

/// <summary>
/// Every table's rows summed — <c>count(*)</c> and the sum of a 64-bit hash of each row as text, ids
/// included — kept in <c>table_fingerprint</c> and taken again only for a table something has written
/// since. What has written a table is counted by a statement trigger on every table
/// (<c>note_table_write</c>), in the writing transaction, so the count moves exactly when a write can
/// be seen; a kept sum is stale when the count, the table's file or its columns moved.
///
/// <para>
/// A sum and the count it was taken at are read in one statement, so one snapshot: a write committed
/// while the sum is taken is either in both or in neither.
/// </para>
/// </summary>
internal sealed class TableFingerprints(string connectionString, int sessions, ILogger logger)
{
    /// <summary>The bookkeeping of the marks themselves, which no step reads and nothing watches.</summary>
    public static readonly IReadOnlySet<string> Unwatched =
        new HashSet<string>(StringComparer.Ordinal) { "table_write", "table_fingerprint", "load_step_mark" };

    /// <summary>The applied migrations: no trigger, so summed whenever asked, and read by every step.</summary>
    public const string Migrations = "__EFMigrationsHistory";

    private readonly ConcurrentDictionary<string, Kept> _kept = new(StringComparer.Ordinal);
    private Dictionary<string, (long FileNode, string Columns)> _catalog = new(StringComparer.Ordinal);

    /// <summary>The tables a step can read: every table of the schema but the marks' own.</summary>
    public IReadOnlySet<string> Tables { get; private set; } = new HashSet<string>(StringComparer.Ordinal);

    /// <summary>How long the sums taken so far took, and how many there were.</summary>
    public (TimeSpan Took, int Tables) Spent => (TimeSpan.FromTicks(Interlocked.Read(ref _spentTicks)), _summed);

    private long _spentTicks;
    private int _summed;

    /// <summary>
    /// Puts the trigger on any table that lacks it (and forgets what was kept for those), folds the write
    /// counts, and reads what is kept. Returns the tables that were not watched.
    /// </summary>
    public async Task<IReadOnlyList<string>> Open(CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        var unwatched = new List<string>();
        await using (var command = new NpgsqlCommand("SELECT * FROM watch_table_writes()", connection))
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                unwatched.Add(reader.GetString(0));
            }
        }

        await using (var command = new NpgsqlCommand(
                         """
                         WITH gone AS (DELETE FROM table_write RETURNING table_name, weight)
                         INSERT INTO table_write (table_name, weight)
                         SELECT table_name, sum(weight) FROM gone GROUP BY table_name
                         """,
                         connection))
        {
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        if (unwatched.Count > 0)
        {
            await using var forget = new NpgsqlCommand("DELETE FROM table_fingerprint WHERE table_name = ANY(@tables)", connection);
            forget.Parameters.AddWithValue("tables", unwatched.ToArray());
            await forget.ExecuteNonQueryAsync(cancellationToken);
        }

        _kept.Clear();
        await using (var command = new NpgsqlCommand(
                         "SELECT table_name, file_node, columns, writes, rows, fingerprint FROM table_fingerprint", connection))
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                _kept[reader.GetString(0)] = new Kept(
                    reader.GetInt64(1), reader.GetString(2), reader.GetInt64(3), $"{reader.GetInt64(4)}:{reader.GetString(5)}");
            }
        }

        await ReadCatalog(connection, cancellationToken);
        return unwatched;
    }

    /// <summary>The sums of the tables named, each taken again if anything could have moved it since it was kept.</summary>
    public async Task<IReadOnlyDictionary<string, string>> Of(IEnumerable<string> tables, CancellationToken cancellationToken)
    {
        var wanted = tables.Distinct(StringComparer.Ordinal).ToList();
        await using (var connection = new NpgsqlConnection(connectionString))
        {
            await connection.OpenAsync(cancellationToken);
            await ReadCatalog(connection, cancellationToken);
            var writes = await Writes(connection, cancellationToken);
            var stale = wanted
                .Where(table => !Fresh(table, writes))
                .ToList();
            if (stale.Count > 0)
            {
                await Sum(stale, cancellationToken);
            }
        }

        return wanted.ToDictionary(
            table => table,
            table => _catalog.ContainsKey(table) && _kept.TryGetValue(table, out var kept) ? kept.Fingerprint : "absent",
            StringComparer.Ordinal);
    }

    /// <summary>How many transactions have written each table, as can be seen now.</summary>
    public async Task<Dictionary<string, long>> Writes(CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        return await Writes(connection, cancellationToken);
    }

    private static async Task<Dictionary<string, long>> Writes(NpgsqlConnection connection, CancellationToken cancellationToken)
    {
        var writes = new Dictionary<string, long>(StringComparer.Ordinal);
        await using var command = new NpgsqlCommand(
            "SELECT table_name, sum(weight)::bigint FROM table_write GROUP BY table_name", connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            writes[reader.GetString(0)] = reader.GetInt64(1);
        }

        return writes;
    }

    private bool Fresh(string table, IReadOnlyDictionary<string, long> writes) =>
        table != Migrations
        && _catalog.TryGetValue(table, out var now)
        && _kept.TryGetValue(table, out var kept)
        && kept.FileNode == now.FileNode
        && kept.Columns == now.Columns
        && kept.Writes == writes.GetValueOrDefault(table);

    private async Task ReadCatalog(NpgsqlConnection connection, CancellationToken cancellationToken)
    {
        var catalog = new Dictionary<string, (long, string)>(StringComparer.Ordinal);
        await using var command = new NpgsqlCommand(
            """
            SELECT c.relname, c.relfilenode::bigint,
                   md5(string_agg(a.attname || ' ' || format_type(a.atttypid, a.atttypmod), ',' ORDER BY a.attnum))
            FROM pg_class c
            JOIN pg_namespace n ON n.oid = c.relnamespace
            JOIN pg_attribute a ON a.attrelid = c.oid AND a.attnum > 0 AND NOT a.attisdropped
            WHERE n.nspname = 'public' AND c.relkind = 'r'
            GROUP BY c.relname, c.relfilenode
            """,
            connection);
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                var name = reader.GetString(0);
                if (!Unwatched.Contains(name))
                {
                    catalog[name] = (reader.GetInt64(1), reader.GetString(2));
                }
            }
        }

        _catalog = catalog;
        Tables = catalog.Keys.ToHashSet(StringComparer.Ordinal);
    }

    /// <summary>Takes the sums of the tables given, the largest first, over several sessions at once.</summary>
    private async Task Sum(IReadOnlyList<string> tables, CancellationToken cancellationToken)
    {
        var started = Stopwatch.GetTimestamp();
        var sizes = new Dictionary<string, long>(StringComparer.Ordinal);
        await using (var connection = new NpgsqlConnection(connectionString))
        {
            await connection.OpenAsync(cancellationToken);
            await using var command = new NpgsqlCommand(
                """
                SELECT c.relname, pg_table_size(c.oid) FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace
                WHERE n.nspname = 'public' AND c.relname = ANY(@tables) AND c.relkind = 'r'
                """,
                connection);
            command.Parameters.AddWithValue("tables", tables.ToArray());
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                sizes[reader.GetString(0)] = reader.GetInt64(1);
            }
        }

        var queue = new ConcurrentQueue<string>(tables
            .Where(sizes.ContainsKey)
            .OrderByDescending(table => sizes[table]));
        var workers = Enumerable.Range(0, Math.Max(1, Math.Min(sessions, queue.Count))).Select(async _ =>
        {
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken);
            while (queue.TryDequeue(out var table))
            {
                await SumOne(connection, table, cancellationToken);
            }
        });
        await Task.WhenAll(workers);

        var took = Stopwatch.GetElapsedTime(started);
        Interlocked.Add(ref _spentTicks, took.Ticks);
        Interlocked.Add(ref _summed, sizes.Count);
        logger.Log(
            tables.Any(table => table != Migrations) ? LogLevel.Information : LogLevel.Debug,
            "Summed {Count} table(s) in {Seconds:F1} s: {Tables}",
            sizes.Count, took.TotalSeconds,
            string.Join(", ", tables.Where(sizes.ContainsKey).Take(12)) + (sizes.Count > 12 ? ", ..." : ""));
    }

    private async Task SumOne(NpgsqlConnection connection, string table, CancellationToken cancellationToken)
    {
        var quoted = "\"" + table.Replace("\"", "\"\"") + "\"";
        var watched = table != Migrations;
        await using var command = new NpgsqlCommand(
            $"""
             SELECT {(watched ? "(SELECT coalesce(sum(weight), 0)::bigint FROM table_write WHERE table_name = @table)" : "0::bigint")},
                    (SELECT relfilenode::bigint FROM pg_class WHERE oid = @qualified::regclass),
                    count(*), coalesce(sum(hashtextextended(x::text, 0)), 0)::text
             FROM public.{quoted} x
             """,
            connection);
        command.Parameters.AddWithValue("table", table);
        command.Parameters.AddWithValue("qualified", "public." + quoted);
        long writes, fileNode, rows;
        string sum;
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            await reader.ReadAsync(cancellationToken);
            writes = reader.GetInt64(0);
            fileNode = reader.GetInt64(1);
            rows = reader.GetInt64(2);
            sum = reader.GetString(3);
        }

        var columns = _catalog.TryGetValue(table, out var now) ? now.Columns : string.Empty;
        var kept = new Kept(fileNode, columns, writes, $"{rows}:{sum}");
        _kept[table] = kept;
        if (!watched)
        {
            return;
        }

        await using var keep = new NpgsqlCommand(
            """
            INSERT INTO table_fingerprint (table_name, file_node, columns, writes, rows, fingerprint, at)
            VALUES (@table, @fileNode, @columns, @writes, @rows, @sum, now())
            ON CONFLICT (table_name) DO UPDATE SET file_node = excluded.file_node, columns = excluded.columns,
                writes = excluded.writes, rows = excluded.rows, fingerprint = excluded.fingerprint, at = excluded.at
            """,
            connection);
        keep.Parameters.AddWithValue("table", table);
        keep.Parameters.AddWithValue("fileNode", fileNode);
        keep.Parameters.AddWithValue("columns", columns);
        keep.Parameters.AddWithValue("writes", writes);
        keep.Parameters.AddWithValue("rows", rows);
        keep.Parameters.AddWithValue("sum", sum);
        await keep.ExecuteNonQueryAsync(cancellationToken);
    }

    private sealed record Kept(long FileNode, string Columns, long Writes, string Fingerprint);
}
