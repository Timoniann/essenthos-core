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
    private const int MinimumObservations = 2;

    /// <summary>
    /// What a translation's own edition states. It is the only evidence that is not itself an
    /// inference, so it is what a learned index reads unless a caller deliberately widens it.
    /// </summary>
    private static readonly IReadOnlyList<LinkMethod> DefaultMethods = [LinkMethod.StatedBySource];

    private readonly Dictionary<RenderingCorpusKey, IReadOnlyList<RenderingObservation>> observationsByTextPair = new();

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
        var counts = new Dictionary<string, Dictionary<string, int>>(StringComparer.Ordinal);
        foreach (var observation in observations)
        {
            var key = Key(observation.SourceSurface, language[0]);
            if (key is null || !wanted.Contains(key)
                || !wantedSurfaceForms.Contains(observation.SourceSurface.ToLowerInvariant())
                || (observation.CanonicalBook == excludedBook && observation.CanonicalChapter == excludedChapter))
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

        var distributions = counts
            .Select(pair => new
            {
                pair.Key,
                Total = pair.Value.Values.Sum(),
                Counts = pair.Value,
            })
            .Where(item => item.Total >= MinimumObservations)
            .ToDictionary(
                item => item.Key,
                item => new RenderingDistribution(
                    item.Total,
                    item.Counts.ToDictionary(
                        pair => pair.Key,
                        pair => new RenderingFrequency(pair.Value, (double)pair.Value / item.Total),
                        StringComparer.Ordinal)),
                StringComparer.Ordinal);

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

    private string? Key(string surface, string language) =>
        Key(new EvidentiaToken(0, default, 0, surface, language));

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
        var observations = (await (
            from link in db.Links.AsNoTracking()
            join sourceMembership in db.LinkWords.AsNoTracking() on link.Id equals sourceMembership.LinkId
            join sourceWord in db.Words.AsNoTracking() on sourceMembership.WordId equals sourceWord.Id
            join reference in db.VerseReferences.AsNoTracking() on sourceWord.VerseId equals reference.VerseId
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
            select new RenderingObservation(reference.CanonicalBook, reference.CanonicalChapter, sourceWord.Surface, targetWord.StrongNumber!))
            .ToListAsync(cancellationToken))
            .OrderBy(observation => observation.CanonicalBook)
            .ThenBy(observation => observation.CanonicalChapter)
            .ThenBy(observation => observation.SourceSurface, StringComparer.Ordinal)
            .ThenBy(observation => observation.TargetStrongNumber, StringComparer.Ordinal)
            .ToList();
        observationsByTextPair.Add(key, observations);
        return observations;
    }

    private sealed record RenderingCorpusKey(string FromSlug, string ToSlug, string Methods);

    private sealed record RenderingObservation(int CanonicalBook, int CanonicalChapter, string SourceSurface, string TargetStrongNumber);

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
