using System.Diagnostics;
using Essenthos.Core.Berean;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Utils;
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
            : $"{Links} links over {Verses} verses in {Elapsed}: {Absent} original words the English does " +
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
/// 77.2% of the Greek by inference from Strong numbers. This is a second, independent, human-made
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

    /// <summary>
    /// A withdrawal cascades through a few hundred thousand link words and their claims, which does
    /// not finish inside the default thirty seconds.
    /// </summary>
    private static readonly TimeSpan WithdrawTimeout = TimeSpan.FromMinutes(30);

    private const char OpenParagraph = 'פ';

    private const char ClosedParagraph = 'ס';

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

        // Guarded on this source rather than on the pair: the aligner may already have spoken about
        // these two texts, and what it said is not what this file says.
        if (await db.Links.AnyAsync(
                l => l.FromTextId == from && l.ToTextId == to && l.Source == Source, cancellationToken))
        {
            logger.LogInformation("The Berean is already linked to {Witness}; nothing to do", witnessSlug);
            return new BereanLinkOutcome(true, 0, 0, 0, 0, 0, 0, 0, 0, 0, TimeSpan.Zero);
        }

        if (!File.Exists(tables))
        {
            logger.LogWarning(
                "The Berean tables are not at {Path}, so the Berean is linked to nothing. They are 85 MB "
                + "and are fetched rather than committed; run scripts/fetch-berean.ps1", tables);
            return new BereanLinkOutcome(true, 0, 0, 0, 0, 0, 0, 0, 0, 0, TimeSpan.Zero);
        }

        var started = Stopwatch.StartNew();
        var greekWitness = await db.Texts
            .Where(t => t.Id == to).Select(t => t.Language).FirstAsync(cancellationToken) == "grc";
        var english = await Words(from, cancellationToken);
        var greek = await Words(to, cancellationToken);

        var drafts = new List<Draft>(150_000);
        var covered = new List<int>(30_000);
        int verses = 0, absent = 0, moved = 0, divided = 0, drifted = 0, disputed = 0;
        int compared = 0, agreeing = 0;

        foreach (var (reference, rows) in BereanTable.Verses(tables))
        {
            if (!BereanTextSource.Address(reference, out var book, out var chapter, out var number))
            {
                continue;
            }

            // The file holds both testaments; a load is about one witness, so the rows that speak
            // about the other language are not this run's business.
            var address = (book, chapter, number);
            if (rows[0].IsGreek != greekWitness
                || !english.TryGetValue(address, out var ours)
                || !greek.TryGetValue(address, out var witness))
            {
                continue;
            }

            List<List<long>> run;
            var agreed = 0;
            var of = 0;

            if (rows[0].IsGreek)
            {
                // The Berean's Greek is Nestle respelled, so it joins by order and is checked by the
                // Strong number both sides state independently of the spelling.
                if (rows.Count != witness.Count)
                {
                    divided++;
                    continue;
                }

                (agreed, of) = Agreement(rows, witness);
                if (of > 0 && (double)agreed / of < Agreeing)
                {
                    disputed++;
                    continue;
                }

                run = [.. witness.Select(word => new List<long> { word.Id })];
            }
            else if (Letters(
                         [.. rows.Select(row => HebrewLetters.Of(row.Original))],
                         [.. witness.Select(word => HebrewLetters.Of(word.Surface))]) is { } matched)
            {
                run = [.. matched.Select(words => words.Select(at => witness[at].Id).ToList())];
            }
            else
            {
                divided++;
                continue;
            }

            if (Pair(rows, ours, run, drafts, ref absent, ref moved) is false)
            {
                drifted++;
                continue;
            }

            verses++;
            covered.Add(ours[0].VerseId);
            compared += of;
            agreeing += agreed;
        }

        await Write(from, to, drafts, cancellationToken);
        var replaced = await Supersede(from, to, covered, cancellationToken);

        var outcome = new BereanLinkOutcome(
            false, verses, drafts.Count, absent, moved, divided, drifted, disputed, compared, agreeing,
            started.Elapsed);
        logger.LogInformation(
            "Linked the Berean to {Witness}: {Outcome}; {Replaced} aligner links superseded",
            witnessSlug, outcome, replaced);
        return outcome;
    }

    /// <summary>
    /// Every link these tables wrote between the Berean and one witness, removed so that the next
    /// <see cref="Load"/> draws them again under the reading as it now stands.
    ///
    /// Only this source's links: the aligner's are left where the tables were silent, and the load
    /// that follows supersedes them wherever it now speaks. Against Nestle it also removes the claims
    /// Clear Bible added to these links, so that pair is redrawn after this one and not before.
    /// </summary>
    public async Task<int> Withdraw(string witnessSlug, CancellationToken cancellationToken = default)
    {
        var from = await Text(BereanTextSource.Slug, cancellationToken);
        var to = await Text(witnessSlug, cancellationToken);

        db.Database.SetCommandTimeout(WithdrawTimeout);
        return await db.Database.ExecuteSqlRawAsync(
            "DELETE FROM link WHERE from_text_id = {0} AND to_text_id = {1} AND source = {2}",
            [from, to, Source], cancellationToken);
    }

    /// <summary>
    /// Drops the aligner's answer for the verses a source has now spoken about.
    ///
    /// A model's guess and a person's statement about the same two words are not two facts, and
    /// keeping both would make every one of these words look contended when nothing is in doubt.
    /// The guess is also much the cheaper of the two to get back: <c>align</c> and <c>compose</c>
    /// rebuild it in minutes, and a stated mapping cannot be rebuilt at all.
    ///
    /// Only the verses this load covered. Where it refused one -- the two editions genuinely
    /// differing -- the aligner is all there is, and it stays.
    /// </summary>
    private async Task<int> Supersede(
        int fromTextId,
        int toTextId,
        List<int> verses,
        CancellationToken cancellationToken)
    {
        if (verses.Count == 0)
        {
            return 0;
        }

        return await db.Database.ExecuteSqlRawAsync(
            """
            DELETE FROM link l
            USING link_word lw, word w
            WHERE lw.link_id = l.id AND lw.side = 'from' AND w.id = lw.word_id
              AND l.from_text_id = {0} AND l.to_text_id = {1} AND l.method = 'aligner'
              AND w.verse_id = ANY({2})
            """,
            [fromTextId, toTextId, verses.ToArray()], cancellationToken);
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
    /// The Hebrew, which does not join by order and cannot join by number.
    ///
    /// BHSA writes the preposition, the article and the noun of לָאוֹר as three words and the
    /// Westminster edition the Berean follows writes one, so the counts never agree — 21.4% of
    /// verses, measured. Their Strong numbers do not agree either, because the numbering is
    /// OpenScriptures' against ETCBC's: 43.8%. What both editions preserve exactly is the letters.
    ///
    /// <para>
    /// So the verse is joined on its letters and each Berean word renders the BHSA words its letters
    /// fall in. The division runs both ways: BHSA also writes a name as one word where the Westminster
    /// edition writes two — בֵּית לֶחֶם, בֵּית־אֵל — and then both Berean words render the one BHSA
    /// word, which is what the two editions' letters say and all they say. A word that prints no
    /// letters, an article assimilated into its preposition, goes with the Berean word whose letters
    /// follow it; trailing ones go with nothing.
    /// </para>
    ///
    /// <para>
    /// Returns null unless the whole verse writes the same letters. A verse that differs anywhere — a
    /// qere against a ketiv, a word one edition prints and the other does not — has no place where
    /// the letters can be trusted to line the rest up, and a partial run would pair every remaining
    /// word with the wrong Hebrew and look exactly like a correct verse.
    /// </para>
    /// </summary>
    /// <returns>For each Berean word, the indices of the BHSA words it renders.</returns>
    internal static List<List<int>>? Letters(IReadOnlyList<string> berean, IReadOnlyList<string> bhsa)
    {
        var whole = string.Concat(bhsa);
        if (!string.Equals(string.Concat(berean), whole, StringComparison.Ordinal))
        {
            berean = WithoutSectionMarks(berean, whole) ?? berean;
            if (!string.Equals(string.Concat(berean), whole, StringComparison.Ordinal))
            {
                return null;
            }
        }

        var starts = new int[bhsa.Count + 1];
        for (var at = 0; at < bhsa.Count; at++)
        {
            starts[at + 1] = starts[at] + bhsa[at].Length;
        }

        var runs = new List<List<int>>(berean.Count);
        var start = 0;

        foreach (var letters in berean)
        {
            var end = start + letters.Length;
            var run = new List<int>();

            for (var at = 0; at < bhsa.Count && starts[at] <= end; at++)
            {
                var (from, to) = (starts[at], starts[at + 1]);
                if ((from < end && start < to) || (from == to && start <= from && from < end))
                {
                    run.Add(at);
                }
            }

            runs.Add(run);
            start = end;
        }

        return runs;
    }

    /// <summary>
    /// The Westminster edition's paragraph marks, taken off the word they are printed against.
    ///
    /// It prints <c>פ</c> for an open paragraph and <c>ס</c> for a closed one, and where a section
    /// ends inside a verse the mark is glued to the last word of the section — יִשְׂרָאֵל פ, הוּאס —
    /// with nothing but its place to tell it from a letter. So a final פ or ס is dropped only where
    /// the word does not match BHSA with it and does without it, and the caller still demands the
    /// whole verse agree.
    /// </summary>
    private static List<string>? WithoutSectionMarks(IReadOnlyList<string> berean, string bhsa)
    {
        var unmarked = new List<string>(berean.Count);
        var at = 0;

        foreach (var letters in berean)
        {
            if (bhsa.AsSpan(at).StartsWith(letters, StringComparison.Ordinal))
            {
                unmarked.Add(letters);
            }
            else if (letters.Length > 1
                     && letters[^1] is OpenParagraph or ClosedParagraph
                     && bhsa.AsSpan(at).StartsWith(letters.AsSpan(0, letters.Length - 1), StringComparison.Ordinal))
            {
                unmarked.Add(letters[..^1]);
            }
            else
            {
                return null;
            }

            at += unmarked[^1].Length;
        }

        return unmarked;
    }

    /// <summary>
    /// The English phrases against the verse's own words, in the file's English order.
    ///
    /// The phrases account for every word of a verse and nothing else — measured on 3,000 random
    /// verses, all 3,000 — so this walks the two in step and stops the whole verse the moment they
    /// part. Returning false rather than writing what it has is the point: a partial alignment
    /// produces links about the wrong words for the rest of the verse and looks exactly right.
    /// </summary>
    internal static bool Pair(
        IReadOnlyList<BereanRow> rows,
        IReadOnlyList<Word> ours,
        IReadOnlyList<List<long>> witness,
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

        // Two Berean words can share one witness word where the editions divide a name differently,
        // so a word some row renders is not also unrendered, and a word two silent rows share is
        // unrendered once.
        var rendered = new HashSet<long>();
        for (var i = 0; i < rows.Count; i++)
        {
            if (claimed[i] is { Count: > 0 })
            {
                rendered.UnionWith(witness[i]);
            }
        }

        var omitted = new HashSet<long>();
        for (var i = 0; i < rows.Count; i++)
        {
            var mine = claimed[i] ?? [];
            if (mine.Count > 0)
            {
                drafts.Add(new Draft(LinkRelation.Renders, mine, witness[i]));
                continue;
            }

            // The file distinguishes the two silences, and so does this. A dash is a Greek word the
            // English does not render; an ellipsis is one it renders somewhere else without saying
            // where, and calling that unrendered would be false.
            if (rows[i].English.Contains('.', StringComparison.Ordinal))
            {
                moved++;
                continue;
            }

            var unrendered = witness[i].Where(word => !rendered.Contains(word) && omitted.Add(word)).ToList();
            if (unrendered.Count > 0)
            {
                absent++;
                drafts.Add(new Draft(LinkRelation.Omits, [], unrendered));
            }
        }

        return true;
    }

    private Task<Dictionary<(int, int, int), List<Word>>> Words(
        int textId,
        CancellationToken cancellationToken) =>
        WordsByAddress(db, textId, cancellationToken);

    internal static async Task<Dictionary<(int, int, int), List<Word>>> WordsByAddress(
        AppDbContext db,
        int textId,
        CancellationToken cancellationToken)
    {
        // By the canonical address and not by each text's own numbering. BHSA numbers 2,036 of its
        // verses differently from the English scheme the Berean follows, and pairing by the printed
        // number puts those links on the wrong verse -- which the verification caught as twelve word
        // links crossing a verse pair nothing joins.
        // Where two of a text's verses stand at one address -- BHSA's 1 Kings 22:43 and 22:44 are the
        // English 22:43 -- their words are read one verse after the other, not interleaved by
        // position.
        var rows = await db.VerseReferences
            .Where(reference => reference.IsPrimary && reference.Verse!.TextId == textId)
            .SelectMany(reference => reference.Verse!.Words.Select(word => new
            {
                Book = reference.CanonicalBook,
                ChapterNumber = reference.CanonicalChapter,
                Verse = reference.CanonicalVerse,
                word.Id,
                word.VerseId,
                PrintedChapter = word.Verse!.ChapterNumber,
                PrintedVerse = word.Verse!.Number,
                word.Position,
                word.Surface,
                word.StrongNumber,
            }))
            .ToListAsync(cancellationToken);

        return rows
            .GroupBy(row => (row.Book, row.ChapterNumber, row.Verse))
            .ToDictionary(
                group => group.Key,
                group => group
                    .OrderBy(row => row.PrintedChapter)
                    .ThenBy(row => row.PrintedVerse)
                    .ThenBy(row => row.Position)
                    .Select(row => new Word(row.Id, row.VerseId, row.Surface, row.StrongNumber))
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

                foreach (var word in drafts[i].To)
                {
                    await Row(writer, firstId + i, word, toSide, cancellationToken);
                }
            }

            await writer.CompleteAsync(cancellationToken);
        }

        // The claim that says this loader is the one asserting these links. Written here rather
        // than left to a backfill: a link with no claim is invisible to the agreement measure, and
        // the measure spent a day reporting the migration instead of the corpus.
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
        await db.Texts.Where(t => t.Slug == slug).Select(t => t.Id).FirstOrDefaultAsync(cancellationToken);

    internal sealed record Word(long Id, int VerseId, string Surface, string? Strong);

    internal sealed record Draft(LinkRelation Relation, List<long> From, List<long> To);
}
