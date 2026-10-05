using Essenthos.Core.Loading;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting.Internal;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Essenthos.Core.Tests;

/// <summary>
/// The load as a list of named steps: a failed one is named, and <c>load --from</c> starts there.
/// </summary>
public sealed class DatasetLoaderStepTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("load-steps-").FullName;

    private readonly DatasetStatus _status = new();

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private DatasetLoader Loader()
    {
        var resources = Directory.CreateDirectory(Path.Combine(_root, "Resources")).FullName;
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Dataset:ResourcesPath"] = resources })
            .Build();

        // No loader is registered, so the first step that asks for one fails where it stands.
        return new DatasetLoader(
            new ServiceCollection().BuildServiceProvider(),
            new HostingEnvironment { ContentRootPath = _root },
            configuration,
            _status,
            null!,
            NullLogger<DatasetLoader>.Instance);
    }

    [Fact]
    public void TheKingJamesIsMatchedAgainstAllFiveNumberedGreekEditions()
    {
        DatasetLoader.GreekWitnesses.Should().Equal(
            Essenthos.Core.Loading.TextusReceptusTextSource.Slug(Essenthos.Core.TextusReceptus.Edition.Scrivener1894),
            ByzantineTextSource.Slug,
            NestleTextSource.Slug,
            TischendorfTextSource.Slug,
            WestcottHortTextSource.Slug);
    }

    [Fact]
    public void EveryStepHasANameOfItsOwnAndTheLoadEndsByMeasuring()
    {
        var names = Loader().StepNames();

        names.Should().OnlyHaveUniqueItems(name => name.ToUpperInvariant());
        names.Should().StartWith("BHSA");
        names.Should().ContainInOrder("KJV", "statistics", "lexicon", "recipe", "encyclopedia", "person-register");
        names[^1].Should().Be("verify");
    }

    [Fact]
    public async Task AStepThatIsNotThereIsRefusedBeforeAnythingRuns()
    {
        (await Loader().Run("no-such-step", CancellationToken.None)).Should().BeFalse();

        _status.State.Should().Be(DatasetState.Waiting);
    }

    [Fact]
    public async Task AFailedStepIsNamedAndTheStepsBeforeTheOneNamedAreNotRun()
    {
        (await Loader().Run("Relations", CancellationToken.None)).Should().BeFalse();

        _status.State.Should().Be(DatasetState.Failed);
        _status.Detail.Should().StartWith("relations: ");
        _status.Texts.Should().BeEmpty();
    }
}
