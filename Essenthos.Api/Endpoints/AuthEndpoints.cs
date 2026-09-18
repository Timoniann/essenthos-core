using System.Security.Claims;
using Essenthos.Core.Accounts;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;

namespace Essenthos.Core.Endpoints;

/// <summary>
/// Signing in and out. The flow is the provider's round trip and then one of ours:
///
///     GET /v1/auth/google/login?returnUrl=/read/JHN/1     → Google
///     GET /v1/auth/google/callback                         the framework checks state and exchanges the code
///     GET /v1/auth/complete?returnUrl=/read/JHN/1          an account and a session, then back to the page
///
/// All of it is full-page navigation rather than fetch, because a provider's sign-in page cannot be
/// fetched, and all of it is on the site's own origin, so the session cookie is first-party.
/// </summary>
internal static class AuthEndpoints
{
    public static void MapAuth(this IEndpointRouteBuilder routes, IReadOnlyList<string> providers)
    {
        routes.MapGet("/auth/providers", () => Results.Ok(new AuthProvidersResponse(providers)));

        routes.MapGet("/auth/{provider}/login", (string provider, string? returnUrl) =>
            providers.Contains(provider)
                ? Results.Challenge(
                    new AuthenticationProperties { RedirectUri = $"/v1/auth/complete?returnUrl={Uri.EscapeDataString(Local(returnUrl))}" },
                    [provider])
                : Results.NotFound(new ProblemResponse($"Signing in with \"{provider}\" is not offered here.")));

        routes.MapGet("/auth/complete", async (HttpContext context, AccountsDbContext db, string? returnUrl, ILoggerFactory loggers) =>
        {
            var back = Local(returnUrl);
            var external = await context.AuthenticateAsync(AccountsSetup.ExternalScheme);
            await context.SignOutAsync(AccountsSetup.ExternalScheme);

            if (!external.Succeeded ||
                external.Properties?.Items.TryGetValue(".AuthScheme", out var provider) != true || provider is null ||
                external.Principal.FindFirstValue(ClaimTypes.NameIdentifier) is not { Length: > 0 } subject)
            {
                return Results.Redirect(WithOutcome(back, "failed"));
            }

            var principal = external.Principal;
            var now = DateTimeOffset.UtcNow;
            var email = principal.FindFirstValue(ClaimTypes.Email);
            var verified = VerifiedEmail(principal);

            var credential = await db.Credentials
                .FirstOrDefaultAsync(c => c.Provider == provider && c.Subject == subject, context.RequestAborted);

            // One verified address, one account. The address's owner, if it has one, decides where a
            // new provider goes: into that account, whether the reader is signed in to it or not.
            var owner = verified is null
                ? null
                : await db.AccountEmails.FirstOrDefaultAsync(e => e.Email == verified, context.RequestAborted);

            var signedIn = await context.AuthenticateAsync(SessionAuthenticationHandler.SchemeName);
            Guid accountId;
            if (credential is not null)
            {
                if (signedIn.Succeeded && credential.AccountId != signedIn.Principal!.AccountId())
                {
                    return Results.Redirect(WithOutcome(back, "taken"));
                }

                credential.LastUsedAt = now;
                credential.Email = email ?? credential.Email;
                accountId = credential.AccountId;
            }
            else
            {
                if (signedIn.Succeeded)
                {
                    accountId = signedIn.Principal!.AccountId();
                    if (owner is not null && owner.AccountId != accountId)
                    {
                        return Results.Redirect(WithOutcome(back, "email"));
                    }
                }
                else if (owner is not null)
                {
                    // Signing in with GitHub for the first time, with the address an account already
                    // proved through Google: that is the same person, and this is their account.
                    accountId = owner.AccountId;
                }
                else
                {
                    var account = new Account
                    {
                        Id = Guid.CreateVersion7(),
                        DisplayName = DisplayName(principal, provider),
                        ProviderPhotoUrl = Picture(principal),
                        CreatedAt = now,
                    };
                    db.Accounts.Add(account);
                    accountId = account.Id;
                }

                db.Credentials.Add(new Credential
                {
                    AccountId = accountId,
                    Provider = provider,
                    Subject = subject,
                    Email = email,
                    CreatedAt = now,
                    LastUsedAt = now,
                });
            }

            if (verified is not null && owner is null)
            {
                db.AccountEmails.Add(new AccountEmail { Email = verified, AccountId = accountId, CreatedAt = now });
            }

            if (!signedIn.Succeeded)
            {
                var token = SessionTokens.New();
                var expires = now + SessionTokens.Lifetime;
                db.Sessions.Add(new Session
                {
                    Id = Guid.CreateVersion7(),
                    AccountId = accountId,
                    TokenHash = SessionTokens.Hash(token)!,
                    Device = Device(context.Request),
                    CreatedAt = now,
                    LastSeenAt = now,
                    ExpiresAt = expires,
                });
                context.Response.Cookies.Append(SessionTokens.CookieName, token, SessionTokens.Cookie(context.Request, expires));
            }

            try
            {
                await db.SaveChangesAsync(context.RequestAborted);
            }
            catch (DbUpdateException)
            {
                // Two first sign-ins with one address at the same moment: the address's key let one
                // of them through, and this is the other. Nothing was written; signing in again finds
                // the account the first one made.
                context.Response.Cookies.Delete(SessionTokens.CookieName);
                return Results.Redirect(WithOutcome(back, "failed"));
            }

            loggers.CreateLogger("accounts").LogInformation("Signed in with {Provider}", provider);
            return Results.Redirect(back);
        });

        routes.MapPost("/auth/logout", async (HttpContext context, AccountsDbContext db) =>
        {
            if ((await context.AuthenticateAsync(SessionAuthenticationHandler.SchemeName)).Principal is { Identity.IsAuthenticated: true } user)
            {
                var session = user.SessionId();
                await db.Sessions.Where(s => s.Id == session).ExecuteDeleteAsync(context.RequestAborted);
            }

            context.Response.Cookies.Delete(SessionTokens.CookieName, SessionTokens.Cookie(context.Request, DateTimeOffset.UnixEpoch));
            return Results.NoContent();
        });
    }

    /// <summary>
    /// A path on this site, or the home page. A return address that names another origin is how a
    /// sign-in link is turned into a redirect to somebody else's page, and <c>//evil.example</c> and
    /// <c>/\evil.example</c> are both read by browsers as another origin.
    /// </summary>
    public static string Local(string? returnUrl) =>
        returnUrl is { Length: > 0 } url && url[0] == '/' && (url.Length == 1 || (url[1] != '/' && url[1] != '\\'))
            ? url
            : "/";

    /// <summary>
    /// The address, lower-cased, if the provider says it verified it; otherwise null. Google says so in
    /// <c>email_verified</c>; for GitHub the handler's ticket event puts it there only for the primary
    /// verified address.
    /// </summary>
    internal static string? VerifiedEmail(ClaimsPrincipal principal) =>
        string.Equals(principal.FindFirstValue(AccountsSetup.EmailVerifiedClaim), "true", StringComparison.OrdinalIgnoreCase) &&
        principal.FindFirstValue(ClaimTypes.Email) is { Length: > 0 and <= 320 } email
            ? email.Trim().ToLowerInvariant()
            : null;

    private static string WithOutcome(string url, string outcome) =>
        $"{url}{(url.Contains('?') ? '&' : '?')}signin={outcome}";

    private static string DisplayName(ClaimsPrincipal principal, string provider)
    {
        var name = (provider == "github" ? principal.FindFirstValue("urn:github:name") : null)
            ?? principal.FindFirstValue(ClaimTypes.Name)
            ?? principal.FindFirstValue(ClaimTypes.Email)?.Split('@')[0]
            ?? "Reader";
        name = name.Trim();
        return name.Length > Limits.DisplayName ? name[..Limits.DisplayName] : name;
    }

    /// <summary>The provider's picture, if it is an https URL — nothing else is shown to a reader.</summary>
    private static string? Picture(ClaimsPrincipal principal) =>
        principal.FindFirstValue(AccountsSetup.PictureClaim) is { } url &&
        Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps && url.Length <= 1024
            ? url
            : null;

    private static string? Device(HttpRequest request) =>
        request.Headers.UserAgent.ToString() is { Length: > 0 } agent
            ? agent.Length > Limits.Device ? agent[..Limits.Device] : agent
            : null;
}

internal record AuthProvidersResponse(IReadOnlyList<string> Providers);
