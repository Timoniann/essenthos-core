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
/// <param name="Derived">
/// Of the annotations, how many rest on a name the corpus worked out rather than one the
/// encyclopedia stated. They are the places the geocoding dataset supplied, which carry no Strong
/// number of their own, and they are counted apart because they are worth less.
/// </param>
/// <param name="ByText">What each text ended up with, so the reach is a count rather than a hope.</param>
internal sealed record AnnotationOutcome(
    bool AlreadyLoaded,
    int Resolved,
    int Contested,
    int Unanswered,
    int Annotated,
    int Corroborated,
    int Derived,
    IReadOnlyList<(string Text, int Words)> ByText,
    TimeSpan Elapsed)
{
    public override string ToString() =>
        AlreadyLoaded
            ? "the words are already annotated with the people and places they name"
            : $"{Annotated} words name a person or a place, over {Resolved} Strong numbers that answer with " +
              $"exactly one, in {Elapsed}: {Corroborated} of them in a verse the encyclopedia independently " +
              $"says that entity is named in, and {Derived} on a name the corpus worked out rather than " +
              $"read. {Contested} numbers answer with several and {Unanswered} with " +
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
/// **Who the candidates are is not decided here.** <see cref="EntityCandidates"/> states that once,
/// for this loader and for the reading harness alike, and it is worth reading before this file: it
/// is where a title's Strong numbers are refused as names, where a person who appears only in Greek
/// is refused as the referent of a Masoretic word, and where the geocoding dataset's places are
/// given the Hebrew names that make them reachable at all.
/// </para>
///
/// <para>
/// **The one exclusion that belongs here** is the kind. BHSA's name type is a property of the lemma
/// rather than of the occurrence: all 2,467 occurrences of Israel are marked <c>pers,gens,topo</c>,
/// which says the name can be a person, a people or a place and never that it is one here. So an
/// occurrence is taken only where BHSA commits to a single kind and the entity is that kind. Where
/// it does not commit, nothing is written — which is the discipline that keeps the land of Canaan
/// from being annotated as the person Canaan.
/// </para>
///
/// <para>
/// **Where that kind is doing more than agreeing, the row says so.** Most numbers name one record
/// and the marking only confirms it, and those annotations needed nobody. Some are borne by two —
/// H3778 is Chaldea and the Chaldeans both — and there the marking is not confirming an answer but
/// picking one, on the word's own form. That is a different claim and it is written as a different
/// method, so a reader can tell the occurrences nothing had to decide from the ones a lexical
/// analysis decided.
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
/// **A faint link is refused where the name is already rendered.** An aligner that cannot find a
/// home for a translated word does not say so — it attaches the word to whatever it can score, and
/// in a verse that names somebody that is often the name. So the Ukrainian <em>зійшов</em>, which
/// renders <em>went up</em>, was reached from Abinoam at 0.53 and underlined as the man, in a verse
/// that had already said Abinoam confidently one word earlier. That earlier word is the test: where
/// the Hebrew name already reaches this verse of this text by a firm link, a faint one is the
/// aligner's leftover and is dropped. Where it does not, the faint link is the only account the
/// corpus has and it is kept at what it is worth — <em>Шевна</em> at 0.69 is Shebnah, and a
/// floor low enough to catch the leftovers would have taken him too.
/// </para>
///
/// <para>
/// Idempotent on its own rows the way the encyclopedia's loaders are, so it sits in the start-up
/// pipeline and costs one indexed existence check on a corpus that already has it.
/// </para>
/// </summary>
internal sealed class EntityAnnotationLoader(AppDbContext db, ILogger<EntityAnnotationLoader> logger)
{
    private const string Witness = EntityCandidates.Witness;

    private const string Rendering = EntityCandidates.Rendering;

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

    /// <summary>
    /// The same, where the name itself is the corpus's own conclusion rather than the
    /// encyclopedia's statement — the places the geocoding dataset supplied, which carry no Strong
    /// number and are reached by reading the King James rendering back onto the Hebrew word.
    ///
    /// Lower than a stated name, and deliberately so. It carries the same room for an encyclopedia
    /// that will grow, plus the derivation's own risk: on the hundred-odd places the other dataset
    /// had already joined by hand it has yet to be caught naming the wrong one, but a hundred is
    /// what could be checked out of six hundred and fifty, and a number that hid that would be
    /// claiming more than was measured.
    /// </summary>
    private const double DerivedName = 0.8;

    /// <summary>
    /// The confidence below which a link is not enough on its own to put a name on a word.
    ///
    /// It is not a floor, and nothing is dropped for being faint alone: a faint link is often the
    /// only account the corpus has of a word, and a great many faint ones are true. Shebnah at
    /// 0.69 and Esau at 0.66 sit here, and so do the 250 places the Ukrainian <em>Бог</em> stands
    /// for <em>адонай</em>, none of which reaches 0.59. What this marks is a link weak enough to
    /// lose to a better one, and losing is the only thing that happens to it.
    /// </summary>
    private const double Faint = 0.70;

    /// <summary>
    /// The confidence at which a link is taken to be the rendering, so that a faint link to the
    /// same Hebrew word in the same verse has nothing left to explain.
    ///
    /// The gap between this and <see cref="Faint"/> is the point. Two words of one text can both
    /// render one Hebrew name — <em>of Abinoam</em> is two words in the King James — and a rule
    /// that dropped the weaker of any pair would take the second half of every such rendering. A
    /// word that is part of the rendering scores near the one beside it; a word the aligner had
    /// nowhere else to put does not.
    /// </summary>
    private const double Firm = 0.90;

    private const string Resolution =
        "BHSA's proper-noun marking, and the Strong number the encyclopedia records for the name";

    /// <summary>
    /// The method for an occurrence whose number is borne by more than one record, where the name
    /// type is therefore not agreeing with the only answer but choosing among several.
    ///
    /// <see cref="LinkMethod.StrongNumber"/> means that nothing had to be chosen, and saying it
    /// where something was is a claim of the wrong kind however right the answer: the source string
    /// on the row already names BHSA's marking as half of what established it, and the method
    /// contradicted it. <see cref="LinkMethod.Lexical"/> is the method for a conclusion the word's
    /// form reached, which is what the marking is, and it is what the reader is shown as <em>by the
    /// form of the word</em>.
    ///
    /// <para>
    /// It stands below a reading of the verse rather than above one, and that is the point rather
    /// than a cost. A marking says which kind of thing the lexeme names; somebody who read the
    /// sentence knows more than that, and where the two disagree the sentence should win.
    /// </para>
    /// </summary>
    private const LinkMethod ByTheForm = LinkMethod.Lexical;

    private const string Derivation =
        "BHSA's proper-noun marking, and the Hebrew name read off the King James word that renders " +
        "it in a verse the geocoding dataset says the place is named in";

    private const string VerseList =
        "the encyclopedia's own list of the verses each entity is named in";

    /// <summary>
    /// Long enough for a pass over four and a half million words and their links, which is what the
    /// carrying step is. The default thirty seconds is what a start-up pass gets on the day the
    /// counts are cold, and a start-up pass that throws does not fail its own step — it fails every
    /// step after it.
    /// </summary>
    private const int Patient = 1800;

    /// <summary>The numbers that name exactly one entity, which are the only ones annotated.</summary>
    private static readonly string Resolvable =
        $"""
         SELECT number, min(entity_id) AS entity_id, bool_and(stated) AS stated
         FROM ({EntityCandidates.Naming}) named
         GROUP BY 1 HAVING count(*) = 1
         """;

    private const string Workspace =
        """
        CREATE TEMP TABLE annotation (
            word_id bigint PRIMARY KEY,
            entity_id integer NOT NULL,
            carried double precision NOT NULL,
            corroborated boolean NOT NULL,
            stated boolean NOT NULL,
            distinguished boolean NOT NULL,
            note text NOT NULL)
        """;

    /// <summary>
    /// The Hebrew occurrences that resolve without anyone choosing. <c>carried</c> is 1 because
    /// nothing was crossed to reach them; the words of other texts divide it by what their link is
    /// worth.
    ///
    /// <para>
    /// <c>distinguished</c> is whether the name type had to rule a rival out rather than merely
    /// agree with the only answer there was. It asks of the whole encyclopedia and not of the
    /// candidate list, which is deliberate: the list holds people and places because those are the
    /// only things a word marked <c>pers</c> or <c>topo</c> can be, and it is that very exclusion —
    /// a fact about the word's form, not about its number — that this column is recording. H3778 is
    /// Chaldea to the encyclopedia and the Chaldeans as well, and the fifteen occurrences of the
    /// land are the land because BHSA analyses them as a singular toponym rather than as the plural
    /// gentilic it keeps as a separate lexeme. That is a lexical judgement and the row says so.
    /// </para>
    /// </summary>
    private static readonly string Seed =
        $"""
         INSERT INTO annotation (word_id, entity_id, carried, corroborated, stated, distinguished, note)
         SELECT w.id, resolved.entity_id, 1.0, agreed.named, resolved.stated,
                (SELECT count(DISTINCT n.entity_id) FROM entity_name n
                 WHERE n.hebrew_strong_number = w.strong_number) > 1,
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
    ///
    /// <para>
    /// <c>rendered</c> asks of each Hebrew name, and of each verse it reaches, how well that verse
    /// renders it at best; <c>supported</c> then drops the reaches that are faint in a verse where
    /// the name is already rendered firmly. The comparison is per verse rather than per text on
    /// purpose: a link may land in a verse the translation divided differently, and a firm
    /// rendering three verses away is no reason to take away the only account this verse has.
    /// </para>
    ///
    /// <para>
    /// It happens before unanimity rather than after, so that a leftover cannot veto a good reading
    /// by disagreeing with it.
    /// </para>
    /// </summary>
    private const string Carry =
        """
        WITH reached AS (
            SELECT other.word_id,
                   w.text_id,
                   w.verse_id,
                   seed.entity_id,
                   coalesce(l.confidence, 1.0) AS carried,
                   seed.stated,
                   seed.distinguished,
                   l.method,
                   seed.word_id AS through
            FROM annotation seed
            JOIN link_word mine ON mine.word_id = seed.word_id
            JOIN link l ON l.id = mine.link_id
            JOIN link_word other ON other.link_id = mine.link_id AND other.side <> mine.side
            JOIN word w ON w.id = other.word_id
        ),
        rendered AS (
            SELECT through, text_id, verse_id, max(carried) AS best
            FROM reached GROUP BY 1, 2, 3
        ),
        supported AS (
            SELECT r.*
            FROM reached r
            JOIN rendered d ON d.through = r.through
                 AND d.text_id = r.text_id AND d.verse_id = r.verse_id
            WHERE r.carried >= @faint OR d.best < @firm
        ),
        unanimous AS (
            SELECT word_id FROM supported GROUP BY 1 HAVING count(DISTINCT entity_id) = 1
        ),
        strongest AS (
            SELECT DISTINCT ON (r.word_id) r.*
            FROM supported r JOIN unanimous u ON u.word_id = r.word_id
            ORDER BY r.word_id, r.carried DESC, r.through
        )
        INSERT INTO annotation (word_id, entity_id, carried, corroborated, stated, distinguished, note)
        SELECT s.word_id, s.entity_id, s.carried, agreed.named, s.stated, s.distinguished,
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

    /// <summary>
    /// The conclusion, with the method the row actually earned.
    ///
    /// <c>@method</c> is the resolution that needed nobody, and it is the honest answer for the
    /// overwhelming majority: the number named one record in the whole encyclopedia and the name
    /// type only agreed with it. <c>@form</c> is for the rest, where the number is several records'
    /// and the marking is what chose between them — the annotation is then as good as BHSA's
    /// analysis of that word and no better, and a reader is owed the difference.
    /// </summary>
    private const string Settle =
        """
        INSERT INTO word_entity (word_id, entity_id, method, confidence, source, note)
        SELECT a.word_id, a.entity_id,
               CASE WHEN a.distinguished THEN @form ELSE @method END,
               (CASE WHEN NOT a.stated THEN @derived
                     WHEN a.corroborated THEN @corroborated
                     ELSE @resolution END) * a.carried,
               CASE WHEN a.stated THEN @source ELSE @derivation END, a.note
        FROM annotation a
        ON CONFLICT (word_id, entity_id) DO NOTHING
        """;

    /// <summary>
    /// The resolution's own claim, and the verse list's where it agrees. Written in the same
    /// transaction as the annotation: an annotation nothing claims is invisible to the agreement
    /// measure, which is the failure link_claim was already caught by once.
    ///
    /// <para>
    /// <c>@stated</c> selects which half of the annotations the claim is about, because a stated
    /// name and a derived one are two different assertions and each has to name what made it. The
    /// verse list is a claim only about the stated half: for a derived name the verse list is not a
    /// second opinion but the very evidence the derivation was read from, and writing it as
    /// corroboration would be the corpus agreeing with itself.
    /// </para>
    /// </summary>
    private const string Claim =
        """
        INSERT INTO word_entity_claim (word_entity_id, method, confidence, source, note)
        SELECT a.id, CASE WHEN w.distinguished THEN @form ELSE @method END,
               @confidence * w.carried, @source, a.note
        FROM word_entity a
        JOIN annotation w ON w.word_id = a.word_id AND w.entity_id = a.entity_id
        WHERE w.stated = @stated AND (NOT @corroboration OR w.corroborated)
        ON CONFLICT DO NOTHING
        """;

    public async Task<AnnotationOutcome> Load(CancellationToken cancellationToken = default)
    {
        if (await db.WordEntities.AnyAsync(cancellationToken))
        {
            logger.LogInformation("The words already say whom they name; nothing to do");
            return new AnnotationOutcome(true, 0, 0, 0, 0, 0, 0, [], TimeSpan.Zero);
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
            return new AnnotationOutcome(false, 0, contested, unanswered, 0, 0, 0, [], started.Elapsed);
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await Run(connection, transaction, Workspace, cancellationToken);
        await Run(connection, transaction, Seed, cancellationToken,
            ("witness", Witness), ("rendering", Rendering));
        await Run(connection, transaction, Carry, cancellationToken,
            ("witness", Witness), ("faint", Faint), ("firm", Firm));

        var method = EnumSpelling.Of(LinkMethod.StrongNumber);
        var form = EnumSpelling.Of(ByTheForm);
        await Run(connection, transaction, Settle, cancellationToken,
            ("method", method), ("form", form), ("source", Resolution), ("derivation", Derivation),
            ("resolution", NameResolution), ("corroborated", Corroborated), ("derived", DerivedName));

        await Run(connection, transaction, Claim, cancellationToken,
            ("method", method), ("form", form), ("source", Resolution),
            ("confidence", NameResolution), ("stated", true), ("corroboration", false));
        await Run(connection, transaction, Claim, cancellationToken,
            ("method", method), ("form", form), ("source", Derivation),
            ("confidence", DerivedName), ("stated", false), ("corroboration", false));
        await Run(connection, transaction, Claim, cancellationToken,
            ("method", method), ("form", form), ("source", VerseList),
            ("confidence", Corroborated), ("stated", true), ("corroboration", true));

        var byText = await ByText(connection, transaction, cancellationToken);
        var corroborated = await Corroboration(connection, transaction, VerseList, cancellationToken);
        var derived = await Corroboration(connection, transaction, Derivation, cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        var outcome = new AnnotationOutcome(
            false, resolved, contested, unanswered, byText.Sum(t => t.Words), corroborated, derived,
            byText, started.Elapsed);
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
                 LEFT JOIN ({EntityCandidates.Naming}) n ON n.number = p.number
                 GROUP BY 1
             )
             SELECT count(*) FILTER (WHERE entities = 1),
                    count(*) FILTER (WHERE entities > 1),
                    count(*) FILTER (WHERE entities = 0)
             FROM answered
             """;

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("witness", Witness);
        command.Parameters.AddWithValue("rendering", Rendering);
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

    /// <summary>How many annotations one named source spoke for.</summary>
    private static async Task<int> Corroboration(
        NpgsqlConnection connection,
        IDbContextTransaction transaction,
        string source,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            "SELECT count(*) FROM word_entity a WHERE EXISTS (SELECT 1 FROM word_entity_claim c " +
            "WHERE c.word_entity_id = a.id AND c.source = @source)",
            connection,
            (NpgsqlTransaction)transaction.GetDbTransaction());
        command.Parameters.AddWithValue("source", source);
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
