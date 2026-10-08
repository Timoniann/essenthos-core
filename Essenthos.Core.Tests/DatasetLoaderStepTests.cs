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

    /// <summary>
    /// The names are resolved once over the encyclopedia the register left and again over the one the
    /// passes after it added things to and the fold made one record of two, before anything counts
    /// or carries what the resolutions wrote.
    /// </summary>
    [Fact]
    public void TheNamesAreResolvedAgainAfterTheRecordsAreFolded()
    {
        var names = Loader().StepNames();

        names.Should().ContainInOrder(
            "person-register", "annotations", "things", "fold-records", "settled-names",
            "crossed-names", "name-consensus", "spellings", "verify");
    }

    /// <summary>
    /// A corpus built from nothing runs the register before the records this corpus writes and before the
    /// verses a dataset filed under a namesake are given to their men, so the register is matched again as
    /// soon as each is done, in the load that added its records.
    /// </summary>
    [Fact]
    public void TheRegisterIsMatchedAgainRightAfterTheOwnRecordsAreWritten()
    {
        Loader().StepNames().Should().ContainInOrder(
            "person-register", "annotations", "own-records", "register-rematch", "sense-readings", "misfiled-verses",
            "register-rematch-verses", "fold-records");
    }

    /// <summary>
    /// The relationships are read off the clauses after every step that moves a clause, and before
    /// the verses they were read from are listed.
    /// </summary>
    [Fact]
    public void TheRelationshipsAreReadOffTheClausesWhereTheyEndUp()
    {
        Loader().StepNames().Should().ContainInOrder(
            "descriptors", "fold-records", "refiled-ties", "own-relationships", "relationship-verses");
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
