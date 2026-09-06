using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json;
using Essenthos.Core.Database.Entities.Enums;
using Microsoft.EntityFrameworkCore;

namespace Essenthos.Core.Database.Entities;

/// <summary>
/// A second analysis of a word somebody else made, and what made it.
///
/// <see cref="Word.Morphology"/> means *the text this word belongs to carries this annotation*.
/// BHSA's features are BHSA's, Nestle's are Nestle's, and a reader who asks what a word is is told
/// what its own edition says. That is the right answer and it has one weakness: with one analysis
/// there is no way to tell a fact from a mistake in it. Nestle's <c>case</c> attribute held a
/// gender for 20,772 words and nothing in the corpus could notice, because nothing else had an
/// opinion.
///
/// <para>
/// So a second opinion lives here rather than being merged into the first. Nothing is overwritten
/// and nothing is averaged: both stand, side by side, and where they disagree the disagreement is
/// countable instead of being a silent decision somebody's loader took. Where the second says
/// something the first does not — a degree, a middle where the first could only say middle-or-
/// passive — that is a gain the reader can be shown as coming from a named source.
/// </para>
///
/// <para>
/// The provenance rules are the links': an analysis a source stated about *this* word carries no
/// confidence, one that reached this word through an inference carries one, and every row names
/// what produced it. MorphGNT is the case that made that necessary — it parses the SBLGNT and this
/// corpus holds Nestle 1904, so every row is Tauber's statement about a word of a different edition
/// plus our inference that it is the same word.
/// </para>
/// </summary>
[Index(nameof(WordId))]
[Index(nameof(WordId), nameof(Source), IsUnique = true)]
public class WordParsing
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public long Id { get; set; }

    public long WordId { get; set; }

    public Word? Word { get; set; }

    /// <summary>
    /// The lemma this analysis gives the word, where it gives one — a claim of its own, and not
    /// always the same as the lemma the word's own text carries.
    /// </summary>
    public string? Lemma { get; set; }

    /// <summary>
    /// The features, in the same vocabulary and the same keys <see cref="Word.Morphology"/> uses,
    /// so the two can be compared without a translation table between them. jsonb for the reason
    /// that field gives: a second source has features the first does not, and a third will have
    /// others again.
    /// </summary>
    [Column(TypeName = "jsonb")]
    public required JsonDocument Morphology { get; set; }

    public LinkMethod Method { get; set; }

    /// <summary>
    /// How sure, between 0 and 1, and null exactly when a source stated it of this word. The
    /// database enforces both directions.
    /// </summary>
    public double? Confidence { get; set; }

    public required string Source { get; set; }

    /// <summary>
    /// What the reasoning was, where it is worth reading — for a word the two editions spell
    /// differently, the two spellings, so a reader can see what was joined to what.
    /// </summary>
    public string? Note { get; set; }

    public override string ToString() => $"WordParsing({Source} for word {WordId}, {Method})";
}
