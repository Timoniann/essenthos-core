using Essenthos.Core.Accounts;
using Essenthos.Core.Corpus;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Essenthos.Core.Endpoints;

/// <summary>
/// The texts a reader keeps at hand, listed first wherever texts are chosen, in an order of their own.
///
///     GET    /v1/me/favorite-texts           every one, in the reader's order
///     PUT    /v1/me/favorite-texts/{text}    makes it a favourite, last in the list; one already is stays where it is
///     DELETE /v1/me/favorite-texts/{text}
///     PUT    /v1/me/favorite-texts/order     puts the ones named first, in that order
///
/// A text is named by its slug or any spelling the corpus answers to, and kept by its canonical slug,
/// never by a corpus row id. Making one that already is changes nothing, which is what lets a browser
/// hand the account every favourite it kept before signing in, without asking which the account has.
/// An account keeps <see cref="Limits.FavoriteTextsPerAccount"/> of them.
/// </summary>
internal static class FavoriteTextEndpoints
{
    public static void MapFavoriteTexts(this IEndpointRouteBuilder routes)
    {
        var favorites = routes.MapGroup("/me/favorite-texts").RequireAuthorization();

        favorites.MapGet("", async (HttpContext context, AccountsDbContext db) =>
            Results.Ok(new FavoriteTextsResponse((await Mine(db, context.User.AccountId(), context.RequestAborted))
                .Select(Describe).ToList())));

        favorites.MapPut("/{text}", async (string text, HttpContext context, AccountsDbContext db, ICanonIndex canon) =>
        {
            if (await canon.Text(text, context.RequestAborted) is not { } entry)
            {
                return Results.BadRequest(new ProblemResponse($"There is no text \"{text}\"."));
            }

            var account = context.User.AccountId();
            var mine = await Mine(db, account, context.RequestAborted);
            if (mine.FirstOrDefault(f => Same(f.Text, entry.Slug)) is { } kept)
            {
                return Results.Ok(Describe(kept));
            }

            if (mine.Count >= Limits.FavoriteTextsPerAccount)
            {
                return Results.Conflict(new ProblemResponse(
                    $"An account keeps at most {Limits.FavoriteTextsPerAccount:N0} favourite texts. Remove some to make room."));
            }

            var favorite = new FavoriteText
            {
                Id = Guid.CreateVersion7(),
                AccountId = account,
                Text = entry.Slug,
                Position = mine.Count == 0 ? 0 : mine.Max(f => f.Position) + 1,
                CreatedAt = DateTimeOffset.UtcNow,
            };
            db.FavoriteTexts.Add(favorite);

            try
            {
                await db.SaveChangesAsync(context.RequestAborted);
            }
            catch (DbUpdateException e) when (e.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
            {
                return Results.Conflict(new ProblemResponse("This text was made a favourite by another request at the same moment. Send it again."));
            }

            return Results.Ok(Describe(favorite));
        });

        favorites.MapDelete("/{text}", async (string text, HttpContext context, AccountsDbContext db, ICanonIndex canon) =>
        {
            // A text the corpus no longer holds can still be let go of, by the slug it was kept under.
            var slug = (await canon.Text(text, context.RequestAborted))?.Slug ?? text;
            var account = context.User.AccountId();
            var removed = await db.FavoriteTexts
                .Where(f => f.AccountId == account && f.Text.ToLower() == slug.ToLower())
                .ExecuteDeleteAsync(context.RequestAborted);
            return removed == 0 ? Results.NotFound(new ProblemResponse("This text is not a favourite.")) : Results.NoContent();
        });

        favorites.MapPut("/order", async (HttpContext context, AccountsDbContext db, ICanonIndex canon, FavoriteTextOrder order) =>
        {
            var named = new List<string>();
            foreach (var text in order.Items ?? [])
            {
                if (text is not null && await canon.Text(text, context.RequestAborted) is { } entry)
                {
                    named.Add(entry.Slug);
                }
            }

            var mine = await Mine(db, context.User.AccountId(), context.RequestAborted);
            Reorder(mine, named);

            await db.SaveChangesAsync(context.RequestAborted);
            return Results.Ok(new FavoriteTextsResponse(mine.OrderBy(f => f.Position).Select(Describe).ToList()));
        });
    }

    private static Task<List<FavoriteText>> Mine(AccountsDbContext db, Guid account, CancellationToken cancellationToken) =>
        db.FavoriteTexts
            .Where(f => f.AccountId == account)
            .OrderBy(f => f.Position).ThenBy(f => f.CreatedAt)
            .ToListAsync(cancellationToken);

    /// <summary>Slugs are compared as the corpus resolves them: <c>kjv</c> is <c>KJV</c>.</summary>
    private static bool Same(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Numbers the favourites from 0: those named first, in the order named, then the rest in the order
    /// they were in. <paramref name="favorites"/> is expected in its current order; a text named that is
    /// not a favourite, or named twice, is passed over.
    /// </summary>
    internal static void Reorder(IReadOnlyList<FavoriteText> favorites, IEnumerable<string> named)
    {
        var ordered = new List<FavoriteText>();
        foreach (var text in named)
        {
            if (favorites.FirstOrDefault(f => Same(f.Text, text)) is { } favorite && !ordered.Contains(favorite))
            {
                ordered.Add(favorite);
            }
        }

        ordered.AddRange(favorites.Where(f => !ordered.Contains(f)));
        for (var i = 0; i < ordered.Count; i++)
        {
            if (ordered[i].Position != i)
            {
                ordered[i].Position = i;
            }
        }
    }

    private static FavoriteTextResponse Describe(FavoriteText favorite) =>
        new(favorite.Text, favorite.Position, favorite.CreatedAt, favorite.UpdatedAt);
}

internal record FavoriteTextOrder(IReadOnlyList<string?>? Items);

internal record FavoriteTextResponse(string Text, int Position, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);

internal record FavoriteTextsResponse(IReadOnlyList<FavoriteTextResponse> Items);
