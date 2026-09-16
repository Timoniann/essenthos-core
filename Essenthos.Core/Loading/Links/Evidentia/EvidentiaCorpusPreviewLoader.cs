using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Loading.Links;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using System.Text.Json;

namespace Essenthos.Core.Loading.Links.Evidentia;

/// <summary>
/// Reads one canonical chapter into EVIDENTIA without changing the corpus. It is deliberately a
/// command-side diagnostic: a score here is evidence about readiness, never a persisted word link.
/// </summary>
internal sealed class EvidentiaCorpusPreviewLoader(
    AppDbContext db,
    EvidentiaPipeline pipeline,
    EvidentiaStrongProposalResolver strongProposalResolver,
    EvidentiaKnownRenderingProposalResolver knownRenderingProposalResolver,
    EvidentiaTargetGlossProposalResolver targetGlossProposalResolver,
    EvidentiaDictionaryProposalResolver dictionaryProposalResolver,
    EvidentiaSyntaxReviewGate syntaxReviewGate,
    UdpipeAnnotator udpipe,
    EvidentiaDictionarySenseIndex dictionarySenseIndex,
    EvidentiaKnownRenderingIndex knownRenderingIndex)
{
    private readonly Dictionary<string, SyntaxPrior> syntaxByTargetText = new(StringComparer.Ordinal);

    public async Task<EvidentiaCorpusPreview> Preview(
        string fromSlug,
        string toSlug,
        int canonicalBook,
        int canonicalChapter,
        int? canonicalVerse,
        bool allowSourceStrongEvidence = true,
        bool allowKnownRenderingEvidence = true,
        CancellationToken cancellationToken = default)
    {
        var sourceAnalysis = await SourceTokens(fromSlug, canonicalBook, canonicalChapter, canonicalVerse, cancellationToken);
        var source = sourceAnalysis.Tokens;
        var target = await Tokens(toSlug, canonicalBook, canonicalChapter, null, cancellationToken);
        var dictionaryEvidence = await dictionarySenseIndex.For(source, cancellationToken);
        var targetGlossEvidence = TargetGlossEvidenceSource.For(target);
        var knownRenderingEvidence = allowKnownRenderingEvidence
            ? await knownRenderingIndex.For(fromSlug, toSlug, source, canonicalBook, canonicalChapter, null, cancellationToken)
            : null;
        var preview = pipeline.Preview(
            new EvidentiaRequest(source, target, AllowSourceStrongEvidence: allowSourceStrongEvidence),
            Evidence(dictionaryEvidence, knownRenderingEvidence, targetGlossEvidence));

        return new EvidentiaCorpusPreview(
            fromSlug,
            toSlug,
            canonicalBook,
            canonicalChapter,
            canonicalVerse,
            source.Count,
            target.Count,
            preview);
    }

    public async Task<EvidentiaChapterMeasurement> MeasureChapter(
        string fromSlug,
        string toSlug,
        int canonicalBook,
        int canonicalChapter,
        EvidentiaMeasurementOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new EvidentiaMeasurementOptions();
        var sourceAnalysis = await SourceTokens(fromSlug, canonicalBook, canonicalChapter, null, cancellationToken);
        var source = sourceAnalysis.Tokens;
        var target = await Tokens(toSlug, canonicalBook, canonicalChapter, null, cancellationToken);
        var dictionaryEvidence = await dictionarySenseIndex.For(source, cancellationToken);
        var targetGlossEvidence = TargetGlossEvidenceSource.For(target);
        var knownRenderingEvidence = options.AllowKnownRenderingEvidence
            ? await knownRenderingIndex.For(
                options.LearnRenderingsFrom ?? fromSlug, toSlug, source,
                canonicalBook, canonicalChapter, options.LearnedRenderingMethods, cancellationToken)
            : null;
        var previews = source.GroupBy(token => token.Address)
            .OrderBy(group => group.Key.Verse)
            .Select(group => pipeline.Preview(
                new EvidentiaRequest(group.ToList(), target, AllowSourceStrongEvidence: options.AllowSourceStrongEvidence),
                Evidence(dictionaryEvidence, knownRenderingEvidence, targetGlossEvidence)))
            .ToList();

        var candidates = previews.SelectMany(preview => preview.Candidates).ToList();
        var resolution = strongProposalResolver.Resolve(candidates);
        var knownRenderingResolution = knownRenderingProposalResolver.Resolve(candidates);
        var reviewKnownRenderingResolution = knownRenderingProposalResolver.Resolve(
            candidates, EvidentiaKnownRenderingProposalResolver.Review);
        var globalKnownRenderingResolution = knownRenderingProposalResolver.ResolveGlobally(
            candidates, EvidentiaKnownRenderingProposalResolver.Safe);
        var globalReviewKnownRenderingResolution = knownRenderingProposalResolver.ResolveGlobally(candidates);
        var dictionaryReviewResolution = dictionaryProposalResolver.ResolveAdditional(candidates, globalReviewKnownRenderingResolution.Proposals);
        var targetGlossOnlyResolution = targetGlossProposalResolver.ResolveAdditional(
            candidates, globalReviewKnownRenderingResolution.Proposals.Concat(dictionaryReviewResolution.Proposals));
        var targetSyntax = await Syntax(toSlug, cancellationToken);
        var syntaxEligibleTargetGloss = syntaxReviewGate.ClauseCohesiveTargetGlossCandidates(
            candidates, globalKnownRenderingResolution.Proposals, targetSyntax);
        var syntaxTargetGlossOnlyResolution = targetGlossProposalResolver.ResolveAdditional(
            candidates, globalReviewKnownRenderingResolution.Proposals.Concat(dictionaryReviewResolution.Proposals), syntaxEligibleTargetGloss);
        var targetGlossReviewResolution = new EvidentiaResolution(
            dictionaryReviewResolution.Proposals.Concat(targetGlossOnlyResolution.Proposals).ToList(), 0);
        var syntaxTargetGlossReviewResolution = new EvidentiaResolution(
            dictionaryReviewResolution.Proposals.Concat(syntaxTargetGlossOnlyResolution.Proposals).ToList(), 0);
        var sourceIds = source.Select(token => token.Id).ToHashSet();
        var targetIds = target.Select(token => token.Id).ToHashSet();
        var gold = await GoldPairs(fromSlug, toSlug, sourceIds, targetIds, options.GoldSource, cancellationToken);
        var proposed = candidates.Select(candidate => (candidate.Source.Token.Id, candidate.Target.Token.Id)).ToHashSet();
        var candidateGold = proposed.Intersect(gold).Count();
        var bySource = candidates.GroupBy(candidate => candidate.Source.Token.Id)
            .ToDictionary(
                group => group.Key,
                group => group.Select(candidate => candidate.Target.Token.Id).Distinct().ToHashSet());
        var unambiguous = bySource.Where(pair => pair.Value.Count == 1)
            .Select(pair => (From: pair.Key, To: pair.Value.Single()))
            .ToHashSet();
        var finalProposals = syntaxTargetGlossReviewResolution.Proposals
            .Concat(globalReviewKnownRenderingResolution.Proposals)
            .ToList();
        return new EvidentiaChapterMeasurement(
            fromSlug,
            toSlug,
            options.AllowSourceStrongEvidence,
            options.AllowKnownRenderingEvidence,
            canonicalBook,
            canonicalChapter,
            previews.Count,
            source.Count,
            target.Count,
            sourceAnalysis.Status,
            sourceAnalysis.Detail,
            source.Count(token => token.PartOfSpeech is not null),
            source.Count(token => token.StrongNumber is not null),
            target.Count(token => token.StrongNumber is not null),
            source.Select(token => token.StrongNumber)
                .Where(number => number is not null)
                .Intersect(target.Select(token => token.StrongNumber).Where(number => number is not null))
                .Count(),
            previews.Sum(preview => preview.ContentSourceWords),
            previews.Sum(preview => preview.CoveredContentSourceWords),
            previews.Sum(preview => preview.Candidates.Count),
            candidates.Count(candidate => candidate.Evidence.Any(evidence =>
                evidence.Kind == EvidentiaEvidenceKind.Morphology)),
            sourceIds.Count(id => bySource.ContainsKey(id)),
            bySource.Values.Count(targets => targets.Count > 1),
            previews.Count(preview => preview.NeedsStatisticalFallback),
            gold.Count,
            candidateGold,
            resolution.Proposals.Count,
            resolution.Proposals.Count(proposal => gold.Contains((proposal.Source.Token.Id, proposal.Target.Token.Id))),
            resolution.UnresolvedSourceWords,
            knownRenderingResolution.Proposals.Count,
            knownRenderingResolution.Proposals.Count(proposal => gold.Contains((proposal.Source.Token.Id, proposal.Target.Token.Id))),
            globalKnownRenderingResolution.Proposals.Count,
            globalKnownRenderingResolution.Proposals.Count(proposal => gold.Contains((proposal.Source.Token.Id, proposal.Target.Token.Id))),
            reviewKnownRenderingResolution.Proposals.Count,
            reviewKnownRenderingResolution.Proposals.Count(proposal => gold.Contains((proposal.Source.Token.Id, proposal.Target.Token.Id))),
            globalReviewKnownRenderingResolution.Proposals.Count,
            globalReviewKnownRenderingResolution.Proposals.Count(proposal => gold.Contains((proposal.Source.Token.Id, proposal.Target.Token.Id))),
            targetGlossReviewResolution.Proposals.Count,
            targetGlossReviewResolution.Proposals.Count(proposal => gold.Contains((proposal.Source.Token.Id, proposal.Target.Token.Id))),
            syntaxTargetGlossReviewResolution.Proposals.Count,
            syntaxTargetGlossReviewResolution.Proposals.Count(proposal => gold.Contains((proposal.Source.Token.Id, proposal.Target.Token.Id))),
            unambiguous.Count,
            unambiguous.Intersect(gold).Count(),
            finalProposals.Select(proposal => proposal.Source.Token.Id).Distinct().Count(),
            Samples(finalProposals, candidates, source, target, gold, options.SampleSize));
    }

    public async Task<EvidentiaBookMeasurement> MeasureBook(
        string fromSlug,
        string toSlug,
        int canonicalBook,
        EvidentiaMeasurementOptions? options = null,
        int? firstChapter = null,
        int? lastChapter = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new EvidentiaMeasurementOptions();
        var chapters = await db.VerseReferences.AsNoTracking()
            .Where(reference => reference.Verse!.Text!.Slug == fromSlug
                && reference.CanonicalBook == canonicalBook)
            .Where(reference => !firstChapter.HasValue || reference.CanonicalChapter >= firstChapter.Value)
            .Where(reference => !lastChapter.HasValue || reference.CanonicalChapter <= lastChapter.Value)
            .Select(reference => reference.CanonicalChapter)
            .Distinct()
            .OrderBy(chapter => chapter)
            .ToListAsync(cancellationToken);
        var measurements = new List<EvidentiaChapterMeasurement>(chapters.Count);
        foreach (var chapter in chapters)
        {
            measurements.Add(await MeasureChapter(
                fromSlug, toSlug, canonicalBook, chapter, options, cancellationToken));
        }

        return new EvidentiaBookMeasurement(
            fromSlug,
            toSlug,
            options.AllowSourceStrongEvidence,
            options.AllowKnownRenderingEvidence,
            canonicalBook,
            measurements);
    }

    private async Task<List<EvidentiaToken>> Tokens(
        string slug,
        int canonicalBook,
        int canonicalChapter,
        int? canonicalVerse,
        CancellationToken cancellationToken)
    {
        var rows = await db.VerseReferences
            .AsNoTracking()
            .Where(reference => reference.Verse!.Text!.Slug == slug
                && reference.CanonicalBook == canonicalBook
                && reference.CanonicalChapter == canonicalChapter
                && (!canonicalVerse.HasValue || reference.CanonicalVerse == canonicalVerse.Value))
            .SelectMany(reference => reference.Verse!.Words.Select(word => new
            {
                word.Id,
                reference.CanonicalBook,
                reference.CanonicalChapter,
                reference.CanonicalVerse,
                word.Position,
                word.Surface,
                word.Lemma,
                word.StrongNumber,
                word.Gloss,
                word.Morphology,
                Language = word.Text!.Language,
            }))
            .OrderBy(row => row.CanonicalVerse)
            .ThenBy(row => row.Position)
            .ToListAsync(cancellationToken);

        if (rows.Count == 0)
        {
            throw new InvalidOperationException(
                $"{slug} has no words at canonical address {canonicalBook}:{canonicalChapter}" +
                (canonicalVerse.HasValue ? $":{canonicalVerse.Value}." : "."));
        }

        return rows.Select(row =>
            {
                var morphology = Morphology(row.Morphology);
                return new EvidentiaToken(
                    Id: row.Id,
                    Address: new EvidentiaAddress(row.CanonicalBook, row.CanonicalChapter, row.CanonicalVerse),
                    Position: row.Position,
                    Surface: row.Surface,
                    Language: row.Language,
                    Lemma: row.Lemma,
                    StrongNumber: row.StrongNumber,
                    Gloss: row.Gloss,
                    PartOfSpeech: morphology?.GetValueOrDefault("pos"),
                    Morphology: morphology);
            })
            .ToList();
    }

    private async Task<UdpipeAnnotation> SourceTokens(
        string slug, int book, int chapter, int? verse, CancellationToken cancellationToken)
    {
        var tokens = await Tokens(slug, book, chapter, verse, cancellationToken);
        return await udpipe.Annotate(tokens, cancellationToken);
    }

    private static IReadOnlyList<IEvidentiaEvidenceSource> Evidence(
        EvidentiaDictionarySenseEvidenceSource? dictionaryEvidence,
        EvidentiaKnownRenderingEvidenceSource? knownRenderingEvidence,
        TargetGlossEvidenceSource? targetGlossEvidence) =>
        [.. new IEvidentiaEvidenceSource?[] { dictionaryEvidence, knownRenderingEvidence, targetGlossEvidence }.OfType<IEvidentiaEvidenceSource>()];

    private async Task<SyntaxPrior> Syntax(string textSlug, CancellationToken cancellationToken)
    {
        if (syntaxByTargetText.TryGetValue(textSlug, out var cached))
        {
            return cached;
        }

        var textId = await db.Texts.AsNoTracking()
            .Where(text => text.Slug == textSlug)
            .Select(text => (int?)text.Id)
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new InvalidOperationException($"Unknown text {textSlug}; choose a loaded target text with syntax.");
        await db.Database.OpenConnectionAsync(cancellationToken);
        try
        {
            var syntax = await SyntaxPrior.Read((NpgsqlConnection)db.Database.GetDbConnection(), textId, cancellationToken);
            syntaxByTargetText.Add(textSlug, syntax);
            return syntax;
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }
    }

    private static IReadOnlyDictionary<string, string>? Morphology(JsonDocument? document)
    {
        if (document is null || document.RootElement.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        return document.RootElement.EnumerateObject()
            .Where(property => property.Value.ValueKind == JsonValueKind.String)
            .ToDictionary(property => property.Name, property => property.Value.GetString()!);
    }

    /// <summary>
    /// A deterministic, evenly spread reading sample of what the final review tier actually did:
    /// proposals the stored gold agrees with, proposals it contradicts, and content words carrying
    /// a gold pair that no tier would propose. Reading proposals is how a policy's failure modes
    /// are found; an aggregate percentage hides which kind of word each tier gets wrong.
    /// </summary>
    private static IReadOnlyList<string> Samples(
        IReadOnlyList<EvidentiaProposal> proposals,
        IReadOnlyList<EvidentiaCandidate> candidates,
        IReadOnlyList<EvidentiaToken> source,
        IReadOnlyList<EvidentiaToken> target,
        IReadOnlySet<(long From, long To)> gold,
        int wanted)
    {
        if (wanted <= 0)
        {
            return [];
        }

        var byTargetId = target.DistinctBy(token => token.Id).ToDictionary(token => token.Id);
        var goldTargetsBySource = gold.GroupBy(pair => pair.From)
            .ToDictionary(group => group.Key, group => group.Select(pair => pair.To).ToList());
        var ordered = proposals
            .OrderBy(proposal => proposal.Source.Token.Address.Verse)
            .ThenBy(proposal => proposal.Source.Token.Position)
            .ToList();
        var accepted = ordered.Where(proposal =>
            gold.Contains((proposal.Source.Token.Id, proposal.Target.Token.Id))).ToList();
        var refused = ordered.Where(proposal =>
            !gold.Contains((proposal.Source.Token.Id, proposal.Target.Token.Id))).ToList();
        var proposed = proposals.Select(proposal => proposal.Source.Token.Id).ToHashSet();
        var candidatesBySource = candidates.GroupBy(candidate => candidate.Source.Token.Id)
            .ToDictionary(group => group.Key, group => group.ToList());
        var abstained = source
            .Where(token => !proposed.Contains(token.Id) && goldTargetsBySource.ContainsKey(token.Id))
            .DistinctBy(token => token.Id)
            .ToList();

        var share = Math.Max(1, wanted / 3);
        return
        [
            .. Spread(accepted, share).Select(proposal => Line("accepted", proposal, gold)),
            .. Spread(refused, share).Select(proposal => Line("refused ", proposal, gold)),
            .. Spread(abstained, wanted - 2 * share).Select(token => Abstention(token, goldTargetsBySource, byTargetId, candidatesBySource)),
        ];
    }

    private static IEnumerable<T> Spread<T>(IReadOnlyList<T> items, int wanted)
    {
        if (items.Count == 0 || wanted <= 0)
        {
            yield break;
        }

        var stride = Math.Max(1, items.Count / wanted);
        for (var index = 0; index < items.Count && wanted > 0; index += stride, wanted--)
        {
            yield return items[index];
        }
    }

    private static string Line(
        string verdict,
        EvidentiaProposal proposal,
        IReadOnlySet<(long From, long To)> gold)
    {
        var goldTargets = gold.Where(pair => pair.From == proposal.Source.Token.Id).Select(pair => pair.To).ToList();
        var expected = verdict.Trim() == "refused"
            ? goldTargets.Count == 0 ? "; gold: none" : $"; gold target ids: {string.Join("/", goldTargets)}"
            : string.Empty;
        return $"{verdict} v{proposal.Source.Token.Address.Verse} " +
               $"'{proposal.Source.Token.Surface}' → '{proposal.Target.Token.Surface}' " +
               $"[{proposal.Target.Token.StrongNumber ?? "no-strong"}] {proposal.Kind} {proposal.Confidence:F2} " +
               $"({proposal.Trace?.Tier}: {proposal.Trace?.Rationale}){expected}";
    }

    private static string Abstention(
        EvidentiaToken token,
        IReadOnlyDictionary<long, List<long>> goldTargetsBySource,
        IReadOnlyDictionary<long, EvidentiaToken> byTargetId,
        IReadOnlyDictionary<long, List<EvidentiaCandidate>> candidatesBySource)
    {
        var expected = string.Join(" ", goldTargetsBySource[token.Id]
            .Select(id => byTargetId.TryGetValue(id, out var word) ? $"'{word.Surface}'" : $"#{id}"));
        var candidates = candidatesBySource.GetValueOrDefault(token.Id) ?? [];
        var best = candidates.OrderByDescending(candidate => candidate.Score).FirstOrDefault();
        var reached = best is null
            ? "no candidate edge"
            : $"{candidates.Count} candidates, best '{best.Target.Token.Surface}' {best.Score:F2} " +
              $"[{string.Join(", ", best.Evidence.Select(evidence => evidence.Kind))}]";
        return $"abstain  v{token.Address.Verse} '{token.Surface}' gold {expected}; {reached}";
    }

    private async Task<HashSet<(long From, long To)>> GoldPairs(
        string fromSlug,
        string toSlug,
        IReadOnlySet<long> sourceIds,
        IReadOnlySet<long> targetIds,
        string? goldSource,
        CancellationToken cancellationToken)
    {
        var texts = await db.Texts.AsNoTracking()
            .Where(text => text.Slug == fromSlug || text.Slug == toSlug)
            .Select(text => new { text.Id, text.Slug })
            .ToListAsync(cancellationToken);
        var from = texts.SingleOrDefault(text => text.Slug == fromSlug)
            ?? throw new InvalidOperationException($"Unknown text {fromSlug}.");
        var to = texts.SingleOrDefault(text => text.Slug == toSlug)
            ?? throw new InvalidOperationException($"Unknown text {toSlug}.");
        var links = await db.Links.AsNoTracking()
            .Where(link => (link.FromTextId == from.Id && link.ToTextId == to.Id)
                || (link.FromTextId == to.Id && link.ToTextId == from.Id))
            .Where(link => link.Method == LinkMethod.StatedBySource
                || link.Method == LinkMethod.StrongNumber)
            .Where(link => goldSource == null || link.Source.Contains(goldSource))
            .SelectMany(link => link.Words.Select(word => new
            {
                link.Id,
                link.FromTextId,
                link.ToTextId,
                word.WordId,
                word.Side,
            }))
            .ToListAsync(cancellationToken);

        return links.GroupBy(row => row.Id)
            .SelectMany(group =>
            {
                var forward = group.First().FromTextId == from.Id;
                var left = group.Where(row => row.Side == (forward ? LinkSide.From : LinkSide.To))
                    .Select(row => row.WordId);
                var right = group.Where(row => row.Side == (forward ? LinkSide.To : LinkSide.From))
                    .Select(row => row.WordId);
                return left.SelectMany(one => right.Select(two => (one, two)));
            })
            .Where(pair => sourceIds.Contains(pair.one) && targetIds.Contains(pair.two))
            .ToHashSet();
    }
}

internal sealed record EvidentiaBookMeasurement(
    string From,
    string To,
    bool SourceStrongEvidenceEnabled,
    bool KnownRenderingEvidenceEnabled,
    int CanonicalBook,
    IReadOnlyList<EvidentiaChapterMeasurement> Chapters)
{
    public int Verses => Chapters.Sum(chapter => chapter.Verses);
    public int SourceWords => Chapters.Sum(chapter => chapter.SourceWords);
    public int TargetWords => Chapters.Sum(chapter => chapter.TargetWords);
    public int SourcePartOfSpeechWords => Chapters.Sum(chapter => chapter.SourcePartOfSpeechWords);
    public int CandidateEdges => Chapters.Sum(chapter => chapter.CandidateEdges);
    public int MorphologyScoredEdges => Chapters.Sum(chapter => chapter.MorphologyScoredEdges);
    public int GoldPairs => Chapters.Sum(chapter => chapter.GoldPairs);
    public int GoldPairsInCandidates => Chapters.Sum(chapter => chapter.GoldPairsInCandidates);
    public int GlobalKnownRenderingProposals => Chapters.Sum(chapter => chapter.GlobalKnownRenderingProposals);
    public int CorrectGlobalKnownRenderingProposals => Chapters.Sum(chapter => chapter.CorrectGlobalKnownRenderingProposals);
    public int GlobalReviewKnownRenderingProposals => Chapters.Sum(chapter => chapter.GlobalReviewKnownRenderingProposals);
    public int CorrectGlobalReviewKnownRenderingProposals => Chapters.Sum(chapter => chapter.CorrectGlobalReviewKnownRenderingProposals);
    public int GlobalReviewAndSyntaxTargetGlossProposals => Chapters.Sum(chapter => chapter.GlobalReviewAndSyntaxTargetGlossProposals);
    public int CorrectGlobalReviewAndSyntaxTargetGlossProposals => Chapters.Sum(chapter => chapter.CorrectGlobalReviewAndSyntaxTargetGlossProposals);
    public int FallbackVerses => Chapters.Sum(chapter => chapter.FallbackVerses);
    public int ContentSourceWords => Chapters.Sum(chapter => chapter.ContentSourceWords);
    public int FinalProposedSourceWords => Chapters.Sum(chapter => chapter.FinalProposedSourceWords);
    public double Abstention => ContentSourceWords == 0
        ? 0
        : 1 - (double)FinalProposedSourceWords / ContentSourceWords;
    public double CandidateRecall => GoldPairs == 0 ? 0 : (double)GoldPairsInCandidates / GoldPairs;
    public double GlobalKnownRenderingPrecision => GlobalKnownRenderingProposals == 0 ? 0
        : (double)CorrectGlobalKnownRenderingProposals / GlobalKnownRenderingProposals;
    public double GlobalKnownRenderingRecall => GoldPairs == 0 ? 0
        : (double)CorrectGlobalKnownRenderingProposals / GoldPairs;
    public double GlobalReviewKnownRenderingPrecision => GlobalReviewKnownRenderingProposals == 0 ? 0
        : (double)CorrectGlobalReviewKnownRenderingProposals / GlobalReviewKnownRenderingProposals;
    public double GlobalReviewKnownRenderingRecall => GoldPairs == 0 ? 0
        : (double)CorrectGlobalReviewKnownRenderingProposals / GoldPairs;
    public double GlobalReviewAndSyntaxTargetGlossPrecision => GlobalReviewAndSyntaxTargetGlossProposals == 0 ? 0
        : (double)CorrectGlobalReviewAndSyntaxTargetGlossProposals / GlobalReviewAndSyntaxTargetGlossProposals;
    public double GlobalReviewAndSyntaxTargetGlossRecall => GoldPairs == 0 ? 0
        : (double)CorrectGlobalReviewAndSyntaxTargetGlossProposals / GoldPairs;

    public override string ToString()
    {
        var annotationStatuses = string.Join(", ", Chapters
            .GroupBy(chapter => chapter.SourceAnnotationStatus)
            .OrderBy(group => group.Key)
            .Select(group => $"{group.Key} {group.Count():N0}/{Chapters.Count:N0}"));
        return $"EVIDENTIA book measurement {From} → {To}; mode: " +
               (SourceStrongEvidenceEnabled ? "direct-source-Strong allowed" : "no-source-Strong") +
               (KnownRenderingEvidenceEnabled ? "; held-out learned renderings enabled" : "; dictionary-only baseline") +
               $"; canonical book {CanonicalBook}\n" +
               $"chapters: {Chapters.Count:N0}; verses: {Verses:N0}; words: source {SourceWords:N0}, target {TargetWords:N0}\n" +
               $"source local UDPipe: {annotationStatuses}; POS values: {SourcePartOfSpeechWords:N0}/{SourceWords:N0}\n" +
               $"candidate edges: {CandidateEdges:N0} ({MorphologyScoredEdges:N0} with grammatical agreement); " +
               $"gold candidate hits: {GoldPairsInCandidates:N0}/{GoldPairs:N0} ({CandidateRecall:P2}); fallback verses: {FallbackVerses:N0}\n" +
               $"global stable: {CorrectGlobalKnownRenderingProposals:N0}/{GlobalKnownRenderingProposals:N0} " +
               $"({GlobalKnownRenderingPrecision:P2}); gold recall: {GlobalKnownRenderingRecall:P2}\n" +
               $"global review: {CorrectGlobalReviewKnownRenderingProposals:N0}/{GlobalReviewKnownRenderingProposals:N0} " +
               $"({GlobalReviewKnownRenderingPrecision:P2}); gold recall: {GlobalReviewKnownRenderingRecall:P2}\n" +
               $"global review + syntax-gated target gloss: {CorrectGlobalReviewAndSyntaxTargetGlossProposals:N0}/{GlobalReviewAndSyntaxTargetGlossProposals:N0} " +
               $"({GlobalReviewAndSyntaxTargetGlossPrecision:P2}); gold recall: {GlobalReviewAndSyntaxTargetGlossRecall:P2}\n" +
               $"final abstention: {ContentSourceWords - FinalProposedSourceWords:N0}/{ContentSourceWords:N0} " +
               $"content source words unplaced ({Abstention:P2})" +
               string.Concat(Chapters
                   .Where(chapter => chapter.Samples.Count > 0)
                   .Select(chapter => $"\nsample, chapter {chapter.CanonicalChapter}:\n"
                       + string.Join("\n", chapter.Samples)));
    }
}

internal sealed record EvidentiaCorpusPreview(
    string From,
    string To,
    int CanonicalBook,
    int CanonicalChapter,
    int? CanonicalVerse,
    int SourceWordCount,
    int TargetWordCount,
    EvidentiaPreview Result)
{
    public override string ToString()
    {
        var proven = Result.Candidates.Count(candidate => candidate.Score >= 0.55);
        var sample = Result.Candidates
            .Take(8)
            .Select(candidate =>
                $"{candidate.Source.Token.Address.Verse}:{candidate.Source.Token.Position} '{candidate.Source.Token.Surface}' " +
                $"→ {candidate.Target.Token.Address.Verse}:{candidate.Target.Token.Position} '{candidate.Target.Token.Surface}' " +
                $"{candidate.Score:P0} [{string.Join(", ", candidate.Evidence.Select(evidence => evidence.Kind))}]");

        return $"EVIDENTIA preview {From} → {To}; canonical {CanonicalBook}:{CanonicalChapter}" +
               (CanonicalVerse.HasValue ? $":{CanonicalVerse.Value}" : string.Empty) + "\n" +
               $"words: source {SourceWordCount:N0}, target chapter {TargetWordCount:N0}; " +
               $"candidates: {Result.Candidates.Count:N0}; supported candidates (≥55%): {proven:N0}\n" +
               $"content coverage: {Result.ContentCoverage:P1}; status: {Result.Status}; " +
               $"statistical fallback: {(Result.NeedsStatisticalFallback ? "needed" : "not needed")}\n" +
               $"still missing: {string.Join(", ", Result.Todos)}" +
               (sample.Any() ? "\nexamples:\n" + string.Join("\n", sample) : string.Empty);
    }
}

internal sealed record EvidentiaChapterMeasurement(
    string From,
    string To,
    bool SourceStrongEvidenceEnabled,
    bool KnownRenderingEvidenceEnabled,
    int CanonicalBook,
    int CanonicalChapter,
    int Verses,
    int SourceWords,
    int TargetWords,
    UdpipeAnnotationStatus SourceAnnotationStatus,
    string? SourceAnnotationDetail,
    int SourcePartOfSpeechWords,
    int SourceStrongWords,
    int TargetStrongWords,
    int SharedStrongNumbers,
    int ContentSourceWords,
    int CoveredContentSourceWords,
    int CandidateEdges,
    int MorphologyScoredEdges,
    int CoveredSourceWords,
    int AmbiguousSourceWords,
    int FallbackVerses,
    int GoldPairs,
    int GoldPairsInCandidates,
    int StrongResolvedProposals,
    int CorrectStrongResolvedProposals,
    int StrongUnresolvedSourceWords,
    int KnownRenderingProposals,
    int CorrectKnownRenderingProposals,
    int GlobalKnownRenderingProposals,
    int CorrectGlobalKnownRenderingProposals,
    int ReviewKnownRenderingProposals,
    int CorrectReviewKnownRenderingProposals,
    int GlobalReviewKnownRenderingProposals,
    int CorrectGlobalReviewKnownRenderingProposals,
    int TargetGlossReviewProposals,
    int CorrectTargetGlossReviewProposals,
    int SyntaxTargetGlossReviewProposals,
    int CorrectSyntaxTargetGlossReviewProposals,
    int UnambiguousProposals,
    int CorrectUnambiguousProposals,
    int FinalProposedSourceWords,
    IReadOnlyList<string> Samples)
{
    public double SourceCoverage => SourceWords == 0 ? 0 : (double)CoveredSourceWords / SourceWords;
    public double ContentCoverage => ContentSourceWords == 0
        ? 0
        : (double)CoveredContentSourceWords / ContentSourceWords;
    public double CandidateRecall => GoldPairs == 0 ? 0 : (double)GoldPairsInCandidates / GoldPairs;
    public double StrongResolutionRecall => GoldPairs == 0 ? 0
        : (double)CorrectStrongResolvedProposals / GoldPairs;
    public double StrongResolutionPrecision => StrongResolvedProposals == 0 ? 0
        : (double)CorrectStrongResolvedProposals / StrongResolvedProposals;
    public double KnownRenderingRecall => GoldPairs == 0 ? 0
        : (double)CorrectKnownRenderingProposals / GoldPairs;
    public double KnownRenderingPrecision => KnownRenderingProposals == 0 ? 0
        : (double)CorrectKnownRenderingProposals / KnownRenderingProposals;
    public double GlobalKnownRenderingRecall => GoldPairs == 0 ? 0
        : (double)CorrectGlobalKnownRenderingProposals / GoldPairs;
    public double GlobalKnownRenderingPrecision => GlobalKnownRenderingProposals == 0 ? 0
        : (double)CorrectGlobalKnownRenderingProposals / GlobalKnownRenderingProposals;
    public double ReviewKnownRenderingRecall => GoldPairs == 0 ? 0
        : (double)CorrectReviewKnownRenderingProposals / GoldPairs;
    public double ReviewKnownRenderingPrecision => ReviewKnownRenderingProposals == 0 ? 0
        : (double)CorrectReviewKnownRenderingProposals / ReviewKnownRenderingProposals;
    public double GlobalReviewKnownRenderingRecall => GoldPairs == 0 ? 0
        : (double)CorrectGlobalReviewKnownRenderingProposals / GoldPairs;
    public double GlobalReviewKnownRenderingPrecision => GlobalReviewKnownRenderingProposals == 0 ? 0
        : (double)CorrectGlobalReviewKnownRenderingProposals / GlobalReviewKnownRenderingProposals;
    public int GlobalReviewAndTargetGlossProposals => GlobalReviewKnownRenderingProposals + TargetGlossReviewProposals;
    public int CorrectGlobalReviewAndTargetGlossProposals => CorrectGlobalReviewKnownRenderingProposals
        + CorrectTargetGlossReviewProposals;
    public double GlobalReviewAndTargetGlossRecall => GoldPairs == 0 ? 0
        : (double)CorrectGlobalReviewAndTargetGlossProposals / GoldPairs;
    public double GlobalReviewAndTargetGlossPrecision => GlobalReviewAndTargetGlossProposals == 0 ? 0
        : (double)CorrectGlobalReviewAndTargetGlossProposals / GlobalReviewAndTargetGlossProposals;
    public int GlobalReviewAndSyntaxTargetGlossProposals => GlobalReviewKnownRenderingProposals + SyntaxTargetGlossReviewProposals;
    public int CorrectGlobalReviewAndSyntaxTargetGlossProposals => CorrectGlobalReviewKnownRenderingProposals
        + CorrectSyntaxTargetGlossReviewProposals;
    public double GlobalReviewAndSyntaxTargetGlossRecall => GoldPairs == 0 ? 0
        : (double)CorrectGlobalReviewAndSyntaxTargetGlossProposals / GoldPairs;
    public double GlobalReviewAndSyntaxTargetGlossPrecision => GlobalReviewAndSyntaxTargetGlossProposals == 0 ? 0
        : (double)CorrectGlobalReviewAndSyntaxTargetGlossProposals / GlobalReviewAndSyntaxTargetGlossProposals;
    public double UnambiguousPrecision => UnambiguousProposals == 0
        ? 0
        : (double)CorrectUnambiguousProposals / UnambiguousProposals;

    /// <summary>
    /// The share of content source words the final review tier declines to place at all. It is
    /// reported beside precision and recall because a policy can buy either of them with it, and
    /// an aggregate that leaves it out cannot be compared with another run.
    /// </summary>
    public double Abstention => ContentSourceWords == 0
        ? 0
        : 1 - (double)FinalProposedSourceWords / ContentSourceWords;

    public override string ToString()
    {
        var evaluation = GoldPairs == 0
            ? "stored Strong/source pairs in scope: 0; comparison: unavailable (no existing sourced gold pairs)\n" +
              $"resolved Strong proposals: {StrongResolvedProposals:N0}; not accepted automatically\n" +
              $"single-candidate graph proposals: {UnambiguousProposals:N0}; not evaluated without gold"
            : $"stored Strong/source pairs in scope: {GoldPairs:N0}; candidate hits: {GoldPairsInCandidates:N0}/{GoldPairs:N0} ({CandidateRecall:P1})\n" +
              $"resolved Strong proposals: {StrongResolvedProposals:N0}; correct: {CorrectStrongResolvedProposals:N0}/{StrongResolvedProposals:N0} " +
              $"({StrongResolutionPrecision:P1}); gold recall: {CorrectStrongResolvedProposals:N0}/{GoldPairs:N0} ({StrongResolutionRecall:P1}); " +
              $"unresolved repeated/mismatched Strong words: {StrongUnresolvedSourceWords:N0}\n" +
              $"stable learned-rendering proposals: {CorrectKnownRenderingProposals:N0}/{KnownRenderingProposals:N0} " +
              $"({KnownRenderingPrecision:P1}); gold recall: {CorrectKnownRenderingProposals:N0}/{GoldPairs:N0} ({KnownRenderingRecall:P1})\n" +
              $"global stable learned-rendering proposals: {CorrectGlobalKnownRenderingProposals:N0}/{GlobalKnownRenderingProposals:N0} " +
              $"({GlobalKnownRenderingPrecision:P1}); gold recall: {CorrectGlobalKnownRenderingProposals:N0}/{GoldPairs:N0} ({GlobalKnownRenderingRecall:P1})\n" +
              $"review learned-rendering proposals: {CorrectReviewKnownRenderingProposals:N0}/{ReviewKnownRenderingProposals:N0} " +
              $"({ReviewKnownRenderingPrecision:P1}); gold recall: {CorrectReviewKnownRenderingProposals:N0}/{GoldPairs:N0} ({ReviewKnownRenderingRecall:P1})\n" +
              $"global review learned-rendering proposals: {CorrectGlobalReviewKnownRenderingProposals:N0}/{GlobalReviewKnownRenderingProposals:N0} " +
              $"({GlobalReviewKnownRenderingPrecision:P1}); gold recall: {CorrectGlobalReviewKnownRenderingProposals:N0}/{GoldPairs:N0} ({GlobalReviewKnownRenderingRecall:P1})\n" +
              $"unique target-gloss review additions: {CorrectTargetGlossReviewProposals:N0}/{TargetGlossReviewProposals:N0}; " +
              $"global review + target gloss: {CorrectGlobalReviewAndTargetGlossProposals:N0}/{GlobalReviewAndTargetGlossProposals:N0} " +
              $"({GlobalReviewAndTargetGlossPrecision:P1}); gold recall: {CorrectGlobalReviewAndTargetGlossProposals:N0}/{GoldPairs:N0} ({GlobalReviewAndTargetGlossRecall:P1})\n" +
              $"syntax-gated target-gloss review additions: {CorrectSyntaxTargetGlossReviewProposals:N0}/{SyntaxTargetGlossReviewProposals:N0}; " +
              $"global review + syntax-gated target gloss: {CorrectGlobalReviewAndSyntaxTargetGlossProposals:N0}/{GlobalReviewAndSyntaxTargetGlossProposals:N0} " +
              $"({GlobalReviewAndSyntaxTargetGlossPrecision:P1}); gold recall: {CorrectGlobalReviewAndSyntaxTargetGlossProposals:N0}/{GoldPairs:N0} ({GlobalReviewAndSyntaxTargetGlossRecall:P1})\n" +
              $"single-candidate graph proposals: {UnambiguousProposals:N0}; correct: {CorrectUnambiguousProposals:N0}/{UnambiguousProposals:N0} " +
              $"({UnambiguousPrecision:P1})";

        return $"EVIDENTIA measurement {From} → {To}; mode: " +
                (SourceStrongEvidenceEnabled ? "direct-source-Strong allowed" : "no-source-Strong") +
               (KnownRenderingEvidenceEnabled ? "; held-out learned renderings enabled" : "; dictionary-only baseline") +
               $"; canonical {CanonicalBook}:{CanonicalChapter}\n" +
               $"verses: {Verses:N0}; words: source {SourceWords:N0}, target {TargetWords:N0}\n" +
               $"source local UDPipe: {SourceAnnotationStatus}" +
               (SourceAnnotationDetail is null ? string.Empty : $" ({SourceAnnotationDetail})") +
               $"; POS values: {SourcePartOfSpeechWords:N0}; " +
               $"source Strong tags in corpus: {SourceStrongWords:N0} " +
               (SourceStrongEvidenceEnabled ? "(enabled)" : "(ignored by this mode)") + "; " +
               $"target Strong tags: {TargetStrongWords:N0}; " +
               $"shared Strong values: {SharedStrongNumbers:N0}\n" +
               $"candidate edges: {CandidateEdges:N0} ({MorphologyScoredEdges:N0} with grammatical agreement); " +
               $"source coverage: {CoveredSourceWords:N0}/{SourceWords:N0} ({SourceCoverage:P1}); " +
               $"content coverage: {CoveredContentSourceWords:N0}/{ContentSourceWords:N0} ({ContentCoverage:P1}); " +
               $"ambiguous source words: {AmbiguousSourceWords:N0}; fallback verses: {FallbackVerses:N0}\n" +
               evaluation +
               $"\nfinal abstention: {ContentSourceWords - FinalProposedSourceWords:N0}/{ContentSourceWords:N0} " +
               $"content source words unplaced ({Abstention:P1})" +
               (Samples.Count == 0 ? string.Empty : "\nsample:\n" + string.Join("\n", Samples));
    }
}

/// <summary>
/// What one read-only EVIDENTIA measurement is allowed to read. The learned-rendering source is
/// separate from the measured text on purpose: a benchmark whose index and whose gold come from
/// the same stated table measures self-consistency, so an independent number needs the renderings
/// learned from one pair and the answer key taken from another.
/// </summary>
/// <param name="LearnRenderingsFrom">
/// The text whose stated renderings feed the learned index, when it is not the measured source.
/// </param>
/// <param name="LearnedRenderingMethods">
/// Which link methods the learned index may read. The default is source-stated only; naming
/// <see cref="LinkMethod.StrongNumber"/> admits printed-number inferences, which is the only
/// English rendering evidence the New Testament pairs have that is not the Berean gold itself.
/// </param>
/// <param name="GoldSource">
/// A substring of <c>Link.Source</c>, so one dataset's rows can be scored on their own.
/// </param>
internal sealed record EvidentiaMeasurementOptions(
    bool AllowSourceStrongEvidence = true,
    bool AllowKnownRenderingEvidence = true,
    string? LearnRenderingsFrom = null,
    IReadOnlyList<LinkMethod>? LearnedRenderingMethods = null,
    string? GoldSource = null,
    int SampleSize = 0);
