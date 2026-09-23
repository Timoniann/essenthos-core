namespace Essenthos.Core.Desk;

/// <summary>
/// Where the console reads and writes. The review lists and the record files are tracked, so they
/// are the checkout's own; the pictures are corpus bytes, so they are wherever the corpus is.
/// </summary>
/// <param name="Repository">The essenthos-core checkout whose tracked files a decision changes.</param>
/// <param name="Resources">The corpus folder, which holds the pictures.</param>
/// <param name="Workspace">The avioniq control folder, where every avioniq command is run from.</param>
internal sealed record DeskPaths(string Repository, string Resources, string Workspace)
{
    public const string RepositoryKey = "Desk:Repository";

    public const string ResourcesKey = "Dataset:ResourcesPath";

    public const string WorkspaceKey = "Desk:Workspace";

    /// <summary>What marks the checkout when walking up from the assembly.</summary>
    private const string RepositoryMarker = "Essenthos.Core.sln";

    /// <summary>What marks the control folder when walking up from the checkout.</summary>
    private const string WorkspaceMarker = ".avioniq";

    /// <summary>The owner's own tracked files: the review lists, his picture choices and the change log.</summary>
    public string Owner => Path.Combine(Repository, "Resources", "Essenthos");

    public string Review => Path.Combine(Owner, "review");

    /// <summary>The site's switches, beside the API that serves them.</summary>
    public string SiteSettings => Path.Combine(Repository, "Essenthos.Api", Configuration.SiteSettings.FileName);

    public string Records => Path.Combine(Repository, "Essenthos.Forge", "Loading", "Encyclopedia");

    public string Images => Path.Combine(Resources, "Images");

    public string Generated => Path.Combine(Images, "generated");

    public static DeskPaths Read(IConfiguration configuration)
    {
        var repository = Configured(configuration, RepositoryKey, AppContext.BaseDirectory)
                         ?? Above(AppContext.BaseDirectory, RepositoryMarker, directory: false)
                         ?? throw new DirectoryNotFoundException(
                             $"No essenthos-core checkout above {AppContext.BaseDirectory}. Run the console from its " +
                             $"checkout, or set {RepositoryKey} to the folder holding {RepositoryMarker}.");

        var resources = Configured(configuration, ResourcesKey, repository) ?? Path.Combine(repository, "Resources");

        var workspace = Configured(configuration, WorkspaceKey, repository)
                        ?? Above(repository, WorkspaceMarker, directory: true)
                        ?? throw new DirectoryNotFoundException(
                            $"No avioniq control folder above {repository}. Set {WorkspaceKey} to the folder " +
                            $"holding {WorkspaceMarker}.");

        return new DeskPaths(repository, resources, workspace);
    }

    private static string? Configured(IConfiguration configuration, string key, string relativeTo)
    {
        var value = configuration[key];
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var path = Path.GetFullPath(Path.Combine(relativeTo, value));
        return Directory.Exists(path)
            ? path
            : throw new DirectoryNotFoundException($"{key} is set to {path}, which does not exist. Correct it or remove it.");
    }

    /// <summary>The nearest folder at or above <paramref name="start"/> holding <paramref name="marker"/>.</summary>
    private static string? Above(string start, string marker, bool directory)
    {
        for (var folder = new DirectoryInfo(start); folder is not null; folder = folder.Parent)
        {
            var candidate = Path.Combine(folder.FullName, marker);
            if (directory ? Directory.Exists(candidate) : File.Exists(candidate))
            {
                return folder.FullName;
            }
        }

        return null;
    }

    /// <summary>
    /// A path under <paramref name="root"/> named by a relative path from a request or a file, or
    /// null where it would leave the folder.
    /// </summary>
    public static string? Under(string root, string relative)
    {
        var full = Path.GetFullPath(Path.Combine(root, relative));
        var within = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return full.StartsWith(within, StringComparison.OrdinalIgnoreCase) ? full : null;
    }
}
