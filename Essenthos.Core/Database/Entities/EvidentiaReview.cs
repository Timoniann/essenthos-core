using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Essenthos.Core.Database.Entities.Enums;
using Microsoft.EntityFrameworkCore;

namespace Essenthos.Core.Database.Entities;

/// <summary>
/// A person's verdict on one decision, and whether it has reached the corpus.
///
/// <para>
/// A decision with no review is waiting, which is what makes the queue a query rather than a second
/// list to keep in step. Only an approved or corrected review is ever written as a link, and only
/// by the command that writes links.
/// </para>
///
/// <para>
/// **<see cref="Examined"/> is the difference between a person's claim and a rule's.** Approving a
/// proposal after reading it is a person stating the correspondence, and it is written as
/// <see cref="LinkMethod.Manual"/>. Accepting a whole tier is a decision about the rule, not about
/// the words, so what it writes is the rule's own claim with its confidence — a heuristic that a
/// person let through must not read as one a person checked.
/// </para>
/// </summary>
[Index(nameof(DecisionId), IsUnique = true)]
[Index(nameof(LinkId))]
[Index(nameof(CorrectedTargetWordId))]
public class EvidentiaReview
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public long Id { get; set; }

    public long DecisionId { get; set; }

    public EvidentiaDecision? Decision { get; set; }

    public EvidentiaVerdict Verdict { get; set; }

    /// <summary>Whether the reviewer read this decision, rather than accepting a tier it belonged to.</summary>
    public bool Examined { get; set; }

    /// <summary>The word the reviewer placed the source word on instead; set exactly for a correction.</summary>
    public long? CorrectedTargetWordId { get; set; }

    public Word? CorrectedTargetWord { get; set; }

    public required string Reviewer { get; set; }

    public DateTimeOffset ReviewedAt { get; set; }

    public string? Note { get; set; }

    /// <summary>The link the verdict was written onto, whether new or already there.</summary>
    public long? LinkId { get; set; }

    public Link? Link { get; set; }

    public DateTimeOffset? AppliedAt { get; set; }

    /// <summary>Why an approved verdict was not written, where the corpus already outranks it.</summary>
    public string? Withheld { get; set; }

    public override string ToString() => $"EvidentiaReview({Verdict} on decision {DecisionId})";
}
