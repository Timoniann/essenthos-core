namespace Essenthos.Core.Configuration;

/// <summary>
/// Where the corpus sources live. They are 373 MB and belong to essenthos-api, so this project
/// reads them where they are rather than carrying a copy. The path resolves against the content
/// root, not the working directory, so it holds wherever the process was started from.
/// </summary>
internal static class ResourcePaths
{
    public const string ConfigurationKey = "Dataset:ResourcesPath";

    /// <summary>
    /// The sibling checkout, relative to this project's content root — which is the project folder,
    /// so the workspace is two levels up rather than one.
    /// </summary>
    private const string DevelopmentDefault = "../../essenthos-api/Resources";

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
                "Point it at the Resources directory of the essenthos-api checkout, as an absolute path if this is " +
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
                $"The corpus source \"{Path.Combine(segments)}\" is not at {path}. It lives in the " +
                $"essenthos-api checkout and is not copied here; point \"{ConfigurationKey}\" at that " +
                $"folder's Resources directory.",
                path);
        }

        return path;
    }
}
