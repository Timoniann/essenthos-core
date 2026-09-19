using System.Diagnostics;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.TextusReceptus;
using Essenthos.Core.Utils;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;

namespace Essenthos.Core.Loading.Links;

/// <param name="Expanded">
/// Words the first edition prints and the second does not. This and <paramref name="Omitted"/> are
/// what a second Septuagint was loaded for: until now no Greek word of either edition could be
/// shown against the other's, and the deuterocanon had no counterpart in this corpus at all.
/// </param>
/// <param name="Omitted">Words the second edition prints and the first does not.</param>
/// <param name="Unpaired">
/// Addresses one edition fills and the other does not — the Psalms of Solomon, which Brenton does
/// not print, and Ecclesiastes, which this transcription of Swete does not supply. No link is
/// written for either: a book the other edition never contained is not a word it omits.
/// </param>
internal sealed record SeptuagintLinkOutcome(
    bool AlreadyLoaded,
    int Addresses,
    int Links,
    int Identical,
    int Differing,
    int Expanded,
    int Omitted,
    int Unpaired,
    IReadOnlyList<(string Book, int Expanded, int Omitted)> ByBook,
    TimeSpan Elapsed)
{
    public override string ToString() =>
        AlreadyLoaded
            ? "the two Septuagints are already linked"
            : $"{Links} links over {Addresses} shared addresses in {Elapsed}: {Identical} where the two " +
              $"editions write the same letters, {Differing} where they write the same word differently, " +
              $"{Expanded} the first prints and the second does not, {Omitted} the second prints and the " +
              $"first does not, over {Unpaired} addresses only one of them fills. Plus and minus per book: " +
              string.Join("; ", ByBook.Select(b => $"{b.Book} +{b.Expanded} -{b.Omitted}"));
}

/// <summary>
/// Swete's Septuagint against Brenton's, word for word.
///
/// Two Greek editions of one work, which is the pairing this corpus was rebuilt to hold and the one
/// it has been unable to make: Swete arrived linked to nothing at all, and Brenton's deuterocanon —
/// Tobit, Judith, the Maccabees, Sirach, Wisdom — stood against nothing, because BHSA has no such
/// books and BHSA was the only Old Testament witness here.
///
/// <para>
/// Nothing about it is the aligner's problem. Both sides are Greek, so the evidence is the letters
/// each edition prints, and <see cref="WitnessAlignment"/> either finds them or reports that it did
/// not. That is the same method the Samaritan Pentateuch is joined to BHSA by, and it is why these
/// links carry a confidence near certainty where a Slavic translation's carry a model's guess.
/// </para>
///
/// <para>
/// The two do not read the same text and are not meant to. Swete printed Codex Vaticanus as it
/// stands; Brenton printed a text to be translated from. So this writes far more absences than the
/// two Greek New Testaments do against each other, and every one of them is a reading a scholar can
/// ask about rather than a defect in the join.
/// </para>
///
/// <para>
/// **The join is the canonical frame, not the editions' own numbering.** Both declare the
/// Septuagint versification and they still divide their text differently in most of the books they
/// share — that difference is the reason to hold both — so the addresses they agree on are the
/// frame's, which is what the frame loaders placed them at. An address either edition
/// fills with more than one verse is taken whole, so the words of both verses stand in one bag and
/// no link crosses a boundary the frame does not already join.
/// </para>
/// </summary>
internal sealed class SeptuagintLinkLoader(AppDbContext db, ILogger<SeptuagintLinkLoader> logger)
{
    internal const string Source =
        "the letters both Greek editions print, aligned within each verse of the canonical frame";

    private const string LinkImport =
        """
        COPY link (id, from_text_id, to_text_id, relation, method, confidence, source)
        FROM STDIN (FORMAT BINARY)
        """;

    private const string LinkWordImport =
        "COPY link_word (link_id, word_id, side) FROM STDIN (FORMAT BINARY)";

    public async Task<SeptuagintLinkOutcome> Load(
        string fromSlug,
        string toSlug,
        CancellationToken cancellationToken = default)
    {
        var from = await Text(fromSlug, cancellationToken);
        var to = await Text(toSlug, cancellationToken);

        if (from.Language != to.Language)
        {
            throw new InvalidOperationException(
                $"\"{fromSlug}\" is in {from.Language} and \"{toSlug}\" is in {to.Language}. This compares two " +
                "witnesses by the letters they write, which is only evidence while both write the same " +
                "alphabet; a pair in two languages needs the aligner.");
        }

        if (await db.Links.AnyAsync(l => l.FromTextId == from.Id && l.ToTextId == to.Id, cancellationToken))
        {
            logger.LogInformation("{From} and {To} are already linked; nothing to do", fromSlug, toSlug);
            return new SeptuagintLinkOutcome(true, 0, 0, 0, 0, 0, 0, 0, [], TimeSpan.Zero);
        }

        var started = Stopwatch.StartNew();
        var here = await Words(from.Id, cancellationToken);
        var there = await Words(to.Id, cancellationToken);

        var drafts = new List<GreekDraft>(600_000);
        var byBook = new SortedDictionary<int, (int Expanded, int Omitted)>();
        var addresses = 0;
        var unpaired = 0;

        foreach (var (address, left) in here.OrderBy(entry => entry.Key))
        {
            if (!there.TryGetValue(address, out var right))
            {
                unpaired++;
                continue;
            }

            addresses++;
            var before = drafts.Count;
            Pair(left, right, drafts);

            var expanded = 0;
            var omitted = 0;
            for (var i = before; i < drafts.Count; i++)
            {
                expanded += drafts[i].Relation == LinkRelation.Expands ? 1 : 0;
                omitted += drafts[i].Relation == LinkRelation.Omits ? 1 : 0;
            }

            var counted = byBook.GetValueOrDefault(address.Book);
            byBook[address.Book] = (counted.Expanded + expanded, counted.Omitted + omitted);
        }

        unpaired += there.Keys.Count(address => !here.ContainsKey(address));

        await Write(from.Id, to.Id, drafts, cancellationToken);

        var outcome = new SeptuagintLinkOutcome(
            false,
            addresses,
            drafts.Count,
            drafts.Count(d => d.Relation == LinkRelation.Equals),
            drafts.Count(d => d.Relation == LinkRelation.Renders),
            drafts.Count(d => d.Relation == LinkRelation.Expands),
            drafts.Count(d => d.Relation == LinkRelation.Omits),
            unpaired,
            [.. byBook.Select(entry => (
                BibleBookAbbreviation.GetByOrdinal(entry.Key)?.FullName.Full ?? entry.Key.ToString(),
                entry.Value.Expanded,
                entry.Value.Omitted))],
            started.Elapsed);

        logger.LogInformation("Linked {From} to {To}: {Outcome}", fromSlug, toSlug, outcome);
        return outcome;
    }

    /// <summary>
    /// One address of each edition laid against the other. The alignment decides what corresponds to
    /// what and how sure it is; this only turns its answer into rows.
    /// </summary>
    private static void Pair(List<GreekWord> left, List<GreekWord> right, List<GreekDraft> drafts)
    {
        var forms = WitnessAlignment.Pair(
            [.. left.Select(w => w.Form)], [.. right.Select(w => w.Form)]);

        foreach (var pairing in forms)
        {
            drafts.Add(new GreekDraft(
                pairing.Relation,
                [.. pairing.From.Select(at => left[at].Id)],
                [.. pairing.To.Select(at => right[at].Id)],
                pairing.Confidence));
        }
    }

    /// <summary>
    /// An edition's words, keyed by the address the frame places each verse at and ordered as the
    /// edition prints them.
    ///
    /// Brenton carries a lemma on nearly every word and Swete carries none, so the lexeme half of a
    /// form is empty on one side of this pair and the alignment falls back to the letters. That is a
    /// fact about the sources rather than about the method: give Swete lemmas and the same code
    /// gains a second kind of evidence without changing.
    /// </summary>
    private async Task<Dictionary<(int Book, int Chapter, int Verse), List<GreekWord>>> Words(
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
                r.Verse!.ChapterNumber,
                VerseNumber = r.Verse!.Number,
                w.Id,
                w.Position,
                w.Surface,
                w.Lemma,
            }))
            .ToListAsync(cancellationToken);

        return rows
            .GroupBy(r => (r.CanonicalBook, r.CanonicalChapter, r.CanonicalVerse))
            .ToDictionary(
                group => group.Key,
                group => group
                    .OrderBy(r => r.ChapterNumber).ThenBy(r => r.VerseNumber).ThenBy(r => r.Position)
                    .Select(r => new GreekWord(
                        r.Id,
                        new WitnessForm(
                            GreekLetters.Bare(r.Surface), GreekLetters.Bare(r.Lemma ?? string.Empty))))
                    .ToList());
    }

    private async Task Write(
        int fromTextId,
        int toTextId,
        List<GreekDraft> drafts,
        CancellationToken cancellationToken)
    {
        if (drafts.Count == 0)
        {
            return;
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();

        var firstId = await ReserveLinkIds(connection, drafts.Count, cancellationToken);
        var method = EnumSpelling.Of(LinkMethod.Lexical);
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
                await writer.WriteAsync(drafts[i].Confidence, NpgsqlDbType.Double, cancellationToken);
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

    private sealed record GreekWord(long Id, WitnessForm Form);

    private sealed record GreekDraft(
        LinkRelation Relation,
        List<long> From,
        List<long> To,
        double Confidence);
}
