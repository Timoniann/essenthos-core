using Microsoft.EntityFrameworkCore;

namespace Essenthos.Core.Database.Entities;

/// <summary>
/// What a chapter's context is made of whatever the reader's languages: the records it names and how, its events, periods,
/// measures, commandments, topics and family trees, as one JSON document. Made by the serving build (<c>context/chapters</c>).
/// </summary>
[PrimaryKey(nameof(CanonicalBook), nameof(CanonicalChapter))]
public class ContextChapter
{
    public int CanonicalBook { get; set; }

    public int CanonicalChapter { get; set; }

    public required string Body { get; set; }

    public required string Hash { get; set; }
}

/// <summary>
/// One record as a chapter's context shows it in a language of words and a language of names: its local name, our own line,
/// and the line's claims with the names they point at, as one JSON document. Made by the serving build
/// (<c>context/lines/{prose}</c>).
/// </summary>
[PrimaryKey(nameof(Prose), nameof(Names), nameof(Slug))]
public class ContextLine
{
    public required string Prose { get; set; }

    public required string Names { get; set; }

    public required string Slug { get; set; }

    public required string Body { get; set; }
}
