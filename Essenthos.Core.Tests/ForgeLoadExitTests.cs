using Essenthos.Core.Loading;
using Essenthos.Core.Verbs;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting.Internal;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Essenthos.Core.Tests;

/// <summary>A failed load stays failed when the command finishes its database maintenance.</summary>
[Collection(WitnessDatabaseCollection.Name)]
public sealed class ForgeLoadExitTests(WitnessDatabase database) : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("forge-load-exit-").FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Theory]
    [InlineData("no-such-step", false)]
    [InlineData("relations", true)]
    public async Task ARejectedOrThrowingLoadReturnsFailureAfterMaintenance(string from, bool failedStep)
    {
        var resources = Directory.CreateDirectory(Path.Combine(_root, "Resources")).FullName;
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Dataset:ResourcesPath"] = resources })
            .Build();
        var status = new DatasetStatus();
        using var services = new ServiceCollection()
            // No step loader is registered: resuming at relations fails inside the load's catch boundary.
            .AddSingleton(provider => new DatasetLoader(provider,
                new HostingEnvironment { ContentRootPath = _root }, configuration, status,
                null!, NullLogger<DatasetLoader>.Instance))
            .BuildServiceProvider();
        using var connection = database.NewConnection();
        var forge = new ForgeRun(services, NullLogger.Instance, resources, connection.ConnectionString);

        var exit = await ForgeVerbs.Find("load")!.Run(forge, ["load", "--from", from]);

        exit.Should().Be(1);
        status.State.Should().Be(failedStep ? DatasetState.Failed : DatasetState.Waiting);
        if (failedStep)
        {
            status.Detail.Should().StartWith("relations: ");
        }
    }
}
