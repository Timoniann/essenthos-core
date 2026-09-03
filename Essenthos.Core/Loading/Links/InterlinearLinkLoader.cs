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
/// The one stated word-level correspondence a Slavic text has.
///
/// Everything else the Ukrainian reaches, it reaches through a model: 396,093 links to BHSA, every
/// one of them inferred, none of them asserted by anybody. unfoldingWord's interlinear is people
/// saying *this Ukrainian word renders that Hebrew word*, and that is a different kind of claim —
/// <c>stated-by-source</c>, no confidence, the same standing as the King James mapping file.
///
/// It joins without alignment because the source marks its own morpheme boundaries. BHSA holds
/// <c>וַ⁠יְהִי</c> as two words, the conjunction and the verb, and the interlinear writes it with
/// U+2060 in exactly that place and tags it <c>c:H1961</c>. So the pieces are matched to BHSA's
/// words by their folded form, in order, and a verse where that does not come out exact is
/// refused rather than forced — <see cref="InterlinearOutcome.Refused"/> counts those, and they
/// keep the links this does write worth the name.
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
        var witnesses = new Dictionary<string, int>();
        foreach (var slug in (string[])["bhsa", "nestle1904"])
        {
            witnesses[slug] = await Text(slug, cancellationToken);
        }

        var drafts = new List<InterlinearDraft>(40_000);
        var books = 0;
        var verses = 0;
        var refused = 0;
        var words = 0;

        foreach (var file in Directory.GetFiles(folder, "*.usfm").OrderBy(path => path))
        {
            var ordinal = Ordinal(Path.GetFileName(file));
            if (ordinal is null)
            {
                continue;
            }

            var witness = witnesses[ordinal <= BookReferences.OldTestamentBookCount ? "bhsa" : "nestle1904"];
            var read = Usfm3AlignmentReader.Read(File.ReadAllText(file));
            var here = await Words(translation, ordinal.Value, cancellationToken);
            var there = await Words(witness, ordinal.Value, cancellationToken);
            books++;

            foreach (var verse in read)
            {
                var address = (verse.Chapter, verse.Number);
                if (!here.TryGetValue(address, out var translated) || !there.TryGetValue(address, out var original))
                {
                    refused++;
                    continue;
                }

                var made = Pair(verse, translated, original, translation, witness, drafts);
                if (made == 0)
                {
                    refused++;
                }
                else
                {
                    verses++;
                    words += made;
                }
            }
        }

        await Write(source, drafts, cancellationToken);

        var outcome = new InterlinearOutcome(
            translationSlug, books, verses, refused, drafts.Count, words, started.Elapsed);
        logger.LogInformation("Linked from the interlinear: {Outcome}", outcome);
        return outcome;
    }

    /// <summary>
    /// One verse, span by span.
    ///
    /// Both sides are walked forward with a cursor, and a span whose pieces are not all found is
    /// skipped without moving either cursor. That is what makes partial matching safe here: the
    /// cursors only ever pass words that were confirmed, so a span that fails cannot push the
    /// following ones onto the wrong words.
    ///
    /// Skipping matters because the sources are not the same editions. unfoldingWord aligns
    /// against its own Hebrew and its own Greek, and ours are BHSA and Nestle 1904 — the Hebrew
    /// divides some words differently and the Greek is a different text altogether. Refusing a
    /// whole verse for one word that does not exist in our edition threw away 91% of what the
    /// interlinear states.
    /// </summary>
    private static int Pair(
        AlignedVerse verse,
        List<FoldedWord> translated,
        List<FoldedWord> original,
        int fromTextId,
        int toTextId,
        List<InterlinearDraft> drafts)
    {
        var made = 0;
        var here = 0;
        var there = 0;

        foreach (var span in verse.Spans)
        {
            var (ours, afterOurs) = Find(translated, span.Words, "ukr", here);
            var (theirs, afterTheirs) = Find(original, span.Morphemes, original[0].Language, there);
            if (ours.Count == 0 || theirs.Count == 0)
            {
                continue;
            }

            drafts.Add(new InterlinearDraft(fromTextId, toTextId, ours, theirs));
            here = afterOurs;
            there = afterTheirs;
            made += ours.Count;
        }

        return made;
    }

    /// <summary>
    /// The words of a span, found in order from the cursor. Answers an empty list when any one of
    /// them is missing, so the caller can leave the cursor where it was.
    /// </summary>
    private static (List<long> Words, int After) Find(
        List<FoldedWord> words,
        IReadOnlyList<string> wanted,
        string? language,
        int from)
    {
        var found = new List<long>(wanted.Count);
        var at = from;

        foreach (var one in wanted)
        {
            var folded = WordFolding.Fold(one, language);
            var next = words.FindIndex(at, candidate => candidate.Folded == folded);
            if (next < 0)
            {
                return ([], from);
            }

            found.Add(words[next].Id);
            at = next + 1;
        }

        return (found, at);
    }

    /// <summary>The book a file is for, from a name like <c>17-EST.usfm</c>.</summary>
    private static int? Ordinal(string fileName)
    {
        var hyphen = fileName.IndexOf('-');
        return hyphen < 0
            ? null
            : BookReferences.ResolveOrdinal(Path.GetFileNameWithoutExtension(fileName)[(hyphen + 1)..]);
    }

    private async Task<Dictionary<(int, int), List<FoldedWord>>> Words(
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
                Language = w.Text!.Language,
            }))
            .ToListAsync(cancellationToken);

        return rows
            .GroupBy(r => (r.CanonicalChapter, r.CanonicalVerse))
            .ToDictionary(
                group => group.Key,
                group => group.OrderBy(r => r.Position)
                    .Select(r => new FoldedWord(r.Id, r.NormalisedText ?? string.Empty, r.Language))
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

    private sealed record FoldedWord(long Id, string Folded, string Language);

    private sealed record InterlinearDraft(int FromTextId, int ToTextId, List<long> From, List<long> To);
}
