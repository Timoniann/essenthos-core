using Essenthos.Core.Accounts;
using Microsoft.EntityFrameworkCore;

namespace Essenthos.Core.Endpoints;

/// <summary>
/// The bookmark colours a reader reaches for, kept with the account so each of their devices offers
/// the same ones.
///
///     GET /v1/me/bookmark-colors    { recent, own }
///     PUT /v1/me/bookmark-colors    { recent?, own? } — each list given replaces the kept one
///
/// <c>recent</c> is newest first and holds named colours and <c>#rrggbb</c> alike, at most
/// <see cref="Limits.RecentBookmarkColors"/>; <c>own</c> holds only <c>#rrggbb</c>, in the order the
/// reader added them, at most <see cref="Limits.OwnBookmarkColors"/>. A colour named twice is kept
/// once, where it was first named. The client sends the whole list, so a browser that kept colours
/// before signing in hands the account the two lists merged.
/// </summary>
internal static class BookmarkColorEndpoints
{
    public static void MapBookmarkColors(this IEndpointRouteBuilder routes)
    {
        var colors = routes.MapGroup("/me/bookmark-colors").RequireAuthorization();

        colors.MapGet("", async (HttpContext context, AccountsDbContext db) =>
        {
            var account = context.User.AccountId();
            var kept = await db.Accounts.AsNoTracking()
                .Where(a => a.Id == account)
                .Select(a => new BookmarkColorsResponse(a.RecentBookmarkColors, a.OwnBookmarkColors))
                .FirstOrDefaultAsync(context.RequestAborted);
            return kept is null ? Results.Unauthorized() : Results.Ok(kept);
        });

        colors.MapPut("", async (HttpContext context, AccountsDbContext db, BookmarkColorsPut put) =>
        {
            var recent = Read(put.Recent, own: false);
            var own = Read(put.Own, own: true);
            if (recent is { Error: { } recentError })
            {
                return Results.BadRequest(new ProblemResponse(recentError));
            }

            if (own is { Error: { } ownError })
            {
                return Results.BadRequest(new ProblemResponse(ownError));
            }

            var account = context.User.AccountId();
            if (await db.Accounts.FirstOrDefaultAsync(a => a.Id == account, context.RequestAborted) is not { } kept)
            {
                return Results.Unauthorized();
            }

            if (recent?.Colors is { } recentColors && !recentColors.SequenceEqual(kept.RecentBookmarkColors))
            {
                kept.RecentBookmarkColors = recentColors;
            }

            if (own?.Colors is { } ownColors && !ownColors.SequenceEqual(kept.OwnBookmarkColors))
            {
                kept.OwnBookmarkColors = ownColors;
            }

            await db.SaveChangesAsync(context.RequestAborted);
            return Results.Ok(new BookmarkColorsResponse(kept.RecentBookmarkColors, kept.OwnBookmarkColors));
        });
    }

    /// <summary>
    /// A list as the account keeps it — each colour once, in its kept spelling — or what is wrong with
    /// it; null for a list not sent.
    /// </summary>
    internal static (List<string>? Colors, string? Error)? Read(IReadOnlyList<string?>? sent, bool own)
    {
        if (sent is null)
        {
            return null;
        }

        var limit = own ? Limits.OwnBookmarkColors : Limits.RecentBookmarkColors;
        var kept = new List<string>();
        foreach (var color in sent)
        {
            var spelled = color is null ? null : BookmarkEndpoints.Color(color);
            if (spelled is null || (own && spelled[0] != '#'))
            {
                return (null, own ? OwnRule : BookmarkEndpoints.ColorRule);
            }

            if (!kept.Contains(spelled))
            {
                kept.Add(spelled);
            }
        }

        return kept.Count > limit
            ? (null, own
                ? $"An account keeps at most {Limits.OwnBookmarkColors} colours of its own. Remove some to make room."
                : $"An account remembers at most {Limits.RecentBookmarkColors} recent colours.")
            : (kept, null);
    }

    private const string OwnRule = "A colour of one's own is written #rrggbb.";
}

internal record BookmarkColorsPut(IReadOnlyList<string?>? Recent, IReadOnlyList<string?>? Own);

internal record BookmarkColorsResponse(IReadOnlyList<string> Recent, IReadOnlyList<string> Own);
