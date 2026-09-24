using Essenthos.Core.Configuration;
using Essenthos.Core.Database;
using Essenthos.Core.Publishing;
using Essenthos.Core.Verification;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting.Internal;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Essenthos.Core.Tests;

/// <summary>
/// A release is a copy of this machine's corpus, and what the owner decided has to be in it and has
/// to be where the next rebuild will find it. These are the refusals that say so: his files not
/// committed, a change of his waiting on a step nobody ran since.
/// </summary>
[Collection(WitnessDatabaseCollection.Name)]
public sealed class PublisherUnrecordedTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly string _root = Directory.CreateTempSubdirectory("unrecorded-").FullName;

    public PublisherUnrecordedTests(WitnessDatabase database)
    {
        _db = database.NewContext();
        _db.Database.ExecuteSqlRaw("TRUNCATE text CASCADE");
        Directory.CreateDirectory(Path.Combine(_root, "Resources", "Essenthos"));
    }

    public void Dispose()
    {
        _db.Dispose();
        foreach (var file in Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories))
        {
            File.SetAttributes(file, FileAttributes.Normal);
        }

        Directory.Delete(_root, recursive: true);
    }

    private string Log => Path.Combine(_root, "Resources", OwnerChanges.DefaultPath);

    [Fact]
    public async Task AChangeWaitingOnALoadIsNamedUntilTheLoadIsRecorded()
    {
        await File.WriteAllLinesAsync(Log,
        [
            """{"at":"2026-09-24T19:30:16Z","section":"records","action":"review","target":"object/ark","needs":"load"}""",
            """{"at":"2026-09-24T19:31:00Z","section":"portraits","action":"status","target":"person/amnon","needs":"images"}""",
            """{"at":"2026-09-24T19:32:00Z","section":"apply","action":"images","after":"succeeded","needs":null}""",
        ]);

        (await Publisher().Unrecorded(CancellationToken.None)).Should().ContainSingle()
            .Which.Should().Contain("1 of the owner's changes wait on load");

        await File.AppendAllLinesAsync(Log, ["""{"at":"2026-09-25T08:00:00Z","section":"apply","action":"load","after":"succeeded","needs":null}"""]);

        (await Publisher().Unrecorded(CancellationToken.None)).Should().BeEmpty();
    }

    [Fact]
    public async Task AnOwnersFileNotCommittedIsNamed()
    {
        await Shell.Run("git", ["-C", _root, "init", "-q"], CancellationToken.None);
        var answer = Path.Combine(_root, "Resources", "Essenthos", "review", "objects-and-observances.json");
        Directory.CreateDirectory(Path.GetDirectoryName(answer)!);
        await File.WriteAllTextAsync(answer, "{}");
        await File.WriteAllTextAsync(Path.Combine(_root, "unrelated.txt"), "not the owner's");

        var unrecorded = await Publisher().Unrecorded(CancellationToken.None);

        unrecorded.Should().ContainSingle().Which.Should()
            .Contain("1 of the owner's files are not committed (Resources/Essenthos/review/objects-and-observances.json)");
    }

    private Publisher Publisher()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Dataset:ResourcesPath"] = Path.Combine(_root, "Resources") })
            .Build();
        return new Publisher(
            _db,
            new CorpusCheck(_db, NullLogger<CorpusCheck>.Instance),
            configuration,
            new HostingEnvironment { ContentRootPath = _root },
            NullLoggerFactory.Instance,
            NullLogger<Publisher>.Instance);
    }
}
