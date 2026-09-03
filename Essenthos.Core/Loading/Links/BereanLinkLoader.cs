using System.Diagnostics;
using Essenthos.Core.Berean;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using NpgsqlTypes;

namespace Essenthos.Core.Loading.Links;

/// <param name="Divided">
/// Verses where the table's Greek and ours are not the same number of words. These are the textual
/// variants: the Berean's Greek carries readings the Nestle base does not, and the table marks each
/// with its edition's siglum. Refused whole rather than aligned partly, because a link built on a
/// misalignment is a claim about the wrong word and looks exactly like a correct one.
/// </param>
/// <param name="Drifted">
/// Verses where the English phrases do not account for the words of the verse in order. Nothing in
/// the measurement predicted any, so one is a fault worth seeing rather than a case to absorb.
/// </param>
/// <param name="Disputed">
/// Verses where fewer than half the Strong numbers agree. The two sides state their numbers
/// independently, so this is the check that a verse whose counts happen to match is really the same
/// verse.
/// </param>
/// <param name="Moved">
/// Words the table marks <c>. . .</c> — the Greek word is rendered, but somewhere else in the verse
/// and the file does not say where. No link is written: naming the wrong English words would be
/// worse than naming none, and calling it unrendered would be false.
/// </param>
internal sealed record BereanLinkOutcome(
    bool AlreadyLoaded,
    int Verses,
    int Links,
    int Absent,
    int Moved,
    int Divided,
    int Drifted,
    int Disputed,
    int NumbersCompared,
    int NumbersAgreeing,
    TimeSpan Elapsed)
{
    public override string ToString() =>
        AlreadyLoaded
            ? "the Berean is already linked to the Greek"
            : $"{Links} links over {Verses} verses in {Elapsed}: {Absent} Greek words the English does " +
              $"not render, {Moved} whose rendering the file places elsewhere, {Divided} verses the two " +
              $"divide differently, {Drifted} whose English did not line up, {Disputed} refused on their " +
              $"numbers; {NumbersAgreeing} of {NumbersCompared} Strong numbers agree " +
              $"({(NumbersCompared == 0 ? 0 : (double)NumbersAgreeing / NumbersCompared):P2})";
}

/// <summary>
/// Which Berean word renders which Greek word, stated by the Berean's own translation tables.
///
/// The corpus has had exactly one whole-Bible stated word mapping — the King James against the
/// Hebrew — and nothing at all of the kind for the New Testament, where the King James reaches
/// 72.7% of the Greek by inference from Strong numbers. This is a second, independent, human-made
/// answer, and it is the only thing the New Testament's methods can be calibrated against.
///
/// <para>
/// **The join is order, and the check is the number.** Their Greek is the Berean Greek Bible, which
/// is Nestle 1904 with modernised spelling, so the surface forms do not match — Nestle writes
/// Δαυείδ where they write Δαυίδ — and matching on the form would throw away most of the file. What
/// does hold is the word order, and both sides state a Strong number independently of each other and
/// of the spelling. Measured over the New Testament before this was written: 7,488 of 7,939 verses
/// have the same number of Greek words, and inside those, 128,029 of 129,491 Strong numbers agree —
/// **98.87%**. The 1.13% are two dictionaries disposing of the same suppletive verbs differently,
/// λέγω against εἶπον 992 times and ὁράω against ἰδού 212, which is a disagreement about lexicography
/// and not about which word is which.
/// </para>
///
/// <para>
/// So a verse is refused whole where the counts differ, and refused again where the numbers agree on
/// fewer than half its words — the second is the net that catches a verse whose counts match by
/// accident. Everything that survives is written as <c>stated-by-source</c> with no confidence,
/// because it is what the file says and not what we worked out.
/// </para>
/// </summary>
internal sealed class BereanLinkLoader(AppDbContext db, ILogger<BereanLinkLoader> logger)
{
    /// <summary>
    /// How much of a verse's Strong numbers must agree before its order is believed. Well below the
    /// 98.87% the corpus reaches, because the job is to catch a verse that is not the same verse,
    /// not to hold the file to a standard of lexicography.
    /// </summary>
    private const double Agreeing = 0.5;

    private const string Source =
        "Berean Standard Bible translation tables, bereanbible.com, public domain";

    private const string LinkImport =
        """
        COPY link (id, from_text_id, to_text_id, relation, method, confidence, source)
        FROM STDIN (FORMAT BINARY)
        """;

    private const string LinkWordImport =
        "COPY link_word (link_id, word_id, side) FROM STDIN (FORMAT BINARY)";

    public async Task<BereanLinkOutcome> Load(
        string tables,
        string witnessSlug,
        CancellationToken cancellationToken = default)
    {
        var from = await Text(BereanTextSource.Slug, cancellationToken);
        var to = await Text(witnessSlug, cancellationToken);

        if (from == 0 || to == 0)
        {
            return new BereanLinkOutcome(true, 0, 0, 0, 0, 0, 0, 0, 0, 0, TimeSpan.Zero);
        }

        if (await db.Links.AnyAsync(l => l.FromTextId == from && l.ToTextId == to, cancellationToken))
        {
            logger.LogInformation("The Berean is already linked to {Witness}; nothing to do", witnessSlug);
            return new BereanLinkOutcome(true, 0, 0, 0, 0, 0, 0, 0, 0, 0, TimeSpan.Zero);
        }

        if (!File.Exists(tables))
        {
            logger.LogWarning(
                "The Berean tables are not at {Path}, so the Berean is linked to nothing. They are 85 MB "
                + "and are fetched rather than committed; FTR-0182 says from where", tables);
            return new BereanLinkOutcome(true, 0, 0, 0, 0, 0, 0, 0, 0, 0, TimeSpan.Zero);
        }

        var started = Stopwatch.StartNew();
        var english = await Words(from, cancellationToken);
        var greek = await Words(to, cancellationToken);

        var drafts = new List<Draft>(150_000);
        int verses = 0, absent = 0, moved = 0, divided = 0, drifted = 0, disputed = 0;
        int compared = 0, agreeing = 0;

        foreach (var (reference, rows) in BereanTable.Verses(tables))
        {
            if (!BereanTextSource.Address(reference, out var book, out var chapter, out var number))
            {
                continue;
            }

            var address = (book, chapter, number);
            if (!rows[0].IsGreek
                || !english.TryGetValue(address, out var ours)
                || !greek.TryGetValue(address, out var witness))
            {
                continue;
            }

            if (rows.Count != witness.Count)
            {
                divided++;
                continue;
            }

            var (agreed, of) = Agreement(rows, witness);
            if (of > 0 && (double)agreed / of < Agreeing)
            {
                disputed++;
                continue;
            }

            if (Pair(rows, ours, witness, drafts, ref absent, ref moved) is false)
            {
                drifted++;
                continue;
            }

            verses++;
            compared += of;
            agreeing += agreed;
        }

        await Write(from, to, drafts, cancellationToken);

        var outcome = new BereanLinkOutcome(
            false, verses, drafts.Count, absent, moved, divided, drifted, disputed, compared, agreeing,
            started.Elapsed);
        logger.LogInformation("Linked the Berean to {Witness}: {Outcome}", witnessSlug, outcome);
        return outcome;
    }

    /// <summary>
    /// How many of the verse's Strong numbers the two sides state the same way, and how many either
    /// of them states at all.
    /// </summary>
    private static (int Agreed, int Of) Agreement(IReadOnlyList<BereanRow> rows, IReadOnlyList<Word> witness)
    {
        int agreed = 0, of = 0;
        for (var i = 0; i < rows.Count; i++)
        {
            if (rows[i].StrongNumber.Length == 0 || witness[i].Strong is not { Length: > 0 } theirs)
            {
                continue;
            }

            of++;
            if (string.Equals(rows[i].StrongNumber, theirs, StringComparison.Ordinal))
            {
                agreed++;
            }
        }

        return (agreed, of);
    }

    /// <summary>
    /// The English phrases against the verse's own words, in the file's English order.
    ///
    /// The phrases account for every word of a verse and nothing else — measured on 3,000 random
    /// verses, all 3,000 — so this walks the two in step and stops the whole verse the moment they
    /// part. Returning false rather than writing what it has is the point: a partial alignment
    /// produces links about the wrong words for the rest of the verse and looks exactly right.
    /// </summary>
    private static bool Pair(
        IReadOnlyList<BereanRow> rows,
        IReadOnlyList<Word> ours,
        IReadOnlyList<Word> witness,
        List<Draft> drafts,
        ref int absent,
        ref int moved)
    {
        var claimed = new List<long>[rows.Count];
        var at = 0;

        // The file's English order, kept as indices into the verse's own rows, so a row's rendering
        // and the Greek word it renders never have to be looked up by value.
        var byEnglish = Enumerable.Range(0, rows.Count)
            .OrderBy(index => rows[index].EnglishOrder)
            .ToList();

        foreach (var index in byEnglish)
        {
            var rendering = BereanWords.Rendering(rows[index].English);
            var mine = new List<long>(rendering.Count);

            foreach (var word in rendering)
            {
                if (at >= ours.Count || !BereanWords.Same(ours[at].Surface, word))
                {
                    return false;
                }

                mine.Add(ours[at].Id);
                at++;
            }

            claimed[index] = mine;
        }

        if (at != ours.Count)
        {
            return false;
        }

        for (var i = 0; i < rows.Count; i++)
        {
            var mine = claimed[i] ?? [];
            if (mine.Count > 0)
            {
                drafts.Add(new Draft(LinkRelation.Renders, mine, witness[i].Id));
                continue;
            }

            // The file distinguishes the two silences, and so does this. A dash is a Greek word the
            // English does not render; an ellipsis is one it renders somewhere else without saying
            // where, and calling that unrendered would be false.
            if (rows[i].English.Contains('.', StringComparison.Ordinal))
            {
                moved++;
            }
            else
            {
                absent++;
                drafts.Add(new Draft(LinkRelation.Omits, [], witness[i].Id));
            }
        }

        return true;
    }

    private async Task<Dictionary<(int, int, int), List<Word>>> Words(
        int textId,
        CancellationToken cancellationToken)
    {
        var rows = await db.Words
            .Where(word => word.TextId == textId)
            .Select(word => new
            {
                Book = word.Verse!.Book!.CanonicalOrdinal,
                word.Verse!.ChapterNumber,
                Verse = word.Verse!.Number,
                word.Id,
                word.Position,
                word.Surface,
                word.StrongNumber,
            })
            .ToListAsync(cancellationToken);

        return rows
            .GroupBy(row => (row.Book, row.ChapterNumber, row.Verse))
            .ToDictionary(
                group => group.Key,
                group => group.OrderBy(row => row.Position)
                    .Select(row => new Word(row.Id, row.Surface, row.StrongNumber))
                    .ToList());
    }

    private async Task Write(
        int fromTextId,
        int toTextId,
        List<Draft> drafts,
        CancellationToken cancellationToken)
    {
        if (drafts.Count == 0)
        {
            return;
        }

        await db.Database.OpenConnectionAsync(cancellationToken);
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
                await writer.WriteAsync(EnumSpelling.Of(drafts[i].Relation), NpgsqlDbType.Text, cancellationToken);
                await writer.WriteAsync(method, NpgsqlDbType.Text, cancellationToken);
                await writer.WriteNullAsync(cancellationToken);
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

                await Row(writer, firstId + i, drafts[i].To, toSide, cancellationToken);
            }

            await writer.CompleteAsync(cancellationToken);
        }

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
        await db.Texts.Where(t => t.Slug == slug).Select(t => t.Id).FirstOrDefaultAsync(cancellationToken);

    private sealed record Word(long Id, string Surface, string? Strong);

    private sealed record Draft(LinkRelation Relation, List<long> From, long To);
}
