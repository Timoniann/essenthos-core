using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Essenthos.Core.Database.Entities;

/// <summary>
/// A subject of a topical index — AARON, FAITH, TITHES — and the verses it files under it.
///
/// <para>
/// A topic is somebody's reading of what a verse is about, not a fact about the text, which is why
/// it is a table of its own rather than an entity: an entity is a person or a place the text names,
/// and <em>Faith</em> is named in none of the verses filed under it.
/// </para>
/// </summary>
[Index(nameof(Slug), IsUnique = true)]
public class Topic
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    public required string Slug { get; set; }

    /// <summary>The heading as the index prints it, capitals and all.</summary>
    public required string Name { get; set; }

    public required string Source { get; set; }

    public List<TopicReference> References { get; set; } = [];

    public override string ToString() => $"Topic({Slug})";
}

/// <summary>
/// Verses of one chapter the index files under a topic, with the line it files them under.
///
/// <para>
/// A reference to a whole chapter has neither verse; one that runs to the end of a chapter has a
/// first verse and no last. The frame's own length of the chapter is what closes them, so a chapter
/// that holds a verse more in one numbering is not cut short here.
/// </para>
/// </summary>
[Index(nameof(CanonicalBook), nameof(CanonicalChapter))]
[Index(nameof(TopicId))]
public class TopicReference
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    public int TopicId { get; set; }

    public Topic? Topic { get; set; }

    public int CanonicalBook { get; set; }

    public int CanonicalChapter { get; set; }

    public int? FirstVerse { get; set; }

    public int? LastVerse { get; set; }

    /// <summary>
    /// The line of the entry the verses are cited on — <em>Makes the golden calf</em> — with the line
    /// above it where the index nests one under another. Null where the verses stand under the topic
    /// itself.
    /// </summary>
    public string? Heading { get; set; }

    public override string ToString() =>
        $"TopicReference({TopicId} at {CanonicalBook} {CanonicalChapter}:{FirstVerse}-{LastVerse})";
}
