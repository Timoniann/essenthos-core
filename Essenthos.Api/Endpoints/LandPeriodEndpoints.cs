using System.Text.Json;
using Essenthos.Core.Database;
using Microsoft.EntityFrameworkCore;

namespace Essenthos.Core.Endpoints;

/// <summary>
/// The periods of the lands around the Bible — Egypt's New Kingdom, the Late Bronze Age of the
/// Levant — as each scholar who defines one dates it, for the timeline to draw behind its events.
///
/// A sibling of <c>/v1/timeline</c> rather than a part of it: fifteen hundred bands that a reader
/// may switch off have no business delaying the first picture. Nothing is resolved here. The same
/// name under five authorities comes back as five periods, each with its own ends and its own
/// authority, because the disagreement is what the band is drawn to show.
/// </summary>
internal static class LandPeriodEndpoints
{
    /// <summary>Where a PeriodO identifier resolves.</summary>
    private const string Resolver = "http://n2t.net/ark:/99152/";

    public static void MapLandPeriods(this IEndpointRouteBuilder routes)
    {
        routes.MapGet("/timeline/lands", async (AppDbContext db, CancellationToken cancellationToken) =>
            Results.Ok(await All(db, cancellationToken)));
    }

    internal static async Task<TimelineLandsResponse> All(AppDbContext db, CancellationToken cancellationToken)
    {
        var authorities = await db.PeriodAuthorities
            .OrderBy(a => a.PeriodoId)
            .Select(a => new LandAuthorityResponse(
                a.PeriodoId, a.Attribution, a.Citation, a.YearPublished, a.Uri ?? Resolver + a.PeriodoId))
            .ToListAsync(cancellationToken);

        var periods = await db.PeriodDefinitions
            .OrderBy(p => p.StartEarliest).ThenBy(p => p.StopLatest).ThenBy(p => p.PeriodoId)
            .Select(p => new
            {
                p.PeriodoId,
                Authority = p.Authority!.PeriodoId,
                p.Label,
                p.Labels,
                p.Region,
                p.CoverageDescription,
                p.StartLabel,
                p.StartEarliest,
                p.StartLatest,
                p.StopLabel,
                p.StopEarliest,
                p.StopLatest,
            })
            .ToListAsync(cancellationToken);

        return new TimelineLandsResponse(
            authorities,
            [
                .. periods.Select(p => new LandPeriodResponse(
                    p.PeriodoId,
                    p.Authority,
                    p.Label,
                    Labels(p.Labels),
                    p.Region,
                    p.CoverageDescription,
                    p.StartLabel,
                    [p.StartEarliest, p.StartLatest],
                    p.StopLabel,
                    [p.StopEarliest, p.StopLatest],
                    Resolver + p.PeriodoId)),
            ]);
    }

    private static IDictionary<string, string> Labels(string? stored)
    {
        var labels = new Dictionary<string, string>(StringComparer.Ordinal);
        if (string.IsNullOrEmpty(stored))
        {
            return labels;
        }

        using var document = JsonDocument.Parse(stored);
        foreach (var language in document.RootElement.EnumerateObject())
        {
            if (language.Value.GetString() is { } label)
            {
                labels[language.Name] = label;
            }
        }

        return labels;
    }
}

/// <summary>The periods of the lands and the works that define them.</summary>
internal record TimelineLandsResponse(
    IList<LandAuthorityResponse> Authorities,
    IList<LandPeriodResponse> Periods);

/// <summary>
/// A work that defines periods. <c>Attribution</c> is what follows a date — surnames, title, year —
/// and <c>Citation</c> the reference in full.
/// </summary>
internal record LandAuthorityResponse(
    string Id,
    string Attribution,
    string Citation,
    int? Year,
    string Uri);

/// <summary>
/// One authority's period in one land. The years are astronomical — <c>-1549</c> is 1550 BCE — and
/// each end is the earliest and the latest it may be, equal where the source gives one year.
/// <c>Labels</c> holds the source's own name in each language it has one in.
/// </summary>
internal record LandPeriodResponse(
    string Id,
    string Authority,
    string Label,
    IDictionary<string, string> Labels,
    string Region,
    string? Place,
    string? StartLabel,
    int[] Start,
    string? StopLabel,
    int[] Stop,
    string Uri);
