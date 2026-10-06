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

namespace Essenthos.Core.Tests;

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
                [DatabaseConnection.ConnectionStringKey] = "Host=localhost;Port=5437;Database=postgres;Username=essenthos",
            })
            .AddUserSecrets(typeof(EncryptedBackupTests).Assembly)
            .AddEnvironmentVariables().Build();
        var connection = new NpgsqlConnectionStringBuilder(DatabaseConnection.Read(configuration));
        var publicKeys = Directory.CreateDirectory(Path.Combine(folder, "public")).FullName;
        var privateKeys = Directory.CreateDirectory(Path.Combine(folder, "private")).FullName;
        var backups = Directory.CreateDirectory(Path.Combine(folder, "backups")).FullName;
        var client = "essenthos-backup-client-" + Guid.NewGuid().ToString("N");
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

            await Docker(["run", "--name", client, "--network", "none", "--mount", $"type=bind,source={privateKeys},target=/private", "--mount", $"type=bind,source={publicKeys},target=/public", "--entrypoint", "sh", Image, "-c",
                "set -eu; export GNUPGHOME=/private; chmod 700 /private; gpg --batch --pinentry-mode loopback --passphrase '' --quick-generate-key 'Recovery rehearsal <recovery@example.invalid>' default default 1d >/dev/null 2>&1; gpg --batch --armor --export > /public/rehearsal.asc"]);
            await Docker(["rm", client]);
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
            await Create(connection, restored);
            await Restore(encryptedFile, privateKeys, restored, connection, password);
            (await Fingerprint(connection, restored)).Should().Equal(original);
            (await Fingerprint(connection, source)).Should().Equal(original);
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
            await Create(connection, failed);
            var broken = Path.Combine(backups, "broken.dump.gpg");
            await File.WriteAllBytesAsync(broken, encryptedBytes[..(encryptedBytes.Length / 2)]);
            await Assert.ThrowsAnyAsync<Exception>(() => Restore(broken, privateKeys, failed, connection, password));
            (await Fingerprint(connection, source)).Should().Equal(original);
            Console.WriteLine("Recovery proof: encrypted stream, all table fingerprints, keys, cookie/bearer sessions, expiration/revocation, revision sequence, migration, retention and failed-candidate preservation passed.");
        }
        finally
        {
            NpgsqlConnection.ClearAllPools();
            foreach (var name in new[] { source, restored, failed })
            {
                await using var db = Context(connection, name);
                await db.Database.EnsureDeletedAsync();
            }
            await Docker(["rm", "-f", client], requireSuccess: false);
            if (!Path.GetFullPath(folder).StartsWith(Path.GetTempPath(), StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Cleanup must remain inside the owned temporary directory.");
            Directory.Delete(folder, recursive: true);
        }
    }

    private static async Task<(int Code, string Output, string Error)> Run(
        IEnumerable<string> args, Dictionary<string, string>? environment = null)
    {
        using var process = Process.Start(Info(args, environment))!;
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

    private static async Task Create(NpgsqlConnectionStringBuilder connection, string name)
    {
        await using var db = new NpgsqlConnection(Connection(connection, "postgres"));
        await db.OpenAsync();
        await using var command = new NpgsqlCommand($"CREATE DATABASE {name}", db);
        await command.ExecuteNonQueryAsync();
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

    private static async Task Restore(string file, string keys, string database, NpgsqlConnectionStringBuilder connection, Dictionary<string, string> password)
    {
        using var decrypt = Process.Start(Info(["run", "--rm", "--network", "none", "--mount", $"type=bind,source={keys},target=/private", "--mount", $"type=bind,source={file},target=/encrypted,readonly", "--entrypoint", "sh", Image, "-c", "GNUPGHOME=/private gpg --batch --quiet --decrypt /encrypted"], password))!;
        using var restore = Process.Start(Info(["run", "--rm", "-i", "--entrypoint", "pg_restore", "-e", "PGPASSWORD", Image, "--exit-on-error", "-h", "host.docker.internal", "-p", connection.Port.ToString(), "-U", connection.Username!, "-d", database], password, input: true))!;
        var decryptError = decrypt.StandardError.ReadToEndAsync();
        var restoreError = restore.StandardError.ReadToEndAsync();
        var restoreOutput = restore.StandardOutput.ReadToEndAsync();
        await decrypt.StandardOutput.BaseStream.CopyToAsync(restore.StandardInput.BaseStream);
        restore.StandardInput.Close();
        await Task.WhenAll(decrypt.WaitForExitAsync(), restore.WaitForExitAsync(), decryptError, restoreError, restoreOutput);
        if (decrypt.ExitCode != 0 || restore.ExitCode != 0) throw new InvalidOperationException($"Recovery candidate refused: decrypt exit {decrypt.ExitCode}, restore exit {restore.ExitCode}.");
    }

    private static ProcessStartInfo Info(IEnumerable<string> args, Dictionary<string, string>? environment = null, bool input = false)
    {
        var info = new ProcessStartInfo("docker") { RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = input, UseShellExecute = false, CreateNoWindow = true };
        foreach (var arg in args) info.ArgumentList.Add(arg);
        if (environment is not null) foreach (var (key, value) in environment) info.Environment[key] = value;
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
