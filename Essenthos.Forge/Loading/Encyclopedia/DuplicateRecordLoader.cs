using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Database.Entities.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace Essenthos.Core.Loading.Encyclopedia;

/// <param name="Listed">Pairs the list holds.</param>
/// <param name="Folded">Records folded into another this run.</param>
/// <param name="AlreadyFolded">Pairs an earlier run has already folded.</param>
/// <param name="Missing">Pairs naming a record the encyclopedia does not hold, left alone.</param>
/// <param name="Moved">Rows moved onto the record that stays, across every table that names a record.</param>
/// <param name="Joined">Rows that said again what the record that stays already said, joined into its own.</param>
/// <param name="Parted">Rows a split moved off the dataset's record onto the other man's, this run.</param>
internal sealed record DuplicateRecordOutcome(
    int Listed,
    int Folded,
    int AlreadyFolded,
    int Missing,
    int Moved,
    int Joined,
    TimeSpan Elapsed,
    int Parted = 0)
{
    public override string ToString() =>
        $"{Listed} records the dataset wrote twice for one referent: {Folded} folded into the record that " +
        $"stays ({Moved} rows moved, {Joined} that repeated it joined), {AlreadyFolded} folded by an earlier " +
        $"run, {Missing} naming a record not held, in {Elapsed}" +
        (Parted > 0 ? $"; {Parted} rows of a record that held two men moved to the other" : "");
}

/// <summary>
/// The records a dataset wrote twice for one person, folded into one.
///
/// <para>
/// **BibleData files a man once for each list he stands in.** The priests and Levites who settled in
/// Jerusalem are listed in 1 Chronicles 9 and again in Nehemiah 11, the priests of the return in
/// Nehemiah 12:1-7 and again, a generation on, in 12:12-21, and the dataset gives each appearance a
/// record of its own: Hilkiah son of Meshullam is <c>hilkiah-3</c> in one and <c>hilkiah-6</c> in the
/// other. Every word that names him then leaves one of the two with an empty page, and a reader is
/// shown two men where the text has one. Which pairs are one man is <see cref="Resource"/>, each with
/// the verses that say so; this does what the list says.
/// </para>
///
/// <para>
/// **Everything the second record held moves to the first**: its verses, names, relationships,
/// descriptions, pictures, the words that name it and whatever else names it by row. Where the two
/// said the same thing — the same verse from the same source, the same name, two annotations of one
/// word — it is kept once, and an annotation that loses keeps its testimony as a claim on the one
/// that stays. What cannot move is the second record's own identity, and that is kept as a
/// <see cref="MergedRecord"/>: its address, so a link to it still arrives, and its name, description
/// and BibleData id, so what the source said is still there to be read.
/// </para>
///
/// <para>
/// **Run on every load, after every pass that writes onto a record by its address.** Those passes
/// read files keyed by the addresses as the dataset gave them, so on a corpus built from nothing they
/// have to find both records first; this then folds what they wrote. A pair already folded is found
/// by its address being gone and is not folded twice. Unfolding one again is a rebuild of the
/// encyclopedia, which is what taking a pair off the list asks for.
/// </para>
/// </summary>
internal sealed class DuplicateRecordLoader(AppDbContext db, ILogger<DuplicateRecordLoader> logger)
{
    public const string Resource = "Essenthos.Core.Loading.Encyclopedia.DuplicateRecords.json";

    private static readonly JsonSerializerOptions Shape = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private const string Folding =
        """
        CREATE TEMP TABLE folding (folded integer PRIMARY KEY, kept integer NOT NULL) ON COMMIT DROP;
        INSERT INTO folding (folded, kept) SELECT * FROM unnest(@folded, @kept);
        CREATE TEMP TABLE fold_count (rows integer NOT NULL, joined boolean NOT NULL) ON COMMIT DROP
        """;

    /// <summary>
    /// The statements, in order. Each that can find a row already saying the same thing on the record
    /// that stays deals with that row first, so no move ever meets a unique index; everything left on
    /// the folded record after the last of them goes with it when it is deleted.
    /// </summary>
    private static readonly string[] Statements =
    [
        // An earlier fold into the record being folded now follows it.
        "UPDATE merged_record m SET entity_id = f.kept FROM folding f WHERE m.entity_id = f.folded",

        // Two annotations of one word, one on each record: the stronger stays — a person's or a
        // source's word over an inference, then the surer — and the other's testimony becomes its
        // claims, so the agreement is kept and nothing is counted twice.
        """
        CREATE TEMP TABLE twice ON COMMIT DROP AS
        SELECT DISTINCT ON (a.word_id, f.kept)
               a.word_id, f.kept, a.id AS stays
        FROM word_entity a
        JOIN folding f ON a.entity_id IN (f.folded, f.kept)
        WHERE EXISTS (SELECT 1 FROM word_entity b
                      WHERE b.word_id = a.word_id AND b.entity_id IN (f.folded, f.kept) AND b.id <> a.id)
        ORDER BY a.word_id, f.kept,
                 CASE a.method WHEN 'manual' THEN 0 WHEN 'stated-by-source' THEN 1 ELSE 2 END,
                 a.confidence DESC NULLS FIRST,
                 a.entity_id = f.kept DESC,
                 a.id
        """,
        """
        CREATE TEMP TABLE yielding ON COMMIT DROP AS
        SELECT a.id AS yields, t.stays
        FROM twice t
        JOIN folding f ON f.kept = t.kept
        JOIN word_entity a ON a.word_id = t.word_id AND a.entity_id IN (f.folded, f.kept) AND a.id <> t.stays
        """,
        """
        INSERT INTO word_entity_claim (word_entity_id, method, confidence, source, note)
        SELECT y.stays, a.method, a.confidence, a.source, a.note
        FROM yielding y JOIN word_entity a ON a.id = y.yields
        ON CONFLICT DO NOTHING
        """,
        """
        INSERT INTO word_entity_claim (word_entity_id, method, confidence, source, note)
        SELECT y.stays, c.method, c.confidence, c.source, c.note
        FROM yielding y JOIN word_entity_claim c ON c.word_entity_id = y.yields
        ON CONFLICT DO NOTHING
        """,
        """
        WITH gone AS (DELETE FROM word_entity a USING yielding y WHERE a.id = y.yields RETURNING 1)
        INSERT INTO fold_count SELECT count(*), true FROM gone
        """,
        Move("word_entity", "entity_id"),

        // The verse list: a verse both records cite from the same source is cited once, and the list
        // is keyed on the record, the verse and the source, so the kept record's row is the one kept.
        MoveUnlessHeld("entity_verse",
            "c.canonical_book = o.canonical_book AND c.canonical_chapter = o.canonical_chapter "
            + "AND c.canonical_verse = o.canonical_verse AND c.source = o.source"),
        """
        WITH gone AS (
            DELETE FROM entity_verse v USING entity_verse w
            WHERE v.entity_id IN (SELECT kept FROM folding) AND w.entity_id = v.entity_id
              AND w.canonical_book = v.canonical_book AND w.canonical_chapter = v.canonical_chapter
              AND w.canonical_verse = v.canonical_verse AND w.source = v.source
              AND w.disputed = v.disputed AND w.label IS NOT DISTINCT FROM v.label
              AND w.id < v.id
            RETURNING 1)
        INSERT INTO fold_count SELECT count(*), true FROM gone
        """,

        // The names: the folded record's own name stays as a name of the man, which is how Jucal is
        // still found under Jucal; a name both records carry is carried once.
        Move("entity_name", "entity_id"),
        Move("entity_name", "aspect_of_entity_id"),
        """
        WITH gone AS (
            DELETE FROM entity_name n USING entity_name o
            WHERE n.entity_id IN (SELECT kept FROM folding) AND o.entity_id = n.entity_id
              AND o.label = n.label
              AND o.hebrew IS NOT DISTINCT FROM n.hebrew
              AND o.hebrew_transliterated IS NOT DISTINCT FROM n.hebrew_transliterated
              AND o.greek IS NOT DISTINCT FROM n.greek
              AND o.greek_transliterated IS NOT DISTINCT FROM n.greek_transliterated
              AND o.meaning IS NOT DISTINCT FROM n.meaning
              AND o.hebrew_strong_number IS NOT DISTINCT FROM n.hebrew_strong_number
              AND o.greek_strong_number IS NOT DISTINCT FROM n.greek_strong_number
              AND o.kind IS NOT DISTINCT FROM n.kind
              AND o.aspect_of_entity_id IS NOT DISTINCT FROM n.aspect_of_entity_id
              AND o.id < n.id
            RETURNING 1)
        INSERT INTO fold_count SELECT count(*), true FROM gone
        """,

        // Relationships move row by row and are never joined: the owner's review list decides them
        // by row, and a row deleted here would be a decision with nothing left to apply to. Only a
        // relationship the fold turned into one between the man and himself goes.
        Move("entity_relationship", "from_entity_id"),
        Move("entity_relationship", "to_entity_id"),
        "DELETE FROM entity_relationship WHERE from_entity_id = to_entity_id AND from_entity_id IN (SELECT kept FROM folding)",

        // What our own reading said of each: the folded record's clauses follow the kept record's,
        // and a clause that now names the man as his own relative goes.
        Move("entity_descriptor", "target_entity_id"),
        """
        WITH tops AS (
            SELECT f.folded, f.kept,
                   coalesce((SELECT max(d.ordinal) FROM entity_descriptor d WHERE d.entity_id = f.kept), 0) AS kept_top,
                   coalesce((SELECT max(d.ordinal) FROM entity_descriptor d WHERE d.entity_id = f.folded), 0) AS folded_top
            FROM folding f),
        offsets AS (
            SELECT folded, kept,
                   kept_top + coalesce(sum(folded_top) OVER (PARTITION BY kept ORDER BY folded
                                        ROWS BETWEEN UNBOUNDED PRECEDING AND 1 PRECEDING), 0) AS shift
            FROM tops),
        moved AS (
            UPDATE entity_descriptor d SET entity_id = o.kept, ordinal = d.ordinal + o.shift
            FROM offsets o WHERE d.entity_id = o.folded
            RETURNING 1)
        INSERT INTO fold_count SELECT count(*), false FROM moved
        """,
        "DELETE FROM entity_descriptor WHERE entity_id = target_entity_id AND entity_id IN (SELECT kept FROM folding)",

        // A claim both records carry from one source by one method is one claim: where they said
        // different things of the two, the kept record's says both, as the person register writes a
        // record two of its bearers reach.
        """
        UPDATE entity_claim k
        SET note = coalesce(k.note || '; ', '') || o.note, confidence = greatest(k.confidence, o.confidence)
        FROM entity_claim o JOIN folding f ON o.entity_id = f.folded
        WHERE k.entity_id = f.kept AND k.method = o.method AND k.source = o.source
          AND o.note IS NOT NULL AND strpos(coalesce(k.note, ''), o.note) = 0
        """,

        // Rows one record may have only one of: the kept record's stands.
        MoveUnlessHeld("entity_claim", "c.method = o.method AND c.source = o.source"),
        MoveUnlessHeld("entity_name_form", "c.language = o.language AND c.grammatical_case = o.grammatical_case"),
        MoveUnlessHeld("place_location", "true"),
        """
        UPDATE entity_rendering r SET occurrences = r.occurrences + o.occurrences
        FROM entity_rendering o JOIN folding f ON o.entity_id = f.folded
        WHERE r.entity_id = f.kept AND r.text_id = o.text_id AND r.form = o.form
        """,
        // A text's heading spelling stays the kept record's; the count that follows the fold rebuilds
        // all of them from the words.
        """
        UPDATE entity_rendering o SET heading = false
        FROM folding f
        WHERE o.entity_id = f.folded AND o.heading
          AND EXISTS (SELECT 1 FROM entity_rendering k WHERE k.entity_id = f.kept AND k.text_id = o.text_id)
        """,
        MoveUnlessHeld("entity_rendering", "c.text_id = o.text_id AND c.form = o.form"),

        // A picture of the folded record joins the kept record's gallery, and leads only where the
        // kept record had no picture of that kind leading.
        """
        UPDATE entity_image i SET role = 'gallery'
        FROM folding f
        WHERE i.entity_id = f.folded AND i.role = 'primary'
          AND EXISTS (SELECT 1 FROM entity_image k WHERE k.entity_id = f.kept AND k.kind = i.kind AND k.role = 'primary')
        """,
        MoveUnlessHeld("entity_image", "c.file = o.file"),

        """
        DELETE FROM title_bearer b USING folding f, title_bearer k
        WHERE (b.title_entity_id = f.folded AND k.title_entity_id = f.kept AND k.bearer_entity_id = b.bearer_entity_id)
           OR (b.bearer_entity_id = f.folded AND k.bearer_entity_id = f.kept AND k.title_entity_id = b.title_entity_id)
        """,
        Move("title_bearer", "title_entity_id"),
        Move("title_bearer", "bearer_entity_id"),

        Move("entity_alternative", "entity_id"),
        Move("entity_alternative", "alternative_entity_id"),
        "DELETE FROM entity_alternative WHERE entity_id = alternative_entity_id AND entity_id IN (SELECT kept FROM folding)",

        Move("entity", "origin_entity_id"),
        Move("entity_passage", "entity_id"),
        Move("observance_time", "entity_id"),
        Move("event", "entity_id"),
        Move("period", "entity_id"),
        Move("strong_gentilic", "people_entity_id"),
        Move("strong_gentilic", "origin_entity_id"),

        "DELETE FROM entity WHERE id IN (SELECT folded FROM folding)",
    ];

    private static string Move(string table, string column) =>
        $"""
         WITH moved AS (
             UPDATE {table} t SET {column} = f.kept FROM folding f WHERE t.{column} = f.folded RETURNING 1)
         INSERT INTO fold_count SELECT count(*), false FROM moved
         """;

    private static string MoveUnlessHeld(string table, string same) =>
        $"""
         WITH moved AS (
             UPDATE {table} o SET entity_id = f.kept FROM folding f
             WHERE o.entity_id = f.folded
               AND NOT EXISTS (SELECT 1 FROM {table} c WHERE c.entity_id = f.kept AND {same})
             RETURNING 1)
         INSERT INTO fold_count SELECT count(*), false FROM moved
         """;

    public async Task<DuplicateRecordOutcome> Load(CancellationToken cancellationToken = default)
    {
        var list = Read();
        var folded = await Fold(list, cancellationToken);
        var parted = await Split(list.Splits ?? [], cancellationToken);
        return folded with { Parted = parted };
    }

    /// <summary>
    /// The words, verses and name a dataset's record holds for a second man, moved to his record.
    ///
    /// <para>
    /// **The fold's other half.** BibleData writes Bartholomew and Nathanael as one man, because a
    /// tradition does, and every word of John that says Nathanael then names a man the chapter never
    /// calls Bartholomew. Where the owner has ruled that the verses are the other man's, what the
    /// record holds in them moves: the annotations of their words, in every text; the verse rows, the
    /// dataset's and ours alike, keeping their sources, because they are still the same testimony
    /// about the verse; the relationships and our own reading's clauses that cite one of those verses,
    /// row by row as a fold moves them, since a father named in the second man's verse is his father;
    /// and the name the record holds for him, which his own record carries instead.
    /// An annotation his record already has for the same word keeps the moved one's testimony as its
    /// claims, as a fold does. The rest of the record stays as it was.
    /// </para>
    ///
    /// <para>
    /// After the passes that annotate and cite, so that what they wrote is moved rather than written
    /// again beside it; a split already made finds nothing left to move.
    /// </para>
    /// </summary>
    /// <returns>The rows moved or joined.</returns>
    internal async Task<int> Split(IReadOnlyList<DuplicateRecordSplit> splits, CancellationToken cancellationToken = default)
    {
        if (splits.Count == 0)
        {
            return 0;
        }

        var slugs = splits.SelectMany(s => new[] { s.From, s.To }).Distinct(StringComparer.Ordinal).ToList();
        var held = await db.Entities
            .Where(e => slugs.Contains(e.Slug))
            .ToDictionaryAsync(e => e.Slug, e => e.Id, StringComparer.Ordinal, cancellationToken);

        await db.Database.OpenConnectionAsync(cancellationToken);
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();
        var parted = 0;
        foreach (var split in splits)
        {
            if (!held.TryGetValue(split.From, out var from) || !held.TryGetValue(split.To, out var to))
            {
                logger.LogWarning(
                    "The list of records written wrongly moves {Name} from \"{From}\" to \"{To}\", and the encyclopedia " +
                    "does not hold both. Correct the pair in {Resource}, or load the record it moves to first.",
                    split.Name ?? split.To, split.From, split.To, Resource);
                continue;
            }

            var spans = split.Verses.Select(ScriptureSpan.Parse).ToList();
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            await Annotating.Run(connection, transaction, Parting, cancellationToken,
                ("books", spans.Select(v => v.Book).ToArray()),
                ("fromChapters", spans.Select(v => v.FromChapter).ToArray()),
                ("fromVerses", spans.Select(v => v.FromVerse ?? 0).ToArray()),
                ("toChapters", spans.Select(v => v.ToChapter).ToArray()),
                ("toVerses", spans.Select(v => v.ToVerse ?? int.MaxValue).ToArray()));
            // A split without a name moves verses between two records that both hold the name already,
            // Tamar to Tamar: the name rows are each record's own and neither moves.
            foreach (var statement in split.Name is null ? SplitStatements[..^NameStatements] : SplitStatements)
            {
                await Annotating.Run(connection, transaction, statement, cancellationToken,
                    ("from", from), ("to", to), ("name", split.Name ?? string.Empty));
            }

            var (moved, joined) = await Counted(connection, transaction, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            if (moved + joined > 0)
            {
                logger.LogInformation(
                    "Moved {Rows} rows of {Name} from {From} to {To}: {Why}", moved + joined, split.Name ?? split.To, split.From,
                    split.To, split.Why);
            }

            parted += moved + joined;
        }

        return parted;
    }

    /// <summary>How many of <see cref="SplitStatements"/>, at its end, move the name.</summary>
    private const int NameStatements = 2;

    /// <summary>The verses a split moves, as the spans the file writes, and the tally of what moved.</summary>
    private const string Parting =
        """
        CREATE TEMP TABLE parting (book integer NOT NULL, from_chapter integer NOT NULL, from_verse integer NOT NULL,
                                   to_chapter integer NOT NULL, to_verse integer NOT NULL) ON COMMIT DROP;
        INSERT INTO parting SELECT * FROM unnest(@books, @fromChapters, @fromVerses, @toChapters, @toVerses);
        CREATE TEMP TABLE fold_count (rows integer NOT NULL, joined boolean NOT NULL) ON COMMIT DROP
        """;

    /// <summary>Whether the row a statement names by <c>{0}</c> stands in a verse the split moves.</summary>
    private const string InParting =
        """
        EXISTS (SELECT 1 FROM parting p
                WHERE p.book = {0}.canonical_book
                  AND ({0}.canonical_chapter, {0}.canonical_verse) >= (p.from_chapter, p.from_verse)
                  AND ({0}.canonical_chapter, {0}.canonical_verse) <= (p.to_chapter, p.to_verse))
        """;

    /// <summary>
    /// The statements of a split, in order: the annotations, joined where his record already names
    /// the word and moved where it does not; the verse rows, the same way; the relationships and
    /// clauses the moved verses cite; and the name.
    /// </summary>
    private static readonly string[] SplitStatements =
    [
        $"""
         CREATE TEMP TABLE parted ON COMMIT DROP AS
         SELECT a.id, a.word_id, t.id AS joins
         FROM word_entity a
         JOIN word w ON w.id = a.word_id
         JOIN verse_reference r ON r.verse_id = w.verse_id AND r.is_primary
         LEFT JOIN word_entity t ON t.word_id = a.word_id AND t.entity_id = @to
         WHERE a.entity_id = @from AND {string.Format(InParting, "r")}
         """,
        """
        INSERT INTO word_entity_claim (word_entity_id, method, confidence, source, note)
        SELECT p.joins, a.method, a.confidence, a.source, a.note
        FROM parted p JOIN word_entity a ON a.id = p.id
        WHERE p.joins IS NOT NULL
        ON CONFLICT DO NOTHING
        """,
        """
        INSERT INTO word_entity_claim (word_entity_id, method, confidence, source, note)
        SELECT p.joins, c.method, c.confidence, c.source, c.note
        FROM parted p JOIN word_entity_claim c ON c.word_entity_id = p.id
        WHERE p.joins IS NOT NULL
        ON CONFLICT DO NOTHING
        """,
        """
        WITH gone AS (DELETE FROM word_entity a USING parted p WHERE a.id = p.id AND p.joins IS NOT NULL RETURNING 1)
        INSERT INTO fold_count SELECT count(*), true FROM gone
        """,
        """
        WITH moved AS (
            UPDATE word_entity a SET entity_id = @to FROM parted p WHERE a.id = p.id AND p.joins IS NULL RETURNING 1)
        INSERT INTO fold_count SELECT count(*), false FROM moved
        """,
        $"""
         WITH gone AS (
             DELETE FROM entity_verse v
             WHERE v.entity_id = @from AND {string.Format(InParting, "v")}
               AND EXISTS (SELECT 1 FROM entity_verse o
                           WHERE o.entity_id = @to AND o.source = v.source
                             AND (o.canonical_book, o.canonical_chapter, o.canonical_verse)
                                 = (v.canonical_book, v.canonical_chapter, v.canonical_verse))
             RETURNING 1)
         INSERT INTO fold_count SELECT count(*), true FROM gone
         """,
        $"""
         WITH moved AS (
             UPDATE entity_verse v SET entity_id = @to
             WHERE v.entity_id = @from AND {string.Format(InParting, "v")}
             RETURNING 1)
         INSERT INTO fold_count SELECT count(*), false FROM moved
         """,
        $"""
         WITH moved AS (
             UPDATE entity_relationship r
             SET from_entity_id = CASE WHEN r.from_entity_id = @from THEN @to ELSE r.from_entity_id END,
                 to_entity_id = CASE WHEN r.to_entity_id = @from THEN @to ELSE r.to_entity_id END
             WHERE (r.from_entity_id = @from OR r.to_entity_id = @from) AND {string.Format(InParting, "r")}
             RETURNING 1)
         INSERT INTO fold_count SELECT count(*), false FROM moved
         """,
        "DELETE FROM entity_relationship WHERE from_entity_id = @to AND to_entity_id = @to",
        $"""
         WITH moved AS (
             UPDATE entity_descriptor d SET target_entity_id = @to
             WHERE d.target_entity_id = @from AND {string.Format(InParting, "d")}
             RETURNING 1)
         INSERT INTO fold_count SELECT count(*), false FROM moved
         """,
        $"""
         WITH top AS (SELECT coalesce(max(ordinal), 0) AS ordinal FROM entity_descriptor WHERE entity_id = @to),
         leaving AS (
             SELECT d.id, row_number() OVER (ORDER BY d.ordinal) AS n
             FROM entity_descriptor d
             WHERE d.entity_id = @from AND {string.Format(InParting, "d")}),
         moved AS (
             UPDATE entity_descriptor d SET entity_id = @to, ordinal = top.ordinal + l.n
             FROM leaving l, top WHERE d.id = l.id
             RETURNING 1)
         INSERT INTO fold_count SELECT count(*), false FROM moved
         """,
        "DELETE FROM entity_descriptor WHERE entity_id = @to AND target_entity_id = @to",
        """
        WITH gone AS (
            DELETE FROM entity_name n
            WHERE n.entity_id = @from AND n.label = @name
              AND EXISTS (SELECT 1 FROM entity_name o WHERE o.entity_id = @to AND o.label = @name)
            RETURNING 1)
        INSERT INTO fold_count SELECT count(*), true FROM gone
        """,
        """
        WITH moved AS (UPDATE entity_name n SET entity_id = @to WHERE n.entity_id = @from AND n.label = @name RETURNING 1)
        INSERT INTO fold_count SELECT count(*), false FROM moved
        """,
    ];

    internal async Task<DuplicateRecordOutcome> Fold(DuplicateRecordList list, CancellationToken cancellationToken = default)
    {
        var started = Stopwatch.StartNew();

        var slugs = list.Merges.SelectMany(m => new[] { m.Keeps, m.Folds }).Distinct(StringComparer.Ordinal).ToList();
        var held = await db.Entities
            .Where(e => slugs.Contains(e.Slug))
            .Select(e => new { e.Id, e.Slug, e.Kind, e.Name, e.Distinguisher, e.SourceId, e.Source })
            .ToDictionaryAsync(e => e.Slug, StringComparer.Ordinal, cancellationToken);
        var gone = await db.MergedRecords
            .Where(m => slugs.Contains(m.Slug))
            .Select(m => new { m.Slug, m.EntityId })
            .ToDictionaryAsync(m => m.Slug, m => m.EntityId, StringComparer.Ordinal, cancellationToken);

        int already = 0, missing = 0;
        var folds = new List<(int Folded, int Kept, MergedRecord Record)>();
        foreach (var merge in list.Merges)
        {
            if (!held.TryGetValue(merge.Folds, out var folded))
            {
                if (gone.ContainsKey(merge.Folds))
                {
                    already++;
                }
                else
                {
                    missing++;
                    logger.LogWarning(
                        "The list of records written twice folds \"{Folds}\" into \"{Keeps}\", and the encyclopedia " +
                        "holds no record \"{Folds}\". Correct the address in {Resource}, or take the pair off it.",
                        merge.Folds, merge.Keeps, merge.Folds, Resource);
                }

                continue;
            }

            int? kept = held.TryGetValue(merge.Keeps, out var keeps) ? keeps.Id
                : gone.TryGetValue(merge.Keeps, out var into) ? into
                : null;
            if (kept is null || kept == folded.Id || (keeps is not null && keeps.Kind != folded.Kind))
            {
                missing++;
                logger.LogWarning(
                    "The list of records written twice folds \"{Folds}\" into \"{Keeps}\", which the encyclopedia " +
                    "does not hold as a second record of the same kind. Correct the pair in {Resource}.",
                    merge.Folds, merge.Keeps, Resource);
                continue;
            }

            folds.Add((folded.Id, kept.Value, new MergedRecord
            {
                Slug = folded.Slug,
                EntityId = kept.Value,
                Name = folded.Name,
                Distinguisher = folded.Distinguisher,
                RecordSourceId = folded.SourceId,
                RecordSource = folded.Source,
                Method = list.Method,
                Confidence = merge.Confidence ?? list.Confidence,
                Reason = merge.Why,
                Source = merge.Source ?? list.Source,
            }));
        }

        if (folds.Count == 0)
        {
            var nothing = new DuplicateRecordOutcome(list.Merges.Count, 0, already, missing, 0, 0, started.Elapsed);
            logger.LogInformation("No record written twice is left to fold: {Outcome}", nothing);
            return nothing;
        }

        await db.Database.OpenConnectionAsync(cancellationToken);
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        // Written before the fold so an earlier fold into one of these follows it with the rest.
        db.MergedRecords.AddRange(folds.Select(f => f.Record));
        await db.SaveChangesAsync(cancellationToken);

        await Annotating.Run(connection, transaction, Folding, cancellationToken,
            ("folded", folds.Select(f => f.Folded).ToArray()),
            ("kept", folds.Select(f => f.Kept).ToArray()));
        foreach (var statement in Statements)
        {
            await Annotating.Run(connection, transaction, statement, cancellationToken);
        }

        var (moved, joined) = await Counted(connection, transaction, cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        var outcome = new DuplicateRecordOutcome(
            list.Merges.Count, folds.Count, already, missing, moved, joined, started.Elapsed);
        logger.LogInformation("Folded the records the dataset wrote twice for one person: {Outcome}", outcome);
        return outcome;
    }

    private static async Task<(int Moved, int Joined)> Counted(
        NpgsqlConnection connection,
        IDbContextTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            "SELECT coalesce(sum(rows) FILTER (WHERE NOT joined), 0)::int, " +
            "coalesce(sum(rows) FILTER (WHERE joined), 0)::int FROM fold_count",
            connection,
            (NpgsqlTransaction)transaction.GetDbTransaction());
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);
        return (reader.GetInt32(0), reader.GetInt32(1));
    }

    internal static DuplicateRecordList Read()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(Resource)
                           ?? throw new FileNotFoundException(
                               $"The embedded resource \"{Resource}\" is not in this assembly. It is added by the " +
                               "EmbeddedResource item in Essenthos.Forge.csproj; if the file was moved or renamed, " +
                               "that item and this name have to move with it.",
                               Resource);

        var list = JsonSerializer.Deserialize<DuplicateRecordFile>(stream, Shape)
                   ?? throw new InvalidDataException($"The embedded resource \"{Resource}\" is empty.");

        return new DuplicateRecordList(
            EnumSpelling.ToLinkMethod(list.Method),
            list.Confidence,
            list.Source,
            list.Merges,
            list.Splits);
    }
}

internal sealed record DuplicateRecordFile(
    string Method,
    double? Confidence,
    string Source,
    IReadOnlyList<DuplicateRecordPair> Merges,
    IReadOnlyList<DuplicateRecordSplit>? Splits = null);

internal sealed record DuplicateRecordList(
    LinkMethod Method,
    double? Confidence,
    string Source,
    IReadOnlyList<DuplicateRecordPair> Merges,
    IReadOnlyList<DuplicateRecordSplit>? Splits = null);

/// <summary>A second man a dataset's record holds, and the verses that are his.</summary>
/// <param name="From">The dataset's record, by address.</param>
/// <param name="To">His own record, by address.</param>
/// <param name="Name">
/// The name the dataset's record holds for him, which moves with him; null where both records hold the
/// name already and only the verses move.
/// </param>
/// <param name="Verses">The verses that are his, as spans: <c>JHN 1:45-49</c>.</param>
/// <param name="Why">Who ruled so, and on what.</param>
internal sealed record DuplicateRecordSplit(string From, string To, string? Name, IReadOnlyList<string> Verses, string Why);

/// <param name="Keeps">The record that stays, by address.</param>
/// <param name="Folds">The record folded into it, by address.</param>
/// <param name="Why">The verses that make them one person.</param>
/// <param name="Confidence">How sure the reading of this pair is, where it is less sure than the list.</param>
/// <param name="Source">Who said so, where the pair was not read with the rest of the list.</param>
internal sealed record DuplicateRecordPair(
    string StrongNumber,
    string Keeps,
    string Folds,
    string Why,
    double? Confidence = null,
    string? Source = null);
