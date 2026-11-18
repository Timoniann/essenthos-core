using Microsoft.EntityFrameworkCore;

namespace Essenthos.Core.Database.Entities;

/// <summary>
/// A Strong number a list of verses marks on a word: the word's own number, and the number of each word of an edition of an
/// original (any text that is not a translation, not only the one a Strong page counts over) that a renders or equals link
/// joins it to, on every word of a translation and on a word of an original only where it carries no number of its own.
/// Loaded by the build from <c>texts/&lt;SLUG&gt;/numbers</c>. Keyed by verse first, so the words a list of verses marks for a number are one
/// read of the verses asked. A word no such link reaches, and a word reached only through another translation's own tags,
/// carries no row and is marked nowhere.
/// </summary>
[PrimaryKey(nameof(VerseId), nameof(WordId), nameof(StrongNumber))]
public class WordRenderedStrong
{
    public int VerseId { get; set; }

    public long WordId { get; set; }

    public required string StrongNumber { get; set; }
}
