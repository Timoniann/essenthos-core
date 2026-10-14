namespace Essenthos.Core.Loading.Links.Evidentia;

/// <summary>What the pivot is allowed to change.</summary>
[Flags]
internal enum EvidentiaPivotPolicy
{
    None = 0,

    /// <summary>A placement every pivot that names the word contradicts is withdrawn.</summary>
    Withhold = 1,

    /// <summary>A placement a pivot agrees with goes into the safe tier.</summary>
    Safe = 2,
}

/// <summary>
/// The pivot is a second opinion on what EVIDENTIA concluded from the words themselves, never a placement
/// of its own: where the two name the same pair the placement is right far more often than either alone,
/// and where every pivot names another word EVIDENTIA's own choice is right about half the time.
///
/// Placing an unplaced word where a second pivot or the word's own lexical evidence agreed with the pivot
/// was measured and is not done: those placements were right 84% of the time on the held-out passages and
/// 82% on the sealed ones, under the 95% every kind of link is held to.
/// </summary>
internal static class EvidentiaPivotCheck
{
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
}
