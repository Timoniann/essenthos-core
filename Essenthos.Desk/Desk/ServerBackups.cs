using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Essenthos.Core.Desk;

/// <summary>One encrypted backup kept on this machine.</summary>
/// <param name="File">Its name, as the server wrote it.</param>
/// <param name="Database">Which database it is a dump of.</param>
/// <param name="TakenAt">When the server wrote it, read from its name.</param>
/// <param name="CopiedAt">When it reached this machine.</param>
internal sealed record BackupCopy(string File, string Database, string TakenAt, string CopiedAt, long Bytes);

/// <param name="State"><c>succeeded</c> or <c>failed</c>.</param>
/// <param name="Problem">
/// Why it failed, as a key the page words: <c>address</c> (no server to copy from), <c>drive</c> (the
/// folder's drive is not there), <c>connect</c> (ssh could not reach or sign in), <c>folder</c> (the
/// server has no such folder), <c>none</c> (no backups there), <c>plain</c> (only unencrypted ones),
/// <c>check</c> (a file arrived different from the server's), <c>copy</c> (the transfer broke off),
/// <c>local</c> (this machine could not write it). Null when it succeeded.
/// </param>
/// <param name="Detail">What ssh or the server said, for when the key is not enough.</param>
/// <param name="Copied">The files this attempt brought over; one already here is not copied again.</param>
/// <param name="Automatic">Whether the console started it on its own rather than on the button.</param>
/// <param name="Unencrypted">How many dumps the server holds in the clear, which are never copied.</param>
internal sealed record BackupAttempt(
    string At,
    string State,
    string? Problem,
    string? Detail,
    IReadOnlyList<string> Copied,
    bool Automatic,
    int Unencrypted);

/// <param name="Automatic">Whether the console copies the newest backups once a day while it runs.</param>
/// <param name="Keep">How many copies of each database are kept here, newest first.</param>
internal sealed record BackupSettings(bool Automatic, int Keep);

/// <param name="LastSucceededAt">When an attempt last succeeded, which is what the next automatic one counts from.</param>
internal sealed record BackupLedger(BackupAttempt? Last, string? LastSucceededAt);

/// <param name="Server">The ssh address copies come from, or null where none is set.</param>
/// <param name="Remote">The folder on the server the backup service writes into.</param>
/// <param name="Folder">Where the copies are kept on this machine.</param>
/// <param name="Drive">Whether the folder's drive is there.</param>
/// <param name="KeepDays">The longest any copy but the newest of each database is kept, as on the server.</param>
/// <param name="NextAt">When the console will next copy on its own, or null where it will not.</param>
internal sealed record ServerBackupsResponse(
    string? Server,
    string Remote,
    string Folder,
    bool Drive,
    BackupSettings Settings,
    int KeepDays,
    bool Running,
    BackupAttempt? Last,
    string? LastSucceededAt,
    string? NextAt,
    IReadOnlyList<BackupCopy> Copies);

internal sealed record BackupSettingsRequest(bool? Automatic, int? Keep, string? Note);

/// <param name="ExitCode">255 is ssh's own failure: it never reached the command.</param>
internal sealed record ShellResult(int ExitCode, string Output, string Error);

/// <summary>A command run on the server, its output read as text or, for a file, into a stream.</summary>
internal interface IServerShell
{
    Task<ShellResult> Run(string server, string command, Stream? output, CancellationToken cancellationToken);
}

/// <summary>
/// The command run over the same ssh the deploy uses: the owner's own key, never a password, and
/// never a prompt, so an automatic copy that cannot sign in fails and says so instead of waiting.
/// </summary>
internal sealed class SshShell(IConfiguration configuration) : IServerShell
{
    /// <summary>How long ssh may take to reach the server before the attempt is given up.</summary>
    private const int ConnectSeconds = 15;

    public async Task<ShellResult> Run(string server, string command, Stream? output, CancellationToken cancellationToken)
    {
        var program = configuration.GetSection("Desk:Backups:Ssh").Get<string[]>() is { Length: > 0 } several
            ? several
            : configuration["Desk:Backups:Ssh"] is { Length: > 0 } one ? [one] : ["ssh"];
        var start = new ProcessStartInfo(program[0])
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var argument in program.Skip(1).Concat(
                     ["-o", "BatchMode=yes", "-o", $"ConnectTimeout={ConnectSeconds}", server, command]))
        {
            start.ArgumentList.Add(argument);
        }

        return await ShellProcess.Run(start, output, cancellationToken);
    }
}

internal static class ShellProcess
{
    /// <summary>Runs a process to the end, its output copied into <paramref name="output"/> where one is given.</summary>
    public static async Task<ShellResult> Run(ProcessStartInfo start, Stream? output, CancellationToken cancellationToken)
    {
        using var process = new Process { StartInfo = start };
        try
        {
            process.Start();
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or FileNotFoundException)
        {
            return new ShellResult(-1, string.Empty, $"{start.FileName} could not be started: {exception.Message}");
        }

        process.StandardInput.Close();
        var error = process.StandardError.ReadToEndAsync(cancellationToken);
        string text;
        if (output is null)
        {
            text = await process.StandardOutput.ReadToEndAsync(cancellationToken);
        }
        else
        {
            await process.StandardOutput.BaseStream.CopyToAsync(output, cancellationToken);
            text = string.Empty;
        }

        await process.WaitForExitAsync(cancellationToken);
        return new ShellResult(process.ExitCode, text, await error);
    }
}

/// <summary>
/// The accounts' backups copied from the server to this machine: the newest encrypted dump of each
/// database, fetched as the server wrote it and checked against the server's own checksum, so it is
/// never opened on the way — only the private key the owner keeps can open it. A dump the server
/// wrote in the clear is never fetched. The copies are kept as many as the owner says, and none but
/// the newest of each database longer than the server keeps its own, so a copy here outlives nothing
/// the privacy page promises.
///
/// <para>
/// The server's address is the one the deploy uses (<c>Publish:Targets:&lt;prod&gt;:Ssh</c>, in user
/// secrets) unless <c>Desk:Backups:Server</c> names another; the folders are under
/// <c>Desk:Backups</c>. The owner's settings are a file beside his other choices, and each change to
/// them is in the change log; the attempts are the console's own record, kept outside the checkout.
/// </para>
/// </summary>
internal sealed partial class ServerBackups(
    DeskPaths paths,
    IConfiguration configuration,
    IServerShell shell,
    ChangeLog log,
    ILogger<ServerBackups> logger)
{
    public const string Section = "backups";

    public const int DefaultKeep = 7;

    /// <summary>How long the server keeps each dump, and so the longest a copy is kept here.</summary>
    public const int DefaultKeepDays = 14;

    public const string SettingsFile = "server-backups.json";

    private const string LedgerFile = "server-backups-last.json";

    private const string DefaultFolder = @"E:\Projects\Essenthos\server-backups";

    private const string DefaultRemote = "/srv/essenthos/backups";

    private const string Encrypted = ".dump.gpg";

    private const string Partial = ".partial";

    private const string Succeeded = "succeeded";

    private const string Failed = "failed";

    /// <summary>The list script's exit when the folder is not there, told apart from ssh's own 255.</summary>
    private const int NoFolderExit = 3;

    private const int SshFailedExit = 255;

    /// <summary>How much of what ssh said is kept with an attempt.</summary>
    private const int MostDetail = 600;

    /// <summary>How long after a copy the next automatic one is due: a day, less the time a check can come late.</summary>
    public static readonly TimeSpan Interval = TimeSpan.FromHours(23);

    /// <summary>How long after an automatic attempt failed it is tried again.</summary>
    public static readonly TimeSpan RetryAfter = TimeSpan.FromHours(3);

    /// <summary>How long a file half copied is left before it is taken for one a stopped console abandoned.</summary>
    private static readonly TimeSpan AbandonedAfter = TimeSpan.FromHours(1);

    /// <summary>What a custom-format pg_dump begins with, which an encrypted file never does.</summary>
    private static readonly byte[] PlainDump = "PGDMP"u8.ToArray();

    [GeneratedRegex(@"^(?<database>[a-z0-9_]+)-(?<stamp>\d{8}T\d{4}Z)\.dump\.gpg$")]
    private static partial Regex BackupName();

    [GeneratedRegex(@"^/[A-Za-z0-9._/-]+$")]
    private static partial Regex RemotePath();

    [GeneratedRegex(@"^[a-f0-9]{64}$")]
    private static partial Regex Sha256();

    private readonly SemaphoreSlim _gate = new(1, 1);

    private readonly Lock _ledgerLock = new();

    public string? Server
    {
        get
        {
            if (configuration["Desk:Backups:Server"] is { Length: > 0 } server)
            {
                return server;
            }

            var target = configuration[$"Desk:Deploy:Environments:{Deployment.Production}:Target"] is { Length: > 0 } t
                ? t
                : Deployment.Production;
            return configuration[$"Publish:Targets:{target}:Ssh"] is { Length: > 0 } ssh ? ssh : null;
        }
    }

    public string Remote => configuration["Desk:Backups:Remote"] is { Length: > 0 } remote ? remote.TrimEnd('/') : DefaultRemote;

    public string Folder => Path.GetFullPath(configuration["Desk:Backups:Folder"] is { Length: > 0 } folder ? folder : DefaultFolder);

    public int KeepDays => configuration.GetValue("Desk:Backups:KeepDays", DefaultKeepDays);

    private string SettingsPath => Path.Combine(paths.Owner, SettingsFile);

    private string LedgerPath => Path.Combine(paths.Cache, LedgerFile);

    private bool Drive => Path.GetPathRoot(Folder) is { } root && Directory.Exists(root);

    public ServerBackupsResponse Read(DateTime now)
    {
        var settings = Settings();
        var ledger = Ledger();
        var next = NextAutomatic(settings, ledger, Server is not null);
        return new ServerBackupsResponse(
            Server,
            Remote,
            Folder,
            Drive,
            settings,
            KeepDays,
            _gate.CurrentCount == 0,
            ledger.Last,
            ledger.LastSucceededAt,
            next is { } at ? (at < now ? now : at).ToString("O", CultureInfo.InvariantCulture) : null,
            Copies());
    }

    public BackupSettings Settings()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                using var stream = File.OpenRead(SettingsPath);
                if (JsonSerializer.Deserialize(stream, DeskJsonContext.Default.BackupSettings) is { } read)
                {
                    return read with { Keep = Math.Clamp(read.Keep, 1, KeepDays) };
                }
            }
        }
        catch (Exception exception) when (exception is JsonException or IOException)
        {
            logger.LogWarning(exception, "The backup settings at {Path} could not be read; the defaults stand", SettingsPath);
        }

        return new BackupSettings(true, DefaultKeep);
    }

    /// <summary>Changes the owner's settings and logs each change; null where a value is out of range.</summary>
    public async Task<BackupSettings?> Set(BackupSettingsRequest request)
    {
        if (request.Keep is { } asked && (asked < 1 || asked > KeepDays))
        {
            return null;
        }

        var before = Settings();
        var after = new BackupSettings(request.Automatic ?? before.Automatic, request.Keep ?? before.Keep);
        if (after == before)
        {
            return after;
        }

        Directory.CreateDirectory(paths.Owner);
        var written = SettingsPath + ".writing";
        await using (var stream = File.Create(written))
        {
            await JsonSerializer.SerializeAsync(stream, after, DeskJsonContext.Default.BackupSettings);
        }

        File.Move(written, SettingsPath, overwrite: true);
        if (after.Automatic != before.Automatic)
        {
            await log.Append(Section, "automatic", "setting/automatic", JsonValue.Create(before.Automatic), JsonValue.Create(after.Automatic), request.Note, null);
        }

        if (after.Keep != before.Keep)
        {
            await log.Append(Section, "keep", "setting/keep", JsonValue.Create(before.Keep), JsonValue.Create(after.Keep), request.Note, null);
            if (Drive && Directory.Exists(Folder))
            {
                Prune(after.Keep, DateTime.UtcNow);
            }
        }

        return after;
    }

    /// <summary>Whether an automatic copy is due: on, a server to copy from, and none recently, nor a failure just now.</summary>
    public bool Due(DateTime now) =>
        NextAutomatic(Settings(), Ledger(), Server is not null) is { } next && next <= now;

    public static DateTime? NextAutomatic(BackupSettings settings, BackupLedger ledger, bool hasServer)
    {
        if (!settings.Automatic || !hasServer)
        {
            return null;
        }

        var next = ledger.LastSucceededAt is { } succeeded ? Parse(succeeded) + Interval : DateTime.MinValue;
        if (ledger.Last is { State: Failed, Automatic: true } failed && Parse(failed.At) + RetryAfter > next)
        {
            next = Parse(failed.At) + RetryAfter;
        }

        return next;
    }

    /// <summary>
    /// Copies the newest encrypted backup of each database here, unless another copy is under way,
    /// and records how it went. Null where one was already running.
    /// </summary>
    public async Task<BackupAttempt?> Copy(bool automatic, CancellationToken cancellationToken)
    {
        if (!await _gate.WaitAsync(0, cancellationToken))
        {
            return null;
        }

        try
        {
            BackupAttempt attempt;
            try
            {
                attempt = await Attempt(automatic, cancellationToken);
            }
            catch (IOException exception)
            {
                logger.LogWarning(exception, "A backup copy could not be written to {Folder}", Folder);
                attempt = Fail(automatic, "local", exception.Message);
            }

            Record(attempt);
            logger.LogInformation("Backup copy {State}{Problem}, {Count} file(s) copied", attempt.State,
                attempt.Problem is null ? "" : $" ({attempt.Problem})", attempt.Copied.Count);
            return attempt;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Removes the copies past the owner's count for each database, and any but the newest of each
    /// that is older than the server keeps its own. The newest is kept whatever its age, so a server
    /// that stopped making backups never leaves this machine with none.
    /// </summary>
    public void Prune(int keep, DateTime now)
    {
        if (!Directory.Exists(Folder))
        {
            return;
        }

        var limit = now - TimeSpan.FromDays(KeepDays);
        foreach (var database in Copies().GroupBy(c => c.Database))
        {
            var newestFirst = database.OrderByDescending(c => c.TakenAt, StringComparer.Ordinal).ToList();
            foreach (var copy in newestFirst.Skip(1).Select((copy, index) => (copy, index: index + 1))
                         .Where(c => c.index >= keep || Parse(c.copy.TakenAt) < limit)
                         .Select(c => c.copy))
            {
                File.Delete(Path.Combine(Folder, copy.File));
                logger.LogInformation("Backup copy {File} removed", copy.File);
            }
        }

        foreach (var stale in Directory.EnumerateFiles(Folder, "*" + Partial)
                     .Where(file => File.GetLastWriteTimeUtc(file) < now - AbandonedAfter))
        {
            File.Delete(stale);
        }
    }

    public IReadOnlyList<BackupCopy> Copies()
    {
        if (!Drive || !Directory.Exists(Folder))
        {
            return [];
        }

        return Directory.EnumerateFiles(Folder, "*" + Encrypted)
            .Select(path => (path, match: BackupName().Match(Path.GetFileName(path))))
            .Where(f => f.match.Success)
            .Select(f =>
            {
                var info = new FileInfo(f.path);
                return new BackupCopy(
                    info.Name,
                    f.match.Groups["database"].Value,
                    Stamp(f.match.Groups["stamp"].Value).ToString("O", CultureInfo.InvariantCulture),
                    info.LastWriteTimeUtc.ToString("O", CultureInfo.InvariantCulture),
                    info.Length);
            })
            .OrderByDescending(c => c.TakenAt, StringComparer.Ordinal)
            .ThenBy(c => c.Database, StringComparer.Ordinal)
            .ToList();
    }

    public BackupLedger Ledger()
    {
        lock (_ledgerLock)
        {
            return LedgerUnlocked();
        }
    }

    /// <summary>The script that lists the server's backups: each encrypted one with its size and checksum, and the plain ones by name.</summary>
    public static string ListCommand(string remote) =>
        $"cd '{remote}' 2>/dev/null || {{ echo \"There is no folder {remote} on the server.\" >&2; exit {NoFolderExit}; }}; " +
        "for f in *.dump.gpg; do [ -f \"$f\" ] || continue; " +
        "printf 'encrypted\\t%s\\t%s\\t%s\\n' \"$f\" \"$(stat -c %s -- \"$f\")\" \"$(sha256sum -- \"$f\" | cut -d' ' -f1)\"; done; " +
        "for f in *.dump; do [ -f \"$f\" ] && printf 'plain\\t%s\\n' \"$f\"; done; exit 0";

    private async Task<BackupAttempt> Attempt(bool automatic, CancellationToken cancellationToken)
    {
        if (Server is not { } server)
        {
            return Fail(automatic, "address", null);
        }

        if (!Drive)
        {
            return Fail(automatic, "drive", null);
        }

        if (!RemotePath().IsMatch(Remote))
        {
            return Fail(automatic, "folder", $"{Remote} is not a folder the console can name on the server.");
        }

        var listed = await shell.Run(server, ListCommand(Remote), null, cancellationToken);
        if (listed.ExitCode != 0)
        {
            return Fail(automatic, listed.ExitCode == NoFolderExit ? "folder" : "connect", Said(listed));
        }

        var encrypted = new List<(string Name, string Database, string Stamp, long Bytes, string Sha)>();
        var plain = 0;
        foreach (var line in listed.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var fields = line.Split('\t');
            if (fields is ["plain", _])
            {
                plain++;
            }
            else if (fields is ["encrypted", var name, var size, var sha]
                     && BackupName().Match(name) is { Success: true } match
                     && long.TryParse(size, NumberStyles.None, CultureInfo.InvariantCulture, out var bytes)
                     && Sha256().IsMatch(sha))
            {
                encrypted.Add((name, match.Groups["database"].Value, match.Groups["stamp"].Value, bytes, sha));
            }
        }

        if (encrypted.Count == 0)
        {
            return Fail(automatic, plain > 0 ? "plain" : "none", null, unencrypted: plain);
        }

        Directory.CreateDirectory(Folder);
        var copied = new List<string>();
        foreach (var newest in encrypted.GroupBy(e => e.Database).Select(g => g.MaxBy(e => e.Stamp, StringComparer.Ordinal)))
        {
            var target = Path.Combine(Folder, newest.Name);
            if (File.Exists(target) && new FileInfo(target).Length == newest.Bytes && await HashOf(target, cancellationToken) == newest.Sha)
            {
                continue;
            }

            var partial = target + Partial;
            ShellResult fetched;
            await using (var file = File.Create(partial))
            {
                fetched = await shell.Run(server, $"cat -- '{Remote}/{newest.Name}'", file, cancellationToken);
            }

            string sha;
            bool looksEncrypted;
            await using (var written = File.OpenRead(partial))
            {
                sha = Convert.ToHexStringLower(await SHA256.HashDataAsync(written, cancellationToken));
                written.Position = 0;
                looksEncrypted = await LooksEncrypted(written, cancellationToken);
            }

            if (fetched.ExitCode != 0)
            {
                File.Delete(partial);
                return Fail(automatic, fetched.ExitCode == SshFailedExit ? "connect" : "copy", Said(fetched), copied, plain);
            }

            if (sha != newest.Sha || !looksEncrypted)
            {
                File.Delete(partial);
                return Fail(automatic, "check", $"{newest.Name} arrived different from the server's file.", copied, plain);
            }

            File.Move(partial, target, overwrite: true);
            copied.Add(newest.Name);
        }

        Prune(Settings().Keep, DateTime.UtcNow);
        return new BackupAttempt(JsonFiles.Now(), Succeeded, null, null, copied, automatic, plain);
    }

    /// <summary>
    /// Whether a file begins as an OpenPGP message does, with a packet tag, and not as a plain dump —
    /// so a dump in the clear given the encrypted name is never kept as if it were one.
    /// </summary>
    private static async Task<bool> LooksEncrypted(Stream stream, CancellationToken cancellationToken)
    {
        var head = new byte[PlainDump.Length];
        var read = await stream.ReadAtLeastAsync(head, head.Length, throwOnEndOfStream: false, cancellationToken);
        return read > 0 && (head[0] & 0x80) != 0 && !head.AsSpan(0, read).SequenceEqual(PlainDump);
    }

    private static async Task<string> HashOf(string path, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        return Convert.ToHexStringLower(await SHA256.HashDataAsync(stream, cancellationToken));
    }

    private static BackupAttempt Fail(
        bool automatic, string problem, string? detail, IReadOnlyList<string>? copied = null, int unencrypted = 0) =>
        new(JsonFiles.Now(), Failed, problem, detail, copied ?? [], automatic, unencrypted);

    private void Record(BackupAttempt attempt)
    {
        lock (_ledgerLock)
        {
            var before = LedgerUnlocked();
            var ledger = new BackupLedger(attempt, attempt.State == Succeeded ? attempt.At : before.LastSucceededAt);
            Directory.CreateDirectory(paths.Cache);
            var written = LedgerPath + ".writing";
            using (var stream = File.Create(written))
            {
                JsonSerializer.Serialize(stream, ledger, DeskJsonContext.Default.BackupLedger);
            }

            File.Move(written, LedgerPath, overwrite: true);
        }
    }

    private BackupLedger LedgerUnlocked()
    {
        try
        {
            if (File.Exists(LedgerPath))
            {
                using var stream = File.OpenRead(LedgerPath);
                return JsonSerializer.Deserialize(stream, DeskJsonContext.Default.BackupLedger) ?? new BackupLedger(null, null);
            }
        }
        catch (Exception exception) when (exception is JsonException or IOException)
        {
            logger.LogWarning(exception, "The record of backup copies at {Path} could not be read", LedgerPath);
        }

        return new BackupLedger(null, null);
    }

    private static string? Said(ShellResult result)
    {
        var text = (result.Error.Trim().Length > 0 ? result.Error : result.Output).Trim();
        return text.Length == 0 ? null : text.Length > MostDetail ? text[..MostDetail] : text;
    }

    private static DateTime Stamp(string stamp) =>
        DateTime.ParseExact(stamp, "yyyyMMdd'T'HHmm'Z'", CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal);

    private static DateTime Parse(string at) =>
        DateTime.Parse(at, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal);
}

/// <summary>
/// The automatic copy: while the console runs it looks every quarter of an hour, and copies when the
/// last copy is a day old — so a console started in the morning has the night's backup within minutes,
/// and one left running copies once a day. It also removes copies past their time on every look.
/// </summary>
internal sealed class ServerBackupSchedule(ServerBackups backups, ILogger<ServerBackupSchedule> logger) : BackgroundService
{
    /// <summary>How long after the console starts it first looks, so starting it is not slowed by a copy.</summary>
    private static readonly TimeSpan FirstLook = TimeSpan.FromMinutes(2);

    private static readonly TimeSpan Tick = TimeSpan.FromMinutes(15);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(FirstLook, stoppingToken);
            using var timer = new PeriodicTimer(Tick);
            do
            {
                try
                {
                    var now = DateTime.UtcNow;
                    backups.Prune(backups.Settings().Keep, now);
                    if (backups.Due(now))
                    {
                        await backups.Copy(automatic: true, stoppingToken);
                    }
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    logger.LogWarning(exception, "The automatic backup copy could not look at its folder");
                }
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }
}
