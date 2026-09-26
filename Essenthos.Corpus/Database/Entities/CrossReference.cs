using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Essenthos.Core.Database.Entities;

/// <summary>
/// One verse sent to another verse, or to a run of verses, by one set of cross references.
///
/// <para>
/// Addressed in the shared frame and never by a verse row, because a set says which verses it
/// relates and not which edition's verses: the reader shows the same reference beside the King James
/// and beside the Synodal, and a verse row is renumbered by every rebuild. Both ends are canonical
/// book, chapter and verse; the far end may run on to another verse, another chapter or — at a book's
/// end — into the next book.
/// </para>
///
/// <para>
/// A set is somebody's reading of which verses belong together, not a fact about the text, which is
/// why the set and its credit are on every row. A parallel this project detected is one row per pair
/// of verses, both ways round, grouped into its passage, with the words the two verses share.
/// </para>
/// </summary>
[Index(nameof(Set), nameof(Book), nameof(Chapter), nameof(Verse))]
[Index(nameof(Set), nameof(Passage))]
public class CrossReference
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    /// <summary>Which set says so, as <see cref="Corpus.CrossReferenceSets"/> names it.</summary>
    public required string Set { get; set; }

    /// <summary>The set's credit, which the dataset declaration counts the row under.</summary>
    public required string Source { get; set; }

    public int Book { get; set; }

    public int Chapter { get; set; }

    public int Verse { get; set; }

    public int ToBook { get; set; }

    public int ToChapter { get; set; }

    public int ToVerse { get; set; }

    /// <summary>Where the far end stops, when it is more than one verse; null otherwise, with the other two.</summary>
    public int? ToEndBook { get; set; }

    public int? ToEndChapter { get; set; }

    public int? ToEndVerse { get; set; }

    /// <summary>
    /// Where the set puts this among the references of its verse, from 1: by votes, by the order
    /// the set prints them in, or by how much of the verse the two share.
    /// </summary>
    public int Rank { get; set; }

    /// <summary>What readers have voted it, where the set is ranked by votes; below zero is voted down.</summary>
    public int? Votes { get; set; }

    /// <summary>The word of the verse the set files it under — the Treasury's catchword — as the set prints it.</summary>
    public string? Note { get; set; }

    /// <summary>
    /// For a detected parallel, the passage it belongs to, one number per passage and direction, so
    /// the rows of one passage give its extent on both sides. Renumbered by every load; nothing outside
    /// this table holds it.
    /// </summary>
    public int? Passage { get; set; }

    /// <summary>The text whose word positions <see cref="Matched"/> gives.</summary>
    public string? MatchedIn { get; set; }

    /// <summary>
    /// The words the two verses share, as positions in each verse of <see cref="MatchedIn"/>, this
    /// verse's first: <c>1-1 2-2 4-3</c>.
    /// </summary>
    public string? Matched { get; set; }

    public override string ToString() =>
        $"CrossReference({Set} {Book} {Chapter}:{Verse} -> {ToBook} {ToChapter}:{ToVerse})";
}
