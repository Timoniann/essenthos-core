using System.Diagnostics;
using Essenthos.Core.Database;
using Essenthos.Core.Endpoints;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Essenthos.Core.Tests.Platform;

/// <summary>The warm-up the API runs as it starts, once it is already serving.</summary>
public sealed class StartupWarmupTests
{
    [Theory]
    [InlineData("http://0.0.0.0:5279", "http://127.0.0.1:5279/")]
    [InlineData("http://[::]:5279", "http://127.0.0.1:5279/")]
    [InlineData("http://+:8080", "http://127.0.0.1:8080/")]
    [InlineData("http://localhost:5301", "http://127.0.0.1:5301/")]
    [InlineData("http://127.0.0.1:5301", "http://127.0.0.1:5301/")]
    public void ItAsksItselfOnTheLoopbackWhereverItListens(string listening, string asked) =>
        StartupWarmup.Loopback(["https://0.0.0.0:443", listening])!.ToString().Should().Be(asked);

    [Fact]
    public void AProcessListeningOnlyOverTlsAsksNothingOfItself() =>
        StartupWarmup.Loopback(["https://0.0.0.0:443"]).Should().BeNull();

    [Fact]
    public async Task ADatabaseThatCannotBeReachedEndsItAtOnceAndLeavesEveryCountToTheFirstRequest()
    {
        var logs = new Logs();
        var services = new ServiceCollection()
            .AddDbContext<AppDbContext>(options => options.UseNpgsql("Host=127.0.0.1;Port=1;Username=nobody;Password=x;Timeout=2"))
            .BuildServiceProvider();
        var scopes = services.GetRequiredService<IServiceScopeFactory>();
        var warmup = new StartupWarmup(
            scopes,
            new TextFacts(scopes),
            new ContextWeightsCache(scopes, NullLogger<ContextWeightsCache>.Instance),
            new NoServer(),
            new ConfigurationBuilder().Build(),
            logs);

        var started = Stopwatch.StartNew();
        await warmup.Run(TimeSpan.FromSeconds(30), CancellationToken.None);

        started.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(15));
        logs.Lines.Should().ContainSingle().Which.Should().Contain("the database");
    }

    [Fact]
    public async Task ItsBudgetStopsItWhereverItHasGot()
    {
        var logs = new Logs();
        var services = new ServiceCollection()
            .AddDbContext<AppDbContext>(options => options.UseNpgsql("Host=127.0.0.1;Port=1;Username=nobody;Password=x;Timeout=30"))
            .BuildServiceProvider();
        var scopes = services.GetRequiredService<IServiceScopeFactory>();
        var warmup = new StartupWarmup(
            scopes,
            new TextFacts(scopes),
            new ContextWeightsCache(scopes, NullLogger<ContextWeightsCache>.Instance),
            new NoServer(),
            new ConfigurationBuilder().Build(),
            logs);

        using var stopping = new CancellationTokenSource();
        await stopping.CancelAsync();
        await warmup.Run(TimeSpan.FromSeconds(30), stopping.Token);

        logs.Lines.Should().ContainSingle().Which.Should().Contain("the database");
    }

    private sealed class NoServer : IServer
    {
        public IFeatureCollection Features { get; } = new FeatureCollection();

        public Task StartAsync<TContext>(IHttpApplication<TContext> application, CancellationToken cancellationToken)
            where TContext : notnull => Task.CompletedTask;

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public void Dispose()
        {
        }
    }

    /// <summary>The warnings and the closing line the warm-up writes.</summary>
    private sealed class Logs : ILogger<StartupWarmup>
    {
        public List<string> Lines { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (IsEnabled(logLevel))
            {
                Lines.Add(formatter(state, exception));
            }
        }
    }
}
