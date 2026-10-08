using System.Diagnostics;
using Essenthos.Core.Database;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Essenthos.Core.Loading.Encyclopedia;

/// <param name="Kept">Pronouns that keep the person a link carried onto them, because the verse settles who it is.</param>
/// <param name="Withdrawn">Pronouns whose person was taken back, because the verse does not settle it.</param>
/// <param name="ByText">How many were taken back in each text.</param>
internal sealed record PronounReferentOutcome(
    int Kept,
    int Withdrawn,
    IReadOnlyList<(string Text, int Words)> ByText,
    TimeSpan Elapsed)
{
    public override string ToString() =>
        Withdrawn == 0
            ? $"every pronoun that carries a person names one the verse settles ({Kept} of them)"
            : $"{Withdrawn} pronouns carried a person the verse does not settle and lost it, {Kept} keep theirs, " +
              $"in {Elapsed}. Per text: " + string.Join(", ", ByText.Select(t => $"{t.Text} {t.Words}"));
}

/// <summary>
/// A person carried along a link onto a pronoun: the Berean renders מֹשֶׁה as <em>him</em>, and the
/// word <em>him</em> then names Moses. The pronoun does refer to him; the owner's ruling of
/// 2026-10-07 is to keep it where it is certainly clear who the pronoun is, and to take it away where
/// it is disputed or unclear.
///
/// <para>
/// <strong>Certainly clear</strong>, as a rule a reader can check:
/// </para>
/// <list type="bullet">
/// <item>the pronoun says a sex — <em>he</em>, <em>she</em>, <em>он</em>, <em>вона</em>, <em>er</em>,
/// <em>ella</em> — and the person has it, or his sex is not recorded;</item>
/// <item>the same text names him, by a word that is not a pronoun, in the same verse or the verse
/// before;</item>
/// <item>no one else that pronoun could be — another person or title of that sex, or of none
/// recorded — is named there;</item>
/// <item>the word beside the pronoun does not itself name him: <em>her mother-in-law</em> is Naomi,
/// and its <em>her</em> is Ruth.</item>
/// </list>
/// <para>
/// Or the text capitalises the pronoun where a sentence does not begin, which is how the Berean, the
/// Synodal and the 1917 JPS write God and Christ, and the person is YHVH, Jesus or the Holy Spirit.
/// A pronoun that says no sex — <em>thee</em>, <em>them</em>, <em>my</em>, <em>свой</em> — is clear
/// only by that capital: read against its verse it was the addressee, the speaker or a group as often
/// as the person.
/// </para>
/// <para>
/// A ruling and a row with an answer read of its own word are never taken back. Idempotent: the rule
/// reads only the words that are not pronouns, which it does not touch, so a second run takes nothing.
/// </para>
/// </summary>
internal sealed class PronounReferents(AppDbContext db, ILogger<PronounReferents> logger)
{
    /// <summary>Who a capitalised pronoun in the middle of a sentence can be.</summary>
    private const string Capitalised = "'yhvh', 'jesus', 'holy-spirit'";

    /// <summary>
    /// The rows carried onto pronouns that are not clear, as temp tables the caller reads.
    /// </summary>
    /// <param name="candidates">
    /// A query of (row_id, word_id, entity_id): the carried rows to judge. Their ids are what the caller
    /// takes back by.
    /// </param>
    /// <param name="named">
    /// A relation of (word_id, entity_id) holding the names the verses carry, which says who is named
    /// near a pronoun.
    /// </param>
    private static string Judge(string candidates, string named) =>
        $"""
         DROP TABLE IF EXISTS pronoun_judged, pronoun_named, pronoun_carried;
         CREATE TEMP TABLE pronoun_carried ON COMMIT DROP AS
         SELECT a.row_id, a.entity_id, w.id AS word_id, w.text AS surface, w.position, w.verse_id, w.text_id,
                t.slug AS text_slug, t.language, v.book_id, v.chapter_number, v.number,
                e.slug AS person, e.sex, pronoun.sex AS says,
                before.trailer AS before
         FROM ({candidates}) a
         JOIN entity e ON e.id = a.entity_id AND e.kind IN ('person', 'title')
         JOIN word w ON w.id = a.word_id
         JOIN text t ON t.id = w.text_id
         JOIN verse v ON v.id = w.verse_id
         JOIN {Pronouns.Rows()} ON pronoun.language = t.language
              AND pronoun.word = lower(regexp_replace(w.text, '[[:punct:]]', '', 'g'))
         LEFT JOIN word before ON before.verse_id = w.verse_id AND before.position = w.position - 1;

         CREATE TEMP TABLE pronoun_named ON COMMIT DROP AS
         SELECT DISTINCT p.row_id, a.entity_id, e.sex
         FROM pronoun_carried p
         JOIN verse around ON around.text_id = p.text_id AND around.book_id = p.book_id
              AND around.chapter_number = p.chapter_number AND around.number IN (p.number, p.number - 1)
         JOIN word w ON w.verse_id = around.id
         JOIN {named} a ON a.word_id = w.id
         JOIN entity e ON e.id = a.entity_id AND e.kind IN ('person', 'title')
         WHERE NOT EXISTS (SELECT 1 FROM {Pronouns.Rows()}
                           WHERE pronoun.language = p.language
                             AND pronoun.word = lower(regexp_replace(w.text, '[[:punct:]]', '', 'g')));

         CREATE TEMP TABLE pronoun_judged ON COMMIT DROP AS
         SELECT p.row_id, p.text_slug,
                (p.person IN ({Capitalised})
                    AND p.surface ~ '^[[:upper:]]'
                    AND NOT (p.language = 'eng' AND p.surface = 'I')
                    AND p.position > 1
                    AND coalesce(p.before, '') !~ '[.!?:;"“”‘’«»]')
                OR (p.says IS NOT NULL
                    AND (p.sex IS NULL OR p.sex = p.says)
                    AND EXISTS (SELECT 1 FROM pronoun_named n WHERE n.row_id = p.row_id AND n.entity_id = p.entity_id)
                    AND NOT EXISTS (SELECT 1 FROM pronoun_named n
                                    WHERE n.row_id = p.row_id AND n.entity_id <> p.entity_id
                                      AND (n.sex IS NULL OR n.sex = p.says))
                    AND NOT EXISTS (SELECT 1 FROM word beside
                                    JOIN {named} named ON named.word_id = beside.id
                                    WHERE beside.verse_id = p.verse_id
                                      AND beside.position IN (p.position - 1, p.position + 1)
                                      AND named.entity_id = p.entity_id)) AS clear
         FROM pronoun_carried p
         """;

    /// <summary>The carried rows a pronoun's person may be taken back from: the ones nobody ruled on.</summary>
    private const string Written =
        """
        SELECT a.id AS row_id, a.word_id, a.entity_id
        FROM word_entity a
        WHERE a.confidence IS NOT NULL
          AND NOT EXISTS (SELECT 1 FROM word_entity_claim c
                          WHERE c.word_entity_id = a.id AND c.method <> 'stated-by-source'
                            AND coalesce(c.note, '') NOT LIKE @carried)
        """;

    /// <summary>
    /// The rows a carry is about to write, which no row holds yet. A word has one expected row, so the
    /// word stands for the row.
    /// </summary>
    private const string NotYetWritten =
        """
        SELECT x.word_id AS row_id, x.word_id, x.entity_id
        FROM expected x
        WHERE x.confidence IS NOT NULL
          AND NOT EXISTS (SELECT 1 FROM word_entity a WHERE a.word_id = x.word_id AND a.entity_id = x.entity_id)
        """;

    /// <summary>The names the corpus holds and the carry is about to write.</summary>
    private const string NamesWithTheExpected =
        "(SELECT word_id, entity_id FROM word_entity UNION ALL SELECT word_id, entity_id FROM expected)";

    private static readonly string JudgeWritten = Judge(Written, "word_entity");

    private static readonly string JudgeNew = Judge(NotYetWritten, NamesWithTheExpected);

    /// <summary>
    /// Leaves out of a carry's expected rows the ones that would be written only to be taken back at
    /// once: a person the verse does not settle on a pronoun. Written and withdrawn on every load, such a
    /// row burns an id each time and leaves nothing behind. The rows already in the corpus are not
    /// touched here; <see cref="Withdraw"/> takes them back.
    /// </summary>
    /// <returns>How many expected rows were left out.</returns>
    public async Task<int> LeaveOut(NpgsqlConnection connection, NpgsqlTransaction transaction, CancellationToken cancellationToken)
    {
        await using (var judge = new NpgsqlCommand(JudgeNew, connection, transaction))
        {
            await judge.ExecuteNonQueryAsync(cancellationToken);
        }

        await using var delete = new NpgsqlCommand(
            "DELETE FROM expected x USING pronoun_judged j WHERE x.word_id = j.row_id AND NOT j.clear",
            connection, transaction);
        var left = await delete.ExecuteNonQueryAsync(cancellationToken);
        if (left > 0)
        {
            logger.LogInformation("{Left} carried names on pronouns the verse does not settle were not written", left);
        }

        return left;
    }

    /// <param name="write">False to count what would be taken back and take back nothing.</param>
    public async Task<PronounReferentOutcome> Withdraw(CancellationToken cancellationToken = default, bool write = true)
    {
        var started = Stopwatch.StartNew();
        await db.Database.OpenConnectionAsync(cancellationToken);
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        await using (var judge = new NpgsqlCommand(JudgeWritten, connection, transaction))
        {
            judge.Parameters.AddWithValue("carried", Annotating.CarriedNote);
            await judge.ExecuteNonQueryAsync(cancellationToken);
        }

        var byText = new List<(string, int)>();
        var kept = 0;
        await using (var count = new NpgsqlCommand(
                         "SELECT text_slug, count(*) FILTER (WHERE NOT clear), count(*) FILTER (WHERE clear) " +
                         "FROM pronoun_judged GROUP BY 1 ORDER BY 2 DESC, 1", connection, transaction))
        await using (var reader = await count.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                if (reader.GetInt64(1) > 0)
                {
                    byText.Add((reader.GetString(0), (int)reader.GetInt64(1)));
                }

                kept += (int)reader.GetInt64(2);
            }
        }

        int withdrawn;
        await using (var delete = new NpgsqlCommand(
                         "DELETE FROM word_entity a USING pronoun_judged j WHERE a.id = j.row_id AND NOT j.clear",
                         connection, transaction))
        {
            withdrawn = await delete.ExecuteNonQueryAsync(cancellationToken);
        }

        if (write)
        {
            await transaction.CommitAsync(cancellationToken);
        }
        else
        {
            await transaction.RollbackAsync(cancellationToken);
        }

        var outcome = new PronounReferentOutcome(kept, withdrawn, byText, started.Elapsed);
        logger.LogInformation("{Outcome}", outcome);
        return outcome;
    }
}
