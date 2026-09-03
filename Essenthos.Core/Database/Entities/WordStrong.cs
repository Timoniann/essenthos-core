using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Essenthos.Core.Database.Entities.Enums;
using Microsoft.EntityFrameworkCore;

namespace Essenthos.Core.Database.Entities;

/// <summary>
/// A Strong number somebody or something proposes for a word, and what proposed it.
///
/// <see cref="Word.StrongNumber"/> means *the source states this*. BHSA states its numbers, Nestle
/// states its own, all three Textus Receptus editions state theirs — and every method downstream
/// reads that column as testimony, which is why the Strong join is the most trusted inference the
/// corpus makes. An inference written into the same column would be indistinguishable from a
/// statement, and telling those apart is the thing this schema exists to do.
///
/// <para>
/// So a proposal lives here instead, with the same provenance rules the links live under: a number a
/// source states carries no confidence, one a process inferred carries one, and every row names what
/// produced it. The Septuagint is the case that made this necessary — Strong never numbered the
/// Greek Old Testament, so a number on a Brenton word is always our reasoning from a GLAUx lemma and
/// never Strong's own claim.
/// </para>
///
/// <para>
/// **Several rows per word is the point, not a defect.** One word may be proposed a number by its
/// lemma, another by the aligner's Hebrew counterpart, another by a hand correction, and where they
/// agree that agreement is worth more than any of them alone. When enough independent methods agree
/// on a word, the settled number is promoted into <see cref="Word.StrongNumber"/> and the reasoning
/// stays here — which is the migration path the owner asked for, and the reason the table holds
/// candidates rather than one answer.
/// </para>
/// </summary>
[Index(nameof(WordId))]
[Index(nameof(Number))]
[Index(nameof(WordId), nameof(Number), nameof(Method), IsUnique = true)]
public class WordStrong
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public long Id { get; set; }

    public long WordId { get; set; }

    public Word? Word { get; set; }

    /// <summary>
    /// <c>H####</c> or <c>G####</c>, unpadded, as <see cref="StrongEntry.StrongNumber"/> writes it.
    /// No foreign key, for the reason <see cref="StrongEntry"/> gives: ETCBC's H9000 range names
    /// prefix morphemes Strong never catalogued, and a key would make those words unloadable.
    /// </summary>
    public required string Number { get; set; }

    public LinkMethod Method { get; set; }

    /// <summary>
    /// How sure, between 0 and 1, and null exactly when a source stated it. The database enforces
    /// both directions.
    /// </summary>
    public double? Confidence { get; set; }

    public required string Source { get; set; }

    /// <summary>What the reasoning was, where it is worth reading — the lemma it went through.</summary>
    public string? Note { get; set; }

    public override string ToString() => $"WordStrong({Number} for word {WordId}, {Method})";
}
