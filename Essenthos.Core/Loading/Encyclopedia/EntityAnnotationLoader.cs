using System.Diagnostics;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace Essenthos.Core.Loading.Encyclopedia;

/// <param name="Resolved">
/// Proper-noun Strong numbers the encyclopedia answers with exactly one person or place, so that
/// the occurrence needs nobody to choose.
/// </param>
/// <param name="Contested">
/// Numbers it answers with several. These are left unannotated on purpose and are the work the
/// reading has to do — twenty-three men are called Zechariah and no amount of counting says which
/// of them this verse means.
/// </param>
/// <param name="Unanswered">Numbers it answers with nobody at all.</param>
/// <param name="ByText">What each text ended up with, so the reach is a count rather than a hope.</param>
internal sealed record AnnotationOutcome(
    bool AlreadyLoaded,
    int Resolved,
    int Contested,
    int Unanswered,
    int Annotated,
    int Corroborated,
    IReadOnlyList<(string Text, int Words)> ByText,
    TimeSpan Elapsed)
{
    public override string ToString() =>
        AlreadyLoaded
            ? "the words are already annotated with the people and places they name"
            : $"{Annotated} words name a person or a place, over {Resolved} Strong numbers that answer with " +
              $"exactly one, in {Elapsed}: {Corroborated} of them in a verse the encyclopedia independently " +
              $"says that entity is named in. {Contested} numbers answer with several and {Unanswered} with " +
              "nobody, and both are left unannotated. Per text: " +
              string.Join(", ", ByText.Select(t => $"{t.Text} {t.Words}"));
}

/// <summary>
/// Says which word names which person or place, for the case that needs nobody's judgement.
///
/// The encyclopedia knows 4,361 people and places and where each is named — but only to the verse.
/// A verse naming four people cannot tell a reader which word is which, so the two halves of this
/// project have never met at the word, and the reader's hover card has had nothing to show.
///
/// <para>
/// **What is annotated is the case where nothing has to be chosen.** BHSA marks a word as a name
/// and says what kind of name it is; the encyclopedia records, for each entity, the Strong number
/// its name is. Where that number names exactly one entity, and the kind BHSA marks is the kind
/// that entity is, the occurrence resolves without anyone weighing anything. Everything else is
/// left null, and that is the answer rather than a gap: a number naming twenty-three Zechariahs is
/// not resolved by taking the most frequent or the nearest, and a number the encyclopedia does not
/// hold is not resolved at all.
/// </para>
///
/// <para>
/// **Two exclusions are worth naming, because the obvious reading of the data gets both wrong.**
/// A label whose Strong number is a list — <c>H4428,H3389</c> for <em>King of Jerusalem</em> — says
/// what the words of a title are, not what the entity is called, and reading it as a name puts the
/// city of Jerusalem on the man Adonizedek and the place Tsereth-hash-Shachar on Jesus. And BHSA's
/// name type is a property of the lemma rather than of the occurrence: all 2,467 occurrences of
/// Israel are marked <c>pers,gens,topo</c>, which says the name can be a person, a people or a
/// place and never that it is one here. So an occurrence is taken only where BHSA commits to a
/// single kind and the encyclopedia's entity is that kind. Where it does not commit, nothing is
/// written — which is the same discipline that keeps the land of Canaan from being annotated as the
/// person Canaan.
/// </para>
///
/// <para>
/// **The annotation then travels on the links that already exist.** A King James word linked to an
/// annotated Hebrew word names what that Hebrew word names, and the confidence of the link is
/// carried into the confidence of the annotation, so a word reached by a source's own mapping is
/// not stored looking like one an aligner guessed at. One hop only, and always from the Hebrew: a
/// second hop through another translation would be an inference about an inference, and the reader
/// would have no way to see that it was.
/// </para>
///
/// <para>
/// Idempotent on its own rows the way the encyclopedia's loaders are, so it sits in the start-up
/// pipeline and costs one indexed existence check on a corpus that already has it.
/// </para>
/// </summary>
internal sealed class EntityAnnotationLoader(AppDbContext db, ILogger<EntityAnnotationLoader> logger)
{
    /// <summary>
    /// The text whose annotation is read rather than derived. It is the only one here that marks
    /// its proper nouns, and every other text is reached from it through the links.
    /// </summary>
    public const string Witness = "bhsa";

    /// <summary>
    /// How sure the corpus is that an occurrence of a name names the one entity that bears it.
    ///
    /// It is short of certainty for one reason, and the reason is not the resolution: the
    /// encyclopedia is 3,010 people and 1,351 places and the Bible names more than that, so a number
    /// answering with exactly one entity today can answer with two once somebody is added. Nothing
    /// in the data contradicts the annotation — this is the room left for what the data does not
    /// yet hold.
    /// </summary>
    private const double NameResolution = 0.9;

    /// <summary>
    /// The same, where the encyclopedia's own list of verses says this entity is named in this
    /// verse. That list is compiled from the datasets' reading of the text and not from Strong
    /// numbers, so it is a second and independent answer to the same question, and where the two
    /// agree the only thing left open is the one above. Measured over the Hebrew, they agree on
    /// 13,587 of 14,138 occurrences; almost every disagreement is the list being silent about a
    /// verse rather than naming somebody else.
    /// </summary>
    private const double Corroborated = 0.99;

    private const string Resolution =
        "BHSA's proper-noun marking, and the Strong number the encyclopedia records for the name";

    private const string VerseList =
        "the encyclopedia's own list of the verses each entity is named in";

    /// <summary>
    /// Long enough for a pass over four and a half million words and their links, which is what the
    /// carrying step is. The default thirty seconds is what a start-up pass gets on the day the
    /// counts are cold, and a start-up pass that throws does not fail its own step — it fails every
    /// step after it.
    /// </summary>
    private const int Patient = 1800;

    /// <summary>
    /// Every entity a Strong number is the name of.
    ///
    /// Only a label that is a single number is read. A comma-joined value is the numbers of the
    /// words of a title, and the words of a title are not the entity's name: taken as one, H3389
    /// stops being Jerusalem and becomes Adonizedek, who is called king of it.
    /// </summary>
    private const string Naming =
        """
        SELECT DISTINCT n.hebrew_strong_number AS number, n.entity_id
        FROM entity_name n
        WHERE n.hebrew_strong_number IS NOT NULL AND position(',' IN n.hebrew_strong_number) = 0
        """;

    /// <summary>The numbers that name exactly one entity, which are the only ones annotated.</summary>
    private static readonly string Resolvable =
        $"""
         SELECT number, min(entity_id) AS entity_id
         FROM ({Naming}) named
         GROUP BY 1 HAVING count(*) = 1
         """;

    private const string Workspace =
        """
        CREATE TEMP TABLE annotation (
            word_id bigint PRIMARY KEY,
            entity_id integer NOT NULL,
            carried double precision NOT NULL,
            corroborated boolean NOT NULL,
            note text NOT NULL)
        """;

    /// <summary>
    /// The Hebrew occurrences that resolve without anyone choosing. <c>carried</c> is 1 because
    /// nothing was crossed to reach them; the words of other texts divide it by what their link is
    /// worth.
    /// </summary>
    private static readonly string Seed =
        $"""
         INSERT INTO annotation (word_id, entity_id, carried, corroborated, note)
         SELECT w.id, resolved.entity_id, 1.0, agreed.named,
                w.strong_number || ', which BHSA marks ' || (w.morphology->>'nameType')
         FROM word w
         JOIN text t ON t.id = w.text_id AND t.slug = @witness
         JOIN ({Resolvable}) resolved ON resolved.number = w.strong_number
         JOIN entity e ON e.id = resolved.entity_id
         CROSS JOIN LATERAL (SELECT EXISTS (
             SELECT 1 FROM verse_reference r
             JOIN entity_verse ev ON ev.entity_id = resolved.entity_id
                  AND ev.canonical_book = r.canonical_book
                  AND ev.canonical_chapter = r.canonical_chapter
                  AND ev.canonical_verse = r.canonical_verse
             WHERE r.verse_id = w.verse_id AND r.is_primary) AS named) agreed
         WHERE (w.morphology->>'nameType' = 'pers' AND e.kind = 'person')
            OR (w.morphology->>'nameType' = 'topo' AND e.kind = 'place')
         """;

    /// <summary>
    /// The same annotations on every word the links say stands for one of those Hebrew words.
    ///
    /// A word reached from two Hebrew words that name two different entities is left alone: the
    /// links disagree about who is named and picking between them is the thing this loader does not
    /// do. Where they agree, the strongest link decides the confidence, because being reached twice
    /// is not weaker than being reached once.
    /// </summary>
    private const string Carry =
        """
        WITH reached AS (
            SELECT other.word_id,
                   seed.entity_id,
                   coalesce(l.confidence, 1.0) AS carried,
                   l.method,
                   seed.word_id AS through
            FROM annotation seed
            JOIN link_word mine ON mine.word_id = seed.word_id
            JOIN link l ON l.id = mine.link_id
            JOIN link_word other ON other.link_id = mine.link_id AND other.side <> mine.side
        ),
        unanimous AS (
            SELECT word_id FROM reached GROUP BY 1 HAVING count(DISTINCT entity_id) = 1
        ),
        strongest AS (
            SELECT DISTINCT ON (r.word_id) r.*
            FROM reached r JOIN unanimous u ON u.word_id = r.word_id
            ORDER BY r.word_id, r.carried DESC, r.through
        )
        INSERT INTO annotation (word_id, entity_id, carried, corroborated, note)
        SELECT s.word_id, s.entity_id, s.carried, agreed.named,
               'through ' || @witness || ' word ' || s.through || ', linked by ' || s.method
        FROM strongest s
        JOIN word w ON w.id = s.word_id
        CROSS JOIN LATERAL (SELECT EXISTS (
            SELECT 1 FROM verse_reference r
            JOIN entity_verse ev ON ev.entity_id = s.entity_id
                 AND ev.canonical_book = r.canonical_book
                 AND ev.canonical_chapter = r.canonical_chapter
                 AND ev.canonical_verse = r.canonical_verse
            WHERE r.verse_id = w.verse_id AND r.is_primary) AS named) agreed
        ON CONFLICT (word_id) DO NOTHING
        """;

    private const string Settle =
        """
        INSERT INTO word_entity (word_id, entity_id, method, confidence, source, note)
        SELECT a.word_id, a.entity_id, @method,
               (CASE WHEN a.corroborated THEN @corroborated ELSE @resolution END) * a.carried,
               @source, a.note
        FROM annotation a
        ON CONFLICT (word_id, entity_id) DO NOTHING
        """;

    /// <summary>
    /// The resolution's own claim, and the verse list's where it agrees. Written in the same
    /// transaction as the annotation: an annotation nothing claims is invisible to the agreement
    /// measure, which is the failure link_claim was already caught by once.
    /// </summary>
    private const string Claim =
        """
        INSERT INTO word_entity_claim (word_entity_id, method, confidence, source, note)
        SELECT a.id, @method, @confidence * w.carried, @source, a.note
        FROM word_entity a
        JOIN annotation w ON w.word_id = a.word_id AND w.entity_id = a.entity_id
        WHERE NOT @corroboration OR w.corroborated
        ON CONFLICT DO NOTHING
        """;

    public async Task<AnnotationOutcome> Load(CancellationToken cancellationToken = default)
    {
        if (await db.WordEntities.AnyAsync(cancellationToken))
        {
            logger.LogInformation("The words already say whom they name; nothing to do");
            return new AnnotationOutcome(true, 0, 0, 0, 0, 0, [], TimeSpan.Zero);
        }

        var started = Stopwatch.StartNew();
        await db.Database.OpenConnectionAsync(cancellationToken);
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();

        var (resolved, contested, unanswered) = await Answers(connection, cancellationToken);
        if (resolved == 0)
        {
            logger.LogWarning(
                "No proper-noun Strong number resolves to a single person or place, so nothing can be " +
                "annotated. Either the encyclopedia has not been loaded yet or {Witness} is not in the " +
                "corpus; both are earlier steps of the same pipeline",
                Witness);
            return new AnnotationOutcome(false, 0, contested, unanswered, 0, 0, [], started.Elapsed);
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await Run(connection, transaction, Workspace, cancellationToken);
        await Run(connection, transaction, Seed, cancellationToken, ("witness", Witness));
        await Run(connection, transaction, Carry, cancellationToken, ("witness", Witness));

        var method = EnumSpelling.Of(LinkMethod.StrongNumber);
        await Run(connection, transaction, Settle, cancellationToken,
            ("method", method), ("source", Resolution),
            ("resolution", NameResolution), ("corroborated", Corroborated));

        await Run(connection, transaction, Claim, cancellationToken,
            ("method", method), ("source", Resolution),
            ("confidence", NameResolution), ("corroboration", false));
        await Run(connection, transaction, Claim, cancellationToken,
            ("method", method), ("source", VerseList),
            ("confidence", Corroborated), ("corroboration", true));

        var byText = await ByText(connection, transaction, cancellationToken);
        var corroborated = await Corroboration(connection, transaction, cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        var outcome = new AnnotationOutcome(
            false, resolved, contested, unanswered, byText.Sum(t => t.Words), corroborated, byText,
            started.Elapsed);
        logger.LogInformation("Annotated: {Outcome}", outcome);
        return outcome;
    }

    /// <summary>
    /// How many proper-noun numbers the encyclopedia answers with one entity, with several, and
    /// with nobody. The second and third are the size of the work this loader deliberately does not
    /// do, and they belong in the record beside what it did.
    /// </summary>
    private async Task<(int Resolved, int Contested, int Unanswered)> Answers(
        NpgsqlConnection connection, CancellationToken cancellationToken)
    {
        var sql =
            $"""
             WITH proper AS (
                 SELECT DISTINCT w.strong_number AS number
                 FROM word w JOIN text t ON t.id = w.text_id AND t.slug = @witness
                 WHERE w.morphology->>'nameType' IS NOT NULL AND w.strong_number IS NOT NULL
             ),
             answered AS (
                 SELECT p.number, count(DISTINCT n.entity_id) AS entities
                 FROM proper p
                 LEFT JOIN ({Naming}) n ON n.number = p.number
                 GROUP BY 1
             )
             SELECT count(*) FILTER (WHERE entities = 1),
                    count(*) FILTER (WHERE entities > 1),
                    count(*) FILTER (WHERE entities = 0)
             FROM answered
             """;

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("witness", Witness);
        command.CommandTimeout = Patient;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);
        return ((int)reader.GetInt64(0), (int)reader.GetInt64(1), (int)reader.GetInt64(2));
    }

    private static async Task<IReadOnlyList<(string Text, int Words)>> ByText(
        NpgsqlConnection connection,
        IDbContextTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            "SELECT t.slug, count(*) FROM word_entity a JOIN word w ON w.id = a.word_id " +
            "JOIN text t ON t.id = w.text_id GROUP BY 1 ORDER BY 2 DESC",
            connection,
            (NpgsqlTransaction)transaction.GetDbTransaction());
        command.CommandTimeout = Patient;

        var counts = new List<(string, int)>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            counts.Add((reader.GetString(0), (int)reader.GetInt64(1)));
        }

        return counts;
    }

    private static async Task<int> Corroboration(
        NpgsqlConnection connection,
        IDbContextTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            "SELECT count(*) FROM word_entity a WHERE EXISTS (SELECT 1 FROM word_entity_claim c " +
            "WHERE c.word_entity_id = a.id AND c.source = @source)",
            connection,
            (NpgsqlTransaction)transaction.GetDbTransaction());
        command.Parameters.AddWithValue("source", VerseList);
        command.CommandTimeout = Patient;
        return (int)(long)(await command.ExecuteScalarAsync(cancellationToken))!;
    }

    private static async Task Run(
        NpgsqlConnection connection,
        IDbContextTransaction transaction,
        string sql,
        CancellationToken cancellationToken,
        params (string Name, object Value)[] parameters)
    {
        await using var command = new NpgsqlCommand(
            sql, connection, (NpgsqlTransaction)transaction.GetDbTransaction());
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        command.CommandTimeout = Patient;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
