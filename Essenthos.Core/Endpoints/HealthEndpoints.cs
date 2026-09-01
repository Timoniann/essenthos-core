using Essenthos.Core.Database;
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
            var texts = reachable
                ? await db.Texts.OrderBy(t => t.Slug).Select(t => t.Slug).ToListAsync(cancellationToken)
                : [];

            var response = new HealthResponse(
                reachable && dataset.State != DatasetState.Failed ? "ok" : "degraded",
                reachable,
                dataset.State.ToString().ToLowerInvariant(),
                dataset.Detail,
                texts);

            return reachable
                ? Results.Ok(response)
                : Results.Json(response, statusCode: StatusCodes.Status503ServiceUnavailable);
        });
    }
}

/// <param name="Dataset">
/// Whether the load is still working, finished, or gave up. An API that answers 404 for everything
/// should be able to say which of those it is.
/// </param>
internal record HealthResponse(
    string Status,
    bool Database,
    string Dataset,
    string? Detail,
    IReadOnlyList<string> Texts);
