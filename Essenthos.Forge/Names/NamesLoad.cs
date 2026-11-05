using System.Diagnostics;
using System.Globalization;
using System.Text;
using Essenthos.Core.Conversion;
using Essenthos.Core.Products;
using Essenthos.Core.Products.Essx;
using Npgsql;

namespace Essenthos.Core.Names;

/// <summary>
/// The names projection into a serving database's <c>word_entity</c> (DOC-0224 §7.3): every row of it, by the word id its
/// text's words product carries and the record's id by slug in the database, with its <c>role</c> and <c>origin</c> (a
/// <c>base-reading</c> row is the base's record beside a <c>differs</c> one on the same word, the second card), and a carried
/// name's note naming the word it was carried from by its id, as the API reads it. The texts' rows are made at once and copied
/// one COPY per text in one transaction; the table's indexes are dropped before and made after. When asked, the table is
/// then read back by key and compared with the products and
/// with <c>names/live</c>, the database the names were exported from.
/// </summary>
internal static class NamesLoad
{
    public sealed record Outcome(long Rows, int Texts, long UnknownRecords, long UnknownWords, TimeSpan Load, TimeSpan Indexes, Comparison? ReadBack);

    /// <param name="Products">(key, record) rows the products hold that the table does not, and the other way.</param>
    /// <param name="Live">(key, record) rows names/live holds that the table does not, and the other way.</param>
    public sealed record Comparison(long Rows, long MissingFromTable, long ExtraInTable, long OnlyLive, long OnlyTable);

    private const string Indexes =
        """
        CREATE UNIQUE INDEX IF NOT EXISTS ix_word_entity_word_id_entity_id ON word_entity (word_id, entity_id);
        CREATE INDEX IF NOT EXISTS ix_word_entity_entity_id ON word_entity (entity_id);
        """;

    /// <param name="readBack">Read the table back and compare it with the products and names/live (the names-load verb's
    /// proof); a build discards the comparison, so it leaves it out.</param>
    public static async Task<Outcome> Run(string connectionString, ProductStore store, IReadOnlyList<string> texts, CancellationToken cancellationToken,
        bool readBack = true)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        var records = new Dictionary<string, int>(StringComparer.Ordinal);
        await using (var command = new NpgsqlCommand("SELECT id, slug FROM entity", connection))
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                records[reader.GetString(1)] = reader.GetInt32(0);
            }
        }

        var clock = Stopwatch.StartNew();
        var ids = new WordIdCache(store);

        // Each text's lines are made at once; they are copied in the texts' order, so the table is the same row for row.
        var lines = new (List<string> Lines, long UnknownRecords, long UnknownWords)[texts.Count];
        Parallel.For(0, texts.Count, new ParallelOptions { CancellationToken = cancellationToken },
            i => lines[i] = Lines(store, texts[i], records, ids));
        long rows = lines.Sum(l => (long)l.Lines.Count), unknownRecords = lines.Sum(l => l.UnknownRecords), unknownWords = lines.Sum(l => l.UnknownWords);

        await using (var transaction = await connection.BeginTransactionAsync(cancellationToken))
        {
            await Execute(connection, "DROP INDEX IF EXISTS ix_word_entity_word_id_entity_id; DROP INDEX IF EXISTS ix_word_entity_entity_id; TRUNCATE word_entity CASCADE;", cancellationToken);
            foreach (var text in lines)
            {
                await using var writer = await connection.BeginTextImportAsync(
                    "COPY word_entity (word_id, entity_id, method, confidence, source, note, role, origin) FROM STDIN", cancellationToken);
                foreach (var line in text.Lines)
                {
                    writer.Write(line);
                }
            }

            await transaction.CommitAsync(cancellationToken);
        }

        var load = clock.Elapsed;
        clock.Restart();
        await Execute(connection, Indexes + "ANALYZE word_entity;", cancellationToken);
        var indexes = clock.Elapsed;
        var comparison = readBack ? await ReadBack(connection, store, texts, records, cancellationToken) : null;
        return new Outcome(rows, texts.Count, unknownRecords, unknownWords, load, indexes, comparison);
    }

    /// <summary>One text's names as COPY lines, with the names on a record or a word the database lacks counted and left out.</summary>
    private static (List<string> Lines, long UnknownRecords, long UnknownWords) Lines(ProductStore store, string text, Dictionary<string, int> records,
        WordIdCache cache)
    {
        long unknownRecords = 0, unknownWords = 0;
        var lines = new List<string>();
        var ids = cache.Of(text);
        using var reader = store.Open(NameProducts.Projected(text), ProjectedName.Schema(text));
        foreach (var name in reader.Rows())
        {
            if (!records.TryGetValue(name.Record, out var record))
            {
                unknownRecords++;
                continue;
            }

            if (!ids.TryGetValue(name.Own.Own, out var word))
            {
                unknownWords++;
                continue;
            }

            // The API reads the word a name was carried from by its id in this note (Annotations.CarriedFrom).
            string? note = null;
            if (name.Through is { } through && through.IndexOf(' ') is var space and > 0)
            {
                var reference = through[..space];
                note = cache.Of(reference).TryGetValue(through[(space + 1)..], out var from) ? $"through {reference} word {from}" : $"through {through}";
            }

            lines.Add(string.Create(CultureInfo.InvariantCulture,
                $"{word}\t{record}\t{Escape(name.Method)}\t{(name.Confidence is { } c ? c.ToString("R", CultureInfo.InvariantCulture) : "\\N")}\t{Escape(name.Source)}\t{(note is null ? "\\N" : Escape(note))}\t{Escape(name.Role)}\t{Escape(name.Origin)}\n"));
        }

        return (lines, unknownRecords, unknownWords);
    }

    /// <summary>Each text's word ids by key, read once however many texts carry names through it.</summary>
    private sealed class WordIdCache(ProductStore store)
    {
        private readonly System.Collections.Concurrent.ConcurrentDictionary<string, Lazy<Dictionary<string, long>>> _texts = new(StringComparer.Ordinal);

        public Dictionary<string, long> Of(string text) => _texts.GetOrAdd(text, t => new Lazy<Dictionary<string, long>>(() => WordIds(store, t))).Value;
    }

    private static async Task<Comparison> ReadBack(NpgsqlConnection connection, ProductStore store, IReadOnlyList<string> texts,
        Dictionary<string, int> records, CancellationToken cancellationToken)
    {
        var slugs = records.ToDictionary(r => r.Value, r => r.Key);
        var keys = new Dictionary<long, (string Text, string Own)>();
        foreach (var text in texts)
        {
            foreach (var (own, id) in WordIds(store, text))
            {
                keys[id] = (text, own);
            }
        }

        var table = new HashSet<(string Text, string Own, string Record)>();
        await using (var command = new NpgsqlCommand("SELECT word_id, entity_id FROM word_entity", connection))
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                var (text, own) = keys[reader.GetInt64(0)];
                table.Add((text, own, slugs[reader.GetInt32(1)]));
            }
        }

        long missing = 0, onlyLive = 0;
        var products = new HashSet<(string, string, string)>();
        var live = new HashSet<(string, string, string)>();
        foreach (var text in texts)
        {
            using (var reader = store.Open(NameProducts.Projected(text), ProjectedName.Schema(text)))
            {
                foreach (var name in reader.Rows().Where(n => records.ContainsKey(n.Record)))
                {
                    products.Add((text, name.Own.Own, name.Record));
                }
            }

            if (store.Exists(NameProducts.Live(text)))
            {
                using var reader = store.Open(NameProducts.Live(text), LiveName.Schema(text));
                foreach (var name in reader.Rows())
                {
                    live.Add((text, name.Own.Own, name.Record));
                }
            }
        }

        missing = products.Count(p => !table.Contains(p));
        onlyLive = live.Count(l => !table.Contains(l));
        return new Comparison(table.Count, missing, table.Count(t => !products.Contains(t)), onlyLive, table.Count(t => !live.Contains(t)));
    }

    private static Dictionary<string, long> WordIds(ProductStore store, string text)
    {
        var ids = new Dictionary<string, long>(StringComparer.Ordinal);
        if (store.Find(TextWord.ProductOf(text)) is { } relative && relative.EndsWith(EssxLayout.Extension, StringComparison.Ordinal))
        {
            // Two columns of the words product decoded, not all of them.
            using var file = EssxReader.Open(store.FullPath(relative));
            var table = file.Table(EssxFormat.TableName);
            for (var g = 0; g < table.RowGroups; g++)
            {
                var group = table.ReadGroup(g, ["own", "id"]);
                var own = group.Column("own");
                var id = group.Column("id");
                for (var r = 0; r < group.Rows; r++)
                {
                    if (id.Integer(r) is { } value)
                    {
                        ids[WordKey.Parse(own.Text(r)!, text).Own] = value;
                    }
                }
            }

            return ids;
        }

        using var reader = store.Open(TextWord.ProductOf(text), TextWord.Schema(text));
        foreach (var word in reader.Rows())
        {
            if (word.Id is { } id)
            {
                ids[word.Key.Own] = id;
            }
        }

        return ids;
    }

    private static string Escape(string value)
    {
        if (value.AsSpan().IndexOfAny("\\\t\n\r") < 0)
        {
            return value;
        }

        var escaped = new StringBuilder(value.Length + 8);
        foreach (var letter in value)
        {
            escaped.Append(letter switch { '\\' => "\\\\", '\t' => "\\t", '\n' => "\\n", '\r' => "\\r", _ => letter.ToString() });
        }

        return escaped.ToString();
    }

    private static async Task Execute(NpgsqlConnection connection, string sql, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(sql, connection) { CommandTimeout = 0 };
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
