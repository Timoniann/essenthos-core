using Essenthos.Core.Configuration;
using Microsoft.Extensions.Configuration;

namespace Essenthos.Core.Tests;

/// <summary>
/// The corpus sources, found the way the application finds them — through
/// <c>Dataset:ResourcesPath</c> resolved against the project's content root — so that a test which
/// cannot find a source file is telling you something true about the configuration.
/// </summary>
internal static class TestResources
{
    private const string SolutionFile = "Essenthos.Core.sln";
    private const string ProjectFolder = "Essenthos.Core";

    private static readonly Lazy<string> ResolvedPath = new(() =>
    {
        var configuration = new ConfigurationBuilder()
            .AddEnvironmentVariables()
            .Build();
        return ResourcePaths.Read(configuration, ContentRoot());
    });

    public static string Path(params string[] segments) => ResourcePaths.File(ResolvedPath.Value, segments);

    /// <summary>The Bibles, by the name each reader takes.</summary>
    public static string Nestle1904 => Path("Nestle1904", "Nestle1904.xml");

    public static string ZefaniaKingJames => Path("Zefania", "SF_2009-01-20_ENG_KJV_(KJV+).xml");

    public static string Bible4u(string translation) => Path("bible4u", $"{translation}.xml");

    public static string Tvtms => Path("Versification", "TVTMS.txt");

    /// <summary>Brenton's Septuagint, one USFM file per book.</summary>
    public static string SeptuagintFolder => System.IO.Path.Combine(ResolvedPath.Value, "Septuagint");

    /// <summary>Robinson's composite, one file per book.</summary>
    public static string TextusReceptus(string book) => Path("TextusReceptus", "parsed", $"{book}.UTR");

    /// <summary>The plain Scrivener text the extraction is checked against.</summary>
    public static string Scrivener(string book) => Path("TextusReceptus", "scrivener", $"{book}.SCV");

    public static string TextusReceptusFolder => System.IO.Path.Combine(ResolvedPath.Value, "TextusReceptus");

    public static string Etcbc => System.IO.Path.Combine(ResolvedPath.Value, "etcbc");

    /// <summary>The place layer's second source, one JSON object per line.</summary>
    public static string OpenBibleFolder => System.IO.Path.GetDirectoryName(Path("OpenBible", "ancient.jsonl"))!;

    /// <summary>
    /// The project folder, which is what the host uses as its content root. Walking up from the
    /// test assembly rather than counting <c>..</c> segments, because the depth of the output
    /// folder is the build configuration's business and changes without warning.
    /// </summary>
    private static string ContentRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(System.IO.Path.Combine(directory.FullName, SolutionFile)))
        {
            directory = directory.Parent;
        }

        if (directory is null)
        {
            throw new DirectoryNotFoundException(
                $"No {SolutionFile} above {AppContext.BaseDirectory}, so the content root cannot be found. " +
                "Run the tests from inside the essenthos-core checkout.");
        }

        return System.IO.Path.Combine(directory.FullName, ProjectFolder);
    }
}
