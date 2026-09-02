using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Essenthos.Core.Database.Entities;

/// <summary>
/// Someone's reckoning of when things happened.
///
/// Not a column on an event. Two columns can hold two opinions, cannot say whose they are, cannot
/// cite either, and cannot hold a third — and disagreement here is the normal case rather than the
/// exception: Ussher differs from the base reckoning in 413 of the 419 events they share, by as
/// much as 236 years, and Shulman in all 303 of his.
///
/// So a date belongs to a chronology, and a chronology belongs to whoever computed it. This is the
/// model PeriodO uses for period definitions and CIDOC-CRM for time spans, and both arrived at it
/// for the same reason: a corpus that resolves a disagreement has destroyed the thing a reader
/// came to see.
/// </summary>
[Index(nameof(Slug), IsUnique = true)]
public class Chronology
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    public required string Slug { get; set; }

    public required string Name { get; set; }

    /// <summary>Who reckoned it — Ussher, Shulman, the dataset itself, this project.</summary>
    public string? Authority { get; set; }

    /// <summary>What it rests on, in a sentence: which text, which readings, which assumptions.</summary>
    public string? Basis { get; set; }

    /// <summary>Where to go and check. A published work, an edition, a repository.</summary>
    public string? Source { get; set; }

    /// <summary>
    /// The year in this chronology that is 1 BCE. Every reckoning counts from its own creation, and
    /// they differ by more than a millennium — the Septuagint's is roughly 1,500 years earlier than
    /// the Masoretic — so a year from creation means nothing until this is known.
    /// </summary>
    public int LastYearBeforeTheCommonEra { get; set; }

    /// <summary>Which to show when nobody has chosen. Exactly one is expected to be true.</summary>
    public bool IsDefault { get; set; }

    /// <summary>Where it sits in a list, so the reader meets them in a sensible order.</summary>
    public int Position { get; set; }

    public ICollection<EventDate> Dates { get; set; } = [];

    public override string ToString() => $"Chronology({Slug})";
}

/// <summary>
/// One chronology's answer for one event.
///
/// A variant, not a correction. Several may stand against the same event and none of them is the
/// one the corpus believes — that is the reader's to decide, and the reason each carries its own
/// source.
/// </summary>
/// <remarks>
/// <see cref="EarliestYear"/> and <see cref="LatestYear"/> are for a reckoning that gives a range
/// rather than a year. CIDOC-CRM models a time span with four numbers for exactly this reason, and
/// its rule for merging conflicting sources is worth keeping in mind: the inner bounds take the
/// union, the outer bounds the intersection.
/// </remarks>
[Index(nameof(EventId), nameof(ChronologyId), IsUnique = true)]
[Index(nameof(ChronologyId))]
public class EventDate
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    public int EventId { get; set; }
    public Event? Event { get; set; }

    public int ChronologyId { get; set; }
    public Chronology? Chronology { get; set; }

    /// <summary>Years from that chronology's own creation.</summary>
    public int? Year { get; set; }

    /// <summary>The bounds, where the reckoning gives a range instead of a year.</summary>
    public int? EarliestYear { get; set; }

    public int? LatestYear { get; set; }

    /// <summary>
    /// The arithmetic that produced it, in a sentence, so the figure can be checked rather than
    /// believed. This is the field that made this dataset worth choosing.
    /// </summary>
    public string? Calculation { get; set; }

    /// <summary>The page, paragraph or line this rests on in the work it came from.</summary>
    public string? Citation { get; set; }

    public string? Notes { get; set; }

    public override string ToString() => $"EventDate(event {EventId}, chronology {ChronologyId})";
}
