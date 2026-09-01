using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Essenthos.Core.Database.Entities.Enums;
using Microsoft.EntityFrameworkCore;

namespace Essenthos.Core.Database.Entities;

/// <summary>
/// The same statement one level up, and stored rather than derived, because verse correspondence is
/// what constrains word alignment and therefore has to exist first. Chapters need nothing of their
/// own: a chapter is a range of verses, so chapter correspondence is a query over these.
/// </summary>
[Index(nameof(FromTextId), nameof(ToTextId))]
public class VerseLink
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    public int FromTextId { get; set; }

    public Text? FromText { get; set; }

    public int ToTextId { get; set; }

    public Text? ToText { get; set; }

    public LinkRelation Relation { get; set; }

    public LinkMethod Method { get; set; }

    public double? Confidence { get; set; }

    public required string Source { get; set; }

    public string? Note { get; set; }

    public ICollection<VerseLinkVerse> Verses { get; set; } = [];
}
