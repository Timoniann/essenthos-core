using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Loading;
using Microsoft.EntityFrameworkCore;

namespace Essenthos.Core.Endpoints;

internal static class HealthEndpoints
{
    public static void MapHealth(this IEndpointRouteBuilder routes)
    {
        routes.MapGet("/health", async (AppDbContext db, DatasetStatus dataset, CancellationToken cancellationToken) =>
        {
            var reachable = await db.Database.CanConnectAsync(cancellationToken);
            if (!reachable)
            {
                return Results.Json(
                    new HealthResponse("degraded", null, ["the database is not reachable"], false, []),
                    statusCode: StatusCodes.Status503ServiceUnavailable);
            }

            var texts = await db.Texts
                .OrderBy(t => t.Slug)
                .Select(t => new { t.Slug, t.Kind })
                .ToListAsync(cancellationToken);

            var counts = new DatasetCountsResponse(
                await db.Words.CountAsync(w => w.Text!.Kind != TextKind.Translation, cancellationToken),
                texts.Count(t => t.Kind == TextKind.Translation),
                0,
                0,
                0);

            return Results.Ok(new HealthResponse(
                Status(dataset),
                counts,
                Missing(dataset, texts.Count),
                dataset.State == DatasetState.Ready,
                texts.Select(t => t.Slug).ToList()));
        });
    }

    /// <summary>
    /// The contract's three words. A load that failed is <c>degraded</c> rather than
    /// <c>loading</c>, because a client that polls a failed load waits forever.
    /// </summary>
    private static string Status(DatasetStatus dataset) => dataset.State switch
    {
        DatasetState.Ready => "ready",
        DatasetState.Failed => "degraded",
        _ => "loading",
    };

    private static IList<string> Missing(DatasetStatus dataset, int texts) => dataset.State switch
    {
        DatasetState.Failed => [dataset.Detail ?? "the dataset load failed; the API's own log has the cause"],
        DatasetState.Ready when texts == 0 => ["the load finished and wrote nothing"],
        DatasetState.Ready => [],
        _ => [dataset.Detail is null ? "the dataset is loading" : $"loading {dataset.Detail}"],
    };
}

/// <param name="Loaded">Whether the load has finished. <c>status</c> says the same thing in words.</param>
/// <param name="Texts">Which texts are in the corpus, so a client need not ask a second endpoint.</param>
internal record HealthResponse(
    string Status,
    DatasetCountsResponse? Dataset,
    IList<string> Missing,
    bool Loaded,
    IList<string> Texts);

internal record DatasetCountsResponse(
    int OriginalWords,
    int Translations,
    int StrongEntries,
    int People,
    int Places);
