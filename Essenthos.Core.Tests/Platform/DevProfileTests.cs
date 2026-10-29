using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

namespace Essenthos.Core.Tests.Platform;

/// <summary>
/// The production server runs no dev: its containers start only under the compose profile "dev", and the
/// proxy serves dev's hostnames only when DEV_SITES says so. Checked on the text of the files; the files
/// themselves are checked with `docker compose config` and `caddy adapt` (deploy/README.md).
/// </summary>
public sealed class DevProfileTests
{
    private static string Deploy(string file) => File.ReadAllText(Path.Combine(Checkout(), "deploy", file)).ReplaceLineEndings("\n");

    private static string Service(string compose, string name) =>
        Regex.Match(compose, $@"^  {name}:\n(?<body>[\s\S]*?)(?=^  [a-z-]+:\n|^volumes:)", RegexOptions.Multiline).Groups["body"].Value;

    [Fact]
    public void DevsContainersStartOnlyUnderTheirProfileAndNothingElseWaitsForThem()
    {
        var compose = Deploy("compose.yaml");

        foreach (var service in new[] { "api-dev-migrate", "api-dev", "web-dev" })
        {
            Service(compose, service).Should().Contain("profiles: [\"dev\"]", $"{service} is dev's");
        }

        foreach (var service in new[] { "db", "api-migrate", "api", "web", "proxy", "backup" })
        {
            Service(compose, service).Should().NotContain("profiles:", $"{service} runs on every server");
            Service(compose, service).Should().NotContainAny(["- api-dev", "- web-dev", "api-dev-migrate:"],
                "a service every server runs cannot wait for one that only dev's profile starts");
        }

        compose.Should().NotContainAny(["${DEV_DOMAIN:?", "${DEV_API_DOMAIN:?", "${DEV_USER:?", "${DEV_PASSWORD_HASH:?",
            "${READER_DEV_PASSWORD:?", "${APP_DEV_PASSWORD:?"], "a production-only .env has no dev values");
        Service(compose, "proxy").Should().Contain("DEV_SITES: ${DEV_SITES:-off}");
    }

    [Fact]
    public void TheProxyServesDevsHostnamesOnlyWhenSwitchedOn()
    {
        var caddyfile = Deploy("Caddyfile");

        caddyfile.Should().Contain("\nimport dev-sites-{$DEV_SITES:off}\n").And.Contain("(dev-sites-off) {\n}");
        var on = caddyfile[caddyfile.IndexOf("(dev-sites-on) {", StringComparison.Ordinal)..];
        on.Should().Contain("\t{$DEV_DOMAIN} {").And.Contain("\t{$DEV_API_DOMAIN} {");
        Regex.Matches(caddyfile, @"^\{\$DEV_", RegexOptions.Multiline).Should().BeEmpty("no dev site is outside the switch");
    }

    [Fact]
    public void DevsDatabaseRolesAreMadeOnlyWhereDevsPasswordsAre()
    {
        var roles = Deploy(Path.Combine("initdb", "10-roles.sh"));
        var guard = roles.IndexOf("if [ -n \"${READER_DEV_PASSWORD:-}\" ] && [ -n \"${APP_DEV_PASSWORD:-}\" ]", StringComparison.Ordinal);

        guard.Should().BeGreaterThan(-1);
        roles.IndexOf("CREATE ROLE essenthos_reader_dev", StringComparison.Ordinal).Should().BeGreaterThan(guard);
        roles.IndexOf("CREATE DATABASE essenthos_app_dev", StringComparison.Ordinal).Should().BeGreaterThan(guard);
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
