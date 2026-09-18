namespace Essenthos.Core.Accounts;

/// <summary>
/// A reader's note on a passage: one verse or a run of them, in their own words. Private — a note is
/// one person's margin, not a commentary (FTR-0026 is commentaries).
///
/// **The anchor is a canonical address, never a corpus row id** (RUL-0185). Every corpus release
/// renumbers <c>verse.id</c>, so a note stored against one would name a different verse after the next
/// publication, silently and with no way back. Book, chapter and verse in the shared frame name the
/// same place in every release by construction, and <c>forge publish</c> refuses a release in which
/// an anchor no longer resolves.
///
/// The range is inside one book. It can cross a chapter — John 7:53–8:11 is one passage — which is why
/// the end carries its own chapter.
///
/// The anchor is in the note's own row rather than in a table of anchors: a note has exactly one, and
/// a second table would be a join on every chapter the reader opens for a flexibility nothing uses.
/// </summary>
public class Note : IRevised
{
    public Guid Id { get; set; }

    public Guid AccountId { get; set; }

    /// <summary>
    /// The text the note is about, by its slug — <c>KJV</c> — when it is about one wording; null when it
    /// is about the passage itself, whichever text it is read in.
    /// </summary>
    public string? Text { get; set; }

    /// <summary>The canonical book ordinal: Genesis is 1, Matthew 40.</summary>
    public int Book { get; set; }

    public int Chapter { get; set; }

    public int Verse { get; set; }

    public int EndChapter { get; set; }

    public int EndVerse { get; set; }

    /// <summary>What the reader wrote, as they wrote it. Shown as plain text with its line breaks.</summary>
    public required string Body { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public long Revision { get; set; }

    public override string ToString() => $"Note({Id}, {Book} {Chapter}:{Verse}-{EndChapter}:{EndVerse})";
}
