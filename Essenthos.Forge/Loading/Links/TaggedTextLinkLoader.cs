using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Strong;
using Microsoft.EntityFrameworkCore;

namespace Essenthos.Core.Loading.Links;

/// <summary>
/// What the numbers would draw for a pair, scored against the links a source already states for it,
/// before any of it is written.
/// </summary>
/// <param name="Drafts">Matches the numbers draw.</param>
/// <param name="Words">Translation words they name.</param>
/// <param name="StatedWords">Of those, the words a source's link names, which are the ones it can be scored on.</param>
/// <param name="AgreedWords">Of those, the words whose match reaches a witness word the source links them to.</param>
/// <param name="Pairs">Word pairs the matches name on the stated words.</param>
/// <param name="AgreedPairs">Of those, the pairs a source's link names too.</param>
internal sealed record TaggedTextScore(string From, string To, int Drafts, int Words, int StatedWords, int AgreedWords, int Pairs, int AgreedPairs)
{
    public double WordPrecision => StatedWords == 0 ? 0 : (double)AgreedWords / StatedWords;

    public double PairPrecision => Pairs == 0 ? 0 : (double)AgreedPairs / Pairs;

    public override string ToString() =>
        $"{From} to {To}: {Drafts} matches over {Words} words; on the {StatedWords} words a source links, " +
        $"{AgreedWords} reach a word it links them to ({WordPrecision:P2}); word pairs {AgreedPairs}/{Pairs} ({PairPrecision:P2})";
}

/// <param name="Tags">The numbers the edition puts on each of the translation's words, by word id.</param>
/// <param name="Credit">
/// What the links drawn from it say they rest on, which is also the prefix the dataset declaration
/// claims — the words are the only place a link records where its numbers came from.
/// </param>
internal sealed record EditionNumbers(IReadOnlyDictionary<long, WordTag> Tags, string Credit);

/// <summary>
/// A translation's Strong numbers matched to a witness's in the configured database, verse by verse, by
/// <see cref="StrongNumberMatch"/>, and scored against the links a source states for the pair; nothing is written.
/// It is the measure behind the verb <c>reina-valera-strong-score</c>.
///
/// <para>
/// The links themselves are the build's: <see cref="Mapping.StrongNumberSource"/> draws the same matches from the
/// store's files into <c>mapping/&lt;pair&gt;/strong</c>, and the merge settles them against the other methods. The
/// sources and the corroboration note here are the ones those rows carry.
/// </para>
/// </summary>
internal sealed class TaggedTextLinkLoader(AppDbContext db, ILogger<TaggedTextLinkLoader> logger)
{
    private const string Greek = "grc";

    internal static string Source(string fromSlug, string toSlug) =>
        $"the Strong numbers {fromSlug} carries, matched within the verse against {toSlug}";

    internal static string Source(EditionNumbers edition, string toSlug) =>
        $"{edition.Credit}, matched within the verse against {toSlug}";

    /// <summary>
    /// The note on a claim a source's link is given where the numbers reach exactly its words. The
    /// link keeps the source's own method; this says a printed number agrees with it.
    /// </summary>
    internal const string CorroborationNote = "the Strong numbers name the same words";

    /// <summary>
    /// What the numbers would draw for the pair, scored against the links a source states for it
    /// (<see cref="LinkMethod.StatedBySource"/>), writing nothing: how far a number-matched link
    /// agrees with a hand-made alignment of the same text, before it is believed.
    /// </summary>
    public async Task<TaggedTextScore> Score(
        string fromSlug,
        string toSlug,
        EditionNumbers? edition,
        CancellationToken cancellationToken = default)
    {
        var (from, to) = await Pair(fromSlug, toSlug, cancellationToken);
        var (drafts, _, _, _, _, _, _) = await Match(from, to, edition, cancellationToken);
        var stated = new Dictionary<long, HashSet<long>>();
        foreach (var (named, targets) in await Stated(from.Id, to.Id, cancellationToken))
        {
            foreach (var word in named)
            {
                if (!stated.TryGetValue(word, out var reached))
                {
                    stated[word] = reached = [];
                }

                reached.UnionWith(targets);
            }
        }


        int words = 0, statedWords = 0, agreedWords = 0, pairs = 0, agreedPairs = 0;
        foreach (var draft in drafts)
        {
            foreach (var word in draft.From.Distinct())
            {
                words++;
                if (!stated.TryGetValue(word, out var targets))
                {
                    continue;
                }

                statedWords++;
                agreedWords += draft.To.Any(targets.Contains) ? 1 : 0;
                pairs += draft.To.Count;
                agreedPairs += draft.To.Count(targets.Contains);
            }
        }

        var score = new TaggedTextScore(fromSlug, toSlug, drafts.Count, words, statedWords, agreedWords, pairs, agreedPairs);
        logger.LogInformation("{Score}", score);
        return score;
    }

    private async Task<(Database.Entities.Text From, Database.Entities.Text To)> Pair(
        string fromSlug,
        string toSlug,
        CancellationToken cancellationToken)
    {
        var from = await db.Texts.SingleOrDefaultAsync(t => t.Slug == fromSlug, cancellationToken);
        var to = await db.Texts.SingleOrDefaultAsync(t => t.Slug == toSlug, cancellationToken);
        if (from is null || to is null)
        {
            throw new InvalidOperationException(
                $"\"{fromSlug}\" and \"{toSlug}\" must both be loaded before the correspondences between them " +
                "can be. Load the texts first; this reads them, it does not create them.");
        }

        return (from, to);
    }

    /// <summary>
    /// Every match the numbers draw between the two texts, verse by verse, before anything already in
    /// the corpus is consulted.
    /// </summary>
    private async Task<(List<Draft> Drafts, int Verses, int Unplaced, int Tagged, int Matched, int Resolved, int Redirects)>
        Match(
            Database.Entities.Text from,
            Database.Entities.Text to,
            EditionNumbers? edition,
            CancellationToken cancellationToken)
    {
        var series = to.Language == Greek ? StrongNumbers.Greek : StrongNumbers.Hebrew;
        var translation = await TranslationVerses(from.Id, edition, series, cancellationToken);
        var witness = await WitnessVerses(to.Id, cancellationToken);
        var (pairs, tagged, unplaced) = Paired(translation, witness);

        var resolution = series == StrongNumbers.Greek
            ? await Resolution(pairs, cancellationToken)
            : [];

        var (drafts, matched, resolved) = Drafted(pairs, resolution);
        return (drafts, pairs.Count, unplaced, tagged, matched, resolved, resolution.Count);
    }

    /// <summary>Each translation verse beside the witness's words at every address it stands at.</summary>
    private static (List<VersePair> Pairs, int Tagged, int Unplaced) Paired(List<TranslationVerse> translation, Witness witness)
    {
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

        return (pairs, tagged, unplaced);
    }

    /// <summary>What the numbers draw in each verse pair.</summary>
    private static (List<Draft> Drafts, int Matched, int Resolved) Drafted(
        List<VersePair> pairs,
        Dictionary<string, NumberRedirect> resolution)
    {
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
                [.. pair.Witness.Select(word => new StrongNumberMatch.WitnessWord(word.Id, word.Strong, word.Suffixed))],
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

        return (drafts, matched, resolved);
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
        var words = await db.Words.Where(w => w.TextId == textId)
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
        var words = await db.Words.Where(w => w.TextId == textId)
            .Select(w => new
            {
                w.VerseId,
                w.Position,
                w.Id,
                w.StrongNumber,
                Suffixed = w.StrongNumber == ObjectMarker.Number && w.Morphology != null
                           && EF.Functions.JsonExists(w.Morphology, ObjectMarker.SuffixFeature),
            })
            .ToListAsync(cancellationToken);

        var byVerse = words
            .Where(w => addresses.ContainsKey(w.VerseId))
            .GroupBy(w => w.VerseId)
            .ToDictionary(
                verse => verse.Key,
                verse => verse.OrderBy(w => w.Position)
                    .Select(w => new WitnessWord(w.Id, w.StrongNumber, w.Suffixed))
                    .ToList());

        var byAddress = new Dictionary<(int, int, int), List<int>>(byVerse.Count + 256);
        foreach (var (verse, stands) in addresses)
        {
            foreach (var address in stands)
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

    /// <summary>The links a source states between the two texts, with the words on each side.</summary>
    private async Task<List<(List<long> From, List<long> To)>> Stated(int fromTextId, int toTextId, CancellationToken cancellationToken)
    {
        var rows = await db.LinkWords
            .Where(lw => lw.Link!.FromTextId == fromTextId && lw.Link.ToTextId == toTextId && lw.Link.Method == LinkMethod.StatedBySource)
            .Select(lw => new { lw.LinkId, lw.WordId, lw.Side })
            .ToListAsync(cancellationToken);

        return
        [
            .. rows.GroupBy(row => row.LinkId).Select(link => (
                (List<long>)[.. link.Where(row => row.Side == LinkSide.From).Select(row => row.WordId)],
                (List<long>)[.. link.Where(row => row.Side == LinkSide.To).Select(row => row.WordId)])),
        ];
    }

    /// <param name="Unit">
    /// The rendering the word belongs to. A word the corpus tags itself is its own; two words an
    /// edition tags as one phrase share one, and are matched as one occurrence of the number.
    /// </param>
    private sealed record TaggedWord(long Id, IReadOnlyList<string> Numbers, long Unit);

    private sealed record WitnessWord(long Id, string? Strong, bool Suffixed);

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
