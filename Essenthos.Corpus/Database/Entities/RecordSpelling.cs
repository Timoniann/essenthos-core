using Microsoft.EntityFrameworkCore;

namespace Essenthos.Core.Database.Entities;

/// <summary>
/// How often one text prints one form for one record under one Strong number: the words that name the record in the
/// text, counted by their letters and by the number the annotation went through (the word's own, that of the original
/// word it was carried from, or the one its note leads with). A record page reads the rows of the reader's language to
/// say which of the record's names each spelling prints, without touching a word.
///
/// <para>
/// A build product (<c>build/products</c>, <c>record-spellings/{text}</c>): counted from the served tables once a
/// build has filled them, and counted again for a text whose inputs moved.
/// </para>
/// </summary>
[PrimaryKey(nameof(EntityId), nameof(TextId), nameof(StrongNumber), nameof(Form))]
public class RecordSpelling
{
    public int EntityId { get; set; }

    public Entity? Entity { get; set; }

    public int TextId { get; set; }

    public Text? Text { get; set; }

    public required string StrongNumber { get; set; }

    /// <summary>The word's letters as the text prints them.</summary>
    public required string Form { get; set; }

    public int Occurrences { get; set; }
}
