using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Essenthos.Core.Database.Entities;

/// <summary>
/// One thing a person is known for, as a clause that reads after <em>known for</em>: <em>built the
/// ark and was saved from the Flood with his family</em>, with the verses that say it.
///
/// <para>
/// The line under a name says who somebody is by whom they are related to; this says what they did
/// or what happened to them. It is this corpus's own sentence, written from the person's verses, so
/// it is ours in every language: <see cref="English"/> is the clause, <see cref="Texts"/> its
/// renderings, and a reader asking for a language it has not been rendered into is given the English.
/// </para>
/// </summary>
[Index(nameof(EntityId), nameof(Ordinal), IsUnique = true)]
public class EntityKnownFor
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    public int EntityId { get; set; }

    public Entity? Entity { get; set; }

    /// <summary>Where the clause stands among the person's, from the first.</summary>
    public int Ordinal { get; set; }

    public required string English { get; set; }

    /// <summary>Who wrote it and rendered it, and when. Never empty.</summary>
    public required string Source { get; set; }

    public ICollection<EntityKnownForText> Texts { get; set; } = [];

    public ICollection<EntityKnownForReference> References { get; set; } = [];

    public override string ToString() => $"EntityKnownFor({EntityId} {Ordinal})";
}

/// <summary>A clause of <see cref="EntityKnownFor"/> in a reader's language other than English.</summary>
[Index(nameof(KnownForId), nameof(Language), IsUnique = true)]
public class EntityKnownForText
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    public int KnownForId { get; set; }

    public EntityKnownFor? KnownFor { get; set; }

    /// <summary>The three-letter code the texts are tagged with: <c>ukr</c>, <c>deu</c>, <c>spa</c>.</summary>
    [MaxLength(8)]
    public required string Language { get; set; }

    public required string Text { get; set; }

    public override string ToString() => $"EntityKnownForText({KnownForId} {Language})";
}

/// <summary>
/// A run of verses a clause of <see cref="EntityKnownFor"/> rests on, addressed canonically and from
/// one verse to another that may stand in a later chapter.
/// </summary>
[Index(nameof(KnownForId), nameof(Position), IsUnique = true)]
public class EntityKnownForReference
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    public int KnownForId { get; set; }

    public EntityKnownFor? KnownFor { get; set; }

    /// <summary>Where the run stands among the clause's references, from the first.</summary>
    public int Position { get; set; }

    public int CanonicalBook { get; set; }

    public int CanonicalChapter { get; set; }

    public int CanonicalVerse { get; set; }

    public int EndChapter { get; set; }

    public int EndVerse { get; set; }

    public override string ToString() =>
        $"EntityKnownForReference({KnownForId} {CanonicalBook} {CanonicalChapter}:{CanonicalVerse}-{EndChapter}:{EndVerse})";
}
