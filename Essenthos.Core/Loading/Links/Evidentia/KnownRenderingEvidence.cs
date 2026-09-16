using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities.Enums;
using Microsoft.EntityFrameworkCore;

namespace Essenthos.Core.Loading.Links.Evidentia;

/// <summary>
/// Reads previously source-stated renderings as a translation-specific lexical prior. The chapter
/// being measured is excluded from the index, so its stored links remain evaluation data rather
/// than becoming evidence for their own prediction.
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

    private readonly Dictionary<RenderingCorpusKey, IReadOnlyList<RenderingObservation>> observationsByTextPair = new();
    private readonly Dictionary<string, Dictionary<(int Book, int Chapter), IReadOnlySet<int>>> heldOutVersesByText =
        new(StringComparer.Ordinal);
    private readonly Dictionary<(string Surface, string Language), string?> keyBySurface = [];

    public async Task<EvidentiaKnownRenderingEvidenceSource?> For(
        string fromSlug,
        string toSlug,
        IReadOnlyList<EvidentiaToken> source,
        int excludedBook,
        int excludedChapter,
        IReadOnlyList<LinkMethod>? methods = null,
        CancellationToken cancellationToken = default)
    {
        methods ??= DefaultMethods;
        var language = source.Select(token => token.Language).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (language.Count != 1)
        {
            return null;
        }

        var wanted = source
            .Select(Key)
            .Where(key => key is not null)
            .Cast<string>()
            .ToHashSet(StringComparer.Ordinal);
        if (wanted.Count == 0)
        {
            return null;
        }
        var wantedSurfaceForms = source
            .Where(token => Key(token) is not null)
            .Select(token => token.Surface.ToLowerInvariant())
            .ToHashSet(StringComparer.Ordinal);
        var observations = await Observations(fromSlug, toSlug, methods, cancellationToken);
        var heldOut = await HeldOutVerses(fromSlug, excludedBook, excludedChapter, cancellationToken);
        var distributions = RenderingDistributions.Build(
            observations, heldOut, wanted, wantedSurfaceForms,
            surface => Key(surface, language[0]), MinimumObservations);

        return distributions.Count == 0
            ? null
            : new EvidentiaKnownRenderingEvidenceSource(distributions, $"known-rendering:{fromSlug}→{toSlug}");
    }

    private string? Key(EvidentiaToken token)
    {
        if (!languagePacks.TryAnalyse(token, out var analysis) || !analysis.IsContentWord)
        {
            return null;
        }

        // Historical source-stated renderings were loaded before UDPipe annotations existed.
        // Keep this index keyed by the language pack's stable normalisation, so a newly analysed
        // source token can still consult the same evidence rather than silently changing keys.
        return analysis.Normalised;
    }

    // One observation list is read for every chapter of a book run, and stemming a surface is the
    // same answer every time.
    private string? Key(string surface, string language)
    {
        if (keyBySurface.TryGetValue((surface, language), out var cached))
        {
            return cached;
        }

        var key = Key(new EvidentiaToken(0, default, 0, surface, language));
        keyBySurface.Add((surface, language), key);
        return key;
    }

    private async Task<IReadOnlyList<RenderingObservation>> Observations(
        string fromSlug, string toSlug, IReadOnlyList<LinkMethod> methods, CancellationToken cancellationToken)
    {
        var key = new RenderingCorpusKey(fromSlug, toSlug, string.Join(',', methods.Order()));
        if (observationsByTextPair.TryGetValue(key, out var cached))
        {
            return cached;
        }

        var texts = await db.Texts.AsNoTracking()
            .Where(text => text.Slug == fromSlug || text.Slug == toSlug)
            .Select(text => new { text.Id, text.Slug })
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
            select new RenderingObservation(sourceWord.VerseId, sourceWord.Surface, targetWord.StrongNumber!))
            .ToListAsync(cancellationToken))
            .OrderBy(observation => observation.VerseId)
            .ThenBy(observation => observation.SourceSurface, StringComparer.Ordinal)
            .ThenBy(observation => observation.TargetStrongNumber, StringComparer.Ordinal)
            .ToList();
        observationsByTextPair.Add(key, observations);
        return observations;
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

    private sealed record RenderingCorpusKey(string FromSlug, string ToSlug, string Methods);
}

internal sealed record RenderingObservation(int VerseId, string SourceSurface, string TargetStrongNumber);

/// <summary>
/// Turns rendering observations into a distribution per source form. It is a pure function of what
/// was read, so the held-out exclusion - the guarantee every published number rests on - can be
/// asserted rather than read off a where clause.
/// </summary>
internal static class RenderingDistributions
{
    public static IReadOnlyDictionary<string, RenderingDistribution> Build(
        IEnumerable<RenderingObservation> observations,
        IReadOnlySet<int> heldOutVerses,
        IReadOnlySet<string> wantedKeys,
        IReadOnlySet<string> wantedSurfaceForms,
        Func<string, string?> key,
        int minimumObservations)
    {
        var counts = new Dictionary<string, Dictionary<string, int>>(StringComparer.Ordinal);
        foreach (var observation in observations)
        {
            var form = key(observation.SourceSurface);
            if (form is null
                || heldOutVerses.Contains(observation.VerseId)
                || !wantedKeys.Contains(form)
                || !wantedSurfaceForms.Contains(observation.SourceSurface.ToLowerInvariant()))
            {
                continue;
            }

            if (!counts.TryGetValue(form, out var byNumber))
            {
                byNumber = new Dictionary<string, int>(StringComparer.Ordinal);
                counts.Add(form, byNumber);
            }

            byNumber[observation.TargetStrongNumber] = byNumber.GetValueOrDefault(observation.TargetStrongNumber) + 1;
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
                        StringComparer.Ordinal)),
                StringComparer.Ordinal);
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
internal sealed class EvidentiaKnownRenderingEvidenceSource(
    IReadOnlyDictionary<string, RenderingDistribution> bySourceKey,
    string sourceName) : IEvidentiaEvidenceSource
{
    private const double MinimumScore = EvidentiaDefaults.KnownRenderingBaseScore;
    private const double MaximumAdditionalScore = EvidentiaDefaults.KnownRenderingShareScore;

    public IEnumerable<EvidentiaEvidence> Find(EvidentiaAnalysis source, EvidentiaAnalysis target)
    {
        var key = source.Normalised;
        if (!source.IsContentWord
            || target.Token.StrongNumber is not { } strongNumber
            || !bySourceKey.TryGetValue(key, out var distribution)
            || !distribution.Senses.TryGetValue(strongNumber, out var frequency))
        {
            yield break;
        }

        var nextShare = distribution.NextShare(frequency.Share);
        yield return new EvidentiaEvidence(
            EvidentiaEvidenceKind.KnownRendering,
            MinimumScore + MaximumAdditionalScore * frequency.Share,
            $"{sourceName}; observations={frequency.Count}/{distribution.Observations}; " +
            $"share={frequency.Share:P0}; next sense={nextShare:P0}",
            new EvidentiaEvidenceSupport(distribution.Observations, frequency.Share, nextShare));
    }
}
