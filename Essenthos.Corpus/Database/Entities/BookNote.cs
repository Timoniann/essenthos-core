using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Essenthos.Core.Database.Entities.Enums;
using Microsoft.EntityFrameworkCore;

namespace Essenthos.Core.Database.Entities;

/// <summary>
/// A note an edition printed on a book's title rather than beside any verse: Kulish marks
/// <em>ПЕРВА КНИГА ЦАРІВ</em> with "Зветься також: Третя книга царів". The book is the whole anchor.
/// Hanging it on the first verse would make it a remark about that verse, which the edition never
/// made.
/// </summary>
[Index(nameof(BookId), nameof(Position), IsUnique = true)]
public class BookNote
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    /// <summary>The book as this text has it, so the text is implied.</summary>
    public int BookId { get; set; }
    public Book? Book { get; set; }

    /// <summary>One-based order on the title, as the edition printed them.</summary>
    public int Position { get; set; }

    public VerseNoteKind Kind { get; set; }

    public string Content { get; set; } = string.Empty;
}
