using System.Collections.Concurrent;
using System.Net;
using System.Text;
using Essenthos.Core.Endpoints;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Essenthos.Core.Tests;

/// <summary>
/// What a browser's Content-Security-Policy report becomes: a log line with the directive and the
/// blocked address, in either of the two shapes browsers send, and nothing that names the reader.
/// </summary>
public sealed class CspReportTests
{
    private const string Legacy = """
        {
          "csp-report": {
            "document-uri": "https://dev.essenthos.org/read/GEN/1?reader=someone",
            "referrer": "https://dev.essenthos.org/account",
            "violated-directive": "img-src 'self'",
            "effective-directive": "img-src",
            "original-policy": "default-src 'self'",
            "disposition": "report",
            "blocked-uri": "https://tiles.example.org/5/10/12.png?key=secret#top",
            "status-code": 200
          }
        }
        """;

    private const string Batch = """
        [
          {
            "type": "csp-violation",
            "url": "https://dev.essenthos.org/map?q=someone",
            "user_agent": "Mozilla/5.0",
            "body": {
              "documentURL": "https://dev.essenthos.org/map?q=someone",
              "effectiveDirective": "script-src-elem",
              "blockedURL": "inline",
              "disposition": "enforce",
              "sample": "alert(1)"
            }
          },
          { "type": "deprecation", "body": { "id": "something" } },
          {
            "type": "csp-violation",
            "body": { "effectiveDirective": "connect-src", "blockedURL": "wss://example.org/socket?token=abc", "disposition": "report" }
          }
        ]
        """;

    [Fact]
    public void BothShapesAreReadDownToTheDirectiveAndTheBlockedAddress()
    {
        CspReportEndpoints.Read(Encoding.UTF8.GetBytes(Legacy)).Should().Equal(
            new CspViolation("report", "img-src", "https://tiles.example.org/5/10/12.png"));

        CspReportEndpoints.Read(Encoding.UTF8.GetBytes(Batch)).Should().Equal(
            new CspViolation("enforce", "script-src-elem", "inline"),
            new CspViolation("report", "connect-src", "wss://example.org/socket"));

        CspReportEndpoints.Read("{}"u8).Should().BeEmpty();
        CspReportEndpoints.Read("[1, \"x\", null]"u8).Should().BeEmpty();
    }

    [Theory]
    [InlineData("https://user:password@example.org:8443/a/b?c=d#e", "https://example.org:8443/a/b")]
    [InlineData("inline", "inline")]
    [InlineData("eval", "eval")]
    [InlineData("data:image/png;base64,AAAA", "data")]
    [InlineData("blob:https://dev.essenthos.org/0b8e", "blob")]
    [InlineData("", "(none)")]
    [InlineData(null, "(none)")]
    [InlineData("anything\nwritten into the log", "(unreadable)")]
    public void ABlockedAddressKeepsNoQueryNoFragmentAndNoCredentials(string? blocked, string logged) =>
        CspReportEndpoints.Blocked(blocked).Should().Be(logged);

    [Fact]
    public async Task AReportIsLoggedAndAnythingElseIsRefused()
    {
        var logged = new ConcurrentQueue<string>();
        var builder = WebApplication.CreateSlimBuilder(["--urls=http://127.0.0.1:0"]);
        builder.Logging.ClearProviders().AddProvider(new Capture(logged));
        builder.Services.AddRateLimits(new RateLimitSettings(true, Plenty, Plenty, Plenty, Plenty, Plenty));
        await using var app = builder.Build();
        app.UseRateLimits(new RateLimitSettings(true, Plenty, Plenty, Plenty, Plenty, Plenty));
        app.MapGroup("/v1").MapCspReports();
        await app.StartAsync();
        using var http = new HttpClient { BaseAddress = new Uri(app.Urls.First()) };

        (await Post(http, Legacy, "application/csp-report")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await Post(http, Batch, "application/reports+json")).StatusCode.Should().Be(HttpStatusCode.NoContent);

        var lines = logged.Where(line => line.Contains("Content-Security-Policy")).ToList();
        lines.Should().Equal(
            "Content-Security-Policy report img-src: https://tiles.example.org/5/10/12.png",
            "Content-Security-Policy enforce script-src-elem: inline",
            "Content-Security-Policy report connect-src: wss://example.org/socket");
        string.Join("\n", lines).Should().NotContainAny("someone", "secret", "token", "alert", "Mozilla", "account");

        (await Post(http, Legacy, "text/plain")).StatusCode.Should().Be(HttpStatusCode.UnsupportedMediaType);
        (await Post(http, "{ not json", "application/csp-report")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await Post(http, new string(' ', CspReportEndpoints.MaxBodyBytes + 1), "application/csp-report")).StatusCode
            .Should().Be(HttpStatusCode.RequestEntityTooLarge);

        var many = "[" + string.Join(",", Enumerable.Repeat(
            """{ "type": "csp-violation", "body": { "effectiveDirective": "img-src", "blockedURL": "data" } }""", 50)) + "]";
        CspReportEndpoints.Read(Encoding.UTF8.GetBytes(many)).Should().HaveCount(CspReportEndpoints.MaxViolationsPerPost);
    }

    [Fact]
    public void TheEdgeSendsReportsWhereTheApiTakesThemAndLetsThemPastDevsPassword()
    {
        var caddyfile = File.ReadAllText(Path.Combine(Checkout(), "deploy", "Caddyfile"));
        var route = "/v1" + CspReportEndpoints.Route;

        caddyfile.Should().Contain($"report-uri {route};").And.Contain($"csp=\"{route}\"")
            .And.Contain($"@guarded not path {route}");
        foreach (var mode in new[] { "off", "report-only", "enforce" })
        {
            caddyfile.Should().Contain($"(csp-{mode}) {{");
        }
    }

    private static readonly RateLimit Plenty = new(6000, 1000);

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

    private static Task<HttpResponseMessage> Post(HttpClient http, string body, string mediaType) =>
        http.PostAsync("/v1" + CspReportEndpoints.Route, new StringContent(body, Encoding.UTF8, mediaType));

    private sealed class Capture(ConcurrentQueue<string> lines) : ILoggerProvider
    {
        public ILogger CreateLogger(string categoryName) => new Logger(lines);

        public void Dispose()
        {
        }

        private sealed class Logger(ConcurrentQueue<string> lines) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
                Func<TState, Exception?, string> formatter) => lines.Enqueue(formatter(state, exception));
        }
    }
}
