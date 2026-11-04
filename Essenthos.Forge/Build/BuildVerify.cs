using System.Globalization;
using System.Text;
using Essenthos.Core.Products;
using Essenthos.Core.Verification;
using Npgsql;

namespace Essenthos.Core.Build;

/// <summary>A served table and the rows a build left in it.</summary>
internal sealed record ServedTable(string Table, long Rows)
{
    public static readonly ProductSchema<ServedTable> Schema = new ProductSchema<ServedTable>()
        .Text("table", t => t.Table)
        .Integer("rows", t => t.Rows)
        .Reads(row => new ServedTable(row.Text(0), row.Integer(1)));
}

/// <summary>
/// <c>build/served/&lt;database&gt;</c>: what a database was built or last applied from. Its inputs are the products loaded,
/// each with the hash it had, so <c>build --apply</c> knows which moved since; its rows are every table's row count, which
/// <c>verify --level essentials</c> holds the database to. A file, not a table: Postgres keeps no marks.
/// </summary>
internal static class ServedState
{
    public const string KeysNote = "foreign-keys";

    public static string Product(string database) => $"build/served/{database}";

    public static void Write(ProductStore store, ProductGraph graph, string database, IReadOnlyDictionary<string, long> rows, int keys, string producer)
    {
        var loaded = store.Manifest.Entries
            .Where(e => graph.Find(e.Product) is { Declaration.Tables.Count: > 0 })
            .Select(e => e.Product).Order(StringComparer.Ordinal).ToList();
        using var writer = store.Create(Product(database), ProductKind.Hand, ServedTable.Schema, ProductHeader.DescribeProducer(producer), store.Inputs(loaded),
            header => header.Notes.Add(new(KeysNote, keys.ToString(CultureInfo.InvariantCulture))));
        foreach (var (table, count) in rows.OrderBy(r => r.Key, StringComparer.Ordinal))
        {
            writer.Write(new ServedTable(table, count));
        }

        writer.Complete();
        store.Save();
    }

    public static (ProductHeader Header, Dictionary<string, long> Rows)? Read(ProductStore store, string database)
    {
        if (!store.Exists(Product(database)))
        {
            return null;
        }

        using var reader = store.Open(Product(database), ServedTable.Schema);
        return (reader.Header!, reader.Rows().ToDictionary(r => r.Table, r => r.Rows, StringComparer.Ordinal));
    }
}

/// <summary>
/// <c>forge verify --level skip|essentials|all [--only m,…] [--texts A,…]</c> (DOC-0224 §14, NOT-0216 D11). <c>essentials</c>
/// holds every table to the rows the build left in it and the words and verses to their products' counts, finds every
/// foreign key in place and validated, measures coverage against the floor, and pairing on the pairs a build touched;
/// <c>all</c> is the whole verification, recorded for <c>/v1/health</c> as before; <c>--only</c> measures the ones named
/// (<c>integrity</c> for every integrity check); <c>--texts</c> keeps the measures of those texts.
/// </summary>
internal static class BuildVerify
{
    public const string Skip = "skip";
    public const string Essentials = "essentials";
    public const string All = "all";

    public static async Task<bool> Run(
        IServiceProvider services,
        string connectionString,
        ProductStore? store,
        string level,
        IReadOnlySet<string>? only,
        IReadOnlySet<string>? texts,
        IReadOnlyCollection<string>? touchedPairs,
        double floor,
        ILogger logger)
    {
        if (level == Skip && only is null)
        {
            logger.LogWarning("verify skipped: nothing measured");
            return true;
        }

        using var scope = services.CreateScope();
        var check = scope.ServiceProvider.GetRequiredService<CorpusCheck>();
        if (only is not null)
        {
            var measured = Keep(await check.Measure(only.Contains, CancellationToken.None), texts);
            logger.LogInformation("\n{Report}", measured.Describe());
            return only.Contains("coverage") ? CorpusGate.Pass(measured, floor, logger) : measured.Broken == 0;
        }

        if (level == All)
        {
            var measures = await check.Measure();
            await check.Record(measures);
            measures = Keep(measures, texts);
            logger.LogInformation("\n{Report}", measures.Describe());
            return CorpusGate.Pass(measures, floor, logger);
        }

        if (level != Essentials)
        {
            throw new ArgumentException($"verify --level takes {Skip}, {Essentials} or {All}, not '{level}'.");
        }

        if (store is null)
        {
            throw new ArgumentException("verify --level essentials holds the database to its products, so it needs the Resources folder.");
        }

        var database = new NpgsqlConnectionStringBuilder(connectionString).Database!;
        var report = new StringBuilder();
        var pass = await Counts(connectionString, store, database, report);
        pass &= await Keys(connectionString, store, database, report);
        var coverage = Keep(await check.Measure(name => name is "coverage" or "unaligned" or "pairing", CancellationToken.None), texts);
        if (touchedPairs is not null)
        {
            coverage = coverage with { Pairing = [.. coverage.Pairing.Where(p => touchedPairs.Contains($"{p.Text}-{p.Against}") || touchedPairs.Contains($"{p.Against}-{p.Text}"))] };
        }

        foreach (var pairing in coverage.Pairing)
        {
            report.AppendLine(CultureInfo.InvariantCulture,
                $"pairing {pairing.Text}-{pairing.Against}: {pairing.Chapters} chapters, {pairing.Divided} divided, {pairing.Verses} verses, {pairing.Suspect} suspect");
        }

        logger.LogInformation("verify essentials:\n{Report}", report.ToString().TrimEnd());
        pass &= CorpusGate.Pass(coverage, floor, logger);
        return pass;
    }

    private static CorpusMeasures Keep(CorpusMeasures measures, IReadOnlySet<string>? texts)
    {
        if (texts is null)
        {
            return measures;
        }

        bool Any(params string[] names) => names.Any(texts.Contains);
        return measures with
        {
            Coverage = [.. measures.Coverage.Where(c => Any(c.Text))],
            Reach = [.. measures.Reach.Where(r => Any(r.Witness, r.From))],
            Contention = [.. measures.Contention.Where(c => Any(c.Text, c.Against))],
            Crowding = [.. measures.Crowding.Where(c => Any(c.Text, c.Witness))],
            Absence = [.. measures.Absence.Where(a => Any(a.Text, a.Against))],
            Pairing = [.. measures.Pairing.Where(p => Any(p.Text, p.Against))],
            Shared = [.. measures.Shared.Where(s => Any(s.Text))],
            Unaligned = [.. measures.Unaligned.Where(u => Any(u.Text))],
            Stranded = measures.Stranded?.Where(s => Any(s.Text, s.Witness)).ToList(),
            Variation = measures.Variation?.Where(v => Any(v.Text, v.Against)).ToList(),
        };
    }

    /// <summary>Every table against the rows the build left, and the words and verses against their products' rows.</summary>
    private static async Task<bool> Counts(string connectionString, ProductStore store, string database, StringBuilder report)
    {
        var expected = new Dictionary<string, long>(StringComparer.Ordinal);
        if (ServedState.Read(store, database) is { } served)
        {
            foreach (var (table, rows) in served.Rows)
            {
                expected[table] = rows;
            }
        }
        else
        {
            report.AppendLine($"no build/served/{database}: only the words and verses are held to their products");
        }

        long Products(string suffix) => store.Manifest.Entries
            .Where(e => e.Product.StartsWith("Essenthos/texts/", StringComparison.Ordinal) && Path.GetFileNameWithoutExtension(e.Product) == suffix
                        && e.Product.Count(c => c == '/') == 3)
            .Sum(e => e.Rows);

        var fromProducts = new Dictionary<string, long> { ["word"] = Products("words"), ["verse"] = Products("verses") };
        var pass = true;
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        foreach (var table in expected.Keys.Union(fromProducts.Keys).Order(StringComparer.Ordinal))
        {
            await using var command = new NpgsqlCommand($"SELECT count(*) FROM \"{table}\"", connection);
            long held;
            try
            {
                held = Convert.ToInt64(await command.ExecuteScalarAsync(), CultureInfo.InvariantCulture);
            }
            catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.UndefinedTable)
            {
                report.AppendLine(CultureInfo.InvariantCulture, $"MISSING table {table}");
                pass = false;
                continue;
            }

            if (expected.TryGetValue(table, out var built) && built != held)
            {
                report.AppendLine(CultureInfo.InvariantCulture, $"DIFFERS {table}: {held} rows, the build left {built}");
                pass = false;
            }

            if (fromProducts.TryGetValue(table, out var made) && made != held)
            {
                report.AppendLine(CultureInfo.InvariantCulture, $"DIFFERS {table}: {held} rows, its products hold {made}");
                pass = false;
            }
        }

        report.AppendLine(CultureInfo.InvariantCulture,
            $"row counts: {expected.Count} tables held to the build, word {fromProducts["word"]} and verse {fromProducts["verse"]} to their products{(pass ? "" : ": DIFFERS")}");
        return pass;
    }

    private static async Task<bool> Keys(string connectionString, ProductStore store, string database, StringBuilder report)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "SELECT count(*), count(*) FILTER (WHERE NOT convalidated) FROM pg_constraint WHERE contype = 'f' AND connamespace = 'public'::regnamespace", connection);
        await using var reader = await command.ExecuteReaderAsync();
        await reader.ReadAsync();
        var (keys, unvalidated) = (reader.GetInt64(0), reader.GetInt64(1));
        var built = ServedState.Read(store, database)?.Header.Notes.LastOrDefault(n => n.Key == ServedState.KeysNote).Value;
        var pass = unvalidated == 0 && (built is null || long.Parse(built, CultureInfo.InvariantCulture) == keys);
        report.AppendLine(CultureInfo.InvariantCulture,
            $"foreign keys: {keys} in place{(built is null ? "" : $", the build made {built}")}, {unvalidated} not validated{(pass ? "" : ": FAILS")}");
        return pass;
    }
}
