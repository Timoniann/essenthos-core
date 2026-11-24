using System.Diagnostics;
using System.Globalization;

namespace Essenthos.Core;

/// <summary>
/// One line in the API's log for every request: its route, its status, how long it took, and how many
/// statements it sent to the database in how long. The line names the route's template and nothing a
/// reader sent: no path values, no query, no address, no account.
///
/// <para>
/// The statements are counted from Npgsql's own activity rather than an EF Core interceptor, because
/// search, renderings and the Strong book pages send theirs on a raw <c>NpgsqlCommand</c>. A
/// statement's time runs until its reader is finished, so it includes reading the rows and turning
/// them into objects as they arrive. Statements a request runs side by side can add up to more than
/// the request took.
/// </para>
///
/// <para>
/// The crawler pages are the one route where the address and the caller are what is wanted, so they
/// leave a second line with the path and the User-Agent, and never the query or the client's address:
/// every answer that is not a 200, and one 200 in <see cref="PageSampleEvery"/>, which is what the log's
/// 100 MB can carry at 90,000 requests a day. <c>RequestTiming:PageAddresses=false</c> takes it out.
/// </para>
/// </summary>
internal static partial class RequestTiming
{
    public const string Category = "Essenthos.Core.RequestTiming";

    private const string Unmatched = "(unmatched)";

    /// <summary>What a probe asks every few seconds says nothing about readers and would bury them.</summary>
    private const string Health = "/v1/health";

    private const string NpgsqlSource = "Npgsql";

    /// <summary>The name Npgsql gives the activity of opening a connection, which runs no statement.</summary>
    private const string Connecting = "CONNECT";

    private const int LongestMethod = 7;

    private const string Pages = "/v1/pages/{**path}";

    internal const int PageSampleEvery = 10;

    private const int LongestPath = 200;

    private const int LongestAgent = 160;

    private static readonly AsyncLocal<Tally?> Current = new();

    private static readonly Lazy<ActivityListener> Listening = new(Listen);

    public static void UseRequestTiming(this IApplicationBuilder app, IConfiguration configuration)
    {
        if (!configuration.GetValue("RequestTiming:Enabled", true))
        {
            return;
        }

        var sendHeader = configuration.GetValue("RequestTiming:ServerTiming", true);
        var logPages = configuration.GetValue("RequestTiming:PageAddresses", true);
        var pagesAnswered = 0L;
        var logger = app.ApplicationServices.GetRequiredService<ILoggerFactory>().CreateLogger(Category);
        _ = Listening.Value;

        app.Use(async (context, next) =>
        {
            // Read before the rest runs: the exception handler clears the endpoint from the context.
            var route = (context.GetEndpoint() as RouteEndpoint)?.RoutePattern.RawText ?? Unmatched;
            if (route.StartsWith(Health, StringComparison.Ordinal))
            {
                await next(context);
                return;
            }

            var tally = new Tally();
            Current.Value = tally;
            var started = Stopwatch.GetTimestamp();
            if (sendHeader)
            {
                context.Response.OnStarting(() =>
                {
                    context.Response.Headers["Server-Timing"] = tally.Header(Stopwatch.GetElapsedTime(started));
                    return Task.CompletedTask;
                });
            }

            try
            {
                await next(context);
            }
            finally
            {
                var method = context.Request.Method;
                Line(
                    logger,
                    method.Length <= LongestMethod ? method : "OTHER",
                    route,
                    context.Response.StatusCode,
                    (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds,
                    tally.Statements,
                    (long)tally.Time.TotalMilliseconds);

                if (logPages && route == Pages && SaysPage(context.Response.StatusCode, Interlocked.Increment(ref pagesAnswered)))
                {
                    PageLine(
                        logger,
                        context.Response.StatusCode,
                        Printable(context.Request.Path.Value, LongestPath),
                        Printable(context.Request.Headers.UserAgent.ToString(), LongestAgent));
                }
            }
        });
    }

    [LoggerMessage(EventId = 1, Level = LogLevel.Information, Message = "{Method} {Route} {Status} ms={TotalMs} sql={Statements} sqlms={SqlMs}")]
    private static partial void Line(ILogger logger, string method, string route, int status, long totalMs, int statements, long sqlMs);

    [LoggerMessage(EventId = 2, Level = LogLevel.Information, Message = "page {Status} {Path} agent={Agent}")]
    private static partial void PageLine(ILogger logger, int status, string path, string agent);

    internal static bool SaysPage(int status, long answered) =>
        status != StatusCodes.Status200OK || answered % PageSampleEvery == 0;

    /// <summary>What a caller sent, cut short and on one line, so it cannot forge a line of the log.</summary>
    internal static string Printable(string? sent, int longest)
    {
        if (string.IsNullOrEmpty(sent))
        {
            return "-";
        }

        var cut = sent.Length <= longest ? sent : sent[..longest];
        return string.Create(cut.Length, cut, static (written, from) =>
        {
            for (var i = 0; i < from.Length; i++)
            {
                written[i] = char.IsControl(from[i]) ? '?' : from[i];
            }
        });
    }

    /// <summary>
    /// For the life of the process. Outside a request nothing is sampled, so the warm-up and any other
    /// work of the process's own costs nothing here and counts towards nothing.
    /// </summary>
    private static ActivityListener Listen()
    {
        var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == NpgsqlSource,
            Sample = static (ref ActivityCreationOptions<ActivityContext> _) =>
                Current.Value is null ? ActivitySamplingResult.None : ActivitySamplingResult.PropagationData,
            ActivityStopped = static activity =>
            {
                if (Current.Value is { } tally && !activity.OperationName.StartsWith(Connecting, StringComparison.Ordinal))
                {
                    tally.Add(activity.Duration);
                }
            },
        };
        ActivitySource.AddActivityListener(listener);
        return listener;
    }

    /// <summary>A request's statements. It may run some side by side, so each is added atomically.</summary>
    private sealed class Tally
    {
        private int _statements;
        private long _ticks;

        public int Statements => Volatile.Read(ref _statements);

        public TimeSpan Time => TimeSpan.FromTicks(Interlocked.Read(ref _ticks));

        public void Add(TimeSpan duration)
        {
            Interlocked.Increment(ref _statements);
            Interlocked.Add(ref _ticks, duration.Ticks);
        }

        public string Header(TimeSpan total) => string.Create(
            CultureInfo.InvariantCulture,
            $"total;dur={total.TotalMilliseconds:F1}, db;dur={Time.TotalMilliseconds:F1};desc=\"{Statements} statements\"");
    }
}
