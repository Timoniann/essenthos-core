using Npgsql;

namespace Essenthos.Core.Configuration;

/// <summary>
/// A connection string, assembled from a tracked half and an untracked one. The password is never
/// part of the tracked configuration; it arrives from user secrets in development and from the
/// environment in a deployment.
///
/// Two databases are read this way: the corpus under <c>Database</c>, and the accounts database the
/// API owns under <c>Accounts</c>. Separate keys because they are separate roles with separate
/// passwords — the corpus's can only read.
/// </summary>
internal static class DatabaseConnection
{
    public const string ConnectionStringKey = "Database:ConnectionString";
    public const string PasswordKey = "Database:Password";

    public const string AccountsConnectionStringKey = "Accounts:ConnectionString";
    public const string AccountsPasswordKey = "Accounts:Password";

    public static string Read(IConfiguration configuration) =>
        Read(configuration, ConnectionStringKey, PasswordKey);

    public static string ReadAccounts(IConfiguration configuration) =>
        Read(configuration, AccountsConnectionStringKey, AccountsPasswordKey);

    private static string Read(IConfiguration configuration, string connectionStringKey, string passwordKey)
    {
        var connectionString = configuration[connectionStringKey];
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                $"No database connection string. Set \"{connectionStringKey}\" in appsettings.json, " +
                $"or the environment variable {connectionStringKey.Replace(":", "__")}.");
        }

        var builder = new NpgsqlConnectionStringBuilder(connectionString);
        var password = configuration[passwordKey];
        if (!string.IsNullOrEmpty(password))
        {
            builder.Password = password;
        }

        if (string.IsNullOrEmpty(builder.Password))
        {
            throw new InvalidOperationException(
                $"No database password. It is deliberately absent from appsettings.json; supply it with " +
                $"`dotnet user-secrets set \"{passwordKey}\" \"<password>\"` in the Essenthos.Api project, " +
                $"or with the environment variable {passwordKey.Replace(":", "__")}.");
        }

        return builder.ConnectionString;
    }
}
