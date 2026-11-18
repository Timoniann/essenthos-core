using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Essenthos.Core.Configuration;
using Essenthos.Core.Corpus;
using Essenthos.Core.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;
using Npgsql;
using Xunit;

namespace Essenthos.Core.Tests;

/// <summary>
/// A scratch database with the migrations applied, one per test class. The shapes these tests are
/// about — a set of words on each side of a link, an empty side, a link crossing a verse boundary —
/// are claims about what Postgres will accept, so they are asked of Postgres. An in-memory provider
/// enforces none of the constraints under test and would answer yes to all of them.
///
/// <para>
/// **The names are per run, and that is the whole point.** The database used to be the constant
/// <c>essenthos_core_test</c>, dropped and recreated at the top of the fixture — so a second
/// <c>dotnet test</c> anywhere on the machine deleted the first one's database out from under it
/// mid-test. Five identical runs of an unchanged tree gave 601, 579, 601, 543, 601 passes, every
/// failure a database-backed class and every one of them 3D000 <em>database does not exist</em>.
/// Three agents hit it the same afternoon in three worktrees without knowing about each other.
/// </para>
///
/// <para>
/// A suite that fails a tenth of its tests on a coin flip teaches everyone to re-run rather than to
/// read the failure, which is how a real regression gets waved through — so this is a correctness
/// problem about every other test, not a convenience.
/// </para>
///
/// <para>
/// **Each class has a database of its own**, copied from a migrated template. When the classes
/// shared one they had to run one after another, and that queue was the suite: ten minutes of it
/// while everything else was done in two.
/// </para>
///
/// <para>
/// **The template outlives the run.** Migrating it took ten seconds of every run, and no class that
/// asks Postgres could start before it was done. It is kept on the server under a name that carries
/// a hash of what the migrations do, so a run copies the one an earlier run made with the same
/// migrations, and migrates a new one only when they changed.
/// </para>
/// </summary>
public sealed class WitnessDatabase : IAsyncLifetime
{
    /// <summary>
    /// Every database this fixture makes starts with this, then the id of the process that made it.
    /// The process id keeps a run's names its own, because the failure being prevented is two
    /// <em>live</em> runs sharing a name and no two live processes share an id; and it is what tells
    /// a later run which databases a crashed one left behind.
    /// </summary>
    private const string NamePrefix = "essenthos_core_test_";

    private static readonly string RunPrefix = $"{NamePrefix}{Environment.ProcessId}_";

    /// <summary>
    /// The kept templates start with this, then the hash of the migrations. Unlike
    /// <see cref="NamePrefix"/> it names no process: a template belongs to every run that has the same
    /// migrations.
    /// </summary>
    private const string TemplatePrefix = "essenthos_core_template_";

    /// <summary>A template no run has copied for this long is dropped by the next run.</summary>
    private static readonly TimeSpan TemplateUnusedFor = TimeSpan.FromDays(2);

    /// <summary>
    /// How many classes hold a database at once. Each keeps a few connections open, and the classes
    /// that load whole texts are bound by the database's cores rather than the runner's threads.
    /// </summary>
    private const int MostAtOnce = 16;

    /// <summary>
    /// The suite's own Postgres, the compose service <c>test-db</c>: durability off, and nothing on
    /// it a reader is waiting for. <c>Database__ConnectionString</c> points the suite elsewhere.
    /// </summary>
    public const string TestServer = "Host=localhost;Port=5439;Username=essenthos";

    private const string DefaultConnectionString = $"{TestServer};Database=essenthos_core_test";

    private static readonly SemaphoreSlim Slots = new(MostAtOnce);

    private static readonly Lazy<Task<(Server Server, string Template)>> Run = new(Prepare);

    private static int _made;

    private Server? _server;
    private string _name = string.Empty;
    private string _connectionString = string.Empty;
    private Task<object>? _shared;

    /// <summary>
    /// Copies the template for this class: a fraction of a second, where migrating takes seconds.
    /// </summary>
    public async Task InitializeAsync()
    {
        (_server, var template) = await Run.Value;
        await Slots.WaitAsync();
        try
        {
            var name = $"{RunPrefix}{Interlocked.Increment(ref _made)}";
            await _server.Execute($"CREATE DATABASE \"{name}\" TEMPLATE \"{template}\" STRATEGY WAL_LOG");
            _name = name;
            _connectionString = _server.Scratch(name);
        }
        catch
        {
            Slots.Release();
            throw;
        }
    }

    /// <summary>
    /// Once per run: drops what runs no longer alive left behind, has what this run makes go when the
    /// process does, and finds the template for these migrations, migrating it if no run has.
    /// </summary>
    private static async Task<(Server Server, string Template)> Prepare()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [DatabaseConnection.ConnectionStringKey] = DefaultConnectionString,
                [DatabaseConnection.CommandTimeoutKey] = "1800",
            })
            .AddUserSecrets(typeof(WitnessDatabase).Assembly)
            .AddEnvironmentVariables()
            .Build();

        var server = new Server(DatabaseConnection.Read(configuration));
        await server.DropLeftovers();
        AppDomain.CurrentDomain.ProcessExit += (_, _) => server.DropRun();

        var template = await server.Template(TemplatePrefix + MigrationsHash(), Migrate);
        return (server, template);
    }

    private static async Task Migrate(string connectionString)
    {
        await using (var context = NewContext(connectionString))
        {
            await context.Database.MigrateAsync();
            await context.Database.ExecuteSqlRawAsync(ExternalReferences.Schema);
            await context.Database.ExecuteSqlRawAsync(NoAutovacuumSql);
        }

        // Postgres refuses to rename or copy a database anybody is connected to, and the pool is somebody.
        NpgsqlConnection.ClearPool(new NpgsqlConnection(connectionString));
    }

    /// <summary>
    /// What the migrations do, as the SQL each one generates, and what <see cref="Migrate"/> adds after
    /// them: two runs with the same hash build the same template. The SQL rather than the migration
    /// files, because a migration may build its SQL from code elsewhere (<c>link_method_bit</c> is
    /// written from the <c>LinkMethod</c> enum). Generated without a model, so nothing connects
    /// and no migration's target model is built: about a second, where the full script takes six.
    /// </summary>
    private static string MigrationsHash()
    {
        using var context = NewContext("Host=localhost;Database=none");
        var migrations = context.GetService<IMigrationsAssembly>();
        var generator = context.GetService<IMigrationsSqlGenerator>();
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var (id, type) in migrations.Migrations.OrderBy(m => m.Key, StringComparer.Ordinal))
        {
            hash.AppendData(Encoding.UTF8.GetBytes(id));
            foreach (var command in generator.Generate(migrations.CreateMigration(type, context.Database.ProviderName!).UpOperations))
            {
                hash.AppendData(Encoding.UTF8.GetBytes(command.CommandText));
            }
        }

        hash.AppendData(Encoding.UTF8.GetBytes(ExternalReferences.Schema + NoAutovacuumSql));
        return Convert.ToHexStringLower(hash.GetHashAndReset())[..16];
    }

    /// <summary>
    /// Statistics here change only when a test gathers them. Left to autovacuum they described
    /// whatever the table held on its last visit — the previous test's few dozen links, or part of
    /// this load — and a planner told that link words are nearly all on one side nests a loop over
    /// half a million links in a query that takes a second. That is why the whole-corpus classes
    /// failed one full run in four and never alone: a lone run finishes before autovacuum has
    /// visited a database that new. The setting is on the template's tables, and a copy keeps it.
    /// </summary>
    private const string NoAutovacuumSql =
        """
        DO $$
        DECLARE t record;
        BEGIN
            FOR t IN SELECT schemaname, tablename FROM pg_tables WHERE schemaname = 'public' LOOP
                EXECUTE format('ALTER TABLE %I.%I SET (autovacuum_enabled = false)', t.schemaname, t.tablename);
            END LOOP;
        END $$
        """;

    /// <summary>
    /// Empties the corpus and forgets what the planner knew about it, for the classes that load
    /// whole texts, either side of their test. <c>TRUNCATE</c> keeps every column's statistics, so
    /// without the second statement the next test plans against the texts and ids of this one.
    /// </summary>
    public async Task Empty(TimeSpan timeout)
    {
        await using var connection = NewConnection();
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
            TRUNCATE text, strong_entry CASCADE;
            SELECT count(pg_clear_attribute_stats(schemaname, tablename, attname, inherited))
            FROM pg_stats WHERE schemaname = 'public';
            """,
            connection) { CommandTimeout = (int)timeout.TotalSeconds };
        await command.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// How every session on a scratch database is set up.
    ///
    /// <para>
    /// A test class whose constructor throws is never disposed, so the transaction it opened stays
    /// open, holding the rows it had written, and the next test to write the same key waits on it
    /// until its own command times out — and leaves its own transaction open in turn. When every
    /// class shared one database, one duplicate key in a constructor became 56 thirty-second
    /// timeouts in a row, 28 of a 35-minute run. Now the chain cannot leave its class, whose
    /// database is dropped with every session on it when the class ends, and an abandoned
    /// transaction is ended after two minutes idle besides. Not sooner: with the whole suite running
    /// at once a loader's own transaction sat idle past 20 seconds while it parsed between two
    /// writes, and was killed.
    /// </para>
    ///
    /// <para>
    /// Commits are not waited on: a database dropped at the end of the run has nothing a crash could
    /// lose.
    /// </para>
    /// </summary>
    private const string ScratchSessionOptions =
        "-c idle_in_transaction_session_timeout=2min -c synchronous_commit=off";

    /// <summary>
    /// Drops the class's database and gives its turn to the next class. Without this every class
    /// would leave one behind, and a machine that has run the suite a few times would have a Postgres
    /// with hundreds of databases in it.
    /// </summary>
    public async Task DisposeAsync()
    {
        if (_name.Length == 0)
        {
            return;
        }

        if (_shared is { IsCompletedSuccessfully: true, Result: IDisposable shared })
        {
            shared.Dispose();
        }

        try
        {
            // The pool still holds open connections to the database about to be dropped.
            NpgsqlConnection.ClearPool(new NpgsqlConnection(_connectionString));
            await _server!.Drop(_name);
        }
        finally
        {
            Slots.Release();
        }
    }

    public AppDbContext NewContext() => NewContext(_connectionString);

    /// <summary>
    /// What a class makes once and every one of its tests reads: a load its tests only look at is
    /// loaded for the first test and read by the rest, rather than loaded again for each. The class's
    /// database is its own and its tests run one after another, so nothing else writes in between.
    /// It is disposed with the database.
    /// </summary>
    public async Task<T> Shared<T>(Func<Task<T>> make) where T : class
    {
        _shared ??= Make();
        return (T)await _shared;

        async Task<object> Make() => await make();
    }

    private static AppDbContext NewContext(string connectionString) =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(connectionString).Options);

    /// <summary>The raw connection, for asking the database what it actually stored.</summary>
    public NpgsqlConnection NewConnection() => new(_connectionString);

    /// <summary>
    /// The configured Postgres, reached through its maintenance database to make and drop the
    /// scratch ones.
    ///
    /// The host, port and credentials are taken from configuration — CI supplies its own, and so
    /// does anyone running against a different Postgres — but the database name is replaced rather
    /// than honoured. This fixture drops what it makes, and a name somebody typed by hand is exactly
    /// the one they would mind losing.
    /// </summary>
    private sealed class Server(string configured)
    {
        private readonly string _maintenance =
            new NpgsqlConnectionStringBuilder(configured) { Database = "postgres" }.ConnectionString;

        public string Scratch(string name)
        {
            var builder = new NpgsqlConnectionStringBuilder(configured) { Database = name };
            builder.Options = string.IsNullOrEmpty(builder.Options)
                ? ScratchSessionOptions
                : $"{builder.Options} {ScratchSessionOptions}";
            return builder.ConnectionString;
        }

        public async Task Execute(string sql)
        {
            await using var connection = new NpgsqlConnection(_maintenance);
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand(sql, connection);
            await command.ExecuteNonQueryAsync();
        }

        /// <summary>Forced, so that a connection some test left open cannot keep it.</summary>
        public Task Drop(string name) => Execute($"DROP DATABASE IF EXISTS \"{name}\" WITH (FORCE)");

        /// <summary>
        /// The kept template called <paramref name="name"/>, migrated first if no run has made it.
        ///
        /// <para>
        /// Runs that start together take turns on a lock named after it, so one migrates and the rest
        /// copy what it made. It is migrated under a name of this run's and renamed when it is whole, so
        /// no run copies a half-migrated template, and one a crashed run left half-made is among that
        /// run's leftovers. Connections to it are refused: Postgres will not copy a database somebody is
        /// connected to, and one session left open on it would stop every run on the machine.
        /// </para>
        ///
        /// <para>
        /// Each run dates the template it copies, and drops the others no run has copied for
        /// <see cref="TemplateUnusedFor"/>: one per change to the migrations would pile up otherwise.
        /// </para>
        /// </summary>
        public async Task<string> Template(string name, Func<string, Task> migrate)
        {
            await using var connection = new NpgsqlConnection(
                new NpgsqlConnectionStringBuilder(_maintenance) { Pooling = false }.ConnectionString);
            await connection.OpenAsync();
            await Scalar(connection, "SELECT pg_advisory_lock(hashtext($1))", name);
            if (await Scalar(connection, "SELECT 1 FROM pg_database WHERE datname = $1", name) is null)
            {
                var building = $"{RunPrefix}template";
                await migrate(Scratch(building));
                await Scalar(connection, $"ALTER DATABASE \"{building}\" WITH ALLOW_CONNECTIONS false");
                await Scalar(connection, $"ALTER DATABASE \"{building}\" RENAME TO \"{name}\"");
            }

            await Scalar(connection, $"COMMENT ON DATABASE \"{name}\" IS '{DateTime.UtcNow:yyyy-MM-dd}'");
            await Scalar(connection, "SELECT pg_advisory_unlock(hashtext($1))", name);

            var unused = new List<string>();
            await using (var command = new NpgsqlCommand(
                             """
                             SELECT datname FROM pg_database
                             WHERE starts_with(datname, $1) AND datname <> $2
                               AND coalesce(shobj_description(oid, 'pg_database'), '') < $3
                             """,
                             connection))
            {
                command.Parameters.AddWithValue(TemplatePrefix);
                command.Parameters.AddWithValue(name);
                command.Parameters.AddWithValue($"{DateTime.UtcNow - TemplateUnusedFor:yyyy-MM-dd}");
                await using var reader = await command.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    unused.Add(reader.GetString(0));
                }
            }

            // A run copying one of them right now holds it; it is left for a later run, not waited for.
            await Scalar(connection, "SET lock_timeout = '1s'");
            foreach (var old in unused)
            {
                try
                {
                    await Scalar(connection, $"DROP DATABASE IF EXISTS \"{old}\" WITH (FORCE)");
                }
                catch (PostgresException)
                {
                }
            }

            return name;
        }

        private static async Task<object?> Scalar(NpgsqlConnection connection, string sql, string? parameter = null)
        {
            await using var command = new NpgsqlCommand(sql, connection);
            if (parameter is not null)
            {
                command.Parameters.AddWithValue(parameter);
            }

            return await command.ExecuteScalarAsync();
        }

        /// <summary>
        /// Every scratch database of a run that is no longer alive, and this process's own from a
        /// crashed run whose id it inherited. A live run's databases are its own business.
        /// </summary>
        public async Task DropLeftovers()
        {
            foreach (var name in await Names())
            {
                var processId = int.Parse(string.Concat(name[NamePrefix.Length..].TakeWhile(char.IsAsciiDigit)));
                if (processId == Environment.ProcessId || !IsRunning(processId))
                {
                    await Drop(name);
                }
            }
        }

        /// <summary>
        /// Whatever a class failed to drop, and a template this run did not finish migrating, as the
        /// process exits. A failure here is left to the next run's <see cref="DropLeftovers"/> rather
        /// than thrown into the exit.
        /// </summary>
        public void DropRun()
        {
            try
            {
                NpgsqlConnection.ClearAllPools();
                foreach (var name in Names().GetAwaiter().GetResult().Where(n => n.StartsWith(RunPrefix)))
                {
                    Drop(name).GetAwaiter().GetResult();
                }
            }
            catch (NpgsqlException)
            {
            }
        }

        /// <summary>
        /// The databases named as a run names them: the prefix, a process id and a suffix, or no suffix
        /// as a run named its one database before each class had its own.
        /// </summary>
        private async Task<List<string>> Names()
        {
            await using var connection = new NpgsqlConnection(_maintenance);
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand(
                "SELECT datname FROM pg_database WHERE datname ~ ('^' || $1 || '[0-9]+(_|$)')", connection);
            command.Parameters.AddWithValue(NamePrefix);

            var names = new List<string>();
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                names.Add(reader.GetString(0));
            }

            return names;
        }

        private static bool IsRunning(int processId)
        {
            try
            {
                using var process = Process.GetProcessById(processId);
                return true;
            }
            catch (ArgumentException)
            {
                return false;
            }
        }
    }
}

/// <summary>
/// The classes that ask Postgres. The name is what they carry; <see cref="DatabaseClassCollections"/>
/// makes each of them a collection of its own, so each gets a database of its own.
/// </summary>
[CollectionDefinition(Name)]
public sealed class WitnessDatabaseCollection : ICollectionFixture<WitnessDatabase>
{
    public const string Name = "witness-database";
}
