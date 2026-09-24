using System.Text.Json;
using Essenthos.Core.Database;
using Microsoft.EntityFrameworkCore;

namespace Essenthos.Core.Endpoints;

/// <summary>
/// How often two sources that both answered for a word named the same words, per pair of texts.
///
/// A link's method says how it was made and its confidence says how sure the process was; neither
/// says how often a link made that way turns out to agree with somebody else's answer. The last
/// measurement of the corpus already counts that — a word two sources linked, each once, either to
/// the same words (<c>agreed</c>) or to different ones — and this serves it on its own, a few
/// hundred bytes rather than the whole report, so a reader can be told what the aligner's number is
/// worth where it was checked, and that it was not checked where no second source answered.
/// </summary>
internal static class LinkCheckEndpoints
{
    /// <summary>
    /// The fewest checked words a pair is reported on. Below it the check is a sample of exceptions
    /// rather than of the links: measured on 2026-09-24, every pair with fewer than a thousand
    /// checked words agreed on none of them, while every pair above it agreed on 35 to 100 in 100,
    /// and *agreed 0 in 100* would describe a few hundred odd words as though it described the text.
    /// </summary>
    internal const int FewestChecked = 1000;

    public static void MapLinkChecks(this IEndpointRouteBuilder routes)
    {
        routes.MapGet("/link-checks", async (AppDbContext db, CancellationToken cancellationToken) =>
        {
            var latest = await db.VerificationRuns
                .OrderByDescending(v => v.RanAt)
                .FirstOrDefaultAsync(cancellationToken);

            return latest is null
                ? Results.NotFound(new ProblemResponse("the corpus has not been measured yet"))
                : Results.Ok(new LinkChecksResponse(latest.RanAt, Checks(latest.Measures.RootElement)));
        });
    }

    /// <summary>
    /// The contention rows that checked enough to say anything. A pair no second source answered
    /// for is left out rather than sent as nought of nought: absent is the claim that nothing was
    /// checked.
    /// </summary>
    internal static IList<LinkCheckResponse> Checks(JsonElement measures)
    {
        if (!measures.TryGetProperty("contention", out var rows) || rows.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var checks = new List<LinkCheckResponse>();
        foreach (var row in rows.EnumerateArray())
        {
            var agreed = Count(row, "corroborated");
            var disputed = Count(row, "disputed");
            if (agreed + disputed < FewestChecked
                || row.GetProperty("text").GetString() is not { } text
                || row.GetProperty("against").GetString() is not { } witness)
            {
                continue;
            }

            checks.Add(new LinkCheckResponse(text, witness, agreed + disputed, agreed));
        }

        return checks;
    }

    private static int Count(JsonElement row, string name) =>
        row.TryGetProperty(name, out var value) && value.TryGetInt32(out var count) ? count : 0;
}

/// <param name="MeasuredAt">When the corpus was last measured, which is when these were counted.</param>
internal record LinkChecksResponse(DateTimeOffset MeasuredAt, IList<LinkCheckResponse> Pairs);

/// <param name="Text">The text whose words were linked.</param>
/// <param name="Witness">The text they were linked to.</param>
/// <param name="Checked">Words of <paramref name="Text"/> that two sources linked, each once.</param>
/// <param name="Agreed">Of those, the words both sources linked to the same words.</param>
internal record LinkCheckResponse(string Text, string Witness, int Checked, int Agreed);
