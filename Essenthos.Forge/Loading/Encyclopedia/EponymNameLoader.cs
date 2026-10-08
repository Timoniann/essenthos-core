using System.Diagnostics;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace Essenthos.Core.Loading.Encyclopedia;

/// <param name="Occurrences">Hebrew names a tribe bears with its ancestor that nothing had named.</param>
/// <param name="Children">Of those, Israel the nation, which names the people.</param>
/// <param name="Eponyms">Of those, the ones named after the ancestor.</param>
/// <param name="Realms">Of those, a king's, a land's or a city's, which are left to the realm rule.</param>
/// <param name="Written">Annotations written under this pass's source, the carried ones included.</param>
internal sealed record EponymNameOutcome(
    bool AlreadyLoaded,
    int Occurrences,
    int Children,
    int Eponyms,
    int Realms,
    int Written,
    IReadOnlyList<(string Text, int Words)> ByText,
    TimeSpan Elapsed)
{
    public override string ToString() =>
        AlreadyLoaded
            ? "the names a tribe shares with its ancestor are already named"
            : $"{Occurrences} Hebrew names a tribe shares with its ancestor named nobody: {Children} are the " +
              $"nation Israel and name the people, {Eponyms} name the ancestor, and {Realms} are a king's, a land's or " +
              $"a city's and are left, in {Elapsed}. {Written} words written in all. " +
              "Per text: " + string.Join(", ", ByText.Select(t => $"{t.Text} {t.Words}"));
}

/// <summary>
/// Reuben, Asher, Naphtali, Ephraim and Israel standing alone, where nothing has yet said whether the
/// sentence means the man or the tribe.
///
/// <para>
/// A tribe's name is its ancestor's: רְאוּבֵן is the man in Genesis 46:9 and the tribe in Numbers
/// 1:20, one lexeme and one Strong number, and BHSA marks it a person, a people and a place at
/// once. The resolution by number refuses a lexeme of several kinds and a dataset's verse list names
/// only the man, so these words named nobody.
/// </para>
///
/// <para>
/// <strong>Until the sentence is read they name the ancestor</strong>, on the owner's ruling of
/// 2026-09-16, and Israel standing alone outside Genesis is the nation, because the tribe that bears
/// Israel's name is all twelve: <em>all Israel came to Shechem</em>, <em>to your tents, O Israel</em>.
/// It stays the man where the verse sets him among the fathers (<em>the God of Abraham, Isaac and
/// Israel</em>) and where it is his son or his firstborn (<em>Reuben the firstborn of Israel</em>).
/// The reading of each sentence (<see cref="EponymReadingLoader"/>) answers above this wherever it
/// speaks.
/// </para>
///
/// <para>
/// Neither the name after <em>sons of</em>, which the owner ruled on 2026-10-08 is the man with
/// <em>sons</em> the people (<see cref="EponymReadingLoader"/>), nor a name after a king, a land or a
/// city, which <see cref="RealmNameLoader"/> answers, is named here. It answers only where nothing has,
/// and is idempotent on its own source.
/// </para>
/// </summary>
internal sealed class EponymNameLoader(AppDbContext db, ILogger<EponymNameLoader> logger)
{
    public const string Source =
        "Essenthos, on the project owner's ruling of 2026-09-16 that a name a tribe shares with its " +
        "ancestor names the ancestor until the sentence is read, Israel standing alone outside Genesis " +
        "being the nation";

    /// <summary>
    /// What the rule wrote while it read <em>the children of Israel</em> as the people on the name, which
    /// the owner's ruling of 2026-10-08 gives the man.
    /// </summary>
    internal const string FormerSource =
        "Essenthos, on the project owner's ruling of 2026-09-16 that a name a tribe shares with its " +
        "ancestor names the ancestor until the sentence is read, and that the children of Israel are " +
        "the people";

    /// <summary>בֵּן, son: in the construct plural before a tribe's name, <em>the sons of Reuben</em>.</summary>
    private const string Son = "H1121";

    /// <summary>
    /// Whether the word <c>w</c> of BHSA, Israel standing alone, is the nation: outside Genesis,
    /// unless Isaac stands just before it, as in <em>Abraham, Isaac and Israel</em>, or it follows
    /// <em>son of</em> or <em>firstborn of</em>. <em>My son, my firstborn, Israel</em> of Exodus 4:22
    /// is the nation, and the suffix says so: the noun is not in the construct.
    /// </summary>
    private const string Nation =
        """
        (w.strong_number = 'H3478'
         AND NOT EXISTS (SELECT 1 FROM verse_reference r
                         WHERE r.verse_id = w.verse_id AND r.is_primary AND r.canonical_book = 1)
         AND NOT EXISTS (SELECT 1 FROM word father
                         WHERE father.verse_id = w.verse_id AND father.strong_number = 'H3327'
                           AND father.position BETWEEN w.position - 4 AND w.position - 1)
         AND NOT EXISTS (SELECT 1 FROM word kin
                         WHERE kin.verse_id = w.verse_id AND kin.position = w.position - 1
                           AND kin.strong_number IN ('H1121', 'H1060')
                           AND kin.morphology ->> 'number' = 'sg' AND kin.morphology ->> 'state' = 'c'))
        """;

    /// <summary>
    /// The words a realm is named after. <em>The king of Israel</em> and <em>the land of Judah</em>
    /// are neither the man nor the tribe, and <see cref="RealmNameLoader"/> answers them.
    /// </summary>
    private static readonly string[] Realm = RealmNameLoader.Governing;

    /// <summary>
    /// How sure the nation is: the tribe Israel's name stands for is all twelve, and outside Genesis the
    /// bare name is the people far more often than the man. The ancestor stands in wherever the
    /// sentence could be the man or his tribe, which is what the ruling says it is for.
    /// </summary>
    private const double ByThePhrase = 0.9;

    /// <summary>The ancestor where the sentence could be him or his tribe, until it is read.</summary>
    private const double ForNow = 0.7;

    private static readonly string Occurrences =
        $$"""
        CREATE TEMP TABLE occurrence (
            word_id bigint PRIMARY KEY,
            number text NOT NULL,
            people_id integer NOT NULL,
            ancestor_id integer NOT NULL,
            children boolean NOT NULL,
            realm boolean NOT NULL)
        ON COMMIT DROP;
        INSERT INTO occurrence (word_id, number, people_id, ancestor_id, children, realm)
        SELECT w.id, w.strong_number, bearing.people_id, bearing.ancestor_id,
               {{Nation}} AND NOT coalesce(before.strong_number = ANY(@realm), FALSE),
               coalesce(before.strong_number = ANY(@realm), FALSE)
        FROM word w
        JOIN text t ON t.id = w.text_id AND t.slug = @witness
        LEFT JOIN word before ON before.verse_id = w.verse_id AND before.position = w.position - 1
        CROSS JOIN LATERAL (
            SELECT count(DISTINCT e.id) AS peoples, min(e.id) AS people_id,
                   min(e.origin_entity_id) AS ancestor_id
            FROM entity_name n
            JOIN entity e ON e.id = n.entity_id AND e.kind = 'people'
            WHERE n.hebrew_strong_number = w.strong_number
              AND coalesce(n.kind, '') NOT IN ('title', 'description')) bearing
        WHERE w.morphology->>'pos' = 'nmpr'
          AND w.strong_number IS NOT NULL
          AND bearing.peoples = 1 AND bearing.ancestor_id IS NOT NULL
          AND EXISTS (SELECT 1 FROM entity_name n
                      WHERE n.entity_id = bearing.ancestor_id AND n.hebrew_strong_number = w.strong_number)
          AND NOT EXISTS (SELECT 1 FROM word_entity spoken WHERE spoken.word_id = w.id)
          AND NOT coalesce(before.strong_number = @son AND before.morphology ->> 'state' = 'c'
                           AND before.morphology ->> 'number' = 'pl', FALSE)
        """;

    private const string Seed =
        """
        INSERT INTO pending_annotation (word_id, entity_id, confidence, corroborated, note)
        SELECT o.word_id,
               CASE WHEN o.children THEN o.people_id ELSE o.ancestor_id END,
               CASE WHEN o.children THEN @phrase ELSE @now END,
               FALSE,
               o.number || CASE WHEN o.children THEN ', Israel standing alone outside Genesis, so the nation'
                                ELSE ', a name the tribe shares with its ancestor, so the ancestor' END
        FROM occurrence o
        WHERE o.children OR NOT o.realm
        """;

    private const string Tally =
        """
        SELECT count(*), count(*) FILTER (WHERE children),
               count(*) FILTER (WHERE NOT children AND NOT realm),
               count(*) FILTER (WHERE NOT children AND realm)
        FROM occurrence
        """;

    public async Task<EponymNameOutcome> Load(CancellationToken cancellationToken = default)
    {
        var started = Stopwatch.StartNew();
        await db.Database.OpenConnectionAsync(cancellationToken);
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var withdrawn = await Annotating.Withdraw(connection, transaction, FormerSource, cancellationToken);
        if (withdrawn == 0 && await db.WordEntities.AnyAsync(a => a.Source == Source, cancellationToken))
        {
            logger.LogInformation("The names a tribe shares with its ancestor are already named; nothing to do");
            return new EponymNameOutcome(true, 0, 0, 0, 0, 0, [], TimeSpan.Zero);
        }

        await Annotating.Run(connection, transaction, Occurrences, cancellationToken,
            ("witness", EntityCandidates.Witness), ("son", Son), ("realm", Realm));
        var (occurrences, children, eponyms, realms) = await Counted(connection, transaction, cancellationToken);

        await Annotating.Run(connection, transaction, Annotating.Workspace, cancellationToken);
        await Annotating.Run(connection, transaction, Seed, cancellationToken,
            ("phrase", ByThePhrase), ("now", ForNow));
        await Annotating.CarryAcrossLinks(connection, transaction, cancellationToken);

        var method = EnumSpelling.Of(LinkMethod.RuleBased);
        await Annotating.Run(connection, transaction, Annotating.Settle, cancellationToken,
            ("method", method), ("source", Source));
        await Annotating.Run(connection, transaction, Annotating.Claim, cancellationToken,
            ("method", method), ("source", Source));

        var byText = await Annotating.ByText(connection, transaction, Source, cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        var outcome = new EponymNameOutcome(
            false, occurrences, children, eponyms, realms, byText.Sum(t => t.Words), byText, started.Elapsed);
        logger.LogInformation("Named the names a tribe shares with its ancestor: {Outcome}", outcome);
        return outcome;
    }

    private static async Task<(int Occurrences, int Children, int Eponyms, int Realms)> Counted(
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
