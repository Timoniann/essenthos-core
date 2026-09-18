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
