using System.Diagnostics;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Essenthos.Core.Configuration;
using Essenthos.Core.Loading.Links;
using Essenthos.Core.Verbs;
using Npgsql;

namespace Essenthos.Core.Loading;

/// <param name="Verb">The Forge verb, as typed: one whose <see cref="ForgeVerb.Records"/> says it is recorded.</param>
/// <param name="Arguments">What followed the verb, flags included.</param>
/// <param name="At">When it last ran, or null for a step written down from what the corpus held rather than recorded as it ran.</param>
/// <param name="Note">Why the step is there, for a step somebody wrote by hand.</param>
internal sealed record RecipeStep(string Verb, IReadOnlyList<string> Arguments, DateTimeOffset? At = null, string? Note = null)
{
    public override string ToString() => string.Join(' ', [Verb, .. Arguments]);
}

internal sealed record RecipeFile(string About, IReadOnlyList<RecipeStep> Steps);

/// <summary>
/// The Forge runs that write links no step of the load writes, in the order they last ran, kept in
/// <c>Resources/Essenthos/recipe.json</c> so that a load into an empty database ends with the corpus
/// this machine has and not a smaller one.
///
/// <para>
/// **Every such verb records itself.** A run that finishes appends its own line; a line that ran
/// before with the same arguments moves to the end instead of being written twice, because the
/// corpus holds what the latest run of a pair wrote and the order is what the replay has to repeat.
/// A trial (<c>--dry-run</c>, or <c>names</c> without <c>--apply</c>) writes nothing and records
/// nothing.
/// </para>
///
/// <para>
/// **Replaying is idempotent.** Each step is run as its own Forge process, exactly as it was typed,
/// unless the corpus already holds what it writes: a pair the aligner or a composition has reached,
/// a pair the names have settled, a numbering already linked. A composition whose middle text gained
/// links since it was drawn is replayed with <see cref="Refresh"/>, which redraws those verses alone.
/// <c>reload</c> is never run again: a load reads that text afresh anyway, and on a warm corpus it
/// would delete the links the next steps are skipped for. EVIDENTIA's verdicts are not here; they
/// have their own ledger, which the load replays after this.
/// </para>
/// </summary>
internal static class Recipe
{
    public static readonly string[] FileName = ["Essenthos", "recipe.json"];

    /// <summary>
    /// Set in the environment of a replayed step, so the run does not record itself a second time.
    /// An argument would be read by the verbs that take their witnesses from what follows them.
    /// </summary>
    public const string Replaying = "ESSENTHOS_RECIPE_STEP";

    /// <summary>The compose flag that redraws only the verses whose middle text gained links since (<see cref="CompositionPipeline.Stale"/>).</summary>
    public const string Refresh = "--refresh";

    private const string About =
        "The Forge runs that write links the load does not, in the order they last ran. Each verb records itself "
        + "here when it finishes; the load replays them after its own linking steps, skipping what the corpus already holds.";

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static string PathIn(string resources) => Path.Combine([resources, .. FileName]);

    public static IReadOnlyList<RecipeStep> Read(string resources)
    {
        var path = PathIn(resources);
        return File.Exists(path)
            ? JsonSerializer.Deserialize<RecipeFile>(File.ReadAllText(path), Json)?.Steps ?? []
            : [];
    }

    /// <summary>
    /// Whether a finished run of these arguments changed the corpus in a way the load would not
    /// repeat, as its verb declares (<see cref="ForgeVerbs"/>): a trial writes nothing, and a replayed
    /// step is already in the recipe.
    /// </summary>
    public static bool Records(string[] args) =>
        args.Length > 0
        && ForgeVerbs.Find(args[0])?.Records is { } records
        && Environment.GetEnvironmentVariable(Replaying) is null
        && !args.Contains("--dry-run")
        && records(args);

    /// <summary>The step a run of these arguments is recorded as.</summary>
    public static RecipeStep StepOf(string[] args, DateTimeOffset? at = null) =>
        // A configuration switch (--Database:ConnectionString=…) belongs to the machine, not the step.
        new(args[0], [.. args[1..].Where(argument => !(argument.StartsWith("--", StringComparison.Ordinal) && argument.Contains(':')))], at);

    public static void Record(string resources, string[] args, DateTimeOffset at)
    {
        if (!Records(args))
        {
            return;
        }

        var step = StepOf(args, at);
        var steps = Read(resources)
            .Where(earlier => !Same(earlier, step))
            .Append(step)
            .ToList();
        var path = PathIn(resources);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(new RecipeFile(About, steps), Json) + "\n");
    }

    /// <summary>The verbs that put back the EVIDENTIA ledger's verdicts on their own pair after they write.</summary>
    private static readonly HashSet<string> ReplayTheLedger = new(StringComparer.Ordinal) { "align", "compose", "names", "names-first" };

    /// <summary>The verbs that read nothing on disk but the ledger runs of their pair, if that.</summary>
    private static readonly HashSet<string> ReadOnlyTheDatabase =
        new(StringComparer.Ordinal) { "align", "compose", "names", "names-first", "possessives", "unshare", "strong" };

    /// <summary>
    /// The recipe's steps as its marks see them (<see cref="Marks.RecipeMarks"/>): each under the line the
    /// recipe dedupes it by, with the texts it reads — the slugs among its arguments and the texts its verb
    /// says it changes, every text where it cannot say — and the files it reads: the ledger runs of its own
    /// pair for a verb that replays them, nothing more for a verb that reads only the database, and
    /// <paramref name="sources"/> for the rest.
    /// </summary>
    public static IReadOnlyList<Marks.RecipeLine> Lines(
        IReadOnlyList<RecipeStep> steps, IReadOnlyCollection<string> texts, string resources, IReadOnlyList<Marks.StepFile> sources)
    {
        var known = texts.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var ledger = Path.Combine(resources, "Essenthos", "evidentia");
        var runs = Directory.Exists(ledger)
            ? Directory.EnumerateDirectories(ledger).Select(Path.GetFileName).OfType<string>().Order(StringComparer.Ordinal).ToList()
            : [];
        var seen = new Dictionary<string, int>(StringComparer.Ordinal);
        var lines = new List<Marks.RecipeLine>();
        foreach (var step in steps)
        {
            var command = string.Join(' ', [step.Verb, .. step.Arguments.Select(Normalised)]);
            seen[command] = seen.GetValueOrDefault(command) + 1;
            if (seen[command] > 1)
            {
                command += $" #{seen[command]}";
            }

            var read = Read(step, known);
            IReadOnlyList<Marks.StepFile> files = ReplayTheLedger.Contains(step.Verb)
                ? [.. runs.Where(run => run.Split('-') is [var from, var to, ..] && read.Contains(from) && read.Contains(to))
                    .Select(run => (Marks.StepFile)$"Essenthos/evidentia/{run}")]
                : ReadOnlyTheDatabase.Contains(step.Verb) ? []
                : sources;
            lines.Add(new Marks.RecipeLine(step, command, read, files));
        }

        return lines;
    }

    private static IReadOnlyList<string> Read(RecipeStep step, IReadOnlySet<string> known)
    {
        var read = step.Arguments
            .Where(argument => !argument.StartsWith("--", StringComparison.Ordinal))
            .SelectMany(argument => argument.Split(',', StringSplitOptions.RemoveEmptyEntries))
            .Select(argument => argument.ToUpperInvariant())
            .Where(known.Contains)
            .ToHashSet(StringComparer.Ordinal);
        var changes = ForgeVerbs.Find(step.Verb)?.Relinks?.Invoke([step.Verb, .. step.Arguments]);
        if (changes is { Texts: null } || (read.Count == 0 && changes is not { Texts.Count: > 0 }))
        {
            return [.. known.Select(text => text.ToUpperInvariant()).Order(StringComparer.Ordinal)];
        }

        read.UnionWith((changes?.Texts ?? []).Select(text => text.ToUpperInvariant()).Where(known.Contains));
        return [.. read.Order(StringComparer.Ordinal)];
    }

    private static bool Same(RecipeStep one, RecipeStep two) =>
        one.Verb == two.Verb
        && one.Arguments.Select(Normalised).SequenceEqual(two.Arguments.Select(Normalised));

    private static string Normalised(string argument) =>
        argument.StartsWith("--", StringComparison.Ordinal) ? argument : argument.ToUpperInvariant();

    /// <summary>
    /// The step as a load replays it: as it was recorded, or, for a composition its middle text has
    /// moved on from, with <see cref="Refresh"/>. A pair never composed is left as recorded, and drawn
    /// whole.
    /// </summary>
    public static async Task<RecipeStep> Replayed(
        NpgsqlConnection connection, RecipeStep step, CancellationToken cancellationToken)
    {
        if (step.Verb != "compose" || step.Arguments.Contains(Refresh))
        {
            return step;
        }

        var slugs = step.Arguments.Where(argument => !argument.StartsWith("--", StringComparison.Ordinal)).ToList();
        if (slugs.Count < 3)
        {
            return step;
        }

        var stale = await CompositionPipeline.Stale(
            connection, slugs[0], slugs[1].Split(',', StringSplitOptions.RemoveEmptyEntries), slugs[2],
            step.Arguments.Contains("--daughter"), cancellationToken);
        return stale.Count > 0 ? step with { Arguments = [.. step.Arguments, Refresh] } : step;
    }

    /// <summary>
    /// Why the corpus already holds what the step writes, or null when it has to run. Asked of the
    /// database the step would write to, before its process is started.
    /// </summary>
    public static async Task<string?> AlreadyThere(
        NpgsqlConnection connection, RecipeStep step, CancellationToken cancellationToken)
    {
        var slugs = step.Arguments.Where(argument => !argument.StartsWith("--", StringComparison.Ordinal))
            .Select(argument => argument.ToUpperInvariant())
            .ToList();

        switch (step.Verb)
        {
            case "reload":
                return "the load reads the text afresh itself";

            case "compose" when step.Arguments.Contains(Refresh):
                return null;

            // A line that replaces what is there asks to run whatever is there; whether it runs again
            // is its mark's to say, not the corpus's.
            case "align" when step.Arguments.Contains("--replace"):
                return null;

            // Only the aligner's own links, and only in the line's own direction, say the line has run
            // (PRB-1200). The names pass writes method `aligner` too, and an alignment the other way, or
            // of another part of the canon, is another line's: either one used to stand in for this
            // line, and a cold rebuild skipped `align RUSV KJV --replace` for 23,000 name links, which
            // left RUSV and the Brenton Septuagint without 95% of their links to the King James.
            case "align" when slugs.Count >= 2:
            case "compose" when slugs.Count >= 3:
                var from = slugs[0];
                var to = step.Verb == "align" ? slugs[1] : slugs[2];
                return await Exists(connection, DirectedLinks + " AND l.method = 'aligner'" + FromSource, from, to,
                    CompositionPipeline.Machine, cancellationToken)
                    ? $"{from} is already aligned to {to}"
                    : null;

            case "names" when slugs.Count >= 2:
                return await Exists(connection, PairLinks + FromSource, slugs[0], slugs[1],
                    NameListPass.Source, cancellationToken)
                    ? $"the names of {slugs[0]} and {slugs[1]} are already settled"
                    : null;

            case "possessives" when slugs.Count >= 2:
                return await Exists(connection, PairLinks + FromSource, slugs[0], slugs[1],
                    PossessivePass.Source, cancellationToken)
                    ? $"the possessives of {slugs[0]} are already linked to {slugs[1]}"
                    : null;

            case "synodal-strong":
                return await Exists(connection, BySource, null, null, SynodalStrongLinkLoader.Credit, cancellationToken)
                    ? "the Synodal's Strong numbering is already linked"
                    : null;

            case "union-strong":
                return await Exists(connection, BySource, null, null, UnionStrongLinkLoader.Credit, cancellationToken)
                    ? "the Union Version's Strong numbering is already linked"
                    : null;

            case "reina-valera-strong":
                return await Exists(connection, BySource, null, null, ReinaValeraStrongLinkLoader.Credit, cancellationToken)
                    ? "the Reina-Valera's Strong numbering is already linked"
                    : null;

            case "crosswire-strong":
                foreach (var numbering in CrossWireStrongLinkLoader.Named(step.Arguments))
                {
                    if (!await Exists(connection, BySource, null, null, numbering.Credit, cancellationToken))
                    {
                        return null;
                    }
                }

                return "CrossWire's Strong numberings are already linked";

            case "byzantine-apparatus":
                return await Exists(connection, BySource, null, null, ByzantineApparatusLinkLoader.Credit, cancellationToken)
                    ? "Robinson's apparatus is already linked"
                    : null;

            case "crosswire-positions":
                foreach (var module in CrossWirePositionLinkLoader.Named(step.Arguments))
                {
                    if (!await Exists(connection, BySource, null, null, module.Credit, cancellationToken))
                    {
                        return null;
                    }
                }

                return "the Received Text positions are already linked";

            case "ohb-cuv":
                return await Exists(connection, BySource, null, null, OpenHebrewCuvLinkLoader.Credit, cancellationToken)
                    ? "the Open Hebrew Bible's mapping of the Union Version is already linked"
                    : null;

            // strong, interlinear-join and correct each check for themselves.
            default:
                return null;
        }
    }

    private const string PairLinks =
        """
        SELECT EXISTS (
            SELECT 1 FROM link l
            JOIN text f ON f.id = l.from_text_id
            JOIN text t ON t.id = l.to_text_id
            WHERE ((f.slug = @from AND t.slug = @to) OR (f.slug = @to AND t.slug = @from))
        """;

    private const string DirectedLinks =
        """
        SELECT EXISTS (
            SELECT 1 FROM link l
            JOIN text f ON f.id = l.from_text_id
            JOIN text t ON t.id = l.to_text_id
            WHERE f.slug = @from AND t.slug = @to
        """;

    private const string FromSource =
        " AND l.provenance_id IN (SELECT id FROM provenance WHERE starts_with(source, @source))";

    private const string BySource =
        "SELECT EXISTS (SELECT 1 FROM link l WHERE l.provenance_id IN " +
        "(SELECT id FROM provenance WHERE starts_with(source, @source))";

    private static async Task<bool> Exists(
        NpgsqlConnection connection, string query, string? from, string? to, string? source,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(query + ")", connection);
        command.Parameters.AddWithValue("from", from ?? string.Empty);
        command.Parameters.AddWithValue("to", to ?? string.Empty);
        command.Parameters.AddWithValue("source", source ?? string.Empty);
        return await command.ExecuteScalarAsync(cancellationToken) is true;
    }

    /// <summary>
    /// The Forge as a process of its own, with one step's arguments, against the same database and
    /// the same Resources as this one: handed down in the environment, because a connection string
    /// given on this process's command line would otherwise not reach the child, and a rebuild into a
    /// scratch database would replay into the default one. What its statements name goes to
    /// <paramref name="statements"/> when one is given (<see cref="Marks.StatementTables.ChildLog"/>).
    /// </summary>
    public static ProcessStartInfo Process(RecipeStep step, string connectionString, string resources, string? statements = null)
    {
        var host = Environment.ProcessPath
                   ?? throw new InvalidOperationException("The Forge cannot find its own executable to replay the recipe with.");
        var start = new ProcessStartInfo(host) { UseShellExecute = false };

        // Started as `dotnet Essenthos.Forge.dll`, the host is dotnet and the assembly comes first.
        if (Path.GetFileNameWithoutExtension(host).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
        {
            start.ArgumentList.Add(typeof(Recipe).Assembly.Location);
        }

        start.ArgumentList.Add(step.Verb);
        foreach (var argument in step.Arguments)
        {
            start.ArgumentList.Add(argument);
        }

        start.Environment[Replaying] = step.ToString();
        start.Environment["Database__ConnectionString"] = connectionString;
        start.Environment["Dataset__ResourcesPath"] = resources;
        start.Environment["Dataset__AlignmentsPath"] = AlignmentStore.Root;
        if (statements is not null)
        {
            start.Environment[Marks.StatementTables.ChildLog] = statements;
        }

        return start;
    }
}
