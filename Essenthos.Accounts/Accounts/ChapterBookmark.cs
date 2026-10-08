namespace Essenthos.Core.Accounts;

/// <summary>
/// A place a reader is reading: a whole chapter, marked in one press, with the verse they were on and
/// the texts they had open, so going back to it opens the reader as they left it. An account keeps a
/// list of them in its own order and moves between them. Not a <see cref="Bookmark"/>, which marks a
/// passage to remember; this marks where one is.
///
/// Addressed canonically, as a bookmark is — book and chapter in the shared frame, never a corpus row
/// id — and there is one per chapter per account.
/// </summary>
public class ChapterBookmark : ISoftDeleted
{
    public Guid Id { get; set; }

    public Guid AccountId { get; set; }

    /// <summary>The canonical book ordinal: Genesis is 1, Matthew 40.</summary>
    public int Book { get; set; }

    public int Chapter { get; set; }

    /// <summary>The verse the reader was on, or null when it is not known.</summary>
    public int? Verse { get; set; }

    /// <summary>The slugs of the texts open when the reader was last here, in pane order; may be none.</summary>
    public List<string> Texts { get; set; } = [];

    /// <summary>One of <see cref="Bookmark.Colors"/>.</summary>
    public required string Color { get; set; }

    /// <summary>Where it stands in the reader's own order of the list.</summary>
    public int Position { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public long Revision { get; set; }

    /// <summary>When the reader removed the bookmark; null while it is one of theirs.</summary>
    public DateTimeOffset? DeletedAt { get; set; }

    Guid IRevised.Owner => AccountId;

    public void Forget(DateTimeOffset at)
    {
        DeletedAt = at;
        Verse = null;
        Texts = [];
    }

    public override string ToString() => $"ChapterBookmark({Id}, {Book} {Chapter}, {Color})";
}
