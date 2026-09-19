using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Essenthos.Core.Database.Entities.Enums;
using Microsoft.EntityFrameworkCore;

namespace Essenthos.Core.Database.Entities;

/// <summary>
/// One clause of what this corpus says an entity is: <em>son of Reuel</em>, <em>descendants of
/// Moab</em>.
///
/// <see cref="Entity.Distinguisher"/> is the same information as a finished English sentence
/// somebody else wrote, and it fails three ways at once — it is theirs, so translating it makes a
/// derivative; it is prose, so the Moses inside it cannot be reached; and it exists only in
/// English. A row here is what that sentence would have been made of before it was words: a
/// relation, an entity the encyclopedia holds, and the verse it was read from. The sentence is
/// rendered from these per language, and every name in it is a link because it was never text.
///
/// <para>
/// **A row is one clause and not one description**, which is why <see cref="Ordinal"/> is here:
/// the clauses of one entity are ordered and the order is the order the sentence takes. Two of them
/// render as <em>son of Reuel, father-in-law of Moses</em>, and a set with no order renders as
/// whichever way the query came back.
/// </para>
///
/// <para>
/// It is an assertion, so it carries what asserted it, and the shape is
/// <see cref="WordEntity"/> / <see cref="WordEntityClaim"/>'s rather than a fourth vocabulary for
/// the same idea: the row is the conclusion and holds the strongest claim's method, confidence and
/// source, and <see cref="EntityDescriptorClaim"/> holds every method that says so. That matters
/// here for the reason it matters there — what will fill this table first is a model reading
/// Scripture, and BibleData's 5,448 relationship rows are the answer key it is measured against.
/// A clause both of them arrive at independently is visibly better evidenced than one only the
/// model proposed, and with one method per row the second one has nowhere to go.
/// </para>
///
/// <para>
/// **Nothing here writes <see cref="Entity.Distinguisher"/> and nothing here reads it.** That
/// column keeps holding the sentence BibleData supplied, under BibleData's name, and stops being
/// what a reader is shown. It is not dead and it is not to be dropped: it is the imported record,
/// it is what this layer is measured against, and it is the only description that exists for an
/// entity nothing has yet been generated for.
/// </para>
/// </summary>
/// <remarks>
/// <see cref="EntityRelationship"/> is a different table saying a similar thing, and the difference
/// is provenance and purpose. That one is BibleData's whole edge list, with no per-row source, and
/// it is not displayed; this one is what the encyclopedia says in its own voice, ordered for a
/// sentence, and every row of it names what established it.
/// </remarks>
[Index(nameof(EntityId))]
[Index(nameof(TargetEntityId))]
[Index(nameof(EntityId), nameof(Ordinal), IsUnique = true)]
public class EntityDescriptor
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    public int EntityId { get; set; }

    public Entity? Entity { get; set; }

    /// <summary>Where in the sentence this clause falls, from 1. Unique within the entity.</summary>
    public int Ordinal { get; set; }

    /// <summary>
    /// One of <see cref="DescriptorRelations"/>, and never anything else. The vocabulary is closed
    /// because every relation needs a phrasing in every language the encyclopedia speaks, and one
    /// that has none renders as its own identifier at a reader. It is a string rather than an enum
    /// for the reason <see cref="EntityRelationship.Type"/> is: the set grows by a document, a
    /// phrasing and a test rather than by a migration.
    /// </summary>
    public required string Relation { get; set; }

    /// <summary>
    /// Whom or what the clause names. Never null: a target the encyclopedia does not hold is not a
    /// clause at all, it is a countable gap, and writing it as prose is the thing this table
    /// exists to stop.
    /// </summary>
    public int TargetEntityId { get; set; }

    public Entity? Target { get; set; }

    /// <summary>
    /// The verse the clause was read from, in the shared canonical frame so that every text reaches
    /// it. Required, and required to be a verse the entity is actually named in — a reference
    /// nobody can follow is worse than none, so the loader refuses one that names no
    /// <see cref="EntityVerse"/> row for this entity.
    /// </summary>
    public int CanonicalBook { get; set; }

    public int CanonicalChapter { get; set; }

    public int CanonicalVerse { get; set; }

    public LinkMethod Method { get; set; }

    /// <summary>
    /// Null exactly where a person or a source stated it, a number in 0..1 where a process
    /// concluded it — as everywhere else here, and enforced by the database in both directions.
    /// </summary>
    public double? Confidence { get; set; }

    /// <summary>The model and the date of the run, or the person. Never empty.</summary>
    public required string Source { get; set; }

    /// <summary>
    /// The file of the generation pass this row was read from, where the loader recorded one.
    ///
    /// Not for a reader: <see cref="Source"/> is what a page credits. This is how one pass is told
    /// from another when the loader asks whether it has already stored an answer, and nothing else
    /// on the row can do it — a re-ask carries the same model and the same date as the batch it
    /// corrects. Null on rows loaded before it was recorded, which is why the first load after this
    /// re-reads them from the files rather than trusting a blank.
    /// </summary>
    public string? Run { get; set; }

    /// <summary>Why, where it is worth reading. Usually the model's own sentence for the clause.</summary>
    public string? Note { get; set; }

    public ICollection<EntityDescriptorClaim> Claims { get; set; } = [];

    public override string ToString() =>
        $"EntityDescriptor({EntityId} {Relation} {TargetEntityId})";
}

/// <summary>
/// One method saying that a clause of a description is true, and how sure it is.
///
/// The same table for the same reason <see cref="WordEntityClaim"/> and <see cref="LinkClaim"/>
/// exist: agreement is the cheapest evidence there is, and with one method per row the second
/// method to arrive has nowhere to go and the first one to speak wins. Here the two methods are a
/// model reading the verse and the imported relationship rows the reading is measured against, and
/// telling apart <em>the model said so</em> from <em>the model said so and the answer key agrees</em>
/// is the whole of what makes this layer publishable.
/// </summary>
[Index(nameof(EntityDescriptorId))]
[Index(nameof(EntityDescriptorId), nameof(Method), nameof(Source), IsUnique = true)]
public class EntityDescriptorClaim
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    public int EntityDescriptorId { get; set; }

    public EntityDescriptor? EntityDescriptor { get; set; }

    public LinkMethod Method { get; set; }

    /// <summary>Null exactly when a source stated it or a person set it, as everywhere else.</summary>
    public double? Confidence { get; set; }

    public required string Source { get; set; }

    public string? Note { get; set; }

    public override string ToString() =>
        $"EntityDescriptorClaim({Method} on descriptor {EntityDescriptorId})";
}

/// <summary>
/// The name of an entity in one language, in one grammatical case.
///
/// Ukrainian and German put the target of most of these phrases in the genitive —
/// <em>тесть Мойсея</em>, not <em>тесть Мойсей</em>, and <em>Sohn Aarons</em>, not
/// <em>Sohn Aaron</em> — so a rendering needs the name in the form the phrase puts it in, and
/// there is nowhere else in the corpus to keep one. <see cref="EntityName"/> holds what a source
/// calls an entity, in the source's language; this holds what we call it, in a reader's.
///
/// <para>
/// **The forms are produced with the name and never computed from it.** A stemmer guessing the
/// genitive of a Hebrew proper name is wrong often and silently, and a reader cannot tell — which
/// is the same failure as an alignment that looks sourced. Where a form is missing the rendering
/// falls back to the English name and says nothing about it: a gap, not an error, and never an
/// invented inflection.
/// </para>
///
/// <para>
/// It carries <see cref="Source"/> for the reason every string this encyclopedia renders into
/// another language will, and the reason nothing else in this corpus is exempt: a name form is a
/// claim about a person's name in a language, it will usually have been produced by a model, and a
/// machine's rendering of a Biblical name is a claim nobody has checked unless the row says who
/// made it and when.
/// </para>
/// </summary>
[Index(nameof(EntityId))]
[Index(nameof(EntityId), nameof(Language), nameof(GrammaticalCase), IsUnique = true)]
public class EntityNameForm
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    public int EntityId { get; set; }

    public Entity? Entity { get; set; }

    /// <summary>The three-letter code the texts are tagged with: <c>eng</c>, <c>ukr</c>, <c>rus</c>.</summary>
    public required string Language { get; set; }

    /// <summary>
    /// One of <see cref="GrammaticalCases"/>. Named in full because <c>case</c> is a reserved word
    /// in SQL, and a column nobody can type unquoted is a column somebody eventually mistypes.
    /// </summary>
    public required string GrammaticalCase { get; set; }

    public required string Form { get; set; }

    public LinkMethod Method { get; set; }

    public double? Confidence { get; set; }

    /// <summary>The model and the date, or the person. Never empty.</summary>
    public required string Source { get; set; }

    /// <summary>
    /// The file of the generation pass this row was read from, where the loader recorded one.
    ///
    /// Not for a reader: <see cref="Source"/> is what a page credits. This is how one pass is told
    /// from another when the loader asks whether it has already stored an answer, and nothing else
    /// on the row can do it — a re-ask carries the same model and the same date as the batch it
    /// corrects. Null on rows loaded before it was recorded, which is why the first load after this
    /// re-reads them from the files rather than trusting a blank.
    /// </summary>
    public string? Run { get; set; }

    public override string ToString() => $"EntityNameForm({EntityId} {Language} {GrammaticalCase})";
}

/// <summary>
/// The cases a name form can be held in.
///
/// <see cref="Nominative"/> and <see cref="Genitive"/> are the two a generation pass is asked for
/// with every name. <see cref="Locative"/> is here because the Slavic phrasings need it and neither
/// of the other two can stand in for it — <em>жив у Вифлеємі</em> is not the genitive
/// <em>Вифлеєма</em> — so a clause that puts a place in it renders with the English name until a
/// pass supplies one.
/// </summary>
public static class GrammaticalCases
{
    public const string Nominative = "nominative";

    public const string Genitive = "genitive";

    public const string Locative = "locative";

    public static readonly IReadOnlyList<string> All = [Nominative, Genitive, Locative];
}

/// <summary>
/// Every relation a descriptor clause may state, and nothing else.
///
/// It is closed on purpose. Each of these has a phrasing in each language the encyclopedia speaks,
/// and a relation with no phrasing has only one thing it can do at a reader, which is print its own
/// identifier. So the loader refuses a relation that is not here, loudly, rather than storing a
/// clause that can never be rendered.
///
/// <para>
/// Adding one is three changes and not one: this list, a phrasing in every language, and a test.
/// The generation pass writes these names and the reader renders them, so the written contract
/// between the two is changed before they are.
/// </para>
/// </summary>
public static class DescriptorRelations
{
    public const string SonOf = "son-of";
    public const string DaughterOf = "daughter-of";
    public const string FatherOf = "father-of";
    public const string MotherOf = "mother-of";
    public const string BrotherOf = "brother-of";
    public const string SisterOf = "sister-of";
    public const string HusbandOf = "husband-of";
    public const string WifeOf = "wife-of";
    public const string GrandfatherOf = "grandfather-of";
    public const string GrandmotherOf = "grandmother-of";
    public const string AncestorOf = "ancestor-of";
    public const string DescendantOf = "descendant-of";
    public const string HalfBrotherOf = "half-brother-of";
    public const string HalfSisterOf = "half-sister-of";
    public const string GrandsonOf = "grandson-of";
    public const string GranddaughterOf = "granddaughter-of";
    public const string UncleOf = "uncle-of";
    public const string AuntOf = "aunt-of";
    public const string NephewOf = "nephew-of";
    public const string NieceOf = "niece-of";
    public const string FatherInLawOf = "father-in-law-of";
    public const string MotherInLawOf = "mother-in-law-of";
    public const string SonInLawOf = "son-in-law-of";
    public const string DaughterInLawOf = "daughter-in-law-of";
    public const string BrotherInLawOf = "brother-in-law-of";
    public const string SisterInLawOf = "sister-in-law-of";
    public const string ConcubineOf = "concubine-of";

    public const string KingOf = "king-of";
    public const string QueenOf = "queen-of";
    public const string ProphetTo = "prophet-to";
    public const string PriestOf = "priest-of";
    public const string JudgeOf = "judge-of";
    public const string HighPriestOf = "high-priest-of";
    public const string CommanderOf = "commander-of";
    public const string GovernorOf = "governor-of";
    public const string TetrarchOf = "tetrarch-of";
    public const string ServantOf = "servant-of";
    public const string MasterOf = "master-of";
    public const string DiscipleOf = "disciple-of";
    public const string ApostleOf = "apostle-of";
    public const string ScribeOf = "scribe-of";
    public const string CompanionOf = "companion-of";

    /// <summary>
    /// The teacher a disciple follows, read from the teacher's side. <em>Rabbi</em> is what the
    /// Gospels have the Twelve call Jesus, and <see cref="DiscipleOf"/> is the same tie from theirs.
    /// </summary>
    public const string TeacherOf = "teacher-of";

    /// <summary>
    /// A party bound to another by a covenant or a league: <em>these were confederate with
    /// Abram</em> (GEN 14:13). Symmetric, as the text states it.
    /// </summary>
    public const string AllyOf = "ally-of";

    /// <summary>
    /// The children of two siblings, which the text states of Esther and Mordecai: <em>his uncle's
    /// daughter</em> (EST 2:7). One relation for both sexes, because the claim does not say which it
    /// is and a gendered word would have to guess.
    /// </summary>
    public const string CousinOf = "cousin-of";

    /// <summary>
    /// What one entity is to another in a killing, both ways round: a minor figure named only as
    /// the person somebody struck down has this and nothing else to say, and the person who struck
    /// them is often remembered for exactly that.
    /// </summary>
    public const string KilledBy = "killed-by";

    public const string KillerOf = "killer-of";

    /// <summary>
    /// What one person is to another in a rape, both ways round, which the text states plainly of
    /// Shechem and Dinah (GEN 34:2) and of Amnon and Tamar (2SA 13:14). A dataset filed both women
    /// beside the dead under one word, <c>victim</c>; they are a different fact, and a page reading
    /// <em>Dinah, killed by Shechem</em> would say something the text does not.
    /// </summary>
    public const string RapedBy = "raped-by";

    public const string RaperOf = "raper-of";

    /// <summary>
    /// Who kept whom out of their own means: Joanna and Susanna, <em>which ministered unto him of
    /// their substance</em> (LUK 8:3). Both ways round.
    /// </summary>
    public const string SupporterOf = "supporter-of";

    public const string SupportedBy = "supported-by";

    /// <summary>
    /// The maker and the made, which the text states of the LORD God and the man he formed of the
    /// dust (GEN 2:7). Both ways round.
    /// </summary>
    public const string CreatorOf = "creator-of";

    public const string CreatedBy = "created-by";

    /// <summary>
    /// Who stood to inherit from whom: <em>one born in mine house is mine heir</em> (GEN 15:3).
    /// <see cref="InheritedBy"/> is the same tie read from the one who leaves the inheritance.
    /// </summary>
    public const string HeirOf = "heir-of";

    public const string InheritedBy = "inherited-by";

    /// <summary>
    /// Who carried whom away into exile: <em>Jeconiah ... whom Nebuchadnezzar the king of Babylon had
    /// carried away</em> (EST 2:6). Not an answer to the question a killing answers, for the reason a
    /// rape is not: being exiled and being killed are two facts.
    /// </summary>
    public const string ExilerOf = "exiler-of";

    public const string ExiledBy = "exiled-by";

    /// <summary>
    /// A being the text defines by whom or what it belongs to, rather than by descent —
    /// <em>the angel of the bottomless pit</em>, <em>the angel of the LORD</em>.
    /// </summary>
    public const string AngelOf = "angel-of";

    public const string OfTribe = "of-tribe";
    public const string OfPeople = "of-people";
    public const string FromPlace = "from-place";
    public const string LivedIn = "lived-in";
    public const string BuriedIn = "buried-in";

    /// <summary>
    /// The one the owner asked for by name: <c>moabites descendants-of moab</c> renders as
    /// <em>нащадки Моава</em>, with Moab a link.
    /// </summary>
    public const string DescendantsOf = "descendants-of";

    public const string CityIn = "city-in";
    public const string RegionOf = "region-of";
    public const string RiverOf = "river-of";
    public const string MountainIn = "mountain-in";

    /// <summary>
    /// Which wall or city a named gate belongs to. It is possessive and not locational, so it takes
    /// the genitive and not the locative the four clauses around it take.
    /// </summary>
    public const string GateOf = "gate-of";

    public const string Near = "near";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
    {
        SonOf, DaughterOf, FatherOf, MotherOf, BrotherOf, SisterOf, HusbandOf, WifeOf,
        HalfBrotherOf, HalfSisterOf,
        GrandfatherOf, GrandmotherOf, GrandsonOf, GranddaughterOf,
        UncleOf, AuntOf, NephewOf, NieceOf,
        AncestorOf, DescendantOf,
        FatherInLawOf, MotherInLawOf, SonInLawOf, DaughterInLawOf,
        BrotherInLawOf, SisterInLawOf, ConcubineOf, CousinOf,
        KingOf, QueenOf, ProphetTo, PriestOf, JudgeOf, HighPriestOf,
        CommanderOf, GovernorOf, TetrarchOf, ServantOf, MasterOf,
        DiscipleOf, ApostleOf, ScribeOf, CompanionOf, TeacherOf, AllyOf,
        KilledBy, KillerOf, RapedBy, RaperOf, AngelOf,
        SupporterOf, SupportedBy, CreatorOf, CreatedBy, HeirOf, InheritedBy, ExilerOf, ExiledBy,
        OfTribe, OfPeople, FromPlace, LivedIn, BuriedIn,
        DescendantsOf,
        CityIn, RegionOf, RiverOf, MountainIn, GateOf, Near,
    };
}
/// <summary>
/// The relations that put something somewhere, and so can only point at a place.
///
/// <para>
/// A tribe is a person, a people and a territory at once in this encyclopedia, and a pass reading
/// <em>Bethlehem, a city in Judah</em> means the territory while the name it reaches for is most
/// often the patriarch's. Measured on the corpus: 107 placing clauses point at a person or a
/// people, and 60 of them are the twelve tribes. A reader who follows <em>a city in Judah</em> and
/// arrives at Jacob's son has been told something false by a link.
/// </para>
/// </summary>
public static class PlacingRelations
{
    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
    {
        DescriptorRelations.LivedIn,
        DescriptorRelations.BuriedIn,
        DescriptorRelations.FromPlace,
        DescriptorRelations.CityIn,
        DescriptorRelations.RegionOf,
        DescriptorRelations.RiverOf,
        DescriptorRelations.MountainIn,
        DescriptorRelations.GateOf,
        DescriptorRelations.Near,
    };
}

