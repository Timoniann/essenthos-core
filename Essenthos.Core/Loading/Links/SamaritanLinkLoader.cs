using System.Diagnostics;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Utils;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;

namespace Essenthos.Core.Loading.Links;

/// <param name="Expanded">
/// Words the Samaritan has and the Masoretic has not. This and <paramref name="Omitted"/> are the
/// numbers this text was loaded for: the corpus has never before been able to say, in Hebrew, that
/// one witness carries a word another does not.
/// </param>
/// <param name="Omitted">Words the Masoretic has and the Samaritan has not.</param>
/// <param name="Unpaired">
/// Verses one text numbers and the other does not — the altar of incense, which the Samaritan sets
/// after Exodus 26:35 rather than at Exodus 30, and Deuteronomy 34:2-3, which it writes as part of
/// 34:1. No link is written for either: the words are not missing, they are somewhere else, and an
/// <c>omits</c> there would be a false statement rather than an incomplete one.
/// </param>
internal sealed record SamaritanLinkOutcome(
    bool AlreadyLoaded,
    int Verses,
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
            ? "the Samaritan Pentateuch is already linked to BHSA"
            : $"{Links} links over {Verses} verses in {Elapsed}: {Identical} where the two witnesses write the " +
              $"same consonants, {Differing} where they write the same word differently, {Expanded} the " +
              $"Samaritan has and the Masoretic has not, {Omitted} the Masoretic has and the Samaritan has " +
              $"not, over {Unpaired} verses one numbers and the other does not. Plus and minus per book: " +
              string.Join("; ", ByBook.Select(b => $"{b.Book} +{b.Expanded} -{b.Omitted}"));
}

/// <summary>
/// The Samaritan Pentateuch against BHSA, word for word.
///
/// Nobody states this correspondence — there is no Samaritan-to-Masoretic word mapping anywhere —
/// so every link here is an inference, says <c>lexical</c>, and carries a confidence. What makes
/// the inference cheap is that both datasets come out of the same ETCBC encoding practice and cut
/// words into the same morphemes: <c>בראשית</c> is <c>ב</c> then <c>ראשית</c> on both sides. So the
/// two verses can be laid against each other letter for letter rather than guessed at.
///
/// <para>
/// The pairing is an alignment over the consonants of one verse against the consonants of the same
/// verse in the other text, and its scoring is the whole of the honesty here. Two words pair when
/// they are the same consonants, or when they are the same lexeme, or when they differ by a letter
/// or two — which is the Samaritan writing plene where the Masoretic writes defective, and is by
/// far the commonest difference between them. Two words that are none of those never pair: the
/// score for it is set below the cost of leaving both unpaired, so the alignment prefers to say
/// <em>this one has a word the other has not, twice</em> over inventing a correspondence.
/// </para>
///
/// <para>
/// Which side lacks the word is the relation's to carry, and a link with one empty side cannot say
/// it for itself: <c>expands</c> names words on the <c>from</c> side alone, which is the Samaritan,
/// and <c>omits</c> names words on the <c>to</c> side alone, which is BHSA.
/// </para>
/// </summary>
internal sealed class SamaritanLinkLoader(AppDbContext db, ILogger<SamaritanLinkLoader> logger)
{
    private const string Source =
        "the consonants both Hebrew witnesses write, aligned within each verse";

    private const string LinkImport =
        """
        COPY link (id, from_text_id, to_text_id, relation, method, confidence, source)
        FROM STDIN (FORMAT BINARY)
        """;

    private const string LinkWordImport =
        "COPY link_word (link_id, word_id, side) FROM STDIN (FORMAT BINARY)";

    public async Task<SamaritanLinkOutcome> Load(
        string fromSlug,
        string toSlug,
        CancellationToken cancellationToken = default)
    {
        var from = await Text(fromSlug, cancellationToken);
        var to = await Text(toSlug, cancellationToken);

        if (from.Versification != to.Versification || from.Versification == Versification.Unknown)
        {
            throw new InvalidOperationException(
                $"\"{fromSlug}\" numbers its verses as {from.Versification} and \"{toSlug}\" as " +
                $"{to.Versification}. This joins the two on the numbering they both use, so it needs both " +
                "to say what that numbering is and needs it to be the same one; a pair that follows two " +
                "schemes needs a loader that reads verse_reference instead.");
        }

        if (await db.Links.AnyAsync(l => l.FromTextId == from.Id && l.ToTextId == to.Id, cancellationToken))
        {
            logger.LogInformation("{From} and {To} are already linked; nothing to do", fromSlug, toSlug);
            return new SamaritanLinkOutcome(true, 0, 0, 0, 0, 0, 0, 0, [], TimeSpan.Zero);
        }

        var started = Stopwatch.StartNew();
        var here = await Words(from.Id, cancellationToken);
        var there = await Words(to.Id, cancellationToken);

        var drafts = new List<HebrewDraft>(130_000);
        var byBook = new SortedDictionary<int, (int Expanded, int Omitted)>();
        var verses = 0;
        var unpaired = 0;

        foreach (var (address, left) in here.OrderBy(entry => entry.Key.Book)
                     .ThenBy(entry => entry.Key.Chapter).ThenBy(entry => entry.Key.Verse))
        {
            if (!there.TryGetValue(address, out var right))
            {
                unpaired++;
                continue;
            }

            verses++;
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

        var outcome = new SamaritanLinkOutcome(
            false,
            verses,
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
    /// One verse of each witness laid against the other. The alignment decides what corresponds to
    /// what and how sure it is; this only turns its answer into rows, and the ids it hands back are
    /// positions within the two verses.
    /// </summary>
    private static void Pair(List<HebrewWord> left, List<HebrewWord> right, List<HebrewDraft> drafts)
    {
        var forms = HebrewWitnessAlignment.Pair(
            [.. left.Select(w => w.Form)], [.. right.Select(w => w.Form)]);

        foreach (var pairing in forms)
        {
            drafts.Add(new HebrewDraft(
                pairing.Relation,
                [.. pairing.From.Select(at => left[at].Id)],
                [.. pairing.To.Select(at => right[at].Id)],
                pairing.Confidence));
        }
    }

    /// <summary>
    /// Both witnesses' words, keyed by the address each gives the verse. They follow the same
    /// numbering, so this is the texts' own addresses rather than the shared frame — which is the
    /// stronger join here: the frame collapses BHSA's Numbers 25:19 and 26:1 onto one canonical
    /// address, and the two texts number that pair identically.
    /// </summary>
    private async Task<Dictionary<(int Book, int Chapter, int Verse), List<HebrewWord>>> Words(
        int textId,
        CancellationToken cancellationToken)
    {
        var rows = await db.Words
            .Where(w => w.TextId == textId)
            .Select(w => new
            {
                Book = w.Verse!.Book!.CanonicalOrdinal,
                Chapter = w.Verse!.ChapterNumber,
                Verse = w.Verse!.Number,
                w.Id,
                w.Position,
                w.Surface,
                w.Lemma,
            })
            .ToListAsync(cancellationToken);

        return rows
            .GroupBy(r => (r.Book, r.Chapter, r.Verse))
            .ToDictionary(
                group => group.Key,
                group => group
                    .OrderBy(r => r.Position)
                    .Select(r => new HebrewWord(
                        r.Id,
                        new HebrewForm(
                            HebrewLetters.Of(r.Surface), HebrewLetters.Of(r.Lemma ?? string.Empty))))
                    .ToList());
    }

    private async Task Write(
        int fromTextId,
        int toTextId,
        List<HebrewDraft> drafts,
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

    private sealed record HebrewWord(long Id, HebrewForm Form);

    private sealed record HebrewDraft(
        LinkRelation Relation,
        List<long> From,
        List<long> To,
        double Confidence);
}
