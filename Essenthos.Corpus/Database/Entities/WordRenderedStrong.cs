using Microsoft.EntityFrameworkCore;

namespace Essenthos.Core.Database.Entities;

/// <summary>
/// A Strong number a word of a text carries or renders: the word's own number, and each number of the edition of an original
/// the text is counted over (<c>texts/&lt;SLUG&gt;/index/strong</c>, the same rows as <see cref="StrongRendering"/>) whose
/// word a rendering link of the text joins it to. Keyed by verse first, so the words a list of verses marks for a number are
/// one read of the verses asked. A word no such link reaches carries no row and is marked nowhere.
/// </summary>
[PrimaryKey(nameof(VerseId), nameof(WordId), nameof(StrongNumber))]
public class WordRenderedStrong
{
    public int VerseId { get; set; }

    public long WordId { get; set; }

    public required string StrongNumber { get; set; }
}
