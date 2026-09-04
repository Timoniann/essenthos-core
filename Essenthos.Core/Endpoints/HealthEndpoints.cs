using System.Text.Json;
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
                    new HealthResponse("degraded", null, ["the database is not reachable"], false, [], null),
                    statusCode: StatusCodes.Status503ServiceUnavailable);
            }

            var texts = await db.Texts
                .OrderBy(t => t.Slug)
                .Select(t => new { t.Slug, t.Kind })
                .ToListAsync(cancellationToken);

            var counts = new DatasetCountsResponse(
                await db.Words.CountAsync(w => w.Text!.Kind != TextKind.Translation, cancellationToken),
                texts.Count(t => t.Kind == TextKind.Translation),
                await db.StrongEntries.CountAsync(cancellationToken),
                await db.Entities.CountAsync(e => e.Kind == EntityKind.Person, cancellationToken),
                await db.Entities.CountAsync(e => e.Kind == EntityKind.Place, cancellationToken));

            var verified = await db.VerificationRuns
                .OrderByDescending(v => v.RanAt)
                .Select(v => new VerificationResponse(
                    v.RanAt, v.Broken, v.Rendered, v.RenderedWords, v.Words))
                .FirstOrDefaultAsync(cancellationToken);

            return Results.Ok(new HealthResponse(
                Status(dataset, verified),
                counts,
                Missing(dataset, texts.Count, verified),
                dataset.State == DatasetState.Ready,
                texts.Select(t => t.Slug).ToList(),
                verified));
        });

        // The measures themselves, which are a report rather than a status: several hundred numbers
        // that nothing polls and a person reads once.
        routes.MapGet("/verification", async (AppDbContext db, CancellationToken cancellationToken) =>
        {
            var latest = await db.VerificationRuns
                .OrderByDescending(v => v.RanAt)
                .FirstOrDefaultAsync(cancellationToken);

            return latest is null
                ? Results.NotFound(new ProblemResponse("the corpus has not been measured yet"))
                : Results.Ok(new VerificationReportResponse(
                    latest.RanAt, latest.Broken, latest.Rendered, latest.Measures.RootElement));
        });
    }

    /// <summary>
    /// The contract's three words. A load that failed is <c>degraded</c> rather than
    /// <c>loading</c>, because a client that polls a failed load waits forever — and so is a load
    /// that finished over a corpus breaking its own integrity checks, because those are not
    /// measurements with a range but shapes no correct load produces.
    /// </summary>
    private static string Status(DatasetStatus dataset, VerificationResponse? verified) => dataset.State switch
    {
        DatasetState.Ready when verified is { Broken: > 0 } => "degraded",
        DatasetState.Ready => "ready",
        DatasetState.Failed => "degraded",
        _ => "loading",
    };

    private static IList<string> Missing(DatasetStatus dataset, int texts, VerificationResponse? verified) =>
        dataset.State switch
        {
            DatasetState.Failed => [dataset.Detail ?? "the dataset load failed; the API's own log has the cause"],
            DatasetState.Ready when texts == 0 => ["the load finished and wrote nothing"],
            DatasetState.Ready when verified is null => ["the corpus is loaded and has not been measured"],
            DatasetState.Ready when verified.Broken > 0 =>
                [$"the corpus breaks {verified.Broken} integrity checks; /v1/verification names them"],
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
    IList<string> Texts,
    VerificationResponse? Verified);

/// <param name="Broken">Integrity checks the corpus fails. Anything but zero is a defect.</param>
/// <param name="Rendered">
/// The share of words that reach a witness, over every text the corpus has linked to one. It is a
/// trend line and describes no text: <c>/v1/verification</c> reports coverage per section, which is
/// where a number that describes something is.
/// </param>
/// <param name="RenderedWords">
/// The numerator, and <paramref name="Words"/> the denominator, so the share can be checked rather
/// than believed.
///
/// A ratio alone cannot be reproduced or compared, and "which words did you count" has several
/// defensible answers in this corpus: words reaching any link at all, words reaching a
/// non-translation witness, and words reaching an original-language text. Two measurements a day
/// apart once differed by four points with no way to tell which question either had asked. A word
/// counts here when a link names it as rendering or equalling a word of a text that is not a
/// translation.
///
/// The denominator is the words with a counterpart to reach. A word in a verse no witness of this
/// text holds is outside it — Brenton's deuterocanon has no Hebrew anywhere in this corpus, and
/// counting its 98,670 words as unreached would report the canon as an alignment failure.
/// <c>/v1/verification</c> carries the excluded count and the per-section rows behind it.
/// </param>
internal record VerificationResponse(
    DateTimeOffset RanAt, int Broken, double Rendered, int RenderedWords, int Words);

/// <param name="Measures">
/// Coverage, reach, contention and integrity, as the check computed them. Held as it was stored
/// rather than retyped: it is a report, and a fifth measure should not be a schema change.
/// </param>
internal record VerificationReportResponse(
    DateTimeOffset RanAt,
    int Broken,
    double Rendered,
    JsonElement Measures);

internal record ProblemResponse(string Message);

internal record DatasetCountsResponse(
    int OriginalWords,
    int Translations,
    int StrongEntries,
    int People,
    int Places);
