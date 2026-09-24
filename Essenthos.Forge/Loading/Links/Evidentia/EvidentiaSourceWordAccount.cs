namespace Essenthos.Core.Loading.Links.Evidentia;

/// <summary>
/// What became of one source word: placed, or why not. The reasons are ordered from the most
/// deliberate to the least, and a word takes the first that applies, so a function word with a
/// stray candidate is still counted as the policy it is rather than as a refusal.
/// </summary>
internal enum EvidentiaWordOutcome
{
    Proposed,

    /// <summary>The language pack calls it a function word, which EVIDENTIA does not attempt.</summary>
    FunctionWord,

    /// <summary>No pack claimed the word, so nothing lexical could be said about it.</summary>
    NoLanguageAnalysis,

    NoCandidate,

    /// <summary>Every candidate stands in a neighbouring verse, and every tier reads its own verse only.</summary>
    NeighbouringVerseOnly,

    /// <summary>
    /// Every learned rendering in the verse would put an auxiliary word on a kind of word it cannot
    /// correspond to, such as a subject pronoun on the article of a noun.
    /// </summary>
    AuxiliaryWordOffItsKind,

    /// <summary>A learned rendering exists and the review policy's score, lead or observation floor refused it.</summary>
    RenderingRefusedByPolicy,

    /// <summary>The policy admitted a rendering, and the one-to-one assignment gave every such target to another word.</summary>
    RenderingTargetAssignedElsewhere,

    /// <summary>Only a dictionary sense or a target gloss, which the review tiers declined.</summary>
    DictionaryOrGlossDeclined,

    OtherEvidenceOnly,
}

internal sealed record EvidentiaWordRecord(
    int CanonicalBook,
    int CanonicalChapter,
    int CanonicalVerse,
    long SourceWordId,
    int SourcePosition,
    string SourceSurface,
    string? SourcePartOfSpeech,
    EvidentiaWordOutcome Outcome,
    long? TargetWordId,
    string? TargetSurface,
    string? TargetStrongNumber,
    string? ProposalKind,
    bool? Correct,
    bool GoldCoversSourceWord,
    string? Rationale = null,
    bool Safe = false,
    IReadOnlyList<EvidentiaCandidateTrace>? Candidates = null,
    string? Absence = null,
    bool? AbsenceCorrect = null,
    bool? CorrectByWord = null);

/// <summary>One of the strongest edges a word had in its own verse, kept so a word left unplaced can be read.</summary>
internal sealed record EvidentiaCandidateTrace(
    long TargetWordId,
    string TargetSurface,
    string? TargetStrongNumber,
    double Score,
    IReadOnlyList<string> Evidence,
    double? RenderingShare);

/// <summary>
/// Every source word of a passage, counted once: how many were placed, how many of those the gold
/// agrees with, contradicts or cannot judge, and why each of the rest was not placed. A share of
/// words left unplaced means nothing until it says how much of it is policy.
/// </summary>
internal readonly record struct EvidentiaSourceWordAccount(
    int SourceWords,
    int Proposed,
    int Correct,
    int Contradicted,
    int Unscored,
    int FunctionWord,
    int NoLanguageAnalysis,
    int NoCandidate,
    int NeighbouringVerseOnly,
    int AuxiliaryWordOffItsKind,
    int RenderingRefusedByPolicy,
    int RenderingTargetAssignedElsewhere,
    int DictionaryOrGlossDeclined,
    int OtherEvidenceOnly)
{
    public int NotProposed => SourceWords - Proposed;

    public int AttemptedAndNotProposed => NotProposed - FunctionWord - NoLanguageAnalysis;

    public static EvidentiaSourceWordAccount Of(IEnumerable<EvidentiaWordRecord> words)
    {
        var all = words as IReadOnlyCollection<EvidentiaWordRecord> ?? [.. words];
        int Count(EvidentiaWordOutcome outcome) => all.Count(word => word.Outcome == outcome);
        return new EvidentiaSourceWordAccount(
            all.Count,
            Count(EvidentiaWordOutcome.Proposed),
            all.Count(word => word.Correct == true),
            all.Count(word => word.Correct == false && word.GoldCoversSourceWord),
            all.Count(word => word.Correct == false && !word.GoldCoversSourceWord),
            Count(EvidentiaWordOutcome.FunctionWord),
            Count(EvidentiaWordOutcome.NoLanguageAnalysis),
            Count(EvidentiaWordOutcome.NoCandidate),
            Count(EvidentiaWordOutcome.NeighbouringVerseOnly),
            Count(EvidentiaWordOutcome.AuxiliaryWordOffItsKind),
            Count(EvidentiaWordOutcome.RenderingRefusedByPolicy),
            Count(EvidentiaWordOutcome.RenderingTargetAssignedElsewhere),
            Count(EvidentiaWordOutcome.DictionaryOrGlossDeclined),
            Count(EvidentiaWordOutcome.OtherEvidenceOnly));
    }

    public static EvidentiaSourceWordAccount operator +(EvidentiaSourceWordAccount one, EvidentiaSourceWordAccount two) =>
        new(one.SourceWords + two.SourceWords,
            one.Proposed + two.Proposed,
            one.Correct + two.Correct,
            one.Contradicted + two.Contradicted,
            one.Unscored + two.Unscored,
            one.FunctionWord + two.FunctionWord,
            one.NoLanguageAnalysis + two.NoLanguageAnalysis,
            one.NoCandidate + two.NoCandidate,
            one.NeighbouringVerseOnly + two.NeighbouringVerseOnly,
            one.AuxiliaryWordOffItsKind + two.AuxiliaryWordOffItsKind,
            one.RenderingRefusedByPolicy + two.RenderingRefusedByPolicy,
            one.RenderingTargetAssignedElsewhere + two.RenderingTargetAssignedElsewhere,
            one.DictionaryOrGlossDeclined + two.DictionaryOrGlossDeclined,
            one.OtherEvidenceOnly + two.OtherEvidenceOnly);

    public string Report()
    {
        var total = SourceWords;
        string Share(int count) => total == 0 ? "0.00%" : $"{(double)count / total:P2}";
        return $"source words: {SourceWords:N0}; proposed {Proposed:N0} ({Share(Proposed)}): " +
               $"correct {Correct:N0} ({Share(Correct)}), contradicted on gold-covered words {Contradicted:N0} ({Share(Contradicted)}), " +
               $"on words the gold does not reach {Unscored:N0} ({Share(Unscored)}); " +
               $"no proposal {NotProposed:N0} ({Share(NotProposed)})\n" +
               $"no proposal, by reason: function word {FunctionWord:N0} ({Share(FunctionWord)}); " +
               $"no language analysis {NoLanguageAnalysis:N0} ({Share(NoLanguageAnalysis)}); " +
               $"attempted {AttemptedAndNotProposed:N0} ({Share(AttemptedAndNotProposed)}) = " +
               $"no candidate {NoCandidate:N0}, neighbouring verse only {NeighbouringVerseOnly:N0}, " +
               $"auxiliary word off its kind {AuxiliaryWordOffItsKind:N0}, " +
               $"rendering refused by policy {RenderingRefusedByPolicy:N0}, " +
               $"rendering target assigned elsewhere {RenderingTargetAssignedElsewhere:N0}, " +
               $"dictionary or gloss declined {DictionaryOrGlossDeclined:N0}, other evidence only {OtherEvidenceOnly:N0}";
    }

    /// <summary>
    /// Classifies every source word against the final proposals. <paramref name="admittedRenderings"/>
    /// is the set of source words whose learned rendering the review policy would accept if its
    /// target were free, which is what separates a refusal from a lost assignment.
    /// </summary>
    public static IReadOnlyList<EvidentiaWordRecord> Classify(
        IReadOnlyList<EvidentiaToken> source,
        Func<EvidentiaToken, EvidentiaAnalysis?> analyse,
        IReadOnlyList<EvidentiaCandidate> candidates,
        IReadOnlyList<EvidentiaProposal> proposals,
        IReadOnlySet<long> admittedRenderings,
        IReadOnlySet<(long From, long To)> gold,
        IReadOnlySet<long> covered,
        int canonicalBook,
        int canonicalChapter,
        IReadOnlySet<(long From, long To)>? safe = null)
    {
        var proposalBySource = proposals
            .GroupBy(proposal => proposal.Source.Token.Id)
            .ToDictionary(group => group.Key, group => group.First());
        var candidatesBySource = candidates
            .GroupBy(candidate => candidate.Source.Token.Id)
            .ToDictionary(group => group.Key, group => group.ToList());
        return
        [
            .. source.DistinctBy(token => token.Id).Select(token =>
            {
                var proposal = proposalBySource.GetValueOrDefault(token.Id);
                var outcome = proposal is not null
                    ? EvidentiaWordOutcome.Proposed
                    : Reason(analyse(token), candidatesBySource.GetValueOrDefault(token.Id) ?? [],
                        admittedRenderings.Contains(token.Id));
                return new EvidentiaWordRecord(
                    canonicalBook,
                    canonicalChapter,
                    token.Address.Verse,
                    token.Id,
                    token.Position,
                    token.Surface,
                    token.PartOfSpeech,
                    outcome,
                    proposal?.Target.Token.Id,
                    proposal?.Target.Token.Surface,
                    proposal?.Target.Token.StrongNumber,
                    proposal?.Kind.ToString(),
                    proposal is null ? null : gold.Contains((token.Id, proposal.Target.Token.Id)),
                    covered.Contains(token.Id),
                    proposal?.Trace?.Rationale,
                    proposal is not null && safe is not null && safe.Contains((token.Id, proposal.Target.Token.Id)),
                    Strongest(candidatesBySource.GetValueOrDefault(token.Id) ?? []));
            }),
        ];
    }

    private const int TracedCandidates = 4;

    private static IReadOnlyList<EvidentiaCandidateTrace> Strongest(IReadOnlyList<EvidentiaCandidate> candidates) =>
    [
        .. candidates
            .Where(candidate => candidate.Evidence.Any(evidence => evidence.Kind == EvidentiaEvidenceKind.ExactCanonicalAddress))
            .OrderByDescending(candidate => candidate.Score)
            .Take(TracedCandidates)
            .Select(candidate => new EvidentiaCandidateTrace(
                candidate.Target.Token.Id,
                candidate.Target.Token.Surface,
                candidate.Target.Token.StrongNumber,
                candidate.Score,
                [.. candidate.Evidence.Select(evidence => evidence.Kind.ToString())],
                candidate.Evidence
                    .Where(evidence => evidence.Kind == EvidentiaEvidenceKind.KnownRendering)
                    .Select(evidence => evidence.Support?.Share)
                    .FirstOrDefault())),
    ];

    /// <summary>The evidence some tier proposes from; anything else only ranks a candidate.</summary>
    private static readonly IReadOnlySet<EvidentiaEvidenceKind> ProposingEvidence = new HashSet<EvidentiaEvidenceKind>
    {
        EvidentiaEvidenceKind.KnownRendering,
        EvidentiaEvidenceKind.DictionarySense,
        EvidentiaEvidenceKind.TargetGloss,
    };

    private static EvidentiaWordOutcome Reason(
        EvidentiaAnalysis? analysis,
        IReadOnlyList<EvidentiaCandidate> candidates,
        bool renderingAdmitted)
    {
        if (analysis is null || analysis.WordClass == EvidentiaWordClass.Unknown)
        {
            return EvidentiaWordOutcome.NoLanguageAnalysis;
        }

        if (analysis.IsFunctionWord)
        {
            return EvidentiaWordOutcome.FunctionWord;
        }

        if (candidates.Count == 0)
        {
            return EvidentiaWordOutcome.NoCandidate;
        }

        var exactCandidates = candidates
            .Where(candidate => candidate.Evidence.Any(evidence => evidence.Kind == EvidentiaEvidenceKind.ExactCanonicalAddress))
            .ToList();
        var exact = exactCandidates
            .Where(candidate => !candidate.PlacesAnAuxiliaryWordOffItsKind)
            .SelectMany(candidate => candidate.Evidence)
            .Select(evidence => evidence.Kind)
            .ToHashSet();
        if (exactCandidates.Count == 0)
        {
            return EvidentiaWordOutcome.NeighbouringVerseOnly;
        }

        if (!exact.Overlaps(ProposingEvidence)
            && exactCandidates.Any(candidate => candidate.PlacesAnAuxiliaryWordOffItsKind
                && candidate.Evidence.Any(evidence => ProposingEvidence.Contains(evidence.Kind))))
        {
            return EvidentiaWordOutcome.AuxiliaryWordOffItsKind;
        }

        if (exact.Contains(EvidentiaEvidenceKind.KnownRendering))
        {
            return renderingAdmitted
                ? EvidentiaWordOutcome.RenderingTargetAssignedElsewhere
                : EvidentiaWordOutcome.RenderingRefusedByPolicy;
        }

        return exact.Contains(EvidentiaEvidenceKind.DictionarySense) || exact.Contains(EvidentiaEvidenceKind.TargetGloss)
            ? EvidentiaWordOutcome.DictionaryOrGlossDeclined
            : EvidentiaWordOutcome.OtherEvidenceOnly;
    }
}
