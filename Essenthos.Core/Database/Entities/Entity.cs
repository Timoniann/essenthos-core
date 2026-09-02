using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Essenthos.Core.Database.Entities.Enums;
using Microsoft.EntityFrameworkCore;

namespace Essenthos.Core.Database.Entities;

/// <summary>
/// A person or a place the text names.
///
/// Deliberately not a word and not a verse: an entity is a thing the world contains, and where it
/// is named is a separate fact recorded in <see cref="EntityVerse"/>. The old schema hung person
/// annotations on King James words, so no other translation could show a name at all (PRB-0026);
/// here the entity stands on its own and its references are addressed canonically, which every
/// text shares.
/// </summary>
[Index(nameof(Slug), IsUnique = true)]
[Index(nameof(Kind))]
[Index(nameof(SourceId))]
public class Entity
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    public EntityKind Kind { get; set; }

    /// <summary>The public identifier, stable and lower case: <c>moses</c>, <c>jerusalem</c>.</summary>
    public required string Slug { get; set; }

    public required string Name { get; set; }

    /// <summary>
    /// What tells this one from every other of the same name — "son of Nun", "the Hittite". The
    /// dataset carries it because 3,009 people share far fewer than 3,009 names.
    /// </summary>
    public string? Distinguisher { get; set; }

    public string? Sex { get; set; }

    public string? Tribe { get; set; }

    /// <summary>A place's kind as the source classifies it: city, region, astronomical.</summary>
    public string? PlaceKind { get; set; }

    public string? ModernEquivalent { get; set; }

    public string? Notes { get; set; }

    /// <summary>
    /// Its identifier in the dataset it came from — <c>Moses_1</c>, <c>heaven_1</c>. Kept so a
    /// correction upstream can be found again, and so two datasets can be reconciled later.
    /// </summary>
    public required string SourceId { get; set; }

    /// <summary>Which dataset said so. This corpus will hold more than one.</summary>
    public required string Source { get; set; }

    /// <summary>
    /// OpenBible's identifier for a place, which is how coordinates are reached without holding a
    /// second gazetteer.
    /// </summary>
    public string? OpenBibleId { get; set; }

    public ICollection<EntityName> Names { get; set; } = [];

    public ICollection<EntityVerse> Verses { get; set; } = [];

    public override string ToString() => $"Entity({Kind} {Slug})";
}

/// <summary>
/// One name or title an entity is called by, in the languages the source carries it in.
///
/// This is where the encyclopedia meets the rest of the corpus: a label carries a Strong number,
/// and a Strong number reaches words. Nothing uses that yet, and the column is why it will be
/// possible without another load.
/// </summary>
[Index(nameof(EntityId))]
[Index(nameof(StrongNumber))]
public class EntityName
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    public int EntityId { get; set; }
    public Entity? Entity { get; set; }

    public required string Label { get; set; }

    public string? Hebrew { get; set; }

    public string? HebrewTransliterated { get; set; }

    public string? Greek { get; set; }

    public string? GreekTransliterated { get; set; }

    /// <summary>What the name means, where the source says. "Drawn out", "House of Bread".</summary>
    public string? Meaning { get; set; }

    public string? StrongNumber { get; set; }

    /// <summary>Name, title, epithet — what kind of label this is.</summary>
    public string? Kind { get; set; }

    public override string ToString() => $"EntityName({Label})";
}

/// <summary>
/// One entity standing in one relation to another — son, father, servant, killer.
/// </summary>
/// <remarks>
/// <see cref="Category"/> is the source's own honesty: <c>explicit</c> where a verse says it and
/// <c>inferred</c> where the dataset worked it out. Keeping that distinction is the same discipline
/// the link table applies to words, and losing it would make a deduction look like a citation.
/// </remarks>
[Index(nameof(FromEntityId))]
[Index(nameof(ToEntityId))]
public class EntityRelationship
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    public int FromEntityId { get; set; }
    public Entity? From { get; set; }

    public int ToEntityId { get; set; }
    public Entity? To { get; set; }

    public required string Type { get; set; }

    public required string Category { get; set; }

    /// <summary>The verse the source rests it on, where it rests it on one.</summary>
    public int? CanonicalBook { get; set; }

    public int? CanonicalChapter { get; set; }

    public int? CanonicalVerse { get; set; }

    public string? Notes { get; set; }

    public override string ToString() => $"EntityRelationship({FromEntityId} {Type} {ToEntityId})";
}

/// <summary>
/// A place where an entity is named, addressed in the shared canonical frame so that every text
/// reaches it — which is the whole reason it is not a word id.
/// </summary>
/// <remarks>
/// <see cref="Disputed"/> marks a reference the source itself cannot resolve. BibleData holds the
/// God of the Old Testament and Jesus as one entity, and 1,397 New Testament references say only
/// "G-d" or "Lord", which in the Gospels may be either. Those are kept and flagged rather than
/// assigned, because assigning them would be this corpus asserting a reading of the text.
/// </remarks>
[Index(nameof(EntityId))]
[Index(nameof(CanonicalBook), nameof(CanonicalChapter), nameof(CanonicalVerse))]
public class EntityVerse
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    public int EntityId { get; set; }
    public Entity? Entity { get; set; }

    public int CanonicalBook { get; set; }

    public int CanonicalChapter { get; set; }

    public int CanonicalVerse { get; set; }

    /// <summary>What the text calls the entity here — "Lamb", "King of the Jews", "G-d".</summary>
    public string? Label { get; set; }

    public bool Disputed { get; set; }

    public override string ToString() =>
        $"EntityVerse({EntityId} at {CanonicalBook} {CanonicalChapter}:{CanonicalVerse})";
}

/// <summary>
/// Something that happened, and when the source thinks it happened.
///
/// The dates are the reason this dataset was chosen over the others: every one is computed from a
/// verse and shows its arithmetic in <see cref="Calculation"/>, and where a chronologer disagrees
/// his figure sits beside it rather than replacing it. A reader can therefore see not only the
/// year but why it is that year and who else says otherwise, which is the difference between a
/// timeline and a claim.
/// </summary>
[Index(nameof(Slug), IsUnique = true)]
[Index(nameof(EntityId))]
[Index(nameof(YearFromCreation))]
public class Event
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    public required string Slug { get; set; }

    public required string Name { get; set; }

    public string? Description { get; set; }

    public string? Kind { get; set; }

    /// <summary>Whom it happened to, where the source names one.</summary>
    public int? EntityId { get; set; }
    public Entity? Entity { get; set; }

    /// <summary>Years from the creation as this dataset counts them, Adam being year 1.</summary>
    public int? YearFromCreation { get; set; }

    public int? BceYear { get; set; }

    public int? AgeAtEvent { get; set; }

    /// <summary>The arithmetic, in a sentence, so the year can be checked rather than believed.</summary>
    public string? Calculation { get; set; }

    public int? CanonicalBook { get; set; }

    public int? CanonicalChapter { get; set; }

    public int? CanonicalVerse { get; set; }

    public string? Location { get; set; }

    /// <summary>Ussher's year from creation, and his paragraph, where he treats it.</summary>
    public int? UssherAnnoMundi { get; set; }

    public int? UssherBceYear { get; set; }

    public string? UssherParagraph { get; set; }

    /// <summary>Shulman's year from creation, following Seder Olam.</summary>
    public int? ShulmanAnnoMundi { get; set; }

    public string? Notes { get; set; }

    public required string Source { get; set; }

    public override string ToString() => $"Event({Slug}, {BceYear} BCE)";
}
