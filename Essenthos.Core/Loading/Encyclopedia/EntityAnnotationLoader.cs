using System.Diagnostics;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Endpoints;
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
internal sealed record NameAnswers(int Resolved, int Contested, int Unanswered)
{
    public override string ToString() =>
        $"{Resolved} answer with exactly one, {Contested} with several and {Unanswered} with nobody";
}

/// <param name="Hebrew">What the encyclopedia answers for the names BHSA marks.</param>
/// <param name="Greek">The same for the names the lexicon writes with a capital.</param>
/// <param name="Refused">
/// Of the Greek numbers that answered with exactly one entity, how many no Greek text vouches for —
/// the encyclopedia spells the name in a way no witness and no lexicon writes under that number, so
/// the join is somebody's slip rather than a fact about the text and nothing is written from it.
/// </param>
/// <param name="Derived">
/// Of the annotations, how many rest on a name the corpus worked out rather than one the
/// encyclopedia stated. They are the places the geocoding dataset supplied, which carry no Strong
/// number of their own, and they are counted apart because they are worth less.
/// </param>
/// <param name="ByText">What each text ended up with, so the reach is a count rather than a hope.</param>
internal sealed record AnnotationOutcome(
    bool AlreadyLoaded,
    NameAnswers Hebrew,
    NameAnswers Greek,
    int Refused,
    int Annotated,
    int Corroborated,
    int Derived,
    IReadOnlyList<(string Text, int Words)> ByText,
    TimeSpan Elapsed)
{
    public override string ToString() =>
        AlreadyLoaded
            ? "the words are already annotated with the people and places they name"
            : $"{Annotated} words name a person or a place in {Elapsed}: {Corroborated} of them in a " +
              $"verse the encyclopedia independently says that entity is named in, and {Derived} on a " +
              $"name the corpus worked out rather than read. Of the Hebrew numbers {Hebrew}; of the " +
              $"Greek {Greek}, and {Refused} of the resolved ones are refused because no Greek text " +
              "spells the name the way the encyclopedia does. Per text: " +
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
/// **The Greek has no such marking, so its gate is built rather than read.** Nestle's morphology
/// says <c>noun</c> 28,394 times and never <em>proper noun</em>, and the lexicon has no
/// proper-noun flag for a Greek entry either. What it does have is a capital letter: 587 of its
/// 5,523 Greek lemmas are written with one, and that is what a lexicographer writing Παῦλος rather
/// than παῦλος is saying. Three things were checked before it was trusted. Robinson's parsing of
/// the Byzantine text tags 177 numbers as proper nouns independently, and the capital agrees with
/// 175 of them — the two it misses are <em>Gabbatha</em> and <em>cherubim</em>, transliterated
/// once each and nobody's name. Nestle's own lemmatisation is a second independent capitalisation
/// and the two agree on 5,318 of 5,330 numbers, the twelve disagreements being titles and
/// loanwords totalling thirty words. And against the encyclopedia's own list of the verses each
/// entity is named in, the capitalised numbers land where it says the entity is 93.2% of the time
/// and the uncapitalised ones 22.2% — because the uncapitalised ones are ἔρχομαι offered as Jesus,
/// ἡμέρα as Jemimah, ζωή as Eve and μέν as Menna, 2,622 words of the New Testament that would
/// otherwise have been annotated with a person.
/// </para>
///
/// <para>
/// **And the Greek join is checked against the text before it is used.** The encyclopedia's Greek
/// Strong numbers are less careful than its Hebrew ones: it gives Ἰωδά the number of Ἰούδας, which
/// resolves to exactly one entity and would have put a walk-on of Luke's genealogy on all 151
/// occurrences of Judas in four Greek texts. So a number is taken only where the encyclopedia's own
/// spelling of the name is one the Greek actually writes under it — the lexicon's lemma, a witness's
/// lemma, or a form some witness prints. That refuses two numbers of 269: Ἰωδά, and Philemon, whose
/// Greek column holds the transliteration rather than the Greek.
/// </para>
///
/// <para>
/// **The annotation then travels on the links that already exist.** A King James word linked to an
/// annotated Hebrew word names what that Hebrew word names, and the confidence of the link is
/// carried into the confidence of the annotation, so a word reached by a source's own mapping is
/// not stored looking like one an aligner guessed at. One hop only, and always from a witness: a
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
/// the name already reaches this verse of this text by a firm link, a faint one is the aligner's
/// leftover and is dropped. Where it does not, the faint link is the only account the corpus has
/// and it is kept at what it is worth — <em>Шевна</em> at 0.69 is Shebnah, and a floor low enough
/// to catch the leftovers would have taken him too.
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

    /// <summary>The language the Greek spellings are folded as, which is what the fold keys on.</summary>
    private const string Greek = "grc";

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
    /// The same for a Greek name, and lower, because two of its three supports are weaker.
    ///
    /// It carries the same room for an encyclopedia that will grow. On top of that the proper-noun
    /// class is the lexicon's capital letter rather than the witness's own marking — measured, and
    /// the measurements are in the class comment, but measured against two sources rather than
    /// stated by the text being read. And the encyclopedia's Greek numbers have been caught being
    /// wrong in a way its Hebrew ones have not: one is another name's number outright, and
    /// thirty-four rows carry an extended numbering that is not Strong's, whose homograph letter is
    /// dropped on the way in and lands them on a different lemma.
    /// </summary>
    private const double GreekNameResolution = 0.85;

    /// <summary>
    /// The same, where the encyclopedia's own list of verses says this entity is named in this
    /// verse. That list is compiled from the datasets' reading of the text and not from Strong
    /// numbers, so it is a second and independent answer to the same question, and where the two
    /// agree the only thing left open is the one above. Measured over the Hebrew, they agree on
    /// 13,587 of 14,138 occurrences; over the Greek, on 5,458 of 5,722. Almost every disagreement
    /// is the list being silent about a verse rather than naming somebody else.
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

    private const string GreekResolution =
        "the lexicon's capitalised lemma, the Greek Strong number the encyclopedia records for the " +
        "name, and the Greek text spelling that name the way the encyclopedia spells it";

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

    /// <summary>What a language answers when nothing of it is loaded, or nothing was asked.</summary>
    private static readonly NameAnswers Nothing = new(0, 0, 0);

    /// <summary>The numbers that name exactly one entity, which are the only ones annotated.</summary>
    private static readonly string Resolvable =
        $"""
         SELECT number, min(entity_id) AS entity_id, bool_and(stated) AS stated
         FROM ({EntityCandidates.Naming}) named
         GROUP BY 1 HAVING count(*) = 1
         """;

    /// <summary>
    /// A word of a Greek witness that is a noun, in whichever dialect of morphology its text
    /// carries: Nestle's own part of speech, or Robinson's tag, whose first field is the class.
    ///
    /// It is the occurrence-level half of the gate, and what it keeps out is the gentilic — 197
    /// occurrences of Ἰουδαῖος, <em>Jewish</em>, and thirty other adjectives and adverbs formed
    /// from a name. Being Roman is not being Rome.
    /// </summary>
    private const string GreekNoun =
        "(w.morphology->>'pos' = 'noun' OR w.morphology->>'robinson' LIKE 'N-%')";

    /// <summary>
    /// The Greek numbers the lexicon writes as a name and the encyclopedia answers with exactly one
    /// entity. Takes <c>@witnesses</c>.
    /// </summary>
    private static readonly string GreekResolvable =
        $"""
         SELECT number, min(entity_id) AS entity_id
         FROM ({EntityCandidates.GreekNaming}) named
         WHERE EXISTS (
             SELECT 1 FROM strong_entry lexicon
             WHERE lexicon.strong_number = named.number
               AND lower(left(lexicon.lemma, 1)) <> left(lexicon.lemma, 1))
         GROUP BY 1 HAVING count(*) = 1
         """;

    /// <summary>
    /// What the encyclopedia calls each of those names, beside what the lexicon calls it. One row
    /// per spelling the encyclopedia offers, so a name it records twice is checked twice.
    /// </summary>
    private static readonly string Claimed =
        $"""
         SELECT DISTINCT resolved.number, n.greek, lexicon.lemma
         FROM ({GreekResolvable}) resolved
         JOIN entity_name n ON n.entity_id = resolved.entity_id
              AND n.greek_strong_number = resolved.number AND n.greek IS NOT NULL
         JOIN strong_entry lexicon ON lexicon.strong_number = resolved.number
         """;

    /// <summary>
    /// Every spelling the Greek texts themselves write under one of those numbers: the form as
    /// printed and the lemma the edition gives it. Both are needed. A name the New Testament never
    /// puts in the nominative — Ἰορδάνης, Δαμασκός, Ζεβεδαῖος — is printed only in oblique cases
    /// and would be unrecognisable against the encyclopedia's citation form; a name whose editions
    /// spell it differently — Μαθθίας against Ματθίας, Σάπφιρα against Σαπφείρη — matches the lemma
    /// of one edition where it matches neither the lexicon nor the other.
    /// </summary>
    private static readonly string Printed =
        $"""
         SELECT DISTINCT w.strong_number, w.normalised_text, w.lemma
         FROM word w
         JOIN text t ON t.id = w.text_id AND t.slug = ANY(@witnesses)
         JOIN ({GreekResolvable}) resolved ON resolved.number = w.strong_number
         """;

    /// <summary>
    /// Where the annotation is assembled before anything is written.
    ///
    /// <c>resolution</c> is what the name is worth before the verse list and the links are taken
    /// into account, and <c>source</c> is what established it in the words the row will carry. Both
    /// are on the row rather than derived from it, because a Hebrew name, a Greek one and a place
    /// the corpus worked out are three different assertions and the step that carries them onto a
    /// translation does not know which it is carrying.
    ///
    /// <para>
    /// Both drop themselves at commit. A temporary table outlives its transaction and belongs to
    /// the connection, and connections here are pooled, so without this a second load in one
    /// process fails on <em>relation already exists</em> — a start-up crash a long way from its
    /// cause.
    /// </para>
    /// </summary>
    private const string Workspace =
        """
        CREATE TEMP TABLE annotation (
            word_id bigint PRIMARY KEY,
            entity_id integer NOT NULL,
            carried double precision NOT NULL,
            corroborated boolean NOT NULL,
            stated boolean NOT NULL,
            resolution double precision NOT NULL,
            source text NOT NULL,
            note text NOT NULL)
        ON COMMIT DROP;
        CREATE TEMP TABLE attested (number text PRIMARY KEY) ON COMMIT DROP
        """;

    /// <summary>
    /// The Hebrew occurrences that resolve without anyone choosing. <c>carried</c> is 1 because
    /// nothing was crossed to reach them; the words of other texts divide it by what their link is
    /// worth.
    /// </summary>
    private static readonly string Seed =
        $"""
         INSERT INTO annotation
             (word_id, entity_id, carried, corroborated, stated, resolution, source, note)
         SELECT w.id, resolved.entity_id, 1.0, agreed.named, resolved.stated,
                CASE WHEN resolved.stated THEN @resolution ELSE @derived END,
                CASE WHEN resolved.stated THEN @source ELSE @derivation END,
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
    /// The same for the Greek, read from each of the four witnesses rather than carried into three
    /// of them, because every one of them states the number this rests on.
    ///
    /// There is no kind test to make. The Hebrew needs one because BHSA's marking says what a name
    /// <em>can</em> be and the entity says what it is; here the entity is the only answer either
    /// side gives, and a number that answers with one entity has nothing to disagree with.
    /// </summary>
    private static readonly string GreekSeed =
        $"""
         INSERT INTO annotation
             (word_id, entity_id, carried, corroborated, stated, resolution, source, note)
         SELECT w.id, resolved.entity_id, 1.0, agreed.named, true, @resolution, @source,
                w.strong_number || ', which the lexicon writes as the name ' || lexicon.lemma
         FROM word w
         JOIN text t ON t.id = w.text_id AND t.slug = ANY(@witnesses)
         JOIN ({GreekResolvable}) resolved ON resolved.number = w.strong_number
         JOIN attested ON attested.number = resolved.number
         JOIN strong_entry lexicon ON lexicon.strong_number = w.strong_number
         CROSS JOIN LATERAL (SELECT EXISTS (
             SELECT 1 FROM verse_reference r
             JOIN entity_verse ev ON ev.entity_id = resolved.entity_id
                  AND ev.canonical_book = r.canonical_book
                  AND ev.canonical_chapter = r.canonical_chapter
                  AND ev.canonical_verse = r.canonical_verse
             WHERE r.verse_id = w.verse_id AND r.is_primary) AS named) agreed
         WHERE {GreekNoun}
         ON CONFLICT (word_id) DO NOTHING
         """;

    /// <summary>
    /// The same annotations on every word the links say stands for one of those witness words.
    ///
    /// A word reached from two witness words that name two different entities is left alone: the
    /// links disagree about who is named and picking between them is the thing this loader does not
    /// do. Where they agree, the strongest link decides the confidence, because being reached twice
    /// is not weaker than being reached once.
    ///
    /// <para>
    /// <c>rendered</c> asks of each witness name, and of each verse it reaches, how well that verse
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
                   seed.resolution,
                   seed.source,
                   l.method,
                   seed.word_id AS through,
                   witness.slug AS spoken_by
            FROM annotation seed
            JOIN word origin ON origin.id = seed.word_id
            JOIN text witness ON witness.id = origin.text_id
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
        INSERT INTO annotation
            (word_id, entity_id, carried, corroborated, stated, resolution, source, note)
        SELECT s.word_id, s.entity_id, s.carried, agreed.named, s.stated, s.resolution, s.source,
               'through ' || s.spoken_by || ' word ' || s.through || ', linked by ' || s.method
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
               (CASE WHEN a.stated AND a.corroborated THEN @corroborated
                     ELSE a.resolution END) * a.carried,
               a.source, a.note
        FROM annotation a
        ON CONFLICT (word_id, entity_id) DO NOTHING
        """;

    /// <summary>
    /// The resolution's own claim. Written in the same transaction as the annotation: an annotation
    /// nothing claims is invisible to the agreement measure, which is the failure link_claim was
    /// already caught by once.
    /// </summary>
    private const string Claim =
        """
        INSERT INTO word_entity_claim (word_entity_id, method, confidence, source, note)
        SELECT a.id, @method, w.resolution * w.carried, w.source, a.note
        FROM word_entity a
        JOIN annotation w ON w.word_id = a.word_id AND w.entity_id = a.entity_id
        ON CONFLICT DO NOTHING
        """;

    /// <summary>
    /// The verse list's claim, where it agrees.
    ///
    /// It is a claim only about the stated half: for a derived name the verse list is not a second
    /// opinion but the very evidence the derivation was read from, and writing it as corroboration
    /// would be the corpus agreeing with itself.
    /// </summary>
    private const string Agreement =
        """
        INSERT INTO word_entity_claim (word_entity_id, method, confidence, source, note)
        SELECT a.id, @method, @confidence * w.carried, @source, a.note
        FROM word_entity a
        JOIN annotation w ON w.word_id = a.word_id AND w.entity_id = a.entity_id
        WHERE w.stated AND w.corroborated
        ON CONFLICT DO NOTHING
        """;

    public async Task<AnnotationOutcome> Load(CancellationToken cancellationToken = default)
    {
        if (await db.WordEntities.AnyAsync(cancellationToken))
        {
            logger.LogInformation("The words already say whom they name; nothing to do");
            return new AnnotationOutcome(
                true, Nothing, Nothing, 0, 0, 0, 0, [], TimeSpan.Zero);
        }

        var started = Stopwatch.StartNew();
        await db.Database.OpenConnectionAsync(cancellationToken);
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();

        var hebrew = await Answers(connection, HebrewNumbers, EntityCandidates.Naming,
            cancellationToken, ("witness", Witness), ("rendering", Rendering));
        var greek = await Answers(connection, GreekNumbers, EntityCandidates.GreekNaming,
            cancellationToken, ("witnesses", EntityCandidates.GreekWitnesses));

        if (hebrew.Resolved == 0 && greek.Resolved == 0)
        {
            logger.LogWarning(
                "No proper-noun Strong number resolves to a single person or place, so nothing can be " +
                "annotated. Either the encyclopedia has not been loaded yet or neither {Witness} nor " +
                "the Greek witnesses are in the corpus; both are earlier steps of the same pipeline",
                Witness);
            return new AnnotationOutcome(false, hebrew, greek, 0, 0, 0, 0, [], started.Elapsed);
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await Run(connection, transaction, Workspace, cancellationToken);
        await Run(connection, transaction, Seed, cancellationToken,
            ("witness", Witness), ("rendering", Rendering), ("source", Resolution),
            ("derivation", Derivation), ("resolution", NameResolution), ("derived", DerivedName));

        var refused = await Attest(connection, transaction, cancellationToken);
        await Run(connection, transaction, GreekSeed, cancellationToken,
            ("witnesses", EntityCandidates.GreekWitnesses),
            ("source", GreekResolution), ("resolution", GreekNameResolution));

        await Run(connection, transaction, Carry, cancellationToken,
            ("faint", Faint), ("firm", Firm));

        var method = EnumSpelling.Of(LinkMethod.StrongNumber);
        await Run(connection, transaction, Settle, cancellationToken,
            ("method", method), ("corroborated", Corroborated));
        await Run(connection, transaction, Claim, cancellationToken, ("method", method));
        await Run(connection, transaction, Agreement, cancellationToken,
            ("method", method), ("source", VerseList), ("confidence", Corroborated));

        var byText = await ByText(connection, transaction, cancellationToken);
        var corroborated = await Corroboration(connection, transaction, VerseList, cancellationToken);
        var derived = await Corroboration(connection, transaction, Derivation, cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        var outcome = new AnnotationOutcome(
            false, hebrew, greek, refused, byText.Sum(t => t.Words), corroborated, derived,
            byText, started.Elapsed);
        logger.LogInformation("Annotated: {Outcome}", outcome);
        return outcome;
    }

    /// <summary>The numbers BHSA marks as names, which is the population the Hebrew half answers.</summary>
    private const string HebrewNumbers =
        """
        SELECT DISTINCT w.strong_number AS number
        FROM word w JOIN text t ON t.id = w.text_id AND t.slug = @witness
        WHERE w.morphology->>'nameType' IS NOT NULL AND w.strong_number IS NOT NULL
        """;

    /// <summary>The same for the Greek: a noun whose lexicon lemma is written with a capital.</summary>
    private static readonly string GreekNumbers =
        $"""
         SELECT DISTINCT w.strong_number AS number
         FROM word w
         JOIN text t ON t.id = w.text_id AND t.slug = ANY(@witnesses)
         JOIN strong_entry lexicon ON lexicon.strong_number = w.strong_number
              AND lower(left(lexicon.lemma, 1)) <> left(lexicon.lemma, 1)
         WHERE {GreekNoun}
         """;

    /// <summary>
    /// Which Greek numbers the text vouches for, and how many resolved ones it does not.
    ///
    /// The comparison is on the folded spelling, and the fold is the one the corpus already ran
    /// over every word — <see cref="WordFolding"/>, in C#, because a fold written a second time in
    /// SQL is a fold that will one day disagree with itself and refuse a name that is there. So the
    /// spellings are read out, folded here against the folded forms the words already carry, and
    /// the numbers that survive are handed back to the seed as a table.
    /// </summary>
    private async Task<int> Attest(
        NpgsqlConnection connection,
        IDbContextTransaction transaction,
        CancellationToken cancellationToken)
    {
        var claimed = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        var writes = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);

        await foreach (var row in Rows(connection, transaction, Claimed, cancellationToken,
                           ("witnesses", EntityCandidates.GreekWitnesses)))
        {
            Remember(claimed, row.GetString(0), row.IsDBNull(1) ? null : row.GetString(1));
            Remember(writes, row.GetString(0), row.IsDBNull(2) ? null : row.GetString(2));
        }

        await foreach (var row in Rows(connection, transaction, Printed, cancellationToken,
                           ("witnesses", EntityCandidates.GreekWitnesses)))
        {
            var number = row.GetString(0);
            if (!row.IsDBNull(1))
            {
                (writes.TryGetValue(number, out var folded) ? folded : writes[number] = [])
                    .Add(row.GetString(1));
            }

            Remember(writes, number, row.IsDBNull(2) ? null : row.GetString(2));
        }

        var attested = claimed
            .Where(name => writes.TryGetValue(name.Key, out var spellings) && spellings.Overlaps(name.Value))
            .Select(name => name.Key)
            .ToArray();

        await Run(connection, transaction,
            "INSERT INTO attested (number) SELECT unnest(@numbers)", cancellationToken,
            ("numbers", attested));

        return claimed.Count - attested.Length;
    }

    private static void Remember(Dictionary<string, HashSet<string>> spellings, string number, string? spelling)
    {
        if (spelling is null)
        {
            return;
        }

        if (!spellings.TryGetValue(number, out var folded))
        {
            spellings[number] = folded = new HashSet<string>(StringComparer.Ordinal);
        }

        folded.Add(WordFolding.Fold(spelling, Greek));
    }

    /// <summary>
    /// How many of one language's proper-noun numbers the encyclopedia answers with one entity,
    /// with several, and with nobody. The second and third are the size of the work this loader
    /// deliberately does not do, and they belong in the record beside what it did.
    /// </summary>
    private static async Task<NameAnswers> Answers(
        NpgsqlConnection connection,
        string numbers,
        string naming,
        CancellationToken cancellationToken,
        params (string Name, object Value)[] parameters)
    {
        var sql =
            $"""
             WITH proper AS ({numbers}),
             answered AS (
                 SELECT p.number, count(DISTINCT n.entity_id) AS entities
                 FROM proper p
                 LEFT JOIN ({naming}) n ON n.number = p.number
                 GROUP BY 1
             )
             SELECT count(*) FILTER (WHERE entities = 1),
                    count(*) FILTER (WHERE entities > 1),
                    count(*) FILTER (WHERE entities = 0)
             FROM answered
             """;

        await using var command = new NpgsqlCommand(sql, connection);
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        command.CommandTimeout = Patient;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);
        return new NameAnswers(
            (int)reader.GetInt64(0), (int)reader.GetInt64(1), (int)reader.GetInt64(2));
    }

    private static async Task<IReadOnlyList<(string Text, int Words)>> ByText(
        NpgsqlConnection connection,
        IDbContextTransaction transaction,
        CancellationToken cancellationToken)
    {
        var counts = new List<(string, int)>();
        await foreach (var row in Rows(connection, transaction,
                           "SELECT t.slug, count(*) FROM word_entity a JOIN word w ON w.id = a.word_id " +
                           "JOIN text t ON t.id = w.text_id GROUP BY 1 ORDER BY 2 DESC",
                           cancellationToken))
        {
            counts.Add((row.GetString(0), (int)row.GetInt64(1)));
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

    private static async IAsyncEnumerable<NpgsqlDataReader> Rows(
        NpgsqlConnection connection,
        IDbContextTransaction transaction,
        string sql,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken,
        params (string Name, object Value)[] parameters)
    {
        await using var command = new NpgsqlCommand(
            sql, connection, (NpgsqlTransaction)transaction.GetDbTransaction());
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        command.CommandTimeout = Patient;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            yield return reader;
        }
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
