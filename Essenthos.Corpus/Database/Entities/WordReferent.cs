using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Essenthos.Core.Database.Entities.Enums;
using Microsoft.EntityFrameworkCore;

namespace Essenthos.Core.Database.Entities;

/// <summary>
/// A word that refers back to another word of its text, and who says so: the pronoun αὐτοῦ and the Νῶε it stands for.
///
/// <para>
/// It is not a name. <see cref="WordEntity"/> says a word names a record; this says a word points at another word, and the
/// record, if there is one, is whatever that word names. Kept apart so a reader is never shown a pronoun as if the text
/// had written the name there, and so the reference stays true when the naming of its antecedent changes.
/// </para>
///
/// <para>
/// One row per word referred to: a pronoun standing for two people has two rows. The word referred to is often in an
/// earlier verse, which the reference is allowed to cross. A word whose antecedent is itself a pronoun points at that
/// pronoun, as the source writes it; following the chain is the reader's work.
/// </para>
/// </summary>
[Index(nameof(WordId), nameof(ReferentWordId), nameof(Source), IsUnique = true)]
[Index(nameof(ReferentWordId))]
public class WordReferent
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public long Id { get; set; }

    public long WordId { get; set; }

    public Word? Word { get; set; }

    public long ReferentWordId { get; set; }

    public Word? ReferentWord { get; set; }

    public LinkMethod Method { get; set; }

    /// <summary>Null exactly when a source stated it, as on every other annotation.</summary>
    public double? Confidence { get; set; }

    public required string Source { get; set; }

    public override string ToString() => $"WordReferent(word {WordId} refers to word {ReferentWordId}, {Method})";
}
