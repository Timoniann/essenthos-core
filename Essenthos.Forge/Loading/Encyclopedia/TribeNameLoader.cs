using System.Diagnostics;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace Essenthos.Core.Loading.Encyclopedia;

/// <param name="Occurrences">Hebrew words standing in the construct after a word for a tribe.</param>
/// <param name="Settled">Of those, the ones exactly one people bears the name of and nothing had named.</param>
/// <param name="Unheld">Words whose name no people record bears, which this pass cannot answer.</param>
/// <param name="Spoken">Words something had already named, which it does not contest.</param>
/// <param name="Written">Annotations written under this pass's source, the carried ones included.</param>
internal sealed record TribeNameOutcome(
    bool AlreadyLoaded,
    int Occurrences,
    int Settled,
    int Unheld,
    int Spoken,
    int Written,
    IReadOnlyList<(string Text, int Words)> ByText,
    TimeSpan Elapsed)
{
    public override string ToString() =>
        AlreadyLoaded
            ? "the names standing after a word for a tribe are already the tribes"
            : $"{Settled} of {Occurrences} Hebrew words standing after a word for a tribe are the " +
              $"people of that name, in {Elapsed}; {Unheld} name a people the encyclopedia does not " +
              $"hold and {Spoken} were already named. {Written} words written in all. Per text: " +
              string.Join(", ", ByText.Select(t => $"{t.Text} {t.Words}"));
}

/// <summary>
/// <em>The tribe of Naphtali</em>, where the name after the word for a tribe is the tribe.
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
/// says <em>tribe</em> with, and a name immediately after either of them is the tribe of that name
/// and not the man it descends from or the land it holds. The lexeme's kinds are not consulted:
/// they say what the word can mean anywhere, and the phrase says what it means here.
/// </para>
///
/// <para>
/// <strong>It answers only where nothing has.</strong> Eighty of these words already name somebody,
/// and forty-three of those name the man rather than the tribe — which may well be worth arguing
/// about, and is not this pass's argument to have: a reading of the verse stands above a rule about
/// the form of a phrase, and a rule that overturned eighty existing answers on a construct would be
/// deciding a question by the order the loaders run in.
/// </para>
///
/// <para>
/// Idempotent on its own source, and it runs after every pass that names a Hebrew word.
/// </para>
/// </summary>
internal sealed class TribeNameLoader(AppDbContext db, ILogger<TribeNameLoader> logger)
{
    public const string Source =
        "Essenthos, reading a name standing after the Hebrew word for a tribe as that tribe, where " +
        "exactly one people bears the name";

    /// <summary>
    /// The two words the Hebrew calls a tribe by — <em>maṭṭeh</em> and <em>šēḇeṭ</em>, both of them
    /// a staff before they are a tribe — standing in the construct before the name.
    /// </summary>
    private static readonly string[] ForTribe = ["H4294", "H7626"];

    /// <summary>
    /// The same room the gentilic resolution leaves, and for the same reason: nothing in the data
    /// contradicts the annotation, and the construct is a strong reading of the phrase rather than a
    /// source stating who is meant.
    /// </summary>
    private const double ByTheConstruct = 0.9;

    private static readonly string Occurrences =
        $"""
         CREATE TEMP TABLE occurrence (
             word_id bigint PRIMARY KEY,
             number text NOT NULL,
             peoples integer NOT NULL,
             entity_id integer,
             spoken boolean NOT NULL)
         ON COMMIT DROP;
         INSERT INTO occurrence (word_id, number, peoples, entity_id, spoken)
         SELECT w.id, w.strong_number, named.peoples, named.entity_id,
                EXISTS (SELECT 1 FROM word_entity a WHERE a.word_id = w.id)
         FROM word w
         JOIN text t ON t.id = w.text_id AND t.slug = @witness
         JOIN word tribe ON tribe.verse_id = w.verse_id AND tribe.position = w.position - 1
              AND tribe.strong_number = ANY(@tribe)
         CROSS JOIN LATERAL (
             SELECT count(DISTINCT e.id) AS peoples, min(e.id) AS entity_id
             FROM entity_name n
             JOIN entity e ON e.id = n.entity_id AND e.kind = 'people'
             WHERE n.hebrew_strong_number = w.strong_number
               AND coalesce(n.kind, '') NOT IN ('title', 'description')) named
         WHERE w.strong_number IS NOT NULL
         """;

    private const string Seed =
        """
        INSERT INTO pending_annotation (word_id, entity_id, confidence, corroborated, note)
        SELECT o.word_id, o.entity_id, @confidence, FALSE,
               o.number || ', standing after the word for a tribe, so the tribe of that name'
        FROM occurrence o
        WHERE o.peoples = 1 AND NOT o.spoken
        """;

    private const string Tally =
        """
        SELECT count(*), count(*) FILTER (WHERE peoples = 1 AND NOT spoken),
               count(*) FILTER (WHERE peoples = 0), count(*) FILTER (WHERE spoken)
        FROM occurrence
        """;

    public async Task<TribeNameOutcome> Load(CancellationToken cancellationToken = default)
    {
        if (await db.WordEntities.AnyAsync(a => a.Source == Source, cancellationToken))
        {
            logger.LogInformation(
                "The names standing after a word for a tribe are already the tribes; nothing to do");
            return new TribeNameOutcome(true, 0, 0, 0, 0, 0, [], TimeSpan.Zero);
        }

        var started = Stopwatch.StartNew();
        await db.Database.OpenConnectionAsync(cancellationToken);
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await Annotating.Run(connection, transaction, Occurrences, cancellationToken,
            ("witness", EntityCandidates.Witness), ("tribe", ForTribe));
        var (occurrences, settled, unheld, spoken) = await Counted(connection, transaction, cancellationToken);

        await Annotating.Run(connection, transaction, Annotating.Workspace, cancellationToken);
        await Annotating.Run(connection, transaction, Seed, cancellationToken, ("confidence", ByTheConstruct));
        await Annotating.CarryAcrossLinks(connection, transaction, cancellationToken);

        var method = EnumSpelling.Of(LinkMethod.Lexical);
        await Annotating.Run(connection, transaction, Annotating.Settle, cancellationToken,
            ("method", method), ("source", Source));
        await Annotating.Run(connection, transaction, Annotating.Claim, cancellationToken,
            ("method", method), ("source", Source));

        var byText = await Annotating.ByText(connection, transaction, Source, cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        var outcome = new TribeNameOutcome(
            false, occurrences, settled, unheld, spoken, byText.Sum(t => t.Words), byText,
            started.Elapsed);
        logger.LogInformation("Named the tribes the construct names: {Outcome}", outcome);
        return outcome;
    }

    private static async Task<(int Occurrences, int Settled, int Unheld, int Spoken)> Counted(
        NpgsqlConnection connection,
        IDbContextTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            Tally, connection, (NpgsqlTransaction)transaction.GetDbTransaction());
        command.CommandTimeout = Annotating.Patient;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);
        return ((int)reader.GetInt64(0), (int)reader.GetInt64(1), (int)reader.GetInt64(2),
            (int)reader.GetInt64(3));
    }
}
