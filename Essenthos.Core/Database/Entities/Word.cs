using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace Essenthos.Core.Database.Entities;

/// <summary>
/// One word of one text. <see cref="Surface"/> and <see cref="Trailer"/> reproduce the source
/// exactly: concatenating a verse's words in order must give the source verse back, which is the
/// property whose absence let the Greek lose the last letter of 19,740 words and the English lose
/// the space after punctuation in 72,277.
/// </summary>
[Index(nameof(VerseId), nameof(Position), IsUnique = true)]
[Index(nameof(TextId), nameof(StrongNumber))]
[Index(nameof(TextId), nameof(NormalisedText))]

// The concordance asks for a number across every text at once, and the composite index cannot
// answer that without its leading column. A million-row scan per lookup is the alternative.
[Index(nameof(StrongNumber))]
public class Word
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public long Id { get; set; }

    public int TextId { get; set; }
    public Text? Text { get; set; }

    public int VerseId { get; set; }
    public Verse? Verse { get; set; }

    /// <summary>Its index within the verse, from 1.</summary>
    public int Position { get; set; }

    [Column("text")]
    public required string Surface { get; set; }

    /// <summary>Whatever separates this word from the next, punctuation and space included.</summary>
    public required string Trailer { get; set; }

    public string? Lemma { get; set; }

    /// <summary><c>H####</c> or <c>G####</c>.</summary>
    public string? StrongNumber { get; set; }

    public string? Gloss { get; set; }

    /// <summary>
    /// The annotation this text happens to carry. BHSA has features Nestle does not and the
    /// Peshitta will have others again, so this is jsonb rather than a column per witness; index
    /// the few keys that are actually queried.
    /// </summary>
    [Column(TypeName = "jsonb")]
    public JsonDocument? Morphology { get; set; }

    /// <summary>Diacritics folded and case lowered, for search. Folding happens in Postgres.</summary>
    public string? NormalisedText { get; set; }

    public override string ToString() => $"Word({Surface}, verse {VerseId}, position {Position})";
}
