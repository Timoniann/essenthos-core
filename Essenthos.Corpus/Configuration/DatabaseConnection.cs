using Npgsql;

namespace Essenthos.Core.Configuration;

/// <summary>
/// The connection string, assembled from a tracked half and an untracked one. The password is
/// never part of the tracked configuration; it arrives from user secrets in development and from
/// the environment in a deployment.
/// </summary>
internal static class DatabaseConnection
{
    public const string ConnectionStringKey = "Database:ConnectionString";
    public const string PasswordKey = "Database:Password";

    public static string Read(IConfiguration configuration)
    {
        var connectionString = configuration[ConnectionStringKey];
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                $"No database connection string. Set \"{ConnectionStringKey}\" in appsettings.json, " +
                $"or the environment variable Database__ConnectionString.");
        }

        var builder = new NpgsqlConnectionStringBuilder(connectionString);
        var password = configuration[PasswordKey];
        if (!string.IsNullOrEmpty(password))
        {
            builder.Password = password;
        }

        if (string.IsNullOrEmpty(builder.Password))
        {
            throw new InvalidOperationException(
                $"No database password. It is deliberately absent from appsettings.json; supply it with " +
                $"`dotnet user-secrets set \"{PasswordKey}\" \"<password>\"` in the Essenthos.Core project, " +
                $"or with the environment variable Database__Password.");
        }

        return builder.ConnectionString;
    }
}
