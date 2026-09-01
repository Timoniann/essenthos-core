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
/// </summary>
public sealed class WitnessDatabase : IAsyncLifetime
{
    private const string DatabaseName = "essenthos_core_test";

    private const string DefaultConnectionString =
        $"Host=localhost;Port=5435;Database={DatabaseName};Username=essenthos";

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

        _connectionString = DatabaseConnection.Read(configuration);

        await using var context = NewContext();
        await context.Database.EnsureDeletedAsync();
        await context.Database.MigrateAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

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
