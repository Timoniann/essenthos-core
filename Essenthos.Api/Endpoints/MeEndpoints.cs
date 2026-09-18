using Essenthos.Core.Accounts;
using Microsoft.EntityFrameworkCore;

namespace Essenthos.Core.Endpoints;

/// <summary>
/// The signed-in reader's own account: who they are to the site, their picture, their devices, and
/// leaving. Reading never needs any of it (NOT-0007).
/// </summary>
internal static class MeEndpoints
{
    private static readonly string[] Locales = ["en", "uk", "de", "es"];

    public static void MapMe(this IEndpointRouteBuilder routes)
    {
        var me = routes.MapGroup("/me").RequireAuthorization();

        me.MapGet("", async (HttpContext context, AccountsDbContext db) =>
            await Describe(db, context.User.AccountId(), context.RequestAborted) is { } account
                ? Results.Ok(account)
                : Results.Unauthorized());

        me.MapPatch("", async (HttpContext context, AccountsDbContext db, MeUpdate update) =>
        {
            var id = context.User.AccountId();
            var account = await db.Accounts.FirstAsync(a => a.Id == id, context.RequestAborted);

            if (update.DisplayName is { } name)
            {
                name = name.Trim();
                if (name.Length is 0 or > Limits.DisplayName)
                {
                    return Results.BadRequest(new ProblemResponse($"A name is 1 to {Limits.DisplayName} characters."));
                }

                account.DisplayName = name;
            }

            if (update.About is { } about)
            {
                about = about.Trim();
                if (about.Length > Limits.About)
                {
                    return Results.BadRequest(new ProblemResponse($"About is at most {Limits.About} characters."));
                }

                account.About = about.Length == 0 ? null : about;
            }

            if (update.Locale is { } locale)
            {
                if (locale.Length > 0 && !Locales.Contains(locale))
                {
                    return Results.BadRequest(new ProblemResponse($"The interface speaks {string.Join(", ", Locales)}."));
                }

                account.Locale = locale.Length == 0 ? null : locale;
            }

            await db.SaveChangesAsync(context.RequestAborted);
            return Results.Ok(await Describe(db, account.Id, context.RequestAborted));
        });

        // The picture as the request body, not a form: one file, its type in Content-Type, and nothing
        // else to parse.
        me.MapPut("/photo", async (HttpContext context, AccountsDbContext db) =>
        {
            var content = await ReadCapped(context.Request, context.RequestAborted);
            if (content is null)
            {
                return Results.Json(
                    new ProblemResponse($"A photo is at most {Limits.PhotoBytes / 1024} KB."),
                    statusCode: StatusCodes.Status413PayloadTooLarge);
            }

            if (Sniff(content) is not { } type)
            {
                return Results.BadRequest(new ProblemResponse("A photo is a JPEG, PNG or WebP image."));
            }

            var id = context.User.AccountId();
            var photo = await db.AccountPhotos.FirstOrDefaultAsync(p => p.AccountId == id, context.RequestAborted);
            if (photo is null)
            {
                db.AccountPhotos.Add(new AccountPhoto { AccountId = id, Content = content, ContentType = type, UpdatedAt = DateTimeOffset.UtcNow });
            }
            else
            {
                photo.Content = content;
                photo.ContentType = type;
                photo.UpdatedAt = DateTimeOffset.UtcNow;
            }

            var account = await db.Accounts.FirstAsync(a => a.Id == id, context.RequestAborted);
            account.PhotoVersion++;
            await db.SaveChangesAsync(context.RequestAborted);
            return Results.Ok(await Describe(db, id, context.RequestAborted));
        });

        me.MapDelete("/photo", async (HttpContext context, AccountsDbContext db) =>
        {
            var id = context.User.AccountId();
            await db.AccountPhotos.Where(p => p.AccountId == id).ExecuteDeleteAsync(context.RequestAborted);
            var account = await db.Accounts.FirstAsync(a => a.Id == id, context.RequestAborted);
            account.PhotoVersion = 0;
            await db.SaveChangesAsync(context.RequestAborted);
            return Results.Ok(await Describe(db, id, context.RequestAborted));
        });

        me.MapGet("/sessions", async (HttpContext context, AccountsDbContext db) =>
        {
            var current = context.User.SessionId();
            var account = context.User.AccountId();
            var now = DateTimeOffset.UtcNow;
            var sessions = await db.Sessions
                .Where(s => s.AccountId == account && s.ExpiresAt > now)
                .OrderByDescending(s => s.LastSeenAt)
                .Select(s => new SessionResponse(s.Id, s.Device, s.CreatedAt, s.LastSeenAt, s.Id == current))
                .ToListAsync(context.RequestAborted);
            return Results.Ok(new SessionsResponse(sessions));
        });

        me.MapDelete("/sessions/{id:guid}", async (HttpContext context, AccountsDbContext db, Guid id) =>
        {
            var account = context.User.AccountId();
            var removed = await db.Sessions
                .Where(s => s.Id == id && s.AccountId == account)
                .ExecuteDeleteAsync(context.RequestAborted);
            return removed == 0 ? Results.NotFound(new ProblemResponse("No such session.")) : Results.NoContent();
        });

        // Deleted, not hidden: the account, how it signed in, its sessions and its photo, in one
        // statement the foreign keys cascade. Nothing else belongs to an account yet; when notes and
        // articles do, what happens to an article others have commented on has to be decided first.
        me.MapDelete("", async (HttpContext context, AccountsDbContext db) =>
        {
            var id = context.User.AccountId();
            await db.Accounts.Where(a => a.Id == id).ExecuteDeleteAsync(context.RequestAborted);
            context.Response.Cookies.Delete(SessionTokens.CookieName, SessionTokens.Cookie(context.Request, DateTimeOffset.UnixEpoch));
            return Results.NoContent();
        });

        // Public: a byline will show it. Cached for ever, because the URL carries the photo's version.
        routes.MapGet("/accounts/{id:guid}/photo", async (HttpContext context, AccountsDbContext db, Guid id) =>
        {
            var photo = await db.AccountPhotos.AsNoTracking().FirstOrDefaultAsync(p => p.AccountId == id, context.RequestAborted);
            if (photo is null)
            {
                return Results.NotFound();
            }

            context.Response.Headers.CacheControl = context.Request.Query.ContainsKey("v")
                ? "public, max-age=31536000, immutable"
                : "no-cache";
            context.Response.Headers.XContentTypeOptions = "nosniff";
            context.Response.Headers.ContentSecurityPolicy = "default-src 'none'";
            return Results.Bytes(photo.Content, photo.ContentType);
        });
    }

    private static async Task<MeResponse?> Describe(AccountsDbContext db, Guid id, CancellationToken cancellationToken)
    {
        var account = await db.Accounts.AsNoTracking()
            .Where(a => a.Id == id)
            .Select(a => new
            {
                a.Id, a.DisplayName, a.About, a.Locale, a.ProviderPhotoUrl, a.PhotoVersion, a.CreatedAt,
                Providers = a.Credentials.OrderBy(c => c.CreatedAt).Select(c => new ProviderResponse(c.Provider, c.Email)).ToList(),
            })
            .FirstOrDefaultAsync(cancellationToken);

        return account is null
            ? null
            : new MeResponse(
                account.Id,
                account.DisplayName,
                account.About,
                account.Locale,
                account.PhotoVersion > 0 ? $"/v1/accounts/{account.Id}/photo?v={account.PhotoVersion}" : account.ProviderPhotoUrl,
                account.PhotoVersion > 0,
                account.Providers,
                account.CreatedAt);
    }

    /// <summary>The body, or null once it passes the cap — read no further than one byte over it.</summary>
    private static async Task<byte[]?> ReadCapped(HttpRequest request, CancellationToken cancellationToken)
    {
        if (request.ContentLength > Limits.PhotoBytes)
        {
            return null;
        }

        using var buffer = new MemoryStream();
        var chunk = new byte[64 * 1024];
        int read;
        while ((read = await request.Body.ReadAsync(chunk, cancellationToken)) > 0)
        {
            buffer.Write(chunk, 0, read);
            if (buffer.Length > Limits.PhotoBytes)
            {
                return null;
            }
        }

        return buffer.ToArray();
    }

    /// <summary>
    /// The type the bytes say they are, not the type the request claims. Only raster formats: an SVG is
    /// a document that can carry script, and it would be served from this origin.
    /// </summary>
    internal static string? Sniff(ReadOnlySpan<byte> content) => content switch
    {
        [0xFF, 0xD8, 0xFF, ..] => "image/jpeg",
        [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, ..] => "image/png",
        [0x52, 0x49, 0x46, 0x46, _, _, _, _, 0x57, 0x45, 0x42, 0x50, ..] => "image/webp",
        _ => null,
    };
}

/// <param name="Photo">
/// The picture to show: the uploaded one, or the provider's, or null. <paramref name="PhotoUploaded"/>
/// says which, so an account page knows whether "remove" means anything.
/// </param>
/// <param name="Providers">How this account signs in, with the address each reported.</param>
internal record MeResponse(
    Guid Id,
    string DisplayName,
    string? About,
    string? Locale,
    string? Photo,
    bool PhotoUploaded,
    IReadOnlyList<ProviderResponse> Providers,
    DateTimeOffset CreatedAt);

internal record ProviderResponse(string Provider, string? Email);

/// <summary>What a reader may change. A field left out is left alone; an empty string clears it.</summary>
internal record MeUpdate(string? DisplayName, string? About, string? Locale);

internal record SessionResponse(Guid Id, string? Device, DateTimeOffset CreatedAt, DateTimeOffset LastSeenAt, bool Current);

internal record SessionsResponse(IReadOnlyList<SessionResponse> Items);
