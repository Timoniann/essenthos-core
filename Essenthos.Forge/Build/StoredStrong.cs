using Npgsql;

namespace Essenthos.Core.Build;

/// <summary>
/// What the Strong pages read per request that only the links, the words and the frame decide, written by the build so a
/// request reads a few rows: <c>text_pair_links</c> (how many links join each text to each edition of the original, which
/// <see cref="Corpus.LinkedOriginals.Of"/> chooses the counted edition by) and <c>strong_occurrence</c> (every numbered word
/// in list order, which the occurrences and verse lists page through) and <c>word_rendered_strong</c> (the numbers a list of
/// verses marks on each word). Each is written for all texts, or for the texts named, inside the caller's transaction.
/// </summary>
internal static class StoredStrong
{
    /// <summary>
    /// Every pair of a text and an edition that is not a translation that some link joins, either way, with the links
    /// counted both ways on the index that leads with both ends of a link. A pair touching a named text is written again.
    /// </summary>
    private const string PairsGone = "DELETE FROM text_pair_links WHERE @all OR text_id = ANY(@texts) OR edition_id = ANY(@texts)";

    private const string PairLinks = """
        INSERT INTO text_pair_links (text_id, edition_id, links, numbered)
        SELECT t.id, e.id, c.links,
               EXISTS (SELECT 1 FROM word w WHERE w.text_id = e.id AND w.strong_number IS NOT NULL)
        FROM text t
        JOIN text e ON e.id <> t.id AND e.kind <> 'translation'
        CROSS JOIN LATERAL (
            SELECT (SELECT count(*) FROM link l WHERE l.from_text_id = t.id AND l.to_text_id = e.id)
                 + (SELECT count(*) FROM link l WHERE l.from_text_id = e.id AND l.to_text_id = t.id) AS links
        ) c
        WHERE (@all OR t.id = ANY(@texts) OR e.id = ANY(@texts)) AND c.links > 0;
        """;

    /// <summary>Every word carrying a number, at the frame address of its verse's primary reference.</summary>
    private const string OccurrencesGone = "DELETE FROM strong_occurrence WHERE @all OR text_id = ANY(@texts)";

    private const string Occurrences = """
        INSERT INTO strong_occurrence (strong_number, corpus, book, chapter, verse, position, word_id, text_id)
        SELECT w.strong_number, t.slug, r.canonical_book, r.canonical_chapter, r.canonical_verse, w.position, w.id, w.text_id
        FROM word w
        JOIN text t ON t.id = w.text_id
        JOIN verse_reference r ON r.verse_id = w.verse_id AND r.is_primary
        WHERE w.strong_number IS NOT NULL AND (@all OR w.text_id = ANY(@texts));
        """;

    /// <summary>
    /// The Strong numbers a list of verses marks on a text's words (TSK-1822): a word's own number; and the number of every word
    /// of an edition of an original (a text that is not a translation) a renders or equals link joins it to, on every word of a
    /// translation and on a word of an original only where it carries no number of its own, since a word of an original
    /// carries the number its edition gives it and a variant's number on a linked edition is not its own. Every linked edition
    /// counts, not only the one a Strong page counts over: a word rendering the Textus Receptus renders its number whichever
    /// critical edition the text is counted over. A translation's own tags on another translation mark nothing here. Rows go
    /// in verse order, so a verse's lie together.
    /// </summary>
    private const string RenderedGone = "DELETE FROM word_rendered_strong r USING verse v WHERE v.id = r.verse_id AND (@all OR v.text_id = ANY(@texts))";

    private const string Rendered = """
        INSERT INTO word_rendered_strong (verse_id, word_id, strong_number)
        SELECT DISTINCT x.verse_id, x.word_id, x.strong_number
        FROM (
            SELECT w.verse_id, w.id AS word_id, w.strong_number
            FROM word w
            WHERE w.strong_number IS NOT NULL AND (@all OR w.text_id = ANY(@texts))
            UNION ALL
            SELECT s.verse_id, s.word_id, ow.strong_number
            FROM link_word s
            JOIN word sw ON sw.id = s.word_id
            JOIN text st ON st.id = sw.text_id
            JOIN link l ON l.id = s.link_id AND l.relation IN ('renders', 'equals')
            JOIN link_word o ON o.link_id = l.id AND o.side <> s.side
            JOIN word ow ON ow.id = o.word_id AND ow.strong_number IS NOT NULL
            JOIN text ot ON ot.id = ow.text_id AND ot.kind <> 'translation'
            WHERE (st.kind = 'translation' OR sw.strong_number IS NULL) AND (@all OR sw.text_id = ANY(@texts))
        ) x
        ORDER BY x.verse_id, x.word_id, x.strong_number;
        """;

    /// <summary>The pairs' links of every text, or of the texts named; the rows written.</summary>
    public static Task<int> WritePairLinks(NpgsqlConnection connection, NpgsqlTransaction? transaction, int[]? texts, CancellationToken cancellationToken) =>
        Run(PairsGone, PairLinks, connection, transaction, texts, cancellationToken);

    /// <summary>The numbered words of every text, or of the texts named; the rows written.</summary>
    public static Task<int> WriteOccurrences(NpgsqlConnection connection, NpgsqlTransaction? transaction, int[]? texts, CancellationToken cancellationToken) =>
        Run(OccurrencesGone, Occurrences, connection, transaction, texts, cancellationToken);

    /// <summary>The numbers marked on the words of every text, or of the texts named; the rows written.</summary>
    public static Task<int> WriteRendered(NpgsqlConnection connection, NpgsqlTransaction? transaction, int[]? texts, CancellationToken cancellationToken) =>
        Run(RenderedGone, Rendered, connection, transaction, texts, cancellationToken);

    /// <summary>All three, on a connection of their own in one transaction.</summary>
    public static async Task<long> Write(string connectionString, CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        long rows = await WritePairLinks(connection, transaction, null, cancellationToken);
        rows += await WriteOccurrences(connection, transaction, null, cancellationToken);
        rows += await WriteRendered(connection, transaction, null, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return rows;
    }

    private static async Task<int> Run(
        string delete, string insert, NpgsqlConnection connection, NpgsqlTransaction? transaction, int[]? texts, CancellationToken cancellationToken)
    {
        var rows = 0;
        foreach (var sql in new[] { delete, insert })
        {
            await using var command = new NpgsqlCommand(sql, connection, transaction) { CommandTimeout = 0 };
            command.Parameters.AddWithValue("all", texts is null);
            command.Parameters.AddWithValue("texts", texts ?? []);
            rows = await command.ExecuteNonQueryAsync(cancellationToken);
        }

        return rows;
    }
}
