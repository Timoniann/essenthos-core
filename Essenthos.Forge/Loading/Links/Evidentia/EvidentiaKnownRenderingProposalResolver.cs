namespace Essenthos.Core.Loading.Links.Evidentia;

/// <summary>
/// Selects only stable, exact-verse rendering proposals from the candidate graph. It resolves one
/// target occurrence at most once, but does not write it: an inferred proposal is still not a
/// source-stated correspondence.
/// </summary>
internal sealed class EvidentiaKnownRenderingProposalResolver
{
    /// <summary>
    /// A learned rendering used to reach exactly 0.90 - the confidence a number printed on both
    /// sides earns - because the offset carried the highest possible lexical score past the
    /// ceiling and the ceiling was that same number. The ceiling is now a rung below it, and the
    /// offset lands the strongest rendering on the ceiling rather than clamping a whole band flat
    /// against it.
    /// </summary>
    private const double MaximumConfidence = EvidentiaDefaults.InferredRenderingCeiling;

    private const double ConfidenceOffset = MaximumConfidence
        - EvidentiaDefaults.KnownRenderingBaseScore - EvidentiaDefaults.KnownRenderingShareScore;
    private const double KnownEvidenceWeight = EvidentiaDefaults.AssignmentEvidenceWeight;
    private const double InvalidAssignmentCost = EvidentiaDefaults.InvalidAssignmentCost;

    /// <summary>
    /// What the safe tier promises: the corpus has rendered this source form at least
    /// <see cref="EvidentiaProposalPolicy.MinimumObservations"/> times outside the passage being
    /// read; it landed on this lexeme in more than <see cref="MinimumSafeShare"/> of them; that
    /// lexeme is at least <see cref="EvidentiaProposalPolicy.MinimumSenseRatio"/> times as frequent
    /// as the strongest competing one across the whole index; and nothing in this verse comes
    /// within <see cref="EvidentiaProposalPolicy.MinimumLeadOverAlternative"/> of it.
    ///
    /// The last of those four is the only one that used to be checked. Measured against the senses
    /// present in one verse, a competitor is usually simply absent and scores zero, so a form the
    /// corpus splits evenly between two lexemes was refused when both happened to stand in the
    /// verse and accepted with a lead of 0.45 when only one did - which is the ordinary case, since
    /// two lexemes rarely co-occur. The index-wide ratio is the test that reads the way the tier
    /// is described.
    ///
    /// The ratio is a ratio rather than another threshold because it does not need calibrating:
    /// twice as often as anything else is the same claim whether a form was seen four times or
    /// four hundred.
    /// </summary>
    public static readonly EvidentiaProposalPolicy Safe = new(
        "safe", MinimumKnownRenderingEvidence: MinimumSafeScore,
        MinimumLeadOverAlternative: EvidentiaDefaults.SafeRenderingLead,
        EvidentiaProposalKind.StableKnownRendering,
        MinimumObservations: EvidentiaDefaults.SafeRenderingObservations,
        MinimumSenseRatio: EvidentiaDefaults.SafeRenderingSenseRatio);

    /// <summary>
    /// The review tier keeps the index's own floor and makes no index-wide claim at all: its output
    /// is a question put to an editor, and a sense that is right here but rarer than another
    /// elsewhere is exactly the kind of question worth asking.
    /// </summary>
    public static readonly EvidentiaProposalPolicy Review = new(
        "review", MinimumKnownRenderingEvidence: EvidentiaDefaults.ReviewRenderingScore,
        MinimumLeadOverAlternative: EvidentiaDefaults.ReviewRenderingLead,
        EvidentiaProposalKind.ReviewKnownRendering,
        MinimumObservations: EvidentiaDefaults.MinimumRenderingObservations,
        MinimumSenseRatio: 0);

    private const double MinimumSafeScore = EvidentiaDefaults.SafeRenderingScore;

    /// <summary>The share <see cref="MinimumSafeScore"/> works out to, said in the unit it is about.</summary>
    private const double MinimumSafeShare =
        (MinimumSafeScore - EvidentiaDefaults.KnownRenderingBaseScore) / EvidentiaDefaults.KnownRenderingShareScore;

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
            // Counted before the auxiliary words are set aside, so refusing one word does not move
            // the relative position of every other word in the verse.
            var groups = verse
                .Where(candidate => !candidate.PlacesAnAuxiliaryWordOffItsKind)
                .GroupBy(candidate => candidate.Source.Token.Id)
                .Select(group => BestChoiceGroup(group, policy))
                .Where(group => group is not null)
                .Cast<RankedChoiceGroup>()
                .ToList();
            var frame = OccurrenceFrame.Of(groups, sourceCount, targetCount);
            var choices = groups
                .Select(group => BestChoice(group, frame))
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
            // Counted before the auxiliary words are set aside, so refusing one word does not move
            // the relative position of every other word in the verse.
            var choices = verse
                .Where(candidate => !candidate.PlacesAnAuxiliaryWordOffItsKind)
                .GroupBy(candidate => candidate.Source.Token.Id)
                .Select(group => BestChoiceGroup(group, policy))
                .Where(choice => choice is not null)
                .Cast<RankedChoiceGroup>()
                .ToList();

            foreach (var choice in GloballyAssign(choices, OccurrenceFrame.Of(choices, sourceCount, targetCount)))
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

    /// <summary>
    /// The source words whose learned rendering the policy accepts before any target is assigned,
    /// so a word left unplaced can be told apart as refused or as having lost its target.
    /// </summary>
    public IReadOnlySet<long> Admitted(
        IEnumerable<EvidentiaCandidate> candidates,
        EvidentiaProposalPolicy policy) =>
        candidates
            .Where(candidate => IsExactKnownRendering(candidate) && !candidate.PlacesAnAuxiliaryWordOffItsKind)
            .GroupBy(candidate => candidate.Source.Token.Id)
            .Where(group => BestChoiceGroup(group, policy) is not null)
            .Select(group => group.Key)
            .ToHashSet();

    private static bool IsExactKnownRendering(EvidentiaCandidate candidate) =>
        candidate.Evidence.Any(evidence => evidence.Kind == EvidentiaEvidenceKind.ExactCanonicalAddress)
        && candidate.Evidence.Any(evidence => evidence.Kind == EvidentiaEvidenceKind.KnownRendering);

    private static RankedChoice BestChoice(RankedChoiceGroup choiceGroup, OccurrenceFrame frame) =>
        choiceGroup.Candidates
            .Select(candidate => new RankedChoice(candidate, choiceGroup.KnownScore, frame.Score(candidate)))
            .OrderByDescending(candidate => candidate.PositionScore)
            .ThenBy(candidate => candidate.Candidate.Target.Token.Position)
            .First();

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

        // The in-verse lead above says only that nothing else in this verse is close. What the
        // corpus knows about the form is a separate question, and the one the tier is named for.
        if (KnownSupport(byStrong[0].Candidates) is not { } support
            || support.Observations < policy.MinimumObservations
            || support.Share < support.NextShare * policy.MinimumSenseRatio)
        {
            return null;
        }

        return new RankedChoiceGroup(byStrong[0].Candidates, byStrong[0].KnownScore);
    }

    private static IReadOnlyList<RankedChoice> GloballyAssign(
        IReadOnlyList<RankedChoiceGroup> choices,
        OccurrenceFrame frame)
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
                    + frame.Score(candidate);
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
                proposals.Add(new RankedChoice(candidate, choices[row].KnownScore, frame.Score(candidate)));
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

    private static EvidentiaEvidenceSupport? KnownSupport(IEnumerable<EvidentiaCandidate> candidates) => candidates
        .SelectMany(candidate => candidate.Evidence)
        .Where(evidence => evidence.Kind == EvidentiaEvidenceKind.KnownRendering)
        .OrderByDescending(evidence => evidence.Score)
        .Select(evidence => evidence.Support)
        .FirstOrDefault();

    /// <summary>
    /// Chooses between occurrences of one lexeme in a verse, which the learned index cannot: every
    /// occurrence carries the same evidence. Two readings of where a rendering should stand are
    /// averaged - its relative position in the verse, and its position between the nearest words
    /// on either side whose rendering occurs in the verse once only - since the first alone ignores
    /// a clause the original orders differently and the second alone follows a single misplaced
    /// neighbour.
    ///
    /// Both distances are squared, so that two occurrences of a lexeme are not crossed when they
    /// can be kept in order (<em>the waters from the waters</em>). Measured linearly, two words on
    /// the same side of both occurrences score the same whichever occurrence each takes, and the
    /// assignment chose the crossing or the parallel reading by the order it met them.
    /// </summary>
    private sealed class OccurrenceFrame
    {
        private readonly List<(int Source, int Target)> anchors;
        private readonly int targetCount;
        private readonly double slope;

        private OccurrenceFrame(List<(int Source, int Target)> anchors, int sourceCount, int targetCount)
        {
            this.anchors = anchors;
            this.targetCount = targetCount;
            slope = sourceCount <= 1 ? 1 : (double)(targetCount - 1) / (sourceCount - 1);
        }

        public static OccurrenceFrame Of(IEnumerable<RankedChoiceGroup> groups, int sourceCount, int targetCount) =>
            new([
                .. groups
                    .Where(group => group.Candidates.Select(candidate => candidate.Target.Token.Id).Distinct().Count() == 1)
                    .Select(group => (Source: group.Candidates[0].Source.Token.Position, Target: group.Candidates[0].Target.Token.Position))
                    .OrderBy(anchor => anchor.Source),
            ], sourceCount, targetCount);

        public double Score(EvidentiaCandidate candidate)
        {
            var source = candidate.Source.Token.Position;
            var target = candidate.Target.Token.Position;
            var span = Math.Max(1, targetCount - 1);
            var relative = (target - (1 + (source - 1) * slope)) / span;
            var between = (target - BetweenAnchors(source)) / span;
            return 1 - (relative * relative + between * between) / 2;
        }

        private double BetweenAnchors(int source)
        {
            var after = anchors.FindIndex(anchor => anchor.Source > source);
            var before = (after < 0 ? anchors.Count : after) - 1;
            while (before >= 0 && anchors[before].Source >= source)
            {
                before--;
            }

            return (before >= 0, after >= 0) switch
            {
                (true, true) => anchors[before].Target + (source - anchors[before].Source)
                    * (double)(anchors[after].Target - anchors[before].Target) / (anchors[after].Source - anchors[before].Source),
                (true, false) => anchors[before].Target + (source - anchors[before].Source) * slope,
                (false, true) => anchors[after].Target - (anchors[after].Source - source) * slope,
                _ => 1 + (source - 1) * slope,
            };
        }
    }

    private sealed record RankedChoice(EvidentiaCandidate Candidate, double KnownScore, double PositionScore);

    private sealed record RankedChoiceGroup(IReadOnlyList<EvidentiaCandidate> Candidates, double KnownScore);
}

/// <param name="MinimumLeadOverAlternative">
/// How far the chosen sense must be ahead of the next sense present in the same verse.
/// </param>
/// <param name="MinimumObservations">
/// How many times the corpus must have rendered the source form at all, outside the passage being
/// read, before a tier may speak about it.
/// </param>
/// <param name="MinimumSenseRatio">
/// How many times as frequent as the strongest competing sense, across the whole index, the chosen
/// one must be.
/// </param>
internal sealed record EvidentiaProposalPolicy(
    string Name,
    double MinimumKnownRenderingEvidence,
    double MinimumLeadOverAlternative,
    EvidentiaProposalKind Kind,
    int MinimumObservations = EvidentiaDefaults.MinimumRenderingObservations,
    double MinimumSenseRatio = 1);
