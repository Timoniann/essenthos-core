namespace Essenthos.Core.Loading.Links.Evidentia;

/// <summary>
/// Produces a read-only evidence graph for a future deterministic alignment run.
///
/// It deliberately has no database dependency: until the graph, the global matcher and their
/// measurements exist, a preview is a safer artifact than a link that could appear sourced.
/// </summary>
internal sealed class EvidentiaPipeline(
    LanguagePackRegistry languagePacks,
    IEnumerable<IEvidentiaEvidenceSource> evidenceSources)
{
    public EvidentiaPreview Preview(
        EvidentiaRequest request,
        IEnumerable<IEvidentiaEvidenceSource>? passageEvidence = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(request.NeighbourVerseDistance);
        if (request.MinimumContentCoverage is < 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(request),
                request.MinimumContentCoverage,
                "Minimum content coverage must be between zero and one.");
        }

        var allEvidence = passageEvidence is null
            ? evidenceSources.ToList()
            : [.. evidenceSources.Concat(passageEvidence)];
        if (!request.AllowSourceStrongEvidence)
        {
            allEvidence.RemoveAll(source => source is StrongNumberEvidenceSource);
        }
        var unsupported = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var source = Analyse(request.Source, unsupported);
        var target = Analyse(request.Target, unsupported);
        var candidates = Candidates(source, target, request.NeighbourVerseDistance, allEvidence);
        var content = source.Where(analysis => !analysis.IsFunctionWord).ToList();
        var measurable = content.Count > 0 ? content : source;
        var covered = candidates
            .Where(candidate => !candidate.Source.IsFunctionWord)
            .Select(candidate => candidate.Source.Token.Id)
            .Distinct()
            .Count();
        var coverage = measurable.Count == 0 ? 0 : (double)covered / measurable.Count;
        var fallback = unsupported.Count > 0 || coverage < request.MinimumContentCoverage;

        var status = unsupported.Count > 0
            ? EvidentiaPreviewStatus.UnsupportedLanguage
            : coverage < request.MinimumContentCoverage
                ? EvidentiaPreviewStatus.InsufficientEvidence
                : EvidentiaPreviewStatus.ReadyForRules;

        return new EvidentiaPreview(
            status,
            candidates,
            measurable.Count,
            covered,
            coverage,
            fallback,
            Todos(unsupported, allEvidence),
            EvidentiaPhraseBuilder.Build(candidates));
    }

    private List<EvidentiaAnalysis> Analyse(
        IEnumerable<EvidentiaToken> tokens,
        HashSet<string> unsupported)
    {
        var analysed = new List<EvidentiaAnalysis>();
        foreach (var token in tokens)
        {
            if (languagePacks.TryAnalyse(token, out var analysis))
            {
                analysed.Add(analysis);
            }
            else
            {
                unsupported.Add(token.Language);
                // Keep the token in the graph so a source-provided anchor (for example a shared
                // Strong number) can still be reported. Its absent language analysis keeps the
                // preview in fallback state; no normalisation evidence is manufactured.
                analysed.Add(new EvidentiaAnalysis(
                    token,
                    token.Surface.ToLowerInvariant(),
                    token.Lemma,
                    token.PartOfSpeech,
                    false,
                    LanguagePackCapability.None));
            }
        }

        return analysed;
    }

    private List<EvidentiaCandidate> Candidates(
        IEnumerable<EvidentiaAnalysis> source,
        IEnumerable<EvidentiaAnalysis> target,
        int maximumDistance,
        IReadOnlyList<IEvidentiaEvidenceSource> sources)
    {
        var candidates = new List<EvidentiaCandidate>();
        foreach (var from in source)
        {
            foreach (var to in target)
            {
                var evidence = StructuralEvidence(from, to, maximumDistance);
                if (!from.IsFunctionWord && from.Normalised == to.Normalised)
                {
                    evidence.Add(new EvidentiaEvidence(
                        EvidentiaEvidenceKind.MatchingNormalisedForm,
                        EvidentiaDefaults.MatchingNormalisedFormScore,
                        "language-pack"));
                }

                evidence.AddRange(sources.SelectMany(source => source.Find(from, to)));

                // Morphology ranks an already lexical candidate; POS alone would create a dense
                // noun-to-noun graph and manufacture coverage without translation evidence.
                if (evidence.Any(IsMeaningfulSignal)
                    && EvidentiaMorphologyScorer.Score(from, to) is { } morphology)
                {
                    evidence.Add(morphology);
                }

                // Sharing a canonical address narrows the search space; it does not say that any
                // particular pair of words corresponds. Treating it as a link would turn every
                // verse into a source-words × target-words graph and report fictional coverage.
                // An edge therefore needs at least one lexical, dictionary, morphology, syntax or
                // model signal. The address evidence then ranks that independently supported edge.
                if (evidence.Any(IsMeaningfulSignal))
                {
                    candidates.Add(new EvidentiaCandidate(from, to, evidence));
                }
            }
        }

        return candidates
            .OrderByDescending(candidate => candidate.Score)
            .ThenBy(candidate => candidate.Source.Token.Address.DistanceTo(candidate.Target.Token.Address))
            .ThenBy(candidate => candidate.Source.Token.Position)
            .ThenBy(candidate => candidate.Target.Token.Position)
            .ToList();
    }

    private static bool IsMeaningfulSignal(EvidentiaEvidence evidence) => evidence.Kind is not
        EvidentiaEvidenceKind.ExactCanonicalAddress and not EvidentiaEvidenceKind.NeighbouringCanonicalAddress;

    private static List<EvidentiaEvidence> StructuralEvidence(
        EvidentiaAnalysis source,
        EvidentiaAnalysis target,
        int maximumDistance)
    {
        var distance = source.Token.Address.DistanceTo(target.Token.Address);
        if (distance == 0)
        {
            return
            [
                new EvidentiaEvidence(
                    EvidentiaEvidenceKind.ExactCanonicalAddress,
                    EvidentiaDefaults.ExactAddressScore,
                    "canonical-frame"),
            ];
        }

        if (distance is > 0 && distance <= maximumDistance)
        {
            return
            [
                new EvidentiaEvidence(
                    EvidentiaEvidenceKind.NeighbouringCanonicalAddress,
                    EvidentiaDefaults.NeighbourAddressScore / distance,
                    "canonical-frame"),
            ];
        }

        return [];
    }

    private static IReadOnlyList<EvidentiaTodo> Todos(
        IReadOnlyCollection<string> unsupported,
        IReadOnlyCollection<IEvidentiaEvidenceSource> sources)
    {
        var todos = new List<EvidentiaTodo>
        {
            EvidentiaTodo.GlobalMatcher,
            EvidentiaTodo.StructuredExplanationStorage,
            EvidentiaTodo.StatisticalFallback,
        };

        if (unsupported.Count > 0)
        {
            todos.Add(EvidentiaTodo.LanguagePack);
        }

        if (!sources.OfType<EvidentiaDictionarySenseEvidenceSource>().Any())
        {
            todos.Add(EvidentiaTodo.DictionaryEvidence);
        }

        return todos;
    }
}
