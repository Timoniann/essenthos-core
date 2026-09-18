using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Encodings.Web;
using Essenthos.Core.Accounts;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Essenthos.Core.Accounts;

/// <summary>
/// A session token: 32 random bytes, given to the device once, and kept only as its SHA-256.
///
/// SHA-256 rather than a password hash on purpose. A password hash is slow because a password is
/// guessable; this token has 256 bits of entropy and is looked up on every request, so a fast hash
/// loses nothing and a slow one would cost every request its time.
/// </summary>
internal static class SessionTokens
{
    public const string CookieName = "essenthos_session";

    /// <summary>
    /// How long a sign-in lasts. Long, because signing in again is a round trip to Google for a reader
    /// who only wanted to read, and a session can be revoked at any moment from the account page.
    /// </summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromDays(90);

    public static string New() => Base64Url(RandomNumberGenerator.GetBytes(32));

    /// <summary>The stored form of <paramref name="token"/>, or null for something that is not one.</summary>
    public static byte[]? Hash(string token)
    {
        if (token.Length is < 40 or > 64)
        {
            return null;
        }

        return SHA256.HashData(System.Text.Encoding.ASCII.GetBytes(token));
    }

    public static CookieOptions Cookie(HttpRequest request, DateTimeOffset expires) => new()
    {
        HttpOnly = true,
        Secure = request.IsHttps,
        // Lax: sent when a reader follows a link to the site, withheld from a form another site posts
        // here — which is the whole of the CSRF defence a same-origin JSON API needs.
        SameSite = SameSiteMode.Lax,
        Path = "/",
        Expires = expires,
        IsEssential = true,
    };

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}

/// <summary>
/// Who is asking, from the session cookie or, for a client that has no cookies, an
/// <c>Authorization: Bearer</c> header carrying the same token.
///
/// Every request that carries one costs one indexed lookup. Nothing else is cached: a revoked session
/// has to stop working on the next request, not at the end of a cache's lifetime.
/// </summary>
internal sealed class SessionAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    AccountsDbContext db)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "session";

    public const string SessionClaim = "sid";

    /// <summary>
    /// <c>last_seen_at</c> is written at most this often, so reading a chapter is not a write per
    /// request.
    /// </summary>
    private static readonly TimeSpan SeenGranularity = TimeSpan.FromMinutes(10);

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var token = Request.Cookies[SessionTokens.CookieName] ?? Bearer();
        if (token is null)
        {
            return AuthenticateResult.NoResult();
        }

        if (SessionTokens.Hash(token) is not { } hash)
        {
            return AuthenticateResult.Fail("That is not a session token.");
        }

        var now = DateTimeOffset.UtcNow;
        var session = await db.Sessions
            .Where(s => s.TokenHash == hash && s.ExpiresAt > now)
            .Select(s => new { s.Id, s.AccountId, s.LastSeenAt })
            .FirstOrDefaultAsync(Context.RequestAborted);

        if (session is null)
        {
            return AuthenticateResult.Fail("The session has ended or was revoked.");
        }

        if (now - session.LastSeenAt > SeenGranularity)
        {
            await db.Sessions.Where(s => s.Id == session.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.LastSeenAt, now), Context.RequestAborted);
        }

        var identity = new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, session.AccountId.ToString()), new Claim(SessionClaim, session.Id.ToString())],
            SchemeName);
        return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName));
    }

    private string? Bearer() =>
        Request.Headers.Authorization.ToString() is { } header && header.StartsWith("Bearer ", StringComparison.Ordinal)
            ? header["Bearer ".Length..].Trim()
            : null;
}

internal static class Signed
{
    public static Guid AccountId(this ClaimsPrincipal user) =>
        Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);

    public static Guid SessionId(this ClaimsPrincipal user) =>
        Guid.Parse(user.FindFirstValue(SessionAuthenticationHandler.SessionClaim)!);
}
