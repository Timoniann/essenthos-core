namespace Essenthos.Core.Loading.Links.Evidentia;

/// <summary>What the pivot is allowed to change. Each was measured on its own before it was kept.</summary>
[Flags]
internal enum EvidentiaPivotPolicy
{
    None = 0,

    /// <summary>A placement every pivot that names the word contradicts is withdrawn.</summary>
    Withhold = 1,

    /// <summary>An unplaced word goes on the free word two pivot texts both name.</summary>
    FillTwoTexts = 2,

    /// <summary>An unplaced word goes on the free word a pivot names and its own lexical evidence names too.</summary>
    FillLexical = 4,

    /// <summary>A placement a pivot agrees with goes into the safe tier.</summary>
    Safe = 8,
}

/// <summary>
/// Reads the pivot against what EVIDENTIA concluded from the words themselves: where the two name the
/// same pair it is right far more often than either alone, where they differ EVIDENTIA's own choice is
/// right about half the time, and on a word EVIDENTIA left unplaced the pivot's pair needs one more
/// signal before it is taken.
///
/// Moving a contradicted placement to the occurrence of the same lexeme the pivot names was tried and is
/// not done: on the benchmark passages it made 10 of 31 placements right where 19 had been.
/// </summary>
internal static class EvidentiaPivotCheck
{
    /// <summary>A review-tier confidence: two readings agreeing, neither of them the verse's own.</summary>
    public const double Confidence = EvidentiaDefaults.TargetGlossReviewConfidence;

    /// <summary>
    /// The words a fill may place: on the benchmark passages an adverb, number or adjective the pivot names
    /// was right about four times in five, and the nouns, names and verbs about nine in ten.
    /// </summary>
    private static readonly HashSet<string> FilledParts = new(StringComparer.Ordinal) { "NOUN", "PROPN", "VERB" };

    /// <summary><em>let</em> writes the original's jussive or cohortative: it goes with its verb, not on a word of its own.</summary>
    private static readonly HashSet<string> MoodVerbs = new(StringComparer.OrdinalIgnoreCase) { "let" };

    private static readonly HashSet<EvidentiaEvidenceKind> LexicalEvidence =
    [
        EvidentiaEvidenceKind.KnownRendering,
        EvidentiaEvidenceKind.DictionarySense,
        EvidentiaEvidenceKind.TargetGloss,
        EvidentiaEvidenceKind.MatchingNormalisedForm,
        EvidentiaEvidenceKind.SharedStrongNumber,
    ];

    /// <summary>The placements every pivot that names the word contradicts.</summary>
    public static IReadOnlySet<(long From, long To)> Contradicted(
        IReadOnlyList<EvidentiaProposal> lexical,
        EvidentiaPivot pivot,
        EvidentiaPivotPolicy policy) =>
        policy.HasFlag(EvidentiaPivotPolicy.Withhold)
            ? lexical
                .Where(proposal => proposal.Kind != EvidentiaProposalKind.SharedEntityInOrder
                    && pivot.Contradicts(proposal.Source.Token.Id, proposal.Target.Token.Id))
                .Select(proposal => (proposal.Source.Token.Id, proposal.Target.Token.Id))
                .ToHashSet()
            : new HashSet<(long, long)>();

    /// <summary>
    /// An unplaced noun, name or verb on the one free word of its verse a pivot names, where a second pivot
    /// text or the word's own lexical evidence names the same word.
    /// </summary>
    public static IReadOnlyList<EvidentiaProposal> Resolve(
        IReadOnlyList<EvidentiaAnalysis> source,
        IReadOnlyList<EvidentiaAnalysis> target,
        IEnumerable<EvidentiaCandidate> candidates,
        IReadOnlyList<EvidentiaProposal> reserved,
        EvidentiaPivot pivot,
        EvidentiaPivotPolicy policy)
    {
        if ((policy & (EvidentiaPivotPolicy.FillTwoTexts | EvidentiaPivotPolicy.FillLexical)) == 0 || pivot.IsEmpty)
        {
            return [];
        }

        var targetById = target.DistinctBy(word => word.Token.Id).ToDictionary(word => word.Token.Id);
        var placed = reserved.Select(proposal => proposal.Source.Token.Id).ToHashSet();
        var taken = reserved.Select(proposal => proposal.Target.Token.Id).ToHashSet();
        var lexical = candidates
            .Where(candidate => candidate.Evidence.Any(evidence => LexicalEvidence.Contains(evidence.Kind)))
            .GroupBy(candidate => (candidate.Source.Token.Id, candidate.Target.Token.Id))
            .ToDictionary(group => group.Key, group => group.First());
        var proposals = new List<EvidentiaProposal>();
        foreach (var word in source.DistinctBy(word => word.Token.Id)
                     .Where(word => word.IsContentWord
                         && FilledParts.Contains(word.Token.PartOfSpeech ?? string.Empty)
                         && !MoodVerbs.Contains(word.Token.Surface)
                         && !placed.Contains(word.Token.Id)))
        {
            var chosen = pivot.Named(word.Token.Id)
                .Select(id => targetById.GetValueOrDefault(id))
                .OfType<EvidentiaAnalysis>()
                .Where(other => other.Token.Address == word.Token.Address
                    && !other.IsFunctionWord
                    && !taken.Contains(other.Token.Id)
                    && ((policy.HasFlag(EvidentiaPivotPolicy.FillTwoTexts) && pivot.Naming(word.Token.Id, other.Token.Id) >= 2)
                        || (policy.HasFlag(EvidentiaPivotPolicy.FillLexical) && lexical.ContainsKey((word.Token.Id, other.Token.Id)))))
                .ToList();
            if (chosen.Count != 1)
            {
                continue;
            }

            var pair = (word.Token.Id, chosen[0].Token.Id);
            var texts = pivot.Naming(pair.Item1, pair.Item2);
            var signal = texts >= 2 && lexical.ContainsKey(pair)
                ? "two texts of the language and the word's own lexical evidence"
                : texts >= 2
                    ? "two texts of the language"
                    : "another text of the language and the word's own lexical evidence";
            taken.Add(chosen[0].Token.Id);
            proposals.Add(new EvidentiaProposal(
                word,
                chosen[0],
                EvidentiaProposalKind.SameLanguagePivot,
                Confidence,
                lexical.TryGetValue(pair, out var candidate)
                    ? EvidentiaDecisionTrace.For(candidate, "review", $"{signal} put the word here")
                    : new EvidentiaDecisionTrace("review", $"{signal} put the word here", [])));
        }

        return proposals;
    }
}
