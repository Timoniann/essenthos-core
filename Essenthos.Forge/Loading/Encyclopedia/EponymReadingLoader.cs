using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace Essenthos.Core.Loading.Encyclopedia;

/// <param name="Names">Names after <em>sons of</em>, given the man.</param>
/// <param name="Heads">Words for <em>sons</em> before them, given the people.</param>
/// <param name="Literal">Of the words for <em>sons</em>, the ones that are the man's own sons, named in the passage.</param>
/// <param name="ReadAsTheMan">Names standing alone that the reading of the sentence gives the man.</param>
/// <param name="ReadAsThePeople">Names standing alone that the reading gives the people.</param>
/// <param name="Unclear">Names the reading could not settle, which keep the interim answer.</param>
/// <param name="Constructs">
/// Names after a word for a tribe or a realm's word, which the owner's earlier rulings answer and
/// from which the other of the two is taken back.
/// </param>
/// <param name="Withdrawn">Weaker annotations naming the other of the two, taken back with what they carried.</param>
/// <param name="Written">Annotations written under this pass's two sources, the carried ones included.</param>
/// <param name="Outdated">
/// Annotations and claims this pass's rule wrote on an earlier load on a word it no longer answers so, taken back.
/// </param>
internal sealed record EponymReadingOutcome(
    bool AlreadyLoaded,
    int Names,
    int Heads,
    int Literal,
    int ReadAsTheMan,
    int ReadAsThePeople,
    int Unclear,
    int Constructs,
    int Withdrawn,
    int Written,
    IReadOnlyList<(string Text, int Words)> ByText,
    IReadOnlyList<(string Tribe, int Man, int People)> ByTribe,
    TimeSpan Elapsed,
    int Outdated = 0)
{
    public override string ToString() =>
        AlreadyLoaded
            ? "the man and the people a tribe's name stands for are already told apart"
            : $"{Names} names after 'sons of' are the man and {Heads} words for 'sons' the people ({Literal} are his " +
              $"own sons and name nobody); of the names standing alone the reading gives {ReadAsTheMan} the man and " +
              $"{ReadAsThePeople} the people, {Unclear} unclear, in {Elapsed}; {Constructs} names after a word for a " +
              $"tribe or a realm keep the owner's answer alone. {Withdrawn} weaker annotations taken " +
              $"back, {Outdated} written before that the rule no longer gives, {Written} words written in all. " +
              "Per tribe (man/people): " +
              string.Join(", ", ByTribe.Select(t => $"{t.Tribe} {t.Man}/{t.People}")) + ". Per text: " +
              string.Join(", ", ByText.Select(t => $"{t.Text} {t.Words}"));
}

/// <summary>One word of a tribe's name, read in its sentence for the man or the people.</summary>
/// <param name="Names">The record the sentence means, or null where the reading could not say.</param>
/// <param name="Instead">The other of the two, which a weaker annotation of the word may name.</param>
internal sealed record EponymReading(
    string Text,
    string Reference,
    int Position,
    string Surface,
    string? Names,
    string Instead,
    double Confidence,
    string Why)
{
    public RuledWord Word => new(Text, Reference, Position, Surface);
}

internal sealed record EponymReadings(string Model, string Read, IReadOnlyList<EponymReading> Readings);

/// <summary>
/// The man and the people a tribe's name stands for, told apart: <em>the sons of Israel</em> as the
/// people on <em>sons</em> and Jacob on <em>Israel</em>, and a name standing alone as whichever its
/// sentence means.
///
/// <para>
/// <strong>The owner's ruling of 2026-10-08.</strong> In <em>the sons of X</em>, where X is a tribe's
/// ancestor, the name is the man — Israel is Jacob — and the word <em>sons</em> is the people: בְּנֵי
/// in the Hebrew, υἱοί in the Greek, and through the links every translation's <em>children</em>,
/// <em>сыны</em> or <em>Söhne</em>; a translation that renders the two as one word,
/// <em>Israelites</em>, gets the people. Where they are his own sons, named in the passage — Genesis
/// 46, Exodus 1:1, 1 Chronicles 2:1 — <em>sons</em> names nobody. A name standing alone is read in its
/// sentence (<see cref="EponymReadings"/>, a model's reading with the man and the people as the two
/// candidates); where the reading could not say, the interim answer of <see cref="EponymNameLoader"/>
/// stands.
/// </para>
///
/// <para>
/// <strong>It outranks what is weaker than the owner's word, and nothing else.</strong> On every word
/// it answers, an annotation naming the other of the two — the interim rule, a reading made before
/// the tribes were records, a reading of the verse, the verses' consensus — is taken back with every
/// word it was carried to. Nothing a person, the owner or a source stated gives way, and a word
/// carrying such an answer is left as it is. So is the interim answer wherever a reading of the word
/// names the other of the two. After every pass that names a word, so nothing writes the other
/// answer back beside it, and before the verses are read off the words. Idempotent on its sources.
/// </para>
/// </summary>
internal sealed class EponymReadingLoader(AppDbContext db, ILogger<EponymReadingLoader> logger)
{
    public const string Source = Essenthos.Core.Corpus.Annotations.SonsOf;

    /// <summary>Whose reading of each sentence a name standing alone was given.</summary>
    public const string ReadingSource =
        "Essenthos, each sentence a tribe's name stands alone in read for the man or the people by gpt-6.1-sol " +
        "(Codex), 2026-10-08";

    /// <summary>Both sources, which a pass that runs before this one must not answer against.</summary>
    public static readonly string[] Sources = [Source, ReadingSource];

    /// <summary>
    /// The owner's earlier rulings by construct — after the word for a tribe the man, after a king, a
    /// land or a city the people — whose answer this pass keeps alone, so a pass that runs before this
    /// one must not put the other of the two beside it either.
    /// </summary>
    public static readonly string[] ConstructSources =
    [
        TribeNameLoader.Source, RealmNameLoader.Source, GreekTribeNameLoader.TribeSource,
        GreekTribeNameLoader.AntecedentSource, GreekTribeNameLoader.RealmSource,
    ];

    private const string Resource = "Essenthos.Core.Loading.Encyclopedia.EponymReadings.json";

    /// <summary>בֵּן in the construct plural, and υἱός.</summary>
    private const string HebrewSons = "H1121";

    private const string GreekSons = "G5207";

    private const double ByThePhrase = 0.9;

    /// <summary>
    /// The passages where <em>the sons of</em> a tribe's ancestor are his own sons, named there: Jacob's
    /// sons going down to Egypt, the genealogies of Exodus 6 and 1 Chronicles 2–7, the census of Numbers
    /// 26, which counts the tribes by the sons it names, and the two sons of Joseph whom Jacob blessed
    /// (Hebrews 11:21). Book, chapter, first and last verse, canonical; the Hebrew and the Greek alike.
    /// </summary>
    internal static readonly (int Book, int Chapter, int From, int To)[] HisOwnSons =
    [
        (1, 42, 5, 5), (1, 45, 21, 21), (1, 46, 5, 5), (1, 46, 8, 27), (1, 50, 25, 25),
        (2, 1, 1, 5), (2, 6, 14, 16), (2, 13, 19, 19),
        (4, 26, 5, 50),
        (13, 2, 1, 4), (13, 4, 1, 1), (13, 4, 24, 24), (13, 5, 1, 3), (13, 6, 1, 3), (13, 6, 16, 16),
        (13, 7, 1, 1), (13, 7, 13, 14), (13, 7, 20, 20), (13, 7, 30, 30), (13, 23, 6, 6),
        (58, 11, 21, 21),
    ];

    /// <summary>
    /// The people whose ancestor bears the Hebrew number <paramref name="number"/> and who either bears
    /// it too or bears no number at all — the tribe of Joseph has no word of its own — and that
    /// ancestor, where exactly one people is so.
    /// </summary>
    private static string Tribe(string number) =>
        $"""
         (SELECT CASE WHEN count(DISTINCT p.id) = 1 THEN min(p.id) END AS people_id,
                 CASE WHEN count(DISTINCT p.id) = 1 THEN min(p.origin_entity_id) END AS ancestor_id
          FROM entity p
          WHERE p.kind = 'people'
            AND EXISTS (SELECT 1 FROM entity_name an WHERE an.entity_id = p.origin_entity_id
                                                     AND an.hebrew_strong_number = {number})
            AND (EXISTS (SELECT 1 FROM entity_name pn WHERE pn.entity_id = p.id AND pn.hebrew_strong_number = {number})
                 OR NOT EXISTS (SELECT 1 FROM entity_name pn WHERE pn.entity_id = p.id
                                                              AND pn.hebrew_strong_number IS NOT NULL)))
         """;

    /// <summary>What every step below works from: the word, the answer it gets, and the other of the two.</summary>
    private const string Targets =
        """
        CREATE TEMP TABLE eponym_target (
            word_id bigint PRIMARY KEY,
            entity_id integer NOT NULL,
            other_id integer NOT NULL,
            role text NOT NULL,
            confidence double precision NOT NULL,
            note text NOT NULL)
        ON COMMIT DROP
        """;

    private static readonly string HebrewSonsOf =
        $"""
         INSERT INTO eponym_target (word_id, entity_id, other_id, role, confidence, note)
         SELECT x.id, x.id_of, x.other, x.role, @confidence, x.note
         FROM (
             SELECT w.id, tribe.ancestor_id AS id_of, tribe.people_id AS other, 'name' AS role,
                    w.strong_number || ', after sons of, so the man the tribe is named after' AS note
             FROM word w
             JOIN text t ON t.id = w.text_id AND t.slug = @witness
             JOIN word sons ON sons.verse_id = w.verse_id AND sons.position = w.position - 1
                  AND sons.strong_number = '{HebrewSons}'
                  AND sons.morphology ->> 'state' = 'c' AND sons.morphology ->> 'number' = 'pl'
             CROSS JOIN LATERAL {Tribe("w.strong_number")} tribe
             WHERE w.morphology ->> 'pos' = 'nmpr' AND tribe.people_id IS NOT NULL
             UNION ALL
             SELECT sons.id, tribe.people_id, tribe.ancestor_id, 'head',
                    '{HebrewSons}, sons of ' || w.strong_number || ', so the people of that name'
             FROM word w
             JOIN text t ON t.id = w.text_id AND t.slug = @witness
             JOIN word sons ON sons.verse_id = w.verse_id AND sons.position = w.position - 1
                  AND sons.strong_number = '{HebrewSons}'
                  AND sons.morphology ->> 'state' = 'c' AND sons.morphology ->> 'number' = 'pl'
             CROSS JOIN LATERAL {Tribe("w.strong_number")} tribe
             WHERE w.morphology ->> 'pos' = 'nmpr' AND tribe.people_id IS NOT NULL
               AND NOT {OwnSons("w.verse_id")}) x
         ON CONFLICT (word_id) DO NOTHING
         """;

    /// <summary>
    /// The same in the Greek witnesses: a name of Hebrew origin after υἱοί, an article allowed between,
    /// whose ancestor and people are the Hebrew name's, and υἱοί naming nobody where they are his own sons.
    /// </summary>
    private static readonly string GreekSonsOf =
        $"""
         INSERT INTO eponym_target (word_id, entity_id, other_id, role, confidence, note)
         WITH after AS MATERIALIZED (
             SELECT w.id, w.strong_number, w.verse_id, sons.id AS sons_id
             FROM word w
             JOIN text t ON t.id = w.text_id AND t.slug = ANY(@greek)
             JOIN word sons ON sons.verse_id = w.verse_id AND sons.strong_number = '{GreekSons}'
                  AND coalesce(sons.morphology ->> 'form', sons.morphology ->> 'robinson') ~ '^N-.P'
                  AND (sons.position = w.position - 1
                       OR (sons.position = w.position - 2
                           AND EXISTS (SELECT 1 FROM word article WHERE article.verse_id = w.verse_id
                                         AND article.position = w.position - 1 AND article.strong_number = 'G3588')))
             WHERE w.strong_number IS NOT NULL
         ),
         answered AS MATERIALIZED (
             SELECT a.*, tribe.people_id, tribe.ancestor_id
             FROM after a
             CROSS JOIN LATERAL (SELECT {GreekTribeNameLoader.Hebrew("a.strong_number")} AS number) hebrew
             CROSS JOIN LATERAL {Tribe("hebrew.number")} tribe
             WHERE hebrew.number IS NOT NULL AND tribe.people_id IS NOT NULL
         )
         SELECT id, ancestor_id, people_id, 'name', @confidence,
                strong_number || ', after sons of, so the man the tribe is named after'
         FROM answered
         UNION ALL
         SELECT sons_id, people_id, ancestor_id, 'head', @confidence,
                '{GreekSons}, sons of ' || strong_number || ', so the people of that name'
         FROM answered
         WHERE NOT {OwnSons("answered.verse_id")}
         ON CONFLICT (word_id) DO NOTHING
         """;

    /// <summary>Whether the verse is one of <see cref="HisOwnSons"/>.</summary>
    private static string OwnSons(string verse) =>
        $"""
         EXISTS (
             SELECT 1 FROM verse_reference r
             JOIN unnest(@books, @chapters, @froms, @tos) AS own(b, c, f, l)
                  ON own.b = r.canonical_book AND own.c = r.canonical_chapter
                 AND r.canonical_verse BETWEEN own.f AND own.l
             WHERE r.verse_id = {verse} AND r.is_primary)
         """;

    /// <summary>
    /// What this pass's rule wrote on an earlier load on a word it no longer answers so — the row or the
    /// claim, on the word itself or carried from it — as the rule stands now: a passage added to
    /// <see cref="HisOwnSons"/>, or a tribe whose records changed. A load from nothing would not write
    /// them, so a load over a corpus that holds them takes them back.
    /// </summary>
    private const string Outdated =
        """
        CREATE TEMP TABLE eponym_outdated ON COMMIT DROP AS
        SELECT a.id AS row_id, NULL::bigint AS claim_id
        FROM word_entity a
        WHERE a.source = @source
          AND NOT EXISTS (SELECT 1 FROM eponym_target x
                          WHERE x.role IN ('head', 'name') AND x.entity_id = a.entity_id
                            AND x.word_id = coalesce(substring(a.note FROM '^through \S+ word ([0-9]+)')::bigint, a.word_id))
        UNION ALL
        SELECT NULL, c.id
        FROM word_entity_claim c
        JOIN word_entity a ON a.id = c.word_entity_id
        WHERE c.source = @source AND a.source <> @source
          AND NOT EXISTS (SELECT 1 FROM eponym_target x
                          WHERE x.role IN ('head', 'name') AND x.entity_id = a.entity_id
                            AND x.word_id = coalesce(substring(c.note FROM '^through \S+ word ([0-9]+)')::bigint, a.word_id))
        """;

    private const string WithdrawOutdated =
        """
        DELETE FROM word_entity_claim c USING eponym_outdated o WHERE c.id = o.claim_id;
        DELETE FROM word_entity a USING eponym_outdated o WHERE a.id = o.row_id
        """;

    private const string Readings =
        """
        INSERT INTO eponym_target (word_id, entity_id, other_id, role, confidence, note)
        SELECT x.word_id, answer.id, other.id, 'reading', x.confidence, x.why
        FROM unnest(@words, @names, @instead, @confidences, @whys) AS x(word_id, names, instead, confidence, why)
        JOIN entity answer ON answer.slug = x.names
        JOIN entity other ON other.slug = x.instead
        ON CONFLICT (word_id) DO NOTHING
        """;

    /// <summary>
    /// The names the owner's earlier rulings answer by their construct — after the word for a tribe the
    /// man, after a king, a land or a city the people — so the other of the two a reading or the verses'
    /// consensus put beside the answer is taken back like any other.
    /// </summary>
    private const string Constructs =
        """
        INSERT INTO eponym_target (word_id, entity_id, other_id, role, confidence, note)
        SELECT DISTINCT ON (a.word_id) a.word_id, a.entity_id, other.id, 'construct', 1.0, ''
        FROM word_entity a
        JOIN word_entity_claim c ON c.word_entity_id = a.id AND c.source = ANY(@constructs)
        JOIN entity e ON e.id = a.entity_id
        JOIN entity other ON (e.kind = 'person' AND other.kind = 'people' AND other.origin_entity_id = e.id)
                          OR (e.kind = 'people' AND other.kind = 'person' AND e.origin_entity_id = other.id)
        WHERE coalesce(a.note, '') NOT LIKE 'through %'
        ORDER BY a.word_id, other.id
        ON CONFLICT (word_id) DO NOTHING
        """;

    /// <summary>
    /// Whether annotation <c>a</c> carries anything a person, the project owner or a source stated of the
    /// word, on itself or in any claim standing on it — the interim rule aside, which this pass answers
    /// above, and a source's testimony that the verse names the record, which is not about the word.
    /// </summary>
    private const string Stated =
        """
        (a.method IN ('manual', 'stated-by-source')
         OR (a.source LIKE '%owner%' AND a.source NOT IN (@interim, @former))
         OR EXISTS (SELECT 1 FROM word_entity_claim c
                    WHERE c.word_entity_id = a.id
                      AND (c.method = 'manual'
                           OR (c.method <> 'stated-by-source' AND c.source LIKE '%owner%'
                               AND c.source NOT IN (@interim, @former)))))
        """;

    /// <summary>
    /// A word something stated names otherwise than this pass would is left as it is: its answer is not
    /// this pass's to change, and a second one beside it would be decided by standing rather than read.
    /// </summary>
    private static readonly string Yield =
        $"""
         DELETE FROM eponym_target x
         WHERE EXISTS (SELECT 1 FROM word_entity a
                       WHERE a.word_id = x.word_id AND a.entity_id <> x.entity_id AND {Stated})
         """;

    /// <summary>
    /// The weaker annotations naming the other of the two on a word this pass answers, and — where a
    /// reading of the word names the other — the interim rule's answer, or the verses' consensus, on a
    /// word it does not.
    /// </summary>
    private static readonly string Losers =
        $"""
         CREATE TEMP TABLE eponym_loser ON COMMIT DROP AS
         SELECT a.id, a.word_id, a.entity_id
         FROM eponym_target x
         JOIN word_entity a ON a.word_id = x.word_id AND a.entity_id = x.other_id
         WHERE NOT {Stated}
         UNION
         SELECT a.id, a.word_id, a.entity_id
         FROM word_entity a
         JOIN entity e ON e.id = a.entity_id
         WHERE a.source IN (@interim, @former, @consensus) AND coalesce(a.note, '') NOT LIKE 'through %'
           AND NOT {Stated}
           AND NOT EXISTS (SELECT 1 FROM eponym_target x WHERE x.word_id = a.word_id)
           AND EXISTS (SELECT 1 FROM word_entity reading
                       JOIN entity r ON r.id = reading.entity_id
                       WHERE reading.word_id = a.word_id AND reading.id <> a.id
                         AND reading.source NOT IN (@interim, @former, @consensus)
                         AND coalesce(reading.note, '') NOT LIKE 'through %'
                         AND reading.method IN ('model-reading', 'manual', 'stated-by-source')
                         AND ((e.kind = 'person' AND r.kind = 'people' AND r.origin_entity_id = e.id)
                              OR (e.kind = 'people' AND r.kind = 'person' AND e.origin_entity_id = r.id)))
         """;

    private const string WithdrawCarried =
        """
        DELETE FROM word_entity a
        WHERE a.entity_id IN (SELECT DISTINCT entity_id FROM eponym_loser)
          AND a.note LIKE 'through %'
          AND (substring(a.note FROM '^through \S+ word ([0-9]+)')::bigint, a.entity_id)
              IN (SELECT word_id, entity_id FROM eponym_loser)
        """;

    private const string WithdrawLosers = "DELETE FROM word_entity a USING eponym_loser l WHERE a.id = l.id";

    private const string Seed =
        """
        INSERT INTO pending_annotation (word_id, entity_id, confidence, corroborated, note)
        SELECT x.word_id, x.entity_id, x.confidence, FALSE, x.note
        FROM eponym_target x
        WHERE x.role = ANY(@roles)
        """;

    /// <summary>
    /// A word the links reached that already names the other of the two keeps it: a translation that
    /// writes <em>Israelites</em> for <em>the sons of Israel</em> in one word has the people from
    /// <em>sons</em>, and Jacob carried from <em>Israel</em> onto the same word would stand beside it.
    /// </summary>
    private const string KeepTheOther =
        """
        DELETE FROM pending_annotation p
        USING eponym_target x
        WHERE x.word_id = coalesce(p.through, p.word_id)
          AND EXISTS (SELECT 1 FROM word_entity a WHERE a.word_id = p.word_id AND a.entity_id = x.other_id)
        """;

    private const string ByTribeOf =
        """
        SELECT CASE WHEN e.kind = 'person' THEN e.slug ELSE origin.slug END,
               count(*) FILTER (WHERE e.kind = 'person'), count(*) FILTER (WHERE e.kind = 'people')
        FROM eponym_target x
        JOIN entity e ON e.id = x.entity_id
        LEFT JOIN entity origin ON origin.id = e.origin_entity_id
        GROUP BY 1 ORDER BY 1
        """;

    public Task<EponymReadingOutcome> Load(CancellationToken cancellationToken = default) =>
        Load(Embedded(), cancellationToken);

    internal async Task<EponymReadingOutcome> Load(EponymReadings file, CancellationToken cancellationToken = default)
    {
        var started = Stopwatch.StartNew();
        await db.Database.OpenConnectionAsync(cancellationToken);
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();

        var held = await db.Texts.Select(t => t.Slug).ToListAsync(cancellationToken);
        var read = file.Readings.Where(r => r.Names is not null && held.Contains(r.Text)).ToList();
        var words = await RuledWords.Resolve(connection, read.Select(r => r.Word), cancellationToken);
        if (await db.WordEntities.AnyAsync(a => a.Source == Source, cancellationToken)
            && await Applied(connection, read, words, cancellationToken)
            && await NothingOutdated(connection, cancellationToken))
        {
            logger.LogInformation("The man and the people a tribe's name stands for are already told apart; nothing to do");
            return new EponymReadingOutcome(true, 0, 0, 0, 0, 0, 0, 0, 0, 0, [], [], TimeSpan.Zero);
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await RuleTargets(connection, transaction, cancellationToken);
        var outdated = await Count(connection, transaction, "SELECT count(*) FROM eponym_outdated", cancellationToken);
        await Annotating.Run(connection, transaction, WithdrawOutdated, cancellationToken);
        await Annotating.Run(connection, transaction, Readings, cancellationToken,
            ("words", read.Select(r => words[r.Word]).ToArray()), ("names", read.Select(r => r.Names!).ToArray()),
            ("instead", read.Select(r => r.Instead).ToArray()),
            ("confidences", read.Select(r => Math.Clamp(r.Confidence, 0, 1)).ToArray()),
            ("whys", read.Select(r => r.Why).ToArray()));

        await Annotating.Run(connection, transaction, Constructs, cancellationToken,
            ("constructs", ConstructSources));

        var sources = new (string, object?)[]
        {
            ("interim", EponymNameLoader.Source), ("former", EponymNameLoader.FormerSource),
            ("consensus", NameConsensusPass.Source),
        };
        await Annotating.Run(connection, transaction, Yield, cancellationToken, sources);
        await Annotating.Run(connection, transaction, Losers, cancellationToken, sources);
        await Annotating.Run(connection, transaction, WithdrawCarried, cancellationToken);
        var withdrawn = await Count(connection, transaction, "SELECT count(*) FROM eponym_loser", cancellationToken);
        await Annotating.Run(connection, transaction, WithdrawLosers, cancellationToken);

        var rule = EnumSpelling.Of(LinkMethod.RuleBased);
        await Write(connection, transaction, ["head"], rule, Source, cancellationToken);
        await Write(connection, transaction, ["name"], rule, Source, cancellationToken);
        await Write(connection, transaction, ["reading"], EnumSpelling.Of(LinkMethod.ModelReading), ReadingSource,
            cancellationToken);

        var names = await Count(connection, transaction, "SELECT count(*) FROM eponym_target WHERE role = 'name'", cancellationToken);
        var heads = await Count(connection, transaction, "SELECT count(*) FROM eponym_target WHERE role = 'head'", cancellationToken);
        var people = await Count(connection, transaction,
            "SELECT count(*) FROM eponym_target x JOIN entity e ON e.id = x.entity_id WHERE x.role = 'reading' AND e.kind = 'people'",
            cancellationToken);
        var man = await Count(connection, transaction, "SELECT count(*) FROM eponym_target WHERE role = 'reading'", cancellationToken) - people;
        var constructs = await Count(connection, transaction, "SELECT count(*) FROM eponym_target WHERE role = 'construct'", cancellationToken);
        var byTribe = await Tribes(connection, transaction, cancellationToken);
        var byText = (await Annotating.ByText(connection, transaction, Source, cancellationToken))
            .Concat(await Annotating.ByText(connection, transaction, ReadingSource, cancellationToken))
            .GroupBy(t => t.Text).Select(g => (g.Key, g.Sum(t => t.Words))).OrderByDescending(t => t.Item2).ToList();
        await transaction.CommitAsync(cancellationToken);

        var outcome = new EponymReadingOutcome(
            false, names, heads, names - heads, man, people, file.Readings.Count(r => r.Names is null), constructs, withdrawn,
            byText.Sum(t => t.Item2), byText, byTribe, started.Elapsed, outdated);
        logger.LogInformation("Told the man from the people a tribe's name stands for: {Outcome}", outcome);
        return outcome;
    }

    /// <summary>
    /// Whether every reading already stands on its word under this pass's reading, so a reading added
    /// to the file after a load is applied by the next one.
    /// </summary>
    private static async Task<bool> Applied(
        NpgsqlConnection connection,
        IReadOnlyList<EponymReading> read,
        IReadOnlyDictionary<RuledWord, long> words,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            """
            SELECT count(*)
            FROM unnest(@words, @names) AS x(word_id, names)
            WHERE NOT EXISTS (SELECT 1 FROM word_entity a
                              JOIN entity e ON e.id = a.entity_id AND e.slug = x.names
                              JOIN word_entity_claim c ON c.word_entity_id = a.id AND c.source = @source
                              WHERE a.word_id = x.word_id)
              AND NOT EXISTS (SELECT 1 FROM word_entity ruled
                              JOIN entity e ON e.id = ruled.entity_id AND e.slug <> x.names
                              WHERE ruled.word_id = x.word_id AND ruled.method IN ('manual', 'stated-by-source'))
            """, connection);
        command.Parameters.AddWithValue("words", read.Select(r => words[r.Word]).ToArray());
        command.Parameters.AddWithValue("names", read.Select(r => r.Names!).ToArray());
        command.Parameters.AddWithValue("source", ReadingSource);
        return (long)(await command.ExecuteScalarAsync(cancellationToken))! == 0;
    }

    /// <summary>
    /// The words the rule answers by <em>sons of</em>, in the Hebrew and the Greek, and what this pass
    /// wrote on an earlier load that the rule no longer gives (<see cref="Outdated"/>).
    /// </summary>
    private static async Task RuleTargets(
        NpgsqlConnection connection,
        IDbContextTransaction transaction,
        CancellationToken cancellationToken)
    {
        (string, object?)[] ownSons =
        [
            ("books", HisOwnSons.Select(s => s.Book).ToArray()), ("chapters", HisOwnSons.Select(s => s.Chapter).ToArray()),
            ("froms", HisOwnSons.Select(s => s.From).ToArray()), ("tos", HisOwnSons.Select(s => s.To).ToArray()),
        ];
        await Annotating.Run(connection, transaction, Targets, cancellationToken);
        await Annotating.Run(connection, transaction, HebrewSonsOf, cancellationToken,
            [("witness", EntityCandidates.Witness), ("confidence", ByThePhrase), .. ownSons]);
        await Annotating.Run(connection, transaction, GreekSonsOf, cancellationToken,
            [("greek", FixedTitles.GreekWitnesses.ToArray()), ("confidence", ByThePhrase), .. ownSons]);
        await Annotating.Run(connection, transaction, Outdated, cancellationToken, ("source", Source));
    }

    /// <summary>Whether nothing this pass wrote before is what its rule no longer gives; asked, and rolled back.</summary>
    private async Task<bool> NothingOutdated(NpgsqlConnection connection, CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await RuleTargets(connection, transaction, cancellationToken);
        var outdated = await Count(connection, transaction, "SELECT count(*) FROM eponym_outdated", cancellationToken);
        await transaction.RollbackAsync(cancellationToken);
        return outdated == 0;
    }

    private static async Task Write(
        NpgsqlConnection connection,
        IDbContextTransaction transaction,
        string[] roles,
        string method,
        string source,
        CancellationToken cancellationToken)
    {
        await Annotating.Run(connection, transaction, "DROP TABLE IF EXISTS pending_annotation", cancellationToken);
        await Annotating.Run(connection, transaction, Annotating.Workspace, cancellationToken);
        await Annotating.Run(connection, transaction, Seed, cancellationToken, ("roles", roles));
        await Annotating.CarryAcrossLinks(connection, transaction, cancellationToken);
        await Annotating.Run(connection, transaction, KeepTheOther, cancellationToken);
        await Annotating.Run(connection, transaction, Annotating.Settle, cancellationToken,
            ("method", method), ("source", source));
        await Annotating.Run(connection, transaction, Annotating.Claim, cancellationToken,
            ("method", method), ("source", source));
    }

    private static async Task<int> Count(
        NpgsqlConnection connection,
        IDbContextTransaction transaction,
        string sql,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(sql, connection, (NpgsqlTransaction)transaction.GetDbTransaction());
        return (int)(long)(await command.ExecuteScalarAsync(cancellationToken))!;
    }

    private static async Task<IReadOnlyList<(string, int, int)>> Tribes(
        NpgsqlConnection connection,
        IDbContextTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(ByTribeOf, connection, (NpgsqlTransaction)transaction.GetDbTransaction());
        var tribes = new List<(string, int, int)>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            tribes.Add((reader.GetString(0), (int)reader.GetInt64(1), (int)reader.GetInt64(2)));
        }

        return tribes;
    }

    internal static EponymReadings Embedded()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(Resource)
                           ?? throw new FileNotFoundException(
                               $"The embedded resource \"{Resource}\" is not in this assembly. It is added by the " +
                               "EmbeddedResource item in Essenthos.Forge.csproj.", Resource);
        return JsonSerializer.Deserialize<EponymReadings>(stream, new JsonSerializerOptions
               {
                   PropertyNameCaseInsensitive = true,
                   PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
               })
               ?? throw new InvalidDataException($"The embedded resource \"{Resource}\" is empty.");
    }
}
