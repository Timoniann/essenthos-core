using System.Text.Json.Serialization;
using Essenthos.Core.Database.Entities;

namespace Essenthos.Core.Context;

/// <summary>
/// The event with the two fields that need a join, read in the query rather than off a
/// navigation property. Projecting <c>e.Entity.Slug</c> through a method EF cannot translate
/// left every one of the 572 events without a person on it, while the filter that uses the
/// same navigation worked — so the timeline could select by a person and never name one.
/// </summary>
internal sealed record EventRow(
    Event Event,
    string? EntitySlug,
    string? EntityName,
    string? DefaultChronology,
    IList<EventDateResponse> Dates);

/// <summary>How an event is read with its dates, for the timeline, the event pages and the chapter context alike.</summary>
internal static class EventRows
{
    /// <summary>
    /// One vocabulary for the two sides of the turn, in the response and in the dates alike. Named
    /// rather than written twice, because they were written twice and disagreed: an event past the
    /// turn said <c>AD</c> at the top and <c>CE</c> in its dates, so a client switching on the
    /// field had three values to handle for two eras.
    /// </summary>
    public const string CommonEra = "CE";

    public const string BeforeTheCommonEra = "BCE";

    /// <summary>
    /// An event with the people and the dates it needs, projected in the query.
    ///
    /// One expression, used by both places that return events, because reading a navigation
    /// property outside the projection is what left all 572 events without a person once already
    /// while the filter over the same navigation went on working.
    /// </summary>
    internal static readonly System.Linq.Expressions.Expression<Func<Event, EventRow>> Rows =
        e => new EventRow(
            e,
            e.Entity == null ? null : e.Entity.Slug,
            e.Entity == null ? null : e.Entity.Name,
            e.Dates.Where(d => d.Chronology!.IsDefault).Select(d => d.Chronology!.Slug).FirstOrDefault(),
            e.Dates
                .OrderBy(d => d.Chronology!.Position)
                .Select(d => new EventDateResponse(
                    d.Chronology!.Slug,
                    d.Chronology.Name,
                    d.Year,
                    d.StatedYear != null
                        ? d.StatedYear < 0 ? -d.StatedYear.Value : d.StatedYear.Value
                        : d.Year == null
                            ? null
                            : d.Year <= d.Chronology.LastYearBeforeTheCommonEra
                                ? d.Chronology.LastYearBeforeTheCommonEra - d.Year.Value + 1
                                : d.Year.Value - d.Chronology.LastYearBeforeTheCommonEra,
                    d.StatedYear != null
                        ? d.StatedYear > 0 ? CommonEra : BeforeTheCommonEra
                        : d.Year != null && d.Year > d.Chronology.LastYearBeforeTheCommonEra
                            ? CommonEra
                            : BeforeTheCommonEra,
                    d.EarliestYear,
                    d.LatestYear,
                    d.Calculation,
                    d.Citation,
                    d.Notes)
                {
                    Stated = d.StatedYear != null,
                    Parts = d.CalculationParts,
                })
                .ToList());
}

/// <param name="Calculation">
/// The arithmetic that produced this reckoning's year, where the reckoning shows its working.
/// </param>
/// <param name="BceYear">
/// The year as a reader writes it, with <paramref name="Era"/> saying which side of the turn: the
/// reckoning's own figure where it states one, and otherwise worked out from
/// <paramref name="Year"/> and the reckoning's zero. <see cref="EventDateResponse.Stated"/> says
/// which. Null where the reckoning gives neither.
/// </param>
/// <param name="Year">
/// Years from the reckoning's own creation. Null where the reckoning states only its year of the
/// common era, which Ussher's Annals do for the paragraphs whose anno mundi column contradicts
/// their other two.
/// </param>
internal record EventDateResponse(
    string Chronology,
    string Name,
    int? Year,
    int? BceYear,
    string Era,
    int? EarliestYear,
    int? LatestYear,
    string? Calculation,
    string? Citation,
    string? Notes)
{
    /// <summary>
    /// Whether <see cref="BceYear"/> is the figure the reckoning's source printed, rather than one
    /// worked out from the year from creation. They differ by a year wherever the reckoning's year
    /// opens in the autumn — Ussher's creation is 4004 BC in his words and 4003 by the subtraction —
    /// and a reader is owed which one is in front of them.
    /// </summary>
    public bool Stated { get; init; }

    /// <summary>
    /// The person's age at the event by this reckoning's own years: the reckoning's year of the
    /// event less its year of the person's birth, and for BibleData the age its own row states.
    /// Null where the reckoning dates no birth of the person, or the event is not one the source
    /// gives an age for — so a year and an age shown together are always one reckoning's.
    /// </summary>
    public int? AgeAtEvent { get; init; }

    /// <summary>The arithmetic in the reader's language, a machine translation; null where none is held.</summary>
    public string? LocalCalculation { get; init; }

    /// <summary>
    /// The arithmetic in its parts, where this corpus worked it out itself, so a page can say it in the
    /// reader's language: the events' names are the reader's language's where a rendering is held.
    /// </summary>
    public Arithmetic? Working { get; init; }

    /// <summary>The parts as stored, read into <see cref="Working"/> once the row is in memory.</summary>
    [JsonIgnore]
    public string? Parts { get; init; }

    /// <summary>The notes on this reckoning's date in the reader's language, a machine translation.</summary>
    public string? LocalNotes { get; init; }
}
