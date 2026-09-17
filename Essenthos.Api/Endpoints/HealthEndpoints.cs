using System.Text.Json;
using Essenthos.Core.Corpus;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities.Enums;
using Microsoft.EntityFrameworkCore;

namespace Essenthos.Core.Endpoints;

/// <summary>
/// Three questions, because they have three different answers and three different costs.
///
/// <c>/health/live</c> asks whether the process is up and touches nothing. It is what a container
/// runtime polls, and it must never depend on the database: a healthy process whose database is
/// briefly away should not be killed and replaced, because the replacement will not help.
///
/// <c>/health/ready</c> asks whether this instance can serve, which is one round trip. It is what a
/// proxy asks before sending traffic.
///
/// <c>/health</c> is the report: what is in the corpus, and what the measures said about it. It
/// counts millions of rows, so it is answered from a cache rather than recomputed for every caller.
/// </summary>
internal static class HealthEndpoints
{
    /// <summary>
    /// How long the report is reused. A corpus never changes under a running API in a deployment —
    /// a release is restored into a database of its own and the process is pointed at it — so this
    /// could be forever there. It is a minute because on the machine that builds the corpus the
    /// loader fills the same database this is reading, and a report that never notices would be
    /// wrong for the whole session.
    /// </summary>
    private static readonly TimeSpan ReportHeldFor = TimeSpan.FromMinutes(1);

    private static readonly SemaphoreSlim ReportGate = new(1, 1);

    private static HealthResponse? report;

    private static DateTimeOffset reportedAt;

    public static void MapHealth(this IEndpointRouteBuilder routes)
    {
        // Deliberately the cheapest endpoint in the API. No database, no query, no allocation worth
        // the name.
        routes.MapGet("/health/live", () => Results.Ok(new HealthProbeResponse("live")));

        routes.MapGet("/health/ready", async (AppDbContext db, CancellationToken cancellationToken) =>
            await db.Database.CanConnectAsync(cancellationToken)
                ? Results.Ok(new HealthProbeResponse("ready"))
                : Results.Json(
                    new HealthProbeResponse("the database is not reachable"),
                    statusCode: StatusCodes.Status503ServiceUnavailable));

        routes.MapGet("/health", async (AppDbContext db, CancellationToken cancellationToken) =>
        {
            if (Held() is { } held)
            {
                return Results.Ok(held);
            }

            await ReportGate.WaitAsync(cancellationToken);
            try
            {
                if (Held() is { } raced)
                {
                    return Results.Ok(raced);
                }

                if (!await db.Database.CanConnectAsync(cancellationToken))
                {
                    return Results.Json(
                        new HealthResponse("degraded", null, ["the database is not reachable"], false, [], null),
                        statusCode: StatusCodes.Status503ServiceUnavailable);
                }

                report = await Report(db, cancellationToken);
                reportedAt = DateTimeOffset.UtcNow;
                return Results.Ok(report);
            }
            finally
            {
                ReportGate.Release();
            }
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

    private static HealthResponse? Held() =>
        report is { } held && DateTimeOffset.UtcNow - reportedAt < ReportHeldFor ? held : null;

    private static async Task<HealthResponse> Report(AppDbContext db, CancellationToken cancellationToken)
    {
        var texts = await db.Texts
            .OrderBy(t => t.Slug)
            .Select(t => new { t.Slug, t.Kind })
            .ToListAsync(cancellationToken);

        var counts = new DatasetCountsResponse(
            await db.Words.CountAsync(w => w.Text!.Kind != TextKind.Translation, cancellationToken),
            texts.Count(t => t.Kind == TextKind.Translation),
            await db.StrongEntries.CountAsync(cancellationToken),
            await db.Entities.CountAsync(e => e.Kind == EntityKind.Person, cancellationToken),
            await db.Entities.CountAsync(e => e.Kind == EntityKind.Place, cancellationToken),
            await db.Entities.CountAsync(e => e.Kind == EntityKind.People, cancellationToken));

        var verified = await db.VerificationRuns
            .OrderByDescending(v => v.RanAt)
            .Select(v => new VerificationResponse(
                v.RanAt, v.Broken, v.Rendered, v.RenderedWords, v.Words))
            .FirstOrDefaultAsync(cancellationToken);

        return new HealthResponse(
            Status(texts.Count, verified),
            counts,
            Missing(texts.Count, verified),
            texts.Count > 0,
            texts.Select(t => t.Slug).ToList(),
            verified);
    }

    /// <summary>
    /// The contract's three words, now asked of the corpus rather than of a loader.
    ///
    /// This API does not load anything: a release is built elsewhere, verified, and restored into a
    /// database it is then pointed at. So <c>loading</c> is what an empty database means here, which
    /// on this machine is a load still running and on a server is a corpus that was never restored.
    /// A corpus that breaks its own integrity checks is <c>degraded</c>, because those are not
    /// measurements with a range but shapes no correct load produces.
    /// </summary>
    private static string Status(int texts, VerificationResponse? verified) => (texts, verified) switch
    {
        (0, _) => "loading",
        (_, { Broken: > 0 }) => "degraded",
        _ => "ready",
    };

    private static IList<string> Missing(int texts, VerificationResponse? verified) => (texts, verified) switch
    {
        (0, _) => ["the corpus is empty: either a load is still running, or no release has been restored here"],
        (_, null) => ["the corpus is loaded and has not been measured"],
        (_, { Broken: > 0 } broken) =>
            [$"the corpus breaks {broken.Broken} integrity checks; /v1/verification names them"],
        _ => [],
    };
}

/// <param name="Status">
/// <c>live</c> or <c>ready</c> when it is, and the reason it is not when it is not. A word rather
/// than an empty body so that a failing probe says something in a log.
/// </param>
internal record HealthProbeResponse(string Status);

/// <param name="Loaded">Whether the corpus has any text in it. <c>status</c> says the same in words.</param>
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

/// <param name="People">Persons. The name predates the kind below it and is not worth moving.</param>
/// <param name="Peoples">Nations, tribes and clans — the collectives the text speaks of as one.</param>
internal record DatasetCountsResponse(
    int OriginalWords,
    int Translations,
    int StrongEntries,
    int People,
    int Places,
    int Peoples);
