using System.Reflection;
using Microsoft.Extensions.Configuration.EnvironmentVariables;
using Microsoft.Extensions.Configuration.Json;

namespace Essenthos.Core.Configuration;

/// <summary>
/// User secrets at the level the default host gives them: above the appsettings files, below the
/// environment and the command line. A provider added later wins, so appending them after the
/// builder has run puts a developer's <c>secrets.json</c> over every deliberate override — a CORS
/// origin or a password set for one run is silently ignored on any machine that once ran
/// <c>dotnet user-secrets set</c>.
///
/// <para>
/// The default host only reads them in Development. They are read whatever the environment here,
/// because the database password lives there and the launch profile that sets the environment has
/// been the thing that was wrong; on a deployed machine there is no secrets file and nothing moves.
/// </para>
/// </summary>
internal static class UserSecrets
{
    private const string FileName = "secrets.json";

    public static void AddBelowEnvironment(IConfigurationBuilder configuration, Assembly assembly)
    {
        if (configuration.Sources.Any(source => source is JsonConfigurationSource { Path: FileName }))
        {
            return;
        }

        var secrets = new ConfigurationBuilder().AddUserSecrets(assembly, optional: true).Sources;
        foreach (var source in secrets)
        {
            InsertBelowEnvironment(configuration, source);
        }
    }

    /// <summary>
    /// Places <paramref name="source"/> directly beneath the application's own environment-variable
    /// provider, the unprefixed one, which the command-line provider follows. The prefixed
    /// <c>ASPNETCORE_</c> and <c>DOTNET_</c> providers near the start are host settings and do not
    /// mark the level.
    /// </summary>
    public static void InsertBelowEnvironment(IConfigurationBuilder configuration, IConfigurationSource source)
    {
        var sources = configuration.Sources;
        var environment = -1;
        for (var index = sources.Count - 1; index >= 0; index--)
        {
            if (sources[index] is EnvironmentVariablesConfigurationSource { Prefix: null or "" })
            {
                environment = index;
                break;
            }
        }

        if (environment < 0)
        {
            configuration.Add(source);
            return;
        }

        sources.Insert(environment, source);
    }
}
