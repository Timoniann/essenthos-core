using Microsoft.EntityFrameworkCore;

namespace Essenthos.Core.Database.Entities;

/// <summary>
/// One word carrying a Strong number, keyed in the order a list of the number's places runs: the text's slug, the place
/// its verse stands in the shared frame (the primary reference), its position, its id. Written by the build for every
/// numbered word, so a page of places is a range of one index instead of every word of the number sorted as it is asked.
/// </summary>
[PrimaryKey(nameof(StrongNumber), nameof(Corpus), nameof(Book), nameof(Chapter), nameof(Verse), nameof(Position), nameof(WordId))]
public class StrongOccurrence
{
    public required string StrongNumber { get; set; }

    /// <summary>The text's slug, which the list is ordered by.</summary>
    public required string Corpus { get; set; }

    /// <summary>The frame's book, chapter and verse of the word's verse.</summary>
    public int Book { get; set; }

    public int Chapter { get; set; }

    public int Verse { get; set; }

    public int Position { get; set; }

    public long WordId { get; set; }

    /// <summary>The text, by id, which a build placing one text again replaces the rows of.</summary>
    public int TextId { get; set; }
}
