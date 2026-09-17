using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Essenthos.Core.Database.Entities.Enums;
using Microsoft.EntityFrameworkCore;

namespace Essenthos.Core.Database.Entities;

/// <summary>
/// A directed claim about how one text stands to another. Two rows say what a single
/// "textual basis" column said uselessly: the King James is translated from the Masoretic in the
/// Old Testament and from the Textus Receptus in the New.
/// </summary>
[Index(nameof(FromTextId), nameof(ToTextId))]
public class TextRelation
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    public int FromTextId { get; set; }
    public Text? FromText { get; set; }

    public int ToTextId { get; set; }
    public Text? ToText { get; set; }

    public TextRelationKind Relation { get; set; }

    /// <summary>Null for the whole Bible, otherwise the book range the claim is limited to.</summary>
    public string? Scope { get; set; }

    public string? Note { get; set; }

    /// <summary>Where the claim comes from.</summary>
    public string? Source { get; set; }
}
