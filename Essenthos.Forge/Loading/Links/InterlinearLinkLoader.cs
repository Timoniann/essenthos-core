using System.Diagnostics;
using System.Globalization;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Door43;
using Essenthos.Core.Corpus;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;

namespace Essenthos.Core.Loading.Links;

/// <param name="Refused">
/// Verses where the two sides did not line up word for word. Nothing is written for these: a
/// stated link that had to be guessed into place is not a stated link.
/// </param>
/// <param name="Links">The links the source stands on once the join is written.</param>
internal sealed record InterlinearOutcome(
    string Text,
    int Books,
    int Verses,
    int Refused,
    int Links,
    int Words,
    TimeSpan Elapsed,
    InterlinearReconciliation? Written = null)
{
    public override string ToString() =>
        Written is null
            ? $"{Text} is already linked from the interlinear"
            : $"{Links} links over {Verses} verses of {Books} books in {Elapsed}, covering {Words} words " +
              $"— {Refused} verses refused because the two sides did not line up. {Written}";
}

/// <summary>
/// What writing a join did to the links already there. A statement is added to what the corpus
/// holds rather than laid over it: a link another method drew over exactly the stated words takes
/// the statement as a second claim, and a link the source no longer states keeps whatever else
/// claims it.
/// </summary>
/// <param name="Kept">Links that already carried the statement and still do.</param>
/// <param name="Adopted">Links another method drew over exactly the stated words, which now carry the statement too.</param>
/// <param name="Written">Links nothing had drawn over the stated words.</param>
/// <param name="Retracted">Statements withdrawn from links whose words the source does not put together.</param>
/// <param name="Yielded">
/// Strong-number claims withdrawn from links that put a stated translated word on other original
/// words. The Strong matcher never writes a match over a word a source states; these were written
/// while the stored statement did not reach that word.
/// </param>
/// <param name="Demoted">Links withdrawn from that another claim still stands on, now headed by it.</param>
/// <param name="Removed">Links withdrawn from that no claim stands on any more, and removed.</param>
internal sealed record InterlinearReconciliation(
    int Kept,
    int Adopted,
    int Written,
    int Retracted,
    int Yielded,
    int Demoted,
    int Removed)
{
    public override string ToString() =>
        $"{Kept} stated links kept, {Adopted} drawn by another method over the same words now carry the " +
        $"statement, {Written} written; {Retracted} statements withdrawn and {Yielded} Strong-number claims " +
        $"yielded to the statement, leaving {Demoted} links headed by another claim and {Removed} removed";
}

/// <summary>
/// The stated word-level correspondence the Slavic texts have, which is the whole of what anyone
/// publishes for either of them.
///
/// Everything else the Ukrainian and the Synodal reach, they reach through a model: hundreds of
/// thousands of links to BHSA and to the Greek, every one of them inferred, none of them asserted
/// by anybody. unfoldingWord's alignment is people saying *this Ukrainian word renders that Hebrew
/// word*, and that is a different kind of claim — <c>stated-by-source</c>, no confidence, the same
/// standing as the King James mapping file.
///
/// The Russian half of it is three books, Titus, Philemon and 2 John, against 66. That is small
/// enough to be worth saying why it is here at all: it is not coverage, it is a standard. Until it
/// was loaded, every Russian link in the corpus came out of a model and nothing could say whether
/// any of them was right.
///
/// It joins without alignment because the source marks its own morpheme boundaries. BHSA holds
/// <c>וַ⁠יְהִי</c> as two words, the conjunction and the verb, and the interlinear writes it with
/// U+2060 in exactly that place and tags it <c>c:H1961</c>. So the pieces are matched to BHSA's
/// words by their form and by the occurrence the source states, and a span where that does not come
/// out exact is refused rather than forced — <see cref="InterlinearJoin"/> says how, and
/// <see cref="InterlinearJoinAccount"/> counts what was refused and why.
/// </summary>
internal sealed class InterlinearLinkLoader(AppDbContext db, ILogger<InterlinearLinkLoader> logger)
{
    /// <summary>
    /// The folder under <c>Resources/Door43</c> and the source recorded on the links, for each text
    /// an interlinear aligns. The source is what a reload finds the stored statements by, so the
    /// startup load and a reload have to spell it identically.
    /// </summary>
    public static (string Folder, string Source) Interlinear(string slug) => slug switch
    {
        Bible4uTextSource.Ohienko => ("uk_ubio",
            "unfoldingWord's Ukrainian Bible Interlinear Ogienko, git.door43.org/uk_ts/uk_ubio, CC BY-SA 4.0"),
        Bible4uTextSource.Synodal => ("ru_rsb",
            "Door43 Russian Synodal alignment of Titus, Philemon and 2 John, made in "
            + "translationCore and published at git.door43.org under CC0 1.0"),
        UnfoldingWordTextSource.Slug => (UnfoldingWordTextSource.Folder, UnfoldingWordSource),
        _ => throw new ArgumentException(
            $"Door43 interlinears exist for {Bible4uTextSource.Ohienko}, {Bible4uTextSource.Synodal} and " +
            $"{UnfoldingWordTextSource.Slug}; {slug} has none. Name one of those as the source, or score against " +
            "stored links instead."),
    };

    /// <summary>
    /// What the links drawn from the unfoldingWord Literal Text's alignment say about themselves. It
    /// names the release, because the alignment is revised from one release to the next.
    /// </summary>
    public const string UnfoldingWordSource =
        "unfoldingWord Literal Text alignment to unfoldingWord's Hebrew Bible and Greek New Testament, "
        + "release 90, git.door43.org/unfoldingWord/en_ult, CC BY-SA 4.0";

    private const string LinkImport =
        """
        COPY link (id, from_text_id, to_text_id, relation, method, source)
        FROM STDIN (FORMAT BINARY)
        """;

    private const string LinkWordImport =
        "COPY link_word (link_id, word_id, side) FROM STDIN (FORMAT BINARY)";

    public async Task<InterlinearOutcome> Load(
        string folder,
        string translationSlug,
        string source,
        CancellationToken cancellationToken = default)
    {
        var translation = await Text(translationSlug, cancellationToken);

        if (await db.Links.AnyAsync(
                l => l.FromTextId == translation && l.Method == LinkMethod.StatedBySource, cancellationToken))
        {
            logger.LogInformation("{Text} is already linked from the interlinear; nothing to do", translationSlug);
            return new InterlinearOutcome(translationSlug, 0, 0, 0, 0, 0, TimeSpan.Zero);
        }

        if (!Directory.Exists(folder))
        {
            logger.LogWarning("No interlinear at {Folder}; {Text} keeps only its aligned links", folder,
                translationSlug);
            return new InterlinearOutcome(translationSlug, 0, 0, 0, 0, 0, TimeSpan.Zero);
        }

        return await Write(folder, translationSlug, translation, source, cancellationToken);
    }

    /// <summary>
    /// The join written over the links the corpus already holds from the interlinear, where
    /// <see cref="Load"/> leaves a text that has any alone. It is how rows an older join wrote are
    /// brought to this one: what the join states is kept or added, a statement it no longer makes is
    /// withdrawn, and a link another method also claims keeps that claim.
    /// </summary>
    public async Task<InterlinearOutcome> Replace(
        string folder,
        string translationSlug,
        string source,
        CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(folder))
        {
            throw new DirectoryNotFoundException(
                $"No interlinear at {folder}. Point Dataset:ResourcesPath at the Resources folder that holds Door43/.");
        }

        return await Write(
            folder, translationSlug, await Text(translationSlug, cancellationToken), source, cancellationToken);
    }

    private async Task<InterlinearOutcome> Write(
        string folder,
        string translationSlug,
        int translation,
        string source,
        CancellationToken cancellationToken)
    {
        var started = Stopwatch.StartNew();
        var join = await Join(folder, translation, cancellationToken);
        var written = await Reconcile(translation, source, join.Drafts, cancellationToken);

        var outcome = new InterlinearOutcome(
            translationSlug,
            join.Books.Count,
            join.Total.VersesJoined,
            join.Total.VersesRead - join.Total.VersesJoined,
            written.Kept + written.Adopted + written.Written,
            join.Total.TranslatedWordsJoined,
            started.Elapsed,
            written);
        logger.LogInformation("Linked from the interlinear: {Outcome}", outcome);
        return outcome;
    }

    /// <summary>
    /// The same join <see cref="Load"/> makes, without writing it, beside the links the corpus already
    /// holds from the interlinear. It is how the answer key a benchmark reads is checked against the
    /// file it came from: what joined, what did not and why, and whether the stored rows are still
    /// this join.
    /// </summary>
    public async Task<InterlinearJoinReport> Measure(
        string folder,
        string translationSlug,
        CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(folder))
        {
            throw new DirectoryNotFoundException(
                $"No interlinear at {folder}. Point Dataset:ResourcesPath at the Resources folder that holds Door43/.");
        }

        var translation = await Text(translationSlug, cancellationToken);
        var join = await Join(folder, translation, cancellationToken);
        var stored = (await db.LinkWords.AsNoTracking()
                .Where(word => word.Link!.FromTextId == translation && word.Link.Method == LinkMethod.StatedBySource)
                .Select(word => new { word.LinkId, word.WordId, word.Side })
                .ToListAsync(cancellationToken))
            .GroupBy(word => word.LinkId)
            .Select(link => (
                From: Key(link.Where(word => word.Side == LinkSide.From).Select(word => word.WordId)),
                To: Key(link.Where(word => word.Side == LinkSide.To).Select(word => word.WordId))))
            .ToList();
        var joined = join.Drafts.Select(draft => (From: Key(draft.From), To: Key(draft.To))).ToHashSet();
        var joinedFrom = join.Drafts.Select(draft => Key(draft.From)).ToHashSet();
        var same = stored.Count(joined.Contains);
        var elsewhere = stored.Count(link => !joined.Contains(link) && joinedFrom.Contains(link.From));
        return new InterlinearJoinReport(
            translationSlug, folder, join.Books, join.Total, join.Drafts.Count,
            new StoredInterlinear(stored.Count, same, elsewhere, stored.Count - same - elsewhere));
    }

    /// <summary>
    /// The links this join makes, as word ids, without writing them: an answer key a measurement can
    /// read when the rows the corpus holds were written by an older join.
    /// </summary>
    public async Task<IReadOnlyList<(IReadOnlyList<long> From, IReadOnlyList<long> To)>> Pairs(
        string folder,
        string translationSlug,
        CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(folder))
        {
            throw new DirectoryNotFoundException(
                $"No interlinear at {folder}. Point Dataset:ResourcesPath at the Resources folder that holds Door43/.");
        }

        var join = await Join(folder, await Text(translationSlug, cancellationToken), cancellationToken);
        return [.. join.Drafts.Select(draft => ((IReadOnlyList<long>)draft.From, (IReadOnlyList<long>)draft.To))];
    }

    private static string Key(IEnumerable<long> words) => string.Join(',', words.Order());

    private async Task<InterlinearJoinResult> Join(string folder, int translation, CancellationToken cancellationToken)
    {
        var witnesses = new Dictionary<string, int>();
        foreach (var slug in (string[])[BhsaTextSource.Slug, NestleTextSource.Slug])
        {
            witnesses[slug] = await Text(slug, cancellationToken);
        }

        var drafts = new List<InterlinearDraft>(40_000);
        var books = new List<(string Book, InterlinearJoinAccount Account)>();
        var total = new InterlinearJoinAccount();

        foreach (var file in Directory.GetFiles(folder, "*.usfm").OrderBy(path => path))
        {
            var name = Path.GetFileNameWithoutExtension(file);
            var ordinal = Ordinal(Path.GetFileName(file));
            if (ordinal is null)
            {
                // Said out loud, because it used to be said to nobody. A file named for a book the
                // abbreviation table does not answer to is skipped whole, and skipping a book of a
                // stated source in silence is indistinguishable from the source not covering it.
                logger.LogWarning(
                    "{File} names no book this corpus knows, so nothing in it was read. Add the " +
                    "name it uses to BibleBookAbbreviation", file);
                continue;
            }

            var witness = witnesses[ordinal <= BookReferences.OldTestamentBookCount
                ? BhsaTextSource.Slug
                : NestleTextSource.Slug];
            var read = Usfm3AlignmentReader.Read(await File.ReadAllTextAsync(file, cancellationToken));
            var here = await Words(translation, ordinal.Value, cancellationToken);
            var there = await Words(witness, ordinal.Value, cancellationToken);
            var account = new InterlinearJoinAccount();
            var pairs = new List<InterlinearPair>();

            foreach (var verse in read)
            {
                var address = (verse.Chapter, verse.Number);
                InterlinearJoin.Verse(
                    $"{name} {verse.Chapter}:{verse.Number}",
                    verse,
                    here.GetValueOrDefault(address) ?? [],
                    Witness(there, address),
                    pairs,
                    account);
            }

            drafts.AddRange(pairs.Select(pair => new InterlinearDraft(translation, witness, pair.From, pair.To)));
            books.Add((name, account));
            total.Add(account);
        }

        return new InterlinearJoinResult(drafts, books, total);
    }

    /// <summary>
    /// The witness words a verse of the interlinear is joined against. A psalm's first verse is printed
    /// with its title at its head, and the witness numbers the title as a verse of its own, so the two
    /// are read as one: a title word the verse repeats then counts twice and is refused rather than
    /// guessed.
    /// </summary>
    private static List<InterlinearWord> Witness(
        Dictionary<(int, int), List<InterlinearWord>> there,
        (int Chapter, int Number) address) =>
        address.Number == 1 && there.TryGetValue((address.Chapter, 0), out var title)
            ? [.. title, .. there.GetValueOrDefault(address) ?? []]
            : there.GetValueOrDefault(address) ?? [];

    /// <summary>The book a file is for, from a name like <c>17-EST.usfm</c>.</summary>
    private static int? Ordinal(string fileName)
    {
        var hyphen = fileName.IndexOf('-');
        return hyphen < 0
            ? null
            : BookReferences.ResolveOrdinal(Path.GetFileNameWithoutExtension(fileName)[(hyphen + 1)..]);
    }

    private async Task<Dictionary<(int, int), List<InterlinearWord>>> Words(
        int textId,
        int ordinal,
        CancellationToken cancellationToken)
    {
        var rows = await db.VerseReferences
            .Where(r => r.IsPrimary && r.Verse!.TextId == textId && r.CanonicalBook == ordinal)
            .SelectMany(r => r.Verse!.Words.Select(w => new
            {
                r.CanonicalChapter,
                r.CanonicalVerse,
                w.Id,
                w.Position,
                w.NormalisedText,
                Written = w.Surface,
                w.StrongNumber,
                Language = w.Text!.Language,
            }))
            .ToListAsync(cancellationToken);

        return rows
            .GroupBy(r => (r.CanonicalChapter, r.CanonicalVerse))
            .ToDictionary(
                group => group.Key,
                group => group.OrderBy(r => r.Position)
                    .Select(r => new InterlinearWord(r.Id, r.NormalisedText ?? string.Empty, r.Language, r.Written, r.StrongNumber))
                    .ToList());
    }

    /// <summary>
    /// Writes a join's links against what the corpus already holds between the translation and the
    /// originals, in one transaction. The tests call it with drafts made by hand, because what is
    /// under test is the rows around them rather than the files.
    /// </summary>
    internal async Task<InterlinearReconciliation> Reconcile(
        int translation,
        string source,
        IReadOnlyList<InterlinearDraft> drafts,
        CancellationToken cancellationToken)
    {
        var distinct = drafts
            .DistinctBy(draft => (draft.ToTextId, Key(draft.From), Key(draft.To)))
            .ToList();

        await db.Database.OpenConnectionAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();
        (string, object)[] parameters =
        [
            ("translation", translation),
            ("source", source),
            ("stated", EnumSpelling.Of(LinkMethod.StatedBySource)),
            ("manual", EnumSpelling.Of(LinkMethod.Manual)),
            ("numbered", EnumSpelling.Of(LinkMethod.StrongNumber)),
            ("renders", EnumSpelling.Of(LinkRelation.Renders)),
        ];

        await Execute(connection, StageDrafts, cancellationToken);
        await using (var writer = await connection.BeginBinaryImportAsync(DraftImport, cancellationToken))
        {
            var fromSide = EnumSpelling.Of(LinkSide.From);
            var toSide = EnumSpelling.Of(LinkSide.To);
            for (var i = 0; i < distinct.Count; i++)
            {
                foreach (var (words, side) in (IEnumerable<(List<long>, string)>)
                         [(distinct[i].From, fromSide), (distinct[i].To, toSide)])
                {
                    foreach (var word in words)
                    {
                        await writer.StartRowAsync(cancellationToken);
                        await writer.WriteAsync(i, NpgsqlDbType.Integer, cancellationToken);
                        await writer.WriteAsync(distinct[i].ToTextId, NpgsqlDbType.Integer, cancellationToken);
                        await writer.WriteAsync(side, NpgsqlDbType.Text, cancellationToken);
                        await writer.WriteAsync(word, NpgsqlDbType.Bigint, cancellationToken);
                    }
                }
            }

            await writer.CompleteAsync(cancellationToken);
        }

        await Execute(connection, Classify, cancellationToken, parameters);
        var kept = await Count(connection, "SELECT count(*) FROM interlinear_match WHERE testified", cancellationToken);
        var adopted = await Count(
            connection, "SELECT count(*) FROM interlinear_match WHERE NOT testified", cancellationToken);

        await Execute(connection, Adopt, cancellationToken, parameters);
        await Execute(connection, Withdraw, cancellationToken, parameters);
        var retracted = await Count(
            connection, "SELECT count(*) FROM interlinear_withdrawn WHERE NOT yielded", cancellationToken);
        var yielded = await Count(
            connection, "SELECT count(*) FROM interlinear_withdrawn WHERE yielded", cancellationToken);
        var removed = await Execute(connection, RemoveUnclaimed, cancellationToken);
        var demoted = await Count(connection, Rehead, cancellationToken, parameters);

        var unmatched = new List<int>();
        await using (var command = new NpgsqlCommand(Unmatched, connection))
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                unmatched.Add(reader.GetInt32(0));
            }
        }

        if (unmatched.Count > 0)
        {
            var fresh = unmatched.Select(index => distinct[index]).ToList();
            var firstId = await ReserveLinkIds(connection, fresh.Count, cancellationToken);
            await WriteLinks(connection, source, fresh, firstId, cancellationToken);

            // The claim that says this loader is the one asserting these links, in the same
            // transaction: a link with no claim is invisible to the agreement measure.
            await LinkClaims.Record(connection, transaction, firstId, fresh.Count, cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return new InterlinearReconciliation(kept, adopted, unmatched.Count, retracted, yielded, demoted, removed);
    }

    private const string StageDrafts =
        """
        CREATE TEMP TABLE interlinear_draft (
            draft integer NOT NULL,
            to_text_id integer NOT NULL,
            side text NOT NULL,
            word_id bigint NOT NULL
        ) ON COMMIT DROP
        """;

    private const string DraftImport =
        "COPY interlinear_draft (draft, to_text_id, side, word_id) FROM STDIN (FORMAT BINARY)";

    /// <summary>
    /// Every link a draft could be the same as or could contradict: the ones touching a stated
    /// translated word, and the ones carrying this source's statement wherever they stand. A link is
    /// the same as a draft when it names exactly its words on both sides.
    ///
    /// <para>
    /// A link carrying a statement is headed by a statement or by a person, so the claims are only
    /// consulted for links of those two methods and not for the million guesses beside them.
    /// </para>
    /// </summary>
    private const string Classify =
        """
        CREATE TEMP TABLE interlinear_shape ON COMMIT DROP AS
        SELECT draft, min(to_text_id) AS to_text_id,
               string_agg(side || ':' || word_id, ',' ORDER BY side, word_id) AS shape
        FROM interlinear_draft
        GROUP BY draft;

        ANALYZE interlinear_draft;
        ANALYZE interlinear_shape;

        CREATE TEMP TABLE interlinear_nearby ON COMMIT DROP AS
        WITH touched AS (
            SELECT lw.link_id
            FROM interlinear_draft d
            JOIN link_word lw ON lw.word_id = d.word_id AND lw.side = 'from'
            WHERE d.side = 'from'
            UNION
            SELECT l.id
            FROM link l
            WHERE l.from_text_id = @translation AND l.method IN (@stated, @manual)
              AND EXISTS (SELECT 1 FROM link_claim c
                          WHERE c.link_id = l.id AND c.method = @stated AND c.source = @source)
        )
        SELECT l.id, l.to_text_id, l.relation,
               (SELECT string_agg(lw.side || ':' || lw.word_id, ',' ORDER BY lw.side, lw.word_id)
                FROM link_word lw WHERE lw.link_id = l.id) AS shape,
               EXISTS (SELECT 1 FROM link_claim c
                       WHERE c.link_id = l.id AND c.method = @stated AND c.source = @source) AS testified
        FROM touched t
        JOIN link l ON l.id = t.link_id
        WHERE l.from_text_id = @translation;

        ANALYZE interlinear_nearby;

        CREATE TEMP TABLE interlinear_match ON COMMIT DROP AS
        SELECT DISTINCT ON (s.draft) s.draft, n.id AS link_id, n.testified
        FROM interlinear_shape s
        JOIN interlinear_nearby n
          ON n.to_text_id = s.to_text_id AND n.shape = s.shape AND n.relation = @renders
        ORDER BY s.draft, n.testified DESC, n.id;

        ANALYZE interlinear_match;
        """;

    /// <summary>
    /// The statement, on every link that names exactly a draft's words. Where another method drew
    /// the link, its own columns are made a claim first, so heading it with the statement loses
    /// nothing it said before.
    /// </summary>
    private const string Adopt =
        """
        INSERT INTO link_claim (link_id, method, confidence, source, note)
        SELECT l.id, l.method, l.confidence, l.source, l.note
        FROM link l
        JOIN interlinear_match m ON m.link_id = l.id AND NOT m.testified
        ON CONFLICT DO NOTHING;

        INSERT INTO link_claim (link_id, method, confidence, source, note)
        SELECT link_id, @stated, NULL, @source, NULL FROM interlinear_match
        ON CONFLICT DO NOTHING;
        """;

    /// <summary>
    /// The statements the join no longer makes, and the Strong-number claims that put a stated
    /// translated word on other words of the same witness.
    /// </summary>
    private const string Withdraw =
        """
        CREATE TEMP TABLE interlinear_withdrawn ON COMMIT DROP AS
        WITH retracted AS (
            DELETE FROM link_claim c
            USING interlinear_nearby n
            WHERE c.link_id = n.id AND n.testified AND c.method = @stated AND c.source = @source
              AND NOT EXISTS (SELECT 1 FROM interlinear_match m WHERE m.link_id = n.id)
            RETURNING c.link_id
        ),
        yielded AS (
            DELETE FROM link_claim c
            USING interlinear_nearby n
            WHERE c.link_id = n.id AND c.method = @numbered
              AND NOT EXISTS (SELECT 1 FROM interlinear_match m WHERE m.link_id = n.id)
              AND EXISTS (
                  SELECT 1
                  FROM link_word lw
                  JOIN interlinear_draft d
                    ON d.word_id = lw.word_id AND d.side = 'from' AND d.to_text_id = n.to_text_id
                  WHERE lw.link_id = n.id AND lw.side = 'from')
            RETURNING c.link_id
        )
        SELECT link_id, false AS yielded FROM retracted
        UNION ALL
        SELECT link_id, true FROM yielded;
        """;

    private const string RemoveUnclaimed =
        """
        DELETE FROM link l
        WHERE l.id IN (SELECT link_id FROM interlinear_withdrawn)
          AND NOT EXISTS (SELECT 1 FROM link_claim c WHERE c.link_id = l.id)
        """;

    /// <summary>
    /// Heads every link whose claims changed with its strongest remaining claim, and counts the
    /// withdrawn-from links that end up headed by something other than a statement.
    /// </summary>
    private static string Rehead =>
        $"""
         WITH best AS (
             SELECT DISTINCT ON (c.link_id) c.link_id, c.method, c.confidence, c.source, c.note
             FROM link_claim c
             WHERE c.link_id IN (SELECT link_id FROM interlinear_withdrawn
                                 UNION SELECT link_id FROM interlinear_match)
             ORDER BY c.link_id, {Standing} DESC, c.confidence DESC NULLS FIRST, c.id
         ),
         headed AS (
             UPDATE link l
             SET method = best.method, confidence = best.confidence, source = best.source, note = best.note
             FROM best
             WHERE l.id = best.link_id
               AND (l.method, l.confidence, l.source, l.note)
                   IS DISTINCT FROM (best.method, best.confidence, best.source, best.note)
             RETURNING l.id, l.method
         )
         SELECT count(*) FROM headed
         WHERE method <> @stated AND id IN (SELECT link_id FROM interlinear_withdrawn)
         """;

    private const string Unmatched =
        """
        SELECT s.draft FROM interlinear_shape s
        WHERE NOT EXISTS (SELECT 1 FROM interlinear_match m WHERE m.draft = s.draft)
        ORDER BY s.draft
        """;

    /// <summary>How much each method knew before it started, written out of <see cref="ClaimStanding"/>.</summary>
    private static string Standing =>
        "CASE c.method "
        + string.Concat(Enum.GetValues<LinkMethod>().Select(method =>
            $"WHEN '{EnumSpelling.Of(method)}' THEN "
            + ClaimStanding.Of(method).ToString(CultureInfo.InvariantCulture) + " "))
        + "ELSE 0 END";

    private static async Task<int> Execute(
        NpgsqlConnection connection,
        string sql,
        CancellationToken cancellationToken,
        params (string Name, object Value)[] parameters)
    {
        await using var command = new NpgsqlCommand(sql, connection) { CommandTimeout = 600 };
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        return await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<int> Count(
        NpgsqlConnection connection,
        string sql,
        CancellationToken cancellationToken,
        params (string Name, object Value)[] parameters)
    {
        await using var command = new NpgsqlCommand(sql, connection) { CommandTimeout = 600 };
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        return (int)(long)(await command.ExecuteScalarAsync(cancellationToken))!;
    }

    private static async Task WriteLinks(
        NpgsqlConnection connection,
        string source,
        List<InterlinearDraft> drafts,
        long firstId,
        CancellationToken cancellationToken)
    {
        var relation = EnumSpelling.Of(LinkRelation.Renders);
        var method = EnumSpelling.Of(LinkMethod.StatedBySource);
        var fromSide = EnumSpelling.Of(LinkSide.From);
        var toSide = EnumSpelling.Of(LinkSide.To);

        await using (var writer = await connection.BeginBinaryImportAsync(LinkImport, cancellationToken))
        {
            for (var i = 0; i < drafts.Count; i++)
            {
                await writer.StartRowAsync(cancellationToken);
                await writer.WriteAsync(firstId + i, NpgsqlDbType.Bigint, cancellationToken);
                await writer.WriteAsync(drafts[i].FromTextId, NpgsqlDbType.Integer, cancellationToken);
                await writer.WriteAsync(drafts[i].ToTextId, NpgsqlDbType.Integer, cancellationToken);
                await writer.WriteAsync(relation, NpgsqlDbType.Text, cancellationToken);
                await writer.WriteAsync(method, NpgsqlDbType.Text, cancellationToken);
                await writer.WriteAsync(source, NpgsqlDbType.Text, cancellationToken);
            }

            await writer.CompleteAsync(cancellationToken);
        }

        await using (var writer = await connection.BeginBinaryImportAsync(LinkWordImport, cancellationToken))
        {
            for (var i = 0; i < drafts.Count; i++)
            {
                foreach (var word in drafts[i].From)
                {
                    await Row(writer, firstId + i, word, fromSide, cancellationToken);
                }

                foreach (var word in drafts[i].To)
                {
                    await Row(writer, firstId + i, word, toSide, cancellationToken);
                }
            }

            await writer.CompleteAsync(cancellationToken);
        }
    }

    private static async Task Row(
        NpgsqlBinaryImporter writer,
        long linkId,
        long wordId,
        string side,
        CancellationToken cancellationToken)
    {
        await writer.StartRowAsync(cancellationToken);
        await writer.WriteAsync(linkId, NpgsqlDbType.Bigint, cancellationToken);
        await writer.WriteAsync(wordId, NpgsqlDbType.Bigint, cancellationToken);
        await writer.WriteAsync(side, NpgsqlDbType.Text, cancellationToken);
    }

    private static async Task<long> ReserveLinkIds(
        NpgsqlConnection connection,
        int count,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            "SELECT setval(pg_get_serial_sequence('link', 'id'), " +
            "coalesce((SELECT max(id) FROM link), 0) + @count) - @count + 1", connection);
        command.Parameters.AddWithValue("count", count);
        return (long)(await command.ExecuteScalarAsync(cancellationToken))!;
    }

    private async Task<int> Text(string slug, CancellationToken cancellationToken) =>
        await db.Texts.Where(t => t.Slug == slug).Select(t => t.Id).FirstOrDefaultAsync(cancellationToken)
            is var id and not 0
            ? id
            : throw new InvalidOperationException($"The text \"{slug}\" must be loaded before it can be linked.");

    private sealed record InterlinearJoinResult(
        List<InterlinearDraft> Drafts,
        List<(string Book, InterlinearJoinAccount Account)> Books,
        InterlinearJoinAccount Total);
}

/// <summary>
/// The stated links the corpus holds from a translation today, set against the join made now.
/// </summary>
/// <param name="Same">Links whose words on both sides are exactly a link of this join.</param>
/// <param name="OnAnotherOriginal">
/// Links whose translated words this join also links, to other original words: a stored row on a
/// word the source did not state.
/// </param>
/// <param name="NotJoined">Links whose translated words this join does not link as a group at all.</param>
internal sealed record InterlinearDraft(int FromTextId, int ToTextId, List<long> From, List<long> To);

internal sealed record StoredInterlinear(int Links, int Same, int OnAnotherOriginal, int NotJoined);

internal sealed record InterlinearJoinReport(
    string Text,
    string Folder,
    IReadOnlyList<(string Book, InterlinearJoinAccount Account)> Books,
    InterlinearJoinAccount Total,
    int Links,
    StoredInterlinear Stored)
{
    public override string ToString() =>
        $"Interlinear join {Folder} → {Text}: {Links:N0} links would be written. The corpus holds " +
        $"{Stored.Links:N0} stated links from {Text}: {Stored.Same:N0} are links of this join, " +
        $"{Stored.OnAnotherOriginal:N0} put the same translated words on other original words, " +
        $"{Stored.NotJoined:N0} have translated words this join does not link as one group" +
        "\n" + string.Concat(Books.Select(book => $"\n{book.Book}\n{book.Account.Report(withExamples: false)}\n")) +
        $"\nall books\n{Total.Report(withExamples: true)}";
}
