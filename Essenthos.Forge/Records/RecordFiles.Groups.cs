using System.Text.RegularExpressions;
using Essenthos.Core.Products;
using Npgsql;

namespace Essenthos.Core.Records;

internal static partial class RecordFiles
{
    /// <summary>
    /// The record files in groups that load apart: two files are in one group when they write one table, when a foreign key joins
    /// a table of one to a table of the other, or when one's statements (load, after, check, requires) name a table the other
    /// writes. A group shares nothing with the rest, so it can be left as it stands while the rest is loaded again.
    /// </summary>
    internal static List<List<RecordFile>> Groups(IReadOnlyList<RecordFile> files, IEnumerable<(string Table, string References)> keys)
    {
        var parent = Enumerable.Range(0, files.Count).ToArray();
        int Root(int i) => parent[i] == i ? i : parent[i] = Root(parent[i]);
        void Join(int a, int b) => parent[Root(a)] = Root(b);

        var writers = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = 0; i < files.Count; i++)
        {
            foreach (var table in Writes(files[i]))
            {
                if (writers.TryGetValue(table, out var other))
                {
                    Join(i, other);
                }
                else
                {
                    writers[table] = i;
                }
            }
        }

        foreach (var (table, references) in keys)
        {
            if (writers.TryGetValue(table, out var a) && writers.TryGetValue(references, out var b))
            {
                Join(a, b);
            }
        }

        var named = new Regex(@"\b(" + string.Join('|', writers.Keys.OrderByDescending(t => t.Length).Select(Regex.Escape)) + @")\b",
            RegexOptions.CultureInvariant);
        for (var i = 0; i < files.Count; i++)
        {
            var file = files[i];
            foreach (Match match in named.Matches(string.Join('\n', file.Load, file.After, file.Check, file.Requires)))
            {
                Join(i, writers[match.Value]);
            }
        }

        return [.. Enumerable.Range(0, files.Count).GroupBy(Root).Select(g => g.Select(i => files[i]).ToList())];
    }

    /// <summary>The tables a file writes: those it empties, and the one its load inserts into.</summary>
    internal static IEnumerable<string> Writes(RecordFile file) =>
        file.Tables.Concat(Inserts().Matches(file.Load).Select(m => m.Groups[1].Value)).Distinct(StringComparer.Ordinal);

    [GeneratedRegex(@"INSERT\s+INTO\s+([a-z_]+)", RegexOptions.CultureInvariant)]
    private static partial Regex Inserts();

    /// <summary>
    /// Like <see cref="Load(NpgsqlConnection, NpgsqlTransaction, ProductStore, CancellationToken)"/>, but a group of files
    /// (<see cref="Groups"/>) none of whose products is in <paramref name="moved"/>, that writes a table and does not hold the
    /// records themselves, is left as it stands: its tables are neither emptied nor loaded. Returns the files loaded and the
    /// groups left.
    /// </summary>
    public static async Task<(List<(string Product, long Rows)> Loaded, List<string> Left)> LoadMoved(
        NpgsqlConnection db, NpgsqlTransaction transaction, ProductStore store, IReadOnlySet<string> moved, CancellationToken cancellationToken)
    {
        if (Untouchable.Contains(db.Database, StringComparer.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"records-load does not write {db.Database}; load into a database of your own.");
        }

        await Execute(db, transaction, SchemaSql, cancellationToken);
        var keys = new List<(string, string)>();
        await using (var command = new NpgsqlCommand(
                         "SELECT conrelid::regclass::text, confrelid::regclass::text FROM pg_constraint WHERE contype = 'f' AND connamespace = 'public'::regnamespace",
                         db, transaction))
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                keys.Add((reader.GetString(0), reader.GetString(1)));
            }
        }

        var left = Groups(All, keys)
            .Where(group => group.Any(f => Writes(f).Any()) && !group.Any(f => Writes(f).Contains("entity")) && !group.Any(f => moved.Contains(f.Product)))
            .ToList();
        var leftFiles = left.SelectMany(g => g).ToHashSet();
        var loaded = await LoadFiles(db, transaction, store, [.. All.Where(f => !leftFiles.Contains(f))], cancellationToken);
        loaded.Add((RecordCanons.Product, await RecordCanons.Load(db, transaction, store, cancellationToken)));
        return (loaded, [.. left.Select(g => string.Join(", ", g.Select(f => f.Product)))]);
    }
}
