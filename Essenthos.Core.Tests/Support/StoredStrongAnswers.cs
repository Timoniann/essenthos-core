using Essenthos.Core.Build;
using Essenthos.Core.Database;
using Essenthos.Core.Indexes;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Essenthos.Core.Tests;

/// <summary>
/// What the build writes for the Strong pages from the links, the words and the frame (<see cref="StoredStrong"/>), and the
/// number each word carries for marking (<see cref="ChainedNumbers"/>, which the build reads from files), written for a
/// test's database on its connection and transaction, replacing what the tables held.
/// </summary>
internal static class StoredStrongAnswers
{
    public static async Task WriteStoredStrong(this AppDbContext db)
    {
        await db.SaveChangesAsync();
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
        {
            await connection.OpenAsync();
        }

        await StoredStrong.WritePairLinks(connection, null, null, CancellationToken.None);
        await StoredStrong.WriteOccurrences(connection, null, null, CancellationToken.None);
        await WriteNumbers(connection);
    }

    /// <summary>The texts' numbers by the build's rule, worked out from the words and links the test put in, keyed by word id.</summary>
    private static async Task WriteNumbers(NpgsqlConnection connection)
    {
        var translations = new Dictionary<string, bool>(StringComparer.Ordinal);
        await foreach (var row in Rows(connection, "SELECT id::text, kind = 'translation' FROM text"))
        {
            translations[(string)row[0]] = (bool)row[1];
        }

        var words = new Dictionary<string, List<(string Key, string? Own, string Form)>>(StringComparer.Ordinal);
        var verses = new Dictionary<string, int>(StringComparer.Ordinal);
        await foreach (var row in Rows(connection, "SELECT text_id::text, id::text, verse_id, strong_number, coalesce(normalised_text, lower(text)) FROM word ORDER BY text_id, verse_id, position"))
        {
            var text = (string)row[0];
            if (!words.TryGetValue(text, out var held))
            {
                words[text] = held = [];
            }

            held.Add(((string)row[1], row[3] as string, (string)row[4]));
            verses[(string)row[1]] = (int)row[2];
        }

        var sides = new Dictionary<long, (string From, string To, double? Confidence, List<string> FromWords, List<string> ToWords)>();
        await foreach (var row in Rows(connection,
                           """
                           SELECT l.id, l.from_text_id::text, l.to_text_id::text, l.confidence, w.word_id::text, w.side::text
                           FROM link l JOIN link_word w ON w.link_id = l.id
                           WHERE l.relation IN ('renders', 'equals')
                           ORDER BY l.id, w.word_id
                           """))
        {
            var id = (long)row[0];
            if (!sides.TryGetValue(id, out var link))
            {
                sides[id] = link = ((string)row[1], (string)row[2], row[3] as double?, [], []);
            }

            ((string)row[5] == "from" ? link.FromWords : link.ToWords).Add((string)row[4]);
        }

        var order = ChainedNumbers.Order(translations, sides.Values.Select(l => (l.From, l.To)).Distinct());
        var place = order.Select((text, at) => (text, at)).ToDictionary(t => t.text, t => t.at, StringComparer.Ordinal);
        var own = words.ToDictionary(t => t.Key, t => (IReadOnlyDictionary<string, (string, int)>)t.Value
            .Where(w => w.Own is not null).ToDictionary(w => w.Key, w => (w.Own!, 0), StringComparer.Ordinal), StringComparer.Ordinal);
        var given = new Dictionary<string, IReadOnlyDictionary<string, (string, int)>>(StringComparer.Ordinal);
        var rows = new List<(int Verse, long Word, string Number)>();
        foreach (var text in order.Where(words.ContainsKey))
        {
            var links = sides.Values
                .SelectMany(l => new[] { (Mine: l.From, Other: l.To, Words: l.FromWords, Theirs: l.ToWords, l.Confidence), (Mine: l.To, Other: l.From, Words: l.ToWords, Theirs: l.FromWords, l.Confidence) })
                .Where(l => l.Mine == text)
                .Select(l => new ChainedNumbers.Joined(l.Words, l.Other, l.Theirs, l.Confidence))
                .ToList();
            var lenders = links.Select(l => l.Other).Distinct().ToDictionary(other => other, other => new ChainedNumbers.Lender(
                !translations[other], place[other] < place[text] ? given[other] : own.GetValueOrDefault(other) ?? new Dictionary<string, (string, int)>()),
                StringComparer.Ordinal);
            var numbered = ChainedNumbers.Resolve(words[text], links, lenders);
            given[text] = numbered.ToDictionary(n => n.Key, n => (n.Number, n.Hops), StringComparer.Ordinal);
            rows.AddRange(numbered.Select(n => (verses[n.Key], long.Parse(n.Key), n.Number)));
        }

        await using (var delete = new NpgsqlCommand("DELETE FROM word_rendered_strong", connection))
        {
            await delete.ExecuteNonQueryAsync();
        }

        await using var writer = await connection.BeginBinaryImportAsync("COPY word_rendered_strong (verse_id, word_id, strong_number) FROM STDIN (FORMAT BINARY)");
        foreach (var (verse, word, number) in rows)
        {
            await writer.WriteRowAsync(CancellationToken.None, verse, word, number);
        }

        await writer.CompleteAsync();
    }

    private static async IAsyncEnumerable<object[]> Rows(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var row = new object[reader.FieldCount];
            reader.GetValues(row);
            yield return [.. row.Select(v => v is DBNull ? null! : v)];
        }
    }
}
