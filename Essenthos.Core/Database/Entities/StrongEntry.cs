using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Essenthos.Core.Database.Entities;

/// <summary>
/// One entry of Strong's concordance, which is what a Strong number on a word points at.
///
/// It belongs to no text. A number is a claim about a lexeme rather than about an occurrence —
/// H430 is <em>elohim</em> wherever it stands, in BHSA and in a Greek edition tagged from the same
/// concordance — so the entry is a fact about the language, and the word rows reach it by number
/// rather than by a foreign key. That is deliberate: 121,077 words in this corpus carry a number
/// that has no entry and never will, because ETCBC assigns the H9000 range to prefix morphemes
/// Strong never catalogued, and a foreign key would make those words unloadable.
/// </summary>
[Index(nameof(StrongNumber), IsUnique = true)]
public class StrongEntry
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    /// <summary>A language letter and the number, unpadded: <c>H430</c>, <c>G26</c>.</summary>
    public required string StrongNumber { get; set; }

    public string? Lemma { get; set; }

    public string? Transliteration { get; set; }

    public string? Pronunciation { get; set; }

    /// <summary>The short gloss, which is what a reader hovering a word wants.</summary>
    public string? Definition { get; set; }

    /// <summary>Where the lexeme comes from, in Strong's own words.</summary>
    public string? Derivation { get; set; }

    /// <summary>
    /// How the King James renders it, as the concordance lists the renderings. This is the closest
    /// thing the dictionary has to the question the corpus is really for.
    /// </summary>
    public string? KjvDefinition { get; set; }

    public string? Morphology { get; set; }

    public string? DetailedDefinition { get; set; }

    public string? SeeAlso { get; set; }

    public string? SourceLanguage { get; set; }

    /// <summary>Its entry in the Theological Wordbook of the Old Testament, where it has one.</summary>
    public string? TwotReference { get; set; }

    public override string ToString() => $"StrongEntry({StrongNumber}, {Lemma})";
}
