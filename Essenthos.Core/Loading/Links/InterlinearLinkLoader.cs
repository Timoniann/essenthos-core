using System.Diagnostics;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Door43;
using Essenthos.Core.Endpoints;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;

namespace Essenthos.Core.Loading.Links;

/// <param name="Refused">
/// Verses where the two sides did not line up word for word. Nothing is written for these: a
/// stated link that had to be guessed into place is not a stated link.
/// </param>
internal sealed record InterlinearOutcome(
    string Text,
    int Books,
    int Verses,
    int Refused,
    int Links,
    int Words,
    TimeSpan Elapsed)
{
    public override string ToString() =>
        Links == 0
            ? $"{Text} is already linked from the interlinear"
            : $"{Links} links over {Verses} verses of {Books} books in {Elapsed}, covering {Words} words " +
              $"— {Refused} verses refused because the two sides did not line up";
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

        var started = Stopwatch.StartNew();
        var join = await Join(folder, translation, cancellationToken);
        await Write(source, join.Drafts, cancellationToken);

        var outcome = new InterlinearOutcome(
            translationSlug,
            join.Books.Count,
            join.Total.VersesJoined,
            join.Total.VersesRead - join.Total.VersesJoined,
            join.Drafts.Count,
            join.Total.TranslatedWordsJoined,
            started.Elapsed);
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
                    there.GetValueOrDefault(address) ?? [],
                    pairs,
                    account);
            }

            drafts.AddRange(pairs.Select(pair => new InterlinearDraft(translation, witness, pair.From, pair.To)));
            books.Add((name, account));
            total.Add(account);
        }

        return new InterlinearJoinResult(drafts, books, total);
    }

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
                Language = w.Text!.Language,
            }))
            .ToListAsync(cancellationToken);

        return rows
            .GroupBy(r => (r.CanonicalChapter, r.CanonicalVerse))
            .ToDictionary(
                group => group.Key,
                group => group.OrderBy(r => r.Position)
                    .Select(r => new InterlinearWord(r.Id, r.NormalisedText ?? string.Empty, r.Language, r.Written))
                    .ToList());
    }

    private async Task Write(string source, List<InterlinearDraft> drafts, CancellationToken cancellationToken)
    {
        if (drafts.Count == 0)
        {
            return;
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();

        var firstId = await ReserveLinkIds(connection, drafts.Count, cancellationToken);
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

        // The claim that says this loader is the one asserting these links. Written here rather
        // than left to a backfill: a link with no claim is invisible to the agreement measure, and
        // the measure spent a day reporting the migration instead of the corpus. PRB-0198.
        await LinkClaims.Record(connection, transaction, firstId, drafts.Count, cancellationToken);

        await transaction.CommitAsync(cancellationToken);
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

    private sealed record InterlinearDraft(int FromTextId, int ToTextId, List<long> From, List<long> To);

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
