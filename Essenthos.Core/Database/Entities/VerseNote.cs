using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Essenthos.Core.Database.Entities.Enums;
using Microsoft.EntityFrameworkCore;

namespace Essenthos.Core.Database.Entities;

/// <summary>
/// A source-attributed note printed beside one verse. It is deliberately not a commentary: the
/// edition supplied it with its text, in its language, and no editor of this product wrote it.
/// </summary>
[Index(nameof(VerseId), nameof(Position), IsUnique = true)]
public class VerseNote
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    public int VerseId { get; set; }
    public Verse? Verse { get; set; }

    /// <summary>
    /// The source word immediately before the note marker, where the edition supplies that
    /// placement. It is null for a verse-level marker and does not assert that the note explains
    /// only this word.
    /// </summary>
    public long? AnchorWordId { get; set; }
    public Word? AnchorWord { get; set; }

    /// <summary>One-based order in the verse, because the order is how the edition printed them.</summary>
    public int Position { get; set; }

    public VerseNoteKind Kind { get; set; }

    public string Content { get; set; } = string.Empty;
}
