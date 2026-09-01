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
/// The four measures answer different questions and none of them substitutes for another. Coverage
/// is the forward view: of the words in a translation, how many say anything. Reach is the same
/// question from the other side, and it is the one nobody asks — a corpus can link 90% of a
/// translation's words to 40% of the witness's, and the forward number hides that entirely.
/// Contention counts words claimed more than once, which is how a heuristic mapping announces
/// itself. Integrity is the only measure with a right answer, and it is zero.
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

    /// <summary>
    /// Walks the links rather than the words: a lateral lookup per word would be two million of
    /// them, and the answer is the same.
    /// </summary>
    private const string CoverageSql =
        """
        WITH claimed AS (
            SELECT lw.word_id,
                   bool_or(l.relation IN ('renders', 'equals')) AS rendered,
                   bool_or(l.relation IN ('omits', 'expands', 'transposes')) AS absent
            FROM link_word lw
            JOIN link l ON l.id = lw.link_id
            JOIN text other ON other.id = l.to_text_id AND other.kind <> 'translation'
            WHERE lw.side = 'from'
            GROUP BY lw.word_id
        ),
        paired AS (
            SELECT DISTINCT l.from_text_id
            FROM link l
            JOIN text other ON other.id = l.to_text_id AND other.kind <> 'translation'
        )
        SELECT t.slug,
               count(*),
               count(*) FILTER (WHERE c.rendered),
               count(*) FILTER (WHERE c.rendered IS NOT TRUE AND c.absent),
               count(*) FILTER (WHERE c.word_id IS NULL AND p.from_text_id IS NOT NULL),
               count(*) FILTER (WHERE c.word_id IS NULL AND p.from_text_id IS NULL)
        FROM word w
        JOIN text t ON t.id = w.text_id AND t.kind = 'translation'
        LEFT JOIN claimed c ON c.word_id = w.id
        LEFT JOIN paired p ON p.from_text_id = w.text_id
        GROUP BY t.slug
        ORDER BY t.slug
        """;

    /// <summary>
    /// The direction nobody measures. A corpus can name 90% of a translation's words and still
    /// leave more than half the witness untouched, and the forward count hides that completely.
    /// </summary>
    private const string ReachSql =
        $"""
        SELECT witness.slug,
               source.slug,
               (SELECT count(*) FROM word lex
                WHERE lex.text_id = witness.id AND NOT {StructuralMorphemes}),
               count(DISTINCT lw.word_id)
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
    private const string ContentionSql =
        """
        SELECT t.slug, against.slug, count(*) FILTER (WHERE claims.n > 1), coalesce(max(claims.n), 0)
        FROM (
            SELECT lw.word_id, l.from_text_id, l.to_text_id, count(*) AS n
            FROM link_word lw
            JOIN link l ON l.id = lw.link_id
            WHERE lw.side = 'from'
            GROUP BY lw.word_id, l.from_text_id, l.to_text_id
        ) claims
        JOIN text t ON t.id = claims.from_text_id
        JOIN text against ON against.id = claims.to_text_id
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
              AND NOT EXISTS (
                  SELECT 1 FROM verse_link vl
                  JOIN verse_link_verse a ON a.verse_link_id = vl.id AND a.verse_id = fw.verse_id
                  JOIN verse_link_verse b ON b.verse_link_id = vl.id AND b.verse_id = tw.verse_id
              )
            """),
    ];

    public async Task<CorpusMeasures> Measure(CancellationToken cancellationToken = default)
    {
        await db.Database.OpenConnectionAsync(cancellationToken);
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();

        var coverage = await Read(connection, CoverageSql, cancellationToken, reader => new Coverage(
            reader.GetString(0), (int)reader.GetInt64(1), (int)reader.GetInt64(2),
            (int)reader.GetInt64(3), (int)reader.GetInt64(4), (int)reader.GetInt64(5)));

        var reach = await Read(connection, ReachSql, cancellationToken, reader => new Reach(
            reader.GetString(0), reader.GetString(1), (int)reader.GetInt64(2), (int)reader.GetInt64(3)));

        var contention = await Read(connection, ContentionSql, cancellationToken, reader => new Contention(
            reader.GetString(0), reader.GetString(1), (int)reader.GetInt64(2), (int)reader.GetInt64(3)));

        var crowding = await Read(connection, CrowdingSql, cancellationToken, reader => new Crowding(
            reader.GetString(0), reader.GetString(1), (int)reader.GetInt64(2), (int)reader.GetInt64(3)));

        var integrity = new List<IntegrityCheck>(Integrity.Length);
        foreach (var (breaks, sql) in Integrity)
        {
            await using var command = new NpgsqlCommand(sql, connection);
            integrity.Add(new IntegrityCheck(breaks, (int)(long)(await command.ExecuteScalarAsync(cancellationToken))!));
        }

        return new CorpusMeasures(coverage, reach, contention, crowding, integrity);
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
            logger.LogInformation("Verified: {Rendered:P1} of translated words reach a witness", current.Rendered);
            return;
        }

        var moved = current.Rendered - previous.Rendered;
        if (moved < -Tolerance)
        {
            logger.LogWarning(
                "Verified: {Rendered:P1} of translated words reach a witness, down from {Before:P1}. " +
                "A load that reaches fewer words than the one before it has lost something",
                current.Rendered, previous.Rendered);
            return;
        }

        logger.LogInformation(
            "Verified: {Rendered:P1} of translated words reach a witness, {Before:P1} last time",
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
