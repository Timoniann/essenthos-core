using System.Numerics;
using System.Reflection;
using System.Text.RegularExpressions;
using Essenthos.Core.Database;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Npgsql;

namespace Essenthos.Core.Loading.Marks;

/// <summary>
/// Which tables each migration touches, so that a migration moves only the marks of the steps that read
/// or wrote one of them. A step's mark keeps the sum of <c>__EFMigrationsHistory</c> as it found it;
/// the migrations applied since are the rows the history holds now beyond the ones that make up that
/// sum. When none of them touches a table the step read, the history is where it was for that step.
///
/// <para>
/// A migration whose reach cannot be told — raw SQL that defines a function or names no known table,
/// an operation on the database, a schema or a sequence, a migration this build does not have — touches
/// every table. So does a history the mark's sum cannot be found in. Data and columns a migration
/// changes move the tables' own sums as well; this is for what the sums cannot see (indexes,
/// constraints, defaults).
/// </para>
/// </summary>
internal sealed partial class MigrationTables(IReadOnlyDictionary<string, IReadOnlySet<string>?> touches)
{
    /// <summary>What raw SQL names: a table, or some other object or word that is none.</summary>
    internal const string Raw = "sql:";

    /// <summary>Each migration this build knows, with what it touches.</summary>
    public IReadOnlyDictionary<string, IReadOnlySet<string>?> Touches => touches;

    /// <summary>Every table, for a migration whose reach cannot be told.</summary>
    private static readonly IReadOnlySet<string>? Everything = null;

    /// <summary>The migrations compiled into the corpus, each with the tables its operations touch.</summary>
    public static MigrationTables OfTheCorpus() => new(Read(typeof(AppDbContext).Assembly));

    /// <summary>
    /// Whether the history now differs from the one summed as <paramref name="then"/> only by migrations
    /// that touch none of <paramref name="reads"/>; and if not, a line saying which do.
    /// </summary>
    public async Task<(bool Unrelated, string Why)> Since(
        string connectionString,
        string then,
        IReadOnlyCollection<string> reads,
        IReadOnlySet<string> known,
        CancellationToken cancellationToken)
    {
        var applied = await Applied(connectionString, cancellationToken);
        var since = Prefix(applied.ByPosition, then) ?? Prefix(applied.ById, then);
        if (since is null)
        {
            return (false, "the applied migrations, which no longer include those it ran after");
        }

        var related = new List<string>();
        foreach (var migration in since)
        {
            if (!touches.TryGetValue(migration, out var touched) || touched is null)
            {
                related.Add($"migration {migration} (its reach cannot be told)");
                continue;
            }

            var tables = touched.Where(name => !name.StartsWith(Raw, StringComparison.Ordinal)).ToHashSet(StringComparer.Ordinal);
            var guessed = touched.Where(name => name.StartsWith(Raw, StringComparison.Ordinal)).Select(name => name[Raw.Length..]).ToList();
            if (guessed.Count > 0 && !guessed.Any(known.Contains))
            {
                related.Add($"migration {migration} (raw SQL naming no table)");
                continue;
            }

            tables.UnionWith(guessed.Where(known.Contains));
            if (tables.Intersect(reads, StringComparer.Ordinal).ToList() is { Count: > 0 } shared)
            {
                related.Add($"migration {migration} ({string.Join(", ", shared)})");
            }
        }

        return (related.Count == 0, string.Join(", ", related));
    }

    /// <summary>
    /// The migrations after the first rows of <paramref name="rows"/> whose sum is <paramref name="then"/>,
    /// as <see cref="TableFingerprints"/> sums a table; null when no number of first rows sums to it.
    /// </summary>
    private static List<string>? Prefix(IReadOnlyList<(string Id, long Hash)> rows, string then)
    {
        var sum = BigInteger.Zero;
        for (var count = 0; count <= rows.Count; count++)
        {
            if ($"{count}:{sum}" == then)
            {
                return [.. rows.Skip(count).Select(row => row.Id)];
            }

            if (count < rows.Count)
            {
                sum += rows[count].Hash;
            }
        }

        return null;
    }

    private static async Task<(List<(string, long)> ByPosition, List<(string, long)> ById)> Applied(
        string connectionString, CancellationToken cancellationToken)
    {
        var rows = new List<(string Id, long Hash, long Position)>();
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            $"""
             SELECT migration_id, hashtextextended(x::text, 0), row_number() OVER (ORDER BY ctid)
             FROM public."{TableFingerprints.Migrations}" x
             """,
            connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add((reader.GetString(0), reader.GetInt64(1), reader.GetInt64(2)));
        }

        return (
            [.. rows.OrderBy(row => row.Position).Select(row => (row.Id, row.Hash))],
            [.. rows.OrderBy(row => row.Id, StringComparer.Ordinal).Select(row => (row.Id, row.Hash))]);
    }

    private static Dictionary<string, IReadOnlySet<string>?> Read(Assembly assembly)
    {
        var touches = new Dictionary<string, IReadOnlySet<string>?>(StringComparer.Ordinal);
        foreach (var type in assembly.GetTypes().Where(type => !type.IsAbstract && typeof(Migration).IsAssignableFrom(type)))
        {
            if (type.GetCustomAttribute<MigrationAttribute>() is not { } attribute)
            {
                continue;
            }

            Migration migration;
            try
            {
                migration = (Migration)Activator.CreateInstance(type)!;
                migration.ActiveProvider = "Npgsql.EntityFrameworkCore.PostgreSQL";
                touches[attribute.Id] = Tables(migration.UpOperations);
            }
            catch (Exception exception) when (exception is TargetInvocationException or InvalidOperationException or MissingMethodException)
            {
                touches[attribute.Id] = Everything;
            }
        }

        return touches;
    }

    /// <summary>
    /// The tables the operations touch, the words of raw SQL prefixed <see cref="Raw"/>, or null where
    /// one of them could touch any.
    /// </summary>
    internal static IReadOnlySet<string>? Tables(IEnumerable<MigrationOperation> operations)
    {
        var tables = new HashSet<string>(StringComparer.Ordinal);
        foreach (var operation in operations)
        {
            switch (operation)
            {
                case SqlOperation sql:
                    if (Routine().IsMatch(sql.Sql))
                    {
                        return Everything;
                    }

                    tables.UnionWith(Identifier().Matches(sql.Sql)
                        .Select(match => match.Value.StartsWith('"') ? match.Value.Trim('"') : match.Value.ToLowerInvariant())
                        .Select(name => Raw + name));
                    continue;
                case CreateTableOperation create:
                    tables.Add(create.Name);
                    tables.UnionWith(create.ForeignKeys.Select(key => key.PrincipalTable));
                    continue;
                case AddForeignKeyOperation key:
                    tables.Add(key.Table);
                    tables.Add(key.PrincipalTable);
                    continue;
                case RenameTableOperation rename:
                    tables.Add(rename.Name);
                    tables.Add(rename.NewName ?? rename.Name);
                    continue;
                case DropTableOperation drop:
                    tables.Add(drop.Name);
                    continue;
                case TableOperation table:
                    tables.Add(table.Name);
                    continue;
                case ITableMigrationOperation onTable:
                    tables.Add(onTable.Table);
                    continue;
                default:
                    return Everything;
            }
        }

        return tables;
    }

    /// <summary>Raw SQL that defines code the database runs, which any statement may reach.</summary>
    [GeneratedRegex(@"\b(FUNCTION|PROCEDURE|TRIGGER|RULE|AGGREGATE|OPERATOR|EXTENSION|VIEW)\b", RegexOptions.IgnoreCase)]
    private static partial Regex Routine();

    /// <summary>
    /// Every word of raw SQL that could be a table's name. Keywords and column names come along; they
    /// only make the migration touch more than it does.
    /// </summary>
    [GeneratedRegex(@"""[^""]+""|\b[a-z_][a-z0-9_]*\b", RegexOptions.IgnoreCase)]
    private static partial Regex Identifier();
}
