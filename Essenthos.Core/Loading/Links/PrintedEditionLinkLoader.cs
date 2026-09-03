using System.Diagnostics;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.TextusReceptus;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;

namespace Essenthos.Core.Loading.Links;

/// <param name="Omitted">
/// Places where one edition has a word and the other has none. These are the first absences this
/// corpus has ever recorded rather than merely failed to fill, and they are the whole reason a
/// second printed edition is worth holding: not that the two mostly agree, but exactly where they
/// do not.
/// </param>
/// <param name="Differing">Places where both editions have a word and the words are not the same.</param>
internal sealed record PrintedEditionOutcome(
    bool AlreadyLoaded,
    int Verses,
    int Links,
    int Identical,
    int Differing,
    int Omitted,
    TimeSpan Elapsed)
{
    public override string ToString() =>
        AlreadyLoaded
            ? "the printed editions are already linked"
            : $"{Links} links over {Verses} verses in {Elapsed}: {Identical} where the two editions write the " +
              $"same word, {Differing} where they write different ones, {Omitted} where one has a word and the " +
              "other has none";
}

/// <summary>
/// Stephanus 1550 against Scrivener 1894, from the file that holds both.
///
/// Nothing is aligned here and nothing is guessed. Robinson's composite is one token stream with a
/// choice at 261 places, so the two editions are the same words everywhere else — the file itself
/// says which word corresponds to which, and where it offers a choice it says that too. Every link
/// carries <c>stated-by-source</c> and no confidence.
///
/// The 52 groups where one side is empty are the interesting ones. They are written with words on
/// one side and nothing on the other, which is what DOC-0007 built the link table to be able to
/// say: an absence recorded rather than a hole left. Which edition lacks the word is the relation's
/// to carry — <c>omits</c> where Stephanus does, <c>expands</c> where Scrivener does.
/// </summary>
internal sealed class PrintedEditionLinkLoader(AppDbContext db, ILogger<PrintedEditionLinkLoader> logger)
{
    private const string Source = "byztxt/greektext-textus-receptus, the variant groups of the composite";

    private const string LinkImport =
        """
        COPY link (id, from_text_id, to_text_id, relation, method, source)
        FROM STDIN (FORMAT BINARY)
        """;

    private const string LinkWordImport =
        "COPY link_word (link_id, word_id, side) FROM STDIN (FORMAT BINARY)";

    public async Task<PrintedEditionOutcome> Load(string folder, CancellationToken cancellationToken = default)
    {
        var from = await Text(TextusReceptusTextSource.Slug(Edition.Stephanus1550), cancellationToken);
        var to = await Text(TextusReceptusTextSource.Slug(Edition.Scrivener1894), cancellationToken);

        if (await db.Links.AnyAsync(l => l.FromTextId == from.Id && l.ToTextId == to.Id, cancellationToken))
        {
            logger.LogInformation("The printed editions are already linked; nothing to do");
            return new PrintedEditionOutcome(true, 0, 0, 0, 0, 0, TimeSpan.Zero);
        }

        var started = Stopwatch.StartNew();
        var stephanus = await VerseWords(from.Id, cancellationToken);
        var scrivener = await VerseWords(to.Id, cancellationToken);

        var drafts = new List<EditionDraft>(150_000);
        var verses = 0;

        foreach (var book in TextusReceptusTextSource.Books)
        {
            var content = await File.ReadAllTextAsync(
                Path.Combine(folder, "parsed", $"{book}.UTR"), cancellationToken);

            var first = UtrReader.Read(content, Edition.Stephanus1550);
            var second = UtrReader.Read(content, Edition.Scrivener1894);
            var canonical = TextusReceptusTextSource.Canonical(book);

            foreach (var verse in first)
            {
                var address = (canonical, verse.Chapter, verse.Number);
                if (!stephanus.TryGetValue(address, out var left) || !scrivener.TryGetValue(address, out var right))
                {
                    continue;
                }

                var other = second.FirstOrDefault(v => v.Chapter == verse.Chapter && v.Number == verse.Number);
                if (other is null || verse.Words.Count != left.Count || other.Words.Count != right.Count)
                {
                    continue;
                }

                verses++;
                Pair(verse, other, left, right, drafts);
            }
        }

        await Write(from.Id, to.Id, drafts, cancellationToken);

        var outcome = new PrintedEditionOutcome(
            false,
            verses,
            drafts.Count,
            drafts.Count(d => d.Relation == LinkRelation.Equals),
            drafts.Count(d => d.Relation == LinkRelation.Renders),
            drafts.Count(d => d.Relation is LinkRelation.Omits or LinkRelation.Expands),
            started.Elapsed);

        logger.LogInformation("Linked the printed editions: {Outcome}", outcome);
        return outcome;
    }

    /// <summary>
    /// Lays the two readings of one verse against each other by the segment each word came from.
    /// A segment is one word outside a variant group and one group inside it, counted the same way
    /// on both sides, so this needs no alignment and admits no guess.
    /// </summary>
    private static void Pair(
        UtrVerse first,
        UtrVerse second,
        List<long> left,
        List<long> right,
        List<EditionDraft> drafts)
    {
        var bySegment = first.Words
            .Select((word, at) => (word, at))
            .GroupBy(entry => entry.word.Segment)
            .ToDictionary(group => group.Key, group => group.ToList());

        var otherBySegment = second.Words
            .Select((word, at) => (word, at))
            .GroupBy(entry => entry.word.Segment)
            .ToDictionary(group => group.Key, group => group.ToList());

        foreach (var segment in bySegment.Keys.Union(otherBySegment.Keys).Order())
        {
            var here = bySegment.GetValueOrDefault(segment, []);
            var there = otherBySegment.GetValueOrDefault(segment, []);

            // A segment present on one side only is a word one edition prints and the other does
            // not. Stored positively, which is what turns a difference between editions from a gap
            // into something a reader can be shown — and which way round it stands is the relation's
            // to say, since a row with one empty side cannot say it on its own.
            var relation = here.Count == 0
                ? LinkRelation.Omits
                : there.Count == 0
                    ? LinkRelation.Expands
                    : Same(here, there) ? LinkRelation.Equals : LinkRelation.Renders;

            drafts.Add(new EditionDraft(
                relation,
                [.. here.Select(entry => left[entry.at])],
                [.. there.Select(entry => right[entry.at])]));
        }
    }

    private static bool Same(
        List<(UtrWord word, int at)> here,
        List<(UtrWord word, int at)> there) =>
        here.Count == there.Count
        && here.Select(entry => entry.word.Surface).SequenceEqual(there.Select(entry => entry.word.Surface));

    private async Task<Dictionary<(int, int, int), List<long>>> VerseWords(
        int textId,
        CancellationToken cancellationToken)
    {
        var rows = await db.VerseReferences
            .Where(r => r.IsPrimary && r.Verse!.TextId == textId)
            .SelectMany(r => r.Verse!.Words.Select(w => new
            {
                r.CanonicalBook,
                r.CanonicalChapter,
                r.CanonicalVerse,
                w.Position,
                w.Id,
            }))
            .ToListAsync(cancellationToken);

        return rows
            .GroupBy(r => (r.CanonicalBook, r.CanonicalChapter, r.CanonicalVerse))
            .ToDictionary(
                group => group.Key,
                group => group.OrderBy(r => r.Position).Select(r => r.Id).ToList());
    }

    private async Task Write(
        int fromTextId,
        int toTextId,
        List<EditionDraft> drafts,
        CancellationToken cancellationToken)
    {
        if (drafts.Count == 0)
        {
            return;
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();

        var firstId = await ReserveLinkIds(connection, drafts.Count, cancellationToken);
        var method = EnumSpelling.Of(LinkMethod.StatedBySource);
        var fromSide = EnumSpelling.Of(LinkSide.From);
        var toSide = EnumSpelling.Of(LinkSide.To);

        await using (var writer = await connection.BeginBinaryImportAsync(LinkImport, cancellationToken))
        {
            for (var i = 0; i < drafts.Count; i++)
            {
                await writer.StartRowAsync(cancellationToken);
                await writer.WriteAsync(firstId + i, NpgsqlDbType.Bigint, cancellationToken);
                await writer.WriteAsync(fromTextId, NpgsqlDbType.Integer, cancellationToken);
                await writer.WriteAsync(toTextId, NpgsqlDbType.Integer, cancellationToken);
                await writer.WriteAsync(
                    EnumSpelling.Of(drafts[i].Relation), NpgsqlDbType.Text, cancellationToken);
                await writer.WriteAsync(method, NpgsqlDbType.Text, cancellationToken);
                await writer.WriteAsync(Source, NpgsqlDbType.Text, cancellationToken);
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

    private async Task<Database.Entities.Text> Text(string slug, CancellationToken cancellationToken) =>
        await db.Texts.SingleOrDefaultAsync(t => t.Slug == slug, cancellationToken)
        ?? throw new InvalidOperationException(
            $"The text \"{slug}\" must be loaded before it can be linked. This reads its words; it does not " +
            "create them.");

    private sealed record EditionDraft(LinkRelation Relation, List<long> From, List<long> To);
}
