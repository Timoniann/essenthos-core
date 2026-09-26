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
