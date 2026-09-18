using System.Xml.Linq;
using Essenthos.Core.Configuration;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.DataProtection.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Essenthos.Core.Accounts;

/// <summary>
/// Sign-in with Google and GitHub, through the framework's own handlers. What this project owns is
/// two tables and a lookup; the authorisation-code exchange, the state and correlation checks and
/// the cookie protection are framework code somebody else maintains.
///
/// There are no passwords anywhere (FTR-0580). A provider is switched on by configuring its client:
/// <c>Authentication:Google:ClientId</c> and <c>ClientSecret</c>, the same for GitHub. One that is not
/// configured is not offered, so the API runs — and reading works — with neither.
/// </summary>
internal static class AccountsSetup
{
    /// <summary>The short-lived cookie a provider's answer is held in between its callback and ours.</summary>
    public const string ExternalScheme = "external";

    /// <summary>
    /// Everything read here, before the application is built and its configuration disposed
    /// (PRB-0414). Returns the providers that are configured, in the order a sign-in page offers them.
    /// </summary>
    public static IReadOnlyList<string> AddAccounts(this IServiceCollection services, IConfiguration configuration)
    {
        var connection = DatabaseConnection.ReadAccounts(configuration);
        services.AddDbContext<AccountsDbContext>(options => options.UseNpgsql(connection));

        services.AddDataProtection().SetApplicationName("essenthos");
        services.AddSingleton<IConfigureOptions<KeyManagementOptions>>(provider =>
            new ConfigureOptions<KeyManagementOptions>(options =>
                options.XmlRepository = new AccountsKeyRepository(provider.GetRequiredService<IServiceScopeFactory>())));

        var authentication = services
            .AddAuthentication(SessionAuthenticationHandler.SchemeName)
            .AddScheme<AuthenticationSchemeOptions, SessionAuthenticationHandler>(SessionAuthenticationHandler.SchemeName, null)
            .AddCookie(ExternalScheme, options =>
            {
                options.Cookie.Name = "essenthos_external";
                options.Cookie.SameSite = SameSiteMode.Lax;
                options.Cookie.HttpOnly = true;
                options.ExpireTimeSpan = TimeSpan.FromMinutes(10);
            });

        var providers = new List<string>();

        if (Client(configuration, "Google") is var (googleId, googleSecret))
        {
            authentication.AddGoogle("google", options =>
            {
                options.ClientId = googleId;
                options.ClientSecret = googleSecret;
                Common(options, "google");
                options.ClaimActions.MapJsonKey(PictureClaim, "picture");
                options.ClaimActions.MapJsonKey(EmailVerifiedClaim, "email_verified");
            });
            providers.Add("google");
        }

        if (Client(configuration, "GitHub") is var (gitHubId, gitHubSecret))
        {
            authentication.AddGitHub("github", options =>
            {
                options.ClientId = gitHubId;
                options.ClientSecret = gitHubSecret;
                Common(options, "github");
                options.Scope.Add("user:email");
                options.ClaimActions.MapJsonKey(PictureClaim, "avatar_url");
                options.Events.OnCreatingTicket = GitHubVerifiedEmail;
            });
            providers.Add("github");
        }

        services.AddAuthorization();
        return providers;
    }

    public const string PictureClaim = "picture";

    /// <summary>"true" when the provider says it verified the address in the email claim.</summary>
    public const string EmailVerifiedClaim = "email_verified";

    /// <summary>
    /// GitHub's profile carries whatever address a user chose to make public, verified or not. The
    /// address list says which one is primary and whether it is verified, so the email claim is
    /// replaced with the primary verified one, or removed when there is none — an address joins
    /// accounts here, and an unverified one must never be able to.
    /// </summary>
    private static async Task GitHubVerifiedEmail(Microsoft.AspNetCore.Authentication.OAuth.OAuthCreatingTicketContext context)
    {
        var identity = context.Identity!;
        foreach (var claim in identity.FindAll(System.Security.Claims.ClaimTypes.Email).ToList())
        {
            identity.RemoveClaim(claim);
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.github.com/user/emails");
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", context.AccessToken);
        request.Headers.Accept.ParseAdd("application/vnd.github+json");
        request.Headers.UserAgent.ParseAdd("essenthos");
        using var response = await context.Backchannel.SendAsync(request, context.HttpContext.RequestAborted);
        if (!response.IsSuccessStatusCode)
        {
            return;
        }

        using var emails = await System.Text.Json.JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(context.HttpContext.RequestAborted),
            cancellationToken: context.HttpContext.RequestAborted);
        foreach (var entry in emails.RootElement.EnumerateArray())
        {
            if (entry.TryGetProperty("primary", out var primary) && primary.GetBoolean() &&
                entry.TryGetProperty("verified", out var verified) && verified.GetBoolean() &&
                entry.TryGetProperty("email", out var email) && email.GetString() is { Length: > 0 } address)
            {
                identity.AddClaim(new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Email, address));
                identity.AddClaim(new System.Security.Claims.Claim(EmailVerifiedClaim, "true"));
                return;
            }
        }
    }

    private static void Common(Microsoft.AspNetCore.Authentication.OAuth.OAuthOptions options, string provider)
    {
        options.SignInScheme = ExternalScheme;
        options.CallbackPath = $"/v1/auth/{provider}/callback";
        // Lax rather than the framework's None. The provider sends the reader back with a top-level
        // GET, which carries a Lax cookie, and a None cookie must be Secure — which a development
        // server on plain http is not, so the correlation check failed there.
        options.CorrelationCookie.SameSite = SameSiteMode.Lax;
    }

    private static (string Id, string Secret)? Client(IConfiguration configuration, string provider) =>
        configuration[$"Authentication:{provider}:ClientId"] is { Length: > 0 } id &&
        configuration[$"Authentication:{provider}:ClientSecret"] is { Length: > 0 } secret
            ? (id, secret)
            : null;
}

/// <summary>
/// The data-protection keys, in the accounts database. See <see cref="AccountsDbContext.DataProtectionKeys"/>
/// for why there. The framework calls this synchronously, rarely — once at start, once a key rotation.
///
/// The keys are stored unencrypted, as the framework's own database store stores them: what they
/// protect is a ten-minute sign-in cookie, and anyone who can read this table can already read
/// everything else in the database it sits in.
/// </summary>
internal sealed class AccountsKeyRepository(IServiceScopeFactory scopes) : IXmlRepository
{
    public IReadOnlyCollection<XElement> GetAllElements()
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AccountsDbContext>();
        return db.DataProtectionKeys.AsNoTracking().Select(k => k.Xml).ToList().Select(XElement.Parse).ToList();
    }

    public void StoreElement(XElement element, string friendlyName)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AccountsDbContext>();
        db.DataProtectionKeys.Add(new DataProtectionKey
        {
            FriendlyName = friendlyName,
            Xml = element.ToString(SaveOptions.DisableFormatting),
        });
        db.SaveChanges();
    }
}
