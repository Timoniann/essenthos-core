using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Essenthos.Core.Database.Entities.Enums;
using Microsoft.EntityFrameworkCore;

namespace Essenthos.Core.Database.Entities;

/// <summary>
/// A note an edition printed on a chapter's own heading rather than beside any verse: Biblica's
/// Ukrainian marks <em>Псалом 9 (9–10)</em> with how its numbering differs from the Hebrew, and
/// addresses the note as verse 0 of the psalm. The chapter is the whole anchor; hanging it on the
/// first verse would make it a remark about that verse, which the edition never made.
/// </summary>
[Index(nameof(ChapterId), nameof(Position), IsUnique = true)]
public class ChapterNote
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    /// <summary>The chapter as this text numbers it, so the text and the book are implied.</summary>
    public int ChapterId { get; set; }
    public Chapter? Chapter { get; set; }

    /// <summary>One-based order over the chapter, as the edition printed them.</summary>
    public int Position { get; set; }

    /// <summary>The heading the note was printed on, as the edition prints it: "Псалом 9 (9–10)", "א Алеф".</summary>
    public string Heading { get; set; } = string.Empty;

    public VerseNoteKind Kind { get; set; }

    public string Content { get; set; } = string.Empty;
}
