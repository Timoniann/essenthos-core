using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Essenthos.Core.Database.Entities;

/// <summary>
/// One article of a scholarly lexicon (Brown-Driver-Briggs, Abbott-Smith, Liddell-Scott-Jones) under the Strong number
/// it is filed at.
///
/// <para>
/// A lexicon divides words its own way, so one Strong number may stand over several articles (BDB gives בָּרָא "shape"
/// and בָּרָא "be fat" apart, both H1254) and one article over several numbers; each pairing is a row, and the number's
/// articles keep the lexicon's order. The number is the lexicon's own keying as its source states it, never inferred
/// here. The article is kept whole as the lexicon prints it: blocks of marked text, its Hebrew and Greek marked as such,
/// the verses it cites and the Strong numbers it names.
/// </para>
/// </summary>
[Index(nameof(StrongNumber), nameof(Dictionary), nameof(Ordinal), IsUnique = true)]
public class LexiconArticle
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    /// <summary>Which lexicon, as <see cref="Corpus.LexiconWorks"/> names it.</summary>
    public required string Dictionary { get; set; }

    /// <summary>A language letter and the number, unpadded, as <see cref="StrongEntry.StrongNumber"/>: <c>H430</c>, <c>G26</c>.</summary>
    public required string StrongNumber { get; set; }

    /// <summary>Its place among this lexicon's articles under the number, from 1.</summary>
    public int Ordinal { get; set; }

    /// <summary>The lexicon's own identifier for the article: BDB's <c>a.dl.ad</c>, Abbott-Smith's <c>ἀγάπη|G26</c>, STEP's <c>G0026</c>.</summary>
    public required string Entry { get; set; }

    /// <summary>The word the article is headed by, as the lexicon prints it.</summary>
    public required string Headword { get; set; }

    /// <summary>
    /// How far the digital edition has got with this article, in its own word, where it says: the Open Scriptures BDB
    /// marks <c>base</c> (the basic definitions), <c>ref</c> (references added, not yet whole) and <c>done</c>.
    /// </summary>
    public string? Status { get; set; }

    /// <summary>The article as blocks of marked text, JSON in the shape commentaries use, handed to the reader whole.</summary>
    public required string Body { get; set; }

    /// <summary>The lexicon's credit, which the dataset declaration counts the row under.</summary>
    public required string Source { get; set; }

    public override string ToString() => $"LexiconArticle({Dictionary} {StrongNumber} #{Ordinal} {Headword})";
}
