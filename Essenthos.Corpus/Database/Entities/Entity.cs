using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Essenthos.Core.Database.Entities.Enums;
using Microsoft.EntityFrameworkCore;

namespace Essenthos.Core.Database.Entities;

/// <summary>
/// A person, a place or a people the text names.
///
/// Deliberately not a word and not a verse: an entity is a thing the world contains, and where it
/// is named is a separate fact recorded in <see cref="EntityVerse"/>. The old schema hung person
/// annotations on King James words, so no other translation could show a name at all; here the
/// entity stands on its own and its references are addressed canonically, which every text shares.
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

    /// <summary>
    /// What sort of object or observance this is — <c>furnishing</c>, <c>vessel</c>,
    /// <c>structure</c>, <c>vestment</c>; <c>feast</c>, <c>fast</c>, <c>sabbath</c>. Null on every
    /// other kind, which the text does not sort this way.
    /// </summary>
    public string? Subtype { get; set; }

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

    /// <summary>
    /// Whom or where a people is named after — Moab for the Moabites, Judah for the Judahites.
    ///
    /// A column on the record rather than a row in <see cref="EntityRelationship"/>, and the reason
    /// is provenance rather than convenience. That table has no per-row source, so every edge in it
    /// is BibleData's and reads as BibleData's; the claim here is Strong's, or ours, and putting it
    /// there would make one witness's statement wear another's name. What established it is the
    /// record's own <see cref="Claims"/>, which is where a reader can already see who said so.
    ///
    /// <para>
    /// Null far more often than not, and that is not a defect: Strong states an origin for every
    /// gentilic he derives and the encyclopedia holds a page for a third of them, so most peoples
    /// name an ancestor the corpus cannot open. The claim on the record still says whom, in his
    /// words; only the link is withheld.
    /// </para>
    /// </summary>
    public int? OriginEntityId { get; set; }

    public Entity? Origin { get; set; }

    public ICollection<EntityName> Names { get; set; } = [];

    public ICollection<EntityVerse> Verses { get; set; } = [];

    /// <summary>What established this record, where anything beyond its dataset did.</summary>
    public ICollection<EntityClaim> Claims { get; set; } = [];

    /// <summary>Who else this might be, where nobody can tell. Usually empty, and that is the point.</summary>
    public ICollection<EntityAlternative> Alternatives { get; set; } = [];

    /// <summary>Where a place is, as one point, when a source says so under terms that allow it.</summary>
    public PlaceLocation? Location { get; set; }

    /// <summary>Its pictures, each with whose it is and under what licence.</summary>
    public ICollection<EntityImage> Images { get; set; } = [];

    /// <summary>The passages a reader is sent to about this entity, and those that command it.</summary>
    public ICollection<EntityPassage> Passages { get; set; } = [];

    /// <summary>Where an observance falls in the year, as the verses that appoint it state.</summary>
    public ICollection<ObservanceTime> Times { get; set; } = [];

    public override string ToString() => $"Entity({Kind} {Slug})";
}

/// <summary>
/// One point for a place: where the identification its gazetteer scores highest puts it.
///
/// <para>
/// A point and nothing more. The gazetteer also draws rivers, regions and archaeological sites as
/// geometry, partly from OpenStreetMap under ODbL, which is share-alike; none of that is held, and
/// no point is held whose own coordinates it credits to OpenStreetMap. What
/// <see cref="CoordinatesSource"/> names is whom the gazetteer says the coordinates came from, so
/// every row can be traced back to the credit that let it in.
/// </para>
///
/// <para>
/// Only the best identification, and none where that one is not allowed: falling back to the
/// second-best would put a place where its own source thinks it probably is not.
/// </para>
/// </summary>
public class PlaceLocation
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.None)]
    public int EntityId { get; set; }

    public Entity? Entity { get; set; }

    public double Longitude { get; set; }

    public double Latitude { get; set; }

    /// <summary>
    /// What the point stands for: <c>point</c> the place itself, <c>representative-point</c> a spot
    /// inside a region or along a path, <c>center</c> the middle of the circle the place is somewhere
    /// in, <c>settlement</c> the town somewhere inside which it stood.
    /// </summary>
    public required string Kind { get; set; }

    /// <summary>
    /// The gazetteer's own score for the identification, as it states it: best read as thousandths,
    /// 500 and above high confidence, and able to run past 1000 or below zero.
    /// </summary>
    public int Score { get; set; }

    /// <summary>The gazetteer's identifier for the modern location the point is read from.</summary>
    public required string ModernId { get; set; }

    /// <summary>Whom the gazetteer credits for these coordinates — <c>wikidata</c>, <c>daahl</c>.</summary>
    public required string CoordinatesSource { get; set; }

    public required string Source { get; set; }

    public override string ToString() => $"PlaceLocation({EntityId} at {Longitude},{Latitude})";
}

/// <summary>
/// What established a record, for the records this corpus writes itself.
///
/// <see cref="Entity.Source"/> answers the question for a record that was imported: BibleData says
/// so, and a dataset's statement carries no confidence because it is testimony rather than a
/// conclusion. It cannot answer it for a record that exists because a verse names somebody no
/// dataset holds — there the honest answer is a method, a confidence and whoever decided, which is
/// three fields and not one.
///
/// <para>
/// So this is <see cref="LinkClaim"/>'s shape a third time, and deliberately so: the same four
/// provenance constraints, the same vocabulary of methods, the same rule that an inference carries
/// a number and a statement does not. A record with no claim is one whose <see cref="Entity.Source"/>
/// is the whole answer, which is every record any dataset supplied.
/// </para>
/// </summary>
[Index(nameof(EntityId))]
[Index(nameof(EntityId), nameof(Method), nameof(Source), IsUnique = true)]
public class EntityClaim
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    public int EntityId { get; set; }

    public Entity? Entity { get; set; }

    public LinkMethod Method { get; set; }

    /// <summary>Null exactly where a person or a source stated it, as everywhere else here.</summary>
    public double? Confidence { get; set; }

    public required string Source { get; set; }

    /// <summary>Why this record exists, in a sentence a reader can weigh.</summary>
    public string? Note { get; set; }

    public override string ToString() => $"EntityClaim({Method} on entity {EntityId})";
}

/// <summary>
/// Who else a record might be, where the evidence does not decide.
///
/// Judges 4:11 calls Hobab Moses' father-in-law and Numbers 10:29 calls him the son of Reuel, whom
/// Exodus 2:18 calls the father-in-law; the Hebrew word carries both father-in-law and
/// brother-in-law, and the question is old and open. A corpus that must answer it picks one and
/// looks certain. A corpus that may not answer it says nothing and looks empty. This is the third
/// thing: the record names the man, and names the man it might instead be, and says why nobody can
/// tell.
///
/// <para>
/// It is on the record rather than on the occurrence because it is a statement about the person —
/// <em>this Hobab may be Reuel under a second name</em> is true wherever he is named, not only in
/// the verse that raised it. Where the alternative is somebody the encyclopedia does not hold,
/// <see cref="AlternativeEntityId"/> is null and <see cref="Describes"/> is all there is, which is
/// the same shape as the rest of this corpus's silences.
/// </para>
/// </summary>
[Index(nameof(EntityId))]
public class EntityAlternative
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    public int EntityId { get; set; }

    public Entity? Entity { get; set; }

    public int? AlternativeEntityId { get; set; }

    public Entity? Alternative { get; set; }

    /// <summary>The alternative in words, for one the encyclopedia holds no record of.</summary>
    public string? Describes { get; set; }

    /// <summary>Why it is open, which is the part a reader is actually owed.</summary>
    public required string Reason { get; set; }

    public required string Source { get; set; }

    public override string ToString() =>
        $"EntityAlternative({EntityId} may be {AlternativeEntityId?.ToString() ?? Describes})";
}

/// <summary>
/// One man or woman the text gives a title to, in the verse where it does: Pharaoh-nechoh at
/// 2KI 23:29, Hilkiah the high priest at 2KI 22:4, Tiberius Caesar at LUK 3:1.
///
/// <para>
/// A title is held by whoever holds the office, and the reason it is a record of its own is that
/// the text often does not say who that is — the Pharaoh of the Exodus is never named, and the
/// Rabshakeh who speaks at the wall is known only by his office. So a bearer is written only where
/// the verse names the person and the title together, and the verse is required: it is the whole
/// of what settles it, and a bearer nobody can check is a guess about who held an office.
/// </para>
/// </summary>
[Index(nameof(TitleEntityId), nameof(BearerEntityId), IsUnique = true)]
[Index(nameof(BearerEntityId))]
public class TitleBearer
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    public int TitleEntityId { get; set; }

    public Entity? Title { get; set; }

    public int BearerEntityId { get; set; }

    public Entity? Bearer { get; set; }

    public int CanonicalBook { get; set; }

    public int CanonicalChapter { get; set; }

    public int CanonicalVerse { get; set; }

    /// <summary>Why this verse settles it, in a sentence a reader can check against it.</summary>
    public string? Note { get; set; }

    public required string Source { get; set; }

    public override string ToString() => $"TitleBearer({BearerEntityId} bears {TitleEntityId})";
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

    /// <summary>
    /// The record this name is that of, where this record is not it.
    ///
    /// A Strong number is a name and not a place, and one name can be borne by several records that
    /// are one place seen several ways: Zion the settlement and Mount Zion its hill, Samaria the
    /// city and Samaria the country called after it, Egypt and the brook, the sea and the river
    /// named after Egypt, Edom and Idumea, Jerusalem and Salem. Left null on all of them, the
    /// number names several records and <see cref="Loading.Encyclopedia.EntityAnnotationLoader"/>
    /// refuses to resolve it, so 146 words that name Zion today would name nobody.
    ///
    /// <para>
    /// So the row says whose name it is. The number then answers with one record — the one the
    /// corpus already reads it onto — while the record here keeps the name and the number as its
    /// own, which is what a page for Mount Zion is owed.
    /// </para>
    ///
    /// <para>
    /// Null is the ordinary case and means what it says: this record is the one the name is of.
    /// Two records both left null are two places under one name — Jericho at Tell es Sultan and
    /// Jericho at Tell el Alayiq — and the number does not resolve, which is the right answer
    /// rather than a gap.
    /// </para>
    /// </summary>
    public int? AspectOfEntityId { get; set; }

    public Entity? AspectOf { get; set; }

    /// <summary>
    /// Which dataset gives this name, where it is not the dataset the entity came from. Null means
    /// the entity's own source says it — every row written before names were credited one by one.
    /// OpenBible's spellings sit on places BibleData created, and a label on somebody else's record
    /// has to say whose it is.
    /// </summary>
    public string? Source { get; set; }

    public override string ToString() => $"EntityName({Label})";
}

/// <summary>
/// What each row of <see cref="EntityRelationship.Category"/> may say, and no more.
///
/// The first three are BibleData's own honesty about its own edge list, and they are kept in its
/// words. The fourth is this corpus's, and it exists because ours is a different kind of thing: a
/// model read a verse the row names and said the relation holds. Calling that <c>explicit</c>
/// would put a reading and a dataset's citation under one word, which is the failure
/// <see cref="LinkClaim"/> was built to prevent everywhere else.
/// </summary>
public static class RelationshipCategories
{
    /// <summary>A verse says it, and the dataset points at the verse.</summary>
    public const string Explicit = "explicit";

    /// <summary>The dataset worked it out from what it holds.</summary>
    public const string Inferred = "inferred";

    /// <summary>The dataset takes it as understood without arguing it.</summary>
    public const string Implicit = "implicit";

    /// <summary>Read here, from the verse on the row, by whatever <c>source</c> names.</summary>
    public const string Read = "read";
}

/// <summary>
/// One entity standing in one relation to another — son, father, servant, killer.
/// </summary>
/// <remarks>
/// <see cref="Category"/> is the source's own honesty: <c>explicit</c> where a verse says it and
/// <c>inferred</c> where the dataset worked it out. Keeping that distinction is the same discipline
/// the link table applies to words, and losing it would make a deduction look like a citation.
///
/// <para>
/// <see cref="Method"/>, <see cref="Confidence"/> and <see cref="Source"/> are here for the reason
/// they are on every other claim this corpus holds, and they arrived when the table stopped being
/// one dataset's. An edge read out of a verse by a model and an edge a dataset states are not the
/// same claim, and a table with no per-row provenance can only present them as though they were —
/// which is what <see cref="EntityVerse.Source"/> was added to stop happening to the verse lists.
/// </para>
///
/// <para>
/// **A relationship with no verse may exist, and only for a witness that gave none.** BibleData
/// states 40 of its 5,448 rows without a reference and there is nothing to be done about that but
/// say so; inventing a citation for them would be a guess wearing the clothes of a source, in the
/// one table where a reader is most likely to follow one. What this corpus concludes for itself
/// always names the verse it read, and that is a database constraint rather than a habit: every
/// method but <see cref="LinkMethod.StatedBySource"/> carries an address.
/// </para>
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

    /// <summary>
    /// The relation, in the vocabulary of whoever states it: BibleData's <c>son</c>, or this
    /// corpus's <c>son-of</c> from <see cref="DescriptorRelations"/>. Deliberately not translated
    /// into one spelling — a row saying <c>son</c> under BibleData's name has to keep saying what
    /// BibleData said, and <see cref="Loading.Encyclopedia.RelationshipVocabulary"/> is where the
    /// two are compared.
    /// </summary>
    public required string Type { get; set; }

    /// <summary>One of <see cref="RelationshipCategories"/>.</summary>
    public required string Category { get; set; }

    /// <summary>The verse the source rests it on, where it rests it on one.</summary>
    public int? CanonicalBook { get; set; }

    public int? CanonicalChapter { get; set; }

    public int? CanonicalVerse { get; set; }

    public LinkMethod Method { get; set; }

    /// <summary>
    /// Null exactly where a person or a source stated it, a number in 0..1 where a process
    /// concluded it — as everywhere else here, and enforced by the database in both directions.
    /// </summary>
    public double? Confidence { get; set; }

    /// <summary>Which dataset, model or person says so. Never empty.</summary>
    public required string Source { get; set; }

    public string? Notes { get; set; }

    /// <summary>
    /// The owner removed this witness's row in his console. It stays in the table as what the witness
    /// said, and no query sees it: a page, a tree and the pick among the witnesses read the table as
    /// though it were not there.
    /// </summary>
    public bool Withdrawn { get; set; }

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
///
/// <para>
/// A row is a verse, once per entity and per source: the list counts verses, and a verse named
/// twice in it — once per word or once per label — is the over-count the unique key refuses.
/// </para>
/// </remarks>
[Index(nameof(EntityId))]
[Index(nameof(CanonicalBook), nameof(CanonicalChapter), nameof(CanonicalVerse))]
[Index(nameof(EntityId), nameof(CanonicalBook), nameof(CanonicalChapter), nameof(CanonicalVerse), nameof(Source),
    IsUnique = true)]
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

    /// <summary>
    /// Which dataset says the entity is named here, which is not always the dataset the entity
    /// came from.
    ///
    /// The place layer is two sources over one set of places: BibleData names 118 of them and
    /// stops after Exodus, and OpenBible names 1,342 across 61 books, 110 of which are the same
    /// places under the identifier BibleData already carries. Joining them onto one entity is
    /// right — a reader wants one Jerusalem — but a count that silently mixes the two would be a
    /// claim neither source makes. So the provenance is per row, at the grain the claim is made.
    /// </summary>
    public required string Source { get; set; }

    /// <summary>
    /// Whether a word of this verse is annotated to the entity — the verse <em>names</em> it — or
    /// the source lists the verse without any word of it saying who: the entity is the subject
    /// under a pronoun, or somebody recalls him, as 2 Kings 9 recalls Zimri through a coup that is
    /// Jehu's. Both are real relations and neither is deleted; a reader following a reference is
    /// owed which one it is. Derived after every pass that annotates a word, from
    /// <see cref="WordEntity"/>, and false until then.
    /// </summary>
    public bool Names { get; set; }

    public override string ToString() =>
        $"EntityVerse({EntityId} at {CanonicalBook} {CanonicalChapter}:{CanonicalVerse})";
}

/// <summary>
/// The records the text is read to name God by: BibleData's two, as the Father and as the God of
/// Israel, and the Holy Spirit. None of them is ever given a face or a figure; the one picture they may
/// have is ours of the glory, light with nothing inside it to see.
/// </summary>
public static class DivineRecords
{
    public const string GodSourcePrefix = "person:YHVH_";

    public const string HolySpiritSourceId = "essenthos:thing:holy-spirit";

    public static bool Contains(string sourceId) =>
        sourceId.StartsWith(GodSourcePrefix, StringComparison.Ordinal)
        || string.Equals(sourceId, HolySpiritSourceId, StringComparison.Ordinal);
}

/// <summary>The two histories drawn on the one axis.</summary>
public static class Realms
{
    public const string Scripture = "scripture";
    public const string World = "world";
}

/// <summary>
/// Where an event's title came from, which is not always where its facts came from.
///
/// Most sources title their own rows and this says so. Some do not: Ussher wrote 7,000 numbered
/// paragraphs and no headings, and a table whose name column cannot be null has to put something
/// there. Quoting the author's opening sentence and writing a summary are both defensible answers
/// and they are not the same answer, so a row says which it took — a made title that reads as the
/// author's is a claim about a dead man's words, and the corpus refuses those everywhere else.
/// </summary>
public static class EventNames
{
    /// <summary>The source named it, and the name is the source's.</summary>
    public const string FromTheSource = "source";

    /// <summary>The source's own words, taken from the start of what it wrote and not rephrased.</summary>
    public const string Quoted = "quoted";

    /// <summary>Written for the corpus. Never the source's words, and the row says what wrote it.</summary>
    public const string Generated = "generated";
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

    /// <summary>Whose words <see cref="Name"/> is. One of <see cref="EventNames"/>.</summary>
    public string NameSource { get; set; } = EventNames.FromTheSource;

    public string? Description { get; set; }

    public string? Kind { get; set; }

    /// <summary>Whom it happened to, where the source names one.</summary>
    public int? EntityId { get; set; }
    public Entity? Entity { get; set; }

    /// <summary>Years from the creation as this dataset counts them, Adam being year 1.</summary>
    public int? YearFromCreation { get; set; }

    /// <summary>
    /// The BCE year as the source printed it, where it printed one — not a derivation. Everything
    /// here can be computed from <see cref="YearFromCreation"/> and the reckoning's zero, so a row
    /// that merely restates the arithmetic says nothing and is not written; what is worth keeping
    /// is a figure a source stated and the arithmetic does not reproduce.
    ///
    /// It is unsigned, because the one source that states it prints it unsigned: a jubilee whose
    /// derivation lands after the turn has <c>BceYear = 8</c> for the year 7 CE. Read it with
    /// <see cref="YearFromCreation"/> beside it, which does carry the sign.
    /// </summary>
    public int? BceYear { get; set; }

    public int? AgeAtEvent { get; set; }

    /// <summary>
    /// Where the source itself puts this among the things it narrates, where it puts it anywhere —
    /// and null, which is most rows, where no source states an order at all.
    ///
    /// <para>
    /// **It is a stated position and never a date, and the two are not the same kind of fact.**
    /// <see cref="YearFromCreation"/> is computed and shows its arithmetic in
    /// <see cref="Calculation"/>; this is a claim a source made in so many words. Ussher writes
    /// <em>the next day</em> and <em>on the third day</em> across four paragraphs he dates to one
    /// year, so what he states there is a sequence and not a day — and a month and day column would
    /// have to be filled by inventing one. The number itself is his own paragraph number, so it is
    /// checkable against the work: <c>¶6298</c> is on the row's <see cref="EventDate.Citation"/>.
    /// </para>
    ///
    /// <para>
    /// **Null is the answer for anything nobody ordered, and it stays null.** Most of the corpus is
    /// dated to a year and placed nowhere within it, and giving those a position would be asserting
    /// an order no source states — worse than the tie, because the tie is visible and the invention
    /// is not. So a reader is told which it is: <c>sequenced</c> on the wire is true for exactly the
    /// rows this is set on.
    /// </para>
    ///
    /// <para>
    /// **It breaks ties on <see cref="YearFromCreation"/> and never moves the axis.** The numbers
    /// are one source's own and mean nothing beside another's, so they order events within one year
    /// and are not a second axis, an offset, or a fraction of a year.
    /// </para>
    /// </summary>
    public int? SequenceInYear { get; set; }

    /// <summary>The arithmetic, in a sentence, so the year can be checked rather than believed.</summary>
    public string? Calculation { get; set; }

    public int? CanonicalBook { get; set; }

    public int? CanonicalChapter { get; set; }

    public int? CanonicalVerse { get; set; }

    public string? Location { get; set; }

    /// <summary>
    /// The verse at which the source names <see cref="Location"/>, which is often not the event's
    /// own: Ishmael's birth is dated at Genesis 16:16 and its Canaan is named at 16:3. It is what
    /// tells two places of one name apart — the Samaria a reign begins in is the one recorded at
    /// that verse.
    /// </summary>
    public int? LocationBook { get; set; }

    public int? LocationChapter { get; set; }

    public int? LocationVerse { get; set; }

    /// <summary>
    /// Which history this belongs to — <c>scripture</c> or <c>world</c>.
    ///
    /// The whole point of putting them on one axis is that they disagree: the Great Pyramid is
    /// finished in 2560 BCE and the Masoretic reckoning has the Flood in 2304 BCE, so on that
    /// reckoning the pyramid is antediluvian and on the Septuagint's it is not. A reader has to be
    /// able to see which claim comes from which history before they can weigh that.
    /// </summary>
    public string Realm { get; set; } = Realms.Scripture;

    /// <summary>
    /// Where in the world, where the source says. The world layer is filtered by it.
    ///
    /// **Today's country, not the polity of the time.** Wikidata's <c>country</c> is the current or
    /// last-known administrative entity, so the Battle of Himera is in Italy and a stele cut around
    /// 1200 BCE can be in a state of 1867. It is a place to filter by and never a claim about who
    /// held the ground, which is why no sentence the corpus writes interpolates it.
    /// <see cref="RegionAtTheTime"/> is the other half.
    /// </summary>
    public string? Region { get; set; }

    /// <summary>
    /// The country the source states for this item whose own lifetime contains the event's year —
    /// Ancient Rome beside Italy — and null where it states none that existed then. Never inferred
    /// from the modern one: a country is only the one of the time if its dates say so.
    /// </summary>
    public string? RegionAtTheTime { get; set; }

    /// <summary>Where to go and check this one row — a Wikidata item, usually.</summary>
    public string? Uri { get; set; }

    public string? Notes { get; set; }

    public required string Source { get; set; }

    /// <summary>What each reckoning makes of it. Never one number — see <see cref="EventDate"/>.</summary>
    public ICollection<EventDate> Dates { get; set; } = [];

    public override string ToString() => $"Event({Slug}, {BceYear} BCE)";
}
