using System.Net;
using Essenthos.Core.Accounts;
using Essenthos.Core.Configuration;
using Essenthos.Core.Endpoints;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Npgsql;
using Xunit;

namespace Essenthos.Core.Tests;

/// <summary>
/// The API's limits, on a real server on a free port with the same middleware in the same order the
/// API uses, and endpoints standing in for the ones each allowance covers. The allowances are made
/// tiny so a handful of requests reaches them.
/// </summary>
public sealed class RateLimitTests
{
    private static readonly RateLimit Plenty = new(6000, 1000);

    private const string Owner = "timoniann@gmail.com";

    [Fact]
    public async Task AClientPastItsBurstIsToldToWaitAndForHowLong()
    {
        await using var server = await Server.Start(Limits(reading: new RateLimit(60, 3)));

        for (var i = 0; i < 3; i++)
        {
            (await server.Http.GetAsync("/v1/text")).StatusCode.Should().Be(HttpStatusCode.OK);
        }

        var refused = await server.Http.GetAsync("/v1/text");
        refused.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        refused.Headers.RetryAfter!.Delta.Should().BeGreaterThanOrEqualTo(TimeSpan.FromSeconds(1));
        refused.Content.Headers.ContentType!.MediaType.Should().Be("text/plain");
        (await refused.Content.ReadAsStringAsync()).Should().Contain("Retry-After");

        (await server.Http.GetAsync("/v1/health/live")).StatusCode.Should().Be(HttpStatusCode.OK,
            "a probe is never told to wait");
    }

    [Fact]
    public async Task AnExpensiveQueryHasASmallerAllowanceOfItsOwn()
    {
        await using var server = await Server.Start(Limits(expensive: new RateLimit(30, 2)));

        (await server.Http.GetAsync("/v1/search")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await server.Http.GetAsync("/v1/search")).StatusCode.Should().Be(HttpStatusCode.OK);
        var refused = await server.Http.GetAsync("/v1/search");
        refused.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        refused.Headers.RetryAfter.Should().NotBeNull();

        (await server.Http.GetAsync("/v1/text")).StatusCode.Should().Be(HttpStatusCode.OK,
            "reading a chapter does not draw on the search allowance");
    }

    [Fact]
    public async Task AForwardedAddressIsBelievedFromTheProxyAndCountedAsItsOwnClient()
    {
        // The test client connects from 127.0.0.1, so here it is the proxy.
        await using var server = await Server.Start(Limits(reading: new RateLimit(60, 2)),
            "--ForwardedHeaders:KnownProxies=127.0.0.1");

        (await server.Get("/v1/text", "203.0.113.1")).Should().Be("203.0.113.1");
        await server.Get("/v1/text", "203.0.113.1");
        (await server.Send("/v1/text", "203.0.113.1")).StatusCode.Should().Be(HttpStatusCode.TooManyRequests);

        (await server.Get("/v1/text", "203.0.113.2")).Should().Be("203.0.113.2",
            "another reader behind the same proxy has an allowance of their own");
        (await server.Get("/v1/text", "2001:db8:1:2:3:4:5:6")).Should().Be("2001:db8:1:2::/64");
    }

    [Fact]
    public async Task AForwardedAddressFromAnythingButTheProxyIsIgnored()
    {
        await using var server = await Server.Start(Limits(reading: new RateLimit(60, 2)),
            "--ForwardedHeaders:KnownProxies=10.89.0.2");

        (await server.Get("/v1/text", "203.0.113.1")).Should().Be("127.0.0.1");
        await server.Get("/v1/text", "203.0.113.2");
        (await server.Send("/v1/text", "203.0.113.3")).StatusCode.Should().Be(HttpStatusCode.TooManyRequests,
            "a client that writes a new address into the header each time is still one client");
    }

    [Fact]
    public async Task ChangesAreCountedPerSessionAndSignInPerAddress()
    {
        await using var server = await Server.Start(Limits(signIn: new RateLimit(10, 2), writing: new RateLimit(60, 2)));
        var one = SessionTokens.New();
        var two = SessionTokens.New();

        (await server.Post("/v1/me/bookmarks", one)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await server.Post("/v1/me/bookmarks", one)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await server.Post("/v1/me/bookmarks", one)).StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        (await server.Post("/v1/me/bookmarks", two)).StatusCode.Should().Be(HttpStatusCode.OK,
            "another reader on the same address has an allowance of their own");
        (await server.Http.GetAsync("/v1/text")).StatusCode.Should().Be(HttpStatusCode.OK,
            "reading is not a change");

        (await server.Http.GetAsync("/v1/auth/google/login")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await server.Http.GetAsync("/v1/auth/google/callback")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await server.Http.GetAsync("/v1/auth/google/login")).StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        (await server.Http.GetAsync("/v1/auth/providers")).StatusCode.Should().Be(HttpStatusCode.OK,
            "every page asks which providers there are, and that signs nobody in");
    }

    [Fact]
    public async Task PolicyReportsHaveAnAllowanceOfTheirOwnAndDrawOnNoOther()
    {
        await using var server = await Server.Start(Limits(
            reading: new RateLimit(60, 2), writing: new RateLimit(60, 2), reports: new RateLimit(60, 3)));

        for (var i = 0; i < 3; i++)
        {
            (await server.Http.PostAsync("/v1" + CspReportEndpoints.Route, null)).StatusCode.Should().Be(HttpStatusCode.OK);
        }

        (await server.Http.PostAsync("/v1" + CspReportEndpoints.Route, null)).StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        (await server.Http.GetAsync("/v1/text")).StatusCode.Should().Be(HttpStatusCode.OK,
            "a page that breaks the policy many times over does not use up its reader's reading");
        (await server.Post("/v1/me/bookmarks", SessionTokens.New())).StatusCode.Should().Be(HttpStatusCode.OK,
            "nor their saving");
    }

    [Fact]
    public async Task NothingIsLimitedWhenTheLimitsAreOff()
    {
        await using var server = await Server.Start(Limits(reading: new RateLimit(1, 1), expensive: new RateLimit(1, 1)) with
        {
            Enabled = false,
        });

        for (var i = 0; i < 5; i++)
        {
            (await server.Http.GetAsync("/v1/search")).StatusCode.Should().Be(HttpStatusCode.OK);
        }
    }

    [Fact]
    public void TheLimitsHaveDefaultsAndRefuseAZeroAllowance()
    {
        var defaults = RateLimits.Read(new ConfigurationBuilder().Build());
        defaults.Should().Be(new RateLimitSettings(true, RateLimits.DefaultReading, RateLimits.DefaultExpensive,
            RateLimits.DefaultSignIn, RateLimits.DefaultWriting, RateLimits.DefaultReports));

        var configured = Configuration(new() { ["RateLimits:Expensive:PerMinute"] = "12" });
        RateLimits.Read(configured).Expensive.Should().Be(new RateLimit(12, RateLimits.DefaultExpensive.Burst));

        var zero = Configuration(new() { ["RateLimits:Reading:Burst"] = "0" });
        FluentActions.Invoking(() => RateLimits.Read(zero)).Should().Throw<InvalidOperationException>()
            .WithMessage("*RateLimits:Enabled*");

        var bucket = new RateLimit(10, 10).Bucket();
        (bucket.ReplenishmentPeriod * 10 / bucket.TokensPerPeriod).Should().Be(TimeSpan.FromMinutes(1));
    }

    [Fact]
    public void AProxyIsNamedByAddressOrNetworkAndNothingElse()
    {
        Proxies.Read(new ConfigurationBuilder().Build()).Should().BeNull("with no proxy nothing forwarded is believed");

        var options = Proxies.Read(Configuration(new() { [Proxies.ConfigurationKey] = "10.89.0.2, 172.16.0.0/12" }))!;
        options.KnownProxies.Should().Equal(IPAddress.Parse("10.89.0.2"));
        options.KnownNetworks.Should().ContainSingle().Which.PrefixLength.Should().Be(12);

        FluentActions.Invoking(() => Proxies.Read(Configuration(new() { [Proxies.ConfigurationKey] = "proxy" })))
            .Should().Throw<InvalidOperationException>().WithMessage("*10.89.0.2*");
    }

    [Fact]
    public void TheOwnerIsAnAdminWhateverElseIsConfigured()
    {
        var configuration = new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(Checkout(), "Essenthos.Api", "appsettings.json"))
            // What user secrets write, and what compose passes from ADMIN_EMAILS.
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Accounts:BootstrapAdmins:0"] = "someone@example.org",
                ["Accounts:BootstrapAdmins"] = "another@example.org, ",
            })
            .Build();

        Admins.Read(configuration).Should().BeEquivalentTo([Owner, "someone@example.org", "another@example.org"]);

        File.ReadAllLines(Path.Combine(Checkout(), "deploy", "env.example"))
            .Should().Contain($"ADMIN_EMAILS={Owner}");
    }

    [Fact]
    public async Task AStatementPastTheTimeoutIsStoppedByTheServer()
    {
        var connection = StatementTimeout.Apply(
            new NpgsqlConnectionStringBuilder(DatabaseConnectionForTests()) { Database = "postgres" }.ConnectionString,
            Configuration(new() { [StatementTimeout.ConfigurationKey] = "1" }));
        new NpgsqlConnectionStringBuilder(connection).Options.Should().Contain("statement_timeout=1000");

        await using var open = new NpgsqlConnection(connection);
        await open.OpenAsync();
        await using var sleep = new NpgsqlCommand("SELECT pg_sleep(3)", open);

        var stopped = await FluentActions.Awaiting(() => sleep.ExecuteScalarAsync()).Should().ThrowAsync<PostgresException>();
        StatementTimeout.Stopped(stopped.Which).Should().BeTrue();
        StatementTimeout.Stopped(new OperationCanceledException("the reader went away", stopped.Which)).Should().BeFalse();

        var unlimited = Configuration(new() { [StatementTimeout.ConfigurationKey] = "0" });
        StatementTimeout.Apply("Host=localhost;Database=x", unlimited).Should().Be("Host=localhost;Database=x");
    }

    private static RateLimitSettings Limits(
        RateLimit? reading = null, RateLimit? expensive = null, RateLimit? signIn = null, RateLimit? writing = null,
        RateLimit? reports = null) =>
        new(true, reading ?? Plenty, expensive ?? Plenty, signIn ?? Plenty, writing ?? Plenty, reports ?? Plenty);

    private static IConfiguration Configuration(Dictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();

    private static string DatabaseConnectionForTests() =>
        DatabaseConnection.Read(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [DatabaseConnection.ConnectionStringKey] = "Host=localhost;Port=5437;Database=postgres;Username=essenthos",
            })
            .AddUserSecrets(typeof(RateLimitTests).Assembly)
            .AddEnvironmentVariables()
            .Build());

    private static string Checkout()
    {
        for (var folder = new DirectoryInfo(AppContext.BaseDirectory); folder is not null; folder = folder.Parent)
        {
            if (File.Exists(Path.Combine(folder.FullName, "Essenthos.Core.sln")))
            {
                return folder.FullName;
            }
        }

        throw new DirectoryNotFoundException("The tests were not built inside an essenthos-core checkout.");
    }

    private sealed class Server(WebApplication app) : IAsyncDisposable
    {
        // Cookies are written by hand, which a handler keeping its own jar would drop.
        public HttpClient Http { get; } = new(new HttpClientHandler { UseCookies = false }) { BaseAddress = new Uri(app.Urls.First()) };

        public static async Task<Server> Start(RateLimitSettings limits, params string[] configuration)
        {
            var builder = WebApplication.CreateSlimBuilder(["--urls=http://127.0.0.1:0", .. configuration]);
            builder.Services.AddRateLimits(limits);
            var app = builder.Build();

            app.UseProxies(Proxies.Read(app.Configuration));
            app.UseRateLimits(limits);
            app.MapGet("/v1/text", (HttpContext context) => RateLimits.Client(context));
            app.MapGet("/v1/search", () => "found").RequireRateLimiting(RateLimits.Expensive);
            app.MapGet("/v1/health/live", () => "live");
            app.MapGet("/v1/auth/providers", () => "google");
            app.MapGet("/v1/auth/google/login", () => "to google");
            app.MapGet("/v1/auth/google/callback", () => "back");
            app.MapPost("/v1/me/bookmarks", () => "kept");
            app.MapPost("/v1" + CspReportEndpoints.Route, () => "logged").RequireRateLimiting(RateLimits.Reports);

            await app.StartAsync();
            return new Server(app);
        }

        public async Task<HttpResponseMessage> Send(string path, string forwardedFor)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, path);
            request.Headers.Add("X-Forwarded-For", forwardedFor);
            return await Http.SendAsync(request);
        }

        public async Task<string> Get(string path, string forwardedFor)
        {
            var response = await Send(path, forwardedFor);
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            return await response.Content.ReadAsStringAsync();
        }

        public async Task<HttpResponseMessage> Post(string path, string session)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, path);
            request.Headers.Add("Cookie", $"{SessionTokens.CookieName}={session}");
            return await Http.SendAsync(request);
        }

        public async ValueTask DisposeAsync()
        {
            Http.Dispose();
            await app.StopAsync();
            await app.DisposeAsync();
        }
    }
}
