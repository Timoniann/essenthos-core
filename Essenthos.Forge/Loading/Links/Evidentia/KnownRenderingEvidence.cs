using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities.Enums;
using Microsoft.EntityFrameworkCore;

namespace Essenthos.Core.Loading.Links.Evidentia;

/// <summary>
/// One entry of the learned index: a form, and which of a word's forms it is. The kind is part of
/// the key rather than beside it, so the exact surface <em>said</em> and the normalisation
/// <em>said</em> are two entries counting two different things - every observation written that
/// way, and every observation of anything that reduces to it.
/// </summary>
internal readonly record struct RenderingKey(EvidentiaFormKind Kind, string Form)
{
    public override string ToString() => $"{Kind.ToString().ToLowerInvariant()} '{Form}'";
}

/// <summary>
/// Every key one word can be filed or looked up under, most specific first.
///
/// Filing and looking up are the same function on purpose. An index keyed by one derivation of a
/// word and consulted by another answers nothing, and says nothing about why: both sides read as
/// correct on their own, and only the two together are wrong.
///
/// The word a pack calls a function word has no keys at all, in either direction. Everything
/// narrower than that - which words may be looked up - belongs where the question is asked, not
/// here: an observation from a witness that states no part of speech is class Unknown, and a
/// filing rule written as "content words only" would leave such a witness unable to teach the
/// index anything.
/// </summary>
internal static class RenderingKeys
{
    public static IReadOnlyList<RenderingKey> Of(EvidentiaAnalysis analysis)
    {
        if (analysis.IsFunctionWord)
        {
            return [];
        }

        var keys = new List<RenderingKey>(3)
        {
            new(EvidentiaFormKind.Surface, analysis.Token.Surface.ToLowerInvariant()),
        };

        // Only where the pack said the word has one. English and Slavic fall their Lemma back to
        // the normalisation when the token carries none, and filing that as a lemma would make a
        // second copy of the normalisation entry and a back-off step that never backs off.
        if (analysis.Capabilities.HasFlag(LanguagePackCapability.Lemma)
            && analysis.Lemma is { Length: > 0 } lemma)
        {
            keys.Add(new(EvidentiaFormKind.Lemma, lemma));
        }

        keys.Add(new(EvidentiaFormKind.Normalised, analysis.Normalised));
        return keys;
    }
}

/// <summary>
/// Reads previously source-stated renderings as a translation-specific lexical prior. The chapter
/// being measured is excluded from the index, so its stored links remain evaluation data rather
/// than becoming evidence for their own prediction.
///
/// An observation is filed under every key <see cref="RenderingKeys"/> derives from the form the
/// corpus wrote, and a lookup asks the same keys of the word it is reading. A language whose pack
/// has no lemmatiser simply has no lemma entries, and an original-language witness, whose surface
/// is its own normalisation, is filed under its stated lemma as well.
/// </summary>
internal sealed class EvidentiaKnownRenderingIndex(
    AppDbContext db,
    LanguagePackRegistry languagePacks)
{
    private const int MinimumObservations = EvidentiaDefaults.MinimumRenderingObservations;

    /// <summary>
    /// What a translation's own edition states. It is the only evidence that is not itself an
    /// inference, so it is what a learned index reads unless a caller deliberately widens it.
    /// </summary>
    private static readonly IReadOnlyList<LinkMethod> DefaultMethods = [LinkMethod.StatedBySource];

    private readonly Dictionary<RenderingCorpusKey, RenderingCorpus> observationsByTextPair = new();
    private readonly Dictionary<string, Dictionary<(int Book, int Chapter), IReadOnlySet<int>>> heldOutVersesByText =
        new(StringComparer.Ordinal);
    private readonly Dictionary<(string Surface, string? Lemma, string Language), IReadOnlyList<RenderingKey>> keysByForm = [];

    /// <param name="fromSlug">The text whose links teach the index.</param>
    /// <param name="sourceSlug">
    /// The text being measured, when it is not <paramref name="fromSlug"/>; named in the refusal.
    /// </param>
    /// <param name="acrossLanguages">
    /// Let a text in another language teach the index. Each observation is still analysed by its own
    /// language's pack, and what the keys of two languages share is then the whole of the transfer.
    /// </param>
    public async Task<EvidentiaKnownRenderingEvidenceSource?> For(
        string fromSlug,
        string toSlug,
        IReadOnlyList<EvidentiaToken> source,
        int excludedBook,
        int excludedChapter,
        IReadOnlyList<LinkMethod>? methods = null,
        string? sourceSlug = null,
        bool acrossLanguages = false,
        CancellationToken cancellationToken = default)
    {
        methods ??= DefaultMethods;
        var language = source.Select(token => token.Language).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (language.Count != 1)
        {
            return null;
        }

        var corpus = await Observations(fromSlug, toSlug, methods, cancellationToken);
        if (!acrossLanguages && !corpus.Language.Equals(language[0], StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"The rendering index for {sourceSlug ?? "the measured text"} ({language[0]}) was asked to learn " +
                $"from {fromSlug} ({corpus.Language}). A rendering index is a statement about one language, so " +
                "every form of the other would be keyed as if it were this one. Learn from a text in " +
                $"{language[0]}, or say the transfer is meant with --learn-across-languages.");
        }

        // Only the content words: they are the only ones a lookup asks about, so an entry for
        // anything else would be built and never read.
        var keysByToken = source
            .Select(token => (Surface: token.Surface.ToLowerInvariant(), Keys: LookupKeys(token)))
            .Where(item => item.Keys.Count > 0)
            .ToList();
        var wanted = keysByToken.SelectMany(item => item.Keys).ToHashSet();
        if (wanted.Count == 0)
        {
            return null;
        }

        var heldOut = await HeldOutVerses(fromSlug, excludedBook, excludedChapter, cancellationToken);
        var distributions = RenderingDistributions.Build(
            corpus.Observations, heldOut, wanted,
            observation => Keys(observation, corpus.Language), MinimumObservations);
        if (distributions.Count == 0)
        {
            return null;
        }

        // By form rather than by token, and answered if any occurrence of it is: the lemma of a
        // form is decided in context, so two occurrences of one spelling can ask different keys.
        var asked = keysByToken
            .GroupBy(item => item.Surface, StringComparer.Ordinal)
            .ToList();
        return new EvidentiaKnownRenderingEvidenceSource(
            distributions,
            $"known-rendering:{fromSlug}→{toSlug}",
            asked.Select(group => group.Key).ToHashSet(StringComparer.Ordinal),
            asked.Where(group => group.SelectMany(item => item.Keys).Any(distributions.ContainsKey))
                .Select(group => group.Key)
                .ToHashSet(StringComparer.Ordinal));
    }

    private IReadOnlyList<RenderingKey> LookupKeys(EvidentiaToken token) =>
        languagePacks.TryAnalyse(token, out var analysis) && analysis.IsContentWord
            ? RenderingKeys.Of(analysis)
            : [];

    private IReadOnlyList<RenderingKey> Keys(EvidentiaToken token) =>
        languagePacks.TryAnalyse(token, out var analysis) ? RenderingKeys.Of(analysis) : [];

    // One observation list is read for every chapter of a book run, and analysing a form is the
    // same answer every time.
    private IReadOnlyList<RenderingKey> Keys(RenderingObservation observation, string language)
    {
        var form = (observation.SourceSurface, observation.SourceLemma, language);
        if (keysByForm.TryGetValue(form, out var cached))
        {
            return cached;
        }

        var keys = Keys(new EvidentiaToken(
            0, default, 0, observation.SourceSurface, language, Lemma: observation.SourceLemma));
        keysByForm.Add(form, keys);
        return keys;
    }

    private async Task<RenderingCorpus> Observations(
        string fromSlug, string toSlug, IReadOnlyList<LinkMethod> methods, CancellationToken cancellationToken)
    {
        var key = new RenderingCorpusKey(fromSlug, toSlug, string.Join(',', methods.Order()));
        if (observationsByTextPair.TryGetValue(key, out var cached))
        {
            return cached;
        }

        var texts = await db.Texts.AsNoTracking()
            .Where(text => text.Slug == fromSlug || text.Slug == toSlug)
            .Select(text => new { text.Id, text.Slug, text.Language })
            .ToListAsync(cancellationToken);
        var sourceText = texts.SingleOrDefault(text => text.Slug == fromSlug)
            ?? throw new InvalidOperationException($"Known rendering evidence needs source text {fromSlug}.");
        var targetText = texts.SingleOrDefault(text => text.Slug == toSlug)
            ?? throw new InvalidOperationException($"Known rendering evidence needs target text {toSlug}.");
        // The verse, not its reference row: a verse placed at two canonical addresses joined twice
        // and was counted twice in the distribution.
        var observations = (await (
            from link in db.Links.AsNoTracking()
            join sourceMembership in db.LinkWords.AsNoTracking() on link.Id equals sourceMembership.LinkId
            join sourceWord in db.Words.AsNoTracking() on sourceMembership.WordId equals sourceWord.Id
            join targetMembership in db.LinkWords.AsNoTracking() on link.Id equals targetMembership.LinkId
            join targetWord in db.Words.AsNoTracking() on targetMembership.WordId equals targetWord.Id
            where methods.Contains(link.Method)
                && sourceWord.TextId == sourceText.Id
                && targetWord.TextId == targetText.Id
                && targetWord.StrongNumber != null
                && ((link.FromTextId == sourceText.Id && link.ToTextId == targetText.Id
                        && sourceMembership.Side == LinkSide.From && targetMembership.Side == LinkSide.To)
                    || (link.FromTextId == targetText.Id && link.ToTextId == sourceText.Id
                        && sourceMembership.Side == LinkSide.To && targetMembership.Side == LinkSide.From))
            select new RenderingObservation(
                sourceWord.VerseId, sourceWord.Surface, sourceWord.Lemma, targetWord.StrongNumber!, link.Id))
            .ToListAsync(cancellationToken))
            .GroupBy(observation => observation.LinkId)
            .SelectMany(StemsOnly)
            .OrderBy(observation => observation.VerseId)
            .ThenBy(observation => observation.SourceSurface, StringComparer.Ordinal)
            .ThenBy(observation => observation.TargetStrongNumber, StringComparer.Ordinal)
            .ToList();
        var corpus = new RenderingCorpus(sourceText.Language, observations);
        observationsByTextPair.Add(key, corpus);
        return corpus;
    }

    /// <summary>
    /// Every verse of the source text carrying a reference inside the held-out chapter. The
    /// exclusion has to be by verse: a verse spanning a chapter boundary has a reference in the
    /// held-out chapter and another outside it, and filtering reference rows let the second one
    /// through - so the verse's own stored link became evidence for predicting itself.
    /// </summary>
    private async Task<IReadOnlySet<int>> HeldOutVerses(
        string slug, int book, int chapter, CancellationToken cancellationToken)
    {
        if (!heldOutVersesByText.TryGetValue(slug, out var byChapter))
        {
            byChapter = (await db.VerseReferences.AsNoTracking()
                .Where(reference => reference.Verse!.Text!.Slug == slug)
                .Select(reference => new
                {
                    reference.VerseId,
                    reference.CanonicalBook,
                    reference.CanonicalChapter,
                })
                .ToListAsync(cancellationToken))
                .GroupBy(reference => (reference.CanonicalBook, reference.CanonicalChapter))
                .ToDictionary(
                    group => group.Key,
                    group => (IReadOnlySet<int>)group.Select(reference => reference.VerseId).ToHashSet());
            heldOutVersesByText.Add(slug, byChapter);
        }

        return byChapter.GetValueOrDefault((book, chapter), new HashSet<int>());
    }

    /// <summary>
    /// A link that names a whole written Hebrew word names its prefixes too - the Berean's
    /// <em>divided</em> is on both וַ and יַּבְדֵּל - and the prefix is the stem's grammar, not what the
    /// English word renders. Learnt as a rendering it made <em>divided</em> as much the ו's word as the
    /// verb's, so a prefix is only learnt from a link that names nothing else.
    /// </summary>
    internal static IEnumerable<RenderingObservation> StemsOnly(IEnumerable<RenderingObservation> link)
    {
        var observations = link.ToList();
        return observations.All(observation => IsPrefix(observation.TargetStrongNumber))
            ? observations
            : observations.Where(observation => !IsPrefix(observation.TargetStrongNumber));
    }

    /// <summary>BHSA's numbers for the prefixes it writes as words: ו, ב, כ, ל, the article and its ה.</summary>
    private static bool IsPrefix(string strongNumber) =>
        strongNumber.Length == 5 && strongNumber.StartsWith("H900", StringComparison.Ordinal);

    private sealed record RenderingCorpusKey(string FromSlug, string ToSlug, string Methods);

    private sealed record RenderingCorpus(string Language, IReadOnlyList<RenderingObservation> Observations);
}

internal sealed record RenderingObservation(
    int VerseId,
    string SourceSurface,
    string? SourceLemma,
    string TargetStrongNumber,
    long LinkId = 0);

/// <summary>
/// Turns rendering observations into a distribution per key. It is a pure function of what was
/// read, so the held-out exclusion - the guarantee every published number rests on - can be
/// asserted rather than read off a where clause.
/// </summary>
internal static class RenderingDistributions
{
    public static IReadOnlyDictionary<RenderingKey, RenderingDistribution> Build(
        IEnumerable<RenderingObservation> observations,
        IReadOnlySet<int> heldOutVerses,
        IReadOnlySet<RenderingKey> wantedKeys,
        Func<RenderingObservation, IReadOnlyList<RenderingKey>> keys,
        int minimumObservations)
    {
        var counts = new Dictionary<RenderingKey, Dictionary<string, int>>();
        foreach (var observation in observations)
        {
            if (heldOutVerses.Contains(observation.VerseId))
            {
                continue;
            }

            foreach (var key in keys(observation))
            {
                // Narrowing to the passage's own keys keeps the dictionary small; it is not a
                // condition on what may match, because every key the passage can ask about is in
                // the set. An observation counts towards each of its keys separately, so a form
                // spelt differently from anything in the passage still feeds the normalisation.
                if (!wantedKeys.Contains(key))
                {
                    continue;
                }

                if (!counts.TryGetValue(key, out var byNumber))
                {
                    byNumber = new Dictionary<string, int>(StringComparer.Ordinal);
                    counts.Add(key, byNumber);
                }

                byNumber[observation.TargetStrongNumber] = byNumber.GetValueOrDefault(observation.TargetStrongNumber) + 1;
            }
        }

        return counts
            .Select(pair => new { pair.Key, Total = pair.Value.Values.Sum(), Counts = pair.Value })
            .Where(item => item.Total >= minimumObservations)
            .ToDictionary(
                item => item.Key,
                item => new RenderingDistribution(
                    item.Total,
                    item.Counts.ToDictionary(
                        pair => pair.Key,
                        pair => new RenderingFrequency(pair.Value, (double)pair.Value / item.Total),
                        StringComparer.Ordinal)));
    }
}

internal sealed record RenderingFrequency(int Count, double Share);

/// <summary>
/// Everything the corpus has seen one source form rendered as, kept together so a policy can ask
/// what the strongest competing sense is across the whole index rather than across one verse.
/// </summary>
internal sealed class RenderingDistribution
{
    private readonly double bestShare;
    private readonly double secondShare;

    public RenderingDistribution(int observations, IReadOnlyDictionary<string, RenderingFrequency> senses)
    {
        Observations = observations;
        Senses = senses;
        var shares = senses.Values.Select(frequency => frequency.Share).OrderByDescending(share => share).ToList();
        bestShare = shares[0];
        secondShare = shares.Count > 1 ? shares[1] : 0;
    }

    public int Observations { get; }

    public IReadOnlyDictionary<string, RenderingFrequency> Senses { get; }

    /// <summary>The strongest share held by a sense other than this one.</summary>
    public double NextShare(double share) => share >= bestShare ? secondShare : bestShare;
}

/// <summary>
/// A rendering seen outside the held-out chapter. It is derived evidence, not a statement made by
/// the new translation, so it remains a candidate until the matcher and review policy decide it.
/// </summary>
/// <param name="askedForms">
/// The distinct lower-cased surfaces of the passage's content words - what this index was asked
/// about - and <paramref name="answeredForms"/> the ones it holds an entry for under any key.
/// The two are reported because a recall figure is not readable without them: a tier that cannot
/// see a word and a tier that saw it and declined are different failures.
/// </param>
internal sealed class EvidentiaKnownRenderingEvidenceSource(
    IReadOnlyDictionary<RenderingKey, RenderingDistribution> bySourceKey,
    string sourceName,
    IReadOnlySet<string> askedForms,
    IReadOnlySet<string> answeredForms) : IEvidentiaEvidenceSource
{
    private const double MinimumScore = EvidentiaDefaults.KnownRenderingBaseScore;
    private const double MaximumAdditionalScore = EvidentiaDefaults.KnownRenderingShareScore;

    public IReadOnlySet<string> AskedForms => askedForms;

    public IReadOnlySet<string> AnsweredForms => answeredForms;

    public IEnumerable<EvidentiaEvidence> Find(EvidentiaAnalysis source, EvidentiaAnalysis target)
    {
        var (key, distribution) = source.IsContentWord ? Entry(source) : default;
        if (distribution is null
            || target.Token.StrongNumber is not { } strongNumber
            || !distribution.Senses.TryGetValue(strongNumber, out var frequency))
        {
            yield break;
        }

        var nextShare = distribution.NextShare(frequency.Share);
        yield return new EvidentiaEvidence(
            EvidentiaEvidenceKind.KnownRendering,
            MinimumScore + MaximumAdditionalScore * frequency.Share,
            $"{sourceName}; key={key}; observations={frequency.Count}/{distribution.Observations}; " +
            $"share={frequency.Share:P0}; next sense={nextShare:P0}",
            new EvidentiaEvidenceSupport(distribution.Observations, frequency.Share, nextShare, key.Kind));
    }

    /// <summary>
    /// The most specific key the index can speak about - not the most specific one that agrees
    /// with the target. Choosing the entry after seeing the answer would let a two-observation
    /// surface be picked over a two-hundred-observation normalisation because it happened to say
    /// yes, and the share and the next share are exactly what that would falsify.
    ///
    /// An entry exists only once it has met <see cref="EvidentiaDefaults.MinimumRenderingObservations"/>,
    /// so "the index can speak about it" and "there is enough of it to speak about" are the same test.
    /// </summary>
    private (RenderingKey Key, RenderingDistribution? Distribution) Entry(EvidentiaAnalysis source)
    {
        foreach (var key in RenderingKeys.Of(source))
        {
            if (bySourceKey.TryGetValue(key, out var distribution))
            {
                return (key, distribution);
            }
        }

        return (default, null);
    }
}
