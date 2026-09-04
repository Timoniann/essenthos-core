using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Essenthos.Core.Database.Entities;

/// <summary>
/// A verse in one text's own numbering. Where that numbering sits in the shared frame is
/// <see cref="VerseReference"/>, and the two are not the same thing: pairing verses by number
/// across texts is what made the split view show different passages in its two panes.
/// </summary>
[Index(nameof(TextId), nameof(BookId), nameof(ChapterNumber), nameof(Number), nameof(Label), IsUnique = true)]
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

    /// <summary>
    /// The letter this edition prints after the number, where it prints one — <c>a</c> in the
    /// Septuagint's Genesis 31:50a, which is material the Hebrew does not have and which the Greek
    /// numbers by extending 50 rather than by inventing a 51.
    ///
    /// Empty for an ordinary verse, and empty rather than null so that the uniqueness of a verse
    /// within its chapter is actually enforced: Postgres treats nulls in a unique index as all
    /// different from one another.
    /// </summary>
    public string Label { get; set; } = string.Empty;

    public ICollection<Word> Words { get; set; } = [];

    public ICollection<VerseReference> References { get; set; } = [];

    /// <summary>
    /// The addresses the edition prints for this verse in its own numbering, where it prints any.
    /// Empty for almost every verse of the corpus and for every text but the two whose publisher
    /// renumbered them.
    /// </summary>
    public ICollection<StatedVerseNumber> StatedNumbers { get; set; } = [];

    public override string ToString() => $"Verse(text {TextId}, book {BookId}, {ChapterNumber}:{Number})";
}
