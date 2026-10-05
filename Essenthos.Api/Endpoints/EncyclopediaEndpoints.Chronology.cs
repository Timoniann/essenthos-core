using Essenthos.Core.Configuration;
using Essenthos.Core.Corpus;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Database.Entities.Enums;
using Microsoft.EntityFrameworkCore;

namespace Essenthos.Core.Endpoints;

internal static partial class EncyclopediaEndpoints
{
    /// <summary>The periods, the reckonings and the timeline.</summary>
    private static void MapChronology(IEndpointRouteBuilder routes)
    {
        // One period: what it is, what opens and closes it, and what each reckoning makes of those.
        routes.MapGet("/periods/{slug}", async (
            string slug,
            AppDbContext db,
            CancellationToken cancellationToken) =>
        {
            var period = await db.Periods
                .Where(p => p.Slug == slug)
                .Select(p => new
                {
                    p.Slug,
                    p.Name,
                    p.Kind,
                    p.Level,
                    p.Realm,
                    p.Region,
                    p.Uri,
                    p.Notes,
                    p.Source,
                    Parent = p.Parent == null ? null : new { p.Parent.Slug, p.Parent.Name },
                    Entity = p.Entity == null ? null : new { p.Entity.Slug, p.Entity.Name, p.Entity.Distinguisher },
                    Opens = p.StartEvent == null ? null : new { p.StartEvent.Slug, p.StartEvent.Name },
                    Closes = p.EndEvent == null ? null : new { p.EndEvent.Slug, p.EndEvent.Name },
                    p.StartEventId,
                    p.EndEventId,
                    p.StartYear,
                    p.EndYear,
                })
                .FirstOrDefaultAsync(cancellationToken);

            if (period is null)
            {
                return Results.NotFound();
            }

            var years = await EventYears(
                db,
                db.EventDates.Where(d => d.EventId == period.StartEventId || d.EventId == period.EndEventId),
                cancellationToken);

            var children = await db.Periods
                .Where(p => p.Parent!.Slug == slug)
                .OrderBy(p => p.StartYear)
                .Select(p => new PeriodRefResponse(p.Slug, p.Name, p.Kind, p.StartYear, p.EndYear))
                .Take(60)
                .ToListAsync(cancellationToken);

            return Results.Ok(new PeriodResponse(
                period.Slug,
                period.Name,
                period.Kind,
                period.Level,
                period.Realm,
                period.Region,
                period.Uri,
                period.Notes,
                period.Source,
                Datasets.Of(period.Source),
                period.Parent is null ? null : new PeriodRefResponse(
                    period.Parent.Slug, period.Parent.Name, null, null, null),
                period.Entity is null ? null : new NamedEntityResponse(
                    period.Entity.Slug, period.Entity.Name, period.Entity.Distinguisher),
                period.Opens is null ? null : new EventRefResponse(period.Opens.Slug, period.Opens.Name),
                period.Closes is null ? null : new EventRefResponse(period.Closes.Slug, period.Closes.Name),
                children)
            {
                Years = Span(years, period.StartEventId, period.EndEventId, period.StartYear, period.EndYear),
            });
        });

        // The reckonings on their own, for a page that has to name or format one and has no
        // reason to hold a chronology's worth of dates to do it.
        routes.MapGet("/chronologies", async (AppDbContext db, CancellationToken cancellationToken) =>
            Results.Ok(new ChronologyListResponse(
                LastYearBeforeChrist, await Chronologies(db, cancellationToken))));

        // Every period with its span in each reckoning: the timeline without its fifteen hundred
        // events, which is all a page placing one year or naming one era needs.
        routes.MapGet("/periods", async (AppDbContext db, CancellationToken cancellationToken) =>
        {
            var anchoring = db.EventDates.Where(d => db.Periods.Any(
                p => p.StartEventId == d.EventId || p.EndEventId == d.EventId));
            var years = await EventYears(db, anchoring, cancellationToken);

            return Results.Ok(new PeriodListResponse(
                LastYearBeforeChrist,
                await Periods(db, years, cancellationToken)));
        });

        // Every event at once, trimmed to what a timeline draws with.
        //
        // The paged endpoint caps at a hundred, so a timeline would make six round trips for the
        // 572 events and more as the corpus grows — and it would make them again on every zoom.
        // That is the shape that stalls. Trimmed, the whole set is 58 KB, which is fetched once
        // and never fetched again, so panning and zooming touch no network at all.
        //
        // When world history arrives and this becomes megabytes, a windowed request earns its
        // complexity. Not before.
        routes.MapGet("/timeline", async (
            AppDbContext db,
            SiteSettingsFile settings,
            CancellationToken cancellationToken) =>
        {
            var events = await InOrder(db.Events)
                .Select(e => new
                {
                    e.Id,
                    e.Slug,
                    e.Name,
                    e.Kind,
                    e.Realm,
                    e.Region,
                    e.RegionAtTheTime,
                    e.Uri,
                    e.SequenceInYear,
                    Location = EventLocation.Of(e.Location, e.LocationBook, e.LocationChapter, e.LocationVerse),
                    EntitySlug = e.Entity == null ? null : e.Entity.Slug,
                })
                .ToListAsync(cancellationToken);

            var years = await EventYears(db, db.EventDates, cancellationToken);
            var periods = await Periods(db, years, cancellationToken);

            var pictures = await TimelinePictures.Of(
                db,
                [.. events.Select(e => (e.EntitySlug, e.Location))],
                periods.Select(p => p.EntitySlug),
                settings.Is(SiteSettings.GeneratedImages),
                cancellationToken);

            return Results.Ok(new TimelineResponse(
                LastYearBeforeChrist,
                await Chronologies(db, cancellationToken),
                [
                    .. events.Select(e => new TimelineEventResponse(
                        e.Slug,
                        e.Name,
                        e.Kind,
                        e.Realm,
                        e.Region,
                        e.Uri,
                        e.EntitySlug,
                        e.SequenceInYear != null,
                        years.GetValueOrDefault(e.Id) ?? [])
                    {
                        Picture = pictures.ForEvent(e.EntitySlug, e.Location),
                        RegionAtTheTime = e.RegionAtTheTime,
                    }),
                ],
                [.. periods.Select(p => p with { Picture = pictures.ForPeriod(p.EntitySlug) })]));
        });
    }

    private static async Task<IList<ChronologyResponse>> Chronologies(
        AppDbContext db,
        CancellationToken cancellationToken) =>
    [
        .. (await db.Chronologies.OrderBy(c => c.Position).ToListAsync(cancellationToken))
            .Select(c => new ChronologyResponse(
                c.Slug, c.Name, c.Authority, c.Basis, c.Source, c.LastYearBeforeTheCommonEra, c.IsDefault)),
    ];

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

    internal static async Task<IList<TimelinePeriodResponse>> Periods(
        AppDbContext db,
        Dictionary<int, Dictionary<string, int>> years,
        CancellationToken cancellationToken)
    {
        var periods = await db.Periods
            .OrderBy(p => p.Level).ThenBy(p => p.StartYear)
            .Select(p => new
            {
                p.Slug,
                p.Name,
                p.Kind,
                p.Level,
                p.Realm,
                p.Region,
                p.Uri,
                ParentSlug = p.Parent == null ? null : p.Parent.Slug,
                EntitySlug = p.Entity == null ? null : p.Entity.Slug,
                p.Notes,
                p.StartEventId,
                p.EndEventId,
                p.StartYear,
                p.EndYear,
            })
            .ToListAsync(cancellationToken);

        return
        [
            .. periods.Select(p => new TimelinePeriodResponse(
                p.Slug,
                p.Name,
                p.Kind,
                p.Level,
                p.Realm,
                p.Region,
                p.Uri,
                p.ParentSlug,
                p.EntitySlug,
                p.Notes,
                Span(years, p.StartEventId, p.EndEventId, p.StartYear, p.EndYear))),
        ];
    }

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

    /// <summary>
    /// The lexicon entries stored against a name, one per word of it.
    ///
    /// Trimmed and emptied out, because the column holds them as one comma-separated string written
    /// with spaces — <c>"H1, NONE"</c> — and splitting it without trimming sends <c>" NONE"</c> to
    /// every client, each of which then has to know that. A word the source has no entry for stays
    /// in the list as the source wrote it: the position matters, and a name is easier to read with
    /// a gap named than with a gap.
    /// </summary>
    private static IList<string> Numbers(string? stored) =>
        stored is { Length: > 0 }
            ? [.. stored.Split(',').Select(entry => entry.Trim()).Where(entry => entry.Length > 0)]
            : [];
}
