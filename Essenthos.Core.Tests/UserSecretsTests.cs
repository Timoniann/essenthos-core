using Essenthos.Core.Configuration;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.Memory;
using Xunit;

namespace Essenthos.Core.Tests;

/// <summary>
/// Where user secrets sit among the builder's sources. A secret is a developer's local default, so
/// the environment and the command line must still be able to override it; the failure this guards
/// is a CORS origin set for a preview being refused because a secret named another.
/// </summary>
public sealed class UserSecretsTests
{
    private const string SecretValue = "from-secrets";
    private const string EnvironmentValue = "from-environment";
    private const string CommandLineValue = "from-command-line";

    private readonly string _key = $"UserSecretsTests{Guid.NewGuid():N}";

    [Fact]
    public void An_environment_variable_overrides_a_user_secret()
    {
        Environment.SetEnvironmentVariable(_key, EnvironmentValue);
        try
        {
            var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions { EnvironmentName = "Production" });
            UserSecrets.InsertBelowEnvironment(builder.Configuration, Secret());

            builder.Configuration[_key].Should().Be(EnvironmentValue);
        }
        finally
        {
            Environment.SetEnvironmentVariable(_key, null);
        }
    }

    [Fact]
    public void A_command_line_argument_overrides_a_user_secret()
    {
        var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions
        {
            EnvironmentName = "Production",
            Args = [$"--{_key}={CommandLineValue}"],
        });
        UserSecrets.InsertBelowEnvironment(builder.Configuration, Secret());

        builder.Configuration[_key].Should().Be(CommandLineValue);
    }

    [Fact]
    public void A_user_secret_applies_when_nothing_overrides_it()
    {
        var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions { EnvironmentName = "Production" });
        UserSecrets.InsertBelowEnvironment(builder.Configuration, Secret());

        builder.Configuration[_key].Should().Be(SecretValue);
    }

    [Fact]
    public void Secrets_the_default_builder_already_reads_are_not_added_twice()
    {
        var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions
        {
            EnvironmentName = "Development",
            ApplicationName = typeof(Program).Assembly.GetName().Name,
        });
        var before = ((IConfigurationBuilder)builder.Configuration).Sources.Count;

        UserSecrets.AddBelowEnvironment(builder.Configuration, typeof(Program).Assembly);

        ((IConfigurationBuilder)builder.Configuration).Sources.Should().HaveCount(before);
    }

    private MemoryConfigurationSource Secret() =>
        new() { InitialData = new Dictionary<string, string?> { [_key] = SecretValue } };
}
