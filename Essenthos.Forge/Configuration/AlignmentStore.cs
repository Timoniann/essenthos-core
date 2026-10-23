namespace Essenthos.Core.Configuration;

/// <summary>
/// Where SIL's aligner keeps what it trained: one workspace per pair of texts, holding the inputs, the
/// model and the answer, reused by every later run whose inputs are the same. A rebuild from nothing
/// trains nothing (<see cref="Loading.ModelRuns"/>) and reads only these, so they are kept with the
/// corpus — <c>Resources/Alignments</c> unless <c>Dataset:AlignmentsPath</c> says otherwise: out of git,
/// mirrored by the backup, reached from a rebuild's Resources folder through its junctions. The system's
/// temporary folder, where they were kept before, is emptied by the operating system's cleanup.
/// </summary>
internal static class AlignmentStore
{
    public const string ConfigurationKey = "Dataset:AlignmentsPath";

    /// <summary>The folder under Resources the store is by default.</summary>
    public const string FolderName = "Alignments";

    /// <summary>
    /// Where the workspaces were kept before. A workspace the store does not hold is copied in from
    /// here the first time it is asked for, if it holds anything; nothing is written here any more.
    /// </summary>
    public static string Former => Path.Combine(Path.GetTempPath(), "essenthos-align");

    private static Func<string>? _resolve;
    private static string? _root;
    private static bool _imports;

    /// <summary>
    /// Read the store's place from configuration when it is first needed, so a verb that aligns nothing
    /// does not need the corpus folder to be found. Called once by the Forge's host. A process that never
    /// calls it — a test — gets a folder of its own and imports nothing.
    /// </summary>
    public static void Configure(IConfiguration configuration, string contentRootPath)
    {
        _resolve = () => Read(configuration, contentRootPath);
        _root = null;
        _imports = true;
    }

    /// <summary>A configured path resolves against the content root; otherwise the folder under Resources.</summary>
    public static string Read(IConfiguration configuration, string contentRootPath) =>
        configuration[ConfigurationKey] is { } configured && !string.IsNullOrWhiteSpace(configured)
            ? Path.GetFullPath(Path.Combine(contentRootPath, configured))
            : Path.Combine(ResourcePaths.Read(configuration, contentRootPath), FolderName);

    public static string Root => _root ??= _resolve?.Invoke()
                                          ?? Path.Combine(Path.GetTempPath(), "essenthos-align-test", $"store-{Environment.ProcessId}");

    /// <summary>The workspace of that name, copied in from <see cref="Former"/> if only that holds it.</summary>
    public static string Workspace(string name)
    {
        var workspace = Path.Combine(Root, name);
        if (_imports)
        {
            Import(Path.Combine(Former, name), workspace);
        }

        return workspace;
    }

    /// <summary>
    /// Copies <paramref name="former"/> to <paramref name="workspace"/> when the store has no such
    /// workspace and the former one holds at least one file that is not empty. An emptied folder is not
    /// copied, so it is trained again rather than mistaken for an answer. The copy is made beside the
    /// workspace and renamed into place, so a copy cut short never stands as the workspace.
    /// </summary>
    internal static bool Import(string former, string workspace)
    {
        if (Directory.Exists(workspace) || !Directory.Exists(former) || !HoldsAnything(former))
        {
            return false;
        }

        var staging = $"{workspace}.importing-{Guid.NewGuid():N}";
        Copy(former, staging);
        try
        {
            Directory.Move(staging, workspace);
            return true;
        }
        catch (IOException) when (Directory.Exists(workspace))
        {
            Directory.Delete(staging, recursive: true);
            return false;
        }
    }

    private static bool HoldsAnything(string folder) =>
        Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories).Any(file => new FileInfo(file).Length > 0);

    private static void Copy(string from, string to)
    {
        Directory.CreateDirectory(to);
        foreach (var file in Directory.EnumerateFiles(from))
        {
            File.Copy(file, Path.Combine(to, Path.GetFileName(file)));
        }

        foreach (var folder in Directory.EnumerateDirectories(from))
        {
            Copy(folder, Path.Combine(to, Path.GetFileName(folder)));
        }
    }

    /// <summary>
    /// Writes the file whole or not at all: the lines go into a file beside it, which is then renamed
    /// over it. A run stopped half way leaves the earlier file, or none, never a truncated one.
    /// </summary>
    public static void WriteWhole(string path, IEnumerable<string> lines)
    {
        var partial = path + ".partial";
        File.WriteAllLines(partial, lines);
        File.Move(partial, path, overwrite: true);
    }
}
