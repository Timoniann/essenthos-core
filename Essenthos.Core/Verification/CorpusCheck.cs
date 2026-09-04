using System.Globalization;
using System.Text.Json;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Essenthos.Core.Verification;

/// <summary>
/// Measures the corpus against itself, on every load, and stores what it found.
///
/// A platform whose claim is citability should publish its own coverage rather than wait to be
/// asked. Everything here was measured by hand over a week of work, one query at a time, and every
/// one of those numbers takes seconds — so the work was not the measuring, it was remembering to
/// measure, which is what this removes.
///
/// The measures answer different questions and none of them substitutes for another. Coverage is
/// the forward view: of the words in a text, how many say anything. Reach is the same question from
/// the other side, and it is the one nobody asks — a corpus can link 90% of a translation's words
/// to 40% of the witness's, and the forward number hides that entirely. Contention counts words
/// claimed more than once, which is how a heuristic mapping announces itself. Pairing asks whether
/// the two texts were laid against each other correctly at all, which every other measure assumes.
/// Integrity is the only measure with a right answer, and it is zero.
///
/// Coverage is reported per section rather than per text, because a text is not one thing: the King
/// James renders 94% of the Hebrew and 77% of the Greek, and one number for it describes neither
/// half. And it covers every text with links rather than the translations alone, because the
/// worst-covered text in this corpus is a printed edition and a headline share computed without it
/// is a headline about a subset.
/// </summary>
internal sealed class CorpusCheck(AppDbContext db, ILogger<CorpusCheck> logger)
{
    /// <summary>
    /// Where the corpus already stands, less a margin. A floor is for catching a load that lost
    /// something, so it is set below today's number rather than above it — an aspiration in this
    /// position fails every build until somebody raises it, and then it is ignored.
    /// </summary>
    public const double RenderedFloor = 0.80;

    /// <summary>
    /// Hebrew prefixes and the object marker. A translation renders these inside the word they
    /// attach to, so counting them as words a translation failed to reach would report a fact about
    /// Hebrew as though it were a defect in the corpus.
    /// </summary>
    private const string StructuralMorphemes = "(strong_number LIKE 'H9%' OR strong_number = 'H853')";

    /// <summary>The last book of each half of the canon, which is where a section ends.</summary>
    private const int LastOldTestamentBook = 39;

    private const int LastNewTestamentBook = 66;

    /// <summary>
    /// Walks the links rather than the words: a lateral lookup per word would be two million of
    /// them, and the answer is the same.
    ///
    /// Whether a word was ever promised anything is asked of its verse and not of its text. The
    /// Septuagint's deuterocanon has no Hebrew counterpart at all and its Daniel 3 has sixty-five
    /// verses BHSA does not hold, and counting those as words the corpus failed to reach would
    /// report the shape of the canon as a defect in the alignment.
    ///
    /// Every text linked to a witness is counted, and every translation whether or not it is —
    /// unfinished alignment work is a fact worth reporting, and it is the <c>unpaired</c> column
    /// that reports it.
    /// </summary>
    private static readonly string CoverageSql =
        $"""
        WITH witness AS (
            SELECT DISTINCT l.from_text_id, l.to_text_id
            FROM link l
            JOIN text other ON other.id = l.to_text_id AND other.kind <> 'translation'
        ),
        placed AS (
            SELECT v.id AS verse_id, v.text_id, r.canonical_book AS book,
                   r.canonical_chapter AS chapter, r.canonical_verse AS verse
            FROM verse v
            JOIN verse_reference r ON r.verse_id = v.id AND r.is_primary
        ),
        paired AS (
            SELECT DISTINCT here.verse_id
            FROM placed here
            JOIN witness w ON w.from_text_id = here.text_id
            JOIN placed there ON there.text_id = w.to_text_id
                AND (there.book, there.chapter, there.verse) = (here.book, here.chapter, here.verse)
        ),
        claimed AS (
            SELECT lw.word_id,
                   bool_or(l.relation IN ('renders', 'equals')) AS rendered,
                   bool_or(l.relation IN ('omits', 'expands', 'transposes')) AS absent
            FROM link_word lw
            JOIN link l ON l.id = lw.link_id
            JOIN text other ON other.id = l.to_text_id AND other.kind <> 'translation'
            WHERE lw.side = 'from'
            GROUP BY lw.word_id
        )
        SELECT t.slug,
               CASE
                   WHEN p.book <= {LastOldTestamentBook} THEN 'old testament'
                   WHEN p.book <= {LastNewTestamentBook} THEN 'new testament'
                   ELSE 'deuterocanon'
               END,
               count(*),
               count(*) FILTER (WHERE c.rendered),
               count(*) FILTER (WHERE c.rendered IS NOT TRUE AND c.absent),
               count(*) FILTER (WHERE c.word_id IS NULL AND pv.verse_id IS NOT NULL),
               count(*) FILTER (WHERE c.word_id IS NULL AND pv.verse_id IS NULL)
        FROM word w
        JOIN text t ON t.id = w.text_id
        JOIN placed p ON p.verse_id = w.verse_id
        LEFT JOIN claimed c ON c.word_id = w.id
        LEFT JOIN paired pv ON pv.verse_id = w.verse_id
        WHERE t.kind = 'translation'
           OR EXISTS (SELECT 1 FROM witness x WHERE x.from_text_id = w.text_id)
        GROUP BY t.slug, 2
        ORDER BY t.slug, 2
        """;

    /// <summary>
    /// A verse pair whose links are all faint, which is what a wrong pairing looks like from
    /// underneath. The links themselves are individually unremarkable and the verse as a whole is
    /// not: Leviticus 11:15 in Brenton is Masoretic 11:16, so every link in it names the wrong
    /// Hebrew word, and its mean confidence is the only thing in the corpus that says so.
    /// </summary>
    private const double WeakVerse = 0.5;

    /// <summary>
    /// How many links a verse pair needs before its mean is worth reading. One faint link is a
    /// faint link; six of them and nothing else is a verse laid against the wrong verse.
    /// </summary>
    private const int EnoughLinks = 3;

    /// <summary>How many of the weakest are named. A reader checks a few and infers the rest.</summary>
    private const int WorstNamed = 12;

    /// <summary>The threshold as SQL reads it, which is not as a Ukrainian locale writes it.</summary>
    private static readonly string Weak = WeakVerse.ToString(CultureInfo.InvariantCulture);

    /// <summary>
    /// Whether the two texts were laid against each other correctly, which every other measure
    /// takes for granted. Verses are paired by canonical address alone, so a chapter the two divide
    /// differently produces links that are wrong in a way no word-level check can see.
    ///
    /// Two signals, and they catch different failures. A chapter where the two hold a different
    /// number of verses is visible without looking at a single link. Where the counts agree and the
    /// division does not, nothing is visibly wrong and the alignment quietly collapses, which the
    /// mean confidence of the verse reports.
    /// </summary>
    private static readonly string PairingSql =
        $"""
        WITH placed AS (
            SELECT v.id AS verse_id, v.text_id, r.canonical_book AS book,
                   r.canonical_chapter AS chapter, r.canonical_verse AS verse
            FROM verse v
            JOIN verse_reference r ON r.verse_id = v.id AND r.is_primary
        ),
        size AS (
            SELECT text_id, book, chapter, count(*) AS verses FROM placed GROUP BY 1, 2, 3
        ),
        pairs AS (SELECT DISTINCT from_text_id, to_text_id FROM link),
        strength AS (
            SELECT l.from_text_id, l.to_text_id, w.verse_id, avg(l.confidence) AS mean, count(*) AS links
            FROM link l
            JOIN link_word lw ON lw.link_id = l.id AND lw.side = 'from'
            JOIN word w ON w.id = lw.word_id
            WHERE l.confidence IS NOT NULL
            GROUP BY 1, 2, 3
        )
        SELECT f.slug,
               t.slug,
               count(DISTINCT (here.book, here.chapter)) FILTER (WHERE there.text_id IS NOT NULL),
               count(DISTINCT (here.book, here.chapter)) FILTER (WHERE here.verses <> there.verses),
               (SELECT count(*) FROM strength s
                 WHERE s.from_text_id = p.from_text_id AND s.to_text_id = p.to_text_id),
               (SELECT count(*) FROM strength s
                 WHERE s.from_text_id = p.from_text_id AND s.to_text_id = p.to_text_id
                   AND s.links >= {EnoughLinks} AND s.mean < {Weak}),
               (SELECT coalesce(array_agg(name ORDER BY mean), ARRAY[]::text[]) FROM (
                    SELECT b.name || ' ' || v.chapter_number || ':' || v.number AS name, s.mean
                    FROM strength s
                    JOIN verse v ON v.id = s.verse_id
                    JOIN book b ON b.id = v.book_id
                    WHERE s.from_text_id = p.from_text_id AND s.to_text_id = p.to_text_id
                      AND s.links >= {EnoughLinks} AND s.mean < {Weak}
                    ORDER BY s.mean
                    LIMIT {WorstNamed}) worst)
        FROM pairs p
        JOIN text f ON f.id = p.from_text_id
        JOIN text t ON t.id = p.to_text_id
        LEFT JOIN size here ON here.text_id = p.from_text_id
        LEFT JOIN size there ON there.text_id = p.to_text_id
            AND (there.book, there.chapter) = (here.book, here.chapter)
        GROUP BY f.slug, t.slug, p.from_text_id, p.to_text_id
        ORDER BY f.slug, t.slug
        """;

    /// <summary>
    /// The direction nobody measures. A corpus can name 90% of a translation's words and still
    /// leave more than half the witness untouched, and the forward count hides that completely.
    ///
    /// <para>
    /// **Split by whether a source said it.** Read as one column this table ranks translations by
    /// quality, and what it actually ranks them by is how much testimony each has: the Berean's
    /// 88.9% into the Greek is its publisher's own word tables and the Ukrainian's 77.3% is a
    /// model. Scored against their own stated pairs, the same aligner is 91.4% on the Berean and
    /// 92.8% on the Ukrainian — a point better on the text the single column puts eleven points
    /// lower. One number invited that reading and it was taken.
    /// </para>
    /// </summary>
    private const string ReachSql =
        $"""
        SELECT witness.slug,
               source.slug,
               (SELECT count(*) FROM word lex
                WHERE lex.text_id = witness.id AND NOT {StructuralMorphemes}),
               count(DISTINCT lw.word_id),
               count(DISTINCT lw.word_id) FILTER (WHERE l.method = 'stated-by-source')
        FROM link l
        JOIN text source ON source.id = l.from_text_id AND source.kind = 'translation'
        JOIN text witness ON witness.id = l.to_text_id AND witness.kind <> 'translation'
        JOIN link_word lw ON lw.link_id = l.id AND lw.side = 'to'
        JOIN word w ON w.id = lw.word_id AND NOT {StructuralMorphemes}
        GROUP BY witness.slug, witness.id, source.slug
        ORDER BY witness.slug, source.slug
        """;

    /// <summary>
    /// A word may legitimately be named by two links — the Synodal writes <em>по роду</em> where
    /// Hebrew writes one word — so this is not an error count. It is a number that moves sharply
    /// when a mapping starts guessing, which is how the heuristic New Testament mapping was found.
    /// </summary>
    /// <summary>
    /// A word given more than one counterpart **by one source**, and a word two sources answer
    /// differently. They were one number until a second source could disagree, and then it read
    /// 18,086 for a pair that had read 0 the morning before — not because anything broke, but
    /// because Clear Bible differs from the Berean's own tables about 8,310 Greek words and the
    /// corpus deliberately keeps both answers (FTR-0186).
    ///
    /// The two are not the same fact. One source claiming a word twice is a defect in that source's
    /// load and should be zero. Two sources claiming it differently is a disagreement between people
    /// who both looked, and is the most interesting row in the corpus. Counted together, the second
    /// hides the first: a pair with real duplication and a pair with rich disagreement report the
    /// same number. PRB-0198.
    /// </summary>
    private const string ContentionSql =
        """
        WITH claimed AS (
            SELECT lw.word_id, l.from_text_id, l.to_text_id, c.source, count(DISTINCT lw.link_id) AS links
            FROM link_word lw
            JOIN link l ON l.id = lw.link_id
            JOIN link_claim c ON c.link_id = l.id
            WHERE lw.side = 'from'
            GROUP BY lw.word_id, l.from_text_id, l.to_text_id, c.source
        ),
        perWord AS (
            SELECT word_id, from_text_id, to_text_id,
                   max(links) AS most_by_one_source,
                   count(*) AS sources
            FROM claimed
            GROUP BY word_id, from_text_id, to_text_id
        )
        SELECT t.slug, against.slug,
               count(*) FILTER (WHERE most_by_one_source > 1),
               coalesce(max(most_by_one_source), 0),
               count(*) FILTER (WHERE sources > 1 AND most_by_one_source = 1)
        FROM perWord
        JOIN text t ON t.id = perWord.from_text_id
        JOIN text against ON against.id = perWord.to_text_id
        GROUP BY t.slug, against.slug
        ORDER BY t.slug, against.slug
        """;

    /// <summary>
    /// The same question as contention asked from the other end, and the one a reader actually
    /// feels: not "how many words does this word claim" but "how many words claim this one". A
    /// witness word claimed by five words of a translation is five words that light together, and
    /// no forward count says so.
    /// </summary>
    private const string CrowdingSql =
        """
        SELECT t.slug, witness.slug, count(*) FILTER (WHERE claims.n > 2), coalesce(max(claims.n), 0)
        FROM (
            SELECT lw.word_id, l.from_text_id, l.to_text_id, count(*) AS n
            FROM link_word lw
            JOIN link l ON l.id = lw.link_id
            WHERE lw.side = 'to'
            GROUP BY lw.word_id, l.from_text_id, l.to_text_id
        ) claims
        JOIN text t ON t.id = claims.from_text_id
        JOIN text witness ON witness.id = claims.to_text_id AND witness.kind <> 'translation'
        GROUP BY t.slug, witness.slug
        ORDER BY t.slug, witness.slug
        """;

    /// <summary>
    /// Each of these should return nothing. They are the shapes the schema cannot forbid but that
    /// no correct load produces, so a count above zero is a defect and not a measurement.
    /// </summary>
    private static readonly (string Breaks, string Sql)[] Integrity =
    [
        ("verses with no canonical reference",
            """
            SELECT count(*) FROM verse v
            WHERE NOT EXISTS (SELECT 1 FROM verse_reference r WHERE r.verse_id = v.id AND r.is_primary)
            """),
        ("Strong numbers that are not a letter and digits",
            "SELECT count(*) FROM word WHERE strong_number IS NOT NULL AND strong_number !~ '^[GH][0-9]+$'"),
        // A number that resolves to nothing is a word the corpus cannot explain. The H9000 range is
        // excluded because ETCBC numbers prefix morphemes there and Strong never catalogued them —
        // 121,077 words carry one, and counting those as broken would misreport the corpus by 21%.
        ("Strong numbers no dictionary entry answers",
            """
            SELECT count(*) FROM word w
            WHERE w.strong_number IS NOT NULL
              AND w.strong_number !~ '^H9[0-9]{3}$'
              AND EXISTS (SELECT 1 FROM strong_entry)
              AND NOT EXISTS (SELECT 1 FROM strong_entry e WHERE e.strong_number = w.strong_number)
            """),
        ("link words whose text disagrees with the link's own",
            """
            SELECT count(*) FROM link_word lw
            JOIN link l ON l.id = lw.link_id
            JOIN word w ON w.id = lw.word_id
            WHERE w.text_id <> CASE lw.side WHEN 'from' THEN l.from_text_id ELSE l.to_text_id END
            """),
        ("links naming no word on either side",
            "SELECT count(*) FROM link l WHERE NOT EXISTS (SELECT 1 FROM link_word lw WHERE lw.link_id = l.id)"),
        // An absence is one claim read from either end, and the relation is the only thing that
        // says which end. An `omits` link names words on the `to` side alone and an `expands` link
        // on the `from` side alone; a row with words on the side the relation says is empty has
        // thrown the direction away, and no query can recover it.
        ("absences whose relation contradicts the side the words are on",
            """
            SELECT count(*) FROM link l
            WHERE l.relation IN ('omits', 'expands')
              AND EXISTS (
                  SELECT 1 FROM link_word lw
                  WHERE lw.link_id = l.id
                    AND lw.side = CASE l.relation WHEN 'omits' THEN 'from' ELSE 'to' END)
            """),
        // A link nothing claims. Every loader writes its claim in the same transaction as the link
        // (PRB-0198), so this is zero — and it is here because for one day it was not: the migration
        // backfilled the links that existed and nothing kept it up, so 403,343 links written
        // afterwards had none and the agreement measure reported the migration instead of the
        // corpus. A number that looks like an answer is worse than a missing one.
        ("links no claim stands on, so nothing says what asserted them",
            """
            SELECT count(*) FROM link l
            WHERE NOT EXISTS (SELECT 1 FROM link_claim c WHERE c.link_id = l.id)
            """),

        // Two links naming exactly the same words in the same pair of texts are not two facts.
        // They are two methods agreeing, and agreeing is what link_claim is for -- one link with
        // two claims. Before that table existed there were 4,664 of these, every one of them the
        // Ukrainian interlinear and the aligner independently reaching the same word pair, stored
        // as rivals. A loader that writes one again has silently gone back to throwing the
        // agreement away.
        // Two links naming exactly the same words in the same pair of texts are not two facts.
        // They are two methods agreeing, and agreeing is what link_claim is for -- one link with two
        // claims. Before that table existed there were 4,664 of these, every one the Ukrainian
        // interlinear and the aligner independently reaching the same word pair, stored as rivals.
        // A loader that writes one again has quietly gone back to throwing the agreement away.
        //
        // Fingerprinted before it is compared. The obvious form of this -- string_agg over every
        // link -- asks Postgres to sort 4.6 million assembled strings across parallel workers, and
        // it died on the shared memory segment rather than returning a wrong answer. Counting the
        // words and summing their ids is cheap and integer-only, and the exact comparison then runs
        // on the handful that collide.
        ("links naming the same words as another link, instead of one link with two claims",
            """
            WITH fingerprint AS (
                SELECT l.id, l.from_text_id, l.to_text_id, count(*) AS words,
                       min(lw.word_id) AS lowest, max(lw.word_id) AS highest, sum(lw.word_id) AS total
                FROM link l JOIN link_word lw ON lw.link_id = l.id
                GROUP BY l.id, l.from_text_id, l.to_text_id),
            colliding AS (
                SELECT f.id, f.from_text_id, f.to_text_id
                FROM fingerprint f
                JOIN (SELECT from_text_id, to_text_id, words, lowest, highest, total
                      FROM fingerprint
                      GROUP BY 1, 2, 3, 4, 5, 6
                      HAVING count(*) > 1) c
                  ON c.from_text_id = f.from_text_id AND c.to_text_id = f.to_text_id
                 AND c.words = f.words AND c.lowest = f.lowest
                 AND c.highest = f.highest AND c.total = f.total),
            shaped AS (
                SELECT co.id, co.from_text_id, co.to_text_id,
                       string_agg(lw.word_id::text, ',' ORDER BY lw.side, lw.word_id) AS words
                FROM colliding co JOIN link_word lw ON lw.link_id = co.id
                GROUP BY co.id, co.from_text_id, co.to_text_id)
            SELECT count(*) FROM (
                SELECT 1 FROM shaped
                GROUP BY from_text_id, to_text_id, words
                HAVING count(*) > 1) duplicated
            """),

        // A link may name words in two verses on purpose — that is how "the word ended up
        // elsewhere" is said. It is only wrong when no verse link joins the two verses, because
        // then the corpus is claiming a correspondence across a boundary it does not believe in.
        ("word links crossing a verse pair no verse link joins",
            """
            SELECT count(DISTINCT l.id)
            FROM link l
            JOIN link_word f ON f.link_id = l.id AND f.side = 'from'
            JOIN word fw ON fw.id = f.word_id
            JOIN link_word t ON t.link_id = l.id AND t.side = 'to'
            JOIN word tw ON tw.id = t.word_id
            JOIN verse_reference fr ON fr.verse_id = fw.verse_id AND fr.is_primary
            JOIN verse_reference tr ON tr.verse_id = tw.verse_id AND tr.is_primary
            WHERE (fr.canonical_book, fr.canonical_chapter, fr.canonical_verse)
               <> (tr.canonical_book, tr.canonical_chapter, tr.canonical_verse)
              -- Joined through verse_link_verse alone rather than through verse_link as well:
              -- the link row adds nothing the two membership rows do not already say, and with a
              -- quarter of a million verse links in the table the extra join was enough to time
              -- the whole verification out.
              AND NOT EXISTS (
                  SELECT 1 FROM verse_link_verse a
                  JOIN verse_link_verse b
                    ON b.verse_link_id = a.verse_link_id AND b.verse_id = tw.verse_id
                  WHERE a.verse_id = fw.verse_id
              )
            """),
    ];

    /// <summary>
    /// Links by how many claims stand on them.
    ///
    /// Counting distinct *methods* was wrong and read as a flat zero for the case it was built for:
    /// the Berean's own tables and Clear Bible's team are both <c>stated-by-source</c>, so 98,989
    /// links with two independent human witnesses counted as one. Two sources agreeing is the
    /// corroboration; which method each used is a separate question. A claim is unique on
    /// (link, method, source), so counting rows counts distinct answers.
    /// </summary>
    private const string AgreementSql =
        """
        SELECT claims, count(*)
        FROM (SELECT link_id, count(*) AS claims FROM link_claim GROUP BY link_id) c
        GROUP BY claims
        ORDER BY claims
        """;

    public async Task<CorpusMeasures> Measure(CancellationToken cancellationToken = default)
    {
        await db.Database.OpenConnectionAsync(cancellationToken);
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();

        var coverage = await Read(connection, CoverageSql, cancellationToken, reader => new Coverage(
            reader.GetString(0), reader.GetString(1), (int)reader.GetInt64(2), (int)reader.GetInt64(3),
            (int)reader.GetInt64(4), (int)reader.GetInt64(5), (int)reader.GetInt64(6)));

        var reach = await Read(connection, ReachSql, cancellationToken, reader => new Reach(
            reader.GetString(0), reader.GetString(1), (int)reader.GetInt64(2), (int)reader.GetInt64(3),
            (int)reader.GetInt64(4)));

        var contention = await Read(connection, ContentionSql, cancellationToken, reader => new Contention(
            reader.GetString(0), reader.GetString(1), (int)reader.GetInt64(2), (int)reader.GetInt64(3),
            (int)reader.GetInt64(4)));

        var crowding = await Read(connection, CrowdingSql, cancellationToken, reader => new Crowding(
            reader.GetString(0), reader.GetString(1), (int)reader.GetInt64(2), (int)reader.GetInt64(3)));

        var pairing = await Read(connection, PairingSql, cancellationToken, reader => new Pairing(
            reader.GetString(0), reader.GetString(1), (int)reader.GetInt64(2), (int)reader.GetInt64(3),
            (int)reader.GetInt64(4), (int)reader.GetInt64(5), reader.GetFieldValue<string[]>(6)));

        var agreement = await Read(connection, AgreementSql, cancellationToken, reader =>
            new Agreement((int)reader.GetInt64(0), (int)reader.GetInt64(1)));

        // Single-threaded, deliberately. These sweep every link in the corpus, and Postgres
        // parallelises them across workers that share their sort state through /dev/shm — which a
        // container gives 64 MB of by default. The duplicate-link check exhausted it and the whole
        // verification died with "No space left on device", which is a true sentence about a
        // segment nobody sized and a false one about the disk. Raising it belongs in the frozen
        // repository's compose file (PRB-0187); not needing it belongs here.
        await using (var single = new NpgsqlCommand("SET max_parallel_workers_per_gather = 0", connection))
        {
            await single.ExecuteNonQueryAsync(cancellationToken);
        }

        var integrity = new List<IntegrityCheck>(Integrity.Length);
        foreach (var (breaks, sql) in Integrity)
        {
            // These sweep the whole link table -- three and a half million rows joined to their
            // words and addresses -- so they are minutes, not seconds, and the default thirty
            // seconds silently turned a correct corpus into a failed verification.
            await using var command = new NpgsqlCommand(sql, connection) { CommandTimeout = 900 };
            integrity.Add(new IntegrityCheck(breaks, (int)(long)(await command.ExecuteScalarAsync(cancellationToken))!));
        }

        await using (var restore = new NpgsqlCommand("RESET max_parallel_workers_per_gather", connection))
        {
            await restore.ExecuteNonQueryAsync(cancellationToken);
        }

        return new CorpusMeasures(coverage, reach, contention, crowding, pairing, agreement, integrity);
    }

    /// <summary>
    /// Measures the corpus, stores the row, and says how it compares with the load before it. The
    /// comparison is the reason the row exists: a number nobody remembers is a number that can fall
    /// without anybody noticing.
    /// </summary>
    public async Task<VerificationRun> Record(CancellationToken cancellationToken = default)
    {
        var measures = await Measure(cancellationToken);
        var previous = await db.VerificationRuns.OrderByDescending(v => v.RanAt).FirstOrDefaultAsync(cancellationToken);

        var verification = new VerificationRun
        {
            RanAt = DateTimeOffset.UtcNow,
            Broken = measures.Broken,
            Rendered = measures.Rendered,
            RenderedWords = measures.RenderedWords,
            Words = measures.Words,
            Measures = JsonSerializer.SerializeToDocument(measures, MeasureJson),
        };

        db.VerificationRuns.Add(verification);
        await db.SaveChangesAsync(cancellationToken);

        Report(verification, previous);
        return verification;
    }

    private void Report(VerificationRun current, VerificationRun? previous)
    {
        if (current.Broken > 0)
        {
            logger.LogError(
                "The corpus breaks {Broken} integrity checks; /v1/health names them and none of them is a " +
                "measurement — each is a shape no correct load produces",
                current.Broken);
        }

        if (previous is null)
        {
            logger.LogInformation(
                "Verified: {Rendered:P1} of the words in a linked text reach a witness", current.Rendered);
            return;
        }

        var moved = current.Rendered - previous.Rendered;
        if (moved < -Tolerance)
        {
            logger.LogWarning(
                "Verified: {Rendered:P1} of the words in a linked text reach a witness, down from {Before:P1}. " +
                "A load that reaches fewer words than the one before it has lost something",
                current.Rendered, previous.Rendered);
            return;
        }

        logger.LogInformation(
            "Verified: {Rendered:P1} of the words in a linked text reach a witness, {Before:P1} last time",
            current.Rendered, previous.Rendered);
    }

    /// <summary>
    /// How far the share may fall before it is worth saying so. Loads differ by a link or two
    /// without anything being wrong; a tenth of a point is not that.
    /// </summary>
    private const double Tolerance = 0.001;

    private static readonly JsonSerializerOptions MeasureJson =
        new(JsonSerializerDefaults.Web) { WriteIndented = false };

    private static async Task<List<T>> Read<T>(
        NpgsqlConnection connection,
        string sql,
        CancellationToken cancellationToken,
        Func<NpgsqlDataReader, T> row)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        var rows = new List<T>();
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(row(reader));
        }

        return rows;
    }
}
