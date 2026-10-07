using System.Diagnostics;
using Essenthos.Core.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace Essenthos.Core.Loading.Encyclopedia;

/// <param name="Written">Words given the name their twin in another edition carries.</param>
/// <param name="Withdrawn">Such names taken back because the twin no longer carries them.</param>
/// <param name="ByText">Words this pass names, per text, once it has run.</param>
internal sealed record EqualTwinNameOutcome(
    int Written,
    int Withdrawn,
    IReadOnlyList<(string Text, int Words)> ByText,
    TimeSpan Elapsed)
{
    public override string ToString() =>
        Written + Withdrawn == 0
            ? "every word an edition prints the same as another already carries the name its twin does"
            : $"{Written} words given the name their twin in another edition carries and {Withdrawn} taken back, " +
              $"in {Elapsed}. Per text: " + string.Join(", ", ByText.Select(t => $"{t.Text} {t.Words}"));
}

/// <summary>
/// The name a word carries, given to the same word in another edition of the same text.
///
/// <para>
/// An <c>equals</c> link says two editions print one word: Swete's Septuagint and Brenton's Greek,
/// the Samaritan Pentateuch and BHSA, Scrivener and Nestle. The passes that name words carry what
/// they read one link from the word they read it on, so an edition linked to nothing but its twin
/// never hears of them: Swete named almost nobody at the word while half a million links said its
/// words are Brenton's.
/// </para>
///
/// <para>
/// <strong>Only to a word nothing else names, and only one answer.</strong> A twin that any pass has
/// already named keeps that answer, and a word its language never names anybody with
/// (<see cref="Annotating.NeverAName"/>) gets none. Only an answer at least as sure as
/// <see cref="Annotating.Faint"/> is given: a faint one is the twin's last resort, not the word's. A word whose own annotations name two records gives nothing, nor
/// does a word twinned with several that disagree. What is given is carried, in the note's own form
/// (<see cref="Annotating.CarriedNote"/>), so a reading of the word itself still comes first, and its
/// confidence is the twin's crossed with the link. Never from a name this pass gave, so it does not
/// chain from edition to edition.
/// </para>
///
/// <para>
/// After every pass that names a word, and idempotent: what it gave and would not give today is taken
/// back, what it would give and has not is written, and a second run changes nothing.
/// </para>
/// </summary>
internal sealed class EqualTwinNames(AppDbContext db, ILogger<EqualTwinNames> logger)
{
    public const string Source = "Essenthos, the name the same word carries in another edition of the text";

    private static readonly string Wanted =
        $"""
        CREATE TEMP TABLE twin_name ON COMMIT DROP AS
        WITH pair AS (
            SELECT origin.word_id AS origin, twin.word_id AS twin, l.method, coalesce(l.confidence, 1.0) AS worth
            FROM link l
            JOIN link_word origin ON origin.link_id = l.id
            JOIN link_word twin ON twin.link_id = l.id AND twin.side <> origin.side
            WHERE l.relation = 'equals'
              AND NOT EXISTS (SELECT 1 FROM link_word other
                              WHERE other.link_id = l.id AND other.word_id NOT IN (origin.word_id, twin.word_id))
        ),
        named AS (
            SELECT x.word_id, min(x.entity_id) AS entity_id,
                   (array_agg(x.method ORDER BY coalesce(x.confidence, 1.0) DESC, x.id))[1] AS method,
                   max(coalesce(x.confidence, 1.0)) AS confidence
            FROM word_entity x
            WHERE x.source <> @source
              AND coalesce(x.confidence, 1.0) >= {Annotating.Faint.ToString(System.Globalization.CultureInfo.InvariantCulture)}
              AND x.word_id IN (SELECT origin FROM pair)
            GROUP BY x.word_id
            HAVING count(DISTINCT x.entity_id) = 1
        ),
        offered AS (
            SELECT p.twin AS word_id, n.entity_id, n.method, n.confidence * p.worth AS confidence,
                   'through ' || lower(ot.slug) || ' word ' || p.origin || ', linked by ' || p.method AS note
            FROM pair p
            JOIN named n ON n.word_id = p.origin
            JOIN word ow ON ow.id = p.origin
            JOIN text ot ON ot.id = ow.text_id
            JOIN word hw ON hw.id = p.twin
            JOIN text ht ON ht.id = hw.text_id
            WHERE NOT EXISTS (SELECT 1 FROM word_entity spoken
                              WHERE spoken.word_id = p.twin AND spoken.source <> @source)
              AND NOT {Annotating.NeverAName}
        )
        SELECT DISTINCT ON (o.word_id) o.word_id, o.entity_id, o.method, o.confidence, o.note
        FROM offered o
        WHERE NOT EXISTS (SELECT 1 FROM offered other
                          WHERE other.word_id = o.word_id AND other.entity_id <> o.entity_id)
        ORDER BY o.word_id, o.confidence DESC, o.note
        """;

    private const string TakeBack =
        """
        DELETE FROM word_entity a
        WHERE a.source = @source
          AND NOT EXISTS (SELECT 1 FROM twin_name t
                          WHERE t.word_id = a.word_id AND t.entity_id = a.entity_id
                            AND t.confidence = a.confidence AND t.note = a.note)
        """;

    private const string Give =
        """
        INSERT INTO word_entity (word_id, entity_id, method, confidence, source, note)
        SELECT t.word_id, t.entity_id, t.method, t.confidence, @source, t.note
        FROM twin_name t
        WHERE NOT EXISTS (SELECT 1 FROM word_entity a WHERE a.word_id = t.word_id AND a.entity_id = t.entity_id)
        """;

    private const string Claim =
        """
        INSERT INTO word_entity_claim (word_entity_id, method, confidence, source, note)
        SELECT a.id, t.method, t.confidence, @source, t.note
        FROM word_entity a
        JOIN twin_name t ON t.word_id = a.word_id AND t.entity_id = a.entity_id
        WHERE a.source = @source
        ON CONFLICT DO NOTHING
        """;

    public async Task<EqualTwinNameOutcome> Load(CancellationToken cancellationToken = default)
    {
        var started = Stopwatch.StartNew();
        await db.Database.OpenConnectionAsync(cancellationToken);
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await Run(connection, transaction, Wanted, cancellationToken);
        var withdrawn = await Run(connection, transaction, TakeBack, cancellationToken);
        var written = await Run(connection, transaction, Give, cancellationToken);
        await Run(connection, transaction, Claim, cancellationToken);
        var byText = written + withdrawn == 0
            ? []
            : await Annotating.ByText(connection, transaction, Source, cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        var outcome = new EqualTwinNameOutcome(written, withdrawn, byText, started.Elapsed);
        logger.LogInformation("Named the words other editions print the same: {Outcome}", outcome);
        return outcome;
    }

    private static async Task<int> Run(
        NpgsqlConnection connection,
        IDbContextTransaction transaction,
        string sql,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(sql, connection, (NpgsqlTransaction)transaction.GetDbTransaction());
        command.Parameters.AddWithValue("source", Source);
        return await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
