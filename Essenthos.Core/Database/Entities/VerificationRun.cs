using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json;

namespace Essenthos.Core.Database.Entities;

/// <summary>
/// What one load measured about the corpus it had just written.
///
/// A row per load, because the number that matters is rarely the number itself — it is whether it
/// moved. A corpus that reached 84% of the Synodal's words yesterday and 79% today has lost
/// something, and nothing else in the system would notice.
///
/// The measures are held as JSON rather than columns because they are a report, not a schema: a
/// fifth measure should not be a migration, and nothing joins to them. The two columns beside it
/// are the ones worth ordering and alerting on.
/// </summary>
public class VerificationRun
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    public DateTimeOffset RanAt { get; set; }

    /// <summary>
    /// How many integrity checks found something. Zero is the only acceptable answer, which is why
    /// it is a column: it is what a health endpoint and a build both ask first.
    /// </summary>
    public int Broken { get; set; }

    /// <summary>
    /// The share of translated words that reach a witness, over the whole corpus. One number for
    /// "is the corpus better or worse than last time", ahead of reading the report.
    /// </summary>
    public double Rendered { get; set; }

    public required JsonDocument Measures { get; set; }

    public override string ToString() => $"VerificationRun({RanAt:u}, {Broken} broken, {Rendered:P1} rendered)";
}
