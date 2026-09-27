using Essenthos.Core.Configuration;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Npgsql;
using Xunit;

namespace Essenthos.Core.Tests;

public class DatabaseConnectionTests
{
    [Theory]
    [InlineData("Host=localhost;Username=essenthos", GssEncryptionMode.Disable)]
    [InlineData("Host=localhost;Username=essenthos;GSS Encryption Mode=Prefer", GssEncryptionMode.Prefer)]
    [InlineData("Host=localhost;Username=essenthos;GssEncryptionMode=Require", GssEncryptionMode.Require)]
    public void KerberosEncryptionIsOffUnlessTheConnectionStringAsksForIt(string connectionString, GssEncryptionMode expected)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [DatabaseConnection.ConnectionStringKey] = connectionString,
                [DatabaseConnection.PasswordKey] = "not-a-secret",
            })
            .Build();

        new NpgsqlConnectionStringBuilder(DatabaseConnection.Read(configuration)).GssEncryptionMode.Should().Be(expected);
    }
}
