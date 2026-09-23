using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using Essenthos.Core.Configuration;
using Essenthos.Core.Corpus;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Loading.Encyclopedia;
using Essenthos.Core.Verification;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Essenthos.Core.Publishing;

/// <summary>
/// The corpus as an artefact: built here, dumped once, and restored whole on a server.
///
/// A release is a <c>pg_dump</c> of this machine's corpus with a <see cref="CorpusRelease"/> row inside
/// it. Publishing restores it into a database nothing reads — <c>&lt;name&gt;_incoming</c> — runs the
/// same gate <c>forge verify</c> runs, against that copy, on that machine, and only then renames it
/// into place. The previous release stays beside it as the rollback, which is a rename and a restart.
///
/// Row-level patches were ruled out by measurement rather than taste: every rebuild renumbers every
/// sequence key, so a diff between two builds is most of the corpus, and the whole corpus is a 376 MB
/// file that dumps in 37 seconds and restores in under a minute.
///
/// Nothing here builds anything on a target, opens a writable corpus connection as the API's role,
/// or dumps a second time for production: production is only offered a release its <c>After</c>
/// target has already accepted, byte for byte, which the SHA-256 in the release's record checks.
/// </summary>
internal sealed class Publisher(
    AppDbContext db,
    CorpusCheck check,
    IConfiguration configuration,
    IHostEnvironment environment,
    ILoggerFactory loggers,
    ILogger<Publisher> logger)
{
    private static readonly JsonSerializerOptions RecordJson = new() { WriteIndented = true };

    private string Releases => configuration["Publish:ReleasesPath"] is { Length: > 0 } configured
        ? configured
        : Path.Combine(Repository, ".releases");

    /// <summary>The checkout root: the folder that holds Resources/.</summary>
    private string Repository => Path.GetDirectoryName(ResourcePaths.Read(configuration, environment.ContentRootPath))!;

    private string SourceContainer => configuration["Publish:SourceContainer"] is { Length: > 0 } container
        ? container
        : "essenthos-core-db-1";

    /// <summary>
    /// Verifies this machine's corpus, labels it, dumps it, and removes the label again, so the label
    /// travels in the dump and the working database stays a working database.
    /// </summary>
    /// <param name="dryRun">Check what can be checked without measuring or dumping, say what would follow, and change nothing.</param>
    public async Task<int> Release(bool allowDirty, CancellationToken cancellationToken, bool dryRun = false)
    {
        var version = ForgeVersion();
        if (version.EndsWith("-dirty", StringComparison.Ordinal) && !allowDirty)
        {
            logger.LogError(
                "This Forge was built from a tree with uncommitted changes ({Version}), so the release could " +
                "not name the code that made it. Commit first, or pass --allow-dirty and accept the label " +
                "saying so", version);
            return 1;
        }

        if ((await db.Database.GetPendingMigrationsAsync(cancellationToken)).ToList() is { Count: > 0 } pending)
        {
            logger.LogError(
                "The corpus is missing {Count} migrations ({First} first). A release carries its schema, so it " +
                "would carry the wrong one", pending.Count, pending[0]);
            return 1;
        }

        if (dryRun)
        {
            logger.LogInformation(
                "Dry run: would measure the corpus against the gate (about two minutes), then dump {Database} as release " +
                "{Name} into {Folder} with the label of {Version}. Nothing was measured, labelled or dumped",
                new NpgsqlConnectionStringBuilder(db.Database.GetConnectionString()).Database,
                Directory.Exists(Releases) ? NextName() : $"{DateTimeOffset.UtcNow:yyyyMMdd}a", Releases, version);
            return 0;
        }

        logger.LogInformation("Measuring the corpus before anything is dumped");
        db.Database.SetCommandTimeout(TimeSpan.FromMinutes(30));
        var measures = await check.Measure(cancellationToken);
        if (!CorpusGate.Pass(measures, CorpusCheck.RenderedFloor, logger))
        {
            logger.LogError("Nothing was released: a corpus that fails the gate here fails it everywhere");
            return 1;
        }

        // Stored, so the dump carries the measurement it was released on and the server's /v1/health
        // reports it without measuring anything.
        await check.Record(measures, cancellationToken);

        Directory.CreateDirectory(Releases);
        var name = NextName();
        var connection = new NpgsqlConnectionStringBuilder(db.Database.GetConnectionString());

        var release = new CorpusRelease
        {
            Name = name,
            BuiltAt = DateTimeOffset.UtcNow,
            ManifestSha = ManifestSha(),
            MigrationHead = (await db.Database.GetAppliedMigrationsAsync(cancellationToken)).Last(),
            ForgeVersion = version,
            OriginalWords = await db.Words.CountAsync(w => w.Text!.Kind != TextKind.Translation, cancellationToken),
            Translations = await db.Texts.CountAsync(t => t.Kind == TextKind.Translation, cancellationToken),
            StrongEntries = await db.StrongEntries.CountAsync(cancellationToken),
            People = await db.Entities.CountAsync(e => e.Kind == EntityKind.Person, cancellationToken),
            Places = await db.Entities.CountAsync(e => e.Kind == EntityKind.Place, cancellationToken),
            Peoples = await db.Entities.CountAsync(e => e.Kind == EntityKind.People, cancellationToken),
        };

        var dump = Path.Combine(Releases, $"{name}.dump");
        var inContainer = $"/tmp/{name}.dump";
        db.CorpusReleases.Add(release);
        await db.SaveChangesAsync(cancellationToken);
        try
        {
            logger.LogInformation("Dumping {Database} as release {Name}", connection.Database, name);
            var started = DateTimeOffset.UtcNow;
            await Shell.Run("docker",
                ["exec", SourceContainer, "pg_dump", "-U", connection.Username!, "-Fc", "-Z6",
                 "-f", inContainer, connection.Database!], cancellationToken);
            await Shell.Run("docker", ["cp", $"{SourceContainer}:{inContainer}", dump], cancellationToken);
            logger.LogInformation("Dumped in {Seconds:F0} s", (DateTimeOffset.UtcNow - started).TotalSeconds);
        }
        finally
        {
            await db.CorpusReleases.Where(r => r.Id == release.Id).ExecuteDeleteAsync(CancellationToken.None);
            await Shell.Run("docker", ["exec", SourceContainer, "rm", "-f", inContainer], CancellationToken.None);
        }

        var record = new ReleaseRecord(
            name, release.BuiltAt, release.ForgeVersion, release.ManifestSha, release.MigrationHead,
            await Sha256(dump, cancellationToken), new FileInfo(dump).Length,
            measures.Rendered, measures.Broken);
        await File.WriteAllTextAsync(
            Path.Combine(Releases, $"{name}.json"), JsonSerializer.Serialize(record, RecordJson), cancellationToken);

        logger.LogInformation(
            "Release {Name}: {Bytes:N0} bytes, sha256 {Sha}, from {Version}. Publish it with `forge publish --to <target>`",
            name, record.Bytes, record.Sha256[..16], version);
        return 0;
    }

    /// <param name="dryRun">
    /// Read the target and the release, apply the same refusals a publication would, say every step that
    /// would follow, and touch nothing — no ssh, no upload, no database.
    /// </param>
    public async Task<int> Publish(
        string targetName, string? releaseName, bool withoutRehearsal, CancellationToken cancellationToken, bool dryRun = false)
    {
        var target = ReleaseTarget.Read(configuration, targetName, Repository);
        var host = new TargetHost(target);
        var record = ReadRecord(releaseName);
        var dump = Path.Combine(Releases, $"{record.Name}.dump");

        if (dryRun)
        {
            return DescribePublication(target, record, dump, withoutRehearsal);
        }

        if (await Sha256(dump, cancellationToken) != record.Sha256)
        {
            logger.LogError("{Dump} is not the file release {Name} recorded. Nothing was published", dump, record.Name);
            return 1;
        }

        if (target.After is { } after && !PublishedTo(record.Name).Contains(after))
        {
            if (!withoutRehearsal)
            {
                logger.LogError(
                    "Release {Name} has not been published to {After}, and {Target} only takes releases {After} " +
                    "has accepted. Publish it there first, or pass --without-rehearsal and own that",
                    record.Name, after, target.Name, after);
                return 1;
            }

            logger.LogWarning("Publishing {Name} to {Target} without it having been to {After}", record.Name, target.Name, after);
        }

        var started = DateTimeOffset.UtcNow;
        var inContainer = $"/releases/{record.Name}.dump";

        // Resumable: an artefact already there with the right hash is not sent twice.
        if (await RemoteSha(host, inContainer, cancellationToken) != record.Sha256)
        {
            logger.LogInformation("Uploading {Bytes:N0} bytes to {Target}", record.Bytes, target.Name);
            await host.Upload(dump, cancellationToken);
            if (await RemoteSha(host, inContainer, cancellationToken) is var arrived && arrived != record.Sha256)
            {
                logger.LogError("The upload arrived as {Arrived}, not {Expected}. Nothing was restored", arrived, record.Sha256);
                return 1;
            }
        }

        var pictures = await SendImages(host, cancellationToken);

        if (await host.Sql("postgres", $"SELECT 1 FROM pg_roles WHERE rolname = {TargetHost.Literal(target.Reader)}", cancellationToken) != "1")
        {
            logger.LogError("{Target} has no role {Reader}; the server's database was not initialised", target.Name, target.Reader);
            return 1;
        }

        var incoming = TargetHost.Identifier(target.Incoming);
        logger.LogInformation("Restoring into {Incoming}, which nothing reads", target.Incoming);
        await host.Sql("postgres", $"DROP DATABASE IF EXISTS {incoming} WITH (FORCE)", cancellationToken);
        await host.Sql("postgres", $"CREATE DATABASE {incoming}", cancellationToken);
        await host.Docker(
            ["exec", target.Container, "pg_restore", "-U", target.Superuser, "-d", target.Incoming,
             "-j", "4", "--no-owner", "--no-privileges", "--exit-on-error", inContainer],
            cancellationToken);

        // A restore carries no planner statistics, and without them the first reader after the swap
        // gets the plans of an empty database.
        await host.Sql(target.Incoming, "VACUUM ANALYZE", cancellationToken);
        logger.LogInformation("Restored in {Seconds:F0} s", (DateTimeOffset.UtcNow - started).TotalSeconds);

        var restored = await host.Sql(target.Incoming, "SELECT name FROM corpus_release", cancellationToken);
        if (restored != record.Name)
        {
            logger.LogError("{Incoming} says it is release \"{Restored}\", not {Name}. Nothing was swapped",
                target.Incoming, restored, record.Name);
            return 1;
        }

        if (!await PicturesArePresent(host, pictures, cancellationToken))
        {
            return 1;
        }

        // The gate, on the target, against the copy about to be served. A failure stops here and the
        // live corpus was never touched.
        logger.LogInformation("Verifying {Incoming} on {Target}", target.Incoming, target.Name);
        var (connectionString, lease) = await host.Connect(target.Incoming, cancellationToken);
        await using (lease)
        {
            await using var restoredDb = new AppDbContext(
                new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(connectionString).Options);
            var measures = await new CorpusCheck(restoredDb, loggers.CreateLogger<CorpusCheck>()).Measure(cancellationToken);
            if (!CorpusGate.Pass(measures, CorpusCheck.RenderedFloor, logger))
            {
                logger.LogError("{Incoming} failed the gate on {Target}; the live corpus is untouched", target.Incoming, target.Name);
                return 1;
            }

            if (Math.Abs(measures.Rendered - record.Rendered) > 1e-9)
            {
                logger.LogError(
                    "{Incoming} measures {Rendered:P3} where the release measured {Expected:P3} when it was made. " +
                    "The same bytes should measure the same; nothing was swapped",
                    target.Incoming, measures.Rendered, record.Rendered);
                return 1;
            }
        }

        // Readers' bookmarks are addressed canonically and cannot move, but a release can lose the verse
        // under one. That is somebody's mark losing its place, so it stops the publication.
        if (await BookmarkAnchors.Unresolved(host, target, cancellationToken) is { } lost)
        {
            if (lost.Count > 0)
            {
                foreach (var point in lost.Take(20))
                {
                    logger.LogError("No verse at {Book} {Chapter}:{Verse}{Text} in {Name}, and a reader's bookmark is anchored there",
                        BookReferences.Name(point.Book), point.Chapter, point.Verse,
                        point.Text.Length == 0 ? "" : $" in {point.Text}", record.Name);
                }

                logger.LogError("{Count} bookmark addresses do not resolve in {Name}; the live corpus is untouched", lost.Count, record.Name);
                return 1;
            }

            logger.LogInformation("Every bookmark in {App} finds its verse in {Name}", target.AppDatabase, record.Name);
        }

        await Grant(host, cancellationToken);
        await WithApiStopped(host, () => Swap(host, cancellationToken), cancellationToken);

        await File.AppendAllTextAsync(
            Path.Combine(Releases, $"{record.Name}.published"),
            $"{target.Name}\t{DateTimeOffset.UtcNow:O}\n", cancellationToken);

        logger.LogInformation(
            "{Target} serves release {Name}; the one before it is {Previous}, and `forge rollback --to {Target}` " +
            "puts it back. {Seconds:F0} s in all",
            target.Name, record.Name, target.Previous, target.Name, (DateTimeOffset.UtcNow - started).TotalSeconds);
        return 0;
    }

    private int DescribePublication(ReleaseTarget target, ReleaseRecord record, string dump, bool withoutRehearsal)
    {
        if (!File.Exists(dump) || new FileInfo(dump).Length != record.Bytes)
        {
            logger.LogError("Dry run: {Dump} is missing or is not the size release {Name} recorded", dump, record.Name);
            return 1;
        }

        if (target.After is { } after && !PublishedTo(record.Name).Contains(after) && !withoutRehearsal)
        {
            logger.LogError("Dry run: release {Name} has not been published to {After}, and {Target} only takes releases {After} has accepted",
                record.Name, after, target.Name, after);
            return 1;
        }

        var host = new TargetHost(target);
        var pictures = Path.Combine(ResourcePaths.Read(configuration, environment.ContentRootPath), EntityImageLoader.Folder);
        var count = Directory.Exists(pictures) ? Directory.EnumerateFiles(pictures, "*", SearchOption.AllDirectories).Count() : 0;
        var where = target.IsRemote ? target.Ssh : "this machine";
        logger.LogInformation("Dry run: publishing release {Name} ({Bytes:N0} bytes, built {BuiltAt:u}) to {Target} on {Where} would", record.Name, record.Bytes, record.BuiltAt, target.Name, where);
        logger.LogInformation("  1. check the dump's SHA-256 against its record, and upload it unless {Target} already has it", target.Name);
        logger.LogInformation("  2. send whichever of the {Count:N0} pictures here {Target} lacks or has with other bytes, into {Folder}", count, target.Name, host.ImagesFolder);
        logger.LogInformation("  3. restore it into {Incoming} in {Container}, which nothing reads, and check its label", target.Incoming, target.Container);
        logger.LogInformation("  4. check every picture it names is on {Target}, run the gate against it there, and check every bookmark finds its verse", target.Name);
        logger.LogInformation("  5. grant {Reader} reading and nothing else, stop {Api}, rename {Incoming} to {Database} and {Database} to {Previous}, start {Api}",
            target.Reader, target.ApiContainer, target.Incoming, target.Database, target.Database, target.Previous, target.ApiContainer);
        logger.LogInformation("Nothing was uploaded, restored or swapped");
        return 0;
    }

    /// <summary>
    /// This machine's pictures, sent where the target's API reads them: only those the target lacks or
    /// has with other bytes, and nothing taken away. Returns what the target has afterwards.
    /// </summary>
    private async Task<Dictionary<string, string>> SendImages(TargetHost host, CancellationToken cancellationToken)
    {
        var source = Path.Combine(ResourcePaths.Read(configuration, environment.ContentRootPath), EntityImageLoader.Folder);
        var here = ImageManifest.Read(source);
        var there = await host.Images(cancellationToken);
        var send = ImageManifest.ToSend(here, there);
        if (send.Count == 0)
        {
            logger.LogInformation("The {Count:N0} pictures in {Folder} on {Target} are current", there.Count, host.ImagesFolder, host.Target.Name);
            return there;
        }

        var bytes = send.Sum(file => new FileInfo(Path.Combine(source, file)).Length);
        logger.LogInformation("Sending {Count:N0} pictures, {Bytes:N0} bytes, to {Folder} on {Target}",
            send.Count, bytes, host.ImagesFolder, host.Target.Name);
        await host.SendImages(source, send, cancellationToken);
        foreach (var file in send)
        {
            there[file] = here[file];
        }

        return there;
    }

    /// <summary>
    /// Whether every picture the incoming release names is where the target's API will look for it. A
    /// missing one stops the publication, because its page would show the credit over a broken
    /// picture; one whose bytes changed since the release was made is served as it now is, and said.
    /// </summary>
    private async Task<bool> PicturesArePresent(
        TargetHost host, IReadOnlyDictionary<string, string> there, CancellationToken cancellationToken)
    {
        var target = host.Target;
        if (await host.Sql(target.Incoming, "SELECT to_regclass('public.entity_image') IS NOT NULL", cancellationToken) != "t")
        {
            return true;
        }

        var rows = await host.Sql(target.Incoming, "SELECT file, digest FROM entity_image", cancellationToken);
        var (missing, differing) = ImageManifest.Check(ImageManifest.Rows(rows), there);
        foreach (var file in differing.Take(20))
        {
            logger.LogWarning("{File} on {Target} is not the picture {Incoming} was made with; it is served as it now is",
                file, target.Name, target.Incoming);
        }

        if (missing.Count == 0)
        {
            return true;
        }

        foreach (var file in missing.Take(20))
        {
            logger.LogError("{Incoming} names the picture {File}, and neither this machine nor {Target} has it",
                target.Incoming, file, target.Name);
        }

        logger.LogError("{Count} pictures of {Incoming} are missing; the live corpus is untouched", missing.Count, target.Incoming);
        return false;
    }

    /// <summary>Exchanges the live corpus and the previous one, and restarts the API onto it.</summary>
    public async Task<int> Rollback(string targetName, CancellationToken cancellationToken, bool dryRun = false)
    {
        var target = ReleaseTarget.Read(configuration, targetName, Repository);
        if (dryRun)
        {
            logger.LogInformation(
                "Dry run: rolling back {Target} on {Where} would stop {Api}, exchange {Database} and {Previous}, and start {Api}. " +
                "Nothing was connected to or renamed",
                target.Name, target.IsRemote ? target.Ssh : "this machine", target.ApiContainer, target.Database, target.Previous, target.ApiContainer);
            return 0;
        }

        var host = new TargetHost(target);
        if (!await host.DatabaseExists(target.Previous, cancellationToken))
        {
            logger.LogError("{Target} has no {Previous} to roll back to", target.Name, target.Previous);
            return 1;
        }

        var swapping = TargetHost.Identifier($"{target.Database}_swapping");
        var live = TargetHost.Identifier(target.Database);
        var previous = TargetHost.Identifier(target.Previous);

        await WithApiStopped(host, async () =>
        {
            await Disconnect(host, target.Database, cancellationToken);
            await Disconnect(host, target.Previous, cancellationToken);
            await host.Sql("postgres",
                [$"ALTER DATABASE {live} RENAME TO {swapping}",
                 $"ALTER DATABASE {previous} RENAME TO {live}",
                 $"ALTER DATABASE {swapping} RENAME TO {previous}",
                 $"ALTER DATABASE {live} WITH ALLOW_CONNECTIONS true",
                 $"ALTER DATABASE {previous} WITH ALLOW_CONNECTIONS true"],
                cancellationToken);
        }, cancellationToken);

        logger.LogInformation("{Target} now serves {Live}; the one it replaced is {Previous}",
            target.Name, await Label(host, target.Database, cancellationToken), await Label(host, target.Previous, cancellationToken));
        return 0;
    }

    /// <summary>What this machine has built, and — with <c>--on</c> — what a target holds.</summary>
    public async Task<int> List(string? targetName, CancellationToken cancellationToken)
    {
        if (targetName is not null)
        {
            var target = ReleaseTarget.Read(configuration, targetName, Repository);
            var host = new TargetHost(target);
            foreach (var database in new[] { target.Database, target.Previous, target.Incoming })
            {
                logger.LogInformation("{Target} {Database,-24} {Label}", target.Name, database,
                    await host.DatabaseExists(database, cancellationToken)
                        ? await Label(host, database, cancellationToken)
                        : "absent");
            }

            return 0;
        }

        if (!Directory.Exists(Releases))
        {
            logger.LogInformation("Nothing released yet. `forge release` makes one in {Folder}", Releases);
            return 0;
        }

        foreach (var file in Directory.GetFiles(Releases, "*.json").Order(StringComparer.Ordinal))
        {
            var record = JsonSerializer.Deserialize<ReleaseRecord>(await File.ReadAllTextAsync(file, cancellationToken))!;
            var published = PublishedTo(record.Name);
            logger.LogInformation("{Name}  {BuiltAt:u}  {Version}  {Bytes,15:N0} bytes  {Published}",
                record.Name, record.BuiltAt, record.ForgeVersion, record.Bytes,
                published.Count == 0 ? "not published" : $"published to {string.Join(", ", published)}");
        }

        return 0;
    }

    /// <summary>
    /// The API's role reads the corpus and does nothing else, and nobody else connects to it at all.
    /// Granted on the incoming copy, before the swap, so the first request after it can read.
    /// </summary>
    private static async Task Grant(TargetHost host, CancellationToken cancellationToken)
    {
        var target = host.Target;
        var database = TargetHost.Identifier(target.Incoming);
        var reader = TargetHost.Identifier(target.Reader);
        await host.Sql("postgres", $"REVOKE ALL ON DATABASE {database} FROM PUBLIC", cancellationToken);
        await host.Sql("postgres", $"GRANT CONNECT ON DATABASE {database} TO {reader}", cancellationToken);
        await host.Sql(target.Incoming, $"GRANT USAGE ON SCHEMA public TO {reader}", cancellationToken);
        await host.Sql(target.Incoming, $"GRANT SELECT ON ALL TABLES IN SCHEMA public TO {reader}", cancellationToken);
    }

    /// <summary>
    /// incoming becomes live, live becomes previous, and the release before that is dropped — in that
    /// order, so that at every step there is a live corpus or one rename away from one, and the old
    /// previous is only dropped once its replacement exists.
    /// </summary>
    private async Task Swap(TargetHost host, CancellationToken cancellationToken)
    {
        var target = host.Target;
        var live = TargetHost.Identifier(target.Database);
        var previous = TargetHost.Identifier(target.Previous);
        var retired = TargetHost.Identifier(target.Retired);
        var incoming = TargetHost.Identifier(target.Incoming);

        await host.Sql("postgres", $"DROP DATABASE IF EXISTS {retired} WITH (FORCE)", cancellationToken);
        if (await host.DatabaseExists(target.Previous, cancellationToken))
        {
            await Disconnect(host, target.Previous, cancellationToken);
            await host.Sql("postgres", $"ALTER DATABASE {previous} RENAME TO {retired}", cancellationToken);
        }

        if (!await host.DatabaseExists(target.Database, cancellationToken))
        {
            await host.Sql("postgres", $"ALTER DATABASE {incoming} RENAME TO {live}", cancellationToken);
        }
        else
        {
            await Disconnect(host, target.Database, cancellationToken);
            try
            {
                // One psql, so the moment with no live corpus is two renames long rather than two
                // round trips through docker and ssh.
                await host.Sql("postgres",
                    [$"ALTER DATABASE {live} RENAME TO {previous}",
                     $"ALTER DATABASE {incoming} RENAME TO {live}",
                     $"ALTER DATABASE {previous} WITH ALLOW_CONNECTIONS true"],
                    cancellationToken);
            }
            catch
            {
                if (await host.DatabaseExists(target.Database, CancellationToken.None))
                {
                    throw;
                }

                logger.LogError("The incoming corpus could not be renamed into place; putting the live one back");
                await host.Sql("postgres",
                    [$"ALTER DATABASE {previous} RENAME TO {live}", $"ALTER DATABASE {live} WITH ALLOW_CONNECTIONS true"],
                    CancellationToken.None);
                throw;
            }
        }

        await host.Sql("postgres", $"DROP DATABASE IF EXISTS {retired} WITH (FORCE)", cancellationToken);
    }

    /// <summary>
    /// A database can only be renamed with nobody in it, and the API reconnects the instant it is
    /// thrown out, so connections are refused first and then ended.
    /// </summary>
    private static async Task Disconnect(TargetHost host, string database, CancellationToken cancellationToken)
    {
        await host.Sql("postgres", $"ALTER DATABASE {TargetHost.Identifier(database)} WITH ALLOW_CONNECTIONS false", cancellationToken);
        await host.Sql("postgres",
            $"SELECT count(pg_terminate_backend(pid)) FROM pg_stat_activity WHERE datname = {TargetHost.Literal(database)}",
            cancellationToken);
    }

    /// <summary>
    /// The API is stopped for the swap and started after it, rather than restarted afterwards.
    ///
    /// Measured: republishing under continuous traffic with a restart after the swap failed 4 requests
    /// in 2,454 — the ones that reached the API in the instant its corpus was renamed away. A stopped
    /// API refuses connections instead, and the proxy holds a refused request and retries it for up to
    /// twenty seconds, which is how the restart itself already cost nothing. The API also keeps some of
    /// the corpus in memory — the canonical frame among it — so it has to start again against the new
    /// release either way.
    ///
    /// Started in a finally: whatever happens to the swap, the API is not left stopped.
    /// </summary>
    private async Task WithApiStopped(TargetHost host, Func<Task> swap, CancellationToken cancellationToken)
    {
        var api = host.Target.ApiContainer;
        var stopped = false;
        try
        {
            await host.Docker(["stop", "--time", "15", api], cancellationToken, quiet: true);
            stopped = true;
        }
        catch (InvalidOperationException exception)
        {
            // A first publication happens before there is an API to point at it; that is not a
            // failure of the publication.
            logger.LogWarning("Could not stop {Api}: {Message}", api, exception.Message.Split('\n')[^1]);
        }

        var started = DateTimeOffset.UtcNow;
        try
        {
            await swap();
        }
        finally
        {
            if (stopped)
            {
                await host.Docker(["start", api], CancellationToken.None, quiet: true);
                logger.LogInformation("{Api} was stopped for {Seconds:F1} s", api, (DateTimeOffset.UtcNow - started).TotalSeconds);
            }
        }
    }

    private static async Task<string> Label(TargetHost host, string database, CancellationToken cancellationToken)
    {
        try
        {
            return await host.Sql(database,
                "SELECT name || '  built ' || to_char(built_at, 'YYYY-MM-DD HH24:MI') || '  ' || forge_version FROM corpus_release",
                cancellationToken) is { Length: > 0 } label ? label : "no release label (a working copy)";
        }
        catch (InvalidOperationException)
        {
            return "unreadable, or older than the release label";
        }
    }

    private static async Task<string> RemoteSha(TargetHost host, string path, CancellationToken cancellationToken)
    {
        try
        {
            var output = await host.Docker(["exec", host.Target.Container, "sha256sum", path], cancellationToken, quiet: true);
            return output.Split(' ', 2)[0];
        }
        catch (InvalidOperationException)
        {
            return string.Empty;
        }
    }

    private ReleaseRecord ReadRecord(string? name)
    {
        var file = name is not null
            ? Path.Combine(Releases, $"{name}.json")
            : Directory.Exists(Releases)
                ? Directory.GetFiles(Releases, "*.json").Order(StringComparer.Ordinal).LastOrDefault()
                : null;

        if (file is null || !File.Exists(file))
        {
            throw new InvalidOperationException(
                name is null ? $"No release in {Releases}. `forge release` makes one." : $"No release {name} in {Releases}.");
        }

        return JsonSerializer.Deserialize<ReleaseRecord>(File.ReadAllText(file))!;
    }

    private IReadOnlySet<string> PublishedTo(string name)
    {
        var ledger = Path.Combine(Releases, $"{name}.published");
        return File.Exists(ledger)
            ? File.ReadAllLines(ledger).Where(l => l.Length > 0).Select(l => l.Split('\t')[0]).ToHashSet()
            : new HashSet<string>();
    }

    private string NextName()
    {
        var day = DateTimeOffset.UtcNow.ToString("yyyyMMdd", System.Globalization.CultureInfo.InvariantCulture);
        for (var letter = 'a'; letter <= 'z'; letter++)
        {
            if (!File.Exists(Path.Combine(Releases, $"{day}{letter}.json")) &&
                !File.Exists(Path.Combine(Releases, $"{day}{letter}.dump")))
            {
                return $"{day}{letter}";
            }
        }

        throw new InvalidOperationException($"Twenty-six releases on {day} already.");
    }

    private string ManifestSha()
    {
        var manifest = Path.Combine(ResourcePaths.Read(configuration, environment.ContentRootPath), "MANIFEST.json");
        return Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(manifest)));
    }

    private static async Task<string> Sha256(string file, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(file);
        return Convert.ToHexStringLower(await SHA256.HashDataAsync(stream, cancellationToken));
    }

    private static string ForgeVersion() =>
        typeof(Publisher).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? "unknown";
}

/// <summary>
/// What <c>forge release</c> wrote, beside the dump: enough to check the file and to know what it is
/// without restoring it.
/// </summary>
internal sealed record ReleaseRecord(
    string Name,
    DateTimeOffset BuiltAt,
    string ForgeVersion,
    string ManifestSha,
    string MigrationHead,
    string Sha256,
    long Bytes,
    double Rendered,
    int Broken);
