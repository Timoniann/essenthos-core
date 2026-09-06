using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Essenthos.Core.Database.Entities.Enums;
using Microsoft.EntityFrameworkCore;

namespace Essenthos.Core.Database.Entities;

/// <summary>
/// One entry of Strong's dictionary in a language other than the one he wrote it in.
///
/// The four prose fields of <see cref="StrongEntry"/> and nothing else. The lemma, the
/// transliteration, the pronunciation, the morphology code, the see-also numbers and the TWOT
/// reference are not language, they are how a lookup finds the entry, and a translated identifier
/// breaks the lookup without saying it has. They are not here, so there is no row this could be
/// written into.
///
/// <para>
/// **A translated gloss is a new claim, not the old one in another language.** <em>Of uncertain
/// affinity</em> rendered into Ukrainian by a model is a machine's reading of Strong, and Strong's
/// own English being public domain says nothing about it. So a row carries what produced it in
/// <see cref="Source"/>, in the idiom every generated row in this corpus uses — the model, the
/// prompt version and the date of the run — and the English it renders stays where it was, on
/// <see cref="StrongEntry"/>, never overwritten. A reader is owed both, side by side.
/// </para>
///
/// <para>
/// **<see cref="KjvDefinition"/> is rendered rather than translated, and it is the weakest field
/// here.** The English is the list of words the King James actually uses, in Strong's affix
/// notation — <c>angels, [idiom] exceeding, God (gods) (-dess, -ly)</c> — and that notation is
/// English morphology. What is stored is the senses those renderings carry, with the affix
/// machinery dropped, which means it stops pointing into the King James text. That is a real loss
/// and the reason the English must be shown beside it rather than instead of it.
/// </para>
///
/// <para>
/// Keyed on the Strong number rather than by a foreign key, for <see cref="StrongEntry"/>'s own
/// reason and <see cref="StrongGentilic"/>'s: a number is a claim about a lexeme, the whole corpus
/// reaches the dictionary by number, and this table is another thing said about the same number.
/// The loader still refuses a number the dictionary does not hold, and counts the refusals.
/// </para>
/// </summary>
[Index(nameof(StrongNumber))]
[Index(nameof(Language))]
[Index(nameof(StrongNumber), nameof(Language), IsUnique = true)]
public class StrongEntryTranslation
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    /// <summary>The entry this renders, as <see cref="StrongEntry.StrongNumber"/> writes it.</summary>
    public required string StrongNumber { get; set; }

    /// <summary>The three-letter code the texts are tagged with: <c>ukr</c>, <c>rus</c>, <c>deu</c>.</summary>
    public required string Language { get; set; }

    /// <summary>The short gloss, which is what a reader hovering a word wants.</summary>
    public string? Definition { get; set; }

    /// <summary>
    /// Where the lexeme comes from. The Strong references and the quoted Hebrew and Greek inside it
    /// are addresses rather than words and are carried through unchanged — <c>from G25;</c> stays
    /// <c>from G25;</c> in every language.
    /// </summary>
    public string? Derivation { get; set; }

    /// <summary>The senses of the King James renderings. See the note on the class.</summary>
    public string? KjvDefinition { get; set; }

    /// <summary>
    /// The numbered senses, with <c>1)</c>, <c>1a)</c>, <c>1b)</c> in the same order and the same
    /// count as the English. A dropped sense number is the failure nobody sees, so the loader
    /// refuses a row that lost one.
    /// </summary>
    public string? DetailedDefinition { get; set; }

    public LinkMethod Method { get; set; }

    /// <summary>
    /// Null, on every row this corpus writes today, and the column is here rather than absent
    /// because a later reading pass over the same rows could fill it.
    ///
    /// <para>
    /// Everywhere else an inference must carry a confidence, so that a heuristic can never be read
    /// as scholarship. A translation is a different kind of claim and the guard against it is a
    /// different one: there is no candidate set to be more or less sure between, and the English it
    /// renders is on the row next to it, so a reader checks it against the source rather than
    /// against a number. Inventing a figure per row would be a number nobody measured, which is
    /// worse than none.
    /// </para>
    /// </summary>
    public double? Confidence { get; set; }

    /// <summary>The model, the prompt version and the date of the run. Never empty.</summary>
    public required string Source { get; set; }

    /// <summary>
    /// The terms the translator itself said it was unsure of, where it named any. It is the only
    /// per-row doubt the run produces and it comes from the run rather than from a rule, so it is
    /// kept as it was given rather than turned into a score.
    /// </summary>
    public string? Note { get; set; }

    public override string ToString() => $"StrongEntryTranslation({StrongNumber} {Language})";
}
