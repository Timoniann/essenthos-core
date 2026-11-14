using Essenthos.Core.Accounts;
using Essenthos.Core.Configuration;
using Essenthos.Core.Pages;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Xunit;

namespace Essenthos.Core.Tests.Platform;

/// <summary>
/// A sitemap list is dated by when what it lists last changed: the same addresses keep their date across
/// processes and releases, other addresses are dated anew, and a store that cannot be reached gives no date.
/// </summary>
public sealed class SitemapDatesTests : IAsyncLifetime
{
    private static readonly string DatabaseName = $"essenthos_app_sitemap_dates_test_{Environment.ProcessId}";

    private string _connectionString = string.Empty;

    public async Task InitializeAsync()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [DatabaseConnection.ConnectionStringKey] = $"{WitnessDatabase.TestServer};Database=postgres",
            })
            .AddUserSecrets(typeof(SitemapDatesTests).Assembly)
            .AddEnvironmentVariables()
            .Build();

        _connectionString = new NpgsqlConnectionStringBuilder(DatabaseConnection.Read(configuration)) { Database = DatabaseName }
            .ConnectionString;

        await using var db = NewContext(_connectionString);
        await db.Database.EnsureDeletedAsync();
        await db.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        if (_connectionString.Length == 0)
        {
            return;
        }

        NpgsqlConnection.ClearAllPools();
        await using var db = NewContext(_connectionString);
        await db.Database.EnsureDeletedAsync();
    }

    private static AccountsDbContext NewContext(string connectionString) =>
        new(new DbContextOptionsBuilder<AccountsDbContext>().UseNpgsql(connectionString).Options);

    private static SitemapDates Dates(string connectionString) =>
        new(new ServiceCollection().AddScoped(_ => NewContext(connectionString)).BuildServiceProvider()
            .GetRequiredService<IServiceScopeFactory>(), NullLogger<SitemapDates>.Instance);

    [Fact]
    public async Task AListKeepsItsDateWhileItsAddressesDoAndIsDatedAnewWhenTheyChange()
    {
        var john = SitemapDates.Hash(["/verse/john/1/1", "/verse/john/1/2"]);
        var first = await Dates(_connectionString).ChangedAt("verses-john", john, default);
        first.Should().NotBeNull();

        await Task.Delay(20);
        (await Dates(_connectionString).ChangedAt("verses-john", john, default))
            .Should().Be(first, "another process, or the next release, sees the same list");

        var changed = SitemapDates.Hash(["/verse/john/1/1", "/verse/john/1/2", "/verse/john/1/3"]);
        changed.Should().NotBe(john);
        (await Dates(_connectionString).ChangedAt("verses-john", changed, default)).Should().BeAfter(first!.Value);
        (await Dates(_connectionString).ChangedAt("verses-mark", john, default)).Should().NotBeNull("each list is dated on its own");
    }

    [Fact]
    public async Task AStoreThatCannotBeReachedGivesNoDate()
    {
        var nowhere = new NpgsqlConnectionStringBuilder(_connectionString) { Database = DatabaseName + "_absent" }.ConnectionString;

        (await Dates(nowhere).ChangedAt("verses-john", "abc", default)).Should().BeNull();
    }
}
