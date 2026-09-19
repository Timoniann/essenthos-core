namespace Essenthos.Core.Accounts;

/// <summary>
/// A reader's bookmark on a passage: one verse or a run of them, in a colour they picked, with a comment
/// to themselves if they wrote one. Private — it is one person's margin, not a commentary; published
/// commentaries are their own thing and are not this.
///
/// **The anchor is a canonical address, never a corpus row id.** Every corpus release renumbers
/// <c>verse.id</c>, so a bookmark stored against one would mark a different verse after the next
/// publication, silently and with no way back. Book, chapter and verse in the shared frame name the
/// same place in every release by construction, and <c>forge publish</c> refuses a release in which an
/// anchor no longer resolves.
///
/// The range is inside one book. It can cross a chapter — John 7:53–8:11 is one passage — which is why
/// the end carries its own chapter.
/// </summary>
public class Bookmark : IRevised
{
    public Guid Id { get; set; }

    public Guid AccountId { get; set; }

    /// <summary>
    /// The text the bookmark is on, by its slug — <c>KJV</c> — when it marks one wording; null when it
    /// marks the passage itself, whichever text it is read in.
    /// </summary>
    public string? Text { get; set; }

    /// <summary>The canonical book ordinal: Genesis is 1, Matthew 40.</summary>
    public int Book { get; set; }

    public int Chapter { get; set; }

    public int Verse { get; set; }

    public int EndChapter { get; set; }

    public int EndVerse { get; set; }

    /// <summary>
    /// One of <see cref="Colors"/>, by name rather than as a hex value, so the client can draw each in a
    /// shade that reads on a light page and on a dark one.
    /// </summary>
    public required string Color { get; set; }

    /// <summary>What the reader wrote to themselves about it, or null for a bookmark with no comment.</summary>
    public string? Comment { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public long Revision { get; set; }

    /// <summary>The colours a bookmark can be. The first is what one gets when none is named.</summary>
    public static readonly IReadOnlyList<string> Colors = ["amber", "green", "blue", "rose", "violet"];

    public override string ToString() => $"Bookmark({Id}, {Book} {Chapter}:{Verse}-{EndChapter}:{EndVerse}, {Color})";
}
