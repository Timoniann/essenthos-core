namespace Essenthos.Core.Loading;

/// <summary>
/// One step of the load, under the name <c>load --from</c> resumes at and <c>load --only</c> names.
/// </summary>
/// <param name="Name">The step's name.</param>
/// <param name="Run">The step.</param>
internal sealed record LoadStep(string Name, Func<CancellationToken, Task> Run)
{
    /// <summary>The group <c>load --only</c> can name instead of the step (<see cref="LoadModules"/>).</summary>
    public string Module { get; init; } = string.Empty;

    /// <summary>
    /// The files and folders under Resources the step reads. Their bytes are part of what it runs on, so a
    /// changed file runs it again. Its code, and the files compiled into the Forge it names, are found
    /// without being listed (<see cref="Marks.CodeDigest"/>).
    /// </summary>
    public IReadOnlyList<Marks.StepFile> Files { get; init; } = [];

    /// <summary>
    /// Raised by hand when the step must run again for a reason its code and files cannot show: something
    /// it reads by reflection or by a name made at run time, or a change elsewhere it depends on.
    /// </summary>
    public int Version { get; init; } = 1;

    /// <summary>
    /// What the step runs as Forge processes of its own — the recipe's replayed verbs — named by their
    /// code. That code is part of the step's, and what their statements name reaches this process through
    /// the file <see cref="Marks.StatementTables.ChildLog"/> names, so their reads are the step's reads.
    /// Null for a step that runs everything in this process.
    /// </summary>
    public Func<IEnumerable<Delegate>>? Children { get; init; }

    /// <summary>
    /// Tables the step only appends a record of its own run to — verify its measures — and reads only to
    /// compare with its last record in its log. They are not what it works from, so its own last record
    /// does not run it again.
    /// </summary>
    public IReadOnlyList<string> Logs { get; init; } = [];

    /// <summary>The step runs every time: it decides from what this run did, not from what is stored.</summary>
    public bool Always { get; init; }

    /// <summary>
    /// The step keeps finer marks of its own (the recipe, one per line: <see cref="Marks.RecipeMarks"/>) and
    /// decides inside what it runs. Its step mark holds the tables it wrote as it left them, so its own
    /// writes do not run it again; a step after it writing them does, and its own marks then decide.
    /// </summary>
    public bool KeepsItsOwnMarks { get; init; }
}
