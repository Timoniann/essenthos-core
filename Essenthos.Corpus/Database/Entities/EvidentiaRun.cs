using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json;

namespace Essenthos.Core.Database.Entities;

/// <summary>
/// One pass of EVIDENTIA over a pair of texts, kept so that what it decided can be read, ranked,
/// reviewed and compared with the next pass. A run writes nothing to the corpus: its decisions are
/// proposals, and only a reviewed one reaches <see cref="Link"/>.
///
/// <para>
/// The configuration is JSON for the reason <see cref="VerificationRun.Measures"/> is: it is what
/// the run was allowed to read and the thresholds it read them with, recorded so the run can be
/// repeated, and nothing joins to it.
/// </para>
/// </summary>
public class EvidentiaRun
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    public int FromTextId { get; set; }

    public Text? FromText { get; set; }

    public int ToTextId { get; set; }

    public Text? ToText { get; set; }

    /// <summary>The run this one repeats for a chosen set of verses, after something was fixed.</summary>
    public int? ParentRunId { get; set; }

    public EvidentiaRun? ParentRun { get; set; }

    public DateTimeOffset StartedAt { get; set; }

    /// <summary>Null while the run is still writing, so a half-written run cannot be mistaken for a whole one.</summary>
    public DateTimeOffset? FinishedAt { get; set; }

    /// <summary>The build that ran, with its commit where the build knows it.</summary>
    public required string RuleVersion { get; set; }

    public required JsonDocument Configuration { get; set; }

    /// <summary>The books, chapters or verses the run covered.</summary>
    public required JsonDocument Scope { get; set; }

    public ICollection<EvidentiaDecision> Decisions { get; set; } = [];

    public override string ToString() => $"EvidentiaRun({Id}, {FromTextId} to {ToTextId}, {RuleVersion})";
}
