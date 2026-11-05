using Microsoft.EntityFrameworkCore;

namespace Essenthos.Core.Database.Entities;

/// <summary>
/// Where a record's name first stands in one verse of a text: the least position of the words that name it there. A record
/// page orders the rows citing one verse by it (the Twelve as Matthew lists them) without reading the verse's words.
///
/// <para>
/// A build product (<c>build/products</c>, <c>record-positions/{text}</c>), made for the text a page reads that order in
/// (the King James): counted from the served tables once a build has filled them, and again when its inputs moved.
/// </para>
/// </summary>
[PrimaryKey(nameof(EntityId), nameof(TextId), nameof(Book), nameof(Chapter), nameof(Verse))]
public class RecordPosition
{
    public int EntityId { get; set; }

    public Entity? Entity { get; set; }

    public int TextId { get; set; }

    public Text? Text { get; set; }

    /// <summary>The canonical ordinal of the verse's book.</summary>
    public int Book { get; set; }

    /// <summary>The chapter as the text numbers it.</summary>
    public int Chapter { get; set; }

    /// <summary>The verse as the text numbers it.</summary>
    public int Verse { get; set; }

    public int Position { get; set; }
}
