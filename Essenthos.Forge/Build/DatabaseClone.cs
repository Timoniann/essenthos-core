using System.Diagnostics;
using Essenthos.Core.Products;
using Npgsql;

namespace Essenthos.Core.Build;

/// <summary>
/// A served database copied whole into another of the same server, with the store's <c>build/served</c> state of it
/// copied under the new name, so <c>build --apply</c> on the copy replaces only what moved since the original was built or
/// last applied. The daily path (PLN-1576 step 5): the seed, a copy of the last database built for live taken before it
/// was swapped in, is cloned into the next database, which takes the moved products; before that one is swapped in, it is
/// cloned into the seed again.
/// </summary>
internal static class DatabaseClone
{
    /// <summary>
    /// <c>CREATE DATABASE &lt;to&gt; TEMPLATE &lt;from&gt; STRATEGY FILE_COPY</c> (a copy of the files after a checkpoint, a
    /// minute or two for the whole corpus) and <c>build/served/&lt;to&gt;</c> from <c>build/served/&lt;from&gt;</c>. Postgres
    /// refuses a template anybody is connected to, so the original must be one nothing serves; neither may be one
    /// <see cref="ServingBuild.Untouchable"/> names.
    /// </summary>
    public static async Task<TimeSpan> Run(string connectionString, ProductStore store, string from, string to, bool replace, ILogger logger,
        CancellationToken cancellationToken)
    {
        ServingBuild.Refuse(from);
        ServingBuild.Refuse(to);
        if (string.Equals(from, to, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"A database is not cloned into itself ({from}).");
        }

        if (ServedState.Read(store, from) is null)
        {
            throw new InvalidOperationException(
                $"No build/served/{from} under {store.ResourcesPath}: only a database build --into or --apply made from this store can be cloned and applied.");
        }

        var clock = Stopwatch.StartNew();
        var admin = new NpgsqlConnectionStringBuilder(connectionString) { Database = "postgres", Pooling = false }.ConnectionString;
        await using (var connection = new NpgsqlConnection(admin))
        {
            await connection.OpenAsync(cancellationToken);
            if (!await Exists(connection, from, cancellationToken))
            {
                throw new InvalidOperationException($"{from} does not exist on this server.");
            }

            await using (var sessions = new NpgsqlCommand("SELECT count(*) FROM pg_stat_activity WHERE datname = @name", connection))
            {
                sessions.Parameters.AddWithValue("name", from);
                if (Convert.ToInt64(await sessions.ExecuteScalarAsync(cancellationToken)) is var connected and > 0)
                {
                    throw new InvalidOperationException($"{from} has {connected} connection(s); a database is copied only while nothing is connected to it.");
                }
            }

            if (await Exists(connection, to, cancellationToken))
            {
                if (!replace)
                {
                    throw new InvalidOperationException($"{to} already exists; clone into a new name, or pass --replace to drop it first.");
                }

                await Execute(connection, $"DROP DATABASE \"{to}\" WITH (FORCE)", cancellationToken);
            }

            await Execute(connection, $"CREATE DATABASE \"{to}\" TEMPLATE \"{from}\" STRATEGY FILE_COPY", cancellationToken);
        }

        ServedState.Copy(store, from, to);
        logger.LogInformation("{To} cloned from {From} with build/served/{To}, in {Seconds:0.0} s", to, from, to, clock.Elapsed.TotalSeconds);
        return clock.Elapsed;
    }

    private static async Task<bool> Exists(NpgsqlConnection connection, string database, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("SELECT 1 FROM pg_database WHERE datname = @name", connection);
        command.Parameters.AddWithValue("name", database);
        return await command.ExecuteScalarAsync(cancellationToken) is not null;
    }

    private static async Task Execute(NpgsqlConnection connection, string sql, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(sql, connection) { CommandTimeout = 0 };
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
