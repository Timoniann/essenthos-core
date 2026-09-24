using System.Globalization;
using Npgsql;

namespace Essenthos.Core;

/// <summary>
/// How long the database may spend on one statement of a request before it stops it:
/// <c>Database:StatementTimeoutSeconds</c>, five by default, zero for no limit.
///
/// Set by the server rather than by the client, so a stopped query stops in Postgres too and does not
/// go on holding a core after the request has been answered. The slowest query a reader can ask for
/// deliberately takes about two seconds on a workstation; five leaves the server room for that and
/// stops anything that would hold the database for longer.
/// </summary>
internal static class StatementTimeout
{
    public const string ConfigurationKey = "Database:StatementTimeoutSeconds";

    public const int DefaultSeconds = 5;

    public const string Message =
        "The query took longer than the API allows and was stopped. Narrow it — fewer or longer search terms, " +
        "a book or a range of books — and ask again.";

    /// <summary>Postgres's code for a statement cancelled by <c>statement_timeout</c>.</summary>
    private const string QueryCanceled = "57014";

    public static string Apply(string connectionString, IConfiguration configuration)
    {
        var seconds = configuration.GetValue(ConfigurationKey, DefaultSeconds);
        if (seconds <= 0)
        {
            return connectionString;
        }

        var builder = new NpgsqlConnectionStringBuilder(connectionString);
        var setting = $"-c statement_timeout={(seconds * 1000).ToString(CultureInfo.InvariantCulture)}";
        builder.Options = string.IsNullOrWhiteSpace(builder.Options) ? setting : $"{builder.Options} {setting}";
        return builder.ConnectionString;
    }

    /// <summary>
    /// Whether <paramref name="error"/> is the server stopping a statement that ran too long. A request
    /// the reader abandoned is cancelled the same way in Postgres, but reaches here as a cancellation.
    /// </summary>
    public static bool Stopped(Exception? error)
    {
        if (error is OperationCanceledException)
        {
            return false;
        }

        for (var inner = error; inner is not null; inner = inner.InnerException)
        {
            if (inner is PostgresException { SqlState: QueryCanceled })
            {
                return true;
            }
        }

        return false;
    }
}
