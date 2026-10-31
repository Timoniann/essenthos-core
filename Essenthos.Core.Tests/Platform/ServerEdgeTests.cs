using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

namespace Essenthos.Core.Tests.Platform;

/// <summary>
/// The server answers on its bare address and on its domain as .env names them, takes its memory budget
/// from .env, and shows the updating page while the switch file exists. Checked on the text of the files;
/// the files themselves are checked with `docker compose config`, `caddy adapt` and the rehearsal stack
/// (deploy/README.md).
/// </summary>
public sealed class ServerEdgeTests
{
    private static string Deploy(string file) => File.ReadAllText(Path.Combine(Checkout(), "deploy", file)).ReplaceLineEndings("\n");

    private static string Service(string compose, string name) =>
        Regex.Match(compose, $@"^  {name}:\n(?<body>[\s\S]*?)(?=^  [a-z-]+:\n|^volumes:)", RegexOptions.Multiline).Groups["body"].Value;

    [Fact]
    public void EachAddressIsServedOnlyWhereEnvNamesIt()
    {
        var caddyfile = Deploy("Caddyfile");
        var proxy = Service(Deploy("compose.yaml"), "proxy");

        proxy.Should().Contain("IP_SITE: ${SITE_IP:+on}").And.Contain("DOMAIN_SITES: ${SITE_DOMAIN:+on}");
        caddyfile.Should().Contain("\nimport ip-site-{$IP_SITE}\n").And.Contain("(ip-site-) {\n}")
            .And.Contain("\nimport domain-sites-{$DOMAIN_SITES}\n").And.Contain("(domain-sites-) {\n}");
        Regex.Matches(caddyfile, @"^(\{\$[A-Z_]+\}|http://)", RegexOptions.Multiline).Should().BeEmpty("no site is outside a switch");
    }

    [Fact]
    public void TheBareAddressOffersNoSignInAndAsksNotToBeIndexed()
    {
        var caddyfile = Deploy("Caddyfile");
        var site = caddyfile[caddyfile.IndexOf("(ip-site-on) {", StringComparison.Ordinal)..caddyfile.IndexOf("(ip-site-) {", StringComparison.Ordinal)];

        site.Should().Contain("\thttp://{$SITE_IP} {").And.Contain("import unlisted")
            .And.Contain("handle /v1/auth/providers {").And.Contain("respond `{\"providers\":[]}` 200");
    }

    [Fact]
    public void EverySiteShowsTheUpdatingPageBeforeAnythingElseWhileTheSwitchFileExists()
    {
        var caddyfile = Deploy("Caddyfile");
        var site = caddyfile[caddyfile.IndexOf("(site) {", StringComparison.Ordinal)..];
        site = site[..site.IndexOf("\n}\n", StringComparison.Ordinal)];

        site.Should().Contain("root /srv/maintenance").And.Contain("try_files /on").And.Contain("invoke @updating updating");
        caddyfile.Should().Contain("&(updating) {").And.Contain("HTML 503").And.Contain("Retry-After 300");
        Regex.Matches(caddyfile, @"^\t\timport site ", RegexOptions.Multiline).Count.Should().Be(
            Regex.Matches(caddyfile, @"^\t(\{\$[A-Z_]+\}|http://\{\$[A-Z_]+\}) \{", RegexOptions.Multiline).Count, "every site imports (site)");
        Service(Deploy("compose.yaml"), "proxy").Should().Contain("${HOST_DATA_ROOT}/maintenance:/srv/maintenance:ro");
    }

    [Fact]
    public void OnlyTheProxyIsOnTheIpv6NetworkAndItKeepsTheAddressTheApiBelieves()
    {
        var compose = Deploy("compose.yaml");
        var proxy = Service(compose, "proxy");
        var networks = compose[compose.IndexOf("\nnetworks:\n", StringComparison.Ordinal)..];

        proxy.Should().Contain("ipv4_address: 10.89.0.2").And.Contain("edge: {}");
        networks.Should().Contain("enable_ipv4: false").And.Contain("enable_ipv6: true");
        Regex.Matches(compose, @"^      edge:", RegexOptions.Multiline).Count.Should().Be(1, "the API must not reach the proxy over IPv6");
        Service(compose, "api").Should().Contain("ForwardedHeaders__KnownProxies: 10.89.0.2");
    }

    [Fact]
    public void TheMemoryBudgetIsEnvsWithTheEightGigabyteMachineAsTheDefault()
    {
        var compose = Deploy("compose.yaml");
        var db = Service(compose, "db");

        db.Should().Contain("mem_limit: ${DB_MEM_LIMIT:-4g}").And.Contain("shared_buffers=${DB_SHARED_BUFFERS:-2GB}")
            .And.Contain("max_parallel_workers_per_gather=${DB_PARALLEL_WORKERS:-2}").And.Contain("shm_size: ${DB_SHM_SIZE:-1gb}");
        compose.Should().Contain("mem_limit: ${API_MEM_LIMIT:-1g}").And.Contain("mem_limit: ${WEB_MEM_LIMIT:-128m}");

        var example = Deploy("env.example").Split('\n');
        example.Should().Contain(["DB_MEM_LIMIT=2g", "DB_PARALLEL_WORKERS=1", "API_MEM_LIMIT=768m", "SITE_DOMAIN="],
            "the example is the 4 GB server's, with no domain yet");
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
