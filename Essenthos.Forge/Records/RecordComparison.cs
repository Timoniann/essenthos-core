using System.Text;
using Npgsql;

namespace Essenthos.Core.Records;

/// <summary>
/// Two databases compared table by table by natural key: every row is written with its row ids replaced by
/// what the row they point at is (a record by its slug, an event by its slug, any other row by its own
/// columns in turn), so a database loaded from the files and the one they were exported from compare
/// whatever ids each gave. Only the columns both databases have are compared; the rest are named.
/// </summary>
internal static class RecordComparison
{
    /// <summary>The tables whose rows are known by one column of their own.</summary>
    private static readonly Dictionary<string, string> NaturalKeys = new(StringComparer.Ordinal)
    {
        ["entity"] = "slug",
        ["event"] = "slug",
        ["period"] = "slug",
        ["occupation"] = "slug",
        ["chronology"] = "slug",
        ["topic"] = "slug",
        ["period_authority"] = "periodo_id",
        ["word"] = "id",
    };

    public sealed record TableResult(string Table, long Before, long After, List<string> OnlyBefore, List<string> OnlyAfter, List<string> Uncompared);

    public static async Task<List<TableResult>> Compare(string before, string after, IEnumerable<string> tables, CancellationToken cancellationToken)
    {
        await using var a = new NpgsqlConnection(before);
        await using var b = new NpgsqlConnection(after);
        await a.OpenAsync(cancellationToken);
        await b.OpenAsync(cancellationToken);
        var columnsA = await Columns(a, cancellationToken);
        var columnsB = await Columns(b, cancellationToken);
        var foreignKeys = await ForeignKeys(a, cancellationToken);
        var results = new List<TableResult>();
        foreach (var table in tables)
        {
            var hereA = columnsA.GetValueOrDefault(table) ?? [];
            var hereB = columnsB.GetValueOrDefault(table) ?? [];
            var shared = hereA.Where(hereB.Contains).ToList();
            var uncompared = hereA.Except(hereB).Select(c => "-" + c).Concat(hereB.Except(hereA).Select(c => "+" + c)).ToList();
            if (shared.Count == 0)
            {
                results.Add(new(table, -1, -1, [], [], uncompared));
                continue;
            }

            var sql = $"SELECT {Projection(table, "t0", shared, foreignKeys, columnsA, columnsB, 0)} FROM {table} t0";
            var rowsA = await Rows(a, sql, cancellationToken);
            var rowsB = await Rows(b, sql, cancellationToken);
            var (onlyA, onlyB) = Difference(rowsA, rowsB);
            results.Add(new(table, rowsA.Count, rowsB.Count, onlyA, onlyB, uncompared));
        }

        return results;
    }

    private static string Projection(
        string table, string alias, List<string> columns, Dictionary<(string, string), string> foreignKeys,
        Dictionary<string, List<string>> columnsA, Dictionary<string, List<string>> columnsB, int depth)
    {
        var parts = new List<string>();
        foreach (var column in columns.Where(c => c != "id"))
        {
            if (foreignKeys.TryGetValue((table, column), out var target) && depth < 4)
            {
                var inner = $"t{depth + 1}";
                var expression = NaturalKeys.TryGetValue(target, out var key)
                    ? $"{inner}.{key}::text"
                    : Projection(target, inner, [.. (columnsA.GetValueOrDefault(target) ?? []).Where(c => (columnsB.GetValueOrDefault(target) ?? []).Contains(c))],
                        foreignKeys, columnsA, columnsB, depth + 1);
                parts.Add($"(SELECT {expression} FROM {target} {inner} WHERE {inner}.id = {alias}.{column})");
            }
            else
            {
                parts.Add($"{alias}.{column}::text");
            }
        }

        return $"concat_ws(E'\\x1f', {string.Join(", ", parts.Select(p => $"coalesce({p}, '\\N')"))})";
    }

    private static (List<string> OnlyA, List<string> OnlyB) Difference(List<string> a, List<string> b)
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var row in a)
        {
            counts[row] = counts.GetValueOrDefault(row) + 1;
        }

        var onlyB = new List<string>();
        foreach (var row in b)
        {
            if (counts.TryGetValue(row, out var n) && n > 0)
            {
                counts[row] = n - 1;
            }
            else
            {
                onlyB.Add(row);
            }
        }

        var onlyA = counts.Where(p => p.Value > 0).SelectMany(p => Enumerable.Repeat(p.Key, p.Value)).ToList();
        onlyA.Sort(StringComparer.Ordinal);
        onlyB.Sort(StringComparer.Ordinal);
        return (onlyA, onlyB);
    }

    private static async Task<List<string>> Rows(NpgsqlConnection db, string sql, CancellationToken cancellationToken)
    {
        var rows = new List<string>();
        await using var command = new NpgsqlCommand(sql, db) { CommandTimeout = 0 };
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(reader.GetString(0));
        }

        return rows;
    }

    private static async Task<Dictionary<string, List<string>>> Columns(NpgsqlConnection db, CancellationToken cancellationToken)
    {
        var columns = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        await using var command = new NpgsqlCommand(
            "SELECT table_name, column_name FROM information_schema.columns WHERE table_schema = 'public' ORDER BY table_name, ordinal_position", db);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var table = reader.GetString(0);
            if (!columns.TryGetValue(table, out var list))
            {
                columns[table] = list = [];
            }

            list.Add(reader.GetString(1));
        }

        return columns;
    }

    private static async Task<Dictionary<(string, string), string>> ForeignKeys(NpgsqlConnection db, CancellationToken cancellationToken)
    {
        var keys = new Dictionary<(string, string), string>();
        await using var command = new NpgsqlCommand(
            """
            SELECT c.conrelid::regclass::text, a.attname, c.confrelid::regclass::text
            FROM pg_constraint c JOIN pg_attribute a ON a.attrelid = c.conrelid AND a.attnum = c.conkey[1]
            WHERE c.contype = 'f' AND array_length(c.conkey, 1) = 1 AND c.connamespace = 'public'::regnamespace
            """, db);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            keys[(reader.GetString(0), reader.GetString(1))] = reader.GetString(2);
        }

        return keys;
    }

    /// <summary>The comparison as a file a person reads: a line per table, then examples of what differs.</summary>
    public static string Report(List<TableResult> results, int examples)
    {
        var text = new StringBuilder();
        text.AppendLine("table\tbefore\tafter\tonly_before\tonly_after\tuncompared_columns");
        foreach (var r in results)
        {
            text.AppendLine($"{r.Table}\t{r.Before}\t{r.After}\t{r.OnlyBefore.Count}\t{r.OnlyAfter.Count}\t{string.Join(' ', r.Uncompared)}");
        }

        foreach (var r in results.Where(r => r.OnlyBefore.Count + r.OnlyAfter.Count > 0))
        {
            text.AppendLine();
            text.AppendLine($"## {r.Table}");
            foreach (var row in r.OnlyBefore.Take(examples))
            {
                text.AppendLine("- " + row.Replace('\x1f', '|'));
            }

            foreach (var row in r.OnlyAfter.Take(examples))
            {
                text.AppendLine("+ " + row.Replace('\x1f', '|'));
            }
        }

        return text.ToString();
    }
}
