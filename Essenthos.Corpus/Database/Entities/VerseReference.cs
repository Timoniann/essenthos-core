using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Essenthos.Core.Database.Entities;

/// <summary>
/// Where a verse sits in the shared address space, so that Joel 3 resolves in any text. This is not
/// a link: a reference is a place, a link is a correspondence between words, and conflating the two
/// is what paired Joel 3:1 with the wrong Hebrew.
/// </summary>
[Index(nameof(CanonicalBook), nameof(CanonicalChapter), nameof(CanonicalVerse))]
public class VerseReference
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    public int VerseId { get; set; }
    public Verse? Verse { get; set; }

    public int CanonicalBook { get; set; }

    public int CanonicalChapter { get; set; }

    public int CanonicalVerse { get; set; }

    /// <summary>
    /// One row per verse is its primary placement; a verse spanning two canonical verses carries
    /// further rows that are not. A unique index enforces the one, and the verification pass
    /// enforces that no two verses of a text claim the same primary placement.
    /// </summary>
    public bool IsPrimary { get; set; }

    public override string ToString() =>
        $"VerseReference(verse {VerseId} at {CanonicalBook}.{CanonicalChapter}.{CanonicalVerse}, primary {IsPrimary})";
}
