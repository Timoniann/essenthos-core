using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;

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

/// <param name="Name">The avioniq action.</param>
/// <param name="Step">What it applies, as the change log names it: <c>images</c>, <c>load</c>, <c>backup</c>.</param>
/// <param name="Registered">Whether avioniq knows it; an allowed action nobody has registered yet cannot be run.</param>
internal sealed record StepEntry(string Name, string Step, bool Registered);

/// <param name="State"><c>running</c>, <c>succeeded</c> or <c>failed</c>.</param>
internal sealed record OperationRun(string Id, string Name, string Step, string State, int? ExitCode, string Started, string? Finished);

/// <param name="Problem">Why avioniq could not be asked what it runs, in a sentence; null when it was.</param>
internal sealed record OperationsResponse(string? Problem, IReadOnlyList<StepEntry> Steps, IReadOnlyList<OperationRun> Runs);

/// <param name="Next">The line to ask from next time.</param>
internal sealed record RunLog(OperationRun Run, IReadOnlyList<string> Lines, int Next);

/// <param name="Run">The run started, or null where it was refused.</param>
internal sealed record RunStarted(OperationRun? Run, string? Problem);

/// <summary>
/// The runs that apply what the owner changed — the pictures drawn again, a backup of what was
/// generated — as avioniq actions, so they are the same runs avioniq lists and nothing runs that
/// avioniq cannot see. Offered only beside the change that needs one, one at a time, and each
/// recorded in the change log when it ends.
/// </summary>
internal sealed class Operations(Avioniq avioniq, OperationAllowance allowance, ChangeLog log, ILogger<Operations> logger)
{
    /// <summary>What each action applies, as the change log's <c>needs</c> names it.</summary>
    private static readonly Dictionary<string, string> Steps = new(StringComparer.Ordinal)
    {
        ["core-images"] = "images",
        ["core-load"] = "load",
    };

    public static string StepOf(string name) => Steps.TryGetValue(name, out var step) ? step : name;

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
        var actions = listed.Succeeded
            ? (JsonSerializer.Deserialize<Board>(listed.Output, Web)?.Actions ?? []).Select(a => a.Name).ToHashSet(StringComparer.Ordinal)
            : [];
        return new OperationsResponse(
            listed.Succeeded ? null : Said(listed),
            allowance.Names.Select(name => new StepEntry(name, StepOf(name), actions.Contains(name))).ToList(),
            Runs());
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

            run = new Running((++_counter).ToString(System.Globalization.CultureInfo.InvariantCulture), name, StepOf(name), DateTime.UtcNow);
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
            Record(run);
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
            Record(run);
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

    /// <summary>A finished run, in the change log, so what waited on its step is known to have had it.</summary>
    private void Record(Running run)
    {
        var finished = run.Snapshot();
        _ = log.Append(ChangeLog.Apply, finished.Step, $"step/{finished.Step}", null, JsonValue.Create(finished.State), null, null)
            .ContinueWith(
                task => logger.LogError(task.Exception, "The end of run {Id} could not be written to the change log", finished.Id),
                TaskContinuationOptions.OnlyOnFaulted);
    }

    private static string Said(AvioniqResult result) =>
        (result.Error.Trim().Length > 0 ? result.Error : result.Output).Trim() is { Length: > 0 } text
            ? text
            : $"avioniq stopped with status {result.ExitCode} and said nothing.";

    private sealed record Board(List<Listed>? Actions);

    private sealed record Listed(string Name);

    private sealed class Running(string id, string name, string step, DateTime started)
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
                return new OperationRun(id, name, step, _exitCode is null ? "running" : _exitCode == 0 ? "succeeded" : "failed",
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
