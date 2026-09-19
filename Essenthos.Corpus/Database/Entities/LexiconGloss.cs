using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Essenthos.Core.Database.Entities;

/// <summary>
/// A lexicon's gloss for one dictionary form: the meaning in a word or two, as its editors wrote it,
/// filed under the Strong number the lexicon gives the entry.
///
/// <para>
/// A row per form rather than per entry, because an entry can print more than one — <c>ἄρρην,
/// ἄρσην</c> is one lexeme spelled two ways — and a word reaches a gloss by whichever form its own
/// lemma is. Several entries can share a form and a number, and each keeps its own row: a lexicon
/// that tells two senses apart has made a distinction, and collapsing them here would be choosing
/// between them for it.
/// </para>
///
/// <para>
/// Nothing here says which word of which text a gloss belongs to. That is concluded when a word is
/// read, by its lemma or its number, and the reading says which way it was reached — a gloss found
/// through a number the corpus proposed is not the same claim as one found through a number the
/// edition prints.
/// </para>
/// </summary>
[Index(nameof(Lemma))]
[Index(nameof(StrongNumber))]
[Index(nameof(Entry), nameof(Lemma), IsUnique = true)]
public class LexiconGloss
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    /// <summary>
    /// The lexicon's own identifier for the entry, as it prints it: <c>G0001G</c>, where the letter
    /// tells apart entries sharing a number.
    /// </summary>
    public required string Entry { get; set; }

    /// <summary>The number the entry is filed under, as the corpus writes one: <c>G1</c>, <c>G20001</c>.</summary>
    public required string StrongNumber { get; set; }

    /// <summary>One dictionary form of the entry, in Unicode NFC, which is how every lemma in the corpus is held.</summary>
    public required string Lemma { get; set; }

    public required string Gloss { get; set; }

    /// <summary>Which lexicon, whose, and under what terms. Never empty.</summary>
    public required string Source { get; set; }

    public override string ToString() => $"LexiconGloss({Entry} {Lemma}: {Gloss})";
}
