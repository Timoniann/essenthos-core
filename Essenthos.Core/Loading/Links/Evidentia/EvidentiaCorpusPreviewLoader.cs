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
    EvidentiaKnownRenderingIndex knownRenderingIndex,
    LanguagePackRegistry languagePacks)
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
        var goldAnnotation = await Gold(fromSlug, toSlug, sourceIds, targetIds, options.GoldSource, cancellationToken);
        var gold = goldAnnotation.Pairs;
        var covered = goldAnnotation.CoveredSourceWords;
        var contentSourceWordIds = previews.SelectMany(preview => preview.ContentSourceWordIds).ToHashSet();
        var proposed = candidates.Select(candidate => (candidate.Source.Token.Id, candidate.Target.Token.Id)).ToHashSet();
        var candidateGold = proposed.Intersect(gold).Count();
        var bySource = candidates.GroupBy(candidate => candidate.Source.Token.Id)
            .ToDictionary(
                group => group.Key,
                group => group.Select(candidate => candidate.Target.Token.Id).Distinct().ToHashSet());
        var unambiguous = bySource.Where(pair => pair.Value.Count == 1)
            .Select(pair => (From: pair.Key, To: pair.Value.Single()))
            .ToHashSet();
        var lexicalProposals = syntaxTargetGlossReviewResolution.Proposals
            .Concat(globalReviewKnownRenderingResolution.Proposals)
            .ToList();
        var attachedWords = EvidentiaAttachedWords.Resolve(
            EvidentiaAuxiliaryWords.Mark([.. source.Select(Analyse).OfType<EvidentiaAnalysis>()]),
            [.. target.Select(Analyse).OfType<EvidentiaAnalysis>()],
            lexicalProposals);
        List<EvidentiaProposal> finalProposals = [.. lexicalProposals, .. attachedWords];
        var words = EvidentiaSourceWordAccount.Classify(
            source,
            Analyse,
            candidates,
            finalProposals,
            knownRenderingProposalResolver.Admitted(candidates, EvidentiaKnownRenderingProposalResolver.Review),
            gold,
            covered,
            canonicalBook,
            canonicalChapter);
        options.Decisions?.Record(new EvidentiaChapterDecisions(
            canonicalBook, canonicalChapter, source, contentSourceWordIds, candidates,
            globalKnownRenderingResolution.Proposals, finalProposals));
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
            covered.Count,
            covered.Count(id => contentSourceWordIds.Contains(id)),
            EvidentiaTierScore.Of(resolution.Proposals, gold, covered),
            resolution.UnresolvedSourceWords,
            EvidentiaTierScore.Of(knownRenderingResolution.Proposals, gold, covered),
            EvidentiaTierScore.Of(globalKnownRenderingResolution.Proposals, gold, covered),
            EvidentiaTierScore.Of(reviewKnownRenderingResolution.Proposals, gold, covered),
            EvidentiaTierScore.Of(globalReviewKnownRenderingResolution.Proposals, gold, covered),
            EvidentiaTierScore.Of(targetGlossReviewResolution.Proposals, gold, covered),
            EvidentiaTierScore.Of(syntaxTargetGlossReviewResolution.Proposals, gold, covered),
            EvidentiaTierScore.Of(attachedWords, gold, covered),
            new EvidentiaTierScore(
                unambiguous.Count,
                unambiguous.Intersect(gold).Count(),
                unambiguous.Count(pair => covered.Contains(pair.From))),
            lexicalProposals.Select(proposal => proposal.Source.Token.Id).Distinct().Count(),
            knownRenderingEvidence?.AskedForms ?? new HashSet<string>(StringComparer.Ordinal),
            knownRenderingEvidence?.AnsweredForms ?? new HashSet<string>(StringComparer.Ordinal),
            Samples(finalProposals, candidates, source, target, gold, options.SampleSize),
            options.RecordDisagreements
                ? Disagreements(finalProposals, source, target, goldAnnotation, canonicalBook, canonicalChapter)
                : [],
            EvidentiaSourceWordAccount.Of(words),
            options.RecordWords ? words : []);
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

    private EvidentiaAnalysis? Analyse(EvidentiaToken token) =>
        languagePacks.TryAnalyse(token, out var analysis) ? analysis : null;

    private async Task<List<EvidentiaToken>> Tokens(
        string slug,
        int canonicalBook,
        int canonicalChapter,
        int? canonicalVerse,
        CancellationToken cancellationToken)
    {
        // A verse spanning two canonical verses carries a reference row for each, and reading the
        // words through the references gave every one of its words back once per row - with the
        // same id and two different addresses. Every count downstream doubled for it, and every
        // resolver could propose the same source word twice. One placement per verse, the primary
        // one where the scope holds it.
        var placements = await db.VerseReferences
            .AsNoTracking()
            .Where(reference => reference.Verse!.Text!.Slug == slug
                && reference.CanonicalBook == canonicalBook
                && reference.CanonicalChapter == canonicalChapter
                && (!canonicalVerse.HasValue || reference.CanonicalVerse == canonicalVerse.Value))
            .Select(reference => new
            {
                reference.VerseId,
                reference.CanonicalBook,
                reference.CanonicalChapter,
                reference.CanonicalVerse,
                reference.IsPrimary,
            })
            .ToListAsync(cancellationToken);
        var addressByVerse = placements
            .GroupBy(placement => placement.VerseId)
            .ToDictionary(
                group => group.Key,
                group => group
                    .OrderByDescending(placement => placement.IsPrimary)
                    .ThenBy(placement => placement.CanonicalVerse)
                    .Select(placement => new EvidentiaAddress(
                        placement.CanonicalBook, placement.CanonicalChapter, placement.CanonicalVerse))
                    .First());
        var verseIds = addressByVerse.Keys.ToList();
        var rows = await db.Words
            .AsNoTracking()
            .Where(word => verseIds.Contains(word.VerseId))
            .Select(word => new
            {
                word.Id,
                word.VerseId,
                word.Position,
                word.Surface,
                word.Trailer,
                word.Lemma,
                word.StrongNumber,
                word.Gloss,
                word.Morphology,
                Language = word.Text!.Language,
            })
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
                    Address: addressByVerse[row.VerseId],
                    Position: row.Position,
                    Surface: row.Surface,
                    Language: row.Language,
                    Trailer: row.Trailer,
                    Lemma: row.Lemma,
                    StrongNumber: row.StrongNumber,
                    Gloss: row.Gloss,
                    PartOfSpeech: morphology?.GetValueOrDefault("pos"),
                    Morphology: morphology);
            })
            .OrderBy(token => token.Address.Verse)
            .ThenBy(token => token.Position)
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

    /// <summary>
    /// The answer key in scope, as pairs and as the annotation those pairs came from. The links are
    /// kept beside the pairs because a disagreement cannot be read without them: how many words
    /// stood on each side of the link decides whether a one-to-one selector could have satisfied it
    /// at all, and which dataset stated it decides whose convention is being compared with ours.
    /// </summary>
    private async Task<EvidentiaGold> Gold(
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
        // Only the links touching a word of the passage can yield a pair in it. Reading every link
        // between the two texts for each chapter carried a whole Bible's worth of rows per chapter,
        // and with the source string on each row it exhausted the server's shared memory.
        var scopeWords = sourceIds.ToList();
        var inScope = db.LinkWords.Where(word => scopeWords.Contains(word.WordId)).Select(word => word.LinkId);
        var scopedLinks = db.Links.AsNoTracking()
            .Where(link => inScope.Contains(link.Id))
            .Where(link => (link.FromTextId == from.Id && link.ToTextId == to.Id)
                || (link.FromTextId == to.Id && link.ToTextId == from.Id))
            .Where(link => link.Method == LinkMethod.StatedBySource
                || link.Method == LinkMethod.StrongNumber)
            .Where(link => goldSource == null || link.Source.Contains(goldSource));
        var described = await scopedLinks
            .Select(link => new { link.Id, link.FromTextId, link.Method, link.Source })
            .ToDictionaryAsync(link => link.Id, cancellationToken);
        var links = await scopedLinks
            .SelectMany(link => link.Words.Select(word => new
            {
                link.Id,
                word.WordId,
                word.Side,
            }))
            .ToListAsync(cancellationToken);

        var goldLinks = links.GroupBy(row => row.Id)
            .Select(group =>
            {
                var link = described[group.Key];
                var forward = link.FromTextId == from.Id;
                var left = group.Where(row => row.Side == (forward ? LinkSide.From : LinkSide.To))
                    .Select(row => row.WordId)
                    .ToList();
                var right = group.Where(row => row.Side == (forward ? LinkSide.To : LinkSide.From))
                    .Select(row => row.WordId)
                    .ToList();
                return new EvidentiaGoldLink(
                    group.Key,
                    link.Method,
                    link.Source,
                    left,
                    right);
            })
            .ToList();
        var pairs = goldLinks
            .SelectMany(link => link.SourceWords.SelectMany(one => link.TargetWords.Select(two => (one, two))))
            .Where(pair => sourceIds.Contains(pair.one) && targetIds.Contains(pair.two))
            .ToHashSet();
        var bySourceWord = new Dictionary<long, List<EvidentiaGoldLink>>();
        foreach (var link in goldLinks.Where(link =>
                     link.SourceWords.Any(sourceIds.Contains) && link.TargetWords.Any(targetIds.Contains)))
        {
            foreach (var word in link.SourceWords.Where(sourceIds.Contains))
            {
                if (!bySourceWord.TryGetValue(word, out var found))
                {
                    bySourceWord[word] = found = [];
                }

                found.Add(link);
            }
        }

        return new EvidentiaGold(pairs, pairs.Select(pair => pair.one).ToHashSet(), bySourceWord);
    }

    /// <summary>
    /// Every proposal the answer key contradicts, written out with enough of both annotations to be
    /// read: the verse, the word we placed, the words the gold placed it on, and how many words its
    /// link joined. A proposal on a word the gold never reaches is written out too, marked, because
    /// the count of those is the part of the old precision figure that was never a mistake.
    /// </summary>
    private static IReadOnlyList<EvidentiaDisagreement> Disagreements(
        IReadOnlyList<EvidentiaProposal> proposals,
        IReadOnlyList<EvidentiaToken> source,
        IReadOnlyList<EvidentiaToken> target,
        EvidentiaGold gold,
        int canonicalBook,
        int canonicalChapter)
    {
        var byTargetId = target.DistinctBy(token => token.Id).ToDictionary(token => token.Id);
        var sourceByVerse = source.DistinctBy(token => token.Id)
            .GroupBy(token => token.Address.Verse)
            .ToDictionary(group => group.Key, group => group.OrderBy(token => token.Position).ToList());
        return
        [
            .. proposals
                .Where(proposal => !gold.Pairs.Contains((proposal.Source.Token.Id, proposal.Target.Token.Id)))
                .OrderBy(proposal => proposal.Source.Token.Address.Verse)
                .ThenBy(proposal => proposal.Source.Token.Position)
                .Select(proposal =>
                {
                    var word = proposal.Source.Token;
                    var links = gold.LinksBySourceWord.GetValueOrDefault(word.Id) ?? [];
                    return new EvidentiaDisagreement(
                        canonicalBook,
                        canonicalChapter,
                        word.Address.Verse,
                        word.Id,
                        word.Position,
                        word.Surface,
                        proposal.Target.Token.Id,
                        proposal.Target.Token.Position,
                        proposal.Target.Token.Surface,
                        proposal.Target.Token.StrongNumber,
                        proposal.Target.Token.Lemma,
                        proposal.Target.Token.Gloss,
                        gold.CoveredSourceWords.Contains(word.Id),
                        [.. links.Select(link => $"{link.SourceWords.Count}x{link.TargetWords.Count}")],
                        [.. links.Select(link => link.Source).Distinct()],
                        [.. links
                            .SelectMany(link => link.TargetWords)
                            .Distinct()
                            .Where(byTargetId.ContainsKey)
                            .Select(id => byTargetId[id])
                            .OrderBy(token => token.Position)
                            .Select(token => new EvidentiaGoldTarget(
                                token.Position, token.Surface, token.StrongNumber, token.Lemma, token.Gloss))],
                        proposal.Trace?.Tier ?? string.Empty,
                        proposal.Trace?.Rationale ?? string.Empty,
                        proposal.Confidence,
                        string.Join(" ", sourceByVerse.GetValueOrDefault(word.Address.Verse, [])
                            .Select(token => token.Position == word.Position ? $"[{token.Surface}]" : token.Surface)));
                }),
        ];
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
    public int GoldCoveredSourceWords => Chapters.Sum(chapter => chapter.GoldCoveredSourceWords);
    public int GoldCoveredContentSourceWords => Chapters.Sum(chapter => chapter.GoldCoveredContentSourceWords);
    public EvidentiaTierScore StrongResolved => EvidentiaTierScore.Total(Chapters.Select(chapter => chapter.StrongResolved));
    public EvidentiaTierScore GlobalKnownRendering => EvidentiaTierScore.Total(Chapters.Select(chapter => chapter.GlobalKnownRendering));
    public EvidentiaTierScore GlobalReviewKnownRendering => EvidentiaTierScore.Total(Chapters.Select(chapter => chapter.GlobalReviewKnownRendering));
    public EvidentiaTierScore GlobalReviewAndSyntaxTargetGloss => EvidentiaTierScore.Total(Chapters.Select(chapter => chapter.GlobalReviewAndSyntaxTargetGloss));
    public EvidentiaTierScore AttachedWords => EvidentiaTierScore.Total(Chapters.Select(chapter => chapter.AttachedWords));
    public EvidentiaTierScore WithAttachedWords => EvidentiaTierScore.Total(Chapters.Select(chapter => chapter.WithAttachedWords));
    public int FallbackVerses => Chapters.Sum(chapter => chapter.FallbackVerses);
    public int ContentSourceWords => Chapters.Sum(chapter => chapter.ContentSourceWords);
    public int FinalProposedSourceWords => Chapters.Sum(chapter => chapter.FinalProposedSourceWords);
    public IReadOnlyList<EvidentiaDisagreement> Disagreements =>
        [.. Chapters.SelectMany(chapter => chapter.Disagreements)];

    public EvidentiaSourceWordAccount WordAccount => Chapters
        .Select(chapter => chapter.WordAccount)
        .Aggregate(default(EvidentiaSourceWordAccount), (running, next) => running + next);

    public IReadOnlyList<EvidentiaWordRecord> Words => [.. Chapters.SelectMany(chapter => chapter.Words)];

    /// <summary>
    /// The book's distinct content source forms, and how many of them the learned index holds an
    /// entry for. Unioned rather than summed: a form standing in four chapters is one word the
    /// index either knows or does not.
    /// </summary>
    public IReadOnlySet<string> IndexAskedForms =>
        Chapters.SelectMany(chapter => chapter.IndexAskedForms).ToHashSet(StringComparer.Ordinal);

    public IReadOnlySet<string> IndexAnsweredForms =>
        Chapters.SelectMany(chapter => chapter.IndexAnsweredForms).ToHashSet(StringComparer.Ordinal);

    public double Abstention => ContentSourceWords == 0
        ? 0
        : 1 - (double)FinalProposedSourceWords / ContentSourceWords;
    public double CandidateRecall => GoldPairs == 0 ? 0 : (double)GoldPairsInCandidates / GoldPairs;
    public double GoldSourceCoverage => SourceWords == 0 ? 0 : (double)GoldCoveredSourceWords / SourceWords;
    public double GoldContentCoverage => ContentSourceWords == 0
        ? 0
        : (double)GoldCoveredContentSourceWords / ContentSourceWords;

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
               $"gold coverage: {GoldCoveredSourceWords:N0}/{SourceWords:N0} source words ({GoldSourceCoverage:P2}), " +
               $"{GoldCoveredContentSourceWords:N0}/{ContentSourceWords:N0} content words ({GoldContentCoverage:P2})\n" +
               StrongResolved.Report("resolved source Strong", GoldPairs) + "\n" +
               GlobalKnownRendering.Report("global stable", GoldPairs) + "\n" +
               GlobalReviewKnownRendering.Report("global review", GoldPairs) + "\n" +
               GlobalReviewAndSyntaxTargetGloss.Report("global review + syntax-gated target gloss", GoldPairs) + "\n" +
               AttachedWords.Report("attached grammatical words", GoldPairs) + "\n" +
               WithAttachedWords.Report("global review + syntax-gated target gloss + attached grammatical words", GoldPairs) + "\n" +
               $"learned index reach: {IndexAnsweredForms.Count:N0}/{IndexAskedForms.Count:N0} " +
               "distinct content source forms have an entry\n" +
               $"final abstention: {ContentSourceWords - FinalProposedSourceWords:N0}/{ContentSourceWords:N0} " +
               $"content source words unplaced ({Abstention:P2})\n" +
               WordAccount.Report() +
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
        var proven = Result.Candidates.Count(candidate =>
            candidate.Score >= EvidentiaDefaults.SupportedCandidateScore);
        var sample = Result.Candidates
            .Take(8)
            .Select(candidate =>
                $"{candidate.Source.Token.Address.Verse}:{candidate.Source.Token.Position} '{candidate.Source.Token.Surface}' " +
                $"→ {candidate.Target.Token.Address.Verse}:{candidate.Target.Token.Position} '{candidate.Target.Token.Surface}' " +
                $"{candidate.Score:P0} [{string.Join(", ", candidate.Evidence.Select(evidence => evidence.Kind))}]");

        return $"EVIDENTIA preview {From} → {To}; canonical {CanonicalBook}:{CanonicalChapter}" +
               (CanonicalVerse.HasValue ? $":{CanonicalVerse.Value}" : string.Empty) + "\n" +
               $"words: source {SourceWordCount:N0}, target chapter {TargetWordCount:N0}; " +
               $"candidates: {Result.Candidates.Count:N0}; " +
               $"supported candidates (≥{EvidentiaDefaults.SupportedCandidateScore:P0}): {proven:N0}\n" +
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
    int GoldCoveredSourceWords,
    int GoldCoveredContentSourceWords,
    EvidentiaTierScore StrongResolved,
    int StrongUnresolvedSourceWords,
    EvidentiaTierScore KnownRendering,
    EvidentiaTierScore GlobalKnownRendering,
    EvidentiaTierScore ReviewKnownRendering,
    EvidentiaTierScore GlobalReviewKnownRendering,
    EvidentiaTierScore TargetGlossReview,
    EvidentiaTierScore SyntaxTargetGlossReview,
    EvidentiaTierScore AttachedWords,
    EvidentiaTierScore Unambiguous,
    int FinalProposedSourceWords,
    IReadOnlySet<string> IndexAskedForms,
    IReadOnlySet<string> IndexAnsweredForms,
    IReadOnlyList<string> Samples,
    IReadOnlyList<EvidentiaDisagreement> Disagreements,
    EvidentiaSourceWordAccount WordAccount,
    IReadOnlyList<EvidentiaWordRecord> Words)
{
    public double SourceCoverage => SourceWords == 0 ? 0 : (double)CoveredSourceWords / SourceWords;
    public double ContentCoverage => ContentSourceWords == 0
        ? 0
        : (double)CoveredContentSourceWords / ContentSourceWords;
    public double CandidateRecall => GoldPairs == 0 ? 0 : (double)GoldPairsInCandidates / GoldPairs;
    public EvidentiaTierScore GlobalReviewAndTargetGloss => GlobalReviewKnownRendering + TargetGlossReview;
    public EvidentiaTierScore GlobalReviewAndSyntaxTargetGloss => GlobalReviewKnownRendering + SyntaxTargetGlossReview;
    public EvidentiaTierScore WithAttachedWords => GlobalReviewAndSyntaxTargetGloss + AttachedWords;

    /// <summary>
    /// How much of the chapter the answer key reaches at all. Every precision figure is a statement
    /// about these words and about no others, so it is reported beside them rather than inferred
    /// from the pair count: two golds with the same number of pairs can cover very different
    /// shares of the text.
    /// </summary>
    public double GoldSourceCoverage => SourceWords == 0 ? 0 : (double)GoldCoveredSourceWords / SourceWords;

    public double GoldContentCoverage => ContentSourceWords == 0
        ? 0
        : (double)GoldCoveredContentSourceWords / ContentSourceWords;

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
              $"resolved Strong proposals: {StrongResolved.Proposals:N0}; not accepted automatically\n" +
              $"single-candidate graph proposals: {Unambiguous.Proposals:N0}; not evaluated without gold"
            : $"stored Strong/source pairs in scope: {GoldPairs:N0}; candidate hits: {GoldPairsInCandidates:N0}/{GoldPairs:N0} ({CandidateRecall:P1})\n" +
              $"gold coverage: {GoldCoveredSourceWords:N0}/{SourceWords:N0} source words ({GoldSourceCoverage:P1}), " +
              $"{GoldCoveredContentSourceWords:N0}/{ContentSourceWords:N0} content words ({GoldContentCoverage:P1})\n" +
              StrongResolved.Report("resolved Strong", GoldPairs) +
              $"; unresolved repeated/mismatched Strong words: {StrongUnresolvedSourceWords:N0}\n" +
              KnownRendering.Report("stable learned-rendering", GoldPairs) + "\n" +
              GlobalKnownRendering.Report("global stable learned-rendering", GoldPairs) + "\n" +
              ReviewKnownRendering.Report("review learned-rendering", GoldPairs) + "\n" +
              GlobalReviewKnownRendering.Report("global review learned-rendering", GoldPairs) + "\n" +
              GlobalReviewAndTargetGloss.Report("global review + target gloss", GoldPairs) + "\n" +
              GlobalReviewAndSyntaxTargetGloss.Report("global review + syntax-gated target gloss", GoldPairs) + "\n" +
              AttachedWords.Report("attached grammatical words", GoldPairs) + "\n" +
              WithAttachedWords.Report("global review + syntax-gated target gloss + attached grammatical words", GoldPairs) + "\n" +
              Unambiguous.Report("single-candidate graph", GoldPairs);

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
               $"\nlearned index reach: {IndexAnsweredForms.Count:N0}/{IndexAskedForms.Count:N0} " +
               "distinct content source forms have an entry" +
               $"\nfinal abstention: {ContentSourceWords - FinalProposedSourceWords:N0}/{ContentSourceWords:N0} " +
               $"content source words unplaced ({Abstention:P1})\n" +
               WordAccount.Report() +
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
/// <param name="RecordDisagreements">
/// Keep every contradicted proposal, so a classification pass reads the same rows the aggregate
/// counted rather than a second run's.
/// </param>
/// <param name="RecordWords">
/// Keep every source word with its outcome, so two runs can be compared word by word.
/// </param>
/// <param name="Decisions">
/// Handed each chapter's candidates and proposals as the measurement made them, so a stored run
/// keeps exactly what was scored rather than a second selection that could drift from it.
/// </param>
internal sealed record EvidentiaMeasurementOptions(
    bool AllowSourceStrongEvidence = true,
    bool AllowKnownRenderingEvidence = true,
    string? LearnRenderingsFrom = null,
    IReadOnlyList<LinkMethod>? LearnedRenderingMethods = null,
    string? GoldSource = null,
    int SampleSize = 0,
    bool RecordDisagreements = false,
    bool RecordWords = false,
    IEvidentiaDecisionSink? Decisions = null);

/// <summary>
/// One tier's proposals scored two ways, because the answer key does not reach every word.
/// <see cref="Precision"/> counts every proposal, which is the rule every figure published before
/// 2026-09-16 was measured under: a proposal on a word no annotator ever looked at counted as a
/// mistake. <see cref="CoveredPrecision"/> counts only the proposals standing on a word the gold
/// does reach, which is the only question the gold can answer. Both are reported, labelled, so
/// neither replaces the other silently.
/// </summary>
internal readonly record struct EvidentiaTierScore(int Proposals, int Correct, int OnCoveredWords)
{
    public int Unscored => Proposals - OnCoveredWords;
    public double Precision => Proposals == 0 ? 0 : (double)Correct / Proposals;
    public double CoveredPrecision => OnCoveredWords == 0 ? 0 : (double)Correct / OnCoveredWords;
    public double Recall(int goldPairs) => goldPairs == 0 ? 0 : (double)Correct / goldPairs;

    public static EvidentiaTierScore Of(
        IEnumerable<EvidentiaProposal> proposals,
        IReadOnlySet<(long From, long To)> gold,
        IReadOnlySet<long> covered)
    {
        var all = proposals as IReadOnlyCollection<EvidentiaProposal> ?? [.. proposals];
        return new EvidentiaTierScore(
            all.Count,
            all.Count(proposal => gold.Contains((proposal.Source.Token.Id, proposal.Target.Token.Id))),
            all.Count(proposal => covered.Contains(proposal.Source.Token.Id)));
    }

    public static EvidentiaTierScore operator +(EvidentiaTierScore one, EvidentiaTierScore two) =>
        new(one.Proposals + two.Proposals, one.Correct + two.Correct, one.OnCoveredWords + two.OnCoveredWords);

    public static EvidentiaTierScore Total(IEnumerable<EvidentiaTierScore> scores) =>
        scores.Aggregate(new EvidentiaTierScore(0, 0, 0), (running, next) => running + next);

    public string Report(string name, int goldPairs) =>
        $"{name}: {Correct:N0}/{Proposals:N0} ({Precision:P2}) counting every proposal; " +
        $"{Correct:N0}/{OnCoveredWords:N0} ({CoveredPrecision:P2}) over gold-covered words " +
        $"({Unscored:N0} unscored); gold recall: {Correct:N0}/{goldPairs:N0} ({Recall(goldPairs):P2})";
}

internal sealed record EvidentiaGoldLink(
    long Id,
    LinkMethod Method,
    string Source,
    IReadOnlyList<long> SourceWords,
    IReadOnlyList<long> TargetWords);

internal sealed record EvidentiaGold(
    IReadOnlySet<(long From, long To)> Pairs,
    IReadOnlySet<long> CoveredSourceWords,
    IReadOnlyDictionary<long, List<EvidentiaGoldLink>> LinksBySourceWord);

internal sealed record EvidentiaGoldTarget(
    int Position,
    string Surface,
    string? StrongNumber,
    string? Lemma,
    string? Gloss);

internal sealed record EvidentiaDisagreement(
    int CanonicalBook,
    int CanonicalChapter,
    int CanonicalVerse,
    long SourceWordId,
    int SourcePosition,
    string SourceSurface,
    long TargetWordId,
    int TargetPosition,
    string TargetSurface,
    string? TargetStrongNumber,
    string? TargetLemma,
    string? TargetGloss,
    bool GoldCoversSourceWord,
    IReadOnlyList<string> GoldLinkShapes,
    IReadOnlyList<string> GoldSources,
    IReadOnlyList<EvidentiaGoldTarget> GoldTargets,
    string Tier,
    string Rationale,
    double Confidence,
    string SourceVerse);
