using Essenthos.Core.Configuration;
using Essenthos.Core.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Npgsql;
using Xunit;

namespace Essenthos.Core.Tests;

/// <summary>
/// A scratch database with the migrations applied. The shapes these tests are about — a set of
/// words on each side of a link, an empty side, a link crossing a verse boundary — are claims about
/// what Postgres will accept, so they are asked of Postgres. An in-memory provider enforces none of
/// the constraints under test and would answer yes to all of them.
///
/// <para>
/// **The name is per run, and that is the whole point.** It used to be the constant
/// <c>essenthos_core_test</c>, dropped and recreated at the top of the fixture — so a second
/// <c>dotnet test</c> anywhere on the machine deleted the first one's database out from under it
/// mid-test. Five identical runs of an unchanged tree gave 601, 579, 601, 543, 601 passes, every
/// failure a database-backed class and every one of them 3D000 <em>database does not exist</em>.
/// Three agents hit it the same afternoon in three worktrees without knowing about each other.
/// PRB-0154.
/// </para>
///
/// <para>
/// A suite that fails a tenth of its tests on a coin flip teaches everyone to re-run rather than to
/// read the failure, which is how a real regression gets waved through — so this is a correctness
/// problem about every other test, not a convenience.
/// </para>
/// </summary>
public sealed class WitnessDatabase : IAsyncLifetime
{
    /// <summary>
    /// The scratch database this run owns. The process id is what makes it unique, because the
    /// failure being prevented is two <em>live</em> runs sharing a name and no two live processes
    /// share an id. A crashed run leaves a stray database behind and a later process may inherit
    /// its id, which is why <see cref="InitializeAsync"/> still deletes before it migrates.
    /// </summary>
    private static readonly string DatabaseName = $"essenthos_core_test_{Environment.ProcessId}";

    private const string DefaultConnectionString =
        "Host=localhost;Port=5435;Database=essenthos_core_test;Username=essenthos";

    private string _connectionString = string.Empty;

    public async Task InitializeAsync()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [DatabaseConnection.ConnectionStringKey] = DefaultConnectionString,
            })
            .AddUserSecrets(typeof(WitnessDatabase).Assembly)
            .AddEnvironmentVariables()
            .Build();

        _connectionString = Scratch(DatabaseConnection.Read(configuration));

        await using var context = NewContext();
        await context.Database.EnsureDeletedAsync();
        await context.Database.MigrateAsync();
    }

    /// <summary>
    /// The configured connection pointed at this run's own database.
    ///
    /// The host, port and credentials are taken from configuration — CI supplies its own, and so
    /// does anyone running against a different Postgres — but the database name is replaced rather
    /// than honoured. This fixture drops what it is pointed at, and a name somebody typed by hand
    /// is exactly the one they would mind losing.
    /// </summary>
    private static string Scratch(string connectionString) =>
        new NpgsqlConnectionStringBuilder(connectionString) { Database = DatabaseName }
            .ConnectionString;

    /// <summary>
    /// Drops the run's database. Without this every run would leave one behind, and a machine that
    /// has run the suite a few hundred times is a machine whose Postgres has a few hundred
    /// databases in it.
    /// </summary>
    public async Task DisposeAsync()
    {
        if (_connectionString.Length == 0)
        {
            return;
        }

        // The pool still holds open connections to the database about to be dropped, and Postgres
        // refuses to drop one anybody is connected to.
        NpgsqlConnection.ClearAllPools();

        await using var context = NewContext();
        await context.Database.EnsureDeletedAsync();
    }

    public AppDbContext NewContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(_connectionString)
            .Options;
        return new AppDbContext(options);
    }

    /// <summary>The raw connection, for asking the database what it actually stored.</summary>
    public NpgsqlConnection NewConnection() => new(_connectionString);
}

[CollectionDefinition(Name)]
public sealed class WitnessDatabaseCollection : ICollectionFixture<WitnessDatabase>
{
    public const string Name = "witness-database";
}
