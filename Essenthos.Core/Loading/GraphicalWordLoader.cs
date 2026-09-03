using System.Diagnostics;
using Essenthos.Core.Database;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Essenthos.Core.Loading;

internal sealed record GraphicalOutcome(int Words, int Runs, TimeSpan Elapsed)
{
    public override string ToString() =>
        Words == 0
            ? "every printed word is already one row"
            : $"{Words} words joined into {Runs} printed words in {Elapsed}";
}

/// <summary>
/// Gives every word that is only part of a printed word the folded form of the whole of it.
///
/// A row here is a morpheme. Hebrew prints several of them as one word — BHSA stores בְּרֵאשִׁית
/// as בְּ and רֵאשִׁית — and the corpus already records which: a word whose trailer is empty has
/// nothing between it and the next on the page. So the run is read off the text rather than
/// derived from morphology, and nothing is guessed at.
///
/// <para>
/// Without this, a search for what the reader sees printed answers nothing. Typing בראשית found
/// 0 verses and ראשית found 43; הארץ, the commonest noun phrase in the Hebrew Bible, found 0 and
/// only the bare ארץ worked. The snippet renders the run as one visual unit, so a reader could
/// copy a word out of a result and get nothing back for it.
/// </para>
///
/// <para>
/// Every member of a run gets the same value and a word printed on its own gets null, so the
/// column is sparse — 121,808 rows of 4.5 million — and a term matching it selects the whole
/// printed word, which is what lets the snippet mark all of it rather than one morpheme in the
/// middle.
/// </para>
///
/// <para>
/// Idempotent the way the folding pass is, but on a different question: <c>graphical_text</c>
/// being null is the ordinary case rather than the unfinished one, so this asks whether any word
/// of the text is joined to the next and has no run recorded. One indexed pass on a loaded corpus.
/// </para>
/// </summary>
internal sealed class GraphicalWordLoader(AppDbContext db, ILogger<GraphicalWordLoader> logger)
{
    /// <summary>
    /// A run is the words from one whose predecessor ended the previous run, up to and including
    /// the first with a non-empty trailer. The last word of a verse ends its run whatever its
    /// trailer says — every Greek verse ends with an empty one, and reading that as "joined to the
    /// next" would join the last word of a verse to the first word of the next.
    /// </summary>
    private const string Fill =
        """
        WITH ordered AS (
            SELECT w.id, w.verse_id, w.position, w.normalised_text,
                   lag(w.trailer) OVER (PARTITION BY w.verse_id ORDER BY w.position) AS before
            FROM word w
            WHERE w.text_id = @text
        ),
        marked AS (
            SELECT id, verse_id, position, normalised_text,
                   count(*) FILTER (WHERE before IS NULL OR before <> '')
                       OVER (PARTITION BY verse_id ORDER BY position
                             ROWS BETWEEN UNBOUNDED PRECEDING AND CURRENT ROW) AS run
            FROM ordered
        ),
        runs AS (
            SELECT verse_id, run,
                   count(*) AS size,
                   string_agg(coalesce(normalised_text, ''), '' ORDER BY position) AS folded
            FROM marked GROUP BY 1, 2
        )
        UPDATE word w
        SET graphical_text = r.folded
        FROM marked m
        JOIN runs r ON r.verse_id = m.verse_id AND r.run = m.run
        WHERE w.id = m.id AND r.size > 1 AND w.graphical_text IS DISTINCT FROM r.folded
        """;

    /// <summary>
    /// Whether this text has a word joined to the next that has no run recorded. Cheaper than
    /// counting, and it is the question — a text of one-row words has nothing to do here and must
    /// not be rewritten every start-up.
    /// </summary>
    private const string Pending =
        """
        SELECT EXISTS (
            SELECT 1 FROM word w
            WHERE w.text_id = @text AND w.graphical_text IS NULL AND w.trailer = ''
              AND EXISTS (SELECT 1 FROM word n
                          WHERE n.verse_id = w.verse_id AND n.position = w.position + 1))
        """;

    public async Task<GraphicalOutcome> Load(CancellationToken cancellationToken = default)
    {
        var started = Stopwatch.StartNew();
        var texts = await db.Texts.Select(t => new { t.Id, t.Slug }).ToListAsync(cancellationToken);

        await db.Database.OpenConnectionAsync(cancellationToken);
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();

        var words = 0;
        var runs = 0;

        foreach (var text in texts)
        {
            await using (var ask = new NpgsqlCommand(Pending, connection))
            {
                ask.Parameters.AddWithValue("text", text.Id);
                if (await ask.ExecuteScalarAsync(cancellationToken) is not true)
                {
                    continue;
                }
            }

            await using var command = new NpgsqlCommand(Fill, connection);
            command.Parameters.AddWithValue("text", text.Id);
            command.CommandTimeout = 1800;
            var written = await command.ExecuteNonQueryAsync(cancellationToken);
            if (written == 0)
            {
                continue;
            }

            words += written;
            runs += await Runs(connection, text.Id, cancellationToken);
            logger.LogInformation(
                "{Text}: {Words} words are part of a longer printed word", text.Slug, written);
        }

        var outcome = new GraphicalOutcome(words, runs, started.Elapsed);
        logger.LogInformation("Printed words: {Outcome}", outcome);
        return outcome;
    }

    private static async Task<int> Runs(
        NpgsqlConnection connection, int textId, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            "SELECT count(DISTINCT (verse_id, graphical_text)) FROM word " +
            "WHERE text_id = @text AND graphical_text IS NOT NULL", connection);
        command.Parameters.AddWithValue("text", textId);
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken));
    }
}
