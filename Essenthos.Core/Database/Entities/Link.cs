using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Essenthos.Core.Database.Entities.Enums;
using Microsoft.EntityFrameworkCore;

namespace Essenthos.Core.Database.Entities;

/// <summary>
/// A statement that some words of one text correspond to some words of another. It names a set on
/// each side, so two Hebrew words rendering one Greek word are a single link naming all three —
/// which is not the same claim as the two Hebrew words being each other, and that difference is why
/// a word identifier shared across texts cannot work.
///
/// Word order never enters, and neither do verse boundaries: a link may name words in two verses,
/// which makes a word ending up elsewhere expressible rather than a defect.
/// </summary>
[Index(nameof(FromTextId), nameof(ToTextId))]
public class Link
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public long Id { get; set; }

    /// <summary>
    /// Both texts are required. Without them a link is ambiguous the moment a second Greek witness
    /// exists, and the ambiguity cannot be recovered afterwards.
    /// </summary>
    public int FromTextId { get; set; }

    public Text? FromText { get; set; }

    public int ToTextId { get; set; }

    public Text? ToText { get; set; }

    public LinkRelation Relation { get; set; }

    public LinkMethod Method { get; set; }

    /// <summary>
    /// Null where a source or a person states the correspondence, a number in 0..1 where a process
    /// inferred it. Check constraints hold those apart, so an inference cannot be stored looking
    /// like scholarship.
    /// </summary>
    public double? Confidence { get; set; }

    /// <summary>The file, the algorithm and its version, or the person. Never empty.</summary>
    public required string Source { get; set; }

    /// <summary>For a manual link, why.</summary>
    public string? Note { get; set; }

    public ICollection<LinkWord> Words { get; set; } = [];

    public override string ToString() => $"Link({FromTextId} to {ToTextId}, {Relation}, {Method})";
}
