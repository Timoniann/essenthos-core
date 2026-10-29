using System.Collections.Concurrent;
using Essenthos.Core.Database;
using Essenthos.Core.Publishing;
using Essenthos.Core.Verification;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting.Internal;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Essenthos.Core.Tests.Platform;

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

    /// <summary>What the publishers made here said, one line each.</summary>
    private readonly ConcurrentQueue<string> _said = new();

    private string Said => string.Join("\n", _said);

    private Publisher Publisher(bool withAddress = true, Dictionary<string, string?>? more = null)
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

        foreach (var (key, value) in more ?? [])
        {
            settings[key] = value;
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
            new Saying(_said));
    }

    /// <summary>Production as the server is set up: one copy, gated on the rehearsal on this machine.</summary>
    private static Dictionary<string, string?> OneCopy() => new()
    {
        ["Publish:Targets:prod:Copies"] = "1",
        ["Publish:Targets:prod:RestoreJobs"] = "2",
        ["Publish:Targets:prod:After"] = "rehearsal",
        ["Publish:Targets:rehearsal:Local"] = "true",
        ["Publish:Targets:rehearsal:DataRoot"] = "rehearsal-data",
        ["Publish:Targets:rehearsal:Container"] = "essenthos-rehearsal-db-1",
        ["Publish:Targets:rehearsal:Database"] = "corpus",
        ["Publish:Targets:rehearsal:Reader"] = "essenthos_reader",
        ["Publish:Targets:rehearsal:DatabasePort"] = "5438",
        ["Publish:Targets:rehearsal:ApiContainer"] = "essenthos-rehearsal-api-1",
        ["Publish:Targets:rehearsal:Copies"] = "1",
    };

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

    private void Published(string name, string target, string at) =>
        File.AppendAllText(Path.Combine(Releases, $"{name}.published"), $"{target}\t{at}\n");

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

    [Fact]
    public async Task ADryRunToAOneCopyTargetSaysTheSiteShowsItsMaintenancePageWhileTheCorpusIsReplaced()
    {
        Release("20260920a", "rehearsal");

        (await Publisher(more: OneCopy()).Publish("prod", null, false, CancellationToken.None, dryRun: true)).Should().Be(0);

        Said.Should().Contain("which keeps 1 corpus copy")
            .And.Contain($"put the maintenance page up ({DataRoot}/maintenance/on) and stop essenthos-api-1")
            .And.Contain("drop corpus, and any corpus_incoming, corpus_previous or corpus_retired")
            .And.Contain("restore it into corpus_incoming in essenthos-db-1 with 2 jobs")
            .And.Contain("rename corpus_incoming to corpus, start essenthos-api-1")
            .And.Contain("the page stays up and essenthos-api-1 stopped until a publication succeeds");
        Said.IndexOf("upload it", StringComparison.Ordinal).Should().BeLessThan(
            Said.IndexOf("maintenance page up", StringComparison.Ordinal), "the release travels while the site still serves");
        Directory.Exists(DataRoot).Should().BeFalse("nothing was uploaded, and no maintenance page went up");
    }

    [Fact]
    public async Task ProductionRefusesWhatTheRehearsalHasNotAcceptedAndSaysHowToRunIt()
    {
        Release("20260920a");

        (await Publisher(more: OneCopy()).Publish("prod", null, false, CancellationToken.None, dryRun: true)).Should().Be(1);

        Said.Should().Contain("has not been published to rehearsal")
            .And.Contain("docker compose --env-file .env.rehearsal -f compose.yaml -f compose.rehearsal.yaml up -d")
            .And.Contain("forge publish --to rehearsal --release 20260920a");
        (await Publisher(more: OneCopy()).Publish("prod", null, true, CancellationToken.None, dryRun: true)).Should().Be(0,
            "--without-rehearsal still overrides it");
    }

    [Fact]
    public async Task RollingBackAOneCopyTargetPublishesTheReleaseItServedBeforeAgain()
    {
        Release("20260920a");
        Release("20260921a");
        Published("20260920a", "prod", "2026-09-20T10:00:00Z");
        Published("20260921a", "prod", "2026-09-21T10:00:00Z");

        (await Publisher(more: OneCopy()).Rollback("prod", CancellationToken.None, dryRun: true)).Should().Be(0,
            "a release production has served needs no rehearsal to be served again");

        Said.Should().Contain("forge publish --to prod --release 20260920a")
            .And.Contain("publishing release 20260920a")
            .And.Contain("drop corpus");
        File.ReadAllLines(Path.Combine(Releases, "20260920a.published")).Should().HaveCount(1, "a dry run publishes nowhere");
    }

    [Fact]
    public async Task ARollbackWithNothingEarlierToPublishSaysWhatToRunInstead()
    {
        Release("20260921a");
        Published("20260921a", "prod", "2026-09-21T10:00:00Z");

        (await Publisher(more: OneCopy()).Rollback("prod", CancellationToken.None, dryRun: true)).Should().Be(1);

        Said.Should().Contain("keeps no previous corpus").And.Contain("forge publish --to prod --release <name>");
    }

    [Fact]
    public async Task ATwoCopyTargetDropsWhatItReplaces()
    {
        Release("20260920a", "dev");

        (await Publisher(more: new() { ["Publish:Targets:prod:Copies"] = "2" })
            .Publish("prod", null, false, CancellationToken.None, dryRun: true)).Should().Be(0);

        Said.Should().Contain("drop any corpus_previous left by an earlier publication")
            .And.Contain("rename corpus_incoming to corpus and corpus to corpus_previous, start essenthos-api-1, and drop corpus_previous")
            .And.NotContain("maintenance");
    }

    [Theory]
    [InlineData("0")]
    [InlineData("4")]
    [InlineData("two")]
    public async Task CopiesIsOneTwoOrThree(string copies)
    {
        Release("20260920a", "dev");

        var publish = () => Publisher(more: new() { ["Publish:Targets:prod:Copies"] = copies })
            .Publish("prod", null, false, CancellationToken.None, dryRun: true);

        await publish.Should().ThrowAsync<InvalidOperationException>().WithMessage("*Copies*takes 1*");
    }

    [Fact]
    public void ProductionKeepsOneCopyAndIsGatedOnTheRehearsalOfTheServerOnThisMachine()
    {
        var checkout = TestResources.Checkout();
        var forge = new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(checkout, "Essenthos.Forge", "appsettings.json"))
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Publish:Targets:prod:Ssh"] = "deploy@192.0.2.1" })
            .Build();
        var prod = ReleaseTarget.Read(forge, "prod", checkout);
        var rehearsal = ReleaseTarget.Read(forge, "rehearsal", checkout);

        (prod.Copies, prod.After, prod.RestoreJobs).Should().Be((1, "rehearsal", 2),
            "the server's Postgres has 2 GB, and four index builds beside its buffers can exhaust it after the live copy is gone");
        (rehearsal.Local, rehearsal.Copies, rehearsal.After, rehearsal.RestoreJobs).Should().Be((true, 1, (string?)null, 2),
            "the rehearsal is the server's stack on this machine, replaced the same way and needing nothing before it");
        (rehearsal.Database, rehearsal.Reader, rehearsal.AppDatabase).Should().Be((prod.Database, prod.Reader, prod.AppDatabase));
    }

    private sealed class Saying(ConcurrentQueue<string> lines) : ILogger<Publisher>
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) => lines.Enqueue(formatter(state, exception));
    }
}
