using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.RegularExpressions;

namespace Essenthos.Core.Loading.Marks;

/// <summary>
/// The tables named by the statements this process sends while it listens: every command, batch and
/// COPY Npgsql runs, whoever made its connection, as Npgsql's own trace reports it. What a step read is
/// what its statements named; a word that only looks like a table (a column, a cast) adds a table it
/// did not read, which costs a little time and never a skip that should have been a run.
/// </summary>
internal sealed partial class StatementTables : IDisposable
{
    /// <summary>
    /// Set in a Forge started by a load step that runs Forge processes of its own: the file each of them
    /// appends what its statements named to as it ends (<see cref="ForParent"/>).
    /// </summary>
    public const string ChildLog = "ESSENTHOS_STATEMENT_LOG";

    private const string QueryText = "db.query.text";

    private readonly IReadOnlySet<string>? _tables;
    private readonly ActivityListener _listener;
    private readonly ConcurrentDictionary<string, byte> _named = new(StringComparer.Ordinal);
    private long _statements;
    private volatile bool _listening;

    /// <param name="tables">The tables to listen for; null for every name a statement uses.</param>
    public StatementTables(IReadOnlySet<string>? tables)
    {
        _tables = tables;
        _listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == "Npgsql",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = Heard,
        };
        ActivitySource.AddActivityListener(_listener);
    }

    /// <summary>Starts a fresh list.</summary>
    public void Start()
    {
        _named.Clear();
        Interlocked.Exchange(ref _statements, 0);
        _listening = true;
    }

    /// <summary>The tables named since <see cref="Start"/>, and how many statements named anything.</summary>
    public (IReadOnlySet<string> Tables, long Statements) Stop()
    {
        _listening = false;
        return (_named.Keys.ToHashSet(StringComparer.Ordinal), Interlocked.Read(ref _statements));
    }

    public void Dispose() => _listener.Dispose();

    /// <summary>
    /// In a Forge a load step started (<see cref="ChildLog"/> set): listens to every statement this process
    /// sends and, disposed as the process ends, appends the names they used to that file. Null otherwise.
    /// </summary>
    public static IDisposable? ForParent() =>
        Environment.GetEnvironmentVariable(ChildLog) is { Length: > 0 } path ? new ParentLog(path) : null;

    private sealed class ParentLog : IDisposable
    {
        private readonly string _path;
        private readonly StatementTables _heard = new(null);

        public ParentLog(string path)
        {
            _path = path;
            _heard.Start();
        }

        public void Dispose()
        {
            var (names, _) = _heard.Stop();
            _heard.Dispose();
            for (var attempt = 0; ; attempt++)
            {
                try
                {
                    File.AppendAllLines(_path, names.Order(StringComparer.Ordinal));
                    return;
                }
                catch (IOException) when (attempt < 20)
                {
                    Thread.Sleep(100);
                }
            }
        }
    }

    private void Heard(Activity activity)
    {
        if (!_listening || activity.GetTagItem(QueryText) is not string sql)
        {
            return;
        }

        Interlocked.Increment(ref _statements);
        foreach (var table in Named(sql, _tables))
        {
            _named.TryAdd(table, 0);
        }
    }

    /// <summary>
    /// The known tables a statement names: every identifier, quoted or not, that is a table's name and is
    /// not a column of something before a dot (unless that something is the schema) or a type after a cast.
    /// </summary>
    public static IEnumerable<string> Named(string sql, IReadOnlySet<string>? tables)
    {
        foreach (Match match in Identifier().Matches(sql))
        {
            var name = match.Groups["quoted"].Success ? match.Groups["quoted"].Value : match.Groups["bare"].Value.ToLowerInvariant();
            if (tables is not null && !tables.Contains(name))
            {
                continue;
            }

            var before = sql.AsSpan(0, match.Index).TrimEnd();
            if (before.EndsWith("::", StringComparison.Ordinal))
            {
                continue;
            }

            if (before.EndsWith(".", StringComparison.Ordinal))
            {
                var qualifier = before[..^1].TrimEnd();
                if (!qualifier.EndsWith("public", StringComparison.OrdinalIgnoreCase)
                    && !qualifier.EndsWith("\"public\"", StringComparison.Ordinal))
                {
                    continue;
                }
            }

            yield return name;
        }
    }

    [GeneratedRegex("\"(?<quoted>[^\"]+)\"|(?<bare>[A-Za-z_][A-Za-z0-9_$]*)")]
    private static partial Regex Identifier();
}
