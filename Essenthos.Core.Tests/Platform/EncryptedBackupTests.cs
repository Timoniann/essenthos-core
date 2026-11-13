using System.Diagnostics;
using System.Security.Cryptography;
using Essenthos.Core.Accounts;
using Essenthos.Core.Configuration;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

namespace Essenthos.Core.Tests.Platform;

public sealed class EncryptedBackupTests
{
    private const string Image = "postgres:18.1@sha256:5773fe724c49c42a7a9ca70202e11e1dff21fb7235b335a73f39297d200b73a2";

    [BackupRehearsalFact]
    [Trait("Category", "Deployment")]
    public async Task AFailedBackupReportsFailureKeepsItsLastGoodDumpsAndPublishesNothing()
    {
        var root = Environment.GetEnvironmentVariable("ESSENTHOS_REHEARSAL_ROOT")!;
        var folder = Path.Combine(Path.GetTempPath(), $"essenthos-backup-failure-{Guid.NewGuid():N}");
        Directory.CreateDirectory(folder);
        try
        {
            // The last good dump of the database that is failing, three weeks old, and one of a
            // database no longer backed up at all, as old.
            var held = Path.Combine(folder, "unreachable-20260901T0300Z.dump.gpg");
            var gone = Path.Combine(folder, "retired-20260901T0300Z.dump.gpg");
            foreach (var old in new[] { held, gone })
            {
                await File.WriteAllTextAsync(old, "old");
                File.SetLastWriteTimeUtc(old, DateTime.UtcNow.AddDays(-21));
            }

            string[] unreachable = ["run", "--rm", "--network", "none", "--entrypoint", "sh", "-e", "PGHOST=127.0.0.1", "-e", "PGPORT=1", "-e", "PGUSER=rehearsal", "-e", "PGCONNECT_TIMEOUT=3", "-e", "BACKUP_DATABASES=unreachable", "-e", "BACKUP_KEEP_DAYS=14", "-e", "BACKUP_HOUR_UTC=3", "--mount", $"type=bind,source={folder},target=/backups", "--mount", $"type=bind,source={Path.Combine(root, "deploy", "backup.sh")},target=/backup.sh,readonly", Image];
            var (code, _, error) = await Run([.. unreachable, "/backup.sh", "--once"]);
            error.Should().Contain("FAILED");
            code.Should().NotBe(0, "a scheduler or operator must detect a failed backup");
            Directory.GetFiles(folder).Select(Path.GetFileName).Should().BeEquivalentTo(
                [Path.GetFileName(held)],
                "a database whose run failed keeps its last good dump past the window, and nothing new is published");

            // The daemon: a failed run is reported and the daemon carries on to its next run rather
            // than either exiting or treating the run as fine.
            var (daemon, _, said) = await Run([.. unreachable, "-c", "timeout 15 sh /backup.sh; echo exit=$?"]);
            daemon.Should().Be(0);
            said.Should().Contain("FAILED").And.Contain("this run failed");

            // And the healthcheck has nothing to go on but a database that never succeeded.
            var (check, _, why) = await Run([.. unreachable, "/backup.sh", "--check"]);
            check.Should().NotBe(0);
            why.Should().Contain("never been backed up");
        }
        finally
        {
            if (!Path.GetFullPath(folder).StartsWith(Path.GetTempPath(), StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Invalid cleanup target.");
            Directory.Delete(folder, recursive: true);
        }
    }

    [BackupRehearsalFact]
    [Trait("Category", "Deployment")]
    public async Task ThePullCopiesOnlyEncryptedBackupsCheckedAgainstTheServerAndKeepsWhatTheConsoleKeeps()
    {
        var root = Environment.GetEnvironmentVariable("ESSENTHOS_REHEARSAL_ROOT")!;
        var folder = Path.Combine(Path.GetTempPath(), $"essenthos-backup-pull-{Guid.NewGuid():N}");
        var server = Directory.CreateDirectory(Path.Combine(folder, "server")).FullName;
        var copies = Path.Combine(folder, "copies");
        try
        {
            string Name(string database, int daysAgo) => $"{database}-{DateTime.UtcNow.AddDays(-daysAgo):yyyyMMdd'T'HHmm'Z'}.dump.gpg";
            string Serve(string name, byte[] bytes) { File.WriteAllBytes(Path.Combine(server, name), bytes); return name; }
            byte[] Sealed(int seed) => [0x85, 0x02, 0x0c, 0x03, .. Enumerable.Range(0, 4096).Select(i => (byte)(i * seed))];
            var daily = Enumerable.Range(0, 6).Select(day => Serve(Name("essenthos_app", day), Sealed(day + 1))).ToList();
            var outOfWindow = Serve(Name("essenthos_app", 20), Sealed(40));
            var onlyOld = Serve(Name("essenthos_umami", 20), Sealed(50));
            Serve("essenthos_app-20260101T0300Z.dump", "PGDMP in the clear"u8.ToArray());
            Serve("notes.txt", "not a backup"u8.ToArray());

            var (code, said, error) = await Pull(root, server, copies, "-Keep", "3");
            code.Should().Be(0, said + error);
            Directory.GetFiles(copies).Select(Path.GetFileName).Should().BeEquivalentTo(
                [daily[0], daily[1], daily[2], onlyOld],
                "the newest three of a database inside the window, and the newest of each whatever its age; never a plain dump");
            foreach (var name in Directory.GetFiles(copies).Select(Path.GetFileName))
                File.ReadAllBytes(Path.Combine(copies, name!)).Should().Equal(File.ReadAllBytes(Path.Combine(server, name!)));
            (said + error).Should().Contain("in the clear");

            (await Pull(root, server, copies, "-Keep", "3")).Output.Should().Contain("0 copied", "a copy already here and equal to the server's is not fetched again");

            // A copy damaged here, and one a stopped run left half written, are fetched whole again.
            var damaged = Path.Combine(copies, daily[0]);
            var bytes = File.ReadAllBytes(damaged);
            bytes[^1] ^= 0xff;
            File.WriteAllBytes(damaged, bytes);
            File.Delete(Path.Combine(copies, daily[1]));
            File.WriteAllBytes(Path.Combine(copies, daily[1] + ".partial"), Sealed(2)[..100]);
            var abandoned = Path.Combine(copies, "essenthos_app-20260102T0300Z.dump.gpg.partial");
            File.WriteAllBytes(abandoned, [1]);
            File.SetLastWriteTimeUtc(abandoned, DateTime.UtcNow.AddHours(-2));
            // And a copy the server has let go of, past the window, ages out here too.
            File.WriteAllBytes(Path.Combine(copies, Name("essenthos_app", 30)), Sealed(60));
            (code, said, error) = await Pull(root, server, copies, "-Keep", "3");
            code.Should().Be(0, said + error);
            said.Should().Contain("2 copied");
            File.ReadAllBytes(damaged).Should().Equal(File.ReadAllBytes(Path.Combine(server, daily[0])));
            Directory.GetFiles(copies).Select(Path.GetFileName).Should().BeEquivalentTo([daily[0], daily[1], daily[2], onlyOld]);
            File.Exists(Path.Combine(copies, Name("essenthos_app", 30))).Should().BeFalse();
            File.Exists(Path.Combine(copies, outOfWindow)).Should().BeFalse();

            // A dump in the clear under the encrypted name is refused, and the run says it failed.
            var impostor = Serve(Name("essenthos_stats", 0), "PGDMP pretending"u8.ToArray());
            (code, _, error) = await Pull(root, server, copies, "-Keep", "3");
            code.Should().NotBe(0);
            error.Should().Contain(impostor);
            File.Exists(Path.Combine(copies, impostor)).Should().BeFalse();
            Directory.GetFiles(copies, "*.partial").Should().BeEmpty();

            // A server that cannot be read changes nothing here.
            var before = Directory.GetFiles(copies).Order().ToList();
            (code, _, error) = await Pull(root, Path.Combine(folder, "missing"), copies, "-Keep", "1");
            code.Should().NotBe(0);
            error.Should().Contain("could not be listed");
            Directory.GetFiles(copies).Order().Should().Equal(before);
        }
        finally
        {
            if (!Path.GetFullPath(folder).StartsWith(Path.GetTempPath(), StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Invalid cleanup target.");
            Directory.Delete(folder, recursive: true);
        }
    }

    [BackupRehearsalFact]
    [Trait("Category", "Deployment")]
    public async Task EncryptedBackupRestoresAccountsKeysAndSessionBehavior()
    {
        var root = Environment.GetEnvironmentVariable("ESSENTHOS_REHEARSAL_ROOT")
            ?? throw new InvalidOperationException("Set ESSENTHOS_REHEARSAL_ROOT to the checkout being rehearsed.");
        var folder = Path.Combine(Path.GetTempPath(), $"essenthos-backup-{Guid.NewGuid():N}");
        var source = $"essenthos_backup_test_{Environment.ProcessId}";
        var restored = source + "_restored";
        var failed = source + "_failed";
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [DatabaseConnection.ConnectionStringKey] = $"{WitnessDatabase.TestServer};Database=postgres",
            })
            .AddUserSecrets(typeof(EncryptedBackupTests).Assembly)
            .AddEnvironmentVariables().Build();
        var connection = new NpgsqlConnectionStringBuilder(DatabaseConnection.Read(configuration));
        var publicKeys = Directory.CreateDirectory(Path.Combine(folder, "public")).FullName;
        var privateKeys = Directory.CreateDirectory(Path.Combine(folder, "private")).FullName;
        var backups = Directory.CreateDirectory(Path.Combine(folder, "backups")).FullName;
        var now = DateTimeOffset.UtcNow;
        var accountId = Guid.CreateVersion7();
        var sessionId = Guid.CreateVersion7();
        var token = SessionTokens.New();
        var expired = SessionTokens.New();
        var revoked = SessionTokens.New();
        var revokedId = Guid.CreateVersion7();
        string protectedCookie;
        try
        {
            await using (var db = Context(connection, source))
            {
                await db.Database.MigrateAsync();
                db.Accounts.Add(new Account { Id = accountId, DisplayName = "Recovery reader", Locale = "uk", CreatedAt = now });
                db.AccountEmails.Add(new AccountEmail { AccountId = accountId, Email = "recovery@example.invalid", CreatedAt = now });
                db.Credentials.Add(new Credential { AccountId = accountId, Provider = "google", Subject = "synthetic-recovery", Email = "recovery@example.invalid", CreatedAt = now, LastUsedAt = now });
                db.Sessions.AddRange(
                    new Session { Id = sessionId, AccountId = accountId, TokenHash = SessionTokens.Hash(token)!, CreatedAt = now, LastSeenAt = now, ExpiresAt = now.AddDays(10) },
                    new Session { Id = Guid.CreateVersion7(), AccountId = accountId, TokenHash = SessionTokens.Hash(expired)!, CreatedAt = now, LastSeenAt = now, ExpiresAt = now.AddDays(-1) },
                    new Session { Id = revokedId, AccountId = accountId, TokenHash = SessionTokens.Hash(revoked)!, CreatedAt = now, LastSeenAt = now, ExpiresAt = now.AddDays(10) });
                db.Bookmarks.Add(new Bookmark { Id = Guid.CreateVersion7(), AccountId = accountId, Text = "KJV", Book = 43, Chapter = 3, Verse = 16, EndChapter = 3, EndVerse = 16, Color = "amber", Comment = "Restored margin", CreatedAt = now });
                db.AccountPhotos.Add(new AccountPhoto { AccountId = accountId, Content = [1, 2, 3, 4], ContentType = "image/png", UpdatedAt = now });
                await db.SaveChangesAsync();
            }
            // Signed out before the backup: the session was valid, and revoking it deletes its row.
            await using (var db = Context(connection, source))
            {
                (await db.Sessions.CountAsync(s => s.Id == revokedId)).Should().Be(1);
                await db.Sessions.Where(s => s.Id == revokedId).ExecuteDeleteAsync();
            }
            using (var services = Services(Connection(connection, source)))
            {
                protectedCookie = services.GetRequiredService<IDataProtectionProvider>().CreateProtector("recovery-cookie").Protect("external-callback-state");
            }
            var original = await Fingerprint(connection, source);
            var password = new Dictionary<string, string> { ["PGPASSWORD"] = connection.Password ?? "" };
            List<string> Backup(params string[] extra) =>
            [
                "run", "--rm", "--entrypoint", "sh", "-e", "PGPASSWORD", "-e", "PGHOST=host.docker.internal", "-e", $"PGPORT={connection.Port}", "-e", $"PGUSER={connection.Username}", "-e", $"BACKUP_DATABASES={source}", "-e", "BACKUP_KEEP_DAYS=14", "-e", "BACKUP_HOUR_UTC=3", .. extra,
                "--mount", $"type=bind,source={backups},target=/backups", "--mount", $"type=bind,source={publicKeys},target=/backup-key,readonly", "--mount", $"type=bind,source={Path.Combine(root, "deploy", "backup.sh")},target=/backup.sh,readonly", Image, "/backup.sh", "--once",
            ];
            List<string> Check() =>
            [
                "run", "--rm", "--network", "none", "--entrypoint", "sh", "-e", $"BACKUP_DATABASES={source}",
                "--mount", $"type=bind,source={backups},target=/backups", "--mount", $"type=bind,source={Path.Combine(root, "deploy", "backup.sh")},target=/backup.sh,readonly", Image, "/backup.sh", "--check",
            ];

            // No key yet: the run fails and writes nothing, unless plain dumps are asked for by name.
            var (refused, _, refusal) = await Run(Backup(), password);
            refused.Should().NotBe(0, "a deployment without its key must not look like one that is backing up");
            refusal.Should().Contain("BACKUP_ALLOW_UNENCRYPTED").And.Contain("FAILED");
            Directory.GetFiles(backups).Should().BeEmpty("nothing is written in the clear without being asked");
            var (allowed, plainSaid, _) = await Run(Backup("-e", "BACKUP_ALLOW_UNENCRYPTED=yes"), password);
            allowed.Should().Be(0);
            plainSaid.Should().Contain("NOT encrypted");
            var plain = Directory.GetFiles(backups, "*.dump").Should().ContainSingle().Subject;

            // A throwaway key in a keyring of its own, made by the same gpg the owner's restore uses.
            await Gpg(privateKeys, "--batch", "--pinentry-mode", "loopback", "--passphrase", "", "--quick-generate-key", "Recovery rehearsal <recovery@example.invalid>", "default", "default", "1d");
            await File.WriteAllTextAsync(Path.Combine(publicKeys, "rehearsal.asc"), await Gpg(privateKeys, "--batch", "--armor", "--export"));
            var stale = Path.Combine(backups, source + "-stale.dump.gpg");
            await File.WriteAllTextAsync(stale, "stale");
            File.SetLastWriteTimeUtc(stale, DateTime.UtcNow.AddDays(-20));
            var output = await Docker(Backup(), password);
            output.Should().Contain("encrypted").And.NotContain("FAILED");
            File.Exists(stale).Should().BeFalse("a database whose run succeeded keeps nothing past the window");
            File.Exists(plain).Should().BeFalse();
            var encryptedFile = Directory.GetFiles(backups, "*.dump.gpg").Should().ContainSingle().Subject;
            Directory.GetFiles(backups, "*.partial").Should().BeEmpty();

            // The healthcheck: fine after a success, failing once that success is over 26 hours old.
            var lastSuccess = Path.Combine(backups, ".last-success-" + source);
            File.Exists(lastSuccess).Should().BeTrue();
            (await Run(Check())).Code.Should().Be(0);
            File.SetLastWriteTimeUtc(lastSuccess, DateTime.UtcNow.AddHours(-27));
            var (stalled, _, stalledSaid) = await Run(Check());
            stalled.Should().NotBe(0);
            stalledSaid.Should().Contain("more than 26 hours");
            File.SetLastWriteTimeUtc(lastSuccess, DateTime.UtcNow);
            var encryptedBytes = await File.ReadAllBytesAsync(encryptedFile);
            System.Text.Encoding.Latin1.GetString(encryptedBytes).Should().NotContain("recovery@example.invalid").And.NotContain("Restored margin");
            var offsite = Directory.CreateDirectory(Path.Combine(folder, "offsite")).FullName;
            var expiredCopy = Path.Combine(offsite, "expired.dump.gpg");
            await File.WriteAllTextAsync(expiredCopy, "expired copy");
            File.SetLastWriteTimeUtc(expiredCopy, DateTime.UtcNow.AddDays(-20));
            var neverCopy = Path.Combine(backups, "never-copy.dump");
            await File.WriteAllTextAsync(neverCopy, "plain backup must stay local");
            // A dump the backup service is holding past the window, because its database's runs fail.
            var heldHere = Path.Combine(backups, "failing-20260901T0300Z.dump.gpg");
            await File.WriteAllTextAsync(heldHere, "held");
            File.SetLastWriteTimeUtc(heldHere, DateTime.UtcNow.AddDays(-20));
            List<string> Offsite(string remote) =>
            [
                "run", "--rm", "--network", "none", "--entrypoint", "sh", "-e", $"BACKUP_OFFSITE={remote}", "-e", "BACKUP_KEEP_DAYS=14", "--mount", $"type=bind,source={backups},target=/backups,readonly", "--mount", $"type=bind,source={offsite},target=/offsite", "--mount", $"type=bind,source={Path.Combine(root, "deploy", "backup-offsite.sh")},target=/backup-offsite.sh,readonly", "rclone/rclone:1.68.2", "/backup-offsite.sh", "--once",
            ];
            var copied = await Docker(Offsite("/offsite"));
            copied.Should().Contain("copied").And.NotContain("FAILED");
            Directory.GetFiles(offsite).Select(Path.GetFileName).Should().BeEquivalentTo(
                [Path.GetFileName(encryptedFile), Path.GetFileName(heldHere)],
                "the expired copy whose dump is gone here goes, and the one held here stays");
            File.Delete(heldHere);
            var (unconfigured, _, unconfiguredSaid) = await Run(Offsite("nowhere:essenthos"));
            unconfigured.Should().NotBe(0, "a copy that went nowhere must not report success");
            unconfiguredSaid.Should().Contain("FAILED");
            (await File.ReadAllBytesAsync(Path.Combine(offsite, Path.GetFileName(encryptedFile)))).Should().Equal(encryptedBytes);
            // The workstation's copy, fetched as the owner's machine fetches it, and restored from there.
            var copies = Path.Combine(folder, "copies");
            var (pulled, pullSaid, pullError) = await Pull(root, backups, copies);
            pulled.Should().Be(0, pullSaid + pullError);
            Directory.GetFiles(copies).Select(Path.GetFileName).Should().BeEquivalentTo([Path.GetFileName(encryptedFile)], "only the encrypted dump leaves the server");
            var copy = Path.Combine(copies, Path.GetFileName(encryptedFile));
            (await File.ReadAllBytesAsync(copy)).Should().Equal(encryptedBytes);
            var container = await TestServerContainer(connection);
            var (restoredCode, restoreSaid, restoreError) = await RestoreScript(root, copy, restored, container, privateKeys);
            restoredCode.Should().Be(0, restoreSaid + restoreError);
            restoreSaid.Should().Contain("data-protection key(s), all equal to the dump");
            (await Fingerprint(connection, restored)).Should().Equal(original);
            (await Fingerprint(connection, source)).Should().Equal(original);

            // Nothing replaces a restored copy but a whole one: a torn file over it leaves it as it was,
            // and the corpus databases are refused before anything is decrypted.
            var torn = Path.Combine(folder, "torn.dump.gpg");
            await File.WriteAllBytesAsync(torn, encryptedBytes[..(encryptedBytes.Length / 2)]);
            (await RestoreScript(root, torn, restored, container, privateKeys, "-Replace")).Code.Should().NotBe(0);
            (await Fingerprint(connection, restored)).Should().Equal(original);
            (await RestoreScript(root, torn, restored, container, privateKeys)).Error.Should().Contain("already exists");
            (await RestoreScript(root, copy, "essenthos_core_seed", container, privateKeys)).Code.Should().NotBe(0);
            (await Exists(connection, restored + "_restoring")).Should().BeFalse("a failed candidate is dropped");
            using (var services = Services(Connection(connection, restored)))
            {
                services.GetRequiredService<IDataProtectionProvider>().CreateProtector("recovery-cookie").Unprotect(protectedCookie).Should().Be("external-callback-state");
            }
            await using (var db = Context(connection, restored))
            {
                var row = await db.Accounts.SingleAsync();
                row.DisplayName.Should().Be("Recovery reader");
                var oldRevision = row.Revision;
                row.About = "After recovery";
                await db.SaveChangesAsync();
                row.Revision.Should().BeGreaterThan(oldRevision);
                await db.Database.MigrateAsync();
            }
            await using (var server = await Server(Connection(connection, restored)))
            using (var http = new HttpClient(new HttpClientHandler { UseCookies = false }) { BaseAddress = new Uri(server.Urls.First()) })
            {
                async Task<int> Status(string value, bool bearer = false)
                {
                    using var request = new HttpRequestMessage(HttpMethod.Get, "/session");
                    if (bearer) request.Headers.Authorization = new("Bearer", value);
                    else request.Headers.Add("Cookie", $"{SessionTokens.CookieName}={value}");
                    using var response = await http.SendAsync(request);
                    return (int)response.StatusCode;
                }
                (await Status(token)).Should().Be(200);
                (await Status(token, true)).Should().Be(200);
                (await Status(expired)).Should().Be(401);
                (await Status(revoked)).Should().Be(401);
                await using var db = Context(connection, restored);
                await db.Sessions.Where(s => s.Id == sessionId).ExecuteDeleteAsync();
                (await Status(token)).Should().Be(401);
                await server.StopAsync();
            }
            var (brokenCode, _, brokenSaid) = await RestoreScript(root, torn, failed, container, privateKeys);
            brokenCode.Should().NotBe(0);
            brokenSaid.Should().Contain("Nothing was restored");
            (await Exists(connection, failed)).Should().BeFalse();
            (await Exists(connection, failed + "_restoring")).Should().BeFalse();
            (await Fingerprint(connection, source)).Should().Equal(original);
            Console.WriteLine("Recovery proof: pulled copy, encrypted stream through the restore script, all table fingerprints, keys, cookie/bearer sessions, expiration/revocation, revision sequence, migration, retention and failed-candidate preservation passed.");
        }
        finally
        {
            NpgsqlConnection.ClearAllPools();
            foreach (var name in new[] { source, restored, failed, restored + "_restoring", failed + "_restoring" })
            {
                await using var db = Context(connection, name);
                await db.Database.EnsureDeletedAsync();
            }
            if (File.Exists(GpgProgram))
                await Run([Path.Combine(Path.GetDirectoryName(GpgProgram)!, "gpgconf.exe"), "--homedir", privateKeys, "--kill", "all"], program: true);
            if (!Path.GetFullPath(folder).StartsWith(Path.GetTempPath(), StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Cleanup must remain inside the owned temporary directory.");
            Directory.Delete(folder, recursive: true);
        }
    }

    private static async Task<(int Code, string Output, string Error)> Run(
        IEnumerable<string> args, Dictionary<string, string>? environment = null, bool program = false)
    {
        using var process = Process.Start(program ? Program(args) : Info(args, environment))!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        await Task.WhenAll(process.WaitForExitAsync(), output, error);
        return (process.ExitCode, await output, await error);
    }

    private static AccountsDbContext Context(NpgsqlConnectionStringBuilder connection, string database) =>
        new(new DbContextOptionsBuilder<AccountsDbContext>().UseNpgsql(Connection(connection, database)).Options);

    private static string Connection(NpgsqlConnectionStringBuilder connection, string database) =>
        new NpgsqlConnectionStringBuilder(connection.ConnectionString) { Database = database, Pooling = false, Timeout = 15, CommandTimeout = 30 }.ConnectionString;

    private static ServiceProvider Services(string connection)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAccounts(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Accounts:ConnectionString"] = connection }).Build());
        return services.BuildServiceProvider();
    }

    private static async Task<WebApplication> Server(string connection)
    {
        var builder = WebApplication.CreateSlimBuilder(["--urls=http://127.0.0.1:0"]);
        builder.Services.AddAccounts(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Accounts:ConnectionString"] = connection }).Build());
        var app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapGet("/session", (HttpContext context) => Results.Ok(context.User.AccountId())).RequireAuthorization();
        await app.StartAsync();
        return app;
    }

    private static async Task<List<string>> Fingerprint(NpgsqlConnectionStringBuilder connection, string name)
    {
        await using var db = new NpgsqlConnection(Connection(connection, name));
        await db.OpenAsync();
        await using var command = new NpgsqlCommand("SELECT tablename FROM pg_tables WHERE schemaname='public' ORDER BY tablename", db);
        var tables = new List<string>();
        await using (var reader = await command.ExecuteReaderAsync())
            while (await reader.ReadAsync()) tables.Add(reader.GetString(0));
        var result = new List<string>();
        foreach (var table in tables)
        {
            await using var rows = new NpgsqlCommand($"SELECT row_to_json(x)::text FROM \"{table}\" x ORDER BY row_to_json(x)::text", db);
            await using var reader = await rows.ExecuteReaderAsync();
            var values = new List<string>();
            while (await reader.ReadAsync()) values.Add(reader.GetString(0));
            result.Add(table + ":" + Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(string.Join('\n', values)))));
        }
        return result;
    }

    private static readonly string GpgProgram = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "GnuPG", "bin", "gpg.exe");

    private static readonly string GitSh = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Git", "usr", "bin", "sh.exe");

    private static async Task<string> Gpg(string home, params string[] args)
    {
        if (!File.Exists(GpgProgram)) throw new InvalidOperationException($"The rehearsal decrypts with the gpg the owner's restore uses, {GpgProgram}; install Gpg4win.");
        var (code, output, error) = await Run([GpgProgram, "--homedir", home, .. args], program: true);
        if (code != 0) throw new InvalidOperationException($"gpg exited {code}: {error}");
        return output;
    }

    /// <summary>The pull script against a folder standing in for the server: each ssh command run by Git's sh.</summary>
    private static async Task<(int Code, string Output, string Error)> Pull(string root, string server, string copies, params string[] extra)
    {
        var shim = Path.Combine(Path.GetDirectoryName(copies)!, "stand-in-ssh.sh");
        await File.WriteAllTextAsync(shim, "while [ \"$1\" = -o ]; do shift 2; done\nshift\nPATH=/usr/bin:$PATH exec sh -c \"$1\"\n");
        static string Quoted(string value) => "'" + value.Replace("'", "''") + "'";
        var command = $"& {Quoted(Path.Combine(root, "scripts", "pull-account-backups.ps1"))} -Server 'deploy@stand-in' -Remote {Quoted(Posix(server))} " +
                      $"-Folder {Quoted(copies)} -Ssh {Quoted(GitSh)},{Quoted(shim.Replace('\\', '/'))} {string.Join(' ', extra)}; exit $LASTEXITCODE";
        return await Run(["pwsh", "-NoProfile", "-NonInteractive", "-Command", command], program: true);
    }

    private static Task<(int Code, string Output, string Error)> RestoreScript(string root, string file, string into, string container, string keys, params string[] extra) =>
        Run(["pwsh", "-NoProfile", "-NonInteractive", "-File", Path.Combine(root, "scripts", "restore-account-backup.ps1"), "-File", file, "-Into", into, "-Container", container, "-GnuPGHome", keys, .. extra], program: true);

    /// <summary>A Windows path as Git's sh names it: C:\Users becomes /c/Users.</summary>
    private static string Posix(string path) => "/" + char.ToLowerInvariant(path[0]) + path[2..].Replace('\\', '/');

    private static async Task<string> TestServerContainer(NpgsqlConnectionStringBuilder connection)
    {
        var names = (await Docker(["ps", "--filter", $"publish={connection.Port}", "--format", "{{.Names}}"])).Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return names.Should().ContainSingle($"one container publishes the test database's port {connection.Port}").Subject;
    }

    private static async Task<bool> Exists(NpgsqlConnectionStringBuilder connection, string name)
    {
        await using var db = new NpgsqlConnection(Connection(connection, "postgres"));
        await db.OpenAsync();
        await using var command = new NpgsqlCommand("SELECT count(*) FROM pg_database WHERE datname = @name", db);
        command.Parameters.AddWithValue("name", name);
        return (long)(await command.ExecuteScalarAsync())! > 0;
    }

    private static ProcessStartInfo Info(IEnumerable<string> args, Dictionary<string, string>? environment = null, bool input = false)
    {
        var info = new ProcessStartInfo("docker") { RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = input, UseShellExecute = false, CreateNoWindow = true };
        foreach (var arg in args) info.ArgumentList.Add(arg);
        if (environment is not null) foreach (var (key, value) in environment) info.Environment[key] = value;
        return info;
    }

    /// <summary>The program the first argument names, rather than docker.</summary>
    private static ProcessStartInfo Program(IEnumerable<string> args)
    {
        var list = args.ToList();
        var info = new ProcessStartInfo(list[0]) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
        foreach (var arg in list.Skip(1)) info.ArgumentList.Add(arg);
        return info;
    }

    private static async Task<string> Docker(IEnumerable<string> args, Dictionary<string, string>? environment = null, bool requireSuccess = true)
    {
        using var process = Process.Start(Info(args, environment))!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        await Task.WhenAll(process.WaitForExitAsync(), output, error);
        if (requireSuccess && process.ExitCode != 0) throw new InvalidOperationException($"Recovery helper exited {process.ExitCode}: {await error}");
        return await output;
    }
}

public sealed class BackupRehearsalFactAttribute : FactAttribute
{
    public BackupRehearsalFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("ESSENTHOS_BACKUP_REHEARSAL") != "1")
            Skip = "Run the registered encrypted-backup rehearsal action; it uses isolated databases and an ephemeral private key.";
    }
}
