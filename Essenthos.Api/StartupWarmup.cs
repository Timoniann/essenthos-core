using System.Diagnostics;
using Essenthos.Core.Database;
using Essenthos.Core.Endpoints;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.EntityFrameworkCore;

namespace Essenthos.Core;

/// <summary>
/// What the first reader after a start would otherwise pay for, paid as the API starts: Entity
/// Framework's model and the queries it compiles, the database connections, the counts the process
/// keeps for its lifetime, and the code of the endpoints readers open first.
///
/// It runs once the API is already serving, so neither the start nor <c>/health/live</c> nor
/// <c>/health/ready</c> waits for it, and a reader who arrives while it runs is served as before.
/// One step at a time, so it never holds more than a few of the database's connections, within
/// <c>Warmup:Seconds</c> altogether (60 by default, 0 to switch it off). It stops at once if the
/// database cannot be reached, and a step the database is too slow for is left to the first request
/// that needs it, as everything was before.
/// </summary>
internal sealed class StartupWarmup(
    IServiceScopeFactory scopes,
    TextFacts facts,
    DatasetCountsCache datasets,
    ContextWeightsCache weights,
    IServer server,
    IConfiguration configuration,
    ILogger<StartupWarmup> logger)
{
    public const string ConfigurationKey = "Warmup:Seconds";

    public const int DefaultSeconds = 60;

    /// <summary>
    /// The pages a reader opens first, asked of the API itself so routing, the endpoints, their queries
    /// and their JSON are compiled before a reader asks. A record or chapter the corpus lacks answers
    /// 404, which warms the same code.
    /// </summary>
    internal static readonly string[] Requests =
    [
        "/v1/corpora",
        "/v1/corpora?brief=true",
        "/v1/parallel/genesis/1?corpora=KJV,BHSA",
        "/v1/context/genesis/1?language=en",
        "/v1/entities/moses?language=en",
        "/v1/entities/moses/references",
    ];

    /// <summary>Starts the warm-up without waiting for it; the caller goes on serving.</summary>
    public void Start(CancellationToken stopping)
    {
        var seconds = configuration.GetValue(ConfigurationKey, DefaultSeconds);
        if (seconds <= 0)
        {
            return;
        }

        _ = Task.Run(() => Run(TimeSpan.FromSeconds(seconds), stopping), CancellationToken.None);
    }

    internal async Task Run(TimeSpan budget, CancellationToken stopping)
    {
        using var within = CancellationTokenSource.CreateLinkedTokenSource(stopping);
        within.CancelAfter(budget);
        var started = Stopwatch.StartNew();

        var reached = await Step("the database", within.Token, async token =>
        {
            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.Texts.AsNoTracking().OrderBy(t => t.Id).Select(t => t.Slug).FirstOrDefaultAsync(token);
        });
        if (!reached)
        {
            return;
        }

        // Each of the rest stands alone: a count that could not be made is no reason not to make the
        // next, and the budget still bounds them all.
        var steps = new (string What, Func<CancellationToken, Task> Run)[]
        {
            ("the texts' facts", facts.Load),
            ("the datasets' counts", datasets.Get),
            ("the Chapter Context's weights", weights.Get),
            ("the first pages", Pages),
        };
        var done = 1;
        foreach (var (what, run) in steps)
        {
            if (within.IsCancellationRequested)
            {
                break;
            }

            done += await Step(what, within.Token, run) ? 1 : 0;
        }

        logger.LogInformation("Warmed up in {Elapsed}: {Done} of {Steps} steps", started.Elapsed, done, steps.Length + 1);
    }

    private async Task<bool> Step(string what, CancellationToken token, Func<CancellationToken, Task> step)
    {
        var started = Stopwatch.StartNew();
        try
        {
            await step(token);
            logger.LogDebug("Warm-up: {Step} in {Elapsed}", what, started.Elapsed);
            return true;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            logger.LogWarning("The warm-up stopped at {Step}: it ran out of time after {Elapsed}", what, started.Elapsed);
            return false;
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "The warm-up could not do {Step}; the first request that needs it will", what);
            return false;
        }
    }

    private async Task Pages(CancellationToken token)
    {
        if (Loopback(server.Features.Get<IServerAddressesFeature>()?.Addresses) is not { } address)
        {
            return;
        }

        using var http = new HttpClient { BaseAddress = address, Timeout = TimeSpan.FromSeconds(15) };
        foreach (var path in Requests)
        {
            using var response = await http.GetAsync(path, HttpCompletionOption.ResponseContentRead, token);
            if ((int)response.StatusCode >= 500)
            {
                throw new HttpRequestException($"{path} answered {(int)response.StatusCode}");
            }
        }
    }

    /// <summary>
    /// The address the process can reach itself at: the first plain-HTTP one it listens on, with an
    /// address that stands for every interface replaced by the loopback.
    /// </summary>
    internal static Uri? Loopback(IEnumerable<string>? addresses)
    {
        var first = addresses?.FirstOrDefault(a => a.StartsWith("http://", StringComparison.OrdinalIgnoreCase));
        if (first is null)
        {
            return null;
        }

        var builder = new UriBuilder(first.Replace("://+", "://0.0.0.0").Replace("://*", "://0.0.0.0"));
        if (builder.Host is "0.0.0.0" or "[::]" or "::" or "localhost")
        {
            builder.Host = "127.0.0.1";
        }

        return builder.Uri;
    }
}
