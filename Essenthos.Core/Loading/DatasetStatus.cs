namespace Essenthos.Core.Loading;

internal enum DatasetState
{
    Waiting,
    Loading,
    Ready,
    Failed,
}

/// <summary>
/// What the dataset load is doing, so that an API answering 404 for everything can say whether it
/// is still working or has given up. The old project ran its load in a fire-and-forget task that
/// swallowed every exception into a line on stdout, and a cold start was indistinguishable from a
/// broken one for as long as anyone cared to wait.
/// </summary>
internal sealed class DatasetStatus
{
    private readonly Lock _lock = new();
    private readonly List<string> _loaded = [];

    public DatasetState State { get; private set; } = DatasetState.Waiting;

    /// <summary>What it is doing now, or why it stopped.</summary>
    public string? Detail { get; private set; }

    /// <summary>One line per text loaded in this run, in the order they were loaded.</summary>
    public IReadOnlyList<string> Texts
    {
        get
        {
            lock (_lock)
            {
                return _loaded.ToArray();
            }
        }
    }

    public void Starting(string what)
    {
        lock (_lock)
        {
            State = DatasetState.Loading;
            Detail = what;
        }
    }

    public void Record(object outcome)
    {
        lock (_lock)
        {
            _loaded.Add(outcome.ToString() ?? string.Empty);
        }
    }

    public void Ready()
    {
        lock (_lock)
        {
            State = DatasetState.Ready;
            Detail = null;
        }
    }

    public void Failed(string why)
    {
        lock (_lock)
        {
            State = DatasetState.Failed;
            Detail = why;
        }
    }
}
