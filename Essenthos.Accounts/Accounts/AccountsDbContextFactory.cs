using Essenthos.Core.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Essenthos.Core.Accounts;

/// <summary>
/// The context <c>dotnet ef</c> builds for this project's migrations, reading the connection the way
/// the API does, user secret for the password included, so there is one answer to where the
/// accounts database is.
/// </summary>
internal sealed class AccountsDbContextFactory : IDesignTimeDbContextFactory<AccountsDbContext>
{
    public AccountsDbContext CreateDbContext(string[] args)
    {
        var configuration = new ConfigurationBuilder()
            .AddUserSecrets(typeof(AccountsDbContextFactory).Assembly, optional: true)
            .AddEnvironmentVariables()
            .Build();

        return new AccountsDbContext(new DbContextOptionsBuilder<AccountsDbContext>()
            .UseNpgsql(DatabaseConnection.ReadAccounts(configuration))
            .Options);
    }
}
