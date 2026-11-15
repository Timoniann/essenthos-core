using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Microsoft.EntityFrameworkCore;

namespace Essenthos.Core.Context;

/// <summary>Where events and periods stand on each reckoning's axis, as the timeline and the chapter context read them.</summary>
internal static class ChronologySpans
{
    /// <summary>
    /// Where each chronology puts each of these dates' events on its own axis, keyed by chronology
    /// slug: its year from creation, except where the reckoning states its year of the common era,
    /// which places the event instead.
    ///
    /// The two differ by a year wherever a reckoning's year opens mid-way through the Julian one.
    /// Ussher's creation is his year 1 and, in his own words, 4004 BC; the axis turns a year into an
    /// era by the chronology's zero, which makes year 1 into 4003, so it is given the position that
    /// reads back as what he printed. And a date that states only its year of the common era —
    /// Ussher's passion narrative, whose anno mundi column is ten years out — is placed by that,
    /// where it would otherwise not be placed at all.
    /// </summary>
    internal static async Task<Dictionary<int, Dictionary<string, int>>> EventYears(
        AppDbContext db,
        IQueryable<EventDate> dates,
        CancellationToken cancellationToken)
    {
        var reckoning = await db.Chronologies.ToDictionaryAsync(
            c => c.Id, c => (c.Slug, Zero: c.LastYearBeforeTheCommonEra), cancellationToken);
        var rows = await dates
            .Where(d => d.Year != null || d.StatedYear != null)
            .Select(d => new { d.EventId, d.ChronologyId, d.Year, d.StatedYear })
            .ToListAsync(cancellationToken);

        var years = new Dictionary<int, Dictionary<string, int>>();
        foreach (var date in rows)
        {
            if (!years.TryGetValue(date.EventId, out var byChronology))
            {
                byChronology = [];
                years[date.EventId] = byChronology;
            }

            var (slug, zero) = reckoning[date.ChronologyId];
            byChronology[slug] = Placed(date.Year, date.StatedYear, zero);
        }

        return years;
    }

    /// <summary>
    /// A date's position on its chronology's axis. A stated year of the common era has no year zero,
    /// so 4004 BC is the axis year that reads back as 4004 BC: <paramref name="zero"/> less 4,003.
    /// </summary>
    internal static int Placed(int? year, int? statedYear, int zero) =>
        statedYear switch
        {
            < 0 => zero + statedYear.Value + 1,
            > 0 => zero + statedYear.Value,
            _ => year!.Value,
        };

    /// <summary>
    /// A period's years, in every chronology that can state both of them.
    ///
    /// Both ends or neither. A band whose start came from Ussher and whose end came from the base
    /// reckoning is a duration nobody computed, and drawing one would be the exact failure this
    /// whole model exists to avoid — Ussher is up to 278 years from the base, so such a band could
    /// be wrong by two centuries while looking authoritative.
    /// </summary>
    internal static Dictionary<string, int[]> Span(
        Dictionary<int, Dictionary<string, int>> years,
        int? startEventId,
        int? endEventId,
        int? startYear,
        int? endYear)
    {
        var span = new Dictionary<string, int[]>();

        if (startEventId is { } opens && endEventId is { } closes
            && years.TryGetValue(opens, out var from) && years.TryGetValue(closes, out var to))
        {
            foreach (var (chronology, year) in from)
            {
                if (to.TryGetValue(chronology, out var ends))
                {
                    span[chronology] = [year, ends];
                }
            }
        }

        // A period with no anchors carries its own years, and they belong to no chronology.
        if (span.Count == 0 && startYear is { } first && endYear is { } last)
        {
            span[""] = [first, last];
        }

        return span;
    }
}
