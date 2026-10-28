using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace Essenthos.Core.Database.Entities;

/// <summary>
/// What the text says someone did or was: a trade, an office, a military or religious role, a role in
/// heaven, or a designation the text gives them (<em>the harlot</em>, <em>a thief</em>). One row per
/// concept, named in four languages by <see cref="OccupationLabel"/>.
/// </summary>
[Index(nameof(Slug), IsUnique = true)]
[Index(nameof(Category))]
public class Occupation
{
    public int Id { get; set; }
    public required string Slug { get; set; }
    public required string Category { get; set; }

    /// <summary>
    /// The Strong numbers of the words that name it in the originals: <c>G2395</c> for a physician,
    /// <c>H4428</c> for a king. A word inside a cited quotation carrying one of them is the word that
    /// names the role there. Empty where the text says it with no word of its own (<em>over the
    /// vineyards</em>), and nothing is marked.
    /// </summary>
    public string[] StrongNumbers { get; set; } = [];

    public List<OccupationLabel> Labels { get; set; } = [];
    public List<PersonOccupation> People { get; set; } = [];
}

[Index(nameof(OccupationId), nameof(Language), IsUnique = true)]
public class OccupationLabel
{
    public int Id { get; set; }
    public int OccupationId { get; set; }
    public Occupation? Occupation { get; set; }
    public required string Language { get; set; }

    /// <summary>The heading of its page and of its entry in the catalogue.</summary>
    public required string Name { get; set; }

    /// <summary>What a man holding it is called.</summary>
    public required string Masculine { get; set; }

    /// <summary>What a woman holding it is called: <em>лікарка</em>, <em>Ärztin</em>, <em>médica</em>.</summary>
    public required string Feminine { get; set; }

    public string? Description { get; set; }
}

/// <summary>One person the text says held one occupation, with the verses that say so.</summary>
[Index(nameof(PersonId), nameof(OccupationId), IsUnique = true)]
public class PersonOccupation
{
    public int Id { get; set; }
    public int PersonId { get; set; }
    public Entity? Person { get; set; }
    public int OccupationId { get; set; }
    public Occupation? Occupation { get; set; }

    /// <summary>Who read it from the text and who decided it. Not served.</summary>
    public required string Source { get; set; }

    public List<OccupationWitness> Witnesses { get; set; } = [];
    public List<OccupationWord> Words { get; set; } = [];
}

/// <summary>
/// One quotation a claim rests on: <c>profession</c> where it says the occupation, <c>identity</c>
/// where it says who the person is (Acts 13:9 for Saul who is Paul).
/// </summary>
[Index(nameof(PersonOccupationId))]
public class OccupationWitness
{
    public int Id { get; set; }
    public int PersonOccupationId { get; set; }
    public PersonOccupation? PersonOccupation { get; set; }
    public required string Kind { get; set; }

    /// <summary>The text quoted, by slug: <c>BHSA</c>, <c>NESTLE1904</c>, <c>KJV</c>.</summary>
    public required string Text { get; set; }

    /// <summary>The verse as that text numbers it: BHSA's <c>NEH 10:3</c> is the English 10:2.</summary>
    public required string Reference { get; set; }

    public int CanonicalBook { get; set; }
    public int CanonicalChapter { get; set; }
    public int CanonicalVerse { get; set; }

    /// <summary>The words quoted, exactly as the text prints them in that verse.</summary>
    public required string Quote { get; set; }
}

/// <summary>
/// A word that names a person's occupation in a verse that says it: the word of the quotation that
/// carries one of the occupation's Strong numbers, and the words of every other text the links say
/// render it. The reader marks these as it marks names.
/// </summary>
[Index(nameof(PersonOccupationId), nameof(WordId), IsUnique = true)]
[Index(nameof(WordId))]
public class OccupationWord
{
    public long Id { get; set; }
    public int PersonOccupationId { get; set; }
    public PersonOccupation? PersonOccupation { get; set; }
    public long WordId { get; set; }
    public Word? Word { get; set; }
}
