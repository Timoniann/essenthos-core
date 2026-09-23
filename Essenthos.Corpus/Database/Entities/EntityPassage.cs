using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Essenthos.Core.Database.Entities;

/// <summary>What a passage is to the entity it is listed on.</summary>
public static class PassageRoles
{
    /// <summary>A passage a reader should read about it: where it is made, described, carried or lost.</summary>
    public const string Key = "key";

    /// <summary>A passage that commands or institutes an observance.</summary>
    public const string Command = "command";

    public static readonly IReadOnlyList<string> All = [Key, Command];
}

/// <summary>
/// A span of verses an entity's page sends a reader to — Exodus 25:10-22 for the ark, Leviticus 23
/// for the feasts.
///
/// <para>
/// It is not <see cref="EntityVerse"/>, and the difference is the point. A verse there is one in which
/// the entity is named, and every row of it is a claim about a word; a passage here is a stretch of
/// text about the thing, most of whose verses say <em>it</em>, <em>thereof</em> or nothing at all of
/// it. Writing each verse of Exodus 25 as a place the menorah is named would put the menorah in a verse
/// about the table, so a passage is held whole, with the range as the text divides it.
/// </para>
///
/// <para>
/// Addressed canonically, like everything outside the corpus, and from one verse to another verse
/// that may stand in a later chapter.
/// </para>
/// </summary>
[Index(nameof(EntityId))]
[Index(nameof(CanonicalBook), nameof(CanonicalChapter), nameof(EndChapter))]
public class EntityPassage
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    public int EntityId { get; set; }

    public Entity? Entity { get; set; }

    /// <summary>One of <see cref="PassageRoles"/>.</summary>
    public required string Role { get; set; }

    /// <summary>The order the source lists it in, which is the order a reader is shown.</summary>
    public int Ordinal { get; set; }

    public int CanonicalBook { get; set; }

    public int CanonicalChapter { get; set; }

    /// <summary>The first verse, or null where the passage is the whole chapter.</summary>
    public int? CanonicalVerse { get; set; }

    public int EndChapter { get; set; }

    /// <summary>The last verse, or null where the passage runs to the end of its last chapter.</summary>
    public int? EndVerse { get; set; }

    public string? Note { get; set; }

    public required string Source { get; set; }

    public override string ToString() =>
        $"EntityPassage({EntityId} {Role} {CanonicalBook} {CanonicalChapter}:{CanonicalVerse}-{EndChapter}:{EndVerse})";
}

/// <summary>How often an observance comes round.</summary>
public static class ObservanceCycles
{
    public const string Weekly = "weekly";
    public const string Monthly = "monthly";
    public const string Yearly = "yearly";
    public const string SeventhYear = "seventh-year";
    public const string FiftiethYear = "fiftieth-year";

    public static readonly IReadOnlyList<string> All = [Weekly, Monthly, Yearly, SeventhYear, FiftiethYear];
}

/// <summary>
/// Where an observance falls, as the verse that appoints it says: the fourteenth day of the first
/// month, the seventh day of the week, the first day of every month.
///
/// <para>
/// **Only what a verse states.** The month is counted as the text counts it, from Abib in which
/// Israel came out of Egypt (Exodus 12:2; 13:4), and a day or a month the text leaves unsaid is
/// null rather than worked out: Weeks is counted from the sheaf and not dated, and the fasts of
/// Zechariah 8:19 have a month and no day. So a row is a citation — <see cref="CanonicalBook"/> and
/// its chapter and verse are required — and an observance the text dates twice, as Purim is on the
/// fourteenth and the fifteenth, carries the row it is dated by.
/// </para>
///
/// <para>
/// Several rows are several positions: the four fasts are one observance in four months.
/// </para>
/// </summary>
[Index(nameof(EntityId))]
public class ObservanceTime
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    public int EntityId { get; set; }

    public Entity? Entity { get; set; }

    /// <summary>One of <see cref="ObservanceCycles"/>.</summary>
    public required string Cycle { get; set; }

    /// <summary>The month, counted from Abib, where the text names one.</summary>
    public int? Month { get; set; }

    /// <summary>
    /// The day of that month, or for a weekly observance the day of the week, where the text names one.
    /// </summary>
    public int? Day { get; set; }

    /// <summary>The last day of the month it runs to, where it runs longer than one.</summary>
    public int? LastDay { get; set; }

    public int CanonicalBook { get; set; }

    public int CanonicalChapter { get; set; }

    public int CanonicalVerse { get; set; }

    public string? Note { get; set; }

    public required string Source { get; set; }

    public override string ToString() => $"ObservanceTime({EntityId} {Cycle} {Month}/{Day})";
}
