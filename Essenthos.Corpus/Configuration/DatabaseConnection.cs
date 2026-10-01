using System.Data.Common;
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

    /// <summary>
    /// How long any one command may run, in seconds, for every connection made from the string.
    /// The Forge sets it, because a statement over the whole corpus outlasts Npgsql's thirty
    /// seconds and a timeout set command by command is one somebody forgets; the API leaves it
    /// unset and keeps the default, with its own limit on the server.
    /// </summary>
    public const string CommandTimeoutKey = "Database:CommandTimeoutSeconds";

    public const string AccountsConnectionStringKey = "Accounts:ConnectionString";
    public const string AccountsPasswordKey = "Accounts:Password";

    private static readonly string[] GssEncryptionModeKeywords = ["GSS Encryption Mode", "GssEncryptionMode"];

    private static readonly string[] CommandTimeoutKeywords = ["Command Timeout", "CommandTimeout"];

    public static string Read(IConfiguration configuration) =>
        Read(configuration, ConnectionStringKey, PasswordKey, CommandTimeoutKey);

    public static string ReadAccounts(IConfiguration configuration) =>
        Read(configuration, AccountsConnectionStringKey, AccountsPasswordKey, null);

    private static string Read(
        IConfiguration configuration,
        string connectionStringKey,
        string passwordKey,
        string? commandTimeoutKey)
    {
        var connectionString = configuration[connectionStringKey];
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                $"No database connection string. Set \"{connectionStringKey}\" in appsettings.json, " +
                $"or the environment variable {connectionStringKey.Replace(":", "__")}.");
        }

        var builder = new NpgsqlConnectionStringBuilder(connectionString);

        // Npgsql asks the server for Kerberos session encryption first unless told not to, and on a
        // Linux image without the Kerberos library that logs a failure to load it. Nothing here uses
        // Kerberos; a connection string that names the setting keeps its own.
        var named = new DbConnectionStringBuilder { ConnectionString = connectionString };
        if (!GssEncryptionModeKeywords.Any(named.ContainsKey))
        {
            builder.GssEncryptionMode = GssEncryptionMode.Disable;
        }

        // A connection string that names its own timeout keeps it.
        if (commandTimeoutKey is not null
            && !CommandTimeoutKeywords.Any(named.ContainsKey)
            && int.TryParse(configuration[commandTimeoutKey], out var seconds))
        {
            builder.CommandTimeout = seconds;
        }

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
