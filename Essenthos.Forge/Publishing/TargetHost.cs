using System.Net;
using System.Net.Sockets;
using Npgsql;

namespace Essenthos.Core.Publishing;

/// <summary>
/// The machine a target lives on, reached the same way whether it is this one or a droplet: every
/// command is a <c>docker</c> command, prefixed with ssh when the target is remote. That is what lets
/// the whole publication be rehearsed here before a server exists, with nothing but the prefix
/// differing.
/// </summary>
internal sealed class TargetHost(ReleaseTarget target)
{
    public ReleaseTarget Target => target;

    public Task<string> Docker(IReadOnlyList<string> arguments, CancellationToken cancellationToken, bool quiet = false) =>
        target.IsRemote
            ? Shell.Run("ssh", [.. SshOptions, target.Ssh, "docker", .. arguments.Select(Shell.QuoteForRemote)], cancellationToken, quiet)
            : Shell.Run("docker", arguments, cancellationToken, quiet);

    /// <summary>
    /// A statement as the superuser over the container's own socket, which the image trusts.
    /// </summary>
    public Task<string> Sql(string database, string statement, CancellationToken cancellationToken) =>
        Sql(database, [statement], cancellationToken);

    /// <summary>
    /// Several statements in one psql, stopping at the first that fails. Each is its own <c>-c</c>
    /// and so its own transaction, because <c>CREATE DATABASE</c>, <c>DROP DATABASE</c> and a rename
    /// of a database refuse to run inside one — and one psql rather than several so that the steps of
    /// a swap follow each other in milliseconds rather than in round trips through docker and ssh.
    /// </summary>
    public Task<string> Sql(string database, IReadOnlyList<string> statements, CancellationToken cancellationToken) =>
        Docker(
            ["exec", target.Container, "psql", "-U", target.Superuser, "-d", database,
             "-v", "ON_ERROR_STOP=1", "-X", "-q", "-A", "-t", .. statements.SelectMany(s => new[] { "-c", s })],
            cancellationToken);

    public async Task<bool> DatabaseExists(string database, CancellationToken cancellationToken) =>
        await Sql("postgres", $"SELECT 1 FROM pg_database WHERE datname = {Literal(database)}", cancellationToken) == "1";

    /// <summary>
    /// Puts the artefact where the database container can read it. Remote, that is scp into the data
    /// root's <c>releases</c> folder; here, a copy into the same folder of the rehearsal's data root.
    /// Either way the container sees it at <c>/releases</c> through a bind mount.
    /// </summary>
    public async Task Upload(string file, CancellationToken cancellationToken)
    {
        var name = Path.GetFileName(file);
        if (target.IsRemote)
        {
            var folder = $"{target.DataRoot.TrimEnd('/')}/releases";
            await Shell.Run("ssh", [.. SshOptions, target.Ssh, "mkdir", "-p", Shell.QuoteForRemote(folder)], cancellationToken);
            await Shell.Run("scp", ["-q", .. SshOptions, file, $"{target.Ssh}:{folder}/{name}"], cancellationToken);
        }
        else
        {
            var folder = Path.Combine(target.DataRoot, "releases");
            Directory.CreateDirectory(folder);
            File.Copy(file, Path.Combine(folder, name), overwrite: true);
        }
    }

    /// <summary>
    /// Where the target's API reads the pictures of this corpus: the data root's <c>images</c> folder,
    /// one folder per corpus, which the API container mounts read-only at <c>/images</c>.
    /// </summary>
    public string ImagesFolder => target.IsRemote
        ? $"{target.DataRoot.TrimEnd('/')}/images/{target.Database}"
        : Path.Combine(target.DataRoot, "images", target.Database);

    /// <summary>The pictures the target has, by path, with their SHA-256; none where the folder is not there yet.</summary>
    public async Task<Dictionary<string, string>> Images(CancellationToken cancellationToken)
    {
        if (!target.IsRemote)
        {
            return ImageManifest.Read(ImagesFolder);
        }

        var folder = Shell.QuoteForRemote(ImagesFolder);
        var listing = await Shell.Run(
            "ssh", [.. SshOptions, target.Ssh, $"mkdir -p {folder} && cd {folder} && find . -type f -exec sha256sum {{}} +"],
            cancellationToken);
        return ImageManifest.Parse(listing);
    }

    /// <summary>
    /// Puts <paramref name="files"/>, paths under <paramref name="source"/>, into the target's images
    /// folder.
    /// </summary>
    public Task SendImages(string source, IReadOnlyList<string> files, CancellationToken cancellationToken) =>
        SendFiles(source, files, ImagesFolder, cancellationToken);

    /// <summary>
    /// Where the target's proxy serves the downloads from and its API lists them: the data root's
    /// downloads folder, mounted read-only into both.
    /// </summary>
    public string DownloadsFolder => target.IsRemote
        ? $"{target.DataRoot.TrimEnd('/')}/{target.DownloadsName}"
        : Path.Combine(target.DataRoot, target.DownloadsName);

    /// <summary>Every file the target serves as a download, by path, with its SHA-256; none where the folder is not there yet.</summary>
    public async Task<Dictionary<string, string>> Downloads(CancellationToken cancellationToken)
    {
        if (!target.IsRemote)
        {
            return ImageManifest.Read(DownloadsFolder, extensions: null);
        }

        var folder = Shell.QuoteForRemote(DownloadsFolder);
        var listing = await Shell.Run(
            "ssh", [.. SshOptions, target.Ssh, $"mkdir -p {folder} && cd {folder} && find . -type f -exec sha256sum {{}} +"],
            cancellationToken);
        return ImageManifest.Parse(listing);
    }

    /// <summary>Puts <paramref name="files"/>, paths under <paramref name="source"/>, into the target's downloads folder.</summary>
    public Task SendDownloads(string source, IReadOnlyList<string> files, CancellationToken cancellationToken) =>
        SendFiles(source, files, DownloadsFolder, cancellationToken);

    /// <summary>
    /// Puts one file into the downloads folder as another name first and then in place under its own,
    /// so a reader fetching it, or the API reading it, never meets it half written.
    /// </summary>
    public async Task ReplaceDownload(string source, string file, CancellationToken cancellationToken)
    {
        if (!target.IsRemote)
        {
            var destination = Path.Combine(DownloadsFolder, file);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            var arriving = destination + ".incoming";
            File.Copy(Path.Combine(source, file), arriving, overwrite: true);
            File.Move(arriving, destination, overwrite: true);
            return;
        }

        var remote = $"{DownloadsFolder}/{file}";
        var incoming = Shell.QuoteForRemote(remote + ".incoming");
        await Shell.Run("ssh", [.. SshOptions, target.Ssh, "mkdir", "-p", Shell.QuoteForRemote(DownloadsFolder)], cancellationToken);
        await Shell.Run("scp", ["-q", .. SshOptions, Path.Combine(source, file), $"{target.Ssh}:{remote}.incoming"], cancellationToken);
        await Shell.Run(
            "ssh", [.. SshOptions, target.Ssh, $"chmod 644 {incoming} && mv -f {incoming} {Shell.QuoteForRemote(remote)}"],
            cancellationToken);
    }

    /// <summary>
    /// Takes <paramref name="files"/>, paths under the downloads folder, away, and the folders that
    /// leave empty.
    /// </summary>
    public async Task RemoveDownloads(IReadOnlyList<string> files, CancellationToken cancellationToken)
    {
        if (files.Count == 0)
        {
            return;
        }

        if (!target.IsRemote)
        {
            foreach (var file in files)
            {
                File.Delete(Path.Combine(DownloadsFolder, file));
            }

            foreach (var folder in Directory.EnumerateDirectories(DownloadsFolder, "*", SearchOption.AllDirectories)
                         .OrderByDescending(folder => folder.Length)
                         .Where(folder => !Directory.EnumerateFileSystemEntries(folder).Any()))
            {
                Directory.Delete(folder);
            }

            return;
        }

        var list = Path.GetTempFileName();
        try
        {
            await File.WriteAllLinesAsync(list, files, cancellationToken);
            var folder = Shell.QuoteForRemote(DownloadsFolder);
            var remote = Shell.QuoteForRemote($"{target.DataRoot.TrimEnd('/')}/{target.DownloadsName}.remove.txt");
            await Shell.Run("scp", ["-q", .. SshOptions, list, $"{target.Ssh}:{remote}"], cancellationToken);
            await Shell.Run(
                "ssh", [.. SshOptions, target.Ssh,
                    $"cd {folder} && tr -d '\\r' < {remote} | xargs -d '\\n' rm -f -- && rm -f {remote} && find . -mindepth 1 -type d -empty -delete"],
                cancellationToken);
        }
        finally
        {
            File.Delete(list);
        }
    }

    /// <summary>
    /// Puts <paramref name="files"/>, paths under <paramref name="source"/>, into <paramref name="destination"/>.
    /// Remote, they go as one tar through scp and are unpacked there, because a thousand separate copies
    /// over ssh take minutes where one archive takes seconds.
    /// </summary>
    private async Task SendFiles(string source, IReadOnlyList<string> files, string destination, CancellationToken cancellationToken)
    {
        if (files.Count == 0)
        {
            return;
        }

        if (!target.IsRemote)
        {
            foreach (var file in files)
            {
                var path = Path.Combine(destination, file);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.Copy(Path.Combine(source, file), path, overwrite: true);
            }

            return;
        }

        var list = Path.GetTempFileName();
        var archive = Path.ChangeExtension(Path.GetTempFileName(), ".tar");
        try
        {
            await File.WriteAllLinesAsync(list, files, cancellationToken);
            await Shell.Run("tar", ["-cf", archive, "-C", source, "-T", list], cancellationToken);
            var folder = Shell.QuoteForRemote(destination);
            var remote = $"{destination}.incoming.tar";
            await Shell.Run("scp", ["-q", .. SshOptions, archive, $"{target.Ssh}:{remote}"], cancellationToken);
            var quoted = Shell.QuoteForRemote(remote);
            await Shell.Run(
                "ssh", [.. SshOptions, target.Ssh, $"mkdir -p {folder} && tar -xf {quoted} -C {folder} && rm {quoted}"],
                cancellationToken);
        }
        finally
        {
            File.Delete(list);
            File.Delete(archive);
        }
    }

    /// <summary>
    /// A connection string the gate can use, and whatever keeps it open: nothing for a target on this
    /// machine, an ssh tunnel for a remote one. Disposing it closes the tunnel.
    /// </summary>
    public async Task<(string ConnectionString, IAsyncDisposable Lease)> Connect(
        string database, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(target.Password))
        {
            throw new InvalidOperationException(
                $"No password for publish target \"{target.Name}\". Set it with " +
                $"`dotnet user-secrets set \"Publish:Targets:{target.Name}:Password\" \"<password>\" --project Essenthos.Forge`, " +
                $"or Publish__Targets__{target.Name}__Password in the environment.");
        }

        var port = target.DatabasePort;
        IAsyncDisposable lease = NoLease.Instance;
        if (target.IsRemote)
        {
            port = FreePort();
            var tunnel = Shell.Start("ssh",
                [.. SshOptions, "-N", "-o", "ExitOnForwardFailure=yes",
                 "-L", $"{port}:127.0.0.1:{target.DatabasePort}", target.Ssh]);
            lease = new Tunnel(tunnel);
            await WaitForPort(port, tunnel, cancellationToken);
        }

        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = "127.0.0.1",
            Port = port,
            Database = database,
            Username = target.Superuser,
            Password = target.Password,
            CommandTimeout = 1800,
        };
        return (builder.ConnectionString, lease);
    }

    public static string Literal(string value) => $"'{value.Replace("'", "''")}'";

    public static string Identifier(string value) => $"\"{value.Replace("\"", "\"\"")}\"";

    /// <summary>
    /// Never a prompt: a publication that stops to ask for a passphrase in the middle of a swap is
    /// worse than one that refuses to start.
    /// </summary>
    private static readonly string[] SshOptions = ["-o", "BatchMode=yes", "-o", "ServerAliveInterval=30"];

    private static int FreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    private static async Task WaitForPort(int port, System.Diagnostics.Process tunnel, CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 100; attempt++)
        {
            if (tunnel.HasExited)
            {
                throw new InvalidOperationException(
                    $"The ssh tunnel exited with {tunnel.ExitCode}: {await tunnel.StandardError.ReadToEndAsync(cancellationToken)}");
            }

            try
            {
                using var client = new TcpClient();
                await client.ConnectAsync(IPAddress.Loopback, port, cancellationToken);
                return;
            }
            catch (SocketException)
            {
                await Task.Delay(100, cancellationToken);
            }
        }

        throw new TimeoutException($"The ssh tunnel never opened port {port}.");
    }

    private sealed class Tunnel(System.Diagnostics.Process process) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            NpgsqlConnection.ClearAllPools();
            if (!process.HasExited)
            {
                process.Kill();
                await process.WaitForExitAsync();
            }

            process.Dispose();
        }
    }

    private sealed class NoLease : IAsyncDisposable
    {
        public static readonly NoLease Instance = new();

        public ValueTask DisposeAsync()
        {
            NpgsqlConnection.ClearAllPools();
            return ValueTask.CompletedTask;
        }
    }
}
