using Essenthos.Core.Configuration;
using Essenthos.Core.Corpus;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Database.Entities.Enums;
using Microsoft.AspNetCore.Mvc;
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
            [FromQuery] string? language,
            AppDbContext db,
            CancellationToken cancellationToken) =>
            await PeriodDetail(db, slug, cancellationToken, language) is { } found
                ? Results.Ok(found)
                : Results.NotFound());

        // The reckonings on their own, for a page that has to name or format one and has no
        // reason to hold a chronology's worth of dates to do it.
        routes.MapGet("/chronologies", async (AppDbContext db, CancellationToken cancellationToken) =>
            Results.Ok(new ChronologyListResponse(
                LastYearBeforeChrist, await Chronologies(db, cancellationToken)))).KeptUntilRelease();

        // Every period with its span in each reckoning: the timeline without its fifteen hundred
        // events, which is all a page placing one year or naming one era needs.
        // With a language, each period carries its name in it where its record can say it.
        routes.MapGet("/periods", async (
                [FromQuery] string? language, AppDbContext db, [FromServices] PeriodLists? lists, CancellationToken cancellationToken) =>
            Results.Ok(lists is null ? await PeriodList(db, language, cancellationToken) : await lists.Get(language, cancellationToken)));

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
            [FromQuery] string? language,
            [FromQuery] string? prose,
            AppDbContext db,
            SiteSettingsFile settings,
            CancellationToken cancellationToken) =>
        {
            var order = await InOrder(db.Events, cancellationToken);
            var unordered = await db.Events
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
            var events = Ordered(unordered, order, e => e.Slug);

            var titles = await TextTranslations.Of(
                db, events.SelectMany(e => new[] { e.Name, e.Region }), ReaderLanguages.Prose(prose, language),
                cancellationToken);
            var years = await EventYears(db, db.EventDates, cancellationToken);
            var periods = await Periods(db, years, cancellationToken);
            var periodNames = await PeriodNames.Of(
                db, [.. periods.Select(p => (p.Slug, p.Name, p.Kind, p.EntitySlug))], language, cancellationToken);

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
                        EventMarks.Unstarred(e.Name),
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
                        LocalName = EventMarks.UnstarredOrNull(TextTranslations.In(titles, e.Name)),
                        LocalRegion = TextTranslations.In(titles, e.Region),
                    }),
                ],
                [
                    .. periods.Select(p => p with
                    {
                        Picture = pictures.ForPeriod(p.EntitySlug),
                        LocalName = periodNames.GetValueOrDefault(p.Slug),
                    }),
                ]));
        }).KeptUntilRelease();
    }

    /// <summary>One period as <c>/v1/periods/{slug}</c> answers it; null where there is none.</summary>
    internal static async Task<PeriodResponse?> PeriodDetail(
        AppDbContext db,
        string slug,
        CancellationToken cancellationToken,
        string? language = null)
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
                Parent = p.Parent == null
                    ? null
                    : new { p.Parent.Slug, p.Parent.Name, p.Parent.Kind, Person = p.Parent.Entity == null ? null : p.Parent.Entity.Slug },
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
            return null;
        }

        var years = await EventYears(
            db,
            db.EventDates.Where(d => d.EventId == period.StartEventId || d.EventId == period.EndEventId),
            cancellationToken);

        var children = await db.Periods
            .Where(p => p.Parent!.Slug == slug)
            .OrderBy(p => p.StartYear)
            .Select(p => new
            {
                Ref = new PeriodRefResponse(p.Slug, p.Name, p.Kind, p.StartYear, p.EndYear),
                Person = p.Entity == null ? null : p.Entity.Slug,
            })
            .Take(60)
            .ToListAsync(cancellationToken);

        List<(string Slug, string Name, string? Kind, string? Person)> named =
        [
            (period.Slug, period.Name, period.Kind, period.Entity?.Slug),
            .. children.Select(c => (c.Ref.Slug, c.Ref.Name, c.Ref.Kind, c.Person)),
        ];
        if (period.Parent is { } outer)
        {
            named.Add((outer.Slug, outer.Name, outer.Kind, outer.Person));
        }

        var local = await PeriodNames.Of(db, named, language, cancellationToken);

        return new PeriodResponse(
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
                period.Parent.Slug, period.Parent.Name, null, null, null)
            {
                LocalName = local.GetValueOrDefault(period.Parent.Slug),
            },
            period.Entity is null ? null : new NamedEntityResponse(
                period.Entity.Slug, period.Entity.Name, period.Entity.Distinguisher),
            period.Opens is null ? null : new EventRefResponse(period.Opens.Slug, EventMarks.Unstarred(period.Opens.Name)),
            period.Closes is null ? null : new EventRefResponse(period.Closes.Slug, EventMarks.Unstarred(period.Closes.Name)),
            [.. children.Select(c => c.Ref with { LocalName = local.GetValueOrDefault(c.Ref.Slug) })])
        {
            Years = Span(years, period.StartEventId, period.EndEventId, period.StartYear, period.EndYear),
            LocalName = local.GetValueOrDefault(period.Slug),
        };
    }

    internal static async Task<IList<ChronologyResponse>> Chronologies(
        AppDbContext db,
        CancellationToken cancellationToken) =>
    [
        .. (await db.Chronologies.OrderBy(c => c.Position).ToListAsync(cancellationToken))
            .Select(c => new ChronologyResponse(
                c.Slug, c.Name, c.Authority, c.Basis, c.Source, c.LastYearBeforeTheCommonEra, c.IsDefault)),
    ];

    /// <inheritdoc cref="ChronologySpans.EventYears"/>
    internal static Task<Dictionary<int, Dictionary<string, int>>> EventYears(
        AppDbContext db, IQueryable<EventDate> dates, CancellationToken cancellationToken) =>
        ChronologySpans.EventYears(db, dates, cancellationToken);

    internal static int Placed(int? year, int? statedYear, int zero) => ChronologySpans.Placed(year, statedYear, zero);

    /// <summary>Every period with its span in each reckoning, named in <paramref name="language"/> where its record can say it.</summary>
    internal static async Task<PeriodListResponse> PeriodList(AppDbContext db, string? language, CancellationToken cancellationToken)
    {
        var anchoring = db.EventDates.Where(d => db.Periods.Any(
            p => p.StartEventId == d.EventId || p.EndEventId == d.EventId));
        var years = await EventYears(db, anchoring, cancellationToken);
        var periods = await Periods(db, years, cancellationToken);
        var named = await PeriodNames.Of(
            db, [.. periods.Select(p => (p.Slug, p.Name, p.Kind, p.EntitySlug))], language, cancellationToken);

        return new PeriodListResponse(
            LastYearBeforeChrist,
            [.. periods.Select(p => named.TryGetValue(p.Slug, out var local) ? p with { LocalName = local } : p)]);
    }

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

    /// <inheritdoc cref="ChronologySpans.Span"/>
    internal static Dictionary<string, int[]> Span(
        Dictionary<int, Dictionary<string, int>> years, int? startEventId, int? endEventId, int? startYear, int? endYear) =>
        ChronologySpans.Span(years, startEventId, endEventId, startYear, endYear);

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

/// <summary>
/// Every period as <c>/v1/periods</c> answers it, per language, read once per process: the corpus does not change under a running
/// API. An event's answer picks the periods that may frame it from here rather than reading them again.
/// </summary>
internal sealed class PeriodLists(IServiceScopeFactory scopes)
{
    private readonly PerRelease<string, PeriodListResponse> _lists = new(async (language, token) =>
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await EncyclopediaEndpoints.PeriodList(db, language.Length == 0 ? null : language, token);
    }, StringComparer.Ordinal);

    public Task<PeriodListResponse> Get(string? language, CancellationToken cancellationToken) =>
        _lists.Get(language ?? "", cancellationToken);
}
