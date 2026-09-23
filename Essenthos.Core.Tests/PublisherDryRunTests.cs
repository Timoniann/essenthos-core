using Essenthos.Core.Database;
using Essenthos.Core.Publishing;
using Essenthos.Core.Verification;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting.Internal;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Essenthos.Core.Tests;

/// <summary>
/// <c>forge publish --dry-run</c> and <c>forge rollback --dry-run</c>, which the owner's console runs to
/// show what a publication would do: the same refusals as the real thing, and nothing touched — no
/// upload, no database, not even a connection.
/// </summary>
public sealed class PublisherDryRunTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("dry-run-").FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private string Releases => Path.Combine(_root, "releases");

    private string DataRoot => Path.Combine(_root, "data");

    private Publisher Publisher(bool withAddress = true)
    {
        Directory.CreateDirectory(Path.Combine(_root, "Resources", "Images", "openbible"));
        File.WriteAllText(Path.Combine(_root, "Resources", "Images", "openbible", "a.jpg"), "a");
        var settings = new Dictionary<string, string?>
        {
            ["Dataset:ResourcesPath"] = Path.Combine(_root, "Resources"),
            ["Publish:ReleasesPath"] = Releases,
            ["Publish:Targets:dev:DataRoot"] = DataRoot,
            ["Publish:Targets:dev:Container"] = "essenthos-db-1",
            ["Publish:Targets:dev:Database"] = "corpus_dev",
            ["Publish:Targets:dev:Reader"] = "essenthos_reader_dev",
            ["Publish:Targets:dev:DatabasePort"] = "5432",
            ["Publish:Targets:dev:ApiContainer"] = "essenthos-api-dev-1",
            ["Publish:Targets:prod:DataRoot"] = DataRoot,
            ["Publish:Targets:prod:Container"] = "essenthos-db-1",
            ["Publish:Targets:prod:Database"] = "corpus",
            ["Publish:Targets:prod:Reader"] = "essenthos_reader",
            ["Publish:Targets:prod:DatabasePort"] = "5432",
            ["Publish:Targets:prod:ApiContainer"] = "essenthos-api-1",
            ["Publish:Targets:prod:After"] = "dev",
        };
        if (withAddress)
        {
            // A documentation address: nothing answers there, which is the point.
            settings["Publish:Targets:dev:Ssh"] = "deploy@192.0.2.1";
            settings["Publish:Targets:prod:Ssh"] = "deploy@192.0.2.1";
        }

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=192.0.2.1;Port=1;Database=never;Username=never;Timeout=1").Options);
        return new Publisher(
            db,
            new CorpusCheck(db, NullLogger<CorpusCheck>.Instance),
            configuration,
            new HostingEnvironment { ContentRootPath = _root },
            NullLoggerFactory.Instance,
            NullLogger<Publisher>.Instance);
    }

    private void Release(string name, params string[] publishedTo)
    {
        Directory.CreateDirectory(Releases);
        File.WriteAllText(Path.Combine(Releases, $"{name}.dump"), "0123456789");
        File.WriteAllText(Path.Combine(Releases, $"{name}.json"),
            $$"""
            { "Name": "{{name}}", "BuiltAt": "2026-09-20T10:00:00+00:00", "ForgeVersion": "1.0.0+abc", "ManifestSha": "m",
              "MigrationHead": "h", "Sha256": "not checked in a dry run", "Bytes": 10, "Rendered": 0.9, "Broken": 0 }
            """);
        if (publishedTo.Length > 0)
        {
            File.WriteAllLines(Path.Combine(Releases, $"{name}.published"), publishedTo.Select(t => $"{t}\t2026-09-21T10:00:00Z"));
        }
    }

    [Fact]
    public async Task ADryRunPublicationSaysYesAndTouchesNothing()
    {
        Release("20260920a");

        (await Publisher().Publish("dev", null, false, CancellationToken.None, dryRun: true)).Should().Be(0);

        Directory.Exists(DataRoot).Should().BeFalse("nothing was uploaded or unpacked");
        File.Exists(Path.Combine(Releases, "20260920a.published")).Should().BeFalse("a dry run publishes nowhere");
    }

    [Fact]
    public async Task ADryRunToProductionRefusesWhatDevHasNotAccepted()
    {
        Release("20260920a");
        (await Publisher().Publish("prod", null, false, CancellationToken.None, dryRun: true)).Should().Be(1);

        Release("20260920a", "dev");
        (await Publisher().Publish("prod", null, false, CancellationToken.None, dryRun: true)).Should().Be(0);
    }

    [Fact]
    public async Task ADryRunRefusesADumpThatIsNotTheOneRecorded()
    {
        Release("20260920a");
        File.WriteAllText(Path.Combine(Releases, "20260920a.dump"), "short");

        (await Publisher().Publish("dev", null, false, CancellationToken.None, dryRun: true)).Should().Be(1);
    }

    [Fact]
    public async Task ADryRunStillWantsTheServersAddress()
    {
        Release("20260920a");

        var publish = () => Publisher(withAddress: false).Publish("dev", null, false, CancellationToken.None, dryRun: true);
        var rollback = () => Publisher(withAddress: false).Rollback("dev", CancellationToken.None, dryRun: true);

        await publish.Should().ThrowAsync<InvalidOperationException>().WithMessage("*has no address*");
        await rollback.Should().ThrowAsync<InvalidOperationException>().WithMessage("*has no address*");
        (await Publisher().Rollback("dev", CancellationToken.None, dryRun: true)).Should().Be(0);
    }
}
