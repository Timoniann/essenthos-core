using System.Globalization;

namespace Essenthos.Core.Loading.Links.Evidentia;

/// <summary>
/// The pairs the statistical aligner holds for a passage, by the translation's word. It learnt them
/// from where words stand together across the whole text and from nothing EVIDENTIA reads, which is
/// what makes its agreement worth something and its pair alone worth little.
/// </summary>
internal sealed class EvidentiaAlignerPairs(IReadOnlyDictionary<long, HashSet<long>> bySource)
{
    private static readonly IReadOnlySet<long> None = new HashSet<long>();

    public IReadOnlySet<long> Of(long source) => bySource.TryGetValue(source, out var targets) ? targets : None;

    public bool Agrees(long source, long target) => bySource.TryGetValue(source, out var targets) && targets.Contains(target);

    /// <summary>The aligner links the word, and not to this one.</summary>
    public bool Contradicts(long source, long target) =>
        bySource.TryGetValue(source, out var targets) && !targets.Contains(target);

    public static EvidentiaAlignerPairs Of(IEnumerable<(long Source, long Target)> pairs)
    {
        var bySource = new Dictionary<long, HashSet<long>>();
        foreach (var (source, target) in pairs)
        {
            if (!bySource.TryGetValue(source, out var targets))
            {
                bySource[source] = targets = [];
            }

            targets.Add(target);
        }

        return new EvidentiaAlignerPairs(bySource);
    }

    /// <summary>Reads the pairs <c>score … --pairs</c> writes: the two word ids first on each line, tab-separated.</summary>
    public static EvidentiaAlignerPairs Read(IEnumerable<string> paths) => Of(paths.SelectMany(path =>
        File.Exists(path)
            ? File.ReadLines(path).Select(line =>
            {
                var fields = line.Split('\t');
                return fields.Length >= 2
                    && long.TryParse(fields[0], NumberStyles.None, CultureInfo.InvariantCulture, out var source)
                    && long.TryParse(fields[1], NumberStyles.None, CultureInfo.InvariantCulture, out var target)
                        ? (source, target)
                        : throw new FormatException(
                            $"'{line}' in {path} is not an aligner pair. Each line starts with the two word ids, " +
                            "separated by a tab, as score --pairs writes them.");
            })
            : throw new FileNotFoundException(
                $"There is no aligner-pairs file at {path}. Write one with score <from> <to> --stated --pairs <file>.", path)));
}

/// <summary>
/// Reads the aligner's pair against what EVIDENTIA concluded from the words themselves. The two were
/// reached by unrelated means, so where they name the same pair it is right far more often than either
/// alone, and where the aligner names another word EVIDENTIA has evidence for as well, EVIDENTIA's own
/// choice is right less than half the time. The aligner's pair is never taken on its own word.
/// </summary>
internal static class EvidentiaAlignerCheck
{
    /// <summary>What a pair the aligner and a dictionary or gloss both name is worth, in the review tier.</summary>
    public const double Confidence = EvidentiaDefaults.TargetGlossReviewConfidence;

    private static readonly HashSet<EvidentiaEvidenceKind> LexicalEvidence =
    [
        EvidentiaEvidenceKind.KnownRendering,
        EvidentiaEvidenceKind.DictionarySense,
        EvidentiaEvidenceKind.TargetGloss,
        EvidentiaEvidenceKind.MatchingNormalisedForm,
    ];

    /// <summary>
    /// The evidence that names the word directly. A learned rendering is left out: a word that has one
    /// and is still unplaced is a word the rendering tiers have already weighed and refused.
    /// </summary>
    private static readonly HashSet<EvidentiaEvidenceKind> DirectEvidence =
    [
        EvidentiaEvidenceKind.DictionarySense,
        EvidentiaEvidenceKind.TargetGloss,
        EvidentiaEvidenceKind.MatchingNormalisedForm,
    ];

    /// <summary>
    /// The placements to withhold: the aligner puts the word on another word of the verse that is free
    /// and that EVIDENTIA has lexical evidence for too, so the evidence does not choose between them.
    /// </summary>
    public static IReadOnlySet<(long From, long To)> Contested(
        IReadOnlyList<EvidentiaProposal> lexical,
        IEnumerable<EvidentiaCandidate> candidates,
        EvidentiaAlignerPairs aligner)
    {
        var taken = lexical.Select(proposal => proposal.Target.Token.Id).ToHashSet();
        var supported = Supported(candidates, LexicalEvidence);
        return lexical
            .Where(proposal => proposal.Kind != EvidentiaProposalKind.SharedEntityInOrder
                && aligner.Contradicts(proposal.Source.Token.Id, proposal.Target.Token.Id)
                && aligner.Of(proposal.Source.Token.Id).Any(other =>
                    !taken.Contains(other) && supported.Contains((proposal.Source.Token.Id, other))))
            .Select(proposal => (proposal.Source.Token.Id, proposal.Target.Token.Id))
            .ToHashSet();
    }

    /// <summary>
    /// An unplaced word on the free word of its verse the aligner pairs it with, where a dictionary sense,
    /// the original's gloss or the spelling names that pair and no learned rendering was weighed for it.
    /// </summary>
    public static IReadOnlyList<EvidentiaProposal> Resolve(
        IEnumerable<EvidentiaCandidate> candidates,
        IReadOnlyList<EvidentiaProposal> reserved,
        EvidentiaAlignerPairs aligner,
        EvidentiaVerseFrame frame)
    {
        var placed = reserved.Select(proposal => proposal.Source.Token.Id).ToHashSet();
        var taken = reserved.Select(proposal => proposal.Target.Token.Id).ToHashSet();
        var proposals = new List<EvidentiaProposal>();
        foreach (var word in candidates
                     .Where(candidate => !placed.Contains(candidate.Source.Token.Id)
                         && !taken.Contains(candidate.Target.Token.Id)
                         && candidate.Source.Token.Address == candidate.Target.Token.Address
                         && aligner.Agrees(candidate.Source.Token.Id, candidate.Target.Token.Id)
                         && candidate.Evidence.Any(evidence => DirectEvidence.Contains(evidence.Kind))
                         && candidate.Evidence.All(evidence => evidence.Kind != EvidentiaEvidenceKind.KnownRendering)
                         && !candidate.PairsAContentWordWithAFunctionWord
                         && !candidate.PlacesAnAuxiliaryWordOffItsKind)
                     .GroupBy(candidate => candidate.Source.Token.Id))
        {
            // The aligner gave the word two words of the verse with this evidence: it has not chosen.
            if (word.Select(candidate => candidate.Target.Token.Id).Distinct().Count() != 1)
            {
                continue;
            }

            var candidate = word.First();
            var proposal = new EvidentiaProposal(
                candidate.Source,
                candidate.Target,
                EvidentiaProposalKind.AlignerAndLexicalEvidence,
                Confidence,
                EvidentiaDecisionTrace.For(candidate, "review",
                    "the statistical aligner pairs the two, and a dictionary sense, the original's gloss or the spelling names the same pair"));
            if (frame.Distance(proposal) < EvidentiaVerseFrame.MaximumDistance && taken.Add(candidate.Target.Token.Id))
            {
                proposals.Add(proposal);
            }
        }

        return proposals;
    }

    private static HashSet<(long From, long To)> Supported(
        IEnumerable<EvidentiaCandidate> candidates, HashSet<EvidentiaEvidenceKind> kinds) =>
        candidates
            .Where(candidate => candidate.Source.Token.Address == candidate.Target.Token.Address
                && candidate.Evidence.Any(evidence => kinds.Contains(evidence.Kind)))
            .Select(candidate => (candidate.Source.Token.Id, candidate.Target.Token.Id))
            .ToHashSet();
}
