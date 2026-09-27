using System.Diagnostics;
using System.Globalization;
using System.Text.Json.Nodes;
using Essenthos.Core.Desk;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Essenthos.Core.Tests;

/// <summary>
/// The console's copy of the server's backups, against a folder on this machine standing in for the
/// server: the same listing script and the same <c>cat</c> run by Git's sh instead of over ssh. What
/// is copied, what never is, what is kept, and what a failure is called.
/// </summary>
public sealed class ServerBackupsTests : IDisposable
{
    private const string Server = "deploy@stand-in";

    private const string Unreachable = "deploy@unreachable";

    private static readonly string GitBin = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Git", "usr", "bin");

    private readonly string _root = Path.Combine(Path.GetTempPath(), $"backups-{Guid.NewGuid():n}");

    private string Remote => Path.Combine(_root, "server");

    private string Local => Path.Combine(_root, "copies");

    private string Repository => Path.Combine(_root, "repository");

    public ServerBackupsTests()
    {
        Directory.CreateDirectory(Remote);
        Directory.CreateDirectory(Repository);
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public async Task TheNewestEncryptedDumpOfEachDatabaseIsCopiedAsTheServerWroteItAndNothingElse()
    {
        var older = ServerFile("essenthos_app", days: 2, Encrypted(1));
        var newest = ServerFile("essenthos_app", days: 1, Encrypted(2));
        var dev = ServerFile("essenthos_app_dev", days: 1, Encrypted(3));
        var plain = ServerFile("essenthos_app", days: 3, "PGDMP in the clear"u8.ToArray(), encrypted: false);
        File.WriteAllText(Path.Combine(Remote, "notes.txt"), "not a backup");

        var attempt = await Backups().Copy(automatic: false, CancellationToken.None);

        attempt!.State.Should().Be("succeeded");
        attempt.Copied.Should().BeEquivalentTo(newest, dev);
        attempt.Unencrypted.Should().Be(1);
        Directory.GetFiles(Local).Select(Path.GetFileName).Should().BeEquivalentTo(newest, dev);
        File.ReadAllBytes(Path.Combine(Local, newest)).Should().Equal(File.ReadAllBytes(Path.Combine(Remote, newest)));
        File.Exists(Path.Combine(Local, older)).Should().BeFalse();
        File.Exists(Path.Combine(Local, plain)).Should().BeFalse();
    }

    [Fact]
    public async Task ACopyAlreadyHereIsNotFetchedAgain()
    {
        var file = ServerFile("essenthos_app", days: 1, Encrypted(1));
        var backups = Backups();

        await backups.Copy(automatic: false, CancellationToken.None);
        var again = await backups.Copy(automatic: true, CancellationToken.None);

        again!.State.Should().Be("succeeded");
        again.Copied.Should().BeEmpty();
        backups.Copies().Select(c => c.File).Should().Equal(file);
        backups.Ledger().Last!.Automatic.Should().BeTrue();
    }

    [Fact]
    public async Task AServerWithOnlyUnencryptedDumpsGivesNothingAndSaysSo()
    {
        ServerFile("essenthos_app", days: 1, "PGDMP"u8.ToArray(), encrypted: false);

        var attempt = await Backups().Copy(automatic: false, CancellationToken.None);

        attempt!.Problem.Should().Be("plain");
        Directory.Exists(Local).Should().BeFalse();
    }

    [Fact]
    public async Task ADumpInTheClearUnderTheEncryptedNameIsNotKept()
    {
        ServerFile("essenthos_app", days: 1, "PGDMP\u0001\u0010 a plain dump"u8.ToArray());

        var attempt = await Backups().Copy(automatic: false, CancellationToken.None);

        attempt!.Problem.Should().Be("check");
        Directory.GetFiles(Local).Should().BeEmpty();
    }

    [Theory]
    [InlineData(null, "address")]
    [InlineData(Unreachable, "connect")]
    public async Task AFailureIsNamedByItsKindAndRecorded(string? server, string problem)
    {
        var backups = Backups(server);

        var attempt = await backups.Copy(automatic: true, CancellationToken.None);

        attempt!.Problem.Should().Be(problem);
        backups.Ledger().Last!.Problem.Should().Be(problem);
        backups.Ledger().LastSucceededAt.Should().BeNull();
    }

    [Fact]
    public async Task AServerWithoutTheFolderIsToldApartFromOneThatCannotBeReached()
    {
        var attempt = await Backups(remote: ToPosix(Path.Combine(_root, "missing"))).Copy(automatic: false, CancellationToken.None);

        attempt!.Problem.Should().Be("folder");
    }

    [Fact]
    public void CopiesPastTheCountOrTheServersTimeAreRemovedButNeverTheNewestOfADatabase()
    {
        Directory.CreateDirectory(Local);
        var kept = new[] { LocalFile("essenthos_app", days: 1), LocalFile("essenthos_app", days: 2) };
        var third = LocalFile("essenthos_app", days: 3);
        var expired = LocalFile("essenthos_app_dev", days: 20);
        var lastOfItsDatabase = LocalFile("essenthos_stats", days: 40);
        var olderStats = LocalFile("essenthos_stats", days: 41);

        Backups().Prune(keep: 2, DateTime.UtcNow);

        Directory.GetFiles(Local).Select(Path.GetFileName).Should()
            .BeEquivalentTo(kept.Append(expired).Append(lastOfItsDatabase));
        File.Exists(Path.Combine(Local, third)).Should().BeFalse();
        File.Exists(Path.Combine(Local, olderStats)).Should().BeFalse();
    }

    [Fact]
    public async Task TheOwnersSettingsAreKeptLoggedAndBounded()
    {
        var backups = Backups();

        (await backups.Set(new BackupSettingsRequest(false, 3, null)))!.Should().Be(new BackupSettings(false, 3));
        (await backups.Set(new BackupSettingsRequest(null, 0, null))).Should().BeNull();

        backups.Settings().Should().Be(new BackupSettings(false, 3));
        var lines = File.ReadAllLines(Path.Combine(Repository, "Resources", "Essenthos", ChangeLog.FileName))
            .Select(line => JsonNode.Parse(line)!).ToList();
        lines.Select(l => (string?)l["action"]).Should().Equal("automatic", "keep");
        lines.Should().OnlyContain(l => (string?)l["section"] == ServerBackups.Section);
    }

    [Fact]
    public void AnAutomaticCopyIsDueADayAfterTheLastAndSomeHoursAfterAFailure()
    {
        var now = DateTime.UtcNow;
        string At(double hoursAgo) => now.AddHours(-hoursAgo).ToString("O", CultureInfo.InvariantCulture);
        BackupAttempt Failed(double hoursAgo) => new(At(hoursAgo), "failed", "connect", null, [], true, 0);
        var on = new BackupSettings(true, 7);

        ServerBackups.NextAutomatic(on, new BackupLedger(null, null), hasServer: true).Should().BeOnOrBefore(now);
        ServerBackups.NextAutomatic(on with { Automatic = false }, new BackupLedger(null, null), hasServer: true).Should().BeNull();
        ServerBackups.NextAutomatic(on, new BackupLedger(null, null), hasServer: false).Should().BeNull();
        ServerBackups.NextAutomatic(on, new BackupLedger(null, At(2)), hasServer: true).Should().BeAfter(now);
        ServerBackups.NextAutomatic(on, new BackupLedger(null, At(24)), hasServer: true).Should().BeOnOrBefore(now);
        ServerBackups.NextAutomatic(on, new BackupLedger(Failed(1), At(30)), hasServer: true).Should().BeAfter(now);
        ServerBackups.NextAutomatic(on, new BackupLedger(Failed(4), At(30)), hasServer: true).Should().BeOnOrBefore(now);
    }

    private ServerBackups Backups(string? server = Server, string? remote = null)
    {
        var settings = new Dictionary<string, string?>
        {
            ["Desk:Backups:Folder"] = Local,
            ["Desk:Backups:Remote"] = remote ?? ToPosix(Remote),
            ["Desk:Backups:Server"] = server,
        };
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var paths = new DeskPaths(Repository, Path.Combine(_root, "resources"), _root, Path.Combine(_root, "cache"));
        return new ServerBackups(paths, configuration, new StandIn(), new ChangeLog(paths), NullLogger<ServerBackups>.Instance);
    }

    private static byte[] Encrypted(byte seed) => [0x85, 0x02, 0x0c, seed, .. Enumerable.Range(0, 4096).Select(i => (byte)(i * seed))];

    private static string Name(string database, int days, bool encrypted = true) =>
        $"{database}-{DateTime.UtcNow.AddDays(-days):yyyyMMdd'T'HHmm'Z'}{(encrypted ? ".dump.gpg" : ".dump")}";

    private string ServerFile(string database, int days, byte[] bytes, bool encrypted = true)
    {
        var name = Name(database, days, encrypted);
        File.WriteAllBytes(Path.Combine(Remote, name), bytes);
        return name;
    }

    private string LocalFile(string database, int days)
    {
        var name = Name(database, days);
        File.WriteAllBytes(Path.Combine(Local, name), Encrypted((byte)days));
        return name;
    }

    /// <summary>A Windows path as Git's sh names it: C:\Users → /c/Users.</summary>
    private static string ToPosix(string path) =>
        "/" + char.ToLowerInvariant(path[0]) + path[2..].Replace('\\', '/');

    /// <summary>The server, as a folder here: each command run by Git's sh, and one address that never answers.</summary>
    private sealed class StandIn : IServerShell
    {
        public Task<ShellResult> Run(string server, string command, Stream? output, CancellationToken cancellationToken)
        {
            if (server == Unreachable)
            {
                return Task.FromResult(new ShellResult(255, string.Empty, "ssh: connect to host unreachable port 22: Connection refused"));
            }

            var start = new ProcessStartInfo(Path.Combine(GitBin, "sh.exe"))
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            start.ArgumentList.Add("-c");
            start.ArgumentList.Add(command);
            start.Environment["PATH"] = GitBin + Path.PathSeparator + start.Environment["PATH"];
            return ShellProcess.Run(start, output, cancellationToken);
        }
    }
}
