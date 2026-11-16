using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Essenthos.Core.Publishing;

/// <summary>
/// The rows of one release a database holding another must lose and gain to hold it, row for row, as one
/// psql script a server runs into a copy nothing reads (DOC-0224 P9).
///
/// Made from the two release dumps alone, so what it is compared with is exactly what a whole restore
/// would give. Every row of every table is read from both archives as COPY text and hashed as the server
/// hashes it (<see cref="CopyRows"/>); the rows whose hash only the base has are deleted by primary key,
/// the rows only the release has are copied in. Where the schemas differ, the tables a migration created
/// or changed go whole and those it removed are dropped (<see cref="SchemaChange"/>); any other change to
/// the schema has no difference, and the release is published whole.
///
/// The script is one transaction. It refuses a database that does not say it is the base release, runs
/// with <c>session_replication_role = replica</c> (foreign keys and the bookkeeping triggers stay quiet:
/// the script writes the final rows of every table itself, <c>table_write</c> and <c>link.fingerprint</c>
/// among them), sets every sequence as the release does, and before it commits sums every table it
/// changed and compares the sum with the release's. With psql's <c>verify_all</c> set it sums every other
/// table too. Any mismatch rolls the whole of it back, so the copy is either the release or untouched.
/// </summary>
internal sealed class ReleaseDifference(ILogger logger)
{
    /// <summary>
    /// A difference larger than this share of the release's dump is not used: restoring the dump costs less.
    /// Measured on two CPUs and 2 GB, a difference of 49 % of its dump applied and vacuumed in 486 s where the
    /// dump restored and vacuumed in 535 s, and was half the upload.
    /// </summary>
    public const double LargestShare = 0.5;

    /// <summary>
    /// A table of at least <see cref="ChurnRows"/> rows that loses <see cref="ChurnShare"/> of them or more is
    /// filled again rather than patched (<see cref="TableReplacement"/>): copying every row into an empty table
    /// and building its indexes afterwards is what a restore does, and on two CPUs it is many times faster
    /// than deleting and inserting millions of rows through the indexes one by one.
    /// </summary>
    public const long ChurnRows = 100_000;

    /// <inheritdoc cref="ChurnRows"/>
    public const double ChurnShare = 0.25;

    /// <summary>The line the script prints after it commits; a script cut short never prints it.</summary>
    public const string AppliedMark = "essenthos-difference-applied";

    private static readonly JsonSerializerOptions RecordJson = new() { WriteIndented = true };

    /// <summary>
    /// The session settings under which a server renders a row as pg_dump rendered it into the archive:
    /// pg_dump's own (ISO dates, Postgres intervals, exact floats) and the containers' UTC.
    /// </summary>
    public static readonly string[] Settings =
    [
        "SET LOCAL DateStyle = 'ISO'",
        "SET LOCAL IntervalStyle = 'postgres'",
        "SET LOCAL extra_float_digits = 3",
        "SET LOCAL TimeZone = 'UTC'",
        "SET LOCAL bytea_output = 'hex'",
    ];

    public static string FileName(string release, string from) => $"{release}.from-{from}.sql.gz";

    public static string RecordName(string release, string from) => $"{release}.from-{from}.json";

    public static DifferenceRecord? Read(string folder, string release, string from)
    {
        var path = Path.Combine(folder, RecordName(release, from));
        return File.Exists(path) ? JsonSerializer.Deserialize<DifferenceRecord>(File.ReadAllText(path)) : null;
    }

    /// <summary>
    /// Makes the difference from <paramref name="from"/> to <paramref name="release"/> out of their dumps
    /// in <paramref name="folder"/>, writes it and its record there, and returns the record; null, said why,
    /// when the two cannot have one.
    /// </summary>
    public async Task<DifferenceRecord?> Make(string folder, string from, string release, int parallel, CancellationToken cancellationToken)
    {
        var clock = Stopwatch.StartNew();
        var before = DumpArchive.Open(Path.Combine(folder, $"{from}.dump"));
        var after = DumpArchive.Open(Path.Combine(folder, $"{release}.dump"));
        var (schema, refusal) = SchemaChange.Of(before.Definitions, after.Definitions);
        if (schema is null)
        {
            logger.LogWarning(
                "Releases {From} and {Release} differ in their schema in a way a difference does not patch ({Refusal}); {Release} is published whole",
                from, release, refusal, release);
            return null;
        }

        var tables = after.Tables.Select(table => table.Name).ToList();
        var missing = tables.Where(table => !schema.Whole.Contains(table) && !before.Tables.Any(entry => entry.Name == table)).ToList();
        if (missing.Count > 0)
        {
            logger.LogWarning("{From} holds no rows of {Tables}, which {Release} holds in tables of the same definition; it is published whole",
                from, string.Join(", ", missing), release);
            return null;
        }

        if (!schema.IsNone)
        {
            logger.LogInformation("The schema changed: {Whole} go whole{Dropped}",
                schema.Whole.Count == 0 ? "no tables" : string.Join(", ", schema.Whole.Order(StringComparer.Ordinal)),
                schema.Dropped.Except(schema.Whole).ToList() is { Count: > 0 } gone ? $", and {string.Join(", ", gone)} are dropped" : "");
        }

        logger.LogInformation("Comparing {Count} tables of {From} and {Release}, {Parallel} at a time", tables.Count, from, release, parallel);
        var record = await Write(
            new ArchiveRows(new Dictionary<string, DumpArchive> { [from] = before, [release] = after }), folder, from, release, tables,
            after.PrimaryKeys(), after.SequenceValues, parallel, cancellationToken, schema,
            new Replacing(ChurnRows, ChurnShare, after.Definitions));
        record = record with { Seconds = Math.Round(clock.Elapsed.TotalSeconds, 1) };
        if (record.Changed.Where(table => table.Replaced).Select(table => table.Table).ToList() is { Count: > 0 } churned)
        {
            logger.LogInformation("So many rows of {Tables} changed that they are filled again rather than patched", string.Join(", ", churned));
        }

        await File.WriteAllTextAsync(Path.Combine(folder, RecordName(release, from)), JsonSerializer.Serialize(record, RecordJson), cancellationToken);
        logger.LogInformation(
            "The difference from {From} to {Release}: {Changed} of {Tables} tables changed, {Gone:N0} rows gone and {Added:N0} added, {Bytes:N0} bytes, made in {Seconds:F0} s",
            from, release, record.Changed.Count, record.Changed.Count + record.Unchanged,
            record.Changed.Sum(table => table.Gone), record.Changed.Sum(table => table.Added), record.Bytes, record.Seconds);
        return record;
    }

    /// <summary>
    /// Compares every table of the two releases and writes the script and its record into <paramref name="folder"/>.
    /// The record's <see cref="DifferenceRecord.Seconds"/> is left for the caller.
    /// </summary>
    public static async Task<DifferenceRecord> Write(
        IReleaseRows source, string folder, string from, string release, IReadOnlyList<string> tables,
        IReadOnlyDictionary<string, IReadOnlyList<string>> primaryKeys, IReadOnlyList<string> sequences, int parallel,
        CancellationToken cancellationToken, SchemaChange? schema = null, Replacing? replacing = null)
    {
        schema ??= SchemaChange.None;
        var whole = schema.Whole;
        var parts = Path.Combine(folder, $".{release}.from-{from}.parts");
        Directory.CreateDirectory(parts);
        try
        {
            var compared = new ConcurrentDictionary<string, TableComparison>(StringComparer.Ordinal);
            await Parallel.ForEachAsync(
                tables.Select((table, index) => (table, index)),
                new ParallelOptions { MaxDegreeOfParallelism = parallel, CancellationToken = cancellationToken },
                async (entry, token) =>
                {
                    var key = primaryKeys.TryGetValue(entry.table, out var columns) ? columns : null;
                    var prefix = Path.Combine(parts, $"{entry.index:D4}");
                    compared[entry.table] = whole.Contains(entry.table)
                        ? await Whole(source, release, entry.table, prefix, token)
                        : await Compare(source, from, release, entry.table, key, prefix, entry.index, replacing, whole, token);
                });

            var file = Path.Combine(folder, FileName(release, from));
            await using (var output = File.Create(file))
            {
                await WriteMember(output, Opening(from), cancellationToken);
                if (!schema.IsNone)
                {
                    await WriteMember(output, "-- The tables the release holds otherwise, or not at all, go; those it holds otherwise come back empty.\n" +
                        schema.Drop + schema.Create, cancellationToken);
                }

                foreach (var table in tables)
                {
                    foreach (var part in compared[table].Parts)
                    {
                        await using var input = File.OpenRead(part);
                        await input.CopyToAsync(output, cancellationToken);
                    }
                }

                if (schema.Finish.Length > 0)
                {
                    await WriteMember(output, "-- The keys, indexes and triggers of the tables sent whole, and the foreign keys to them.\n" + schema.Finish, cancellationToken);
                }

                if (string.Concat(tables.Select(table => compared[table].Foreign)) is { Length: > 0 } foreign)
                {
                    await WriteMember(output, "-- The foreign keys to the tables filled again.\n" + foreign, cancellationToken);
                }

                await WriteMember(output, Closing(release, tables.Select(table => compared[table]).ToList(), sequences), cancellationToken);
            }

            await using var stream = File.OpenRead(file);
            var sha = Convert.ToHexStringLower(await SHA256.HashDataAsync(stream, cancellationToken));
            var changed = tables.Select(table => compared[table]).Where(table => table.Changed)
                .Select(table => new TableChange(table.Table, table.Gone, table.Added, table.Sum.Rows, table.Whole, table.Replaced)).ToList();
            return new DifferenceRecord(
                release, from, FileName(release, from), sha, new FileInfo(file).Length, DateTimeOffset.UtcNow, 0,
                changed, tables.Count - changed.Count, tables.Sum(table => compared[table].Sum.Rows),
                schema.Dropped.Where(table => !schema.Whole.Contains(table)).ToList());
        }
        finally
        {
            Directory.Delete(parts, recursive: true);
        }
    }

    /// <summary>
    /// A table sent whole: every row of the release, copied into the table the script has just created, or,
    /// with <paramref name="replacement"/>, into the table it has just emptied, its keys and indexes built after.
    /// </summary>
    private static async Task<TableComparison> Whole(
        IReleaseRows source, string release, string table, string partPrefix, CancellationToken cancellationToken, TableReplacement? replacement = null)
    {
        var part = partPrefix + ".whole";
        var sum = default(TableSum);
        string? qualified = null, columns = null;
        var rows = new CopyRows();
        await using (var file = File.Create(part))
        await using (var zip = new GZipStream(file, CompressionLevel.Optimal))
        {
            var opened = false;
            await source.Rows(release, table, (name, list) =>
            {
                (qualified, columns) = (name, list);
                if (replacement is not null)
                {
                    zip.Write(Encoding.UTF8.GetBytes($"-- {qualified}: filled again\n{replacement.Drop}"));
                }
            }, line =>
            {
                if (!opened)
                {
                    zip.Write(Encoding.UTF8.GetBytes($"{(replacement is null ? $"-- {qualified}: sent whole\n" : "")}COPY {qualified} ({columns}) FROM stdin;\n"));
                    opened = true;
                }

                sum = sum.Add(rows.Hash(line));
                zip.Write(line);
                zip.WriteByte((byte)'\n');
            }, cancellationToken);
            if (opened)
            {
                zip.Write("\\.\n"u8);
            }

            if (replacement is not null)
            {
                zip.Write(Encoding.UTF8.GetBytes(replacement.Build));
            }
        }

        if (qualified is null || columns is null)
        {
            throw new InvalidDataException($"No rows header for table {table} in {release}.");
        }

        return replacement is null
            ? new TableComparison(table, qualified, columns, sum, 0, sum.Rows, sum.Rows > 0 ? [part] : [], Whole: true)
            : new TableComparison(table, qualified, columns, sum, 0, sum.Rows, [part], Replaced: true, Foreign: replacement.Foreign);
    }

    /// <summary>One table of both releases, and the parts of the script that change it, if any.</summary>
    private static async Task<TableComparison> Compare(
        IReleaseRows source, string from, string release, string table, IReadOnlyList<string>? key, string partPrefix, int index,
        Replacing? replacing, IReadOnlySet<string> whole, CancellationToken cancellationToken)
    {
        var before = new List<UInt128>();
        var after = new List<UInt128>();
        string? beforeColumns = null, afterColumns = null, qualified = null;
        var sum = default(TableSum);
        var beforeRows = new CopyRows();
        var afterRows = new CopyRows();
        await Task.WhenAll(
            source.Rows(from, table, (_, columns) => beforeColumns = columns, line => before.Add(beforeRows.Hash(line)), cancellationToken),
            source.Rows(release, table, (name, columns) => (qualified, afterColumns) = (name, columns), line =>
            {
                var hash = afterRows.Hash(line);
                after.Add(hash);
                sum = sum.Add(hash);
            }, cancellationToken));

        if (qualified is null || afterColumns is null || beforeColumns is null)
        {
            throw new InvalidDataException($"No rows header for table {table} in {(afterColumns is null ? release : from)}.");
        }

        if (beforeColumns != afterColumns)
        {
            throw new InvalidDataException($"{qualified} has the columns ({beforeColumns}) in {from} and ({afterColumns}) in {release}.");
        }

        var (gone, added) = Unmatched(before, after);
        var comparison = new TableComparison(table, qualified, afterColumns, sum, gone.Values.Sum(), added.Values.Sum(), []);
        if (replacing is not null && before.Count >= replacing.Rows && comparison.Gone >= replacing.Share * before.Count)
        {
            return await Whole(source, release, table, partPrefix, cancellationToken, SchemaChange.Replace(replacing.Definitions, table, whole))
                with { Gone = comparison.Gone, Added = comparison.Added };
        }

        if (comparison.Gone == 0 && comparison.Added == 0)
        {
            return comparison;
        }

        if (comparison.Gone > 0 && key is null)
        {
            throw new InvalidDataException($"{qualified} has no primary key, so the {comparison.Gone} rows it loses cannot be named.");
        }

        var columns = DumpArchive.Columns(afterColumns);
        var parts = new List<string>();
        var writes = new List<Task>();
        if (comparison.Gone > 0)
        {
            var positions = key!.Select(column => columns.ToList().IndexOf(column)).ToArray();
            if (positions.Any(position => position < 0))
            {
                throw new InvalidDataException($"The primary key ({string.Join(", ", key!)}) of {qualified} is not among its columns ({afterColumns}).");
            }

            var part = partPrefix + ".gone";
            parts.Add(part);
            writes.Add(WriteGone(source, from, comparison, key!, positions, gone, part, index, cancellationToken));
        }

        if (comparison.Added > 0)
        {
            var part = partPrefix + ".added";
            parts.Add(part);
            writes.Add(WriteAdded(source, release, comparison, added, part, cancellationToken));
        }

        await Task.WhenAll(writes);
        return comparison with { Parts = parts };
    }

    /// <summary>The rows only <paramref name="before"/> has and those only <paramref name="after"/> has, by hash, with how many of each.</summary>
    private static (Dictionary<UInt128, int> Gone, Dictionary<UInt128, int> Added) Unmatched(List<UInt128> before, List<UInt128> after)
    {
        var left = CollectionsMarshal.AsSpan(before);
        var right = CollectionsMarshal.AsSpan(after);
        left.Sort();
        right.Sort();
        var gone = new Dictionary<UInt128, int>();
        var added = new Dictionary<UInt128, int>();
        int i = 0, j = 0;
        while (i < left.Length || j < right.Length)
        {
            if (j == right.Length || (i < left.Length && left[i] < right[j]))
            {
                gone[left[i]] = gone.GetValueOrDefault(left[i]) + 1;
                i++;
            }
            else if (i == left.Length || right[j] < left[i])
            {
                added[right[j]] = added.GetValueOrDefault(right[j]) + 1;
                j++;
            }
            else
            {
                i++;
                j++;
            }
        }

        return (gone, added);
    }

    private static async Task WriteGone(
        IReleaseRows source, string from, TableComparison table, IReadOnlyList<string> key, int[] positions,
        Dictionary<UInt128, int> gone, string part, int index, CancellationToken cancellationToken)
    {
        var temporary = $"essenthos_gone_{index}";
        var keyList = string.Join(", ", key);
        await using var file = File.Create(part);
        await using var zip = new GZipStream(file, CompressionLevel.Optimal);
        var opening = Encoding.UTF8.GetBytes(
            $"-- {table.Qualified}: {table.Gone} rows gone\n" +
            $"CREATE TEMP TABLE {temporary} ON COMMIT DROP AS SELECT {keyList} FROM {table.Qualified} WITH NO DATA;\n" +
            $"COPY {temporary} ({keyList}) FROM stdin;\n");
        zip.Write(opening);
        var rows = new CopyRows();
        var remaining = new Dictionary<UInt128, int>(gone);
        var fields = new Range[positions.Max() + 1];
        await source.Rows(from, table.Table, (_, _) => { }, line =>
        {
            var hash = rows.Hash(line);
            if (!remaining.TryGetValue(hash, out var count) || count == 0)
            {
                return;
            }

            remaining[hash] = count - 1;
            Fields(line, fields);
            for (var k = 0; k < positions.Length; k++)
            {
                if (k > 0)
                {
                    zip.WriteByte((byte)'\t');
                }

                zip.Write(line[fields[positions[k]]]);
            }

            zip.WriteByte((byte)'\n');
        }, cancellationToken);

        var join = string.Join(" AND ", key.Select(column => $"t.{column} = g.{column}"));
        var closing = Encoding.UTF8.GetBytes(
            "\\.\n" +
            $"ANALYZE {temporary};\n" +
            $"DO $$DECLARE essenthos_deleted bigint; BEGIN DELETE FROM {table.Qualified} AS t USING {temporary} AS g WHERE {join}; " +
            $"GET DIAGNOSTICS essenthos_deleted = ROW_COUNT; IF essenthos_deleted <> {table.Gone} THEN " +
            $"RAISE EXCEPTION '{Literal(table.Qualified)}: % rows deleted, where the difference names {table.Gone}', essenthos_deleted; END IF; END $$;\n");
        zip.Write(closing);
    }

    private static async Task WriteAdded(
        IReleaseRows source, string release, TableComparison table, Dictionary<UInt128, int> added, string part, CancellationToken cancellationToken)
    {
        await using var file = File.Create(part);
        await using var zip = new GZipStream(file, CompressionLevel.Optimal);
        zip.Write(Encoding.UTF8.GetBytes($"-- {table.Qualified}: {table.Added} rows added\nCOPY {table.Qualified} ({table.Columns}) FROM stdin;\n"));
        var rows = new CopyRows();
        var remaining = new Dictionary<UInt128, int>(added);
        await source.Rows(release, table.Table, (_, _) => { }, line =>
        {
            var hash = rows.Hash(line);
            if (remaining.TryGetValue(hash, out var count) && count > 0)
            {
                remaining[hash] = count - 1;
                zip.Write(line);
                zip.WriteByte((byte)'\n');
            }
        }, cancellationToken);

        zip.Write("\\.\n"u8);
    }

    /// <summary>The first <c>fields.Length</c> fields of a COPY line.</summary>
    private static void Fields(ReadOnlySpan<byte> line, Span<Range> fields)
    {
        var start = 0;
        for (var k = 0; k < fields.Length; k++)
        {
            var tab = line[start..].IndexOf((byte)'	');
            var end = tab < 0 ? line.Length : start + tab;
            fields[k] = new Range(start, end);
            if (tab < 0 && k + 1 < fields.Length)
            {
                throw new InvalidDataException($"A row has {k + 1} fields where its key needs {fields.Length}.");
            }

            start = end + 1;
        }
    }

    private static string Opening(string from) =>
        $"-- The rows a database holding release {from} loses and gains to hold the release this file is named after.\n" +
        "-- One transaction: psql -X -v ON_ERROR_STOP=1 -f <this, unzipped>, with -v verify_all=1 to sum every table, not only the changed ones.\n" +
        "\\set ON_ERROR_STOP on\n" +
        "\\pset tuples_only on\n" +
        "SET temp_buffers = '64MB';\n" +
        "BEGIN;\n" +
        "SET LOCAL session_replication_role = replica;\n" +
        "SET LOCAL statement_timeout = 0;\n" +
        "SET LOCAL lock_timeout = 0;\n" +
        string.Concat(Settings.Select(setting => setting + ";\n")) +
        $"CREATE FUNCTION pg_temp.{Unequal}(message text) RETURNS boolean LANGUAGE plpgsql PARALLEL RESTRICTED AS $$BEGIN RAISE EXCEPTION '%', message; END $$;\n" +
        "DO $$BEGIN IF (SELECT string_agg(name, ',' ORDER BY name) FROM public.corpus_release) IS DISTINCT FROM " +
        $"'{Literal(from)}' THEN RAISE EXCEPTION 'This database holds release %, and the difference starts from release {Literal(from)}', " +
        "coalesce((SELECT string_agg(name, ',' ORDER BY name) FROM public.corpus_release), 'none'); END IF; END $$;\n";

    private static string Closing(string release, IReadOnlyList<TableComparison> tables, IReadOnlyList<string> sequences)
    {
        var text = new StringBuilder();
        foreach (var sequence in sequences)
        {
            text.Append(sequence).Append('\n');
        }

        text.Append("-- Every table the difference changed holds what the release holds.\n");
        foreach (var table in tables.Where(table => table.Changed))
        {
            text.Append(Check(release, table)).Append('\n');
        }

        text.Append("\\if :{?verify_all}\n");
        foreach (var table in tables.Where(table => !table.Changed))
        {
            text.Append(Check(release, table)).Append('\n');
        }

        text.Append("\\endif\n");
        text.Append("COMMIT;\n");
        text.Append($"\\echo {AppliedMark} {release}\n");
        return text.ToString();
    }

    /// <summary>
    /// One table summed and held to the release's sum, on one line. A plain query rather than a DO block,
    /// which PL/pgSQL would run without parallel workers: on two CPUs a parallel scan sums twice as fast.
    /// </summary>
    public static string Check(string release, TableComparison table) =>
        $"SELECT pg_temp.{Unequal}(format('{Literal(Format(table.Qualified))} holds %s rows summing to %s %s, and release {Literal(Format(release))} " +
        $"holds {table.Sum.Rows} summing to {table.Sum.High} {table.Sum.Low}', sum.r, sum.h, sum.l)) " +
        $"FROM ({TableSum.Sql(table.Qualified, table.Columns)}) AS sum(r, h, l) " +
        $"WHERE sum.r <> {table.Sum.Rows} OR sum.h <> {table.Sum.High} OR sum.l <> {table.Sum.Low};";

    /// <summary>
    /// The function a check calls with what it found when a table is not as the release holds it. It raises,
    /// and is parallel restricted so that the scan under it may still use workers.
    /// </summary>
    private const string Unequal = "essenthos_unequal";

    private static string Format(string text) => text.Replace("%", "%%");

    private static string Literal(string text) => text.Replace("'", "''");

    private static async Task WriteMember(Stream output, string text, CancellationToken cancellationToken)
    {
        await using var zip = new GZipStream(output, CompressionLevel.Optimal, leaveOpen: true);
        await zip.WriteAsync(Encoding.UTF8.GetBytes(text), cancellationToken);
    }

    /// <summary>The rows of a release's tables, read from its dump.</summary>
    private sealed class ArchiveRows(IReadOnlyDictionary<string, DumpArchive> archives) : IReleaseRows
    {
        public Task Rows(string release, string table, Action<string, string> header, RowAction row, CancellationToken cancellationToken) =>
            Task.Run(() => archives[release].Rows(table, header, row), cancellationToken);
    }
}

/// <summary>Where a difference reads the rows of a release's tables from.</summary>
internal interface IReleaseRows
{
    Task Rows(string release, string table, Action<string, string> header, RowAction row, CancellationToken cancellationToken);
}

/// <param name="Table">As the archive names it.</param>
/// <param name="Qualified">As the COPY header names it: <c>public.word</c>.</param>
/// <param name="Columns">The COPY header's column list.</param>
/// <param name="Sum">The release's rows of the table, summed.</param>
/// <param name="Parts">The gzip members of the script that change the table, in order.</param>
/// <param name="Whole">Created by the script and every row copied in, its schema having changed or being new.</param>
/// <param name="Replaced">Same definition in both, but so many of its rows changed that it is filled again (<see cref="TableReplacement"/>).</param>
/// <param name="Foreign">For a table filled again, the foreign keys to it, created after every table's rows.</param>
internal sealed record TableComparison(
    string Table, string Qualified, string Columns, TableSum Sum, long Gone, long Added, IReadOnlyList<string> Parts, bool Whole = false,
    bool Replaced = false, string Foreign = "")
{
    public bool Changed => Whole || Replaced || Gone > 0 || Added > 0;
}

/// <summary>Which tables a difference fills again rather than patches, and the release's definitions to do it with.</summary>
/// <param name="Rows">The fewest rows a table has in the base to be filled again.</param>
/// <param name="Share">The share of those rows it must lose.</param>
internal sealed record Replacing(long Rows, double Share, IReadOnlyList<DumpArchive.Entry> Definitions);

/// <summary>A table the difference changes: how many rows go, how many come, and how many it holds afterwards.</summary>
/// <param name="Whole">Created again from the release's schema with every row, rather than patched.</param>
/// <param name="Replaced">Emptied and filled with every row of the release, its keys and indexes built again.</param>
internal sealed record TableChange(string Table, long Gone, long Added, long Rows, bool Whole = false, bool Replaced = false);

/// <summary>What <c>forge difference</c> wrote beside the script.</summary>
/// <param name="Release">The release a database holds once the script has run.</param>
/// <param name="Base">The release a database must hold for the script to run.</param>
internal sealed record DifferenceRecord(
    string Release,
    string Base,
    string File,
    string Sha256,
    long Bytes,
    DateTimeOffset MadeAt,
    double Seconds,
    IReadOnlyList<TableChange> Changed,
    int Unchanged,
    long Rows,
    IReadOnlyList<string>? Dropped = null);
