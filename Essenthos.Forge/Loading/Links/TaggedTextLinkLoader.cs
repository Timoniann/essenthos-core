using System.Diagnostics;
using Essenthos.Core.Corpus;
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
/// <param name="Confirmed">
/// Aligner links naming words a match names too, folded into the match as a second claim.
/// <paramref name="Exact"/> of them name exactly the same words.
/// </param>
/// <param name="Contradicted">
/// Aligner links whose translated word the numbers give to other witness words and whose witness
/// word they give to other translated words — removed, because a printed number outranks a guess.
/// </param>
/// <param name="Beside">
/// Aligner links from a tagged word to a witness word no number reaches, kept: the numbers are
/// silent about the witness word rather than contrary.
/// </param>
/// <param name="Testified">
/// Matches not written because a source already states what their translated words render.
/// <paramref name="Corroborated"/> of them named the same words, and became a claim on that link.
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
    int Confirmed,
    int Exact,
    int Contradicted,
    int Beside,
    int Testified,
    int Corroborated,
    TimeSpan Elapsed)
{
    /// <summary>The share of the translation's tagged words that reached a word of the witness.</summary>
    public double Reached => Tagged == 0 ? 0 : (double)Matched / Tagged;

    public override string ToString() =>
        AlreadyLoaded
            ? "this pair is already linked by these Strong numbers"
            : $"{Links} links over {Verses} verses in {Elapsed}: {Matched} of {Tagged} tagged words " +
              $"reached the witness ({Reached:P1}), {Unambiguous} where the number was written once " +
              $"on each side, {Paired} where it stood the same number of times on both and they were " +
              $"paired in order, {Contended} where more than one word carried it, {Resolved} matched " +
              $"through the lemma the dictionary names for their form over {Redirects} numbers it " +
              $"resolved, {Unmatched} whose number no witness word in the verse carries, {Unplaced} " +
              $"in a verse the witness does not have. Aligner links: {Confirmed} confirmed and folded " +
              $"in as a claim ({Exact} naming exactly the same words), {Contradicted} contradicted and " +
              $"removed, {Beside} beside a number and kept. {Testified} matches left to a source that " +
              $"states the words already, {Corroborated} of them agreeing with it";
}

/// <param name="Tags">The numbers the edition puts on each of the translation's words, by word id.</param>
/// <param name="Credit">
/// What the links drawn from it say they rest on, which is also the prefix the dataset declaration
/// claims — the words are the only place a link records where its numbers came from.
/// </param>
internal sealed record EditionNumbers(IReadOnlyDictionary<long, WordTag> Tags, string Credit);

/// <summary>
/// A translation that carries its own Strong numbers, matched to a witness that carries them too.
///
/// This is the route Luther 1912 reaches Hebrew and Greek by, and the only one it has that is not
/// this project's own inference: 365,350 of its words arrive from eBible already tagged, and no
/// German text under any licence carries a word alignment. The King James reaches the
/// Greek the same way, through <see cref="NewTestamentLinkLoader"/>, and the matching both use is
/// <see cref="StrongNumberMatch"/>. The difference is only where the tags are: the King James's
/// arrive in a separate edition that has to be laid onto the loaded words first, and Luther's are
/// on the corpus's own words because they came in the file the text did.
///
/// <para>
/// **The Synodal's numbers are neither.** They are Bob Jones University's, in an edition whose terms
/// allow it to be used only unmodified, so they are laid onto the Synodal's words for the length of
/// one run (<see cref="SynodalStrongLinkLoader"/>) and never written to them. The links are the only
/// thing that reaches the database, and they are drawn by exactly the matching Luther's are.
/// </para>
///
/// <para>
/// **Nothing here is <c>stated-by-source</c>, and the distinction is the point.** The edition states
/// the number; the correspondence is this loader's, drawn by matching that number inside a verse. A
/// Strong number is a lemma and not a token, so a verse using one lexeme twice does not say which
/// occurrence a word renders — every link therefore carries <c>strong-number</c> and a confidence,
/// and the ladder those sit on is in <see cref="StrongNumberMatch"/>.
/// </para>
///
/// <para>
/// **The tagging is known imperfect by its own lineage's account.** It descends from the Zefania
/// Strong module of 12/2005; toledot.info, which corrected the same layer, calls the family
/// <em>unvollständig und mit Fehlern behaftet</em>, and eBible holds the version before those
/// corrections. So a link built from it is a good prior and not a reading, which is what
/// the confidence column is for.
/// </para>
///
/// <para>
/// Where the pair already has links, the standing decides between them — a source's statement is
/// not overwritten, and the aligner's guess is folded in or removed (<see cref="NumberedLinkSettlement"/>).
/// </para>
/// </summary>
internal sealed class TaggedTextLinkLoader(AppDbContext db, ILogger<TaggedTextLinkLoader> logger)
{
    private const string Greek = "grc";

    internal static string Source(string fromSlug, string toSlug) =>
        $"the Strong numbers {fromSlug} carries, matched within the verse against {toSlug}";

    private static string Source(EditionNumbers edition, string toSlug) =>
        $"{edition.Credit}, matched within the verse against {toSlug}";

    /// <summary>
    /// The note on a claim a source's link is given where the numbers reach exactly its words. The
    /// link keeps the source's own method; this says a printed number agrees with it.
    /// </summary>
    private const string CorroborationNote = "the Strong numbers name the same words";

    /// <summary>
    /// The note on an aligner claim folded into a match naming more words than the aligner did —
    /// the aligner reached one pair of them, and saying so keeps the claim from reading as more.
    /// </summary>
    private const string WithinNote = "the aligner proposed one word pair of this link";

    private const string LinkImport =
        """
        COPY link (id, from_text_id, to_text_id, relation, method, confidence, source)
        FROM STDIN (FORMAT BINARY)
        """;

    private const string LinkWordImport =
        "COPY link_word (link_id, word_id, side) FROM STDIN (FORMAT BINARY)";

    private const string FoldTable =
        "CREATE TEMP TABLE settled_fold (guess bigint, kept bigint, exact boolean) ON COMMIT DROP";

    private const string FoldImport =
        "COPY settled_fold (guess, kept, exact) FROM STDIN (FORMAT BINARY)";

    /// <summary>
    /// Every claim the folded link carried, and its own columns for a link written before claims
    /// were kept, moved onto the match that names its words.
    /// </summary>
    private const string FoldClaims =
        """
        INSERT INTO link_claim (link_id, method, confidence, source, note)
        SELECT f.kept, c.method, c.confidence, c.source,
               CASE WHEN f.exact THEN c.note ELSE coalesce(c.note, @within) END
        FROM settled_fold f
        JOIN LATERAL (
            SELECT method, confidence, source, note FROM link_claim WHERE link_id = f.guess
            UNION
            SELECT method, confidence, source, note FROM link WHERE id = f.guess
        ) c ON true
        ON CONFLICT DO NOTHING
        """;

    private const string RemoveSettled = "DELETE FROM link WHERE id = ANY(@ids)";

    public Task<TaggedTextLinkOutcome> Load(
        string fromSlug,
        string toSlug,
        CancellationToken cancellationToken = default) =>
        Load(fromSlug, toSlug, null, cancellationToken);

    /// <param name="edition">
    /// Numbers a separate edition puts on the translation's words, held for this run and never
    /// written to them; null reads the numbers the words carry themselves.
    /// </param>
    /// <param name="only">
    /// Translation words written after the pair was linked, and the only ones whose matches are
    /// written: every verse is matched as it always is, so a word is matched against the same
    /// neighbours as before, and a link of these numbers already naming another word of such a
    /// match gives way to it. Null matches the whole pair, once.
    /// </param>
    public async Task<TaggedTextLinkOutcome> Load(
        string fromSlug,
        string toSlug,
        EditionNumbers? edition,
        CancellationToken cancellationToken = default,
        IReadOnlySet<long>? only = null)
    {
        var from = await db.Texts.SingleOrDefaultAsync(t => t.Slug == fromSlug, cancellationToken);
        var to = await db.Texts.SingleOrDefaultAsync(t => t.Slug == toSlug, cancellationToken);
        if (from is null || to is null)
        {
            throw new InvalidOperationException(
                $"\"{fromSlug}\" and \"{toSlug}\" must both be loaded before the correspondences between them " +
                "can be. Load the texts first; this reads them, it does not create them.");
        }

        var source = edition is null ? Source(fromSlug, toSlug) : Source(edition, toSlug);

        // The source names both texts, so links written before either was renamed carry the names
        // they had then; those are this loader's own links all the same.
        var written = TextAliases.Of(fromSlug).Append(fromSlug)
            .SelectMany(
                _ => TextAliases.Of(toSlug).Append(toSlug),
                (translation, witness) => edition is null ? Source(translation, witness) : Source(edition, witness))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        // Only this loader's own links, from these numbers: a pair the aligner already reached is
        // not a pair the numbers have spoken about.
        if (only is null && await db.Links.AnyAsync(
                l => l.FromTextId == from.Id && l.ToTextId == to.Id
                     && l.Method == LinkMethod.StrongNumber && written.Contains(l.Source),
                cancellationToken))
        {
            logger.LogInformation("{From} and {To} are already linked by these numbers; nothing to do", fromSlug, toSlug);
            return new TaggedTextLinkOutcome(
                true, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, TimeSpan.Zero);
        }

        var started = Stopwatch.StartNew();
        var series = to.Language == Greek ? StrongNumbers.Greek : StrongNumbers.Hebrew;
        var translation = await TranslationVerses(from.Id, edition, series, cancellationToken);
        var witness = await WitnessVerses(to.Id, cancellationToken);

        var pairs = new List<VersePair>(24_000);
        var tagged = 0;
        var unplaced = 0;

        foreach (var verse in translation)
        {
            if (verse.Words.Count == 0)
            {
                continue;
            }

            tagged += verse.Words.Count;
            var against = witness.Pool(verse.Addresses);
            if (against.Count == 0)
            {
                unplaced += verse.Words.Count;
                continue;
            }

            pairs.Add(new VersePair(verse.Words, against));
        }

        var resolution = series == StrongNumbers.Greek
            ? await Resolution(pairs, cancellationToken)
            : [];

        var drafts = new List<Draft>(300_000);
        var matched = 0;
        var resolved = 0;

        foreach (var pair in pairs)
        {
            var members = pair.Translation
                .GroupBy(word => word.Unit)
                .ToDictionary(unit => unit.First().Id, unit => unit.Select(word => word.Id).ToList());

            var links = StrongNumberMatch.Verse(
                [.. pair.Translation
                    .DistinctBy(word => word.Unit)
                    .Select(word => new StrongNumberMatch.TaggedWord(word.Id, word.Numbers))],
                [.. pair.Witness.Select(word => new StrongNumberMatch.WitnessWord(word.Id, word.Strong))],
                resolution,
                out var tally);

            resolved += tally.Resolved;
            foreach (var link in links)
            {
                var words = link.From.SelectMany(id => members[id]).ToList();
                matched += words.Count;
                drafts.Add(new Draft(words, link.To, link.Confidence, link.Kind));
            }
        }

        if (only is not null)
        {
            drafts = [.. drafts.Where(draft => draft.From.Any(only.Contains))];
        }

        var testimony = await Drawn(from.Id, to.Id, LinkMethod.StatedBySource, cancellationToken);
        testimony.AddRange(await Drawn(from.Id, to.Id, LinkMethod.Manual, cancellationToken));
        var (kept, corroborations) = YieldToTestimony(drafts, testimony, source);

        var guesses = await Drawn(from.Id, to.Id, LinkMethod.Aligner, cancellationToken);
        var settled = NumberedLinkSettlement.Settle(
            [.. kept.Select(d => ((IReadOnlyList<long>)d.From, (IReadOnlyList<long>)d.To))],
            guesses);

        long[] superseded = only is null ? [] : await Superseded(from.Id, to.Id, written, kept, cancellationToken);
        await Write(from.Id, to.Id, source, kept, corroborations, settled, cancellationToken, superseded);

        var outcome = new TaggedTextLinkOutcome(
            false,
            pairs.Count,
            unplaced,
            kept.Count,
            kept.Count(d => d.Kind == StrongMatchKind.Unambiguous),
            kept.Count(d => d.Kind == StrongMatchKind.Paired),
            kept.Count(d => d.Kind == StrongMatchKind.Contended),
            tagged,
            matched,
            tagged - unplaced - matched,
            resolved,
            resolution.Count,
            settled.Count(s => s.Verdict == SettledVerdict.Confirmed),
            settled.Count(s => s.Verdict == SettledVerdict.Confirmed && s.Exact),
            settled.Count(s => s.Verdict == SettledVerdict.Contradicted),
            settled.Count(s => s.Verdict == SettledVerdict.Beside),
            drafts.Count - kept.Count,
            corroborations.Count,
            started.Elapsed);
        logger.LogInformation("{From} to {To}: {Outcome}", fromSlug, toSlug, outcome);
        return outcome;
    }

    /// <summary>
    /// The matches a source has not already spoken about. A match touching a word some source's
    /// link already names is not written — testimony is not overwritten by an inference — and where
    /// it names exactly that link's words, it is kept as a claim on the source's link instead.
    /// </summary>
    private static (List<Draft> Kept, List<(long Link, double Confidence, string Source)> Corroborations)
        YieldToTestimony(List<Draft> drafts, List<DrawnLink> testimony, string source)
    {
        if (testimony.Count == 0)
        {
            return (drafts, []);
        }

        var stated = new Dictionary<long, List<DrawnLink>>(testimony.Count);
        foreach (var link in testimony)
        {
            foreach (var word in link.From)
            {
                if (!stated.TryGetValue(word, out var naming))
                {
                    naming = [];
                    stated[word] = naming;
                }

                naming.Add(link);
            }
        }

        var kept = new List<Draft>(drafts.Count);
        var corroborations = new List<(long, double, string)>();
        foreach (var draft in drafts)
        {
            var naming = draft.From.Where(stated.ContainsKey).SelectMany(word => stated[word]).Distinct().ToList();
            if (naming.Count == 0)
            {
                kept.Add(draft);
                continue;
            }

            if (naming.FirstOrDefault(link => SameWords(link.From, draft.From) && SameWords(link.To, draft.To))
                is { } same)
            {
                corroborations.Add((same.Link, draft.Confidence, source));
            }
        }

        return (kept, corroborations);
    }

    private static bool SameWords(IReadOnlyList<long> one, IReadOnlyList<long> other) =>
        one.Count == other.Count && one.ToHashSet().SetEquals(other);

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

            foreach (var word in pair.Translation.DistinctBy(word => word.Unit))
            {
                foreach (var number in word.Numbers)
                {
                    yield return new NumberOccurrence(number, numbers);
                }
            }
        }
    }

    /// <summary>
    /// The translation's verses, each with every address it stands at and the words carrying a
    /// number of the witness's series. A psalm's superscription is the head of the Synodal's first
    /// verse and a verse of its own in the Hebrew, so a verse is offered every witness verse at any
    /// of its addresses, not the one at its primary address alone.
    /// </summary>
    private async Task<List<TranslationVerse>> TranslationVerses(
        int textId,
        EditionNumbers? edition,
        char series,
        CancellationToken cancellationToken)
    {
        var addresses = await Addresses(textId, cancellationToken);
        var words = await db.Words
            .Where(w => w.TextId == textId)
            .Select(w => new { w.VerseId, w.Position, w.Id, w.StrongNumber })
            .ToListAsync(cancellationToken);

        return words
            .Where(w => addresses.ContainsKey(w.VerseId))
            .GroupBy(w => w.VerseId)
            .Select(verse => new TranslationVerse(
                addresses[verse.Key],
                [.. verse.OrderBy(w => w.Position)
                    .Select(w => Numbered(w.Id, w.StrongNumber, edition, series))
                    .OfType<TaggedWord>()]))
            .OrderBy(verse => verse.Addresses[0])
            .ToList();
    }

    private static TaggedWord? Numbered(long id, string? column, EditionNumbers? edition, char series)
    {
        if (edition is null)
        {
            return column?[0] == series ? new TaggedWord(id, [column], id) : null;
        }

        if (!edition.Tags.TryGetValue(id, out var tag))
        {
            return null;
        }

        var numbers = tag.Numbers.Where(number => number[0] == series).ToList();
        return numbers.Count == 0 ? null : new TaggedWord(id, numbers, tag.Unit);
    }

    private async Task<Witness> WitnessVerses(int textId, CancellationToken cancellationToken)
    {
        var addresses = await Addresses(textId, cancellationToken);
        var words = await db.Words
            .Where(w => w.TextId == textId)
            .Select(w => new { w.VerseId, w.Position, w.Id, w.StrongNumber })
            .ToListAsync(cancellationToken);

        var byVerse = words
            .Where(w => addresses.ContainsKey(w.VerseId))
            .GroupBy(w => w.VerseId)
            .ToDictionary(
                verse => verse.Key,
                verse => verse.OrderBy(w => w.Position).Select(w => new WitnessWord(w.Id, w.StrongNumber)).ToList());

        var byAddress = new Dictionary<(int, int, int), List<int>>(byVerse.Count + 256);
        foreach (var (verse, at) in addresses)
        {
            foreach (var address in at)
            {
                if (!byAddress.TryGetValue(address, out var standing))
                {
                    standing = [];
                    byAddress[address] = standing;
                }

                standing.Add(verse);
            }
        }

        return new Witness(byVerse, byAddress, addresses);
    }

    /// <summary>Every address each verse of a text stands at, its primary address first.</summary>
    private async Task<Dictionary<int, List<(int, int, int)>>> Addresses(
        int textId,
        CancellationToken cancellationToken)
    {
        var rows = await db.VerseReferences
            .Where(r => r.Verse!.TextId == textId)
            .Select(r => new { r.VerseId, r.CanonicalBook, r.CanonicalChapter, r.CanonicalVerse, r.IsPrimary })
            .ToListAsync(cancellationToken);

        return rows
            .GroupBy(r => r.VerseId)
            .Where(verse => verse.Any(r => r.IsPrimary))
            .ToDictionary(
                verse => verse.Key,
                verse => verse
                    .OrderByDescending(r => r.IsPrimary)
                    .ThenBy(r => (r.CanonicalBook, r.CanonicalChapter, r.CanonicalVerse))
                    .Select(r => (r.CanonicalBook, r.CanonicalChapter, r.CanonicalVerse))
                    .ToList());
    }

    /// <summary>The links one method already drew between the two texts, with the words on each side.</summary>
    private async Task<List<DrawnLink>> Drawn(
        int fromTextId,
        int toTextId,
        LinkMethod method,
        CancellationToken cancellationToken)
    {
        var rows = await db.LinkWords
            .Where(lw => lw.Link!.FromTextId == fromTextId && lw.Link.ToTextId == toTextId && lw.Link.Method == method)
            .Select(lw => new { lw.LinkId, lw.WordId, lw.Side })
            .ToListAsync(cancellationToken);

        return
        [
            .. rows.GroupBy(row => row.LinkId).Select(link => new DrawnLink(
                link.Key,
                [.. link.Where(row => row.Side == LinkSide.From).Select(row => row.WordId)],
                [.. link.Where(row => row.Side == LinkSide.To).Select(row => row.WordId)])),
        ];
    }

    /// <summary>
    /// This loader's own links from these numbers that already name a translation word a new match
    /// names: the match is drawn over the same unit of the edition, and two links saying one word
    /// renders something would be the word named twice.
    /// </summary>
    private async Task<long[]> Superseded(
        int fromTextId,
        int toTextId,
        List<string> written,
        List<Draft> drafts,
        CancellationToken cancellationToken)
    {
        var words = drafts.SelectMany(draft => draft.From).Distinct().ToList();
        return
        [
            .. await db.LinkWords
                .Where(lw => lw.Side == LinkSide.From && words.Contains(lw.WordId)
                             && lw.Link!.FromTextId == fromTextId && lw.Link.ToTextId == toTextId
                             && lw.Link.Method == LinkMethod.StrongNumber && written.Contains(lw.Link.Source))
                .Select(lw => lw.LinkId)
                .Distinct()
                .ToListAsync(cancellationToken),
        ];
    }

    private async Task Write(
        int fromTextId,
        int toTextId,
        string source,
        List<Draft> drafts,
        List<(long Link, double Confidence, string Source)> corroborations,
        List<Settled> settled,
        CancellationToken cancellationToken,
        long[]? superseded = null)
    {
        if (drafts.Count == 0 && corroborations.Count == 0)
        {
            return;
        }

        await db.Database.OpenConnectionAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();

        if (superseded is { Length: > 0 })
        {
            await using var give = new NpgsqlCommand(RemoveSettled, connection);
            give.Parameters.AddWithValue("ids", superseded);
            await give.ExecuteNonQueryAsync(cancellationToken);
        }

        var firstId = drafts.Count == 0 ? 0 : await ReserveLinkIds(connection, drafts.Count, cancellationToken);
        if (drafts.Count > 0)
        {
            await WriteLinks(connection, fromTextId, toTextId, source, drafts, firstId, cancellationToken);
            await LinkClaims.Record(connection, transaction, firstId, drafts.Count, cancellationToken);
        }

        await LinkClaims.Corroborate(
            connection, transaction, corroborations, LinkMethod.StrongNumber, CorroborationNote, cancellationToken);

        await Fold(connection, settled, firstId, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private static async Task WriteLinks(
        NpgsqlConnection connection,
        int fromTextId,
        int toTextId,
        string source,
        List<Draft> drafts,
        long firstId,
        CancellationToken cancellationToken)
    {
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
    }

    /// <summary>
    /// The aligner's links the matches settled: a confirmed one becomes a claim on its match, a
    /// contradicted one is removed, and both are removed as links. In the same transaction as the
    /// matches, so the corpus never holds a guess and its refutation as two answers at once.
    /// </summary>
    private static async Task Fold(
        NpgsqlConnection connection,
        List<Settled> settled,
        long firstId,
        CancellationToken cancellationToken)
    {
        var confirmed = settled.Where(s => s.Verdict == SettledVerdict.Confirmed).ToList();
        var removed = settled
            .Where(s => s.Verdict is SettledVerdict.Confirmed or SettledVerdict.Contradicted)
            .Select(s => s.Link)
            .ToArray();
        if (removed.Length == 0)
        {
            return;
        }

        if (confirmed.Count > 0)
        {
            await using (var create = new NpgsqlCommand(FoldTable, connection))
            {
                await create.ExecuteNonQueryAsync(cancellationToken);
            }

            await using (var writer = await connection.BeginBinaryImportAsync(FoldImport, cancellationToken))
            {
                foreach (var fold in confirmed)
                {
                    await writer.StartRowAsync(cancellationToken);
                    await writer.WriteAsync(fold.Link, NpgsqlDbType.Bigint, cancellationToken);
                    await writer.WriteAsync(firstId + fold.Draft, NpgsqlDbType.Bigint, cancellationToken);
                    await writer.WriteAsync(fold.Exact, NpgsqlDbType.Boolean, cancellationToken);
                }

                await writer.CompleteAsync(cancellationToken);
            }

            await using var claims = new NpgsqlCommand(FoldClaims, connection) { CommandTimeout = 600 };
            claims.Parameters.AddWithValue("within", WithinNote);
            await claims.ExecuteNonQueryAsync(cancellationToken);
        }

        await using var remove = new NpgsqlCommand(RemoveSettled, connection) { CommandTimeout = 600 };
        remove.Parameters.AddWithValue("ids", removed);
        await remove.ExecuteNonQueryAsync(cancellationToken);
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

    /// <param name="Unit">
    /// The rendering the word belongs to. A word the corpus tags itself is its own; two words an
    /// edition tags as one phrase share one, and are matched as one occurrence of the number.
    /// </param>
    private sealed record TaggedWord(long Id, IReadOnlyList<string> Numbers, long Unit);

    private sealed record WitnessWord(long Id, string? Strong);

    private sealed record TranslationVerse(List<(int, int, int)> Addresses, List<TaggedWord> Words);

    /// <summary>One verse of the translation, with the witness's words at every address it stands at.</summary>
    private sealed record VersePair(List<TaggedWord> Translation, List<WitnessWord> Witness);

    private sealed record Draft(List<long> From, List<long> To, double Confidence, StrongMatchKind Kind);

    private sealed record Witness(
        Dictionary<int, List<WitnessWord>> ByVerse,
        Dictionary<(int, int, int), List<int>> ByAddress,
        Dictionary<int, List<(int, int, int)>> Addresses)
    {
        /// <summary>
        /// The witness's words at any of these addresses, each verse once, in the order the witness's
        /// own verses stand.
        /// </summary>
        public List<WitnessWord> Pool(List<(int, int, int)> at)
        {
            if (at.Count == 1)
            {
                return ByAddress.TryGetValue(at[0], out var single) && single.Count == 1
                    ? ByVerse.GetValueOrDefault(single[0]) ?? []
                    : Gather(at);
            }

            return Gather(at);
        }

        private List<WitnessWord> Gather(List<(int, int, int)> at) =>
        [
            .. at.Where(ByAddress.ContainsKey)
                .SelectMany(address => ByAddress[address])
                .Distinct()
                .Where(ByVerse.ContainsKey)
                .OrderBy(verse => Addresses[verse][0])
                .SelectMany(verse => ByVerse[verse]),
        ];
    }
}
