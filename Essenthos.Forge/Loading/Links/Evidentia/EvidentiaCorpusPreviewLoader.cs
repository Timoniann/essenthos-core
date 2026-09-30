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
    LanguagePackRegistry languagePacks,
    InterlinearLinkLoader interlinear,
    EvidentiaFileSourceTexts fileSources,
    EvidentiaContextGlossIndex contextGlossIndex)
{
    private const string InterlinearGoldSource = "Door43 interlinear, joined in memory";

    private readonly Dictionary<string, SyntaxPrior> syntaxByTargetText = new(StringComparer.Ordinal);
    private readonly Dictionary<(string From, string To, int Book), EvidentiaChapterLengths> chapterLengths = [];
    private IReadOnlyDictionary<string, string>? greekGlosses;
    private IReadOnlyList<(IReadOnlyList<long> From, IReadOnlyList<long> To)>? interlinearPairs;
    private (string Path, Dictionary<(int Book, int Chapter, int Verse, int Position), HashSet<int>> Names)? consensusNames;
    private (EvidentiaConfirmedRenderings Renderings, string Language, EvidentiaConfirmedIndex Index)? confirmedIndex;

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
        var lengths = await ChapterLengths(fromSlug, toSlug, canonicalBook, cancellationToken);
        var source = EnglishCompounds.Join(sourceAnalysis.Tokens).Select(lengths.Place).ToList();
        var target = (await Tokens(toSlug, canonicalBook, canonicalChapter, null, cancellationToken)).Select(lengths.Place).ToList();
        var window = await Window(toSlug, canonicalBook, canonicalChapter, target, lengths, EvidentiaDefaults.NeighbourVerseDistance, cancellationToken);
        var dictionaryEvidence = await dictionarySenseIndex.For(source, cancellationToken);
        var targetGlossEvidence = TargetGlossEvidenceSource.For(window);
        var knownRenderingEvidence = allowKnownRenderingEvidence
            ? await knownRenderingIndex.For(
                fromSlug, toSlug, source, canonicalBook, canonicalChapter, null, cancellationToken: cancellationToken)
            : null;
        var preview = pipeline.Preview(
            new EvidentiaRequest(source, Near(window, source, EvidentiaDefaults.NeighbourVerseDistance), AllowSourceStrongEvidence: allowSourceStrongEvidence),
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
        var sourceAnalysis = options.SourceFromFiles
            ? await udpipe.Annotate(fileSources.Tokens(fromSlug, canonicalBook, canonicalChapter, null), cancellationToken)
            : await SourceTokens(fromSlug, canonicalBook, canonicalChapter, null, cancellationToken);
        var lengths = await ChapterLengths(fromSlug, toSlug, canonicalBook, cancellationToken);
        var source = EnglishCompounds.Join(sourceAnalysis.Tokens).Select(lengths.Place).ToList();
        var target = (await Tokens(toSlug, canonicalBook, canonicalChapter, null, cancellationToken)).Select(lengths.Place).ToList();
        var window = await Window(toSlug, canonicalBook, canonicalChapter, target, lengths, options.NeighbourVerseDistance, cancellationToken);
        var dictionaryEvidence = await dictionarySenseIndex.For(source, cancellationToken);
        var targetGlossEvidence = TargetGlossEvidenceSource.For(window);
        var knownRenderingEvidence = options.AllowKnownRenderingEvidence
            ? await knownRenderingIndex.For(
                options.LearnRenderingsFrom ?? fromSlug, toSlug, source,
                canonicalBook, canonicalChapter, options.LearnedRenderingMethods,
                fromSlug, options.LearnAcrossLanguages, cancellationToken)
            : null;
        var previews = source.GroupBy(token => token.Address)
            .OrderBy(group => group.Key.Verse)
            .Select(group => pipeline.Preview(
                new EvidentiaRequest(
                    group.ToList(),
                    Near(window, group, options.NeighbourVerseDistance),
                    options.NeighbourVerseDistance,
                    AllowSourceStrongEvidence: options.AllowSourceStrongEvidence),
                Evidence(dictionaryEvidence, knownRenderingEvidence, targetGlossEvidence)))
            .ToList();

        var candidates = previews.SelectMany(preview => preview.Candidates).ToList();
        List<EvidentiaAnalysis> sourceAnalyses = [.. source.Select(Analyse).OfType<EvidentiaAnalysis>()];
        List<EvidentiaAnalysis> targetAnalyses = [.. target.Select(Analyse).OfType<EvidentiaAnalysis>()];
        var entityAnchors = options.EntityAnchors
            ? EvidentiaEntityAnchors.Resolve(
                sourceAnalyses,
                targetAnalyses,
                await EntityNames(source, target, options.EntityNamesFrom, cancellationToken),
                options.NeighbourVerseDistance)
            : [];
        var resolution = strongProposalResolver.Resolve(candidates);
        var knownRenderingResolution = knownRenderingProposalResolver.Resolve(candidates);
        var reviewKnownRenderingResolution = knownRenderingProposalResolver.Resolve(
            candidates, EvidentiaKnownRenderingProposalResolver.Review);
        var globalKnownRenderingResolution = knownRenderingProposalResolver.ResolveGlobally(
            candidates, EvidentiaKnownRenderingProposalResolver.Safe);
        var globalReviewKnownRenderingResolution = knownRenderingProposalResolver.ResolveGlobally(candidates);
        var frame = EvidentiaVerseFrame.Of(sourceAnalyses, targetAnalyses, globalReviewKnownRenderingResolution.Proposals);
        var dictionaryReviewResolution = frame.Near(
            dictionaryProposalResolver.ResolveAdditional(candidates, globalReviewKnownRenderingResolution.Proposals));
        var targetGlossOnlyResolution = frame.Near(targetGlossProposalResolver.ResolveAdditional(
            candidates, globalReviewKnownRenderingResolution.Proposals.Concat(dictionaryReviewResolution.Proposals)));
        var targetSyntax = await Syntax(toSlug, cancellationToken);
        var syntaxEligibleTargetGloss = syntaxReviewGate.ClauseCohesiveTargetGlossCandidates(
            candidates, globalKnownRenderingResolution.Proposals, targetSyntax);
        var syntaxTargetGlossOnlyResolution = frame.Near(targetGlossProposalResolver.ResolveAdditional(
            candidates, globalReviewKnownRenderingResolution.Proposals.Concat(dictionaryReviewResolution.Proposals), syntaxEligibleTargetGloss));
        var targetGlossReviewResolution = new EvidentiaResolution(
            dictionaryReviewResolution.Proposals.Concat(targetGlossOnlyResolution.Proposals).ToList(), 0);
        var residualKnownRenderingResolution = frame.Near(knownRenderingProposalResolver.ResolveResidual(
            candidates,
            globalReviewKnownRenderingResolution.Proposals
                .Concat(dictionaryReviewResolution.Proposals)
                .Concat(syntaxTargetGlossOnlyResolution.Proposals)));
        var anchoredGapResolution = EvidentiaAnchoredGap.Resolve(
            sourceAnalyses,
            targetAnalyses,
            candidates,
            [
                .. globalReviewKnownRenderingResolution.Proposals,
                .. dictionaryReviewResolution.Proposals,
                .. syntaxTargetGlossOnlyResolution.Proposals,
                .. residualKnownRenderingResolution.Proposals,
            ]);
        var classMatchedTargetGlossResolution = frame.Near(targetGlossProposalResolver.ResolveAdditional(
            candidates,
            [
                .. globalReviewKnownRenderingResolution.Proposals,
                .. dictionaryReviewResolution.Proposals,
                .. syntaxTargetGlossOnlyResolution.Proposals,
                .. residualKnownRenderingResolution.Proposals,
                .. anchoredGapResolution.Proposals,
            ],
            candidates
                .Where(candidate => EvidentiaMorphologyLabels.AreCounterparts(candidate.Source, candidate.Target))
                .Select(candidate => (candidate.Source.Token.Id, candidate.Target.Token.Id))
                .ToHashSet()));
        var repeatedRenderingResolution = EvidentiaRepeatedRendering.Resolve(
            targetAnalyses,
            candidates,
            [
                .. globalReviewKnownRenderingResolution.Proposals,
                .. dictionaryReviewResolution.Proposals,
                .. syntaxTargetGlossOnlyResolution.Proposals,
                .. residualKnownRenderingResolution.Proposals,
                .. anchoredGapResolution.Proposals,
                .. classMatchedTargetGlossResolution.Proposals,
            ]);
        var contextGlossProposals = EvidentiaContextGloss.Resolve(
            sourceAnalyses,
            targetAnalyses,
            contextGlossIndex.For(target, await GreekGlosses(cancellationToken)),
            [
                .. globalReviewKnownRenderingResolution.Proposals,
                .. dictionaryReviewResolution.Proposals,
                .. syntaxTargetGlossOnlyResolution.Proposals,
                .. residualKnownRenderingResolution.Proposals,
                .. anchoredGapResolution.Proposals,
                .. classMatchedTargetGlossResolution.Proposals,
                .. repeatedRenderingResolution.Proposals,
            ]);
        var dictionaryAndGlossProposals = frame.Near(EvidentiaCounterparts.DictionaryAndGloss(
            candidates,
            [
                .. globalReviewKnownRenderingResolution.Proposals,
                .. dictionaryReviewResolution.Proposals,
                .. syntaxTargetGlossOnlyResolution.Proposals,
                .. residualKnownRenderingResolution.Proposals,
                .. anchoredGapResolution.Proposals,
                .. classMatchedTargetGlossResolution.Proposals,
                .. repeatedRenderingResolution.Proposals,
                .. contextGlossProposals,
            ]));
        var syntaxTargetGlossReviewResolution = new EvidentiaResolution(
            dictionaryReviewResolution.Proposals
                .Concat(syntaxTargetGlossOnlyResolution.Proposals)
                .Concat(residualKnownRenderingResolution.Proposals)
                .Concat(anchoredGapResolution.Proposals)
                .Concat(classMatchedTargetGlossResolution.Proposals)
                .Concat(repeatedRenderingResolution.Proposals)
                .Concat(contextGlossProposals)
                .Concat(dictionaryAndGlossProposals)
                .ToList(), 0);
        var sourceIds = source.Select(token => token.Id).ToHashSet();
        var targetIds = target.Select(token => token.Id).ToHashSet();
        // A text read from its files has no link in the corpus, so it has no answer key of its own.
        var goldAnnotation = options.SourceFromFiles
            ? Annotated([], sourceIds, targetIds)
            : options.GoldInterlinear is { } interlinearFolder
                ? await InterlinearGold(fromSlug, interlinearFolder, sourceIds, targetIds, cancellationToken)
                : await Gold(fromSlug, toSlug, sourceIds, targetIds, options.GoldSource, cancellationToken);
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
        // After every lexical tier and only on the words it leaves free: the tiers already place most
        // names rightly, and a name placed ahead of them only takes those words out of the safe tier.
        var placed = lexicalProposals.Select(proposal => proposal.Source.Token.Id).ToHashSet();
        var taken = lexicalProposals.Select(proposal => proposal.Target.Token.Id).ToHashSet();
        List<EvidentiaProposal> named = [.. entityAnchors.Where(anchor =>
            !placed.Contains(anchor.Source.Token.Id) && !taken.Contains(anchor.Target.Token.Id))];
        lexicalProposals.AddRange(named);
        var aligner = options.AlignerPairs
            ?? (options.AlignerLinks ? await AlignerPairs(fromSlug, toSlug, sourceIds, cancellationToken) : null);
        // A word whose placement was withheld stays unplaced: two lexical readings and the aligner on one of
        // them is not the agreement the fill asks for.
        HashSet<long> withheld = [];
        if (aligner is not null
            && EvidentiaAlignerCheck.Contested(lexicalProposals, candidates, aligner) is { Count: > 0 } contested)
        {
            withheld.UnionWith(contested.Select(pair => pair.From));
            EvidentiaResolution Uncontested(EvidentiaResolution resolution) => resolution with
            {
                Proposals = [.. resolution.Proposals.Where(proposal =>
                    !contested.Contains((proposal.Source.Token.Id, proposal.Target.Token.Id)))],
            };
            globalKnownRenderingResolution = Uncontested(globalKnownRenderingResolution);
            globalReviewKnownRenderingResolution = Uncontested(globalReviewKnownRenderingResolution);
            syntaxTargetGlossReviewResolution = Uncontested(syntaxTargetGlossReviewResolution);
            lexicalProposals.RemoveAll(proposal => contested.Contains((proposal.Source.Token.Id, proposal.Target.Token.Id)));
        }

        // Last of the lexical tiers, on the words they leave free, and before the grammatical words, which
        // then find their head placed.
        List<EvidentiaProposal> filled = [];
        if (options.Confirmed is { } renderings && source.FirstOrDefault()?.Language is { } sourceLanguage)
        {
            filled.AddRange(EvidentiaSecondPass.Resolve(
                    sourceAnalyses, targetAnalyses, lexicalProposals, ConfirmedIndex(renderings, sourceLanguage), frame, aligner)
                .Where(proposal => !withheld.Contains(proposal.Source.Token.Id)));
        }

        if (aligner is not null)
        {
            filled.AddRange(EvidentiaAlignerCheck.Resolve(candidates, [.. lexicalProposals, .. filled], aligner, frame)
                .Where(proposal => !withheld.Contains(proposal.Source.Token.Id)));
        }

        syntaxTargetGlossReviewResolution = syntaxTargetGlossReviewResolution with
        {
            Proposals = [.. syntaxTargetGlossReviewResolution.Proposals, .. named, .. filled],
        };
        lexicalProposals.AddRange(filled);
        var markedSource = EvidentiaAuxiliaryWords.Mark(sourceAnalyses);
        var attachedWords = EvidentiaAttachedWords.Resolve(markedSource, targetAnalyses, lexicalProposals);
        List<EvidentiaProposal> finalProposals = [.. lexicalProposals, .. attachedWords];
        finalProposals.AddRange(frame.Near(EvidentiaCounterparts.Resolve(sourceAnalyses, targetAnalyses, finalProposals)));

        var absences = EvidentiaAbsences.Resolve(sourceAnalyses, targetAnalyses, finalProposals);
        var byWord = EvidentiaWordScore.Of(sourceAnalyses, targetAnalyses, finalProposals, absences, goldAnnotation, out var absenceVerdicts);
        // A placement the aligner names too is right more often than the safe tier's own, and one whose word
        // the aligner links elsewhere is right less often than the review tier's. Not the words the aligner's
        // pair itself placed: there it is the evidence, not a second opinion.
        List<EvidentiaProposal> safeLexical =
        [
            .. globalKnownRenderingResolution.Proposals.Where(proposal =>
                aligner?.Contradicts(proposal.Source.Token.Id, proposal.Target.Token.Id) != true),
            .. named,
        ];
        var demoted = globalKnownRenderingResolution.Proposals.Count + named.Count - safeLexical.Count;
        var alreadySafe = safeLexical.Select(proposal => (proposal.Source.Token.Id, proposal.Target.Token.Id)).ToHashSet();
        List<EvidentiaProposal> promoted = aligner is null
            ? []
            : [.. lexicalProposals.Except(filled).Where(proposal =>
                aligner.Agrees(proposal.Source.Token.Id, proposal.Target.Token.Id)
                && !alreadySafe.Contains((proposal.Source.Token.Id, proposal.Target.Token.Id)))];
        safeLexical.AddRange(promoted);
        var safe = EvidentiaAttachedWords.Safe(safeLexical, attachedWords);
        List<EvidentiaProposal> safeProposals =
            [.. finalProposals.Where(proposal => safe.Contains((proposal.Source.Token.Id, proposal.Target.Token.Id)))];
        options.Learns?.Learn(lexicalProposals, safe);
        var splitKey = EvidentiaKeySplit.Of(goldAnnotation, sourceAnalyses, targetAnalyses);
        var acceptedOnSplitKey = EvidentiaWordScore.Of(
            sourceAnalyses, targetAnalyses, finalProposals, absences, goldAnnotation with { Pairs = splitKey.Pairs }, out _).Accepted
            ?? new HashSet<(long From, long To)>();
        var states = EvidentiaStateScore.Of(
            markedSource, finalProposals, absences, absenceVerdicts, goldAnnotation, splitKey, acceptedOnSplitKey, safe, out var wordStates);
        var words = EvidentiaSourceWordAccount.Classify(
            source,
            Analyse,
            candidates,
            finalProposals,
            knownRenderingProposalResolver.Admitted(candidates, EvidentiaKnownRenderingProposalResolver.Review),
            gold,
            covered,
            canonicalBook,
            canonicalChapter,
            safe,
            options.RecordWords ? target : null);
        var suppliedWords = absences.Where(absence => absence.Kind == EvidentiaAbsenceKind.Supplied)
            .ToDictionary(absence => absence.Word.Token.Id);
        words = [.. words.Select(word => suppliedWords.TryGetValue(word.SourceWordId, out var absence)
            ? word with { Absence = absence.Rationale, AbsenceCorrect = absenceVerdicts.GetValueOrDefault(word.SourceWordId) }
            : word.TargetWordId is { } placed && goldAnnotation.CoveredSourceWords.Contains(word.SourceWordId)
                ? word with { CorrectByWord = byWord.Accepted?.Contains((word.SourceWordId, placed)) }
                : word)];
        words = [.. words.Select(word => wordStates.TryGetValue(word.SourceWordId, out var state)
            ? word with
            {
                State = EvidentiaStateMeasure.Name(state.State),
                Rule = state.Rule,
                Grammatical = state.Grammatical,
                HeadWordId = state.HeadWordId,
                HeadState = state.HeadState?.ToString(),
                CorrectOnSplitKey = state.State == EvidentiaWordState.Supplied ? null : state.Right,
                SplitKey = options.RecordWords ? state.KeyTargets : null,
            }
            : word)];
        var routes = new List<EvidentiaRouteAgreement>();
        var routeVerdicts = new Dictionary<(long From, long To), List<string>>();
        foreach (var route in options.RouteTexts ?? [])
        {
            routes.Add(await RouteAgreement(
                route, toSlug, canonicalBook, canonicalChapter, finalProposals, target, routeVerdicts, cancellationToken));
        }

        options.Decisions?.Record(new EvidentiaChapterDecisions(
            canonicalBook, canonicalChapter, source, contentSourceWordIds, candidates,
            safeProposals, finalProposals, absences));
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
            EvidentiaTierScore.Of(safeProposals, gold, covered),
            new EvidentiaTierScore(
                unambiguous.Count,
                unambiguous.Intersect(gold).Count(),
                unambiguous.Count(pair => covered.Contains(pair.From))),
            lexicalProposals.Select(proposal => proposal.Source.Token.Id).Distinct().Count(),
            knownRenderingEvidence?.AskedForms ?? new HashSet<string>(StringComparer.Ordinal),
            knownRenderingEvidence?.AnsweredForms ?? new HashSet<string>(StringComparer.Ordinal),
            Samples(finalProposals, candidates, source, target, gold, options.SampleSize),
            options.RecordDisagreements
                ? Disagreements(finalProposals, source, target, goldAnnotation, canonicalBook, canonicalChapter, routeVerdicts)
                : [],
            EvidentiaSourceWordAccount.Of(words),
            options.RecordWords ? words : [],
            byWord,
            options.RecordWords
                ? [.. absences.Select(absence => new EvidentiaAbsenceRecord(
                    canonicalBook,
                    canonicalChapter,
                    absence.Word.Token.Address.Verse,
                    absence.Word.Token.Id,
                    absence.Word.Token.Position,
                    absence.Word.Token.Surface,
                    absence.Word.Token.StrongNumber,
                    absence.Kind.ToString(),
                    absence.Rationale,
                    absence.Anchor is { } anchor ? $"{anchor.Source.Token.Surface} → {anchor.Target.Token.Surface}" : null,
                    absenceVerdicts.GetValueOrDefault(absence.Word.Token.Id)))]
                : [])
        {
            Routes = routes,
            States = states,
            KeyDoubts = options.RecordKeyDoubts
                ? EvidentiaKeyDoubts.Of(goldAnnotation, splitKey, sourceAnalyses, targetAnalyses, finalProposals, acceptedOnSplitKey)
                : [],
            SecondPass = new EvidentiaSecondPassAccount(
                filled.Count(proposal => proposal.Kind == EvidentiaProposalKind.ConfirmedRendering),
                filled.Count(proposal => proposal.Kind == EvidentiaProposalKind.AlignerAndLexicalEvidence),
                withheld.Count,
                promoted.Count,
                demoted),
        };
    }

    /// <summary>
    /// How far the links proposed for a text agree with the ones another English text's own links
    /// imply. A word of the measured text and a word of the route text in the same verse that share a
    /// learned-rendering key — the same lemma, as the language pack reads it — are taken to render the
    /// same thing, so the original words the route text's own links — stated, or by the Strong number
    /// it prints — put that word on are where the route says the measured word goes. A link is compared only where the route says something;
    /// that is the whole of what it can check, and <see cref="EvidentiaRouteAgreement.Compared"/>
    /// says how much that is.
    /// </summary>
    private async Task<EvidentiaRouteAgreement> RouteAgreement(
        string route,
        string toSlug,
        int canonicalBook,
        int canonicalChapter,
        IReadOnlyList<EvidentiaProposal> proposals,
        IReadOnlyList<EvidentiaToken> target,
        Dictionary<(long From, long To), List<string>> verdicts,
        CancellationToken cancellationToken)
    {
        var routeTokens = (await SourceTokens(route, canonicalBook, canonicalChapter, null, cancellationToken)).Tokens;
        var targetById = target.DistinctBy(token => token.Id).ToDictionary(token => token.Id);
        var routeGold = await Gold(
            route, toSlug, routeTokens.Select(token => token.Id).ToHashSet(), targetById.Keys.ToHashSet(), null,
            cancellationToken);
        var linkedTo = routeGold.Pairs
            .GroupBy(pair => pair.From)
            .ToDictionary(group => group.Key, group => group.Select(pair => pair.To).ToHashSet());
        var targetsByKey = new Dictionary<(int Verse, RenderingKey Key), HashSet<long>>();
        foreach (var token in routeTokens)
        {
            if (!linkedTo.TryGetValue(token.Id, out var targets) || Analyse(token) is not { IsContentWord: true } analysis)
            {
                continue;
            }

            foreach (var key in RenderingKeys.Of(analysis))
            {
                if (!targetsByKey.TryGetValue((token.Address.Verse, key), out var known))
                {
                    targetsByKey[(token.Address.Verse, key)] = known = [];
                }

                known.UnionWith(targets);
            }
        }

        var compared = 0;
        var agreed = 0;
        foreach (var proposal in proposals)
        {
            var word = proposal.Source.Token;
            var said = RenderingKeys.Of(proposal.Source)
                .SelectMany(key => targetsByKey.GetValueOrDefault((word.Address.Verse, key)) ?? [])
                .ToHashSet();
            if (said.Count == 0)
            {
                continue;
            }

            compared++;
            var pair = (word.Id, proposal.Target.Token.Id);
            if (!verdicts.TryGetValue(pair, out var verdict))
            {
                verdicts[pair] = verdict = [];
            }

            if (said.Contains(proposal.Target.Token.Id))
            {
                agreed++;
                verdict.Add($"{route} agrees");
            }
            else
            {
                verdict.Add($"{route} puts it on " + string.Join(" ", said
                    .Where(targetById.ContainsKey)
                    .Select(id => targetById[id])
                    .OrderBy(token => token.Address.Verse)
                    .ThenBy(token => token.Position)
                    .Select(token => token.Surface)));
            }
        }

        return new EvidentiaRouteAgreement(route, proposals.Count, compared, agreed);
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
        var chapters = options.SourceFromFiles
            ? [.. fileSources.Chapters(fromSlug, canonicalBook, firstChapter, lastChapter)]
            : await db.VerseReferences.AsNoTracking()
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

    /// <summary>The pairs the statistical aligner's stored links hold for the passage's words.</summary>
    private async Task<EvidentiaAlignerPairs> AlignerPairs(
        string fromSlug, string toSlug, IReadOnlySet<long> sourceIds, CancellationToken cancellationToken)
    {
        var texts = await db.Texts.AsNoTracking()
            .Where(text => text.Slug == fromSlug || text.Slug == toSlug)
            .ToDictionaryAsync(text => text.Slug, text => text.Id, cancellationToken);
        var from = texts[fromSlug];
        var to = texts[toSlug];
        var scope = sourceIds.ToList();
        var inScope = db.LinkWords.Where(word => scope.Contains(word.WordId)).Select(word => word.LinkId);
        var words = await db.Links.AsNoTracking()
            .Where(link => inScope.Contains(link.Id) && link.Method == LinkMethod.Aligner)
            .Where(link => (link.FromTextId == from && link.ToTextId == to) || (link.FromTextId == to && link.ToTextId == from))
            .SelectMany(link => link.Words.Select(word => new { link.Id, word.WordId }))
            .ToListAsync(cancellationToken);
        return EvidentiaAlignerPairs.Of(words.GroupBy(word => word.Id).SelectMany(link =>
            link.Where(word => sourceIds.Contains(word.WordId)).SelectMany(source =>
                link.Where(word => !sourceIds.Contains(word.WordId)).Select(target => (source.WordId, target.WordId)))));
    }

    private EvidentiaConfirmedIndex ConfirmedIndex(EvidentiaConfirmedRenderings renderings, string language)
    {
        if (confirmedIndex is not { } known || !ReferenceEquals(known.Renderings, renderings) || known.Language != language)
        {
            confirmedIndex = known = (renderings, language, renderings.For(language, languagePacks));
        }

        return known.Index;
    }

    private EvidentiaAnalysis? Analyse(EvidentiaToken token) =>
        languagePacks.TryAnalyse(token, out var analysis) ? analysis : null;

    private async Task<IReadOnlyDictionary<string, string>> GreekGlosses(CancellationToken cancellationToken) =>
        greekGlosses ??= (await db.LexiconGlosses.AsNoTracking()
                .Where(gloss => gloss.StrongNumber.StartsWith("G"))
                .Select(gloss => new { gloss.Lemma, gloss.Gloss })
                .ToListAsync(cancellationToken))
            .GroupBy(gloss => gloss.Lemma, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => string.Join(" ", group.Select(gloss => gloss.Gloss)), StringComparer.Ordinal);

    /// <summary>
    /// The chapter's target words, and the words of the neighbouring chapters' edge verses, which
    /// only the candidate graph sees: they stand in the neighbour window of the chapter's first and
    /// last verses, and nothing is proposed on a word outside its own verse.
    /// </summary>
    private async Task<List<EvidentiaToken>> Window(
        string slug,
        int canonicalBook,
        int canonicalChapter,
        IReadOnlyList<EvidentiaToken> target,
        EvidentiaChapterLengths lengths,
        int neighbourVerseDistance,
        CancellationToken cancellationToken)
    {
        var window = target.ToList();
        var held = target.Select(token => token.Id).ToHashSet();
        foreach (var (chapter, verse) in lengths.Edges(canonicalChapter, neighbourVerseDistance))
        {
            window.AddRange((await Tokens(slug, canonicalBook, chapter, verse, cancellationToken, required: false))
                .Where(token => held.Add(token.Id))
                .Select(lengths.Place));
        }

        return window;
    }

    /// <summary>The window as some source verses see it: the words, of this chapter or a neighbouring one, that stand near one of them.</summary>
    private static List<EvidentiaToken> Near(
        IReadOnlyList<EvidentiaToken> window, IEnumerable<EvidentiaToken> source, int neighbourVerseDistance)
    {
        var addresses = source.Select(token => token.Address).Distinct().ToList();
        return
        [
            .. window.Where(token => addresses.Any(address => address.DistanceTo(token.Address) <= neighbourVerseDistance)),
        ];
    }

    private async Task<EvidentiaChapterLengths> ChapterLengths(
        string fromSlug, string toSlug, int canonicalBook, CancellationToken cancellationToken)
    {
        if (!chapterLengths.TryGetValue((fromSlug, toSlug, canonicalBook), out var lengths))
        {
            var chapters = await db.VerseReferences.AsNoTracking()
                .Where(reference => (reference.Verse!.Text!.Slug == fromSlug || reference.Verse.Text.Slug == toSlug)
                    && reference.CanonicalBook == canonicalBook)
                .GroupBy(reference => reference.CanonicalChapter)
                .Select(group => new { Chapter = group.Key, LastVerse = group.Max(reference => reference.CanonicalVerse) })
                .ToListAsync(cancellationToken);
            chapterLengths[(fromSlug, toSlug, canonicalBook)] = lengths =
                new EvidentiaChapterLengths(chapters.Select(chapter => (chapter.Chapter, chapter.LastVerse)));
        }

        return lengths;
    }

    /// <param name="required">Whether a scope with no words is an error, rather than a verse the text lacks.</param>
    private async Task<List<EvidentiaToken>> Tokens(
        string slug,
        int canonicalBook,
        int canonicalChapter,
        int? canonicalVerse,
        CancellationToken cancellationToken,
        bool required = true)
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

        if (rows.Count == 0 && !required)
        {
            return [];
        }

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
    /// <summary>
    /// Which entity each word of the passage names. The original's are its annotations. The
    /// translation's are its own annotations, or, with <paramref name="consensusFile"/>, the words
    /// file of a <c>name-consensus --out</c> reading, which addresses each word by its verse's first
    /// canonical address and its place in the verse, and knows nothing of the translation's links.
    /// </summary>
    private async Task<EvidentiaEntityNames> EntityNames(
        IReadOnlyList<EvidentiaToken> source,
        IReadOnlyList<EvidentiaToken> target,
        string? consensusFile,
        CancellationToken cancellationToken)
    {
        var targetIds = target.Select(token => token.Id).ToList();
        var targetNames = await Annotations(targetIds, cancellationToken);
        if (consensusFile is null)
        {
            return new EvidentiaEntityNames(await Annotations([.. source.Select(token => token.Id)], cancellationToken), targetNames);
        }

        var names = await ConsensusNames(consensusFile, cancellationToken);
        var sourceNames = new Dictionary<long, IReadOnlySet<int>>();
        await db.Database.OpenConnectionAsync(cancellationToken);
        try
        {
            await using var command = new NpgsqlCommand(
                """
                SELECT w.id, m.canonical_book, m.canonical_chapter, m.canonical_verse,
                       (SELECT count(*) FROM word x WHERE x.verse_id = w.verse_id AND x.position <= w.position)::int
                FROM word w
                CROSS JOIN LATERAL (
                    SELECT r.canonical_book, r.canonical_chapter, r.canonical_verse FROM verse_reference r
                    WHERE r.verse_id = w.verse_id
                    ORDER BY r.canonical_book, r.canonical_chapter, r.canonical_verse LIMIT 1) m
                WHERE w.id = ANY(@ids)
                """,
                (NpgsqlConnection)db.Database.GetDbConnection());
            command.Parameters.AddWithValue("ids", source.Select(token => token.Id).ToArray());
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                if (names.TryGetValue(
                        (reader.GetInt32(1), reader.GetInt32(2), reader.GetInt32(3), reader.GetInt32(4)), out var named))
                {
                    sourceNames[reader.GetInt64(0)] = named;
                }
            }
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }

        return new EvidentiaEntityNames(sourceNames, targetNames);
    }

    private async Task<Dictionary<long, IReadOnlySet<int>>> Annotations(
        IReadOnlyList<long> words, CancellationToken cancellationToken) =>
        (await db.WordEntities.AsNoTracking()
            .Where(annotation => words.Contains(annotation.WordId))
            .Select(annotation => new { annotation.WordId, annotation.EntityId })
            .ToListAsync(cancellationToken))
        .GroupBy(annotation => annotation.WordId)
        .ToDictionary(word => word.Key, word => (IReadOnlySet<int>)word.Select(annotation => annotation.EntityId).ToHashSet());

    private async Task<Dictionary<(int Book, int Chapter, int Verse, int Position), HashSet<int>>> ConsensusNames(
        string path, CancellationToken cancellationToken)
    {
        if (consensusNames is { } cached && cached.Path == path)
        {
            return cached.Names;
        }

        var slugs = await db.Entities.AsNoTracking()
            .ToDictionaryAsync(entity => entity.Slug, entity => entity.Id, StringComparer.Ordinal, cancellationToken);
        var names = new Dictionary<(int, int, int, int), HashSet<int>>();
        foreach (var line in (await File.ReadAllLinesAsync(path, cancellationToken)).Skip(1))
        {
            var fields = line.Split('	');
            var address = fields[0].Split(':');
            if (fields.Length < 4 || address.Length != 3 || !slugs.TryGetValue(fields[3], out var entity))
            {
                continue;
            }

            var key = (int.Parse(address[0]), int.Parse(address[1]), int.Parse(address[2]), int.Parse(fields[1]));
            if (!names.TryGetValue(key, out var named))
            {
                names[key] = named = [];
            }

            named.Add(entity);
        }

        consensusNames = (path, names);
        return names;
    }

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
        // The target's words as well: a word the translation does not render is named by a link with
        // nothing on the translation's side.
        var scopeWords = sourceIds.Concat(targetIds).ToList();
        var inScope = db.LinkWords.Where(word => scopeWords.Contains(word.WordId)).Select(word => word.LinkId);
        var scopedLinks = db.Links.AsNoTracking()
            .Where(link => inScope.Contains(link.Id))
            .Where(link => (link.FromTextId == from.Id && link.ToTextId == to.Id)
                || (link.FromTextId == to.Id && link.ToTextId == from.Id))
            .Where(link => link.Method == LinkMethod.StatedBySource
                || link.Method == LinkMethod.StrongNumber)
            .Where(link => goldSource == null || link.Source.Contains(goldSource));
        var described = await scopedLinks
            .Select(link => new { link.Id, link.FromTextId, link.Method, link.Source, link.Relation })
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
                    right,
                    link.Relation is LinkRelation.Omits or LinkRelation.Expands);
            })
            .ToList();
        return Annotated(goldLinks, sourceIds, targetIds);
    }

    /// <summary>
    /// The answer key made from a Door43 interlinear by the join as it stands, read into memory and
    /// written nowhere. The rows the corpus holds came from an earlier join that dropped every span
    /// whose original word stood before an earlier span's, so scoring against them scores mostly the
    /// stretches that do not reorder.
    /// </summary>
    private async Task<EvidentiaGold> InterlinearGold(
        string fromSlug,
        string folder,
        IReadOnlySet<long> sourceIds,
        IReadOnlySet<long> targetIds,
        CancellationToken cancellationToken)
    {
        interlinearPairs ??= await interlinear.Pairs(folder, fromSlug, cancellationToken);
        var links = interlinearPairs
            .Select((pair, index) => (Pair: pair, Index: index))
            .Where(item => item.Pair.From.Any(sourceIds.Contains))
            .Select(item => new EvidentiaGoldLink(
                -1 - item.Index, LinkMethod.StatedBySource, InterlinearGoldSource, item.Pair.From, item.Pair.To))
            .ToList();
        return Annotated(links, sourceIds, targetIds);
    }

    private static EvidentiaGold Annotated(
        IReadOnlyList<EvidentiaGoldLink> allLinks,
        IReadOnlySet<long> sourceIds,
        IReadOnlySet<long> targetIds)
    {
        // An absence names words on one side only, whichever direction it was written in: those of
        // the translation it supplies, or those of the original it leaves unrendered.
        var absences = allLinks.Where(link => link.Absence).ToList();
        var goldLinks = allLinks.Where(link => !link.Absence).ToList();
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

        return new EvidentiaGold(
            pairs,
            pairs.Select(pair => pair.one).ToHashSet(),
            bySourceWord,
            [.. goldLinks
                .Select(link => link with
                {
                    SourceWords = [.. link.SourceWords.Where(sourceIds.Contains)],
                    TargetWords = [.. link.TargetWords.Where(targetIds.Contains)],
                })
                .Where(link => link.SourceWords.Count > 0 && link.TargetWords.Count > 0)],
            absences.SelectMany(link => link.SourceWords).Where(sourceIds.Contains).ToHashSet(),
            absences.SelectMany(link => link.TargetWords).Where(targetIds.Contains).ToHashSet());
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
        int canonicalChapter,
        IReadOnlyDictionary<(long From, long To), List<string>> routeVerdicts)
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
                            .Select(token => token.Position == word.Position ? $"[{token.Surface}]" : token.Surface)),
                        routeVerdicts.TryGetValue((word.Id, proposal.Target.Token.Id), out var routes)
                            ? string.Join("; ", routes)
                            : null);
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
    public EvidentiaTierScore SafeTier => EvidentiaTierScore.Total(Chapters.Select(chapter => chapter.SafeTier));
    public int FallbackVerses => Chapters.Sum(chapter => chapter.FallbackVerses);
    public int ContentSourceWords => Chapters.Sum(chapter => chapter.ContentSourceWords);
    public int FinalProposedSourceWords => Chapters.Sum(chapter => chapter.FinalProposedSourceWords);
    public IReadOnlyList<EvidentiaDisagreement> Disagreements =>
        [.. Chapters.SelectMany(chapter => chapter.Disagreements)];

    public EvidentiaSourceWordAccount WordAccount => Chapters
        .Select(chapter => chapter.WordAccount)
        .Aggregate(default(EvidentiaSourceWordAccount), (running, next) => running + next);

    public IReadOnlyList<EvidentiaWordRecord> Words => [.. Chapters.SelectMany(chapter => chapter.Words)];

    public IReadOnlyList<EvidentiaAbsenceRecord> Absences => [.. Chapters.SelectMany(chapter => chapter.Absences)];

    public EvidentiaWordMeasure ByWord => Chapters.Aggregate(default(EvidentiaWordMeasure), (running, next) => running + next.ByWord);

    public EvidentiaSecondPassAccount SecondPass =>
        Chapters.Aggregate(default(EvidentiaSecondPassAccount), (running, next) => running + next.SecondPass);

    public EvidentiaStateMeasure States => Chapters.Aggregate(EvidentiaStateMeasure.Empty, (running, next) => running + next.States);

    public IReadOnlyList<EvidentiaKeyDoubtRecord> KeyDoubts => [.. Chapters.SelectMany(chapter => chapter.KeyDoubts)];

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
               SafeTier.Report(EvidentiaChapterMeasurement.SafeTierName, GoldPairs) + "\n" +
               $"learned index reach: {IndexAnsweredForms.Count:N0}/{IndexAskedForms.Count:N0} " +
               "distinct content source forms have an entry\n" +
               $"final abstention: {ContentSourceWords - FinalProposedSourceWords:N0}/{ContentSourceWords:N0} " +
               $"content source words unplaced ({Abstention:P2})\n" +
               WordAccount.Report() + "\n" +
               ByWord.Report() + "\n" +
               States.Report() +
               (SecondPass == default ? string.Empty : "\n" + SecondPass.Report()) +
               string.Concat(EvidentiaRouteAgreement.Total(Chapters.SelectMany(chapter => chapter.Routes))
                   .Select(route => "\n" + route.Report())) +
               string.Concat(Chapters
                   .Where(chapter => chapter.Samples.Count > 0)
                   .Select(chapter => $"\nsample, chapter {chapter.CanonicalChapter}:\n"
                       + string.Join("\n", chapter.Samples)));
    }
}

/// <summary>
/// How many of a passage's final links a route text could check, and how many of those it agrees
/// with. <see cref="Links"/> is every final link, so the share a route reaches is part of the answer.
/// </summary>
/// <summary>
/// What the confirmed renderings and the aligner's pairs changed in a passage: words placed by each,
/// placements withheld, and placements moved into and out of the safe tier.
/// </summary>
internal readonly record struct EvidentiaSecondPassAccount(
    int ConfirmedRenderings, int AlignerAndLexical, int Withheld, int IntoSafe, int OutOfSafe)
{
    public static EvidentiaSecondPassAccount operator +(EvidentiaSecondPassAccount one, EvidentiaSecondPassAccount two) =>
        new(one.ConfirmedRenderings + two.ConfirmedRenderings, one.AlignerAndLexical + two.AlignerAndLexical,
            one.Withheld + two.Withheld, one.IntoSafe + two.IntoSafe, one.OutOfSafe + two.OutOfSafe);

    public string Report() =>
        $"second pass: {ConfirmedRenderings:N0} words placed by the text's own renderings, {AlignerAndLexical:N0} by the aligner " +
        $"with a dictionary sense or gloss; {Withheld:N0} placements withheld where the aligner names another supported word; " +
        $"{IntoSafe:N0} into the safe tier on its agreement, {OutOfSafe:N0} out of it";
}

internal sealed record EvidentiaRouteAgreement(string Route, int Links, int Compared, int Agreed)
{
    public static IEnumerable<EvidentiaRouteAgreement> Total(IEnumerable<EvidentiaRouteAgreement> routes) =>
        routes.GroupBy(route => route.Route, StringComparer.Ordinal)
            .Select(group => new EvidentiaRouteAgreement(
                group.Key, group.Sum(route => route.Links), group.Sum(route => route.Compared), group.Sum(route => route.Agreed)));

    public string Report() =>
        $"route agreement with {Route}: {Agreed:N0}/{Compared:N0} ({(double)Agreed / Math.Max(1, Compared):P2}) " +
        $"of the links the {Route} route reaches; {Compared:N0}/{Links:N0} links reached " +
        $"({(double)Compared / Math.Max(1, Links):P2})";
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
    EvidentiaTierScore SafeTier,
    EvidentiaTierScore Unambiguous,
    int FinalProposedSourceWords,
    IReadOnlySet<string> IndexAskedForms,
    IReadOnlySet<string> IndexAnsweredForms,
    IReadOnlyList<string> Samples,
    IReadOnlyList<EvidentiaDisagreement> Disagreements,
    EvidentiaSourceWordAccount WordAccount,
    IReadOnlyList<EvidentiaWordRecord> Words,
    EvidentiaWordMeasure ByWord,
    IReadOnlyList<EvidentiaAbsenceRecord> Absences)
{
    /// <summary>What a stored run marks safe: the final proposals the safe renderings and their attached words hold.</summary>
    public const string SafeTierName = "safe tier";

    /// <summary>How far the final links agree with each route text asked for; empty where none was.</summary>
    public IReadOnlyList<EvidentiaRouteAgreement> Routes { get; init; } = [];

    public EvidentiaSecondPassAccount SecondPass { get; init; }

    /// <summary>The passage on the split key, by state, with the cascade.</summary>
    public EvidentiaStateMeasure States { get; init; } = EvidentiaStateMeasure.Empty;

    /// <summary>The cases for hand judgment; empty unless asked for.</summary>
    public IReadOnlyList<EvidentiaKeyDoubtRecord> KeyDoubts { get; init; } = [];

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
              SafeTier.Report(SafeTierName, GoldPairs) + "\n" +
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
               WordAccount.Report() + "\n" +
               ByWord.Report() + "\n" +
               States.Report() +
               (SecondPass == default ? string.Empty : "\n" + SecondPass.Report()) +
               string.Concat(Routes.Select(route => "\n" + route.Report())) +
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
/// The text whose stated renderings feed the learned index, when it is not the measured source; several,
/// comma-separated, are read as one corpus (<c>KJV,BSB</c> for a text neither of them is).
/// </param>
/// <param name="LearnedRenderingMethods">
/// Which link methods the learned index may read. The default is source-stated only; naming
/// <see cref="LinkMethod.StrongNumber"/> admits printed-number inferences, which is the only
/// English rendering evidence the New Testament pairs have that is not the Berean gold itself.
/// </param>
/// <param name="GoldSource">
/// A substring of <c>Link.Source</c>, so one dataset's rows can be scored on their own.
/// </param>
/// <param name="LearnAcrossLanguages">
/// Let <paramref name="LearnRenderingsFrom"/> be a text in another language than the measured one.
/// </param>
/// <param name="GoldInterlinear">
/// A Door43 interlinear folder whose join, made in memory, is the answer key instead of the stored
/// links.
/// </param>
/// <param name="RecordDisagreements">
/// Keep every contradicted proposal, so a classification pass reads the same rows the aggregate
/// counted rather than a second run's.
/// </param>
/// <param name="RecordWords">
/// Keep every source word with its outcome, so two runs can be compared word by word.
/// </param>
/// <param name="RecordKeyDoubts">
/// Keep the cases the split key, the key as loaded and the placements do not settle, for a person to judge.
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
    IEvidentiaDecisionSink? Decisions = null,
    string? GoldInterlinear = null,
    bool LearnAcrossLanguages = false,
    bool SourceFromFiles = false,
    IReadOnlyList<string>? RouteTexts = null,
    int NeighbourVerseDistance = EvidentiaDefaults.NeighbourVerseDistance,
    bool EntityAnchors = false,
    string? EntityNamesFrom = null,
    EvidentiaConfirmedRenderings? Confirmed = null,
    EvidentiaConfirmedRenderings? Learns = null,
    bool SecondPass = false,
    IReadOnlyList<int>? ConfirmedByRuns = null,
    IReadOnlyList<string>? ConfirmedByFiles = null,
    bool AlignerLinks = false,
    EvidentiaAlignerPairs? AlignerPairs = null,
    bool RecordKeyDoubts = false);

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

/// <param name="Absence">An <c>omits</c> or <c>expands</c> link: its words have no counterpart on the other side.</param>
internal sealed record EvidentiaGoldLink(
    long Id,
    LinkMethod Method,
    string Source,
    IReadOnlyList<long> SourceWords,
    IReadOnlyList<long> TargetWords,
    bool Absence = false);

/// <param name="Links">The correspondences in scope, each cut to the words of the passage it names.</param>
/// <param name="SuppliedSourceWords">Words of the measured text the answer key says it supplies.</param>
/// <param name="UnrenderedTargetWords">Words of the witness the answer key says the measured text leaves unrendered.</param>
internal sealed record EvidentiaGold(
    IReadOnlySet<(long From, long To)> Pairs,
    IReadOnlySet<long> CoveredSourceWords,
    IReadOnlyDictionary<long, List<EvidentiaGoldLink>> LinksBySourceWord,
    IReadOnlyList<EvidentiaGoldLink> Links,
    IReadOnlySet<long> SuppliedSourceWords,
    IReadOnlySet<long> UnrenderedTargetWords);

/// <summary>A word said to have no counterpart, and whether the answer key agrees; null where it cannot say.</summary>
internal sealed record EvidentiaAbsenceRecord(
    int CanonicalBook,
    int CanonicalChapter,
    int CanonicalVerse,
    long WordId,
    int Position,
    string Surface,
    string? StrongNumber,
    string Kind,
    string Rationale,
    string? Anchor,
    bool? Correct);

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
    string SourceVerse,
    string? Routes = null);
