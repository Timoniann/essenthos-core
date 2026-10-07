using Essenthos.Core.Corpus;
using Essenthos.Core.Database;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Essenthos.Core.Endpoints;

/// <summary>
/// The original words a translation renders the most different ways: where translation choice did
/// the most work. Read from what the load counted for every number and text
/// (<c>strong_reach.renderings</c>), so a page of it is one index range and a sort, never a count.
/// </summary>
internal static class VariedRenderingEndpoints
{
    private const int MostPerPage = 100;

    /// <summary>
    /// Fewer links than this and a number cannot be rendered many ways, only rarely: ranked by share
    /// it is a word read twice in two ways, which says nothing.
    /// </summary>
    internal const int DefaultFloor = 20;

    public static void MapVariedRenderings(this IEndpointRouteBuilder routes) =>
        routes.MapGet("/strong/varied", async (
            [FromQuery] string? corpus,
            [FromQuery] string? language,
            [FromQuery] int? min,
            [FromQuery] string? sort,
            [FromQuery] int? skip,
            [FromQuery] int? take,
            AppDbContext db,
            ICanonIndex canon,
            CancellationToken cancellationToken) =>
        {
            var named = corpus is { Length: > 0 } ? corpus : StrongRenderingCounts.CardTranslation;
            if (await canon.Text(named, cancellationToken) is not { } text)
            {
                return Results.NotFound(new ProblemResponse(
                    $"There is no text \"{named}\". Ask /v1/corpora for the ones this corpus holds."));
            }

            var letter = language is { Length: > 0 }
                ? language.StartsWith('g') || language.StartsWith('G') ? "G" : "H"
                : null;
            return Results.Ok(await Page(
                db, text.Id, text.Slug, letter, Math.Max(1, min ?? DefaultFloor), sort == "share",
                Math.Max(0, skip ?? 0), Math.Clamp(take ?? 50, 1, MostPerPage), cancellationToken));
        }).RequireRateLimiting(RateLimits.Expensive);

    internal static async Task<VariedRenderingsResponse> Page(
        AppDbContext db,
        int textId,
        string slug,
        string? letter,
        int floor,
        bool byShare,
        int skip,
        int take,
        CancellationToken cancellationToken)
    {
        var counted = await db.StrongReaches.AnyAsync(r => r.TextId == textId && r.Renderings != null, cancellationToken);

        var rows = db.StrongReaches.AsNoTracking()
            .Where(r => r.TextId == textId && r.Renderings != null && r.RenderingLinks >= floor && r.Lexical != false);
        if (letter is not null)
        {
            rows = rows.Where(r => r.StrongNumber.StartsWith(letter));
        }

        var total = await rows.CountAsync(cancellationToken);
        var ordered = byShare
            ? rows.OrderByDescending(r => (double)r.Renderings! / r.RenderingLinks).ThenByDescending(r => r.Renderings)
            : rows.OrderByDescending(r => r.Renderings).ThenByDescending(r => r.RenderingLinks);
        var page = await ordered
            .ThenBy(r => r.StrongNumber)
            .Skip(skip)
            .Take(take)
            .Select(r => new
            {
                r.StrongNumber,
                Witness = r.Witness!.Slug,
                r.Occurrences,
                r.Reached,
                r.RenderingLinks,
                Renderings = r.Renderings!.Value,
                r.Phrases,
            })
            .ToListAsync(cancellationToken);

        var numbers = page.Select(r => r.StrongNumber).ToList();
        var entries = await db.StrongEntries
            .Where(e => numbers.Contains(e.StrongNumber))
            .Select(e => new { e.StrongNumber, e.Lemma, e.Transliteration, e.Definition })
            .ToDictionaryAsync(e => e.StrongNumber, cancellationToken);
        var commonest = await StrongEndpoints.Renderings(db, numbers, textId, StrongRenderingCounts.CardRenderings, cancellationToken);

        return new VariedRenderingsResponse(
            slug,
            counted,
            floor,
            byShare ? "share" : "renderings",
            total,
            skip,
            [
                .. page.Select(r => new VariedRenderingResponse(
                    r.StrongNumber,
                    entries.GetValueOrDefault(r.StrongNumber)?.Lemma,
                    entries.GetValueOrDefault(r.StrongNumber)?.Transliteration,
                    entries.GetValueOrDefault(r.StrongNumber)?.Definition,
                    r.Witness,
                    r.Occurrences,
                    r.Reached,
                    r.RenderingLinks,
                    r.Renderings,
                    r.Phrases,
                    commonest.GetValueOrDefault(r.StrongNumber, []))),
            ]);
    }
}

/// <param name="Counted">
/// Whether this text's renderings were counted at all. False for a language with no list of the words
/// it spends on grammar: its numbers are not ranked, which is not the same as none being varied.
/// </param>
/// <param name="Floor">The fewest links with a word of their own a number must have to be ranked.</param>
/// <param name="Sort"><c>renderings</c>, the most distinct renderings first, or <c>share</c>, the most per link.</param>
internal record VariedRenderingsResponse(
    string Corpus,
    bool Counted,
    int Floor,
    string Sort,
    int Total,
    int Skip,
    IList<VariedRenderingResponse> Items);

/// <param name="Witness">The edition of the original the number is counted in.</param>
/// <param name="Occurrences">Its words in that edition.</param>
/// <param name="Reached">Those a link of the text renders.</param>
/// <param name="RenderingLinks">The links whose phrase has a word of its own: the denominator.</param>
/// <param name="Renderings">Distinct renderings once grammar words are set aside and the rest stemmed.</param>
/// <param name="Phrases">Distinct phrases exactly as printed.</param>
/// <param name="Commonest">The commonest phrases, as the lexicon's cards quote them.</param>
internal record VariedRenderingResponse(
    string StrongNumber,
    string? Lemma,
    string? Transliteration,
    string? Definition,
    string Witness,
    int Occurrences,
    int Reached,
    int RenderingLinks,
    int Renderings,
    int Phrases,
    IList<StrongRenderingResponse> Commonest);
