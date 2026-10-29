using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Npgsql;

namespace Essenthos.Core.Loading.Marks;

/// <summary>One line of the recipe as the marks see it: the step, its key in the marks, what it reads.</summary>
internal sealed record RecipeLine(RecipeStep Step, string Command, IReadOnlyList<string> Texts, IReadOnlyList<StepFile> Files);

/// <summary>What running a line did: whether it got past deciding the corpus already holds it, and what its statements named.</summary>
internal sealed record RecipeRun(bool Started, IReadOnlySet<string> Heard)
{
    public static RecipeRun AlreadyThere { get; } = new(false, new HashSet<string>());
}

/// <summary>What became of one line: whether it ran and why, how long it took, whether it wrote rows of its texts.</summary>
internal sealed record RecipeLineOutcome(string Command, bool Ran, string Why, TimeSpan Took, bool Wrote);

/// <summary>
/// Which lines of the recipe a load runs: each is skipped when nothing it reads has moved since it last
/// ran, so a line appended to the recipe runs alone and a line changed in the middle runs with the lines
/// after it that read its texts.
///
/// <para>
/// **A line reads the corpus as the lines before it left it.** What it is keyed on for each text it reads
/// is a chain: the generations of the parts of the text its statements read (<see cref="TextDigests.Parts"/>),
/// then the contribution of every line before it that reads the text. A line's contribution is the digest
/// of what it can write of its texts (their words, their names, their pairs with each other) as its last run
/// that changed them left it, so a line whose rerun writes nothing, or writes back the rows that were there,
/// leaves everything after it as it was. A line's own writes, and the
/// writes of a line after it, never move what it is keyed on: they come after it.
/// </para>
///
/// <para>
/// **A change from outside the recipe moves the texts it touched.** Each text's rows are digested when the
/// recipe starts (<see cref="TextDigests"/>), part by part, and compared with their digest when the recipe
/// last finished; a part that differs was moved by a load step, or a Forge verb, since, and its generation
/// moves, so exactly the lines reading that part of the text run: a name written on a word runs the lines
/// whose statements read names, not every line that aligns the text. The other tables a line's statements
/// named count whole, the same way. A table no line reads moves nothing.
/// </para>
///
/// <para>
/// A line taken out of the recipe loses its mark, so the lines after it that read its texts run again;
/// what it wrote stays in the corpus (no link records the run that wrote it), and the log says so.
/// </para>
/// </summary>
internal sealed class RecipeMarks(
    string connectionString,
    TableFingerprints tables,
    FileDigests files,
    Func<string, string> code,
    ILogger logger)
{
    /// <summary>Raised when every line must run again for a reason nothing else shows.</summary>
    public const int Version = 2;

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = false };

    private readonly Dictionary<string, Mark> _marks = new(StringComparer.Ordinal);
    private readonly Dictionary<string, (string Generation, string Settled)> _inputs = new(StringComparer.Ordinal);

    /// <summary>
    /// Runs the lines in order, each through <paramref name="run"/> unless its mark says nothing it reads
    /// moved (or <paramref name="everything"/>), then <paramref name="finish"/>. Stops at the first line that
    /// throws; the lines before it keep their marks.
    /// </summary>
    /// <param name="finish">
    /// What the recipe does after its last line and before it digests the texts it leaves: the passes that
    /// take back what the lines wrote and the corpus refuses, so that what the recipe leaves is already what
    /// they would leave, and a later step taking it back does not move the lines' texts every time they run.
    /// </param>
    public async Task<IReadOnlyList<RecipeLineOutcome>> Run(
        IReadOnlyList<RecipeLine> lines,
        bool everything,
        Func<RecipeLine, CancellationToken, Task<RecipeRun>> run,
        CancellationToken cancellationToken,
        Func<CancellationToken, Task>? finish = null)
    {
        await tables.Open(cancellationToken);
        await Read(cancellationToken);
        files.Prefetch(lines.SelectMany(line => line.Files), cancellationToken);

        var measuring = Stopwatch.GetTimestamp();
        var otherTables = tables.Tables.Where(Counted).ToList();
        var pieces = await TextDigests.Read(connectionString, null, among: false, cancellationToken);
        var texts = TextDigests.PartsOf(pieces, null);
        var sums = await tables.Of(otherTables, cancellationToken);
        var outside = Generations(texts, sums);
        logger.LogInformation(
            "Recipe: the rows of {Texts} texts and {Tables} other tables digested in {Seconds:F1} s; moved outside the recipe since it last ran: {Moved}",
            texts.Count, otherTables.Count, Stopwatch.GetElapsedTime(measuring).TotalSeconds,
            outside.Count == 0 ? "nothing" : string.Join(", ", outside.Order(StringComparer.Ordinal).Take(20)) + (outside.Count > 20 ? ", ..." : ""));

        var present = lines.Select(line => line.Command).ToHashSet(StringComparer.Ordinal);
        var removed = _marks.Keys.Where(command => !present.Contains(command)).Order(StringComparer.Ordinal).ToList();
        foreach (var command in removed)
        {
            logger.LogWarning(
                "Recipe: `{Command}` was taken out of the recipe; the lines after it that read its texts run again. "
                + "What it wrote stays in the corpus until a rebuild", command);
        }

        var outcomes = new List<RecipeLineOutcome>();
        var current = new Dictionary<TextDigests.Piece, string>(pieces.Sums);
        var chain = new Dictionary<string, string>(StringComparer.Ordinal);
        var changedBy = new Dictionary<string, string>(StringComparer.Ordinal);
        var wroteAny = false;
        try
        {
            foreach (var line in lines)
            {
                _marks.TryGetValue(line.Command, out var mark);
                var parts = mark?.Inputs.Parts ?? TextDigests.Parts;
                var inputs = InputsOf(line, mark?.Tables ?? [], parts, chain, cancellationToken);
                var key = inputs.Digest();
                string? contribution;
                if (!everything && mark is not null && mark.Key == key)
                {
                    contribution = mark.Contribution;
                    outcomes.Add(new RecipeLineOutcome(line.Command, false, "nothing it reads moved", TimeSpan.Zero, false));
                }
                else
                {
                    var why = everything ? "every line was asked for"
                        : mark is null ? "no mark says what it last ran on"
                        : Moved(mark.Inputs, inputs, parts, outside, changedBy);
                    logger.LogInformation("Recipe: `{Command}` runs: {Why}", line.Command, why);
                    await Forget(line.Command, cancellationToken);
                    _marks.Remove(line.Command);

                    var before = await tables.Writes(cancellationToken);
                    var started = Stopwatch.GetTimestamp();
                    var ran = await run(line, cancellationToken);
                    var took = Stopwatch.GetElapsedTime(started);
                    var after = await tables.Writes(cancellationToken);
                    var wrote = TextDigests.Tables.Any(table => after.GetValueOrDefault(table) != before.GetValueOrDefault(table));
                    wroteAny |= wrote;

                    // What the line changed, not that it wrote: a run that writes back the rows that were there
                    // leaves the lines after it as they were.
                    var changed = wrote ? await Changed(line, current, cancellationToken) : [];
                    contribution = changed.Count == 0 ? mark?.Contribution : Contribution(line, changed, mark?.Contribution);
                    wrote = changed.Count > 0;
                    var read = ran.Started
                        ? ran.Heard.Where(Counted).Where(table => _inputs.ContainsKey("table:" + table)).Order(StringComparer.Ordinal).ToArray()
                        : mark?.Tables ?? [];
                    var named = ran.Started ? PartsNamed(ran.Heard) : parts;
                    if (!read.SequenceEqual(mark?.Tables ?? []) || !named.SequenceEqual(parts))
                    {
                        inputs = InputsOf(line, read, named, chain, cancellationToken);
                        key = inputs.Digest();
                    }

                    await Keep(new Mark(line.Command, key, contribution, [.. line.Texts], read, inputs), cancellationToken);
                    if (ran.Started)
                    {
                        logger.LogInformation(
                            "Recipe: `{Command}` took {Seconds:F1} s and {Wrote}", line.Command, took.TotalSeconds,
                            wrote ? "wrote rows of " + string.Join(", ", line.Texts.Take(8)) + (line.Texts.Count > 8 ? ", ..." : "") : "wrote nothing");
                    }

                    foreach (var text in changed.Keys)
                    {
                        changedBy[text] = line.Command;
                    }

                    outcomes.Add(new RecipeLineOutcome(line.Command, true, why, took, wrote));
                }

                if (contribution is not null)
                {
                    foreach (var text in line.Texts)
                    {
                        chain[text] = Digest($"{chain.GetValueOrDefault(text, string.Empty)}\n{line.Command}\n{For(contribution, text)}");
                    }
                }
            }

            foreach (var command in removed)
            {
                await Forget(command, cancellationToken);
                _marks.Remove(command);
            }

            if (finish is not null)
            {
                var before = await tables.Writes(cancellationToken);
                await finish(cancellationToken);
                var after = await tables.Writes(cancellationToken);
                wroteAny |= TextDigests.Tables.Any(table => after.GetValueOrDefault(table) != before.GetValueOrDefault(table));
            }
        }
        finally
        {
            // What the lines wrote is what the lines after them are keyed on, never a change from outside.
            var settledTexts = wroteAny ? await TextDigests.PartsOf(connectionString, null, CancellationToken.None) : texts;
            var settledTables = await tables.Of(otherTables, CancellationToken.None);
            await Settle(settledTexts, settledTables);
        }

        var ranCount = outcomes.Count(outcome => outcome.Ran);
        logger.LogInformation(
            "Recipe: {Ran} of {Lines} line(s) ran ({Wrote} wrote), {Skipped} skipped, nothing they read having moved; {Removed} taken out",
            ranCount, lines.Count, outcomes.Count(outcome => outcome.Wrote), lines.Count - ranCount, removed.Count);
        return outcomes;
    }

    /// <summary>
    /// After a recorded verb was run by hand and recorded as the line <paramref name="command"/> at the end
    /// of <paramref name="lines"/>: leaves the line the mark the load would have left had it run it, so the
    /// next load does not run it again or count what it wrote as a change from outside. Only if its texts
    /// were, before it ran (<paramref name="before"/>), as the recipe last left them; otherwise something
    /// else moved them too, and the next load runs what reads them. Says whether it adopted the line.
    /// </summary>
    public async Task<bool> Adopt(
        IReadOnlyList<RecipeLine> lines,
        string command,
        IReadOnlyDictionary<string, string> before,
        IReadOnlyDictionary<string, long> writesBefore,
        IReadOnlySet<string> heard,
        CancellationToken cancellationToken)
    {
        await tables.Open(cancellationToken);
        await Read(cancellationToken);
        var index = lines.Count - 1;
        while (index >= 0 && lines[index].Command != command)
        {
            index--;
        }

        if (index < 0 || _inputs.Count == 0)
        {
            logger.LogInformation("The recipe's marks do not take `{Command}` in: the recipe has not run with marks here", command);
            return false;
        }

        var line = lines[index];
        var moved = line.Texts
            .Where(text => Settled(text) is not { } settled || settled != before.GetValueOrDefault(text))
            .ToList();
        var read = heard.Where(Counted).Order(StringComparer.Ordinal).ToArray();
        if (moved.Count > 0 || read.Any(table => !_inputs.ContainsKey("table:" + table)))
        {
            logger.LogInformation(
                "The recipe's marks do not take `{Command}` in: {Texts} had moved since the recipe last ran, "
                + "so the next load runs the lines that read them", command, moved.Count > 0 ? string.Join(", ", moved) : "a table it read");
            return false;
        }

        var chain = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var earlier in lines.Take(index))
        {
            if (_marks.TryGetValue(earlier.Command, out var mark) && mark.Contribution is { } contribution)
            {
                foreach (var text in earlier.Texts)
                {
                    chain[text] = Digest($"{chain.GetValueOrDefault(text, string.Empty)}\n{earlier.Command}\n{For(contribution, text)}");
                }
            }
        }

        var inputs = InputsOf(line, read, PartsNamed(heard), chain, cancellationToken);
        var key = inputs.Digest();
        var writes = await tables.Writes(cancellationToken);
        var wrote = TextDigests.Tables.Any(table => writes.GetValueOrDefault(table) != writesBefore.GetValueOrDefault(table));
        _marks.TryGetValue(command, out var old);
        await Keep(new Mark(command, key, wrote ? await Left(line, cancellationToken) : old?.Contribution, [.. line.Texts], read, inputs), cancellationToken);
        await Settle(await TextDigests.PartsOf(connectionString, line.Texts, cancellationToken), new Dictionary<string, string>());
        logger.LogInformation("The recipe's marks take `{Command}` in: the next load does not run it again", command);
        return true;
    }

    /// <summary>A table a line's mark counts whole: neither one the texts' digests cover nor the marks' own.</summary>
    private bool Counted(string table) =>
        !TextDigests.Tables.Contains(table) && table != TableFingerprints.Migrations && !TableFingerprints.Unwatched.Contains(table)
        && tables.Tables.Contains(table);

    /// <summary>
    /// Moves the generation of every text and table whose rows are not what they were when the recipe last
    /// finished, and returns their names.
    /// </summary>
    private HashSet<string> Generations(
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> texts, IReadOnlyDictionary<string, string> sums)
    {
        var moved = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (name, now) in Named(texts).Concat(sums.Select(table => ("table:" + table.Key, table.Value))))
        {
            if (_inputs.TryGetValue(name, out var input) && input.Settled == now)
            {
                continue;
            }

            if (_inputs.ContainsKey(name))
            {
                moved.Add(name);
            }

            _inputs[name] = (Digest($"{input.Generation}\n{now}"), now);
        }

        return moved;
    }

    private Inputs InputsOf(
        RecipeLine line,
        IReadOnlyList<string> read,
        IReadOnlyList<string> parts,
        Dictionary<string, string> chain,
        CancellationToken cancellationToken) =>
        new(
            Version,
            code(line.Step.Verb),
            new SortedDictionary<string, string>(
                files.Of(line.Files, cancellationToken).ToDictionary(file => file.Name, file => file.Digest, StringComparer.Ordinal),
                StringComparer.Ordinal),
            new SortedDictionary<string, string>(
                line.Texts.Distinct(StringComparer.Ordinal).ToDictionary(text => text, text => Chain(chain, text, parts), StringComparer.Ordinal),
                StringComparer.Ordinal),
            new SortedDictionary<string, string>(
                read.ToDictionary(table => table, table => _inputs.TryGetValue("table:" + table, out var input) ? input.Generation : "absent", StringComparer.Ordinal),
                StringComparer.Ordinal))
        {
            Parts = parts,
        };

    /// <summary>
    /// A text as a line reads it: the generations of the parts it reads, then what every line before it that
    /// reads the text left.
    /// </summary>
    private string Chain(Dictionary<string, string> chain, string text, IReadOnlyList<string> parts) =>
        Digest(string.Join(
            '\n',
            [
                .. parts.Select(part => $"{part} {(_inputs.TryGetValue(PartName(text, part), out var input) ? input.Generation : "absent")}"),
                chain.GetValueOrDefault(text, string.Empty),
            ]));

    /// <summary>The parts of a text a line's statements read; all of them where they named none of a text's tables.</summary>
    private static IReadOnlyList<string> PartsNamed(IEnumerable<string> heard)
    {
        var named = heard.Where(TextDigests.PartOf.ContainsKey).Select(table => TextDigests.PartOf[table]).ToHashSet(StringComparer.Ordinal);
        return named.Count == 0 ? TextDigests.Parts : [.. TextDigests.Parts.Where(named.Contains)];
    }

    /// <summary>What a line left of its texts: each text's digest as its run left it, a line a text.</summary>
    private async Task<string> Left(RecipeLine line, CancellationToken cancellationToken)
    {
        var left = await TextDigests.Of(connectionString, line.Texts, cancellationToken);
        return string.Join('\n', left.OrderBy(text => text.Key, StringComparer.Ordinal).Select(text => $"{text.Key} {text.Value}"));
    }

    /// <summary>
    /// The texts of a line that wrote whose rows it changed, each with the digest of what the line can write
    /// of it as it left it: its own words and names and its pairs with the line's other texts.
    /// <paramref name="current"/> holds those pieces as the lines before it left them, and is moved on.
    /// </summary>
    private async Task<Dictionary<string, string>> Changed(
        RecipeLine line, Dictionary<TextDigests.Piece, string> current, CancellationToken cancellationToken)
    {
        var mine = line.Texts.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var after = (await TextDigests.Read(connectionString, line.Texts, among: true, cancellationToken)).Sums;
        var gone = current.Keys.Where(piece => mine.Contains(piece.From) && (piece.To is null || mine.Contains(piece.To)))
            .Where(piece => !after.ContainsKey(piece))
            .ToList();
        var moved = after.Where(piece => current.GetValueOrDefault(piece.Key) != piece.Value).Select(piece => piece.Key)
            .Concat(gone)
            .ToList();
        foreach (var piece in gone)
        {
            current.Remove(piece);
        }

        foreach (var piece in moved.Where(after.ContainsKey))
        {
            current[piece] = after[piece];
        }

        return moved
            .SelectMany(piece => piece.To is { } to ? new[] { piece.From, to } : [piece.From])
            .Distinct(StringComparer.Ordinal)
            .ToDictionary(
                text => text,
                text => TextDigests.Digest(after.Where(piece => piece.Key.From == text || piece.Key.To == text)),
                StringComparer.Ordinal);
    }

    /// <summary>
    /// A line's contribution once it changed <paramref name="changed"/>: each of those texts as it left
    /// them, and for each other text of the line what its last contribution said of it.
    /// </summary>
    private static string Contribution(RecipeLine line, IReadOnlyDictionary<string, string> changed, string? last) =>
        string.Join('\n', line.Texts.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).Select(text =>
            $"{text} {(changed.TryGetValue(text, out var digest) ? digest : last is null ? "unchanged" : For(last, text))}"));

    /// <summary>
    /// What a line's contribution says of one of its texts: that text's digest as the line left it, so a line
    /// after it reading only that text does not run because the line's other text moved.
    /// </summary>
    private static string For(string contribution, string text)
    {
        foreach (var entry in contribution.Split('\n'))
        {
            if (entry.Length > text.Length && entry.StartsWith(text, StringComparison.Ordinal) && entry[text.Length] == ' ')
            {
                return entry[(text.Length + 1)..];
            }
        }

        return contribution;
    }

    /// <summary>A text's digest whole as the recipe last left it, or null where it has none.</summary>
    private string? Settled(string text)
    {
        var parts = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var part in TextDigests.Parts)
        {
            if (!_inputs.TryGetValue(PartName(text, part), out var input))
            {
                return null;
            }

            parts[part] = input.Settled;
        }

        return TextDigests.Whole(parts);
    }

    private static string PartName(string text, string part) => $"text:{text}/{part}";

    private static IEnumerable<(string Name, string Digest)> Named(IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> texts) =>
        texts.SelectMany(text => text.Value.Select(part => (PartName(text.Key, part.Key), part.Value)));

    /// <summary>What moved between a mark's inputs and the inputs now, in a line.</summary>
    private static string Moved(
        Inputs then,
        Inputs now,
        IReadOnlyList<string> parts,
        IReadOnlySet<string> outside,
        IReadOnlyDictionary<string, string> changedBy)
    {
        var moved = new List<string>();
        if (then.Version != now.Version)
        {
            moved.Add($"the marks' version ({then.Version} → {now.Version})");
        }

        if (then.Code != now.Code)
        {
            moved.Add("its verb's code");
        }

        moved.AddRange(now.Files.Where(file => then.Files.GetValueOrDefault(file.Key) != file.Value).Select(file => $"file {file.Key}"));
        moved.AddRange(then.Files.Keys.Except(now.Files.Keys).Select(file => $"file {file} no longer read"));
        moved.AddRange(now.Texts
            .Where(text => then.Texts.GetValueOrDefault(text.Key) != text.Value)
            .Select(text => parts.Where(part => outside.Contains(PartName(text.Key, part))).ToList() is { Count: > 0 } outsideParts
                ? $"text {text.Key}, its {string.Join(" and ", outsideParts)} moved outside the recipe"
                : changedBy.TryGetValue(text.Key, out var by) ? $"text {text.Key}, which `{by}` changed"
                : $"text {text.Key} (a line before it that reads it was added, taken out, moved or wrote)"));
        moved.AddRange(now.Tables
            .Where(table => then.Tables.GetValueOrDefault(table.Key) != table.Value)
            .Select(table => $"table {table.Key}, moved outside the recipe"));
        return moved.Count == 0
            ? "its inputs moved"
            : string.Join(", ", moved.Take(10)) + (moved.Count > 10 ? $" and {moved.Count - 10} more" : "");
    }

    /// <summary>The name a line's mark is kept under in <c>load_step_mark</c>, beside the load's steps.</summary>
    public const string LinePrefix = "recipe line: ";

    /// <summary>The name a text's or table's generation is kept under in <c>load_step_mark</c>.</summary>
    public const string InputPrefix = "recipe input: ";

    // The marks live in load_step_mark, under names no step has: a line's key as its input digest, its
    // contribution (or nothing) as its output digest, the other tables it read as its reads; a text's or
    // table's generation as the input digest and its settled digest as the output digest.
    private async Task Read(CancellationToken cancellationToken)
    {
        _marks.Clear();
        _inputs.Clear();
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            "SELECT step, input_digest, output_digest, reads, inputs::text FROM load_step_mark WHERE starts_with(step, 'recipe ')",
            connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var name = reader.GetString(0);
            if (name.StartsWith(LinePrefix, StringComparison.Ordinal))
            {
                var inputs = JsonSerializer.Deserialize<Inputs>(reader.GetString(4), Json) ?? Inputs.Empty;
                var line = name[LinePrefix.Length..];
                _marks[line] = new Mark(
                    line,
                    reader.GetString(1),
                    reader.GetString(2) is { Length: > 0 } contribution ? contribution : null,
                    [.. inputs.Texts.Keys],
                    reader.IsDBNull(3) ? [] : reader.GetFieldValue<string[]>(3),
                    inputs);
            }
            else if (name.StartsWith(InputPrefix, StringComparison.Ordinal))
            {
                _inputs[name[InputPrefix.Length..]] = (reader.GetString(1), reader.GetString(2));
            }
        }
    }

    private async Task Forget(string command, CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var forget = new NpgsqlCommand("DELETE FROM load_step_mark WHERE step = @step", connection);
        forget.Parameters.AddWithValue("step", LinePrefix + command);
        await forget.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task Keep(Mark mark, CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var keep = new NpgsqlCommand(
            """
            INSERT INTO load_step_mark (step, input_digest, output_digest, reads, inputs, at)
            VALUES (@step, @key, @contribution, @tables, @inputs::jsonb, now())
            ON CONFLICT (step) DO UPDATE SET input_digest = excluded.input_digest, output_digest = excluded.output_digest,
                reads = excluded.reads, inputs = excluded.inputs, at = excluded.at
            """,
            connection);
        keep.Parameters.AddWithValue("step", LinePrefix + mark.Command);
        keep.Parameters.AddWithValue("key", mark.Key);
        keep.Parameters.AddWithValue("contribution", mark.Contribution ?? string.Empty);
        keep.Parameters.AddWithValue("tables", mark.Tables);
        keep.Parameters.AddWithValue("inputs", JsonSerializer.Serialize(mark.Inputs, Json));
        await keep.ExecuteNonQueryAsync(cancellationToken);
        _marks[mark.Command] = mark;
    }

    private async Task Settle(
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> texts, IReadOnlyDictionary<string, string> sums)
    {
        var rows = Named(texts)
            .Concat(sums.Select(table => ("table:" + table.Key, table.Value)))
            .Select(entry => (Name: entry.Item1, Generation: _inputs.TryGetValue(entry.Item1, out var input) ? input.Generation : Digest($"\n{entry.Item2}"), Settled: entry.Item2))
            .ToList();
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var keep = new NpgsqlCommand(
            """
            INSERT INTO load_step_mark (step, input_digest, output_digest, reads, inputs, at)
            SELECT step, generation, settled, NULL, '{}'::jsonb, now() FROM unnest(@steps, @generations, @settled) AS row (step, generation, settled)
            ON CONFLICT (step) DO UPDATE SET input_digest = excluded.input_digest, output_digest = excluded.output_digest, at = excluded.at
            """,
            connection);
        keep.Parameters.AddWithValue("steps", rows.Select(row => InputPrefix + row.Name).ToArray());
        keep.Parameters.AddWithValue("generations", rows.Select(row => row.Generation).ToArray());
        keep.Parameters.AddWithValue("settled", rows.Select(row => row.Settled).ToArray());
        await keep.ExecuteNonQueryAsync();
        foreach (var row in rows)
        {
            _inputs[row.Name] = (row.Generation, row.Settled);
        }
    }

    private static string Digest(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));

    private sealed record Mark(string Command, string Key, string? Contribution, string[] Texts, string[] Tables, Inputs Inputs);

    /// <summary>Everything a line ran on, part by part.</summary>
    internal sealed record Inputs(
        int Version,
        string Code,
        IReadOnlyDictionary<string, string> Files,
        IReadOnlyDictionary<string, string> Texts,
        IReadOnlyDictionary<string, string> Tables)
    {
        /// <summary>The parts of its texts the line's statements read (<see cref="TextDigests.Parts"/>); null in a mark made before they were told apart.</summary>
        public IReadOnlyList<string>? Parts { get; init; }

        public static readonly Inputs Empty = new(
            0, string.Empty, new Dictionary<string, string>(), new Dictionary<string, string>(), new Dictionary<string, string>());

        public string Digest() =>
            RecipeMarks.Digest(string.Join(
                '\n',
                [
                    $"version {Version}",
                    $"code {Code}",
                    .. Files.OrderBy(file => file.Key, StringComparer.Ordinal).Select(file => $"file {file.Key} {file.Value}"),
                    .. Texts.OrderBy(text => text.Key, StringComparer.Ordinal).Select(text => $"text {text.Key} {text.Value}"),
                    .. Tables.OrderBy(table => table.Key, StringComparer.Ordinal).Select(table => $"table {table.Key} {table.Value}"),
                ]));
    }
}
