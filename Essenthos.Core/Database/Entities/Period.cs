using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Essenthos.Core.Database.Entities;

/// <summary>
/// A stretch of time with a name — an era, a reign, a captivity, a life.
///
/// A timeline of 572 points is a field of dots, and no amount of zooming turns dots into a
/// history. What makes one legible is the bands behind the dots: the reader sees *the divided
/// kingdom* before reading a single event, and every dot inside it is then read as belonging to
/// something. This is the layer that carries the meaning.
///
/// Deliberately not years. A period is anchored to the two events that open and close it, and the
/// years are resolved from whichever chronology the reader has chosen — so switching to Ussher
/// moves the bands as well as the dots, and a band never asserts a date its own reckoning does not
/// hold. Where a period has no anchors (a fixed date from an outside authority) it carries its own
/// years and stands outside the chronologies.
/// </summary>
[Index(nameof(Slug), IsUnique = true)]
[Index(nameof(Level))]
public class Period
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    public required string Slug { get; set; }

    public required string Name { get; set; }

    /// <summary>
    /// What sort of stretch it is — <c>era</c>, <c>reign</c>, <c>ministry</c>, <c>oppression</c>,
    /// <c>life</c>, <c>construction</c>, <c>captivity</c>, <c>span</c>. The colour and the filter
    /// both read this.
    /// </summary>
    public string? Kind { get; set; }

    /// <summary>
    /// Which row it draws on: 0 the eras, 1 the spans of rule and captivity that order the
    /// narrative, 2 the lives and ministries, of which there are many more and which overlap
    /// constantly. Each level gets its own band, which is what makes them readable rather than a
    /// pile.
    ///
    /// <b>A band, not a depth.</b> Every period hangs off an era whatever its level, so a level-2
    /// life is the child of a level-0 era and nothing sits under level 1. The number chooses a row;
    /// <see cref="Parent"/> says what the period belongs to.
    /// </summary>
    public int Level { get; set; }

    /// <summary>
    /// The era it opens in — which is not always the era that contains it.
    ///
    /// The eras are contiguous and a band may run past the close of the one it starts in: a
    /// lifespan crosses the Flood because the man outlived it, and the 430 years from the promise
    /// to the covenant end two eras after they begin. For those there is no containing era to point
    /// at, so the parent records where the band opens and <see cref="Notes"/> says how far past its
    /// era it runs. Nesting a child bar inside its parent's bar without reading the years will draw
    /// it outside.
    /// </summary>
    public int? ParentId { get; set; }
    public Period? Parent { get; set; }

    /// <summary>Whose it is, where it belongs to somebody — a reign, a ministry, a life.</summary>
    public int? EntityId { get; set; }
    public Entity? Entity { get; set; }

    public int? StartEventId { get; set; }
    public Event? StartEvent { get; set; }

    public int? EndEventId { get; set; }
    public Event? EndEvent { get; set; }

    /// <summary>
    /// The years, for a period with no anchors. Left null when the anchors carry them, so that a
    /// stale copy can never disagree with the events it is drawn from.
    /// </summary>
    public int? StartYear { get; set; }

    public int? EndYear { get; set; }

    /// <summary>Which history this belongs to — <c>scripture</c> or <c>world</c>.</summary>
    public string Realm { get; set; } = Realms.Scripture;

    /// <summary>Where in the world, where the source says.</summary>
    public string? Region { get; set; }

    /// <summary>Where to go and check it.</summary>
    public string? Uri { get; set; }

    public string? Notes { get; set; }

    public required string Source { get; set; }

    public override string ToString() => $"Period({Slug})";
}
