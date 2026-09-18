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

            var credential = await db.Credentials
                .FirstOrDefaultAsync(c => c.Provider == provider && c.Subject == subject, context.RequestAborted);

            // A reader who is already signed in and signs in with a second provider is adding it to the
            // account they are in. That is the only way two providers come to share an account: nothing
            // joins them by email address on its own.
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

            await db.SaveChangesAsync(context.RequestAborted);
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
