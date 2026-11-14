using Microsoft.EntityFrameworkCore;

namespace Essenthos.Core.Database.Entities;

/// <summary>
/// One family tie a tree draws, as <c>entity_relationship</c> holds it (parent, spouse or sibling, never a deity), with the
/// connected family it belongs to. Loaded from <c>records/derived/family-ties</c>.
/// </summary>
[PrimaryKey(nameof(FromEntityId), nameof(Type), nameof(ToEntityId))]
public class FamilyTie
{
    public int FromEntityId { get; set; }

    public Entity? From { get; set; }

    public required string Type { get; set; }

    public int ToEntityId { get; set; }

    public Entity? To { get; set; }

    public int? CanonicalBook { get; set; }

    public int? CanonicalChapter { get; set; }

    public int? CanonicalVerse { get; set; }

    public int Component { get; set; }
}

/// <summary>A person and the connected family they belong to; a person with no family tie is a family of one.</summary>
[PrimaryKey(nameof(EntityId))]
public class FamilyMember
{
    public int EntityId { get; set; }

    public Entity? Entity { get; set; }

    public required string Slug { get; set; }

    public int Component { get; set; }

    public string? Sex { get; set; }
}

/// <summary>What a record is called in one language of names, as a card shows it. Made by the serving build.</summary>
[PrimaryKey(nameof(Names), nameof(Slug))]
public class RecordName
{
    public required string Names { get; set; }

    public required string Slug { get; set; }

    public required string Kind { get; set; }

    public required string Name { get; set; }

    public string? LocalName { get; set; }

    public string? Distinguisher { get; set; }

    public string? Sex { get; set; }

    public string? AlsoCalled { get; set; }
}

/// <summary>The one plain line under a record's name for one pair of languages, already chosen. Made by the serving build.</summary>
[PrimaryKey(nameof(Prose), nameof(Names), nameof(Slug))]
public class RecordLine
{
    public required string Prose { get; set; }

    public required string Names { get; set; }

    public required string Slug { get; set; }

    public string? Line { get; set; }

    public bool Quoted { get; set; }

    public string? LocalLine { get; set; }

    public string? DescriptorLine { get; set; }
}

/// <summary>One connected family as the document a tree is drawn from, for one pair of languages. Made by the serving build.</summary>
[PrimaryKey(nameof(Component), nameof(Prose), nameof(Names))]
public class FamilyComponent
{
    public int Component { get; set; }

    public required string Prose { get; set; }

    public required string Names { get; set; }

    public required string Body { get; set; }

    public required string Hash { get; set; }

    public int People { get; set; }
}
