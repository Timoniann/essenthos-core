using Essenthos.Core.BetaMasaheft;
using Essenthos.Core.Corpus;
using Essenthos.Core.Database;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Essenthos.Core.Loading;

/// <summary>
/// How many words of the Ethiopic Bible reach an entry of Dillmann's lexicon, and by which way, read
/// from the lexicon's files and the loaded corpus the way the reader reaches them — and a random
/// sample of the matches written out for somebody to read. Reads only.
/// </summary>
internal sealed class GeezLexiconMeasure(AppDbContext db)
{
    private const string Words =
        """
        SELECT w.id, b.canonical_ordinal, v.chapter_number, v.number, w.text,
               coalesce(array_agg(g.lemma) FILTER (WHERE g.lemma IS NOT NULL), '{}'),
               coalesce(array_agg(g.text) FILTER (WHERE g.text IS NOT NULL), '{}')
        FROM word w
        JOIN verse v ON v.id = w.verse_id
        JOIN book b ON b.id = v.book_id
        LEFT JOIN link_word side ON side.word_id = w.id
        LEFT JOIN link_word other ON other.link_id = side.link_id AND other.side <> side.side
        LEFT JOIN word g ON g.id = other.word_id AND g.text_id IN (SELECT id FROM text WHERE language = 'grc')
        WHERE w.text_id = (SELECT id FROM text WHERE slug = @slug)
        GROUP BY w.id, b.canonical_ordinal, v.chapter_number, v.number, w.text
        """;

    /// <summary>The last book of the Old Testament and of the New in the canonical numbering.</summary>
    private const int LastOld = 39;

    private const int LastNew = 66;

    public async Task<string> Measure(string folder, string? samplePath, int sampleSize, CancellationToken cancellationToken)
    {
        var headwords = DillmannLexicon.Headwords(DillmannLexicon.Read(folder));
        var lexicon = new GeezLexicon(headwords.Select(headword => new GeezHeadword(headword.Entry, headword.Forms, headword.Greek)));
        var byEntry = headwords.ToDictionary(headword => headword.Entry, StringComparer.Ordinal);

        await db.Database.OpenConnectionAsync(cancellationToken);
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();
        await using var command = new NpgsqlCommand(Words, connection) { CommandTimeout = 0 };
        command.Parameters.AddWithValue("slug", Sources.GeezSlug);

        var counts = new Dictionary<(string Part, string Via), int>();
        var matched = new List<string>();
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                var book = reader.GetInt32(1);
                var surface = reader.GetString(4);
                var greek = reader.GetFieldValue<string[]>(5).Concat(reader.GetFieldValue<string[]>(6)).ToList();
                var match = lexicon.Match(surface, greek);
                var part = book <= LastOld ? "old" : book <= LastNew ? "new" : "other";
                var via = match?.Via ?? "none";
                counts[(part, via)] = counts.GetValueOrDefault((part, via)) + 1;
                if (match is not null)
                {
                    var entry = byEntry[match.Entry];
                    matched.Add(string.Join('\t',
                        $"{book}:{reader.GetInt32(2)}:{reader.GetInt32(3)}", surface, match.Via, entry.Headword,
                        string.Join(", ", entry.Latin.Take(4)), string.Join(", ", entry.Greek.Take(3)),
                        string.Join(" ", greek.Distinct().Take(3))));
                }
            }
        }

        if (samplePath is not null)
        {
            var random = new Random(sampleSize);
            await File.WriteAllLinesAsync(samplePath, matched.OrderBy(_ => random.Next()).Take(sampleSize), cancellationToken);
        }

        var total = counts.Values.Sum();
        var lines = counts
            .GroupBy(pair => pair.Key.Part)
            .Select(part =>
            {
                var all = part.Sum(pair => pair.Value);
                var found = part.Where(pair => pair.Key.Via != "none").Sum(pair => pair.Value);
                return $"{part.Key}: {found} of {all} words ({100.0 * found / all:0.0}%) — "
                       + string.Join(", ", part.OrderBy(pair => pair.Key.Via).Select(pair => $"{pair.Key.Via} {pair.Value}"));
            });
        var reached = counts.Where(pair => pair.Key.Via != "none").Sum(pair => pair.Value);
        return $"{headwords.Count} headwords; {reached} of {total} Ge'ez words ({100.0 * reached / total:0.0}%) reach one\n"
               + string.Join('\n', lines);
    }
}
