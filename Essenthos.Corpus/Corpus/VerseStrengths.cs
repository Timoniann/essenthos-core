using System.Globalization;
using System.Text;
using Npgsql;

namespace Essenthos.Core.Corpus;

/// <summary>How strongly one verse of a text is linked to the reference text: the links, how many are stated, their mean confidence.</summary>
internal readonly record struct VerseStrengthCount(int Links, int Stated, double? Confidence);

/// <summary>
/// One text's verses of one canonical chapter against one reference text: each verse's strength, and whether only verse
/// links a reading drew join the two there (<see cref="ByReading"/>).
/// </summary>
internal sealed record ChapterStrengthCount(int Reference, int Book, int Chapter, bool ByReading, SortedDictionary<int, VerseStrengthCount> Verses);

/// <summary>
/// The verse strength the reader lays over a pane (<c>/v1/strength</c>), counted for one text against every text linked to
/// it, per canonical chapter, as the build's serving stage counts it (<c>verse_strength</c>, <c>strength/{text}</c>).
///
/// <para>
/// A verse's links are those of the pair, in either direction, that name a word of the text standing primarily at that
/// canonical verse, each counted once however many of its words stand there; a stated link carries no confidence and the
/// mean is over the links that have one, null where none has. A chapter is joined only by reading where every verse link
/// of the pair holding a verse of the text there was drawn by a reading (<c>model-reading</c>), and at least one is.
/// </para>
///
/// <para>
/// A chapter is listed where some link names a word of the text there or the pair is joined there only by reading: a
/// chapter not listed has no strength, and its verses meet through the shared frame.
/// </para>
/// </summary>
internal static class VerseStrengths
{
    /// <summary>The method of a verse link a reading of where each verse falls drew.</summary>
    private const string Reading = "model-reading";

    private const string LinkSql =
        """
        SELECT DISTINCT CASE WHEN lw.side = 'from' THEN l.to_text_id ELSE l.from_text_id END,
               r.canonical_book, r.canonical_chapter, r.canonical_verse, l.id, l.confidence
        FROM word w
        JOIN link_word lw ON lw.word_id = w.id
        JOIN link l ON l.id = lw.link_id
        JOIN verse_reference r ON r.verse_id = w.verse_id AND r.is_primary
        WHERE w.text_id = @text
          AND ((lw.side = 'from' AND l.from_text_id = @text AND l.to_text_id <> @text)
               OR (lw.side = 'to' AND l.to_text_id = @text AND l.from_text_id <> @text))
        """;

    private static readonly string ReadingSql =
        $"""
         SELECT CASE WHEN vl.from_text_id = @text THEN vl.to_text_id ELSE vl.from_text_id END,
                r.canonical_book, r.canonical_chapter, bool_and(vl.method = '{Reading}')
         FROM verse_link vl
         JOIN verse_link_verse s ON s.verse_link_id = vl.id
         JOIN verse v ON v.id = s.verse_id AND v.text_id = @text
         JOIN verse_reference r ON r.verse_id = v.id AND r.is_primary
         WHERE (vl.from_text_id = @text) <> (vl.to_text_id = @text)
         GROUP BY 1, 2, 3
         """;

    /// <summary>Every chapter of the text against every text linked to it, by reference text id, book and chapter.</summary>
    public static async Task<List<ChapterStrengthCount>> Count(NpgsqlConnection connection, int textId, CancellationToken cancellationToken)
    {
        var links = new Dictionary<(int Reference, int Book, int Chapter), SortedDictionary<int, List<(long Id, double? Confidence)>>>();
        await using (var command = new NpgsqlCommand(LinkSql, connection) { CommandTimeout = 0 })
        {
            command.Parameters.AddWithValue("text", textId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var key = (reader.GetInt32(0), reader.GetInt32(1), reader.GetInt32(2));
                if (!links.TryGetValue(key, out var verses))
                {
                    links[key] = verses = [];
                }

                var verse = reader.GetInt32(3);
                if (!verses.TryGetValue(verse, out var list))
                {
                    verses[verse] = list = [];
                }

                list.Add((reader.GetInt64(4), reader.IsDBNull(5) ? null : reader.GetDouble(5)));
            }
        }

        var reading = new Dictionary<(int Reference, int Book, int Chapter), bool>();
        await using (var command = new NpgsqlCommand(ReadingSql, connection) { CommandTimeout = 0 })
        {
            command.Parameters.AddWithValue("text", textId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                reading[(reader.GetInt32(0), reader.GetInt32(1), reader.GetInt32(2))] = reader.GetBoolean(3);
            }
        }

        return
        [
            .. links.Keys.Concat(reading.Where(r => r.Value).Select(r => r.Key)).Distinct()
                .Order()
                .Select(key => new ChapterStrengthCount(key.Reference, key.Book, key.Chapter, reading.GetValueOrDefault(key),
                    links.TryGetValue(key, out var verses)
                        ? new SortedDictionary<int, VerseStrengthCount>(verses.ToDictionary(v => v.Key, v => Of([.. v.Value.OrderBy(l => l.Id).Select(l => l.Confidence)])))
                        : [])),
        ];
    }

    /// <summary>
    /// A verse's strength from its links' confidences: <c>Average</c> over nullable doubles skips the stated links' nulls and
    /// is null when they are all there is, since nothing inferred the pair.
    /// </summary>
    public static VerseStrengthCount Of(IReadOnlyList<double?> confidences) =>
        new(confidences.Count, confidences.Count(c => c is null), confidences.Average());

    /// <summary>
    /// A chapter's verses as the product and the table write them: <c>verse:links:stated:confidence</c> per verse in verse
    /// order, separated by spaces, the confidence as the shortest text that reads back as the same double and empty where
    /// there is none.
    /// </summary>
    public static string Pack(IEnumerable<KeyValuePair<int, VerseStrengthCount>> verses)
    {
        var text = new StringBuilder();
        foreach (var (verse, strength) in verses)
        {
            if (text.Length > 0)
            {
                text.Append(' ');
            }

            text.Append(CultureInfo.InvariantCulture, $"{verse}:{strength.Links}:{strength.Stated}:");
            if (strength.Confidence is { } confidence)
            {
                text.Append(confidence.ToString("R", CultureInfo.InvariantCulture));
            }
        }

        return text.ToString();
    }

    /// <summary>The verses <see cref="Pack"/> wrote.</summary>
    public static Dictionary<int, VerseStrengthCount> Unpack(string packed)
    {
        var verses = new Dictionary<int, VerseStrengthCount>();
        foreach (var entry in packed.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = entry.Split(':');
            if (parts.Length != 4)
            {
                throw new FormatException($"'{entry}' is not a verse's strength (verse:links:stated:confidence).");
            }

            verses[int.Parse(parts[0], CultureInfo.InvariantCulture)] = new VerseStrengthCount(
                int.Parse(parts[1], CultureInfo.InvariantCulture),
                int.Parse(parts[2], CultureInfo.InvariantCulture),
                parts[3].Length == 0 ? null : double.Parse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture));
        }

        return verses;
    }
}
