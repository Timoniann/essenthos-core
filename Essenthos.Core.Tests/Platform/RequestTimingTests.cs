using System.Net;
using System.Text.RegularExpressions;
using System.Threading.Channels;
using Essenthos.Core.Database;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;
using Xunit;

namespace Essenthos.Core.Tests.Platform;

/// <summary>
/// The line a request leaves in the log, on a real server on a free port and a real database, with
/// the middleware where the API has it: outside the exception handler.
/// </summary>
[Collection(WitnessDatabaseCollection.Name)]
public sealed class RequestTimingTests(WitnessDatabase database)
{
    [Fact]
    public async Task ALineNamesTheRouteAndCountsStatementsHoweverTheyAreSent()
    {
        await using var server = await Server.Start(database);

        var response = await server.Http.GetAsync("/v1/books/genesis/chapters/1?secret=hidden");
        var line = await server.NextLine();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var fields = Regex.Match(line, @"^GET /v1/books/\{slug\}/chapters/\{chapter:int\} 200 ms=(\d+) sql=3 sqlms=(\d+)$");
        fields.Success.Should().BeTrue($"the line was: {line}");
        long.Parse(fields.Groups[2].Value).Should().BeGreaterThanOrEqualTo(Server.SleepMilliseconds, "it counts the sleep the statement ran");
        long.Parse(fields.Groups[1].Value).Should().BeGreaterThanOrEqualTo(long.Parse(fields.Groups[2].Value));
        line.Should().NotContainAny("genesis", "secret", "hidden", "127.0.0.1");

        response.Headers.GetValues("Server-Timing").Single()
            .Should().MatchRegex(@"^total;dur=\d+\.\d, db;dur=\d+\.\d;desc=""3 statements""$");
    }

    [Fact]
    public async Task AProbeLeavesNoLineAndNoHeader()
    {
        await using var server = await Server.Start(database);

        var probe = await server.Http.GetAsync("/v1/health/live");
        await server.Http.GetAsync("/v1/quiet");

        probe.Headers.Contains("Server-Timing").Should().BeFalse();
        (await server.NextLine()).Should().StartWith("GET /v1/quiet 200", "the line of the probe would have come first");
    }

    [Fact]
    public async Task AFaultKeepsItsRouteAndReportsTheStatusTheReaderGot()
    {
        await using var server = await Server.Start(database);

        var response = await server.Http.GetAsync("/v1/fault");

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        response.Headers.Contains("Server-Timing").Should().BeTrue();
        (await server.NextLine()).Should().StartWith("GET /v1/fault 503 ");
    }

    [Fact]
    public async Task AnAddressNoRouteMatchesIsNotWrittenDown()
    {
        await using var server = await Server.Start(database);

        var response = await server.Http.GetAsync("/wp-admin/setup.php?x=1");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var line = await server.NextLine();
        line.Should().StartWith("GET (unmatched) 404 ");
        line.Should().NotContain("wp-admin");
    }

    [Fact]
    public async Task RequestsSideBySideCountOnlyTheirOwnStatements()
    {
        await using var server = await Server.Start(database);

        await Task.WhenAll(server.Http.GetAsync("/v1/books/genesis/chapters/1"), server.Http.GetAsync("/v1/quiet"));
        string[] lines = [await server.NextLine(), await server.NextLine()];

        lines.Single(line => line.Contains("/v1/quiet")).Should().Contain(" sql=0 sqlms=0");
        lines.Single(line => line.Contains("/v1/books")).Should().Contain(" sql=3 ");
    }

    [Fact]
    public async Task TheHeaderAndTheWholeThingEachHaveASwitch()
    {
        await using (var noHeader = await Server.Start(database, "--RequestTiming:ServerTiming=false"))
        {
            (await noHeader.Http.GetAsync("/v1/quiet")).Headers.Contains("Server-Timing").Should().BeFalse();
            (await noHeader.NextLine()).Should().StartWith("GET /v1/quiet 200 ");
        }

        await using var off = await Server.Start(database, "--RequestTiming:Enabled=false");
        (await off.Http.GetAsync("/v1/quiet")).Headers.Contains("Server-Timing").Should().BeFalse();
    }

    private sealed class Server(WebApplication app, Channel<string> lines) : IAsyncDisposable
    {
        public const int SleepMilliseconds = 50;

        public HttpClient Http { get; } = new() { BaseAddress = new Uri(app.Urls.First()) };

        /// <summary>
        /// The line is written as the request ends, which can be after the client has its response.
        /// </summary>
        public async Task<string> NextLine()
        {
            using var patience = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            return await lines.Reader.ReadAsync(patience.Token);
        }

        public static async Task<Server> Start(WitnessDatabase database, params string[] configuration)
        {
            // A pool of its own, so the first statement opens a connection and the opening is not counted.
            using var given = database.NewConnection();
            var connection = new NpgsqlConnectionStringBuilder(given.ConnectionString)
            {
                ApplicationName = $"request-timing-{Guid.NewGuid():N}",
            }.ConnectionString;

            var builder = WebApplication.CreateSlimBuilder(["--urls=http://127.0.0.1:0", .. configuration]);
            var lines = Channel.CreateUnbounded<string>();
            builder.Logging.ClearProviders();
            builder.Logging.AddProvider(new Capture(lines));
            builder.Services.AddDbContext<AppDbContext>(options => options.UseNpgsql(connection));
            var app = builder.Build();

            app.UseRequestTiming(app.Configuration);
            app.UseExceptionHandler(handler => handler.Run(context =>
            {
                context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
                return Task.CompletedTask;
            }));

            var v1 = app.MapGroup("/v1");
            v1.MapGet("/health/live", () => "live");
            v1.MapGet("/quiet", () => "quiet");
            v1.MapGet("/fault", string () => throw new InvalidOperationException("Asked to fail."));
            v1.MapGet("/books/{slug}/chapters/{chapter:int}", async (string slug, int chapter, AppDbContext db, CancellationToken cancellationToken) =>
            {
                await db.Links.AnyAsync(cancellationToken);
                await db.Database.ExecuteSqlRawAsync(
                    $"SELECT pg_sleep({SleepMilliseconds / 1000.0:F3})", cancellationToken);

                await db.Database.OpenConnectionAsync(cancellationToken);
                await using var command = new NpgsqlCommand("SELECT 1", (NpgsqlConnection)db.Database.GetDbConnection());
                await command.ExecuteScalarAsync(cancellationToken);
                return $"{slug} {chapter}";
            });

            await app.StartAsync();
            return new Server(app, lines);
        }

        public async ValueTask DisposeAsync()
        {
            Http.Dispose();
            await app.StopAsync();
            await app.DisposeAsync();
        }
    }

    private sealed class Capture(Channel<string> lines) : ILoggerProvider, ILogger
    {
        public ILogger CreateLogger(string categoryName) =>
            categoryName == RequestTiming.Category ? this : Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance;

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            lines.Writer.TryWrite(formatter(state, exception));

        public void Dispose()
        {
        }
    }
}
