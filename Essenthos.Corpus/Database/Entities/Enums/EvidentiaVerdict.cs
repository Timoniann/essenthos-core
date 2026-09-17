namespace Essenthos.Core.Database.Entities.Enums;

/// <summary>What a reviewer said about one EVIDENTIA decision.</summary>
public enum EvidentiaVerdict
{
    Approved,
    Rejected,

    /// <summary>The source word renders a different target word, which the review names.</summary>
    Corrected,
}

/// <summary>
/// Why a run placed a content word nowhere, as far as the candidate graph can say without asking
/// the resolvers: they report what they chose, not what they declined.
/// </summary>
public enum EvidentiaAbstention
{
    /// <summary>No evidence reached any target word.</summary>
    NoCandidate,

    /// <summary>The best candidate's target word went to another source word's proposal.</summary>
    TargetTaken,

    /// <summary>Candidates exist and their targets were free; the policy did not trust any of them.</summary>
    Declined,
}
