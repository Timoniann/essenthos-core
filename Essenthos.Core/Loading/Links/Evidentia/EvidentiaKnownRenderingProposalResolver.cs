namespace Essenthos.Core.Loading.Links.Evidentia;

/// <summary>
/// Selects only stable, exact-verse rendering proposals from the candidate graph. It resolves one
/// target occurrence at most once, but does not write it: an inferred proposal is still not a
/// source-stated correspondence.
/// </summary>
internal sealed class EvidentiaKnownRenderingProposalResolver
{
    private const double MaximumConfidence = 0.90;
    private const double ConfidenceOffset = 0.25;
    private const double KnownEvidenceWeight = 1_000;
    private const double InvalidAssignmentCost = 1_000_000;

    public static readonly EvidentiaProposalPolicy Safe = new(
        "safe", MinimumKnownRenderingEvidence: 0.40, MinimumLeadOverAlternative: 0.10,
        EvidentiaProposalKind.StableKnownRendering);

    public static readonly EvidentiaProposalPolicy Review = new(
        "review", MinimumKnownRenderingEvidence: 0.30, MinimumLeadOverAlternative: 0.05,
        EvidentiaProposalKind.ReviewKnownRendering);

    public EvidentiaResolution Resolve(
        IEnumerable<EvidentiaCandidate> candidates,
        EvidentiaProposalPolicy? policy = null)
    {
        policy ??= Safe;
        var proposals = new List<EvidentiaProposal>();
        foreach (var verse in candidates
                     .Where(IsExactKnownRendering)
                     .GroupBy(candidate => candidate.Source.Token.Address))
        {
            var sourceCount = verse.Select(candidate => candidate.Source.Token.Id).Distinct().Count();
            var targetCount = verse.Select(candidate => candidate.Target.Token.Id).Distinct().Count();
            var choices = verse
                .GroupBy(candidate => candidate.Source.Token.Id)
                .Select(group => BestChoice(group, sourceCount, targetCount, policy))
                .Where(choice => choice is not null)
                .Cast<RankedChoice>()
                .OrderByDescending(choice => choice.KnownScore)
                .ThenByDescending(choice => choice.PositionScore)
                .ThenBy(choice => choice.Candidate.Source.Token.Position)
                .ToList();

            var usedSources = new HashSet<long>();
            var usedTargets = new HashSet<long>();
            foreach (var choice in choices)
            {
                if (!usedSources.Add(choice.Candidate.Source.Token.Id)
                    || !usedTargets.Add(choice.Candidate.Target.Token.Id))
                {
                    continue;
                }

                proposals.Add(new EvidentiaProposal(
                    choice.Candidate.Source,
                    choice.Candidate.Target,
                    policy.Kind,
                    Math.Min(MaximumConfidence, choice.KnownScore + ConfidenceOffset),
                    EvidentiaDecisionTrace.For(choice.Candidate, policy.Name,
                        $"known-rendering score {choice.KnownScore:F2}; lead meets {policy.MinimumLeadOverAlternative:F2}")));
            }
        }

        return new EvidentiaResolution(proposals, 0);
    }

    /// <summary>
    /// Makes the same per-source lexical decision as <see cref="Resolve"/>, then jointly assigns
    /// target occurrences. This only changes collisions among already clear lexical renderings;
    /// it never uses a runner-up Strong sense to fill an unmatched source word.
    /// </summary>
    public EvidentiaResolution ResolveGlobally(
        IEnumerable<EvidentiaCandidate> candidates,
        EvidentiaProposalPolicy? policy = null)
    {
        policy ??= Review;
        var proposals = new List<EvidentiaProposal>();
        foreach (var verse in candidates
                     .Where(IsExactKnownRendering)
                     .GroupBy(candidate => candidate.Source.Token.Address))
        {
            var sourceCount = verse.Select(candidate => candidate.Source.Token.Id).Distinct().Count();
            var targetCount = verse.Select(candidate => candidate.Target.Token.Id).Distinct().Count();
            var choices = verse
                .GroupBy(candidate => candidate.Source.Token.Id)
                .Select(group => BestChoiceGroup(group, policy))
                .Where(choice => choice is not null)
                .Cast<RankedChoiceGroup>()
                .ToList();

            foreach (var choice in GloballyAssign(choices, sourceCount, targetCount))
            {
                var kind = policy.Kind == EvidentiaProposalKind.StableKnownRendering
                    ? EvidentiaProposalKind.GlobalStableKnownRendering
                    : EvidentiaProposalKind.GlobalReviewKnownRendering;
                proposals.Add(new EvidentiaProposal(
                    choice.Candidate.Source,
                    choice.Candidate.Target,
                    kind,
                    Math.Min(MaximumConfidence, choice.KnownScore + ConfidenceOffset),
                    EvidentiaDecisionTrace.For(choice.Candidate, policy.Name,
                        $"global known-rendering assignment; lexical score {choice.KnownScore:F2}")));
            }
        }

        return new EvidentiaResolution(proposals, 0);
    }

    private static bool IsExactKnownRendering(EvidentiaCandidate candidate) =>
        candidate.Evidence.Any(evidence => evidence.Kind == EvidentiaEvidenceKind.ExactCanonicalAddress)
        && candidate.Evidence.Any(evidence => evidence.Kind == EvidentiaEvidenceKind.KnownRendering);

    private static RankedChoice? BestChoice(
        IGrouping<long, EvidentiaCandidate> sourceCandidates,
        int sourceCount,
        int targetCount,
        EvidentiaProposalPolicy policy)
    {
        var choiceGroup = BestChoiceGroup(sourceCandidates, policy);
        if (choiceGroup is null)
        {
            return null;
        }

        return choiceGroup.Candidates
            .Select(candidate => new RankedChoice(candidate, choiceGroup.KnownScore,
                PositionScore(candidate, sourceCount, targetCount)))
            .OrderByDescending(candidate => candidate.PositionScore)
            .ThenBy(candidate => candidate.Candidate.Target.Token.Position)
            .First();
    }

    private static RankedChoiceGroup? BestChoiceGroup(
        IGrouping<long, EvidentiaCandidate> sourceCandidates,
        EvidentiaProposalPolicy policy)
    {
        var byStrong = sourceCandidates
            .Where(candidate => candidate.Target.Token.StrongNumber is not null)
            .GroupBy(candidate => candidate.Target.Token.StrongNumber!)
            .Select(group => new
            {
                Candidates = group.ToList(),
                KnownScore = group.Max(KnownScore),
            })
            .OrderByDescending(group => group.KnownScore)
            .ToList();
        if (byStrong.Count == 0 || byStrong[0].KnownScore < policy.MinimumKnownRenderingEvidence)
        {
            return null;
        }

        var runnerUp = byStrong.Count > 1 ? byStrong[1].KnownScore : 0;
        if (byStrong[0].KnownScore - runnerUp < policy.MinimumLeadOverAlternative)
        {
            return null;
        }

        return new RankedChoiceGroup(byStrong[0].Candidates, byStrong[0].KnownScore);
    }

    private static IReadOnlyList<RankedChoice> GloballyAssign(
        IReadOnlyList<RankedChoiceGroup> choices,
        int sourceCount,
        int targetCount)
    {
        if (choices.Count == 0)
        {
            return [];
        }

        var targets = choices.SelectMany(choice => choice.Candidates)
            .Select(candidate => candidate.Target.Token.Id)
            .Distinct()
            .ToList();
        var targetIndex = targets.Select((id, index) => (id, index))
            .ToDictionary(item => item.id, item => item.index);
        var sourceRows = choices.Count;
        var columnCount = targets.Count + sourceRows;
        var costs = new double[sourceRows + 1, columnCount + 1];
        for (var row = 1; row <= sourceRows; row++)
        {
            for (var column = 1; column <= targets.Count; column++)
            {
                costs[row, column] = InvalidAssignmentCost;
            }

            foreach (var candidate in choices[row - 1].Candidates)
            {
                var column = targetIndex[candidate.Target.Token.Id] + 1;
                var weight = choices[row - 1].KnownScore * KnownEvidenceWeight
                    + PositionScore(candidate, sourceCount, targetCount);
                costs[row, column] = -weight;
            }
        }

        var assignedColumns = MinimumCostAssignment(costs, sourceRows, columnCount);
        var proposals = new List<RankedChoice>();
        for (var row = 0; row < sourceRows; row++)
        {
            var column = assignedColumns[row] - 1;
            if (column < 0 || column >= targets.Count)
            {
                continue;
            }

            var candidate = choices[row].Candidates
                .SingleOrDefault(candidate => candidate.Target.Token.Id == targets[column]);
            if (candidate is not null)
            {
                proposals.Add(new RankedChoice(candidate, choices[row].KnownScore,
                    PositionScore(candidate, sourceCount, targetCount)));
            }
        }

        return proposals;
    }

    internal static int[] MinimumCostAssignment(double[,] costs, int sourceRows, int columnCount)
    {
        var potentialsByRow = new double[sourceRows + 1];
        var potentialsByColumn = new double[columnCount + 1];
        var matchedRowByColumn = new int[columnCount + 1];
        var predecessorColumn = new int[columnCount + 1];

        for (var source = 1; source <= sourceRows; source++)
        {
            matchedRowByColumn[0] = source;
            var currentColumn = 0;
            var minimumDistance = Enumerable.Repeat(double.PositiveInfinity, columnCount + 1).ToArray();
            var used = new bool[columnCount + 1];
            do
            {
                used[currentColumn] = true;
                var currentRow = matchedRowByColumn[currentColumn];
                var delta = double.PositiveInfinity;
                var nextColumn = 0;
                for (var column = 1; column <= columnCount; column++)
                {
                    if (used[column])
                    {
                        continue;
                    }

                    var distance = costs[currentRow, column] - potentialsByRow[currentRow] - potentialsByColumn[column];
                    if (distance < minimumDistance[column])
                    {
                        minimumDistance[column] = distance;
                        predecessorColumn[column] = currentColumn;
                    }

                    if (minimumDistance[column] < delta)
                    {
                        delta = minimumDistance[column];
                        nextColumn = column;
                    }
                }

                for (var column = 0; column <= columnCount; column++)
                {
                    if (used[column])
                    {
                        potentialsByRow[matchedRowByColumn[column]] += delta;
                        potentialsByColumn[column] -= delta;
                    }
                    else
                    {
                        minimumDistance[column] -= delta;
                    }
                }

                currentColumn = nextColumn;
            }
            while (matchedRowByColumn[currentColumn] != 0);

            do
            {
                var previousColumn = predecessorColumn[currentColumn];
                matchedRowByColumn[currentColumn] = matchedRowByColumn[previousColumn];
                currentColumn = previousColumn;
            }
            while (currentColumn != 0);
        }

        var assignedColumns = new int[sourceRows];
        for (var column = 1; column <= columnCount; column++)
        {
            if (matchedRowByColumn[column] != 0)
            {
                assignedColumns[matchedRowByColumn[column] - 1] = column;
            }
        }

        return assignedColumns;
    }

    private static double KnownScore(EvidentiaCandidate candidate) => candidate.Evidence
        .Where(evidence => evidence.Kind == EvidentiaEvidenceKind.KnownRendering)
        .Max(evidence => evidence.Score);

    private static double PositionScore(EvidentiaCandidate candidate, int sourceCount, int targetCount)
    {
        var sourceFraction = sourceCount <= 1 ? 0.5 : (double)(candidate.Source.Token.Position - 1) / (sourceCount - 1);
        var targetFraction = targetCount <= 1 ? 0.5 : (double)(candidate.Target.Token.Position - 1) / (targetCount - 1);
        return 1 - Math.Abs(sourceFraction - targetFraction);
    }

    private sealed record RankedChoice(EvidentiaCandidate Candidate, double KnownScore, double PositionScore);

    private sealed record RankedChoiceGroup(IReadOnlyList<EvidentiaCandidate> Candidates, double KnownScore);
}

internal sealed record EvidentiaProposalPolicy(
    string Name,
    double MinimumKnownRenderingEvidence,
    double MinimumLeadOverAlternative,
    EvidentiaProposalKind Kind);
