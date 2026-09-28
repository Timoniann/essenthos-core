using System.Diagnostics;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace Essenthos.Core.Loading.Encyclopedia;

/// <param name="Occurrences">Hebrew names of a people standing in the construct after a realm's word.</param>
/// <param name="Settled">Of those, the ones exactly one people bears the name of and nothing had named.</param>
/// <param name="Unheld">Nameless words whose name no people record bears, which this pass cannot answer.</param>
/// <param name="Contested">Nameless words whose name several peoples bear, which it does not choose between.</param>
/// <param name="Spoken">Words something had already named, which it does not contest.</param>
/// <param name="Declined">Translation words the links reached that already name somebody else, left as they are.</param>
/// <param name="Written">Annotations written under this pass's source, the carried ones included.</param>
/// <param name="UnheldNames">The names behind <paramref name="Unheld"/>, with how often each stands there.</param>
internal sealed record RealmNameOutcome(
    bool AlreadyLoaded,
    int Occurrences,
    int Settled,
    int Unheld,
    int Contested,
    int Spoken,
    int Declined,
    int Written,
    IReadOnlyList<(string Text, int Words)> ByText,
    IReadOnlyList<(string Number, string Lemma, int Words)> UnheldNames,
    TimeSpan Elapsed)
{
    public override string ToString() =>
        AlreadyLoaded
            ? "the peoples named after a king, a land or a city are already named"
            : $"{Settled} of {Occurrences} Hebrew names standing after a king, a land, a city or a border " +
              $"are the people of that name, in {Elapsed}; {Unheld} name a people the encyclopedia does not " +
              $"hold ({string.Join(", ", UnheldNames.Select(n => $"{n.Lemma} {n.Number} {n.Words}"))}), " +
              $"{Contested} a name several peoples bear, and {Spoken} were already named. {Declined} " +
              $"translation words already naming somebody else were left. {Written} words written in all. " +
              "Per text: " + string.Join(", ", ByText.Select(t => $"{t.Text} {t.Words}"));
}

/// <summary>
/// <em>The king of Israel</em>, <em>the land of Judah</em>, <em>the border of Benjamin</em>: the
/// name after a realm's word is the people.
///
/// <para>
/// A tribe's name is its ancestor's, and the owner's ruling of 2026-09-16 gives the ancestor every
/// such name the sentence does not settle (<see cref="EponymNameLoader"/>). A name standing after a
/// king, a land or a city was left out of that ruling, because Judah the man had no king and held no
/// land. The owner settled it on 2026-09-28: it is the people — the Israelites, the Judahites — and
/// neither the ancestor nor blank.
/// </para>
///
/// <para>
/// <strong>The construct is what settles it, and only for these nouns.</strong> Each of
/// <see cref="Governing"/> is something ruled or held — a king, a kingdom, a reign, a land, a city,
/// a border, a field, a mountain — and in every one of the 555 places a tribe's name stands after
/// one of them it is the people that is ruled or holds it. The construct nouns left out are left
/// out because the man can be meant: <em>the sons of Reuben</em> are the man's sons in Genesis 46:9
/// and the tribe's families in Numbers 26:5, <em>the daughters</em> and <em>the house of Judah</em>
/// are a family as readily as a nation, and <em>the God of Israel</em> is the God of the patriarch
/// at Genesis 33:20 before he is the God of the nation. The words for a body of the people —
/// elders, princes, assembly, camp, remnant, clans, thousands — name the people's members rather
/// than a realm; the ruling of 2026-09-16 already answers them, and a rule here would contest it.
/// The words for a tribe have their own rule in <see cref="TribeNameLoader"/>.
/// </para>
///
/// <para>
/// The name must be one BHSA marks as able to name a people (<c>gens</c>), and exactly one people
/// must bear its number. A name no people bears is counted and listed rather than given a record
/// nobody wrote; a name several bear is a choice this rule does not make.
/// </para>
///
/// <para>
/// <strong>It answers only where nothing has.</strong> A Hebrew word something already named keeps
/// its answer, and so does a translation word the links reach that already names somebody else:
/// the carried annotation would sit beside it as a second answer, and which of the two a reader is
/// shown would then be decided by their standing rather than by anyone reading the verse.
/// </para>
///
/// <para>
/// Idempotent on its own source, and it runs after <see cref="EponymNameLoader"/>, whose realms it
/// answers.
/// </para>
/// </summary>
internal sealed class RealmNameLoader(AppDbContext db, ILogger<RealmNameLoader> logger)
{
    public const string Source =
        "Essenthos, on the project owner's ruling of 2026-09-28 that a people's name standing after a " +
        "king, a land, a city, a border and the like names the people";

    /// <summary>
    /// מֶלֶךְ king, מַמְלָכָה kingdom, מַלְכוּת reign, אֶרֶץ land, אֲדָמָה ground (Ezekiel's
    /// <em>land of Israel</em>), עִיר city, גְּבוּל border, שָׂדֶה field (<em>the fields of
    /// Ephraim</em>, Obadiah 19) and הַר mountain (<em>the hill country of Ephraim</em>).
    /// </summary>
    public static readonly string[] Governing =
        ["H4428", "H4467", "H4438", "H776", "H127", "H5892", "H1366", "H7704", "H2022"];

    /// <summary>
    /// The phrase's own meaning, as sure as the construct after a word for a tribe: nothing in the
    /// data contradicts it, and it is a reading of the phrase rather than a source naming the people.
    /// </summary>
    private const double ByThePhrase = 0.9;

    private const string Occurrences =
        """
        CREATE TEMP TABLE occurrence (
            word_id bigint PRIMARY KEY,
            number text NOT NULL,
            lemma text,
            peoples integer NOT NULL,
            entity_id integer,
            spoken boolean NOT NULL)
        ON COMMIT DROP;
        INSERT INTO occurrence (word_id, number, lemma, peoples, entity_id, spoken)
        SELECT w.id, w.strong_number, w.lemma, named.peoples, named.entity_id,
               EXISTS (SELECT 1 FROM word_entity a WHERE a.word_id = w.id)
        FROM word w
        JOIN text t ON t.id = w.text_id AND t.slug = @witness
        JOIN word realm ON realm.verse_id = w.verse_id AND realm.position = w.position - 1
             AND realm.strong_number = ANY(@governing)
             AND realm.morphology->>'state' = 'c'
        CROSS JOIN LATERAL (
            SELECT count(DISTINCT e.id) AS peoples, min(e.id) AS entity_id
            FROM entity_name n
            JOIN entity e ON e.id = n.entity_id AND e.kind = 'people'
            WHERE n.hebrew_strong_number = w.strong_number
              AND coalesce(n.kind, '') NOT IN ('title', 'description')) named
        WHERE w.strong_number IS NOT NULL
          AND w.morphology->>'pos' = 'nmpr'
          AND 'gens' = ANY(string_to_array(w.morphology->>'nameType', ','))
        """;

    private const string Seed =
        """
        INSERT INTO pending_annotation (word_id, entity_id, confidence, corroborated, note)
        SELECT o.word_id, o.entity_id, @confidence, FALSE,
               o.number || ', standing after a king, a land, a city or a border, so the people of that name'
        FROM occurrence o
        WHERE o.peoples = 1 AND NOT o.spoken
        """;

    /// <summary>The carried rows that would sit beside another entity's annotation on the same word.</summary>
    private const string Decline =
        """
        DELETE FROM pending_annotation p
        WHERE p.through IS NOT NULL
          AND EXISTS (SELECT 1 FROM word_entity a WHERE a.word_id = p.word_id AND a.entity_id <> p.entity_id)
        """;

    private const string Tally =
        """
        SELECT count(*), count(*) FILTER (WHERE peoples = 1 AND NOT spoken),
               count(*) FILTER (WHERE peoples = 0 AND NOT spoken),
               count(*) FILTER (WHERE peoples > 1 AND NOT spoken), count(*) FILTER (WHERE spoken)
        FROM occurrence
        """;

    private const string Unheld =
        """
        SELECT number, coalesce(min(lemma), ''), count(*)
        FROM occurrence
        WHERE peoples = 0 AND NOT spoken
        GROUP BY number
        ORDER BY 3 DESC, 1
        """;

    public async Task<RealmNameOutcome> Load(CancellationToken cancellationToken = default)
    {
        if (await db.WordEntities.AnyAsync(a => a.Source == Source, cancellationToken))
        {
            logger.LogInformation(
                "The peoples named after a king, a land or a city are already named; nothing to do");
            return new RealmNameOutcome(true, 0, 0, 0, 0, 0, 0, 0, [], [], TimeSpan.Zero);
        }

        var started = Stopwatch.StartNew();
        await db.Database.OpenConnectionAsync(cancellationToken);
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await Annotating.Run(connection, transaction, Occurrences, cancellationToken,
            ("witness", EntityCandidates.Witness), ("governing", Governing));
        var (occurrences, settled, unheld, contested, spoken) =
            await Counted(connection, transaction, cancellationToken);
        var unheldNames = await Unnamed(connection, transaction, cancellationToken);

        await Annotating.Run(connection, transaction, Annotating.Workspace, cancellationToken);
        await Annotating.Run(connection, transaction, Seed, cancellationToken, ("confidence", ByThePhrase));
        await Annotating.CarryAcrossLinks(connection, transaction, cancellationToken);
        var declined = await Affected(connection, transaction, Decline, cancellationToken);

        var method = EnumSpelling.Of(LinkMethod.RuleBased);
        await Annotating.Run(connection, transaction, Annotating.Settle, cancellationToken,
            ("method", method), ("source", Source));
        await Annotating.Run(connection, transaction, Annotating.Claim, cancellationToken,
            ("method", method), ("source", Source));

        var byText = await Annotating.ByText(connection, transaction, Source, cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        var outcome = new RealmNameOutcome(
            false, occurrences, settled, unheld, contested, spoken, declined, byText.Sum(t => t.Words),
            byText, unheldNames, started.Elapsed);
        logger.LogInformation("Named the peoples a realm is named after: {Outcome}", outcome);
        return outcome;
    }

    private static async Task<int> Affected(
        NpgsqlConnection connection,
        IDbContextTransaction transaction,
        string sql,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            sql, connection, (NpgsqlTransaction)transaction.GetDbTransaction());
        command.CommandTimeout = Annotating.Patient;
        return await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<(int Occurrences, int Settled, int Unheld, int Contested, int Spoken)> Counted(
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
            (int)reader.GetInt64(3), (int)reader.GetInt64(4));
    }

    private static async Task<IReadOnlyList<(string Number, string Lemma, int Words)>> Unnamed(
        NpgsqlConnection connection,
        IDbContextTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            Unheld, connection, (NpgsqlTransaction)transaction.GetDbTransaction());
        command.CommandTimeout = Annotating.Patient;

        var names = new List<(string, string, int)>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            names.Add((reader.GetString(0), reader.GetString(1), (int)reader.GetInt64(2)));
        }

        return names;
    }
}
