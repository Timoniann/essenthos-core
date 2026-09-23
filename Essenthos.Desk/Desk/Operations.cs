using System.Diagnostics;
using System.Text.Json;

namespace Essenthos.Core.Desk;

/// <summary>
/// Which avioniq actions the console may run: those configured under <c>Desk:Operations</c>, and
/// never one that stops, starts or replaces a running API. The owner's core is his to start and
/// stop, and the console shows how rather than doing it.
/// </summary>
internal sealed class OperationAllowance(IReadOnlyCollection<string> configured)
{
    private static readonly HashSet<string> Never = new(StringComparer.Ordinal)
    {
        "core-clear", "core-publish", "core-rollback",
    };

    public bool Allows(string name) => configured.Contains(name, StringComparer.Ordinal) && !Never.Contains(name);

    public IEnumerable<string> Names => configured.Where(Allows);
}

/// <param name="Kind"><c>service</c>, which runs until stopped, or <c>action</c>, which runs and finishes.</param>
/// <param name="Runnable">Whether the console may run it.</param>
/// <param name="Registered">Whether avioniq knows it; an allowed operation nobody has registered yet is listed as missing.</param>
internal sealed record ServiceEntry(
    string Name,
    string Kind,
    string? Project,
    string? Description,
    string? State,
    string? Command,
    bool Runnable,
    bool Registered);

/// <param name="State"><c>running</c>, <c>succeeded</c> or <c>failed</c>.</param>
internal sealed record OperationRun(string Id, string Name, string State, int? ExitCode, string Started, string? Finished);

internal sealed record OperationsResponse(
    bool Available,
    string? Problem,
    IReadOnlyList<ServiceEntry> Operations,
    IReadOnlyList<ServiceEntry> Services,
    IReadOnlyList<OperationRun> Runs);

/// <param name="Next">The line to ask from next time.</param>
internal sealed record RunLog(OperationRun Run, IReadOnlyList<string> Lines, int Next);

/// <param name="Run">The run started, or null where it was refused.</param>
internal sealed record RunStarted(OperationRun? Run, string? Problem);

/// <summary>
/// The runs that apply what the owner decided — the pictures drawn again, the corpus loaded — as
/// avioniq actions, so they are the same runs avioniq lists and nothing runs that avioniq cannot
/// see. One at a time: two loads against one database are a worse idea than a short wait.
/// </summary>
internal sealed class Operations(Avioniq avioniq, OperationAllowance allowance, ILogger<Operations> logger)
{
    /// <summary>How many lines of one run are kept, which is more than any load prints.</summary>
    private const int MostLines = 50_000;

    /// <summary>How many finished runs are remembered.</summary>
    private const int MostRuns = 20;

    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    private readonly Lock _lock = new();

    private readonly List<Running> _runs = [];

    private int _counter;

    public async Task<OperationsResponse> Read(CancellationToken cancellationToken)
    {
        var listed = await avioniq.Run(["--json", "services", "list"], cancellationToken);
        if (!listed.Succeeded)
        {
            return new OperationsResponse(false, Said(listed), [], [], Runs());
        }

        var board = JsonSerializer.Deserialize<Board>(listed.Output, Web) ?? new Board(null, null);
        var actions = (board.Actions ?? []).ToDictionary(a => a.Name, StringComparer.Ordinal);
        var operations = allowance.Names
            .Select(name => actions.TryGetValue(name, out var action)
                ? Entry(action, "action", runnable: true, registered: true)
                : new ServiceEntry(name, "action", null, null, null, null, false, false))
            .ToList();
        var services = (board.Services ?? []).Select(s => Entry(s, "service", runnable: false, registered: true)).ToList();
        return new OperationsResponse(true, null, operations, services, Runs());
    }

    public async Task<RunStarted> Start(string name, CancellationToken cancellationToken)
    {
        if (!allowance.Allows(name))
        {
            return new RunStarted(null, "The console does not run that. It runs only the operations it lists.");
        }

        var listed = await avioniq.Run(["--json", "services", "list"], cancellationToken);
        var registered = listed.Succeeded
                         && (JsonSerializer.Deserialize<Board>(listed.Output, Web)?.Actions ?? []).Any(a => a.Name == name);
        if (!registered)
        {
            return new RunStarted(null, listed.Succeeded
                ? "avioniq has no action by that name yet. It has to be registered before the console can run it."
                : Said(listed));
        }

        Running run;
        lock (_lock)
        {
            if (_runs.Any(r => r.State == "running"))
            {
                return new RunStarted(null, "Another run has not finished. Wait for it, then start this one.");
            }

            run = new Running((++_counter).ToString(System.Globalization.CultureInfo.InvariantCulture), name, DateTime.UtcNow);
            _runs.Insert(0, run);
            if (_runs.Count > MostRuns)
            {
                _runs.RemoveAt(_runs.Count - 1);
            }
        }

        var process = new Process { StartInfo = avioniq.StartInfo(["services", "run", name]), EnableRaisingEvents = true };
        process.OutputDataReceived += (_, line) => run.Add(line.Data);
        process.ErrorDataReceived += (_, line) => run.Add(line.Data);
        process.Exited += (_, _) =>
        {
            // The last lines arrive after Exited; WaitForExit with no timeout waits for them.
            process.WaitForExit();
            run.Finish(process.ExitCode);
            process.Dispose();
        };

        try
        {
            process.Start();
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or FileNotFoundException)
        {
            logger.LogWarning(exception, "Could not start {Name}", name);
            run.Add($"avioniq could not be started: {exception.Message}");
            run.Finish(-1);
            process.Dispose();
            return new RunStarted(run.Snapshot(), null);
        }

        process.StandardInput.Close();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        logger.LogInformation("Started {Name} as run {Id}", name, run.Id);
        return new RunStarted(run.Snapshot(), null);
    }

    public RunLog? Log(string id, int from)
    {
        Running? run;
        lock (_lock)
        {
            run = _runs.FirstOrDefault(r => r.Id == id);
        }

        return run?.Since(from);
    }

    private IReadOnlyList<OperationRun> Runs()
    {
        lock (_lock)
        {
            return _runs.Select(r => r.Snapshot()).ToList();
        }
    }

    private static ServiceEntry Entry(Listed listed, string kind, bool runnable, bool registered) =>
        new(listed.Name, listed.Kind ?? kind, listed.Project, listed.Description, listed.State, listed.Command, runnable, registered);

    private static string Said(AvioniqResult result) =>
        (result.Error.Trim().Length > 0 ? result.Error : result.Output).Trim() is { Length: > 0 } text
            ? text
            : $"avioniq stopped with status {result.ExitCode} and said nothing.";

    private sealed record Board(List<Listed>? Services, List<Listed>? Actions);

    private sealed record Listed(string Name, string? Kind, string? Project, string? Description, string? State, string? Command);

    private sealed class Running(string id, string name, DateTime started)
    {
        private readonly List<string> _lines = [];

        private int? _exitCode;

        private DateTime? _finished;

        public string Id => id;

        public string State
        {
            get
            {
                lock (_lines)
                {
                    return _exitCode is null ? "running" : _exitCode == 0 ? "succeeded" : "failed";
                }
            }
        }

        public void Add(string? line)
        {
            if (line is null)
            {
                return;
            }

            lock (_lines)
            {
                if (_lines.Count < MostLines)
                {
                    _lines.Add(line);
                }
            }
        }

        public void Finish(int exitCode)
        {
            lock (_lines)
            {
                _exitCode = exitCode;
                _finished = DateTime.UtcNow;
            }
        }

        public OperationRun Snapshot()
        {
            lock (_lines)
            {
                return new OperationRun(id, name, _exitCode is null ? "running" : _exitCode == 0 ? "succeeded" : "failed",
                    _exitCode, started.ToString("O"), _finished?.ToString("O"));
            }
        }

        public RunLog Since(int from)
        {
            lock (_lines)
            {
                var start = Math.Clamp(from, 0, _lines.Count);
                return new RunLog(Snapshot(), _lines.Skip(start).ToList(), _lines.Count);
            }
        }
    }
}
