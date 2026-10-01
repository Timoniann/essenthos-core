using System.Diagnostics;
using System.Globalization;
using System.Text;
using Npgsql;

namespace Essenthos.Core.Loading;

/// <summary>
/// VACUUM (ANALYZE) of the tables a step left worn: the ones whose dead rows, or rows changed since
/// the planner last measured them, reach a share of what they hold.
///
/// <para>
/// A step that replaces a pair's links deletes as many rows as it writes, and Postgres reclaims
/// neither the space nor the planner's picture of the table until a vacuum gets round to it. Left to
/// autovacuum, a day of loads left 1.3 million dead rows in each of link and link_word, and the
/// next step planned its joins against the table as it had been that morning. Running it as the
/// last thing a writing step does costs seconds on a table that changed little and is the only
/// time the change is known to have happened.
/// </para>
///
/// <para>
/// The counts come from <c>pg_stat_user_tables</c>, which a backend reports when it is idle or
/// gone, so the pool is emptied first: the connections that did the writing close, and what they
/// changed is in the counts this reads.
/// </para>
/// </summary>
internal static class Maintenance
{
    /// <summary>Below this many rows a table is never worth a pass of its own.</summary>
    private const long FewRows = 10_000;

    /// <summary>The share of a table's live rows that has to be dead or changed before it is vacuumed.</summary>
    private const double WornShare = 0.02;

    private const string Worn =
        """
        SELECT relname, n_live_tup, n_dead_tup, n_mod_since_analyze
        FROM pg_stat_user_tables
        WHERE schemaname = current_schema()
          AND (cardinality(@tables) > 0 AND relname = ANY(@tables)
               OR cardinality(@tables) = 0
                  AND greatest(n_dead_tup, n_mod_since_analyze) >= @few
                  AND greatest(n_dead_tup, n_mod_since_analyze) >= @share * greatest(n_live_tup, 1))
        ORDER BY greatest(n_dead_tup, n_mod_since_analyze) DESC
        """;

    /// <param name="tables">The tables to vacuum whatever their counts; none for every worn one.</param>
    public static async Task<string> Tidy(
        string connectionString,
        IReadOnlyCollection<string> tables,
        CancellationToken cancellationToken = default)
    {
        NpgsqlConnection.ClearAllPools();
        var report = new StringBuilder();
        var all = Stopwatch.StartNew();

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        var worn = new List<(string Table, long Live, long Dead, long Changed)>();
        await using (var command = new NpgsqlCommand(Worn, connection))
        {
            command.Parameters.AddWithValue("tables", tables.ToArray());
            command.Parameters.AddWithValue("few", FewRows);
            command.Parameters.AddWithValue("share", WornShare);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                worn.Add((reader.GetString(0), reader.GetInt64(1), reader.GetInt64(2), reader.GetInt64(3)));
            }
        }

        if (tables.Except(worn.Select(table => table.Table)).ToList() is { Count: > 0 } unknown)
        {
            throw new ArgumentException(
                $"{string.Join(", ", unknown)}: no such table in the corpus. Name tables as the schema spells them " +
                "(link, link_word, word_entity), or name none to vacuum every table that needs it.");
        }

        foreach (var (table, live, dead, changed) in worn)
        {
            var clock = Stopwatch.StartNew();
            await using var vacuum = new NpgsqlCommand(
                $"VACUUM (ANALYZE) \"{table.Replace("\"", "\"\"", StringComparison.Ordinal)}\"", connection);
            await vacuum.ExecuteNonQueryAsync(cancellationToken);
            report.AppendLine(CultureInfo.InvariantCulture,
                $"  {table}: {dead:N0} dead and {changed:N0} changed of {live:N0} rows, vacuumed in {clock.Elapsed.TotalSeconds:F1} s");
        }

        return worn.Count == 0
            ? "No table needed a vacuum."
            : $"Vacuumed and analysed {worn.Count} tables in {all.Elapsed.TotalSeconds:F1} s:\n{report}";
    }
}
