using Essenthos.Core.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace Essenthos.Core.Database;

/// <summary>
/// The context <c>dotnet ef</c> builds, which is not the one the API runs.
///
/// A migration that only adds a table finishes instantly and needs nothing from this. A migration
/// that moves data does not: <c>LinkClaims</c> writes 4.6 million rows in one statement, and under
/// the thirty seconds Npgsql allows by default it failed halfway with a timeout that reads like a
/// dropped connection. Raising the API's own timeout to suit it would be the wrong fix — a slow
/// query while serving is a fault, and thirty seconds is how it announces itself.
///
/// <para>
/// So migrations get their own context with room to work, and the running service keeps its short
/// leash. The connection string is read the same way <c>Program.cs</c> reads it, including the user
/// secret that holds the password, so there is one answer to where the database is.
/// </para>
/// </summary>
internal sealed class MigrationDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    /// <summary>
    /// Half an hour. Long enough for a data migration over every link in the corpus, short enough
    /// that a migration which has genuinely hung still ends.
    /// </summary>
    private const int Minutes = 30;

    public AppDbContext CreateDbContext(string[] args)
    {
        var configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: true)
            .AddUserSecrets<Program>(optional: true)
            .AddEnvironmentVariables()
            .Build();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(
                DatabaseConnection.Read(configuration),
                npgsql => npgsql.CommandTimeout((int)TimeSpan.FromMinutes(Minutes).TotalSeconds))
            .UseSnakeCaseNamingConvention()
            .Options;

        return new AppDbContext(options);
    }
}
