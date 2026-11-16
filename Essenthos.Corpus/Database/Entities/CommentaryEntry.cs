using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Essenthos.Core.Database.Entities;

/// <summary>
/// One comment of one commentary, over the span of the text it was written on.
///
/// <para>
/// A commentary is not a text: it has no words to link and no verses of its own. It is prose attached to a
/// span, and the span is the commentary's own — Matthew Henry writes on Genesis 1:1-2 and then on 1:3-5 — so a
/// comment is one row over its whole run of verses, never a copy per verse and never cut to one. The span is in
/// the shared frame by canonical book, chapter and verse, never a verse row, which every rebuild renumbers.
/// A comment on verses has all four ends; an introduction to a chapter has its chapter at both ends and no verse;
/// an introduction to the book has neither.
/// </para>
/// </summary>
[Index(nameof(Commentary), nameof(Book), nameof(FirstChapter), nameof(FirstVerse))]
public class CommentaryEntry
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    /// <summary>Which commentary, as <see cref="Corpus.CommentaryWorks"/> names it.</summary>
    public required string Commentary { get; set; }

    /// <summary>The commentary's credit, which the dataset declaration counts the row under.</summary>
    public required string Source { get; set; }

    public int Book { get; set; }

    public int? FirstChapter { get; set; }

    public int? FirstVerse { get; set; }

    public int? LastChapter { get; set; }

    public int? LastVerse { get; set; }

    /// <summary>Its place in the commentary, from 1: introductions before the comments they open, as the book prints them.</summary>
    public int Sequence { get; set; }

    /// <summary>
    /// The comment as blocks of marked text, JSON as the commentary reader writes it (headings, paragraphs, lines of
    /// verse; italic, small capitals, the verses it cites). Kept as text: it is handed to the reader whole.
    /// </summary>
    public required string Body { get; set; }

    public override string ToString() => $"CommentaryEntry({Commentary} {Book} {FirstChapter}:{FirstVerse}-{LastChapter}:{LastVerse})";
}
