using System.Diagnostics;
using System.Globalization;
using Essenthos.Core.Products.Essx;
using Essenthos.Core.Products.Tsv;
using Npgsql;
using NpgsqlTypes;

namespace Essenthos.Core.Products.Postgres;

/// <summary>How the rows go over the wire: binary COPY straight from the decoded arrays, or text COPY as a TSV would.</summary>
public enum CopyMode
{
    Binary,
    Text,
}

/// <summary>
/// One target column: where a product column's value goes. A key column spreads over several
/// target columns (book, chapter, chapter label, verse, verse label, position); a constant column
/// has no source and the same value on every row, which is how a file of one text names its text.
/// </summary>
public sealed record CopyColumn(string Target, string? Source, int Part = 0, string? Constant = null);

public sealed record CopyResult(long Rows, TimeSpan Elapsed, int Streams, CopyMode Mode)
{
    public override string ToString() => $"{Rows:N0} rows in {Elapsed.TotalSeconds:F1} s over {Streams} {Mode} stream(s)";
}

/// <summary>
/// Loads an ESSX table into a Postgres table with <c>COPY</c>. Row groups are dealt out to several
/// connections, each decoding its groups and copying them in binary, so the file's decoding and
/// the server's insertion run on as many cores as there are streams. Nothing is written to the
/// file; the table is whatever the caller made it, so indexes are the caller's to drop and build.
/// </summary>
public static class EssxCopy
{
    /// <summary>The default mapping: every column to one of the same name, a key column to <c>name_book … name_position</c>.</summary>
    public static IReadOnlyList<CopyColumn> DefaultColumns(EssxTable table, params IEnumerable<(string Name, string Value)> constants)
    {
        var columns = new List<CopyColumn>();
        foreach (var (name, value) in constants)
        {
            columns.Add(new CopyColumn(name, null, Constant: value));
        }

        foreach (var column in table.Columns)
        {
            if (column.Type is ColumnType.VerseKey or ColumnType.WordKey)
            {
                string[] parts = ["book", "chapter", "chapter_label", "verse", "verse_label", "position"];
                var count = column.Type == ColumnType.WordKey ? 6 : 5;
                for (var part = 0; part < count; part++)
                {
                    columns.Add(new CopyColumn($"{column.Name}_{parts[part]}", column.Name, part));
                }
            }
            else
            {
                columns.Add(new CopyColumn(column.Name, column.Name));
            }
        }

        return columns;
    }

    public static async Task<CopyResult> Load(
        string connectionString,
        string path,
        string targetTable,
        IReadOnlyList<CopyColumn>? columns = null,
        int streams = 8,
        CopyMode mode = CopyMode.Binary,
        string? sourceTable = null,
        CancellationToken cancellation = default)
    {
        var watch = Stopwatch.StartNew();
        using var file = EssxReader.Open(path);
        var table = sourceTable is null ? file.Tables[0] : file.Table(sourceTable);
        columns ??= DefaultColumns(table);
        var types = await TargetTypes(connectionString, targetTable, cancellation);
        var plan = columns.Select(column => new Bound(
            column,
            column.Source is null ? -1 : IndexOf(table, column.Source),
            types.TryGetValue(column.Target, out var type) ? type : throw new ArgumentException($"Table {targetTable} has no column '{column.Target}'."))).ToArray();
        var groups = Enumerable.Range(0, table.RowGroups).ToList();
        streams = Math.Clamp(streams, 1, Math.Max(1, groups.Count));
        var copyList = $"COPY {targetTable} ({string.Join(", ", columns.Select(column => Quote(column.Target)))}) FROM STDIN" + (mode == CopyMode.Binary ? " (FORMAT BINARY)" : "");

        var tasks = new List<Task<long>>();
        for (var stream = 0; stream < streams; stream++)
        {
            var mine = groups.Where(g => g % streams == stream).ToList();
            var opener = new NpgsqlDataSourceBuilder(connectionString).Build();
            tasks.Add(mode == CopyMode.Binary
                ? CopyBinary(opener, copyList, path, table.Name, mine, plan, cancellation)
                : CopyText(opener, copyList, path, table.Name, mine, plan, cancellation));
        }

        var counts = await Task.WhenAll(tasks);
        return new CopyResult(counts.Sum(), watch.Elapsed, streams, mode);
    }

    private static int IndexOf(EssxTable table, string name)
    {
        for (var i = 0; i < table.Columns.Count; i++)
        {
            if (table.Columns[i].Name == name) return i;
        }

        throw new ArgumentException($"The file has no column '{name}'; it has {string.Join(", ", table.Columns.Select(column => column.Name))}.");
    }

    private static string Quote(string identifier) => "\"" + identifier.Replace("\"", "\"\"") + "\"";

    private sealed record Bound(CopyColumn Column, int Index, NpgsqlDbType Type);

    private static async Task<Dictionary<string, NpgsqlDbType>> TargetTypes(string connectionString, string table, CancellationToken cancellation)
    {
        var parts = table.Split('.');
        var (schema, name) = parts.Length == 2 ? (parts[0], parts[1]) : ("public", parts[0]);
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellation);
        await using var command = new NpgsqlCommand(
            "SELECT column_name, data_type FROM information_schema.columns WHERE table_schema = $1 AND table_name = $2", connection);
        command.Parameters.AddWithValue(schema);
        command.Parameters.AddWithValue(name.Trim('"'));
        var types = new Dictionary<string, NpgsqlDbType>(StringComparer.Ordinal);
        await using var reader = await command.ExecuteReaderAsync(cancellation);
        while (await reader.ReadAsync(cancellation))
        {
            types[reader.GetString(0)] = reader.GetString(1) switch
            {
                "integer" => NpgsqlDbType.Integer,
                "smallint" => NpgsqlDbType.Smallint,
                "bigint" => NpgsqlDbType.Bigint,
                "boolean" => NpgsqlDbType.Boolean,
                "jsonb" => NpgsqlDbType.Jsonb,
                "json" => NpgsqlDbType.Json,
                _ => NpgsqlDbType.Text,
            };
        }

        if (types.Count == 0)
        {
            throw new ArgumentException($"No table {table} in the database.");
        }

        return types;
    }

    /// <summary>Each stream opens the file itself: a reader's handle is one position, and the streams decode at once.</summary>
    private static async Task<long> CopyBinary(NpgsqlDataSource source, string copy, string path, string tableName, List<int> groups, Bound[] plan, CancellationToken cancellation)
    {
        using var file = EssxReader.Open(path);
        var table = file.Table(tableName);
        await using var connection = await source.OpenConnectionAsync(cancellation);
        await using var importer = await connection.BeginBinaryImportAsync(copy, cancellation);
        long rows = 0;
        foreach (var g in groups)
        {
            var group = table.ReadGroup(g);
            var data = plan.Select(bound => bound.Index < 0 ? null : group.Columns[bound.Index]).ToArray();
            for (var i = 0; i < group.Rows; i++)
            {
                importer.StartRow();
                for (var c = 0; c < plan.Length; c++)
                {
                    var bound = plan[c];
                    if (bound.Column.Constant is { } constant)
                    {
                        importer.Write(constant, NpgsqlDbType.Text);
                        continue;
                    }

                    var column = data[c]!;
                    if (column.IsNull(i))
                    {
                        importer.WriteNull();
                        continue;
                    }

                    switch (column)
                    {
                        case KeyData key:
                            WritePart(importer, key, bound.Column.Part, i, bound.Type);
                            break;
                        case IntData integers:
                            WriteInteger(importer, integers.Values[i], bound.Type);
                            break;
                        case BoolData booleans:
                            importer.Write(booleans.Values[i], NpgsqlDbType.Boolean);
                            break;
                        default:
                            importer.Write(column.Text(i)!, bound.Type);
                            break;
                    }
                }

                rows++;
            }
        }

        await importer.CompleteAsync(cancellation);
        return rows;
    }

    private static void WritePart(NpgsqlBinaryImporter importer, KeyData key, int part, int row, NpgsqlDbType type)
    {
        switch (part)
        {
            case 0: WriteInteger(importer, key.Book.Values[row], type); break;
            case 1: WriteInteger(importer, key.Chapter.Values[row], type); break;
            case 2: importer.Write(key.ChapterLabel.Text(row) ?? string.Empty, NpgsqlDbType.Text); break;
            case 3: WriteInteger(importer, key.Verse.Values[row], type); break;
            case 4: importer.Write(key.VerseLabel.Text(row) ?? string.Empty, NpgsqlDbType.Text); break;
            default: WriteInteger(importer, key.Position!.Values[row], type); break;
        }
    }

    private static void WriteInteger(NpgsqlBinaryImporter importer, long value, NpgsqlDbType type)
    {
        switch (type)
        {
            case NpgsqlDbType.Integer: importer.Write(checked((int)value), NpgsqlDbType.Integer); break;
            case NpgsqlDbType.Smallint: importer.Write(checked((short)value), NpgsqlDbType.Smallint); break;
            case NpgsqlDbType.Bigint: importer.Write(value, NpgsqlDbType.Bigint); break;
            default: importer.Write(value.ToString(CultureInfo.InvariantCulture), NpgsqlDbType.Text); break;
        }
    }

    private static async Task<long> CopyText(NpgsqlDataSource source, string copy, string path, string tableName, List<int> groups, Bound[] plan, CancellationToken cancellation)
    {
        using var file = EssxReader.Open(path);
        var table = file.Table(tableName);
        await using var connection = await source.OpenConnectionAsync(cancellation);
        await using var importer = await connection.BeginTextImportAsync(copy, cancellation);
        var line = new System.Text.StringBuilder(256);
        long rows = 0;
        foreach (var g in groups)
        {
            var group = table.ReadGroup(g);
            var data = plan.Select(bound => bound.Index < 0 ? null : group.Columns[bound.Index]).ToArray();
            for (var i = 0; i < group.Rows; i++)
            {
                line.Clear();
                for (var c = 0; c < plan.Length; c++)
                {
                    if (c > 0) line.Append('\t');
                    var bound = plan[c];
                    string? value;
                    if (bound.Column.Constant is { } constant) value = constant;
                    else if (data[c] is KeyData key) value = key.IsNull(i) ? null : PartText(key, bound.Column.Part, i);
                    else value = data[c]!.Text(i);
                    line.Append(TsvEscaping.Escape(value));
                }

                line.Append('\n');
                await importer.WriteAsync(line.ToString());
                rows++;
            }
        }

        await importer.DisposeAsync();
        return rows;
    }

    private static string PartText(KeyData key, int part, int row) => part switch
    {
        0 => key.Book.Values[row].ToString(CultureInfo.InvariantCulture),
        1 => key.Chapter.Values[row].ToString(CultureInfo.InvariantCulture),
        2 => key.ChapterLabel.Text(row) ?? string.Empty,
        3 => key.Verse.Values[row].ToString(CultureInfo.InvariantCulture),
        4 => key.VerseLabel.Text(row) ?? string.Empty,
        _ => key.Position!.Values[row].ToString(CultureInfo.InvariantCulture),
    };
}
