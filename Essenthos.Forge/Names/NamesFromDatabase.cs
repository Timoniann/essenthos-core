using Essenthos.Core.Conversion;
using Essenthos.Core.Products;
using Npgsql;

namespace Essenthos.Core.Names;

/// <summary>
/// A database's names written as files, by the key each word has in its text's words product (the word carries the
/// database's id there, so a correction that moved a word's position since does not misplace its name):
/// <list type="bullet">
/// <item><c>names/live/&lt;SLUG&gt;</c> for every text, what the projection is proved against;</item>
/// <item><c>names/&lt;REF&gt;</c> for the seven reference texts, their settled names (DOC-0224 §15 step 4): written only
/// where missing, or with <c>force</c>, because people edit them afterwards;</item>
/// <item><c>names/rules</c> seeded from the load's passes where missing.</item>
/// </list>
/// Reads the database only. Run again at the cut-over, against live as it stands then.
/// </summary>
internal static partial class NamesFromDatabase
{
    private const string Named =
        """
        SELECT a.word_id, t.slug, e.slug, e.kind, a.method, a.confidence, a.source, a.note
        FROM word_entity a
        JOIN entity e ON e.id = a.entity_id
        JOIN word w ON w.id = a.word_id
        JOIN text t ON t.id = w.text_id
        """;

    private sealed record Row(long Word, string Record, string Kind, string Method, double? Confidence, string Source, string? Note);

    /// <param name="Gone">Names on a word the text's words product no longer holds, by text.</param>
    public sealed record Outcome(int Texts, long Live, long Settled, IReadOnlyDictionary<string, int> Gone, IReadOnlyList<string> Skipped, TimeSpan Elapsed);

    [System.Text.RegularExpressions.GeneratedRegex(@"through (?<text>[A-Za-z0-9]+) word (?<id>\d+)")]
    private static partial System.Text.RegularExpressions.Regex CarriedFrom();

    /// <summary>The key of every word a carried note cites by its database id, by (text, id).</summary>
    private static Dictionary<(string Text, long Id), WordKey> Cited(ProductStore store, IEnumerable<LiveName> names)
    {
        var wanted = new Dictionary<string, HashSet<long>>(StringComparer.Ordinal);
        foreach (var name in names.Where(n => n.Carried))
        {
            foreach (System.Text.RegularExpressions.Match match in CarriedFrom().Matches(name.Note!))
            {
                var text = match.Groups["text"].Value.ToUpperInvariant();
                if (!wanted.TryGetValue(text, out var ids))
                {
                    wanted[text] = ids = [];
                }

                ids.Add(long.Parse(match.Groups["id"].Value, System.Globalization.CultureInfo.InvariantCulture));
            }
        }

        var keys = new System.Collections.Concurrent.ConcurrentDictionary<(string, long), WordKey>();
        Parallel.ForEach(wanted.Where(w => store.Exists(TextWord.ProductOf(w.Key))), entry =>
        {
            using var reader = store.Open(TextWord.ProductOf(entry.Key), TextWord.Schema(entry.Key));
            foreach (var word in reader.Rows())
            {
                if (word.Id is { } id && entry.Value.Contains(id))
                {
                    keys[(entry.Key, id)] = word.Key;
                }
            }
        });
        return new Dictionary<(string, long), WordKey>(keys);
    }

    /// <summary>A carried note with every word it cites by id written as the word's key (RUL-0185).</summary>
    private static string Cite(string note, IReadOnlyDictionary<(string Text, long Id), WordKey> cited) =>
        CarriedFrom().Replace(note, match =>
        {
            var text = match.Groups["text"].Value.ToUpperInvariant();
            var id = long.Parse(match.Groups["id"].Value, System.Globalization.CultureInfo.InvariantCulture);
            return cited.TryGetValue((text, id), out var key) ? $"through {key}" : $"through {text}, a word its words product no longer holds";
        });

    public static async Task<Outcome> Run(string connectionString, ProductStore store, string source, string frozen, bool force, CancellationToken cancellationToken)
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var byText = new Dictionary<string, List<Row>>(StringComparer.Ordinal);
        await using (var connection = new NpgsqlConnection(connectionString))
        {
            await connection.OpenAsync(cancellationToken);
            await using var command = new NpgsqlCommand(Named, connection) { CommandTimeout = 0 };
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var text = reader.GetString(1);
                if (!byText.TryGetValue(text, out var rows))
                {
                    byText[text] = rows = [];
                }

                rows.Add(new Row(
                    reader.GetInt64(0), reader.GetString(2), reader.GetString(3), reader.GetString(4),
                    reader.IsDBNull(5) ? null : reader.GetDouble(5), reader.GetString(6), reader.IsDBNull(7) ? null : reader.GetString(7)));
            }
        }

        var gone = new System.Collections.Concurrent.ConcurrentDictionary<string, int>(StringComparer.Ordinal);
        var skipped = new System.Collections.Concurrent.ConcurrentBag<string>();
        long live = 0, settled = 0;
        var references = new System.Collections.Concurrent.ConcurrentDictionary<string, (List<LiveName> Names, ProductInput Input)>(StringComparer.Ordinal);
        var exported = $"from {source}";
        Parallel.ForEach(byText.OrderByDescending(t => t.Value.Count), new ParallelOptions { MaxDegreeOfParallelism = 8 }, entry =>
        {
            var (text, rows) = (entry.Key, entry.Value);
            if (!store.Exists(TextWord.ProductOf(text)))
            {
                skipped.Add(text);
                return;
            }

            var wanted = rows.Select(r => r.Word).ToHashSet();
            var keys = new Dictionary<long, WordKey>(wanted.Count);
            var input = store.ProductInput(TextWord.ProductOf(text));
            using (var reader = store.Open(TextWord.ProductOf(text), TextWord.Schema(text)))
            {
                foreach (var word in reader.Rows())
                {
                    if (word.Id is { } id && wanted.Contains(id))
                    {
                        keys[id] = word.Key;
                    }
                }
            }

            var names = new List<LiveName>(rows.Count);
            foreach (var row in rows)
            {
                if (!keys.TryGetValue(row.Word, out var key))
                {
                    gone.AddOrUpdate(text, 1, (_, n) => n + 1);
                    continue;
                }

                names.Add(new LiveName(key, row.Record, row.Kind, row.Method, row.Confidence, row.Source, row.Note,
                    row.Note?.StartsWith("through ", StringComparison.Ordinal) == true));
            }

            names.Sort((a, b) => a.Own.CompareTo(b.Own) is var order && order != 0 ? order : string.CompareOrdinal(a.Record, b.Record));
            using (var writer = store.Create(NameProducts.Live(text), ProductKind.Machine, LiveName.Schema(text),
                       ProductHeader.DescribeProducer("forge names-from-database"), [input],
                       header => header.Notes.Add(new("exported", exported))))
            {
                foreach (var name in names)
                {
                    writer.Write(name);
                }

                writer.Complete();
                Interlocked.Add(ref live, writer.Rows);
            }

            if (NameProducts.IsReference(text) && (force || !store.Exists(NameProducts.Settled(text))))
            {
                references[text] = (names, input);
            }
        });

        var cited = Cited(store, references.Values.SelectMany(r => r.Names));
        foreach (var (text, (names, input)) in references.OrderBy(r => r.Key, StringComparer.Ordinal))
        {
            using var writer = store.Create(NameProducts.Settled(text), ProductKind.Hand, SettledName.Schema(text),
                ProductHeader.DescribeProducer("forge names-from-database"), [input],
                header =>
                {
                    header.Notes.Add(new("exported", exported));
                    header.Notes.Add(new("edited", "by hand from here on: a row is a decision that this word names this record"));
                });
            foreach (var name in names)
            {
                writer.Write(new SettledName(name.Own, name.Record, NameRoles.OfKind(name.Kind), name.Method, name.Confidence, name.Source,
                    name.Carried ? $"frozen {frozen}: {Cite(name.Note!, cited)}" : name.Note));
            }

            writer.Complete();
            settled += writer.Rows;
        }

        if (force || !store.Exists(NameProducts.Rules))
        {
            using var writer = store.Create(NameProducts.Rules, ProductKind.Hand, NameRuleRow.Schema,
                ProductHeader.DescribeProducer("forge names-from-database"), [],
                header => header.Notes.Add(new("seeded", "from the load's passes (Pronouns, Annotating.NeverAName, Annotating.Faint); kept by hand from here on")));
            foreach (var rule in NameRules.Seed())
            {
                writer.Write(rule);
            }

            writer.Complete();
        }

        store.Save();
        return new Outcome(byText.Count, live, settled, gone, [.. skipped.Order(StringComparer.Ordinal)], clock.Elapsed);
    }
}
