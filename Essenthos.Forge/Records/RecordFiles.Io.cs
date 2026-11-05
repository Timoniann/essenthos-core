using System.Globalization;
using Essenthos.Core.Products;
using Npgsql;
using NpgsqlTypes;

namespace Essenthos.Core.Records;

/// <summary>
/// Our records, facts, languages and external datasets taken from a database into their files, and put
/// from the files into a database (TSK-1447, DOC-0226). The files are the truth; a database is where
/// the site reads them.
/// </summary>
internal static partial class RecordFiles
{
    public const string Producer = "forge records-export";

    /// <summary>The databases the load refuses to write: live, and the shared snapshot everyone exports from.</summary>
    private static readonly string[] Untouchable = ["essenthos_core", "essenthos_core_rework_src"];

    /// <summary>
    /// What a database of today's schema shows of a record a dataset supplied and we have read for ourselves
    /// (<see cref="OursOnlyRecords"/>): written into the record's own columns, so the file says what the
    /// reader sees and the dataset's values go to its external records.
    /// </summary>
    public sealed record OwnValues(string? Sex, string? Tribe, string? Line);

    /// <summary>Whether the database was filled by this loader rather than by the old load.</summary>
    public static async Task<bool> Loaded(string connection, CancellationToken cancellationToken)
    {
        await using var db = new NpgsqlConnection(connection);
        await db.OpenAsync(cancellationToken);
        return await Loaded(db, cancellationToken);
    }

    /// <summary>Writes every file from the database, and says how many rows each got.</summary>
    public static async Task<List<(string Product, long Rows)>> Export(
        string connection, ProductStore store, IReadOnlyDictionary<string, OwnValues> oursOnly, CancellationToken cancellationToken)
    {
        await using var db = new NpgsqlConnection(connection);
        await db.OpenAsync(cancellationToken);
        var loaded = await Loaded(db, cancellationToken);
        await using (var create = new NpgsqlCommand("CREATE TEMP TABLE ours_only (slug text PRIMARY KEY, sex text, tribe text, line text)", db))
        {
            await create.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var import = await db.BeginBinaryImportAsync("COPY ours_only (slug, sex, tribe, line) FROM STDIN (FORMAT BINARY)", cancellationToken))
        {
            foreach (var (slug, own) in oursOnly)
            {
                await import.WriteRowAsync(cancellationToken, slug, (object?)own.Sex ?? DBNull.Value, (object?)own.Tribe ?? DBNull.Value, (object?)own.Line ?? DBNull.Value);
            }

            await import.CompleteAsync(cancellationToken);
        }

        var database = db.Database;
        var written = new List<(string, long)>();
        foreach (var file in All)
        {
            var schema = file.Schema();
            List<string?[]> rows = file.Listed is { } listed
                ? [.. listed()]
                : await Rows(db, file, loaded ? file.ExportLoaded ?? file.Export : file.Export, cancellationToken);
            using var writer = store.Create(file.Product, file.Kind, schema, ProductHeader.DescribeProducer(Producer), [],
                header =>
                {
                    header.Notes.Add(new("exported", $"from {database}"));
                    if (file.About is { } about)
                    {
                        header.Comments.Add(about);
                    }
                });
            writer.WriteAll(rows);
            writer.Complete();
            written.Add((file.Product, writer.Rows));
        }

        store.Save();
        return written;
    }

    /// <summary>The file's rows as the export gives them, in the file's order (<see cref="RecordFile.Sorted"/>).</summary>
    private static async Task<List<string?[]>> Rows(NpgsqlConnection db, RecordFile file, string export, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(export, db) { CommandTimeout = 0 };
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (reader.FieldCount != file.Parsed.Length)
        {
            throw new InvalidOperationException(
                $"{file.Product}: the export gives {reader.FieldCount} columns, the file has {file.Parsed.Length}.");
        }

        var rows = new List<string?[]>();
        while (await reader.ReadAsync(cancellationToken))
        {
            var row = new string?[file.Parsed.Length];
            for (var i = 0; i < row.Length; i++)
            {
                row[i] = reader.IsDBNull(i)
                    ? null
                    : file.Parsed[i].Type is RecordColumnType.Ref or RecordColumnType.Refs
                        ? Text(file.Parsed[i], Whole(reader.GetFieldValue<int?[]>(i), file.Product, file.Parsed[i].Name))
                        : Text(file.Parsed[i], reader.GetValue(i));
            }

            rows.Add(row);
        }

        return file.Sorted(rows, file.Order(export));
    }

    /// <summary>
    /// Puts every file into the database in one transaction, emptying the tables they fill first; a file
    /// that does not load leaves the database as it was (RUL-0192).
    /// </summary>
    public static async Task<List<(string Product, long Rows)>> Load(
        string connection, ProductStore store, CancellationToken cancellationToken)
    {
        await using var db = new NpgsqlConnection(connection);
        await db.OpenAsync(cancellationToken);
        if (Untouchable.Contains(db.Database, StringComparer.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"records-load does not write {db.Database}; load into a database of your own.");
        }

        await using var transaction = await db.BeginTransactionAsync(cancellationToken);
        var loaded = await Load(db, transaction, store, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return loaded;
    }

    /// <summary>
    /// The same inside the caller's transaction, which it commits. Every table of the record files is emptied with
    /// <c>CASCADE</c>, so a table outside them that refers to a record (the names on words, the spellings) is emptied too
    /// and is the caller's to fill again before it commits.
    /// </summary>
    public static async Task<List<(string Product, long Rows)>> Load(
        NpgsqlConnection db, NpgsqlTransaction transaction, ProductStore store, CancellationToken cancellationToken)
    {
        if (Untouchable.Contains(db.Database, StringComparer.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"records-load does not write {db.Database}; load into a database of your own.");
        }

        await Execute(db, transaction, SchemaSql, cancellationToken);
        var tables = All.SelectMany(f => f.Tables).Distinct().ToList();
        await Execute(db, transaction, $"TRUNCATE {string.Join(", ", tables)} RESTART IDENTITY CASCADE", cancellationToken);

        var loaded = new List<(string, long)>();
        foreach (var file in All)
        {
            if (file.Requires is { } requirement && !await Holds(db, transaction, requirement, cancellationToken))
            {
                loaded.Add((file.Product, -1));
                continue;
            }

            using var reader = store.Open(file.Product, file.Schema());
            var rows = reader.ReadAll();
            var columns = string.Join(", ", file.Parsed.Select(c => $"{c.Name} {c.SqlType}"));
            await Execute(db, transaction, $"DROP TABLE IF EXISTS s; CREATE TEMP TABLE s ({columns}) ON COMMIT DROP", cancellationToken);
            await using (var import = await db.BeginBinaryImportAsync(
                             $"COPY s ({string.Join(", ", file.Parsed.Select(c => c.Name))}) FROM STDIN (FORMAT BINARY)",
                             cancellationToken))
            {
                foreach (var row in rows)
                {
                    await import.StartRowAsync(cancellationToken);
                    for (var i = 0; i < row.Length; i++)
                    {
                        await Stage(import, file.Parsed[i], row[i], file.Product, cancellationToken);
                    }
                }

                await import.CompleteAsync(cancellationToken);
            }

            if (file.Check is { } check)
            {
                var sound = await Scalar(db, transaction, check, cancellationToken);
                if (sound != rows.Count)
                {
                    throw new InvalidOperationException(
                        $"{file.Product}: {rows.Count} rows in the file, {sound} of them sound. A slug names no record, " +
                        "or names one it must not; find it with the file's check as a SELECT.");
                }

                if (file.After is { } kept)
                {
                    await Execute(db, transaction, kept, cancellationToken);
                }

                loaded.Add((file.Product, rows.Count));
                continue;
            }

            var written = await Execute(db, transaction, file.Load, cancellationToken);
            if (written != rows.Count)
            {
                throw new InvalidOperationException(
                    $"{file.Product}: {rows.Count} rows in the file, {written} written. A slug or key names nothing; " +
                    "find it with the file's load statement as a SELECT.");
            }

            if (file.After is { } after)
            {
                await Execute(db, transaction, after, cancellationToken);
            }

            loaded.Add((file.Product, rows.Count));
        }

        loaded.Add((RecordCanons.Product, await RecordCanons.Load(db, transaction, store, cancellationToken)));
        return loaded;
    }

    /// <summary>
    /// A dataset's verse references by whether a word of ours confirms them: a served verse of the same
    /// record at the same verse that a word names. BibleData's are not served, on the owner's ruling, so
    /// its unconfirmed rows are the ones a reader no longer sees; the gazetteer's are served as they stand.
    /// </summary>
    public static async Task<List<(string Dataset, bool Confirmed, long Rows, long Records)>> DatasetVerses(
        string connection, CancellationToken cancellationToken)
    {
        await using var db = new NpgsqlConnection(connection);
        await db.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            """
            SELECT dataset, confirmed, count(*), count(DISTINCT entity_id) FROM (
                SELECT x.dataset, x.entity_id, EXISTS (
                    SELECT 1 FROM entity_verse v WHERE v.entity_id = x.entity_id AND v.names AND v.canonical_book = x.canonical_book
                      AND v.canonical_chapter = x.canonical_chapter AND v.canonical_verse = x.canonical_verse) confirmed
                FROM external_reference x) r
            GROUP BY 1, 2 ORDER BY 1, 2
            """, db) { CommandTimeout = 0 };
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var counts = new List<(string, bool, long, long)>();
        while (await reader.ReadAsync(cancellationToken))
        {
            counts.Add((reader.GetString(0), reader.GetBoolean(1), reader.GetInt64(2), reader.GetInt64(3)));
        }

        return counts;
    }

    /// <summary>Whether the database was filled by this loader: the dataset columns of a record are gone.</summary>
    private static async Task<bool> Loaded(NpgsqlConnection db, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            "SELECT NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema = 'public' AND table_name = 'entity' AND column_name = 'source')",
            db);
        return (bool)(await command.ExecuteScalarAsync(cancellationToken))!;
    }

    private static async Task<long> Scalar(NpgsqlConnection db, NpgsqlTransaction transaction, string sql, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(sql, db, transaction) { CommandTimeout = 0 };
        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture);
    }

    private static async Task<bool> Holds(NpgsqlConnection db, NpgsqlTransaction transaction, string condition, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand($"SELECT {condition}", db, transaction);
        return (bool)(await command.ExecuteScalarAsync(cancellationToken))!;
    }

    private static async Task<int> Execute(NpgsqlConnection db, NpgsqlTransaction transaction, string sql, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(sql, db, transaction) { CommandTimeout = 0 };
        return await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static string Text(RecordColumn column, object value) => column.Type switch
    {
        RecordColumnType.Boolean => (bool)value ? "t" : "f",
        RecordColumnType.Integer => Convert.ToInt64(value, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture),
        RecordColumnType.Ref => Key((int[])value, 0).ToString(),
        RecordColumnType.Refs => string.Join(' ', Enumerable.Range(0, ((int[])value).Length / 3).Select(i => Key((int[])value, i * 3))),
        _ => Convert.ToString(value, CultureInfo.InvariantCulture)!,
    };

    private static int[] Whole(int?[] values, string product, string column) =>
        values.All(v => v.HasValue)
            ? [.. values.Select(v => v!.Value)]
            : throw new InvalidOperationException($"{product}: a verse in column '{column}' is missing its book, chapter or verse.");

    private static VerseKey Key(int[] triple, int at) => new(triple[at], triple[at + 1], triple[at + 2]);

    private static int[] Triple(string text, string product)
    {
        var key = VerseKey.Parse(text);
        if (key.ChapterLabel.Length > 0 || key.VerseLabel.Length > 0)
        {
            throw new FormatException($"{product}: '{text}' is a canonical verse and takes no label.");
        }

        return [key.Book, key.Chapter, key.Verse];
    }

    private static async Task Stage(NpgsqlBinaryImporter import, RecordColumn column, string? text, string product, CancellationToken cancellationToken)
    {
        if (text is null)
        {
            await import.WriteNullAsync(cancellationToken);
            return;
        }

        switch (column.Type)
        {
            case RecordColumnType.Integer:
                await import.WriteAsync(long.Parse(text, CultureInfo.InvariantCulture), NpgsqlDbType.Bigint, cancellationToken);
                break;
            case RecordColumnType.Boolean:
                await import.WriteAsync(text == "t", NpgsqlDbType.Boolean, cancellationToken);
                break;
            case RecordColumnType.Json:
                await import.WriteAsync(text, NpgsqlDbType.Jsonb, cancellationToken);
                break;
            case RecordColumnType.Ref:
                await import.WriteAsync(Triple(text, product), NpgsqlDbType.Array | NpgsqlDbType.Integer, cancellationToken);
                break;
            case RecordColumnType.Refs:
                await import.WriteAsync(
                    text.Split(' ', StringSplitOptions.RemoveEmptyEntries).SelectMany(key => Triple(key, product)).ToArray(),
                    NpgsqlDbType.Array | NpgsqlDbType.Integer, cancellationToken);
                break;
            default:
                await import.WriteAsync(text, NpgsqlDbType.Text, cancellationToken);
                break;
        }
    }
}
