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
    /// This project's own folder, relative to the content root — which is the project folder, so
    /// the repository root is one level up.
    /// </summary>
    private const string DevelopmentDefault = "../Resources";

    /// <summary>
    /// The default is relative to the content root, which is the project folder under
    /// <c>dotnet run</c> and the application folder anywhere else — so outside development it
    /// resolves somewhere that does not exist, and this says so with the path it tried rather than
    /// letting the load fail later against a directory nobody meant.
    /// </summary>
    public static string Read(IConfiguration configuration, string contentRootPath)
    {
        var configured = configuration[ConfigurationKey];
        var path = string.IsNullOrWhiteSpace(configured) ? DevelopmentDefault : configured;
        var resolved = Path.GetFullPath(Path.Combine(contentRootPath, path));

        if (!Directory.Exists(resolved))
        {
            throw new DirectoryNotFoundException(
                $"The corpus sources are not at {resolved}. " +
                (string.IsNullOrWhiteSpace(configured)
                    ? $"Nothing set \"{ConfigurationKey}\", so the development default " +
                      $"\"{DevelopmentDefault}\" was resolved against the content root " +
                      $"{contentRootPath} — which is the project folder under `dotnet run` and " +
                      "the application folder anywhere else. "
                    : $"\"{ConfigurationKey}\" is set to \"{configured}\". ") +
                "Point it at this checkout's Resources directory, as an absolute path if this is " +
                "not a development run.");
        }

        return resolved;
    }

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
