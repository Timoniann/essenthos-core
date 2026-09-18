namespace Essenthos.Core.Configuration;

/// <summary>
/// Where the corpus sources live: this project's own <c>Resources</c> folder. They are about a
/// gigabyte and stay out of the repository, so a checkout has the folder and not the bytes — the
/// fetch scripts and the third parties they name put them there. The path resolves against the
/// content root, not the working directory, so it holds wherever the process was started from.
/// </summary>
internal static class ResourcePaths
{
    public const string ConfigurationKey = "Dataset:ResourcesPath";

    /// <summary>
    /// This project's own folder, one level above the content root.
    /// </summary>
    private const string DevelopmentDefault = "../Resources";

    /// <summary>The folder looked for when walking up, and the name of the default above.</summary>
    private const string FolderName = "Resources";

    /// <summary>
    /// What makes a <c>Resources</c> folder the corpus rather than a namesake. The build copies
    /// <c>Resources/WorldHistory</c> beside the assembly, so an output folder has a <c>Resources</c> of
    /// its own holding one source out of twenty, and a search that took the first folder of that name
    /// stopped there and loaded from it. The manifest is committed with the real one and nowhere else.
    /// </summary>
    private const string Marker = "MANIFEST.json";

    /// <summary>
    /// A configured path is resolved against the content root and has to be there. With nothing
    /// configured the default is tried first, and then each directory above the content root in
    /// turn, because the content root is a different place in each way this is run: the project
    /// folder under the web host, the application folder beside the assembly, and the working
    /// directory for a console host. Walking up finds the checkout's own folder from all three and
    /// asks nobody to know which one they are in.
    ///
    /// It stops at the first <c>Resources</c> directory holding the manifest, so a run started inside another checkout
    /// finds that checkout's sources. That is the right answer for a worktree, which has the
    /// repository and not the gigabyte, and the wrong one nowhere this project is run from.
    /// </summary>
    public static string Read(IConfiguration configuration, string contentRootPath)
    {
        var configured = configuration[ConfigurationKey];
        if (!string.IsNullOrWhiteSpace(configured))
        {
            var set = Path.GetFullPath(Path.Combine(contentRootPath, configured));
            return Directory.Exists(set)
                ? set
                : throw new DirectoryNotFoundException(
                    $"The corpus sources are not at {set}. \"{ConfigurationKey}\" is set to " +
                    $"\"{configured}\". Point it at this checkout's {FolderName} directory, as an " +
                    "absolute path if this is not a development run.");
        }

        var beside = Path.GetFullPath(Path.Combine(contentRootPath, DevelopmentDefault));
        if (IsCorpus(beside))
        {
            return beside;
        }

        for (var above = new DirectoryInfo(contentRootPath); above is not null; above = above.Parent)
        {
            var candidate = Path.Combine(above.FullName, FolderName);
            if (IsCorpus(candidate))
            {
                return candidate;
            }
        }

        throw new DirectoryNotFoundException(
            $"The corpus sources were not found. Nothing set \"{ConfigurationKey}\", so " +
            $"\"{DevelopmentDefault}\" was tried against the content root {contentRootPath}, and " +
            $"then every directory above it was searched for a \"{FolderName}\" folder holding {Marker}. Set " +
            $"\"{ConfigurationKey}\" to this checkout's {FolderName} directory, as an absolute path " +
            "if this is not a development run.");
    }

    private static bool IsCorpus(string folder) => System.IO.File.Exists(Path.Combine(folder, Marker));

    /// <summary>
    /// One source file, with an error that says where it was looked for rather than only that it
    /// was not found — the path is assembled from configuration, so the wrong answer is usually a
    /// wrong setting rather than a missing file.
    /// </summary>
    public static string File(string resourcesPath, params string[] segments)
    {
        var path = Path.GetFullPath(Path.Combine([resourcesPath, .. segments]));
        if (!System.IO.File.Exists(path))
        {
            throw new FileNotFoundException(
                $"The corpus source \"{Path.Combine(segments)}\" is not at {path}. The corpus is not " +
                $"in the repository: run the fetch script for it under scripts/, or point " +
                $"\"{ConfigurationKey}\" at a Resources directory that already has it.",
                path);
        }

        return path;
    }
}
