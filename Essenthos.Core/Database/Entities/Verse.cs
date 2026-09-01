using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Essenthos.Core.Database.Entities;

/// <summary>
/// A verse in one text's own numbering. Where that numbering sits in the shared frame is
/// <see cref="VerseReference"/>, and the two are not the same thing: pairing verses by number
/// across texts is what made the split view show different passages in its two panes.
/// </summary>
[Index(nameof(TextId), nameof(BookId), nameof(ChapterNumber), nameof(Number), IsUnique = true)]
public class Verse
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    public int TextId { get; set; }
    public Text? Text { get; set; }

    public int BookId { get; set; }
    public Book? Book { get; set; }

    public int ChapterId { get; set; }
    public Chapter? Chapter { get; set; }

    /// <summary>Denormalised from the chapter so that addressing a verse needs no join.</summary>
    public int ChapterNumber { get; set; }

    public int Number { get; set; }

    public ICollection<Word> Words { get; set; } = [];

    public ICollection<VerseReference> References { get; set; } = [];

    public override string ToString() => $"Verse(text {TextId}, book {BookId}, {ChapterNumber}:{Number})";
}
