using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Npgsql;

namespace Essenthos.Core.Loading.Marks;

/// <summary>
/// Which steps of the load can be skipped, and the marks that say so. A step is a function of its code,
/// the files it reads and the tables it reads; when all three are what they were the last time it ran to
/// the end, it would only write what is already there. So each step that runs leaves a mark
/// (<c>load_step_mark</c>): a digest of those inputs as they stood when it started, and the tables
/// its statements named and wrote, which are the tables the next load compares.
///
/// <para>
/// Nobody declares which step depends on which. A step that rewrites a table with what it already held
/// leaves its sum unchanged, and every later step that reads that table finds its inputs where they were
/// and is skipped in turn; a step whose output moved runs exactly the steps that read what moved. A
/// step that started and did not finish has no mark, so a failed or cancelled load runs it again.
/// </para>
///
/// <para>
/// When in doubt it runs: a table it cannot sum, a step with no mark, a mark from other code. A false run
/// costs time; a false skip would leave the corpus wrong.
/// </para>
/// </summary>
internal sealed class LoadMarks(
    string connectionString,
    TableFingerprints tables,
    FileDigests files,
    CodeDigest code,
    ILogger logger) : IDisposable
{
    private readonly Dictionary<string, Mark> _marks = new(StringComparer.Ordinal);
    private readonly Dictionary<string, (string Digest, IReadOnlyList<string> Lines)> _code = new(StringComparer.Ordinal);

    /// <summary>The tables the steps of this load changed, and the last step that changed each.</summary>
    private readonly Dictionary<string, string> _movedBy = new(StringComparer.Ordinal);

    /// <summary>
    /// Where the lines each step's code digest was taken over are kept between runs, so a step whose code
    /// moved can say which methods moved. Outside the corpus: nothing decides from it.
    /// </summary>
    private readonly string _codeLines = Path.Combine(
        Path.GetTempPath(), "essenthos-marks", new NpgsqlConnectionStringBuilder(connectionString).Database ?? "default");
    private StatementTables? _statements;

    /// <summary>Watches every table, reads the marks and starts listening for statements.</summary>
    public async Task Open(CancellationToken cancellationToken)
    {
        var unwatched = await tables.Open(cancellationToken);
        if (unwatched.Count > 0)
        {
            logger.LogInformation(
                "Watching the writes of {Count} table(s) not watched before; their sums are taken afresh: {Tables}",
                unwatched.Count, string.Join(", ", unwatched.Take(12)) + (unwatched.Count > 12 ? ", ..." : ""));
        }

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            "SELECT step, input_digest, output_digest, reads, inputs::text, at FROM load_step_mark", connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            _marks[reader.GetString(0)] = new Mark(
                reader.GetString(1),
                reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetFieldValue<string[]>(3),
                JsonSerializer.Deserialize<Inputs>(reader.GetString(4)) ?? Inputs.Empty,
                reader.GetFieldValue<DateTimeOffset>(5));
        }

        _statements = new StatementTables(tables.Tables, new NpgsqlConnectionStringBuilder(connectionString).Database);
    }

    public void Dispose() => _statements?.Dispose();

    /// <summary>
    /// Whether <paramref name="step"/> must run, and why: its inputs now, against those its mark recorded.
    /// </summary>
    public async Task<Decision> Decide(LoadStep step, bool force, CancellationToken cancellationToken)
    {
        var codeDigest = Code(step).Digest;
        var fileDigests = files.Of(step.Files, cancellationToken)
            .ToDictionary(file => file.Name, file => file.Digest, StringComparer.Ordinal);
        _marks.TryGetValue(step.Name, out var mark);

        if (force || step.Always || mark?.Reads is null)
        {
            var why = force ? "every step was asked for" : step.Always ? "it runs every time" : "no mark says what it last ran on";
            return new Decision(step, false, why, step.Version, codeDigest, fileDigests);
        }

        var sums = await tables.Of(mark.Reads, cancellationToken);
        var now = new Inputs(step.Version, codeDigest, fileDigests, new SortedDictionary<string, string>(sums.ToDictionary(), StringComparer.Ordinal));
        if (now.Digest() == mark.InputDigest)
        {
            return new Decision(step, true, $"nothing it reads moved since {mark.At.ToLocalTime():yyyy-MM-dd HH:mm}", step.Version, codeDigest, fileDigests);
        }

        return new Decision(step, false, Moved(mark.Inputs, now, CodeMoved(step), _movedBy), step.Version, codeDigest, fileDigests);
    }

    /// <summary>
    /// Takes the tables' sums as the step finds them, forgets its mark (a step that does not finish must
    /// run again), and starts listening for what its statements name.
    /// </summary>
    public async Task<Running> Begin(Decision decision, CancellationToken cancellationToken)
    {
        var before = await tables.Of(tables.Tables.Append(TableFingerprints.Migrations), cancellationToken);
        var writes = await tables.Writes(cancellationToken);

        await using (var connection = new NpgsqlConnection(connectionString))
        {
            await connection.OpenAsync(cancellationToken);
            await using var forget = new NpgsqlCommand("DELETE FROM load_step_mark WHERE step = @step", connection);
            forget.Parameters.AddWithValue("step", decision.Step.Name);
            await forget.ExecuteNonQueryAsync(cancellationToken);
        }

        _marks.Remove(decision.Step.Name);
        string? childLog = null;
        if (decision.Step.Children is not null)
        {
            childLog = Path.Combine(Path.GetTempPath(), $"essenthos-statements-{Guid.NewGuid():N}.txt");
            Environment.SetEnvironmentVariable(StatementTables.ChildLog, childLog);
        }

        _statements!.Start();
        return new Running(decision, before, writes, childLog);
    }

    /// <summary>
    /// After the step ran to the end: sums again the tables it wrote, and leaves its mark. Returns the tables
    /// it wrote and those whose rows it changed.
    /// </summary>
    public async Task<(IReadOnlyList<string> Wrote, IReadOnlyList<string> Moved)> Finish(Running running, CancellationToken cancellationToken)
    {
        var (heard, _) = _statements!.Stop();
        var named = new HashSet<string>(heard, StringComparer.Ordinal);
        if (running.ChildLog is { } childLog)
        {
            Environment.SetEnvironmentVariable(StatementTables.ChildLog, null);
            if (File.Exists(childLog))
            {
                named.UnionWith(File.ReadLines(childLog).Where(tables.Tables.Contains));
                File.Delete(childLog);
            }
        }

        var step = running.Decision.Step;
        var writesAfter = await tables.Writes(cancellationToken);
        var wrote = writesAfter
            .Where(write => write.Value != running.Writes.GetValueOrDefault(write.Key))
            .Select(write => write.Key)
            .Where(tables.Tables.Contains)
            .Order(StringComparer.Ordinal)
            .ToList();
        var after = await tables.Of(wrote, cancellationToken);
        var moved = wrote.Where(table => after[table] != running.Before.GetValueOrDefault(table)).ToList();

        string[] reads =
        [
            .. named.Concat(wrote).Append(TableFingerprints.Migrations)
                .Except(step.Logs, StringComparer.Ordinal)
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal),
        ];
        var readSums = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var table in reads)
        {
            readSums[table] = step.KeepsItsOwnMarks && after.TryGetValue(table, out var left)
                ? left
                : running.Before.GetValueOrDefault(table, "absent");
        }

        var inputs = new Inputs(step.Version, running.Decision.Code, running.Decision.Files, readSums);
        var output = Digest(string.Join('\n', wrote.Select(table => $"{table} {after[table]}")));
        var mark = new Mark(inputs.Digest(), output, reads, inputs, DateTimeOffset.UtcNow);

        await using (var connection = new NpgsqlConnection(connectionString))
        {
            await connection.OpenAsync(cancellationToken);
            await using var keep = new NpgsqlCommand(
                """
                INSERT INTO load_step_mark (step, input_digest, output_digest, reads, inputs, at)
                VALUES (@step, @input, @output, @reads, @inputs::jsonb, @at)
                ON CONFLICT (step) DO UPDATE SET input_digest = excluded.input_digest, output_digest = excluded.output_digest,
                    reads = excluded.reads, inputs = excluded.inputs, at = excluded.at
                """,
                connection);
            keep.Parameters.AddWithValue("step", step.Name);
            keep.Parameters.AddWithValue("input", mark.InputDigest);
            keep.Parameters.AddWithValue("output", mark.OutputDigest);
            keep.Parameters.Add(new NpgsqlParameter<string[]?>("reads", NpgsqlTypes.NpgsqlDbType.Array | NpgsqlTypes.NpgsqlDbType.Text) { TypedValue = reads });
            keep.Parameters.AddWithValue("inputs", JsonSerializer.Serialize(inputs));
            keep.Parameters.AddWithValue("at", mark.At);
            await keep.ExecuteNonQueryAsync(cancellationToken);
        }

        _marks[step.Name] = mark;
        try
        {
            Directory.CreateDirectory(_codeLines);
            await File.WriteAllLinesAsync(Path.Combine(_codeLines, step.Name + ".txt"), Code(step).Lines, cancellationToken);
        }
        catch (IOException exception)
        {
            logger.LogDebug(exception, "The lines of {Step}'s code were not kept", step.Name);
        }

        return (wrote, moved);
    }

    /// <summary>
    /// How many times the steps are gone through in one load before it stops settling: the first time,
    /// and as often again as a step changes what a step before it read.
    /// </summary>
    public const int Passes = 4;

    /// <summary>
    /// Runs the steps in order, each skipped when its mark says nothing it reads moved, unless
    /// <paramref name="everything"/>; <paramref name="starting"/> hears of each step before it is
    /// decided, so a failure can be put down to it. Stops at the first step that throws.
    ///
    /// <para>
    /// A step can change a table that a step before it read, or that it read itself; that step's mark
    /// then no longer holds, and the next load would run it. So the load goes through the steps again
    /// from the first such step, marks deciding, until nothing it went through changes what an earlier
    /// step read: the load after it finds nothing to run. A load that has not settled after
    /// <see cref="Passes"/> passes says which steps keep moving what others read, and leaves their
    /// marks as they are, so the next load runs them too.
    /// </para>
    /// </summary>
    public async Task<IReadOnlyList<StepOutcome>> Run(
        IReadOnlyList<LoadStep> steps,
        bool everything,
        Action<LoadStep> starting,
        CancellationToken cancellationToken)
    {
        var outcomes = new List<StepOutcome>();
        files.Prefetch(steps.SelectMany(step => step.Files), cancellationToken);
        var pass = steps;
        for (var passes = 1; ; passes++)
        {
            var moved = await Pass(pass, everything && passes == 1, starting, outcomes, cancellationToken);
            var unsettled = await Unsettled(steps, moved, cancellationToken);
            if (unsettled.Count == 0)
            {
                return outcomes;
            }

            var why = string.Join("; ", unsettled.Select(stale => $"{stale.Step.Name} ({stale.Why})"));
            if (passes == Passes)
            {
                logger.LogWarning(
                    "The load did not settle in {Passes} passes: these steps read what a step of the load changes every time, "
                    + "and the next load runs them again: {Steps}",
                    Passes, why);
                return outcomes;
            }

            var first = unsettled.Min(stale => stale.Index);
            logger.LogInformation(
                "Going through the steps again from {Step}: a step of this load changed what {Count} step(s) had read: {Steps}",
                steps[first].Name, unsettled.Count, why);
            pass = [.. steps.Skip(first)];
        }
    }

    /// <summary>
    /// The steps whose marks a table moved in the pass no longer hold: they read it as it stood before.
    /// A step that runs every time decides from the run, not from a mark, and is not counted.
    /// </summary>
    private async Task<List<(int Index, LoadStep Step, string Why)>> Unsettled(
        IReadOnlyList<LoadStep> steps,
        IReadOnlySet<string> moved,
        CancellationToken cancellationToken)
    {
        var unsettled = new List<(int, LoadStep, string)>();
        if (moved.Count == 0)
        {
            return unsettled;
        }

        for (var index = 0; index < steps.Count; index++)
        {
            var step = steps[index];
            if (step.Always || !_marks.TryGetValue(step.Name, out var mark) || mark.Reads is null || !mark.Reads.Any(moved.Contains))
            {
                continue;
            }

            var decision = await Decide(step, force: false, cancellationToken);
            if (!decision.Skip)
            {
                unsettled.Add((index, step, decision.Why));
            }
        }

        return unsettled;
    }

    /// <summary>Goes through the steps once; returns the tables whose rows a step changed.</summary>
    private async Task<IReadOnlySet<string>> Pass(
        IReadOnlyList<LoadStep> steps,
        bool everything,
        Action<LoadStep> starting,
        List<StepOutcome> outcomes,
        CancellationToken cancellationToken)
    {
        var changed = new HashSet<string>(StringComparer.Ordinal);
        foreach (var step in steps)
        {
            starting(step);
            var deciding = System.Diagnostics.Stopwatch.GetTimestamp();
            var decision = await Decide(step, everything, cancellationToken);
            if (decision.Skip)
            {
                var spent = System.Diagnostics.Stopwatch.GetElapsedTime(deciding);
                logger.LogInformation("Step {Step} skipped in {Seconds:F1} s: {Why}", step.Name, spent.TotalSeconds, decision.Why);
                outcomes.Add(new StepOutcome(step.Name, false, decision.Why, spent, [], []));
                continue;
            }

            logger.LogInformation("Step {Step} runs: {Why}", step.Name, decision.Why);
            var running = await Begin(decision, cancellationToken);
            var started = System.Diagnostics.Stopwatch.GetTimestamp();
            try
            {
                await step.Run(cancellationToken);
            }
            catch
            {
                Environment.SetEnvironmentVariable(StatementTables.ChildLog, null);
                throw;
            }

            var took = System.Diagnostics.Stopwatch.GetElapsedTime(started);
            logger.LogInformation("Step {Step} took {Seconds:F1} s", step.Name, took.TotalSeconds);

            var (wrote, moved) = await Finish(running, cancellationToken);
            if (wrote.Count > 0)
            {
                logger.LogInformation(
                    "Step {Step} wrote {Wrote} table(s) and changed the rows of {Moved}: {Tables}",
                    step.Name, wrote.Count, moved.Count, moved.Count == 0 ? "none" : string.Join(", ", moved));
            }

            outcomes.Add(new StepOutcome(step.Name, true, decision.Why, took, wrote, moved));
            foreach (var table in moved)
            {
                changed.Add(table);
                _movedBy[table] = step.Name;
            }
        }

        return changed;
    }

    /// <summary>How long the table sums took in this run, and how many were taken.</summary>
    public (TimeSpan Took, int Tables) Summing => tables.Spent;

    private (string Digest, IReadOnlyList<string> Lines) Code(LoadStep step)
    {
        if (_code.TryGetValue(step.Name, out var known))
        {
            return known;
        }

        var reaches = (step.Children?.Invoke() ?? []).Prepend(step.Run).Select(code.Of).ToList();
        var digest = reaches.Count == 1
            ? reaches[0].Digest
            : Digest(string.Join('\n', reaches.Skip(1).Select(reach => reach.Digest).Order(StringComparer.Ordinal).Prepend(reaches[0].Digest)));
        var lines = reaches.SelectMany(reach => reach.Lines).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
        return _code[step.Name] = (digest, lines);
    }

    /// <summary>The methods whose code moved since the step last ran, as far as the lines kept from then tell.</summary>
    private string? CodeMoved(LoadStep step)
    {
        var kept = Path.Combine(_codeLines, step.Name + ".txt");
        if (!File.Exists(kept))
        {
            return null;
        }

        var then = File.ReadLines(kept).ToHashSet(StringComparer.Ordinal);
        var moved = Code(step).Lines
            .Where(line => !then.Contains(line))
            .Select(line => line[..Math.Max(0, line.LastIndexOf(' '))])
            .Distinct(StringComparer.Ordinal)
            .ToList();
        return moved.Count == 0
            ? null
            : string.Join(", ", moved.Take(5)) + (moved.Count > 5 ? $" and {moved.Count - 5} more" : "");
    }

    /// <summary>What moved between the inputs a mark recorded and the inputs now, in a line.</summary>
    private static string Moved(Inputs then, Inputs now, string? codeMoved, IReadOnlyDictionary<string, string> movedBy)
    {
        var moved = new List<string>();
        if (then.Version != now.Version)
        {
            moved.Add($"its version ({then.Version} → {now.Version})");
        }

        if (then.Code != now.Code)
        {
            moved.Add(codeMoved is null ? "its code" : $"its code ({codeMoved})");
        }

        moved.AddRange(now.Files
            .Where(file => then.Files.GetValueOrDefault(file.Key) != file.Value)
            .Select(file => $"file {file.Key}"));
        moved.AddRange(then.Files.Keys.Except(now.Files.Keys).Select(file => $"file {file} no longer read"));
        moved.AddRange(now.Tables
            .Where(table => then.Tables.GetValueOrDefault(table.Key) != table.Value)
            .Select(table => movedBy.TryGetValue(table.Key, out var by) ? $"table {table.Key}, which {by} changed" : $"table {table.Key}"));
        return moved.Count == 0
            ? "its inputs moved"
            : string.Join(", ", moved.Take(10)) + (moved.Count > 10 ? $" and {moved.Count - 10} more" : "");
    }

    private static string Digest(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));

    /// <summary>What became of one step: whether it ran and why, how long it took, what it wrote and changed.</summary>
    internal sealed record StepOutcome(
        string Step,
        bool Ran,
        string Why,
        TimeSpan Took,
        IReadOnlyList<string> Wrote,
        IReadOnlyList<string> Moved);

    /// <summary>Whether a step is skipped and why, with the code and file digests it was decided on.</summary>
    internal sealed record Decision(
        LoadStep Step,
        bool Skip,
        string Why,
        int Version,
        string Code,
        IReadOnlyDictionary<string, string> Files);

    /// <summary>A step that has started: the tables' sums and write counts as it found them.</summary>
    internal sealed record Running(
        Decision Decision,
        IReadOnlyDictionary<string, string> Before,
        IReadOnlyDictionary<string, long> Writes,
        string? ChildLog);

    private sealed record Mark(string InputDigest, string OutputDigest, string[]? Reads, Inputs Inputs, DateTimeOffset At);

    /// <summary>Everything a step ran on, part by part.</summary>
    internal sealed record Inputs(
        int Version,
        string Code,
        IReadOnlyDictionary<string, string> Files,
        IReadOnlyDictionary<string, string> Tables)
    {
        public static readonly Inputs Empty = new(0, string.Empty, new Dictionary<string, string>(), new Dictionary<string, string>());

        public string Digest() =>
            LoadMarks.Digest(string.Join(
                '\n',
                [
                    $"version {Version}",
                    $"code {Code}",
                    .. Files.OrderBy(file => file.Key, StringComparer.Ordinal).Select(file => $"file {file.Key} {file.Value}"),
                    .. Tables.OrderBy(table => table.Key, StringComparer.Ordinal).Select(table => $"table {table.Key} {table.Value}"),
                ]));
    }
}
