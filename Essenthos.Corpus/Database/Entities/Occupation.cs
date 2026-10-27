using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace Essenthos.Core.Database.Entities;

[Index(nameof(Slug), IsUnique = true)]
[Index(nameof(Category))]
public class Occupation
{
    public int Id { get; set; }
    public required string Slug { get; set; }
    public required string Category { get; set; }
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
    public required string Name { get; set; }
    public required string Masculine { get; set; }
    public required string Feminine { get; set; }
    public string? Description { get; set; }
}

[Index(nameof(PersonId), nameof(OccupationId), IsUnique = true)]
public class PersonOccupation
{
    public int Id { get; set; }
    public int PersonId { get; set; }
    public Entity? Person { get; set; }
    public int OccupationId { get; set; }
    public Occupation? Occupation { get; set; }
    public required string PublicationId { get; set; }
    public required string PublicationSha256 { get; set; }
    public required string Provenance { get; set; }
    public List<OccupationWitness> Witnesses { get; set; } = [];
}

[Index(nameof(PersonOccupationId))]
public class OccupationWitness
{
    public int Id { get; set; }
    public int PersonOccupationId { get; set; }
    public PersonOccupation? PersonOccupation { get; set; }
    public required string Kind { get; set; }
    public required string Text { get; set; }
    public required string Reference { get; set; }
    public int CanonicalBook { get; set; }
    public int CanonicalChapter { get; set; }
    public int CanonicalVerse { get; set; }
    public required string Quote { get; set; }
    public required string SourceText { get; set; }
    public required string SourceSha256 { get; set; }
}
