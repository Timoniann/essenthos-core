using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Essenthos.Core.Database.Entities;

/// <summary>
/// A book as one text has it. Two texts each have their own Genesis, which is what lets a second
/// Hebrew or Greek witness exist at all.
/// </summary>
[Index(nameof(TextId), nameof(CanonicalOrdinal), IsUnique = true)]
[Index(nameof(TextId), nameof(Slug), IsUnique = true)]
[Index(nameof(TextId), nameof(Position))]
public class Book
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    public int TextId { get; set; }
    public Text? Text { get; set; }

    /// <summary>
    /// Its place in the canonical order, 1 to 66, shared by every text. This is the number that
    /// identifies the book across texts.
    /// </summary>
    public int CanonicalOrdinal { get; set; }

    /// <summary>
    /// Its order within this text, which is not the canonical one: BHSA orders the Tanakh so that
    /// its eighth book is 1 Samuel where the canonical eighth is Ruth. Reading one as the other is
    /// a defect the old schema shipped.
    /// </summary>
    public int Position { get; set; }

    public required string Name { get; set; }

    public string? NameNative { get; set; }

    public string? Abbreviation { get; set; }

    public required string Slug { get; set; }

    public ICollection<Chapter> Chapters { get; set; } = [];

    public override string ToString() => $"Book({Name}, canonical {CanonicalOrdinal}, text {TextId})";
}
