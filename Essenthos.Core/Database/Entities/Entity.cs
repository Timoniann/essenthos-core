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
/// and a Strong number reaches words. Nothing uses that yet, and the columns are why it will be
/// possible without another load.
/// </summary>
[Index(nameof(EntityId))]
[Index(nameof(HebrewStrongNumber))]
[Index(nameof(GreekStrongNumber))]
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

    /// <summary>
    /// The Hebrew lexeme this name is, as a Strong number — and for a title, the Strong number of
    /// each word of it, comma-joined the way <see cref="StrongEntry.SeeAlso"/> joins its
    /// cross-references. <em>King of Judah</em> is two words and two numbers, and squeezing them
    /// into one value is what made a sixth of this column unresolvable.
    ///
    /// The two languages are separate columns because a name has both: Elijah is H452 in Kings and
    /// G2243 in Luke, and one column can only keep whichever is read first.
    /// </summary>
    public string? HebrewStrongNumber { get; set; }

    public string? GreekStrongNumber { get; set; }

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
/// God of the Old Testament and Jesus as one entity, and 1,417 New Testament references are
/// labelled with a word the New Testament uses of both — "G-d", "Lord", "Savior", "Judge". Those
/// are kept and flagged rather than assigned, because assigning them would be this corpus
/// asserting a reading of the text.
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

/// <summary>The two histories drawn on the one axis.</summary>
public static class Realms
{
    public const string Scripture = "scripture";
    public const string World = "world";
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
[Index(nameof(Realm))]
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

    /// <summary>
    /// Which history this belongs to — <c>scripture</c> or <c>world</c>.
    ///
    /// The whole point of putting them on one axis is that they disagree: the Great Pyramid is
    /// finished in 2560 BCE and the Masoretic reckoning has the Flood in 2304 BCE, so on that
    /// reckoning the pyramid is antediluvian and on the Septuagint's it is not. A reader has to be
    /// able to see which claim comes from which history before they can weigh that.
    /// </summary>
    public string Realm { get; set; } = Realms.Scripture;

    /// <summary>Where in the world, where the source says. The world layer is filtered by it.</summary>
    public string? Region { get; set; }

    /// <summary>Where to go and check this one row — a Wikidata item, usually.</summary>
    public string? Uri { get; set; }

    public string? Notes { get; set; }

    public required string Source { get; set; }

    /// <summary>What each reckoning makes of it. Never one number — see <see cref="EventDate"/>.</summary>
    public ICollection<EventDate> Dates { get; set; } = [];

    public override string ToString() => $"Event({Slug}, {BceYear} BCE)";
}
