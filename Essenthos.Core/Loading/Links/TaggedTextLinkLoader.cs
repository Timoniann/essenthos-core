using System.Diagnostics;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Strong;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;

namespace Essenthos.Core.Loading.Links;

/// <param name="Tagged">
/// Words of the translation carrying a Strong number of the witness's series, which is everything
/// this method has to work with. Luther 1912 tags 365,350 of its 696,963 words and the untagged
/// half is the function words, which no source says anything about.
/// </param>
/// <param name="Unmatched">
/// Tagged words whose number no witness word in the verse carries, and that the dictionary could not
/// send anywhere either. Counted and never written: the translation may be rendering a longer text
/// than this corpus holds, or the match may simply have failed, and nothing here tells those apart.
/// </param>
/// <param name="Resolved">
/// Words matched through the lemma the dictionary names for their form rather than through a number
/// both sides write. Greek only — the derivations are the Greek concordance's.
/// </param>
/// <param name="Unplaced">
/// Verses of the translation holding a tagged word that the witness has no verse for at all. It is
/// the shape of the two canons against each other rather than a failure of the matching, so it is
/// counted apart from <paramref name="Unmatched"/>.
/// </param>
internal sealed record TaggedTextLinkOutcome(
    bool AlreadyLoaded,
    int Verses,
    int Unplaced,
    int Links,
    int Unambiguous,
    int Paired,
    int Contended,
    int Tagged,
    int Matched,
    int Unmatched,
    int Resolved,
    int Redirects,
    TimeSpan Elapsed)
{
    /// <summary>The share of the translation's tagged words that reached a word of the witness.</summary>
    public double Reached => Tagged == 0 ? 0 : (double)Matched / Tagged;

    public override string ToString() =>
        AlreadyLoaded
            ? "this pair is already linked by its Strong numbers"
            : $"{Links} links over {Verses} verses in {Elapsed}: {Matched} of {Tagged} tagged words " +
              $"reached the witness ({Reached:P1}), {Unambiguous} where the number was written once " +
              $"on each side, {Paired} where it stood the same number of times on both and they were " +
              $"paired in order, {Contended} where more than one word carried it, {Resolved} matched " +
              $"through the lemma the dictionary names for their form over {Redirects} numbers it " +
              $"resolved, {Unmatched} whose number no witness word in the verse carries, {Unplaced} " +
              "in a verse the witness does not have";
}

/// <summary>
/// A translation that carries its own Strong numbers, matched to a witness that carries them too.
///
/// This is the route Luther 1912 reaches Hebrew and Greek by, and the only one it has that is not
/// this project's own inference: 365,350 of its words arrive from eBible already tagged, and no
/// German text under any licence carries a word alignment (DOC-0192). The King James reaches the
/// Greek the same way, through <see cref="NewTestamentLinkLoader"/>, and the matching both use is
/// <see cref="StrongNumberMatch"/>. The difference is only where the tags are: the King James's
/// arrive in a separate edition that has to be laid onto the loaded words first, and Luther's are
/// on the corpus's own words because they came in the file the text did.
///
/// <para>
/// **Nothing here is <c>stated-by-source</c>, and the distinction is the point.** eBible states the
/// number; the correspondence is this loader's, drawn by matching that number inside a verse. A
/// Strong number is a lemma and not a token, so a verse using one lexeme twice does not say which
/// occurrence a word renders — every link therefore carries <c>strong-number</c> and a confidence,
/// and the ladder those sit on is in <see cref="StrongNumberMatch"/>.
/// </para>
///
/// <para>
/// **The tagging is known imperfect by its own lineage's account.** It descends from the Zefania
/// Strong module of 12/2005; toledot.info, which corrected the same layer, calls the family
/// <em>unvollständig und mit Fehlern behaftet</em>, and eBible holds the version before those
/// corrections (PRB-0375). So a link built from it is a good prior and not a reading, which is what
/// the confidence column is for.
/// </para>
/// </summary>
internal sealed class TaggedTextLinkLoader(AppDbContext db, ILogger<TaggedTextLinkLoader> logger)
{
    private const string Greek = "grc";

    private static string Source(string fromSlug, string toSlug) =>
        $"the Strong numbers {fromSlug} carries, matched within the verse against {toSlug}";

    private const string LinkImport =
        """
        COPY link (id, from_text_id, to_text_id, relation, method, confidence, source)
        FROM STDIN (FORMAT BINARY)
        """;

    private const string LinkWordImport =
        "COPY link_word (link_id, word_id, side) FROM STDIN (FORMAT BINARY)";

    public async Task<TaggedTextLinkOutcome> Load(
        string fromSlug,
        string toSlug,
        CancellationToken cancellationToken = default)
    {
        var from = await db.Texts.SingleOrDefaultAsync(t => t.Slug == fromSlug, cancellationToken);
        var to = await db.Texts.SingleOrDefaultAsync(t => t.Slug == toSlug, cancellationToken);
        if (from is null || to is null)
        {
            throw new InvalidOperationException(
                $"\"{fromSlug}\" and \"{toSlug}\" must both be loaded before the correspondences between them " +
                "can be. Load the texts first; this reads them, it does not create them.");
        }

        if (await db.Links.AnyAsync(l => l.FromTextId == from.Id && l.ToTextId == to.Id, cancellationToken))
        {
            logger.LogInformation("{From} and {To} are already linked; nothing to do", fromSlug, toSlug);
            return new TaggedTextLinkOutcome(true, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, TimeSpan.Zero);
        }

        var started = Stopwatch.StartNew();
        var series = to.Language == Greek ? StrongNumbers.Greek : StrongNumbers.Hebrew;
        var translation = await VerseWords(from.Id, cancellationToken);
        var witness = await VerseWords(to.Id, cancellationToken);

        var pairs = new List<VersePair>(24_000);
        var tagged = 0;
        var unplaced = 0;

        foreach (var (address, words) in translation)
        {
            var numbered = words.Where(word => word.Strong?[0] == series).ToList();
            if (numbered.Count == 0)
            {
                continue;
            }

            tagged += numbered.Count;
            if (!witness.TryGetValue(address, out var against))
            {
                unplaced += numbered.Count;
                continue;
            }

            pairs.Add(new VersePair(words, against));
        }

        var resolution = series == StrongNumbers.Greek
            ? await Resolution(pairs, cancellationToken)
            : [];

        var drafts = new List<Draft>(300_000);
        var matched = 0;
        var unmatched = 0;
        var resolved = 0;

        foreach (var pair in pairs)
        {
            var links = StrongNumberMatch.Verse(
                [.. pair.Translation
                    .Where(word => word.Strong?[0] == series)
                    .Select(word => new StrongNumberMatch.TaggedWord(word.Id, [word.Strong!]))],
                [.. pair.Witness.Select(word => new StrongNumberMatch.WitnessWord(word.Id, word.Strong))],
                resolution,
                out var tally);

            unmatched += tally.Unmatched;
            resolved += tally.Resolved;
            matched += links.Sum(link => link.From.Count);
            drafts.AddRange(links.Select(link => new Draft(link.From, link.To, link.Confidence, link.Kind)));
        }

        await Write(from.Id, to.Id, Source(fromSlug, toSlug), drafts, cancellationToken);

        var outcome = new TaggedTextLinkOutcome(
            false,
            pairs.Count,
            unplaced,
            drafts.Count,
            drafts.Count(d => d.Kind == StrongMatchKind.Unambiguous),
            drafts.Count(d => d.Kind == StrongMatchKind.Paired),
            drafts.Count(d => d.Kind == StrongMatchKind.Contended),
            tagged,
            matched,
            unmatched,
            resolved,
            resolution.Count,
            started.Elapsed);
        logger.LogInformation("{From} to {To}: {Outcome}", fromSlug, toSlug, outcome);
        return outcome;
    }

    /// <summary>
    /// The numbers the dictionary can join to the ones this Greek witness writes, kept only where
    /// the verses bear the join out. It runs over every verse before a single link is written,
    /// because a redirect is admitted on how often it explains a failure and one verse cannot say.
    /// </summary>
    private async Task<Dictionary<string, NumberRedirect>> Resolution(
        List<VersePair> pairs,
        CancellationToken cancellationToken)
    {
        var dictionary = await db.StrongEntries
            .Where(entry => entry.StrongNumber.StartsWith("G"))
            .Select(entry => new GreekEntry(entry.StrongNumber, entry.Lemma, entry.Derivation))
            .ToListAsync(cancellationToken);

        var attested = new HashSet<string>(StringComparer.Ordinal);
        foreach (var word in pairs.SelectMany(pair => pair.Witness).Where(word => word.Strong is not null))
        {
            attested.Add(word.Strong!);
        }

        return GreekNumberResolution.Admit(dictionary, attested, Occurrences(pairs));
    }

    private static IEnumerable<NumberOccurrence> Occurrences(List<VersePair> pairs)
    {
        foreach (var pair in pairs)
        {
            var numbers = pair.Witness
                .Where(word => word.Strong is not null)
                .Select(word => word.Strong!)
                .ToHashSet(StringComparer.Ordinal);

            foreach (var word in pair.Translation.Where(word => word.Strong?[0] == StrongNumbers.Greek))
            {
                yield return new NumberOccurrence(word.Strong!, numbers);
            }
        }
    }

    private async Task<Dictionary<(int, int, int), List<Word>>> VerseWords(
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
                w.StrongNumber,
            }))
            .ToListAsync(cancellationToken);

        return rows
            .GroupBy(r => (r.CanonicalBook, r.CanonicalChapter, r.CanonicalVerse))
            .ToDictionary(
                group => group.Key,
                group => group.OrderBy(r => r.Position)
                    .Select(r => new Word(r.Id, r.StrongNumber))
                    .ToList());
    }

    private async Task Write(
        int fromTextId,
        int toTextId,
        string source,
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
        var renders = EnumSpelling.Of(LinkRelation.Renders);
        var byNumber = EnumSpelling.Of(LinkMethod.StrongNumber);
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
                await writer.WriteAsync(renders, NpgsqlDbType.Text, cancellationToken);
                await writer.WriteAsync(byNumber, NpgsqlDbType.Text, cancellationToken);
                await writer.WriteAsync(drafts[i].Confidence, NpgsqlDbType.Double, cancellationToken);
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

    private sealed record Word(long Id, string? Strong);

    /// <summary>One canonical address, with both texts' words as they stand there.</summary>
    private sealed record VersePair(List<Word> Translation, List<Word> Witness);

    private sealed record Draft(List<long> From, List<long> To, double Confidence, StrongMatchKind Kind);
}
