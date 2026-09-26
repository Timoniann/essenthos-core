using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Essenthos.Core.Database.Entities;

/// <summary>
/// One of the commonest phrases a translation writes for a Strong number, and how many links write it:
/// the line under each entry of the lexicon.
///
/// <para>
/// Nothing here is asserted about a word. A row is a count of the links the corpus already holds,
/// made by the same statement the entry page counts with, so it is exactly as good as those links and
/// no better. It is kept because counting it for a page of forty entries reads every link of every
/// occurrence of forty numbers — thirty thousand rows scattered over the two largest tables — and on a
/// database whose cache is cold that took longer than a request is allowed. Counted once per load, it
/// is forty rows read by one index.
/// </para>
/// </summary>
[Index(nameof(TextId), nameof(StrongNumber), nameof(Rank), IsUnique = true)]
public class StrongRendering
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    public required string StrongNumber { get; set; }

    public int TextId { get; set; }

    public Text? Text { get; set; }

    /// <summary>Where the phrase stands among this number's in this text, from 1 for the commonest.</summary>
    public int Rank { get; set; }

    /// <summary>The words of one link on the translation's side, lower case, in the order the text prints them.</summary>
    public required string Phrase { get; set; }

    /// <summary>How many links render the number with this phrase.</summary>
    public int Uses { get; set; }

    public override string ToString() => $"StrongRendering({StrongNumber} in {TextId}: #{Rank} {Phrase} ×{Uses})";
}
