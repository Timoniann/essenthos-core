using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Threading.RateLimiting;
using Essenthos.Core.Accounts;
using Microsoft.AspNetCore.RateLimiting;

namespace Essenthos.Core;

/// <summary>
/// A token bucket: <see cref="Burst"/> requests at once, refilled at <see cref="PerMinute"/>. The
/// reader fires several requests for one chapter, so the burst is what a page needs and the rate is
/// what a person turning pages needs.
/// </summary>
internal sealed record RateLimit(int PerMinute, int Burst)
{
    /// <summary>
    /// Refilled in steps of a few seconds rather than a token at a time, so ten a minute is two every
    /// twelve seconds and three hundred is fifty every ten.
    /// </summary>
    private const int StepsPerMinute = 6;

    public TokenBucketRateLimiterOptions Bucket()
    {
        var tokens = Math.Max(1, (int)Math.Ceiling((double)PerMinute / StepsPerMinute));
        return new TokenBucketRateLimiterOptions
        {
            TokenLimit = Burst,
            TokensPerPeriod = tokens,
            ReplenishmentPeriod = TimeSpan.FromMinutes(1) * tokens / PerMinute,
            QueueLimit = 0,
            AutoReplenishment = true,
        };
    }
}

/// <summary>What <c>RateLimits</c> in configuration asks for, with the defaults the API page states.</summary>
internal sealed record RateLimitSettings(
    bool Enabled,
    RateLimit Reading,
    RateLimit Expensive,
    RateLimit SignIn,
    RateLimit Writing);

/// <summary>
/// How much one client may ask of the API. Everything is counted per address — the reader's own, as
/// the proxy passes it on (see <see cref="Proxies"/>) — and a signed-in change is counted per session
/// instead, so readers sharing one address at a school or a church do not share one allowance for
/// their bookmarks.
///
/// Four allowances, and a request can draw on more than one: every request on <c>Reading</c>; a search,
/// a concordance or a lexicon query on <c>Expensive</c> as well, because those are the queries that
/// cost the database a second where a chapter costs it a tenth; sign-in on <c>SignIn</c>; and anything
/// that changes an account on <c>Writing</c>. Suggestions keep their daily caps on top.
/// </summary>
internal static class RateLimits
{
    public const string ConfigurationKey = "RateLimits";

    /// <summary>The endpoint policy for the queries that cost seconds rather than milliseconds.</summary>
    public const string Expensive = "expensive";

    public static readonly RateLimit DefaultReading = new(300, 200);

    public static readonly RateLimit DefaultExpensive = new(30, 20);

    public static readonly RateLimit DefaultSignIn = new(10, 10);

    public static readonly RateLimit DefaultWriting = new(60, 30);

    private const string SignInPrefix = "/v1/auth/";

    /// <summary>Asked by every page for which buttons to draw; it signs nobody in.</summary>
    private const string SignInProviders = "/v1/auth/providers";

    /// <summary>Probes answer the proxy and the container runtime, which must never be told to wait.</summary>
    private const string HealthPrefix = "/v1/health/";

    /// <summary>
    /// An IPv6 client is given a whole /64 by its provider and can take a new address from it for
    /// every request, so it is counted by the network rather than by the address.
    /// </summary>
    private const int Ipv6NetworkBytes = 8;

    private const string RejectionMessage =
        "Too many requests from this address. Wait the number of seconds in the Retry-After header and " +
        "send it again; the limits are listed on the site's API page.";

    public static RateLimitSettings Read(IConfiguration configuration)
    {
        var section = configuration.GetSection(ConfigurationKey);
        return new RateLimitSettings(
            section.GetValue("Enabled", true),
            Limit(section, "Reading", DefaultReading),
            Limit(section, "Expensive", DefaultExpensive),
            Limit(section, "SignIn", DefaultSignIn),
            Limit(section, "Writing", DefaultWriting));
    }

    public static IServiceCollection AddRateLimits(this IServiceCollection services, RateLimitSettings settings)
    {
        if (!settings.Enabled)
        {
            return services;
        }

        return services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.OnRejected = Reject;

            options.GlobalLimiter = PartitionedRateLimiter.CreateChained(
                PartitionedRateLimiter.Create<HttpContext, string>(context =>
                    context.Request.Path.StartsWithSegments(HealthPrefix.TrimEnd('/'))
                        ? RateLimitPartition.GetNoLimiter("")
                        : Bucket(Client(context), settings.Reading)),
                PartitionedRateLimiter.Create<HttpContext, string>(context =>
                    IsSignIn(context.Request)
                        ? Bucket(Client(context), settings.SignIn)
                        : RateLimitPartition.GetNoLimiter("")),
                PartitionedRateLimiter.Create<HttpContext, string>(context =>
                    IsWrite(context.Request) && !IsSignIn(context.Request)
                        ? Bucket(Session(context.Request) ?? Client(context), settings.Writing)
                        : RateLimitPartition.GetNoLimiter("")));

            options.AddPolicy(Expensive, context => Bucket(Client(context), settings.Expensive));
        });
    }

    /// <summary>
    /// Ahead of authentication, because the provider's callback is answered by the authentication
    /// middleware itself and never reaches an endpoint. The price is that a change is counted by the
    /// session token it carries rather than by the account it resolves to: each is somebody signed in,
    /// and a new session costs a sign-in, which is limited.
    /// </summary>
    public static IApplicationBuilder UseRateLimits(this IApplicationBuilder app, RateLimitSettings settings) =>
        settings.Enabled ? app.UseRateLimiter() : app;

    /// <summary>
    /// Who a request is counted against: the client's address, or its /64 for IPv6. An IPv4 address a
    /// dual-stack socket reports as IPv6 is the IPv4 address it maps.
    /// </summary>
    public static string Client(HttpContext context)
    {
        var address = context.Connection.RemoteIpAddress;
        if (address is null)
        {
            return "unknown";
        }

        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        if (address.AddressFamily != AddressFamily.InterNetworkV6)
        {
            return address.ToString();
        }

        var bytes = address.GetAddressBytes();
        Array.Clear(bytes, Ipv6NetworkBytes, bytes.Length - Ipv6NetworkBytes);
        return new IPAddress(bytes) + "/64";
    }

    /// <summary>The session a request is signed in with, as its hash, or null when it carries none.</summary>
    private static string? Session(HttpRequest request)
    {
        var token = request.Cookies[SessionTokens.CookieName];
        if (string.IsNullOrEmpty(token) &&
            request.Headers.Authorization.ToString() is { } header &&
            header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            token = header["Bearer ".Length..].Trim();
        }

        return token is { Length: > 0 } && SessionTokens.Hash(token) is { } hash
            ? "session:" + Convert.ToHexString(hash)
            : null;
    }

    private static bool IsSignIn(HttpRequest request) =>
        request.Path.StartsWithSegments(SignInPrefix.TrimEnd('/')) &&
        !request.Path.StartsWithSegments(SignInProviders);

    private static bool IsWrite(HttpRequest request) =>
        !(HttpMethods.IsGet(request.Method) || HttpMethods.IsHead(request.Method) || HttpMethods.IsOptions(request.Method));

    private static RateLimitPartition<string> Bucket(string key, RateLimit limit) =>
        RateLimitPartition.GetTokenBucketLimiter(key, _ => limit.Bucket());

    private static async ValueTask Reject(OnRejectedContext rejected, CancellationToken cancellationToken)
    {
        var response = rejected.HttpContext.Response;
        if (rejected.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
        {
            var seconds = Math.Max(1, (int)Math.Ceiling(retryAfter.TotalSeconds));
            response.Headers.RetryAfter = seconds.ToString(CultureInfo.InvariantCulture);
        }

        response.ContentType = "text/plain; charset=utf-8";
        await response.WriteAsync(RejectionMessage, cancellationToken);
    }

    private static RateLimit Limit(IConfigurationSection section, string name, RateLimit fallback)
    {
        var limit = new RateLimit(
            section.GetValue($"{name}:PerMinute", fallback.PerMinute),
            section.GetValue($"{name}:Burst", fallback.Burst));
        if (limit.PerMinute < 1 || limit.Burst < 1)
        {
            throw new InvalidOperationException(
                $"{ConfigurationKey}:{name} allows {limit.PerMinute} a minute with a burst of {limit.Burst}. Both must " +
                $"be at least 1; to lift the limits altogether set {ConfigurationKey}:Enabled to false.");
        }

        return limit;
    }
}
