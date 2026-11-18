using System.Text.RegularExpressions;
using Essenthos.Core.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Npgsql;

namespace Essenthos.Core.Build;

/// <summary>
/// A database behind this Forge's migrations (a seed cloned before they were merged) migrated by <c>build --apply</c>, and the
/// tables they touched loaded again whole (<see cref="ApplyReloads"/>). Every operation of a pending migration is read before
/// anything runs: an index or a foreign key changes no row; a table created, a column or key added, changed or dropped, and every
/// row a migration's SQL writes, touch their table. A touched table only a fresh build fills, or one no load is known to fill,
/// refuses the apply before the database is migrated. After the migrations, every table whose columns, keys, checks or triggers
/// moved is held to what was read, and one the operations did not name is loaded again too, or refused.
/// </summary>
internal static partial class SeedMigration
{
    /// <summary>What a migration's operations touch, and why an operation cannot be read.</summary>
    internal sealed class Reading
    {
        public SortedDictionary<string, string> Touched { get; } = new(StringComparer.Ordinal);

        public SortedSet<string> Dropped { get; } = new(StringComparer.Ordinal);

        public SortedSet<string> Functions { get; } = new(StringComparer.Ordinal);

        public string? Refused { get; set; }

        public void Touch(string table, string because) => Touched.TryAdd(Unquote(table), because);
    }

    /// <summary>
    /// Migrates the configured database when it lacks migrations of this Forge, and returns what must be loaded again whole for
    /// it; null when the apply is refused (logged, with the reason) or a migration failed.
    /// </summary>
    public static async Task<ApplyReloads?> Run(string connectionString, IServiceProvider services, ProductGraph graph, ILogger logger, CancellationToken cancellationToken)
    {
        var reloads = new ApplyReloads();
        var database = new NpgsqlConnectionStringBuilder(connectionString).Database;
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var pending = (await db.Database.GetPendingMigrationsAsync(cancellationToken)).ToList();
        if (pending.Count == 0)
        {
            return reloads;
        }

        var assembly = db.GetService<IMigrationsAssembly>();
        var reading = new Reading();
        foreach (var id in pending)
        {
            Read(id, assembly.CreateMigration(assembly.Migrations[id], db.Database.ProviderName!).UpOperations, reading);
        }

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        foreach (var (function, table) in await TriggersCalling(connection, reading.Functions, cancellationToken))
        {
            reading.Touch(table, $"its trigger calls {function}, which a migration replaces");
        }

        var because = $"migrated ({string.Join(", ", pending)})";
        var refused = reading.Refused
                      ?? reloads.Reload(reading.Touched.Keys.Where(t => !reading.Dropped.Contains(t)), graph, because);
        if (refused is not null)
        {
            logger.LogError("{Database} lacks {Count} migration(s) of this Forge ({Migrations}), and apply cannot follow them: {Refused}. Build --into a new database.",
                database, pending.Count, string.Join(", ", pending), refused);
            return null;
        }

        foreach (var (table, why) in reading.Touched)
        {
            logger.LogInformation("migration touches {Table}: {Why}", table, why);
        }

        var before = await Shape(connection, cancellationToken);
        try
        {
            db.Database.SetCommandTimeout(TimeSpan.FromHours(2));
            await db.Database.MigrateAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is NpgsqlException or InvalidOperationException or DbUpdateException)
        {
            logger.LogError(exception, "Migrating {Database} failed: build --into a new database", database);
            return null;
        }

        var after = await Shape(connection, cancellationToken);
        // A table both dropped and touched stands after the migrations only when one made it again.
        var unread = after.Where(a => reading.Touched.ContainsKey(a.Key)
                ? reading.Dropped.Contains(a.Key)
                : !before.TryGetValue(a.Key, out var shape) || shape != a.Value)
            .Select(a => a.Key).Order(StringComparer.Ordinal).ToList();
        if (unread.Count > 0)
        {
            logger.LogWarning("The migrations changed {Tables} beyond what their operations name, or made them again after dropping them: loaded again as well", string.Join(", ", unread));
            if (reloads.Reload(unread, graph, $"{because}, changed beyond its operations") is { } beyond)
            {
                logger.LogError("{Database} is migrated, but apply cannot follow it: {Refused}. Build --into a new database.", database, beyond);
                return null;
            }
        }

        reloads.Migrations.AddRange(pending);
        return reloads;
    }

    /// <summary>What each operation of one migration touches, into <paramref name="reading"/>.</summary>
    internal static void Read(string migration, IEnumerable<MigrationOperation> operations, Reading reading)
    {
        foreach (var operation in operations)
        {
            switch (operation)
            {
                case CreateIndexOperation or DropIndexOperation or RenameIndexOperation:
                case AddForeignKeyOperation or DropForeignKeyOperation:
                    break;
                case DropTableOperation drop:
                    reading.Dropped.Add(drop.Name);
                    break;
                case RenameTableOperation rename:
                    reading.Touch(rename.NewName ?? rename.Name, $"{migration}: renamed from {rename.Name}");
                    break;
                case CreateTableOperation create:
                    reading.Touch(create.Name, $"{migration}: created");
                    break;
                case SqlOperation sql:
                    ReadSql(migration, sql.Sql, reading);
                    break;
                case ITableMigrationOperation table:
                    reading.Touch(table.Table, $"{migration}: {operation.GetType().Name.Replace("Operation", "", StringComparison.Ordinal)}");
                    break;
                default:
                    reading.Refused ??= $"{migration}: a {operation.GetType().Name} is not an operation apply knows what it does to the rows";
                    break;
            }
        }
    }

    /// <summary>
    /// What a migration's own SQL touches: every table it creates, alters, writes, truncates or puts a trigger on. SQL whose
    /// targets cannot be read from its text (dynamic SQL, sequences, extensions, rules) refuses.
    /// </summary>
    internal static void ReadSql(string migration, string sql, Reading reading)
    {
        var text = LineComment().Replace(sql, "");
        if (Unreadable().Match(text) is { Success: true } unreadable)
        {
            reading.Refused ??= $"{migration}: its SQL holds '{unreadable.Value.Trim()}', whose effect on the rows cannot be read";
            return;
        }

        foreach (Match match in DropTable().Matches(text))
        {
            foreach (var table in match.Groups["tables"].Value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                reading.Dropped.Add(Unquote(table));
            }
        }

        foreach (var (regex, what) in new[]
                 {
                     (CreateTable(), "created"), (AlterTable(), "altered"), (InsertInto(), "rows inserted"), (Update(), "rows updated"),
                     (DeleteFrom(), "rows deleted"), (Copy(), "rows copied"), (Trigger(), "a trigger put on it"),
                 })
        {
            foreach (Match match in regex.Matches(text))
            {
                reading.Touch(match.Groups["table"].Value, $"{migration}: {what} by its SQL");
            }
        }

        foreach (Match match in Truncate().Matches(text))
        {
            foreach (var table in match.Groups["tables"].Value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                reading.Touch(table, $"{migration}: truncated by its SQL");
            }
        }

        foreach (Match match in Function().Matches(text))
        {
            reading.Functions.Add(Unquote(match.Groups["function"].Value));
        }
    }

    private static string Unquote(string name)
    {
        var bare = name.Trim().Trim('"');
        return bare.StartsWith("public.", StringComparison.OrdinalIgnoreCase) ? bare["public.".Length..].Trim('"') : bare.Replace("\"", "", StringComparison.Ordinal);
    }

    /// <summary>The tables whose triggers call a function of these names.</summary>
    private static async Task<List<(string Function, string Table)>> TriggersCalling(NpgsqlConnection connection, IReadOnlyCollection<string> functions,
        CancellationToken cancellationToken)
    {
        var found = new List<(string, string)>();
        if (functions.Count == 0)
        {
            return found;
        }

        await using var command = new NpgsqlCommand(
            "SELECT p.proname, t.tgrelid::regclass::text FROM pg_trigger t JOIN pg_proc p ON p.oid = t.tgfoid WHERE NOT t.tgisinternal AND p.proname = ANY(@functions)",
            connection);
        command.Parameters.AddWithValue("functions", functions.ToArray());
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            found.Add((reader.GetString(0), Unquote(reader.GetString(1))));
        }

        return found;
    }

    /// <summary>Each table's columns, keys and checks (foreign keys aside: they change no row) and triggers with their functions' source.</summary>
    private static async Task<Dictionary<string, string>> Shape(NpgsqlConnection connection, CancellationToken cancellationToken)
    {
        var shapes = new Dictionary<string, string>(StringComparer.Ordinal);
        await using var command = new NpgsqlCommand(
            """
            SELECT c.relname,
                   coalesce((SELECT string_agg(a.attname || ' ' || format_type(a.atttypid, a.atttypmod) || CASE WHEN a.attnotnull THEN ' not null' ELSE '' END
                                               || coalesce(' default ' || pg_get_expr(d.adbin, d.adrelid), '') || ' ' || a.attidentity || a.attgenerated, ', ' ORDER BY a.attnum)
                             FROM pg_attribute a LEFT JOIN pg_attrdef d ON d.adrelid = a.attrelid AND d.adnum = a.attnum
                             WHERE a.attrelid = c.oid AND a.attnum > 0 AND NOT a.attisdropped), '')
                   || ' | ' || coalesce((SELECT string_agg(pg_get_constraintdef(k.oid), ', ' ORDER BY pg_get_constraintdef(k.oid))
                                         FROM pg_constraint k WHERE k.conrelid = c.oid AND k.contype <> 'f'), '')
                   || ' | ' || coalesce((SELECT string_agg(pg_get_triggerdef(t.oid) || ' ' || md5(p.prosrc), ', ' ORDER BY t.tgname)
                                         FROM pg_trigger t JOIN pg_proc p ON p.oid = t.tgfoid WHERE t.tgrelid = c.oid AND NOT t.tgisinternal), '')
            FROM pg_class c WHERE c.relkind IN ('r', 'p') AND c.relnamespace = 'public'::regnamespace
            """, connection) { CommandTimeout = 0 };
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            shapes[reader.GetString(0)] = reader.GetString(1);
        }

        return shapes;
    }

    private const string Name = """(?<table>(?:"?public"?\.)?"?\w+"?)""";

    [GeneratedRegex(@"--[^\r\n]*")]
    private static partial Regex LineComment();

    [GeneratedRegex(@"\bEXECUTE\s+(?!FUNCTION\b|PROCEDURE\b)|\b(?:CREATE|ALTER|DROP)\s+SEQUENCE\b|\bsetval\s*\(|\bCREATE\s+EXTENSION\b|\bALTER\s+DATABASE\b|\bCREATE\s+(?:OR\s+REPLACE\s+)?RULE\b|\bALTER\s+TABLE\b[^;]*\bRENAME\s+TO\b",
        RegexOptions.IgnoreCase)]
    private static partial Regex Unreadable();

    [GeneratedRegex(@"\bCREATE\s+(?:UNLOGGED\s+|TEMP(?:ORARY)?\s+)?TABLE\s+(?:IF\s+NOT\s+EXISTS\s+)?" + Name, RegexOptions.IgnoreCase)]
    private static partial Regex CreateTable();

    [GeneratedRegex(@"\bALTER\s+TABLE\s+(?:IF\s+EXISTS\s+)?(?:ONLY\s+)?" + Name, RegexOptions.IgnoreCase)]
    private static partial Regex AlterTable();

    [GeneratedRegex(@"\bDROP\s+TABLE\s+(?:IF\s+EXISTS\s+)?(?<tables>[\w"".,\s]+?)(?:\s+(?:CASCADE|RESTRICT)\b|\s*;|\s*$)", RegexOptions.IgnoreCase)]
    private static partial Regex DropTable();

    [GeneratedRegex(@"\bINSERT\s+INTO\s+" + Name, RegexOptions.IgnoreCase)]
    private static partial Regex InsertInto();

    [GeneratedRegex(@"\bUPDATE\s+(?:ONLY\s+)?" + Name + @"(?:\s+(?:AS\s+)?\w+)?\s+SET\b", RegexOptions.IgnoreCase)]
    private static partial Regex Update();

    [GeneratedRegex(@"\bDELETE\s+FROM\s+(?:ONLY\s+)?" + Name, RegexOptions.IgnoreCase)]
    private static partial Regex DeleteFrom();

    [GeneratedRegex(@"\bCOPY\s+" + Name + @"\s*(?:\([^)]*\)\s*)?FROM\b", RegexOptions.IgnoreCase)]
    private static partial Regex Copy();

    [GeneratedRegex(@"\bTRUNCATE\s+(?:TABLE\s+)?(?:ONLY\s+)?(?<tables>[\w"".,\s]+?)(?:\s+(?:RESTART|CONTINUE|CASCADE|RESTRICT)\b|\s*;|\s*$)", RegexOptions.IgnoreCase)]
    private static partial Regex Truncate();

    [GeneratedRegex(@"\bCREATE\s+(?:OR\s+REPLACE\s+)?(?:CONSTRAINT\s+)?TRIGGER\s+\w+\s+(?:BEFORE|AFTER|INSTEAD\s+OF)\b[^;]*?\bON\s+" + Name, RegexOptions.IgnoreCase)]
    private static partial Regex Trigger();

    [GeneratedRegex(@"\bCREATE\s+(?:OR\s+REPLACE\s+)?FUNCTION\s+(?<function>(?:""?public""?\.)?""?\w+""?)", RegexOptions.IgnoreCase)]
    private static partial Regex Function();
}
