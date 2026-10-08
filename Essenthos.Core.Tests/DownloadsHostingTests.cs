using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

namespace Essenthos.Core.Tests;

/// <summary>
/// The server's half of the downloads, which lives in two files rather than in code: the proxy serves the
/// export read-only and never lists a folder, the API lists the same folder and links it on the site's own
/// address, and each environment reads its own. Checked on the text of the files, and rehearsed for real
/// with the rehearsal stack (deploy/README.md, "The downloads").
/// </summary>
public sealed class DownloadsHostingTests
{
    private static string Deploy(string file) => File.ReadAllText(Path.Combine(Checkout(), "deploy", file)).ReplaceLineEndings("\n");

    private static string Snippet(string caddyfile, string name)
    {
        var start = caddyfile.IndexOf($"({name}) {{", StringComparison.Ordinal);
        start.Should().BeGreaterThan(-1, $"the Caddyfile defines ({name})");
        var end = caddyfile.IndexOf("\n}", start, StringComparison.Ordinal);
        return caddyfile[start..end];
    }

    [Fact]
    public void TheProxyServesTheExportAtDownloadsWithoutListingAFolder()
    {
        var snippet = Snippet(Deploy("Caddyfile"), "downloads");

        snippet.Should().Contain("path /downloads/*").And.Contain("uri strip_prefix /downloads").And.Contain("file_server");
        snippet.Should().NotContain("browse", "a folder is never listed");
    }

    [Fact]
    public void AFileIsKeptForAYearBecauseItsNameNeverMeansOtherBytesAndTheManifestForMinutes()
    {
        var snippet = Snippet(Deploy("Caddyfile"), "downloads");

        snippet.Should().Contain(@"header @named Cache-Control ""public, max-age=31536000, immutable""");
        snippet.Should().Contain(@"header @manifest Cache-Control ""public, max-age=300""");
        snippet.Should().Contain("Content-Disposition `attachment; filename=\"{re.data.1}.jsonl.gz\"`")
            .And.Contain("Content-Disposition `attachment; filename=\"{re.credit.1}-ATTRIBUTION.txt\"`");
    }

    [Fact]
    public void EachSiteServesItsOwnEnvironmentsFolder()
    {
        var caddyfile = Deploy("Caddyfile");
        var blocks = Regex.Matches(caddyfile, @"^\{\$(?<site>[A-Z_]+)\} \{(?<body>[\s\S]*?)^\}", RegexOptions.Multiline)
            .ToDictionary(m => m.Groups["site"].Value, m => m.Groups["body"].Value);

        blocks["SITE_DOMAIN"].Should().Contain("import downloads /srv/downloads\n");
        blocks["API_DOMAIN"].Should().Contain("import downloads /srv/downloads\n");
        blocks["DEV_DOMAIN"].Should().Contain("import downloads /srv/downloads_dev\n");
        blocks["DEV_API_DOMAIN"].Should().Contain("import downloads /srv/downloads_dev\n");
        blocks["DEV_DOMAIN"].IndexOf("basic_auth", StringComparison.Ordinal).Should()
            .BeLessThan(blocks["DEV_DOMAIN"].IndexOf("import downloads", StringComparison.Ordinal), "dev's files are behind dev's password");
    }

    [Fact]
    public void TheApiAndTheProxyMountTheSameFolderReadOnlyAndTheApiLinksItOnTheSitesOwnAddress()
    {
        var compose = Deploy("compose.yaml").Replace("\r\n", "\n");
        string Service(string name) => Regex.Match(compose, $@"^  {name}:\n(?<body>[\s\S]*?)(?=^  [a-z-]+:\n|^volumes:)", RegexOptions.Multiline).Groups["body"].Value;

        foreach (var (service, folder) in new[] { ("api", "downloads"), ("api-dev", "downloads_dev") })
        {
            var body = Service(service);
            body.Should().Contain("Downloads__Folder: /downloads").And.Contain("Downloads__BaseUrl: /downloads");
            body.Should().Contain($"${{HOST_DATA_ROOT}}/{folder}:/downloads:ro");
        }

        var proxy = Service("proxy");
        proxy.Should().Contain("${HOST_DATA_ROOT}/downloads:/srv/downloads:ro")
            .And.Contain("${HOST_DATA_ROOT}/downloads_dev:/srv/downloads_dev:ro");
    }

    private static string Checkout()
    {
        for (var folder = new DirectoryInfo(AppContext.BaseDirectory); folder is not null; folder = folder.Parent)
        {
            if (File.Exists(Path.Combine(folder.FullName, "Essenthos.Core.sln")))
            {
                return folder.FullName;
            }
        }

        throw new DirectoryNotFoundException("The tests were not built inside an essenthos-core checkout.");
    }
}
