using System.Diagnostics;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace Essenthos.Core.Loading.Encyclopedia;

/// <param name="Occurrences">Hebrew words standing in the construct after a word for a tribe.</param>
/// <param name="Settled">Of those, the ones whose name exactly one ancestor of a people bears.</param>
/// <param name="Unheld">Words whose name no single ancestor bears, which this pass cannot answer.</param>
/// <param name="Spoken">Words something had already named, which the rule now stands beside.</param>
/// <param name="Withdrawn">Annotations written when the rule read the construct as the people, taken back.</param>
/// <param name="Written">Annotations written under this pass's source, the carried ones included.</param>
internal sealed record TribeNameOutcome(
    bool AlreadyLoaded,
    int Occurrences,
    int Settled,
    int Unheld,
    int Spoken,
    int Withdrawn,
    int Written,
    IReadOnlyList<(string Text, int Words)> ByText,
    TimeSpan Elapsed)
{
    public override string ToString() =>
        AlreadyLoaded
            ? "the names standing after a word for a tribe are already the ancestors"
            : $"{Settled} of {Occurrences} Hebrew words standing after a word for a tribe are the ancestor " +
              $"the tribe is named after, in {Elapsed}; {Unheld} name no ancestor the encyclopedia holds and " +
              $"{Spoken} were already named. {Withdrawn} annotations naming the people taken back, " +
              $"{Written} words written in all. Per text: " +
              string.Join(", ", ByText.Select(t => $"{t.Text} {t.Words}"));
}

/// <summary>
/// <em>The tribe of Naphtali</em>, where the name after the word for a tribe is the man the tribe is
/// named after.
///
/// <para>
/// 1 Kings 7:14 says the widow was <em>of the tribe of Naphtali</em>, and the word נַפְתָּלִי there
/// named nobody in any text. Three records bear its number — the man, the tribe and the territory —
/// so the resolution by number refuses it, and BHSA marks the lexeme all three kinds at once, so the
/// marking settles nothing either. Every name in this construct is in that position, which is why
/// eighty-five of them were blank.
/// </para>
///
/// <para>
/// <strong>The construct is what settles it.</strong> מַטֶּה and שֵׁבֶט are the two words the Hebrew
/// says <em>tribe</em> with, and a name immediately after either of them is the ancestor the tribe is
/// named after — the project owner's reading of Revelation 7 on 2026-10-04, which he held for the
/// Hebrew as for the Greek on 2026-10-07: the phrase already says <em>tribe</em>, so the name in it is
/// the man. The ancestor is the one record a people bearing the name descends from and who bears it
/// himself, as <see cref="GreekTribeNameLoader"/> finds him.
/// </para>
///
/// <para>
/// It is written whatever already names the word and settles by standing: a rule about the phrase
/// outranks a resolution by number or by the form of the word, and a reading of the verse or a
/// person's ruling outranks it. What an earlier reading of the construct as the people wrote is
/// taken back. Idempotent on its own source, and it runs after every pass that names a Hebrew word.
/// </para>
/// </summary>
internal sealed class TribeNameLoader(AppDbContext db, ILogger<TribeNameLoader> logger)
{
    public const string Source =
        "Essenthos, on the project owner's reading of 2026-10-04 that a name standing after the word " +
        "for a tribe is the ancestor the tribe is named after, read in the Hebrew";

    /// <summary>What the rule wrote while it read the name after the word for a tribe as the people.</summary>
    internal const string PeopleSource =
        "Essenthos, reading a name standing after the Hebrew word for a tribe as that tribe, where " +
        "exactly one people bears the name";

    /// <summary>
    /// The two words the Hebrew calls a tribe by — <em>maṭṭeh</em> and <em>šēḇeṭ</em>, both of them
    /// a staff before they are a tribe — standing in the construct before the name.
    /// </summary>
    private static readonly string[] ForTribe = ["H4294", "H7626"];

    private const double ByTheConstruct = 0.9;

    private static readonly string Occurrences =
        $"""
         CREATE TEMP TABLE occurrence (
             word_id bigint PRIMARY KEY,
             number text NOT NULL,
             entity_id integer,
             spoken boolean NOT NULL)
         ON COMMIT DROP;
         INSERT INTO occurrence (word_id, number, entity_id, spoken)
         SELECT w.id, w.strong_number, {GreekTribeNameLoader.Ancestor("w.strong_number")},
                EXISTS (SELECT 1 FROM word_entity a WHERE a.word_id = w.id)
         FROM word w
         JOIN text t ON t.id = w.text_id AND t.slug = @witness
         JOIN word tribe ON tribe.verse_id = w.verse_id AND tribe.position = w.position - 1
              AND tribe.strong_number = ANY(@tribe)
         WHERE w.strong_number IS NOT NULL
         """;

    private const string Seed =
        """
        INSERT INTO pending_annotation (word_id, entity_id, confidence, corroborated, note)
        SELECT o.word_id, o.entity_id, @confidence, FALSE,
               o.number || ', standing after the word for a tribe, so the ancestor the tribe is named after'
        FROM occurrence o
        WHERE o.entity_id IS NOT NULL
        """;

    private const string Tally =
        """
        SELECT count(*), count(*) FILTER (WHERE entity_id IS NOT NULL),
               count(*) FILTER (WHERE entity_id IS NULL), count(*) FILTER (WHERE spoken)
        FROM occurrence
        """;

    /// <summary>
    /// The people the construct was read as, taken back: the rows it concluded that nothing else
    /// claims, then its claims on rows something else also claims, which stand on those
    /// (<see cref="StandOnTheRest"/>).
    /// </summary>
    private const string WithdrawThePeople =
        """
        DELETE FROM word_entity a
        WHERE a.source = @former
          AND NOT EXISTS (SELECT 1 FROM word_entity_claim c WHERE c.word_entity_id = a.id AND c.source <> @former)
        """;

    /// <summary>
    /// A row the people was concluded on that another pass also claims stands on that claim now: it
    /// takes the claim's source, method, confidence and note, so the carry and every later pass read
    /// it as that pass's row and not as one of a source that has been taken back.
    /// </summary>
    private const string StandOnTheRest =
        """
        UPDATE word_entity a
        SET source = rest.source, method = rest.method, confidence = rest.confidence, note = rest.note
        FROM (SELECT DISTINCT ON (c.word_entity_id) c.word_entity_id, c.source, c.method, c.confidence, c.note
              FROM word_entity_claim c
              JOIN word_entity owner ON owner.id = c.word_entity_id AND owner.source = @former
              WHERE c.source <> @former
              ORDER BY c.word_entity_id, c.method = 'stated-by-source', coalesce(c.confidence, 1.0) DESC, c.id) rest
        WHERE a.id = rest.word_entity_id
        """;

    private const string WithdrawItsClaims = "DELETE FROM word_entity_claim c WHERE c.source = @former";

    public async Task<TribeNameOutcome> Load(CancellationToken cancellationToken = default)
    {
        if (await db.WordEntities.AnyAsync(a => a.Source == Source, cancellationToken)
            && !await db.WordEntityClaims.AnyAsync(c => c.Source == PeopleSource, cancellationToken))
        {
            logger.LogInformation(
                "The names standing after a word for a tribe are already the ancestors; nothing to do");
            return new TribeNameOutcome(true, 0, 0, 0, 0, 0, 0, [], TimeSpan.Zero);
        }

        var started = Stopwatch.StartNew();
        await db.Database.OpenConnectionAsync(cancellationToken);
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var withdrawn = await Withdraw(connection, transaction, cancellationToken);
        var written = await db.WordEntities.AnyAsync(a => a.Source == Source, cancellationToken);
        await Annotating.Run(connection, transaction, Occurrences, cancellationToken,
            ("witness", EntityCandidates.Witness), ("tribe", ForTribe));
        var (occurrences, settled, unheld, spoken) = await Counted(connection, transaction, cancellationToken);

        if (!written)
        {
            await Annotating.Run(connection, transaction, Annotating.Workspace, cancellationToken);
            await Annotating.Run(connection, transaction, Seed, cancellationToken, ("confidence", ByTheConstruct));
            await Annotating.CarryAcrossLinks(connection, transaction, cancellationToken);

            var method = EnumSpelling.Of(LinkMethod.RuleBased);
            await Annotating.Run(connection, transaction, Annotating.Settle, cancellationToken,
                ("method", method), ("source", Source));
            await Annotating.Run(connection, transaction, Annotating.Claim, cancellationToken,
                ("method", method), ("source", Source));
        }

        var byText = await Annotating.ByText(connection, transaction, Source, cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        var outcome = new TribeNameOutcome(
            false, occurrences, settled, unheld, spoken, withdrawn, byText.Sum(t => t.Words), byText,
            started.Elapsed);
        logger.LogInformation("Named the ancestors the tribe construct names: {Outcome}", outcome);
        return outcome;
    }

    private static async Task<int> Withdraw(
        NpgsqlConnection connection,
        IDbContextTransaction transaction,
        CancellationToken cancellationToken)
    {
        var withdrawn = 0;
        foreach (var statement in new[] { WithdrawThePeople, StandOnTheRest, WithdrawItsClaims })
        {
            await using var command = new NpgsqlCommand(
                statement, connection, (NpgsqlTransaction)transaction.GetDbTransaction());
            command.Parameters.AddWithValue("former", PeopleSource);
            var deleted = await command.ExecuteNonQueryAsync(cancellationToken);
            withdrawn = statement == WithdrawThePeople ? deleted : withdrawn;
        }

        return withdrawn;
    }

    private static async Task<(int Occurrences, int Settled, int Unheld, int Spoken)> Counted(
        NpgsqlConnection connection,
        IDbContextTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            Tally, connection, (NpgsqlTransaction)transaction.GetDbTransaction());

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);
        return ((int)reader.GetInt64(0), (int)reader.GetInt64(1), (int)reader.GetInt64(2),
            (int)reader.GetInt64(3));
    }
}
