using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Essenthos.Core.Database.Entities;

/// <summary>The kingdoms a ruler is drawn under on the timeline of the kings.</summary>
public static class RulerRealms
{
    /// <summary>Saul, David and Solomon, over all twelve tribes before the division.</summary>
    public const string United = "united";
    public const string Israel = "israel";
    public const string Judah = "judah";
    public const string Egypt = "egypt";
    public const string Cush = "cush";
    public const string Aram = "aram";
    public const string Assyria = "assyria";
    public const string Babylon = "babylon";
    public const string Persia = "persia";

    public static readonly IReadOnlyList<string> All =
        [United, Israel, Judah, Egypt, Cush, Aram, Assyria, Babylon, Persia];
}

/// <summary>
/// One ruler of the monarchy or of a nation the text brings into it, and one period of his reign
/// as the timeline draws it.
///
/// <para>
/// The years are the period's, in every reckoning that dates it; this row only says whose it is,
/// in which kingdom it is drawn, and whether it is a reign of his own. A co-regency, a rival reign
/// or a disputed one is <see cref="Shared"/>: drawn, and drawn as sharing the throne. David's reign
/// in Hebron is over Judah while Ish-bosheth's is over Israel, so a period can be drawn under
/// another kingdom than its ruler's. A period that is <see cref="Fallback"/> is drawn only where a
/// reckoning dates none of the ruler's other periods — David's forty years, for a reckoning that
/// does not tell Hebron from Jerusalem.
/// </para>
///
/// <para>
/// A foreign ruler the reckonings do not date has one row with no period, and is placed by the
/// reigns the text sets him in.
/// </para>
/// </summary>
[Index(nameof(EntityId), nameof(PeriodId), IsUnique = true)]
[Index(nameof(PeriodId))]
public class RulerReign
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    public int EntityId { get; set; }

    public Entity? Entity { get; set; }

    /// <summary>One of <see cref="RulerRealms"/>.</summary>
    public required string Realm { get; set; }

    /// <summary>Where the ruler stands in the succession of his kingdom.</summary>
    public int Position { get; set; }

    public int? PeriodId { get; set; }

    public Period? Period { get; set; }

    /// <summary>The kingdom this period is drawn under, where it is not the ruler's own.</summary>
    public string? DrawnUnder { get; set; }

    public bool Shared { get; set; }

    public bool Fallback { get; set; }

    public required string Source { get; set; }

    public override string ToString() => $"RulerReign({EntityId} {Realm} {PeriodId})";
}

/// <summary>What a verse states about a ruler's days.</summary>
public static class ReignRoles
{
    /// <summary>A prophet spoke in them.</summary>
    public const string Prophet = "prophet";

    /// <summary>A ruler of another nation came into them — as ally, enemy, overlord or captor.</summary>
    public const string Nation = "nation";

    /// <summary>Another king began to reign in them, in the year the verse gives.</summary>
    public const string Accession = "accession";

    public static readonly IReadOnlyList<string> All = [Prophet, Nation, Accession];
}

/// <summary>How the verse states it.</summary>
public static class ReignStatementKinds
{
    /// <summary>The heading of a prophet's book: <em>in the days of Uzziah, Jotham, Ahaz</em>.</summary>
    public const string Superscription = "superscription";

    /// <summary>A word or a vision dated by the ruler's year.</summary>
    public const string Dated = "dated";

    /// <summary>The history puts the two together.</summary>
    public const string Narrative = "narrative";

    /// <summary>The chronicle names the prophet as the one who wrote the reign's acts.</summary>
    public const string Record = "record";

    /// <summary>
    /// A word spoken concerning the ruler, which does not say it was spoken while he reigned —
    /// Jeremiah's over Shallum, who had already been carried away.
    /// </summary>
    public const string Concerning = "concerning";

    public static readonly IReadOnlyList<string> All = [Superscription, Dated, Narrative, Record, Concerning];
}

/// <summary>What a stated year is counted from.</summary>
public static class ReignCounts
{
    /// <summary>The year of his reign, the first being the year he began.</summary>
    public const string Reign = "reign";

    /// <summary>The year of his captivity, as Ezekiel dates by Jehoiachin's.</summary>
    public const string Captivity = "captivity";

    /// <summary>The year he died: <em>in the year that king Uzziah died</em>.</summary>
    public const string Death = "death";

    public static readonly IReadOnlyList<string> All = [Reign, Captivity, Death];
}

/// <summary>
/// That somebody was in a ruler's days, in the verse that says so: Isaiah in Uzziah's (ISA 1:1),
/// Pul in Menahem's (2KI 15:19), Nadab made king in the second year of Asa (1KI 15:25).
///
/// <para>
/// **Only what a verse states.** A prophet is set in a king's days where a heading, a dated word,
/// the history or the chronicle puts him there, and not where a span of years merely overlaps —
/// the overlap is the timeline's to draw and the reader's to weigh, and a row here is a citation.
/// So the verse is required, and a king the text never names beside a prophet gets no row for him.
/// </para>
///
/// <para>
/// <see cref="Through"/> is the one relation that is not direct: Haggai's word came to Zerubbabel
/// the son of Shealtiel, and Shealtiel's page lists it as his son's.
/// </para>
/// </summary>
[Index(nameof(EntityId))]
[Index(nameof(RulerEntityId))]
public class ReignStatement
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    /// <summary>Who was in the ruler's days: the prophet, the foreign king, the king who acceded.</summary>
    public int EntityId { get; set; }

    public Entity? Entity { get; set; }

    public int RulerEntityId { get; set; }

    public Entity? Ruler { get; set; }

    /// <summary>One of <see cref="ReignRoles"/>.</summary>
    public required string Role { get; set; }

    /// <summary>One of <see cref="ReignStatementKinds"/>.</summary>
    public required string Kind { get; set; }

    /// <summary>The ruler's year the verse gives, where it gives one.</summary>
    public int? Year { get; set; }

    /// <summary>One of <see cref="ReignCounts"/>, where the verse counts from other than the accession.</summary>
    public string? CountedFrom { get; set; }

    public int? ThroughEntityId { get; set; }

    public Entity? Through { get; set; }

    public int CanonicalBook { get; set; }

    public int CanonicalChapter { get; set; }

    public int CanonicalVerse { get; set; }

    /// <summary>The last verse, where it takes more than one to say it.</summary>
    public int? EndVerse { get; set; }

    public required string Source { get; set; }

    public override string ToString() => $"ReignStatement({EntityId} {Role} of {RulerEntityId})";
}

/// <summary>
/// How long the text says a king reigned, in the verse that says it: Elah two years (1KI 16:8),
/// Zimri seven days (1KI 16:15), Jehoiachin three months (2KI 24:8).
///
/// <para>
/// The figure is the text's and not a reckoning's. The bars are drawn from the reckonings' own
/// years, and this is what a reader can hold them to.
/// </para>
/// </summary>
[Index(nameof(EntityId), IsUnique = true)]
public class ReignLength
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    public int EntityId { get; set; }

    public Entity? Entity { get; set; }

    public int? Years { get; set; }

    public int? Months { get; set; }

    public int? Days { get; set; }

    public int CanonicalBook { get; set; }

    public int CanonicalChapter { get; set; }

    public int CanonicalVerse { get; set; }

    public required string Source { get; set; }

    public override string ToString() => $"ReignLength({EntityId} {Years}y {Months}m {Days}d)";
}

/// <summary>Where a prophet's word was spoken, as a <see cref="ProphetField"/> places it.</summary>
public static class ProphetRealms
{
    /// <summary>All Israel under Saul, David and Solomon, before the kingdom divided.</summary>
    public const string United = RulerRealms.United;

    public const string Israel = RulerRealms.Israel;
    public const string Judah = RulerRealms.Judah;

    /// <summary>Among the captives in Babylon.</summary>
    public const string Exile = "exile";

    /// <summary>In Judah and Jerusalem after the return from Babylon.</summary>
    public const string Return = "return";

    /// <summary>The realms of Judah, Israel and the rest, and the lands of the nations a prophet was sent to.</summary>
    public static readonly IReadOnlyList<string> All = [United, Israel, Judah, Exile, Return, .. RulerRealms.All.Skip(3)];
}

/// <summary>What a <see cref="ProphetField"/> says of the land.</summary>
public static class ProphetFieldKinds
{
    /// <summary>He prophesied there, or to its people and its kings.</summary>
    public const string Prophesied = "prophesied";

    /// <summary>He came from there: Amos from Tekoa, in Judah, though he prophesied at Bethel.</summary>
    public const string From = "from";

    /// <summary>He was sent there once, or sent his word there: Jonah to Nineveh, Elijah's writing to Jehoram.</summary>
    public const string Sent = "sent";

    public static readonly IReadOnlyList<string> All = [Prophesied, From, Sent];
}

/// <summary>
/// Where a prophet spoke, where he came from, and where he was sent, each in the verse that says so:
/// Amos from Tekoa (AMO 1:1) prophesying at Bethel (AMO 7:13), Jonah of Gath-hepher (2KI 14:25) sent
/// to Nineveh (JON 1:2), Ezekiel among the captives by the river of Chebar (EZK 1:1).
///
/// <para>
/// A prophet is Judah's, Israel's or both by where he prophesied, and not by the kings named in
/// his heading: Hosea's names four kings of Judah and one of Israel, and his word is to Ephraim.
/// </para>
/// </summary>
[Index(nameof(EntityId))]
public class ProphetField
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    public int EntityId { get; set; }

    public Entity? Entity { get; set; }

    /// <summary>One of <see cref="ProphetRealms"/>.</summary>
    public required string Realm { get; set; }

    /// <summary>One of <see cref="ProphetFieldKinds"/>.</summary>
    public required string Kind { get; set; }

    /// <summary>The town or land the verse names, where it names one.</summary>
    public int? PlaceEntityId { get; set; }

    public Entity? Place { get; set; }

    public int CanonicalBook { get; set; }

    public int CanonicalChapter { get; set; }

    public int CanonicalVerse { get; set; }

    /// <summary>The last verse, where it takes more than one to say it.</summary>
    public int? EndVerse { get; set; }

    public int Position { get; set; }

    public required string Source { get; set; }

    public override string ToString() => $"ProphetField({EntityId} {Kind} {Realm})";
}

/// <summary>
/// The name a king reigned under where it is not the one his record is headed by: Eliakim, whose
/// name Pharaoh-nechoh turned to Jehoiakim (2KI 23:34), Mattaniah made Zedekiah (2KI 24:17), and
/// Azariah, whom the Chronicler calls Uzziah (2CH 26:1). The chart of the kings writes the throne
/// name, and his page keeps the headword.
/// </summary>
/// <remarks>One row per language, in the language's three-letter code as the name forms have it.</remarks>
[Index(nameof(EntityId), nameof(Language), IsUnique = true)]
public class ThroneName
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    public int EntityId { get; set; }

    public Entity? Entity { get; set; }

    public required string Language { get; set; }

    public required string Name { get; set; }

    public int CanonicalBook { get; set; }

    public int CanonicalChapter { get; set; }

    public int CanonicalVerse { get; set; }

    public required string Source { get; set; }

    public override string ToString() => $"ThroneName({EntityId} {Language} {Name})";
}

/// <summary>What the text says of a king's doing before the LORD.</summary>
public static class RulerMarks
{
    /// <summary>He did what was right, and the text does not take it back.</summary>
    public const string Right = "right";

    /// <summary>He did evil, and nothing else is said of him.</summary>
    public const string Evil = "evil";

    /// <summary>
    /// A right the text qualifies — <em>yet not like David</em>, <em>only the high places were not
    /// removed</em> — or a reign that began one way and ended the other.
    /// </summary>
    public const string Mixed = "mixed";

    public static readonly IReadOnlyList<string> All = [Right, Evil, Mixed];
}

/// <summary>What a mark rests on.</summary>
public static class VerdictBases
{
    /// <summary>The text says of him that he did right, or did evil, in the sight of the LORD.</summary>
    public const string Text = "text";

    /// <summary>The text says neither of him, and the mark is read from what it tells.</summary>
    public const string Reading = "reading";

    public static readonly IReadOnlyList<string> All = [Text, Reading];
}

/// <summary>The histories that judge a king, each in its own words.</summary>
public static class VerdictWitnesses
{
    public const string Samuel = "samuel";
    public const string Kings = "kings";
    public const string Chronicles = "chronicles";

    public static readonly IReadOnlyList<string> All = [Samuel, Kings, Chronicles];
}

/// <summary>
/// The mark a king is drawn with: right, evil or mixed, over everything the histories say of him.
/// What each history says is a <see cref="RulerVerdictWitness"/>, and where Kings and Chronicles
/// part — Manasseh, who in Chronicles humbles himself — both stand under the one mark.
/// </summary>
[Index(nameof(EntityId), IsUnique = true)]
public class RulerVerdict
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    public int EntityId { get; set; }

    public Entity? Entity { get; set; }

    /// <summary>One of <see cref="RulerMarks"/>.</summary>
    public required string Mark { get; set; }

    public required string Source { get; set; }

    public List<RulerVerdictWitness> Witnesses { get; set; } = [];

    public override string ToString() => $"RulerVerdict({EntityId} {Mark})";
}

/// <summary>What one history says of a king, and the verses it says it in.</summary>
[Index(nameof(VerdictId), nameof(Witness), IsUnique = true)]
public class RulerVerdictWitness
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    public int VerdictId { get; set; }

    public RulerVerdict? Verdict { get; set; }

    /// <summary>One of <see cref="VerdictWitnesses"/>.</summary>
    public required string Witness { get; set; }

    /// <summary>One of <see cref="RulerMarks"/>.</summary>
    public required string Mark { get; set; }

    /// <summary>One of <see cref="VerdictBases"/>.</summary>
    public required string Basis { get; set; }

    public int Position { get; set; }

    public List<RulerVerdictPassage> Passages { get; set; } = [];

    public override string ToString() => $"RulerVerdictWitness({VerdictId} {Witness} {Mark})";
}

/// <summary>A verse, or a run of verses in one chapter, a witness's mark is quoted from.</summary>
[Index(nameof(WitnessId))]
public class RulerVerdictPassage
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    public int WitnessId { get; set; }

    public RulerVerdictWitness? Witness { get; set; }

    public int CanonicalBook { get; set; }

    public int CanonicalChapter { get; set; }

    public int CanonicalVerse { get; set; }

    /// <summary>The last verse, where it takes more than one to say it.</summary>
    public int? EndVerse { get; set; }

    public int Position { get; set; }

    public override string ToString() =>
        $"RulerVerdictPassage({WitnessId} {CanonicalBook} {CanonicalChapter}:{CanonicalVerse})";
}

/// <summary>What a stated age is the age at.</summary>
public static class StatedAgeKinds
{
    /// <summary>When he began to reign: <em>twenty and five years old was he when he began to reign</em>.</summary>
    public const string Accession = "accession";

    /// <summary>When he died.</summary>
    public const string Death = "death";

    public static readonly IReadOnlyList<string> All = [Accession, Death];
}

/// <summary>
/// An age the text gives, in the verse that gives it: David thirty when he began to reign (2SA 5:4),
/// Hezekiah twenty-five (2KI 18:2).
///
/// <para>
/// Only the age is held. A year of birth is that age taken from the year a reckoning gives the
/// accession, and so differs by reckoning. Where two verses give two ages — Ahaziah twenty-two in
/// 2KI 8:26 and forty-two in 2CH 22:2 — both are rows, and neither is corrected by the other.
/// </para>
/// </summary>
[Index(nameof(EntityId))]
public class StatedAge
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    public int EntityId { get; set; }

    public Entity? Entity { get; set; }

    /// <summary>One of <see cref="StatedAgeKinds"/>.</summary>
    public required string Kind { get; set; }

    public int Years { get; set; }

    /// <summary>The verse says <em>about</em>: Darius the Mede, about threescore and two.</summary>
    public bool About { get; set; }

    public int CanonicalBook { get; set; }

    public int CanonicalChapter { get; set; }

    public int CanonicalVerse { get; set; }

    public int Position { get; set; }

    public required string Source { get; set; }

    public override string ToString() => $"StatedAge({EntityId} {Kind} {Years})";
}

/// <summary>What a <see cref="ReignEvent"/> was for the people it befell.</summary>
public static class ReignEventKinds
{
    /// <summary>A carrying away out of the land.</summary>
    public const string Exile = ProphetRealms.Exile;

    /// <summary>The going up again.</summary>
    public const string Return = ProphetRealms.Return;

    public static readonly IReadOnlyList<string> All = [Exile, Return];
}

/// <summary>
/// A carrying away or a return, in a verse that sets it in a ruler's days: Samaria taken in the ninth
/// year of Hoshea (2KI 17:6), which is the sixth of Hezekiah (2KI 18:10), and the proclamation in the
/// first year of Cyrus (EZR 1:1).
///
/// <para>
/// One row is one verse dating it by one ruler, as a <see cref="ReignStatement"/> dates a prophet, so
/// the event stands wherever a reckoning puts that ruler's year. The rows of one event share its
/// <see cref="Slug"/>, and name the event the timeline already dates, where it holds one.
/// </para>
/// </summary>
[Index(nameof(Slug))]
[Index(nameof(RulerEntityId))]
public class ReignEvent
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    public required string Slug { get; set; }

    /// <summary>One of <see cref="ReignEventKinds"/>.</summary>
    public required string Kind { get; set; }

    /// <summary>The kingdom it befell: <see cref="RulerRealms.Israel"/> or <see cref="RulerRealms.Judah"/>.</summary>
    public required string Realm { get; set; }

    /// <summary>Where the event stands among the events, in the order they happened.</summary>
    public int Position { get; set; }

    /// <summary>The same event on the timeline, where the chronologies date one.</summary>
    public int? TimelineEventId { get; set; }

    public Event? TimelineEvent { get; set; }

    public int RulerEntityId { get; set; }

    public Entity? Ruler { get; set; }

    /// <summary>The ruler's year the verse gives; null where it says only that it was in his days.</summary>
    public int? Year { get; set; }

    public int CanonicalBook { get; set; }

    public int CanonicalChapter { get; set; }

    public int CanonicalVerse { get; set; }

    /// <summary>The last verse, where it takes more than one to say it.</summary>
    public int? EndVerse { get; set; }

    public required string Source { get; set; }

    public override string ToString() => $"ReignEvent({Slug} {RulerEntityId} {Year})";
}
