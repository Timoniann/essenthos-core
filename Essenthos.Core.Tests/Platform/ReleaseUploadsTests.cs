using Essenthos.Core.Publishing;
using FluentAssertions;
using Xunit;

namespace Essenthos.Core.Tests.Platform;

/// <summary>
/// The dumps a target's releases folder keeps after a publication: the live release's and the one
/// served before it stay, a newer upload stays, every other release dump goes, nothing else is touched,
/// and nothing at all goes when which release is live or which came before cannot be told.
/// </summary>
public sealed class ReleaseUploadsTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("release-uploads-").FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private static string[] Kept(ReleaseUploads.Plan plan) => plan.Keep.Select(kept => kept.File).ToArray();

    [Fact]
    public void TheLiveReleaseAndTheOneServedBeforeStayAndOlderUploadsGo()
    {
        var plan = ReleaseUploads.Choose(
            ["20261001a.dump", "20261015a.dump", "20261029a.dump", "20261101a.dump", "20261105a.dump"],
            published: "20261105a", serving: "20261105a", previous: "20261101a");

        plan.Refusal.Should().BeNull();
        Kept(plan).Should().Equal("20261101a.dump", "20261105a.dump");
        plan.Remove.Should().Equal("20261001a.dump", "20261015a.dump", "20261029a.dump");
        plan.Keep.Single(kept => kept.File == "20261101a.dump").Why.Should().Contain("rollback");
    }

    [Fact]
    public void AfterARollbackTheReleaseRolledBackFromStaysAndWhatLiesBetweenGoes()
    {
        // 20261105a was live, 20261101a is published again: the ledger's latest other release is 20261105a.
        var plan = ReleaseUploads.Choose(
            ["20261029a.dump", "20261101a.dump", "20261103a.dump", "20261105a.dump"],
            published: "20261101a", serving: "20261101a", previous: "20261105a");

        Kept(plan).Should().Equal("20261101a.dump", "20261105a.dump");
        plan.Remove.Should().Equal("20261029a.dump", "20261103a.dump");
    }

    [Fact]
    public void AnUploadNewerThanBothIsLeftForThePublicationThatResumesIt()
    {
        var plan = ReleaseUploads.Choose(
            ["20261101a.dump", "20261105a.dump", "20261105b.dump", "20261106a.dump"],
            published: "20261105a", serving: "20261105a", previous: "20261101a");

        Kept(plan).Should().Equal("20261101a.dump", "20261105a.dump", "20261105b.dump", "20261106a.dump");
        plan.Remove.Should().BeEmpty();
    }

    [Fact]
    public void AFileNotNamedLikeAReleaseDumpIsNeverTouched()
    {
        var plan = ReleaseUploads.Choose(
            ["20261001a.dump", "20261101a.dump", "20261105a.dump", "notes.txt", "20261001a.json", "corpus.dump", "2026101a.dump", "20261001A.dump"],
            published: "20261105a", serving: "20261105a", previous: "20261101a");

        plan.Remove.Should().Equal("20261001a.dump");
        plan.Untouched.Should().BeEquivalentTo("notes.txt", "20261001a.json", "corpus.dump", "2026101a.dump", "20261001A.dump");
    }

    [Fact]
    public void ARepublishedReleaseKeepsTheOneServedBeforeItNotItself()
    {
        var ledger = new (string, DateTimeOffset)[]
        {
            ("20261029a", DateTimeOffset.Parse("2026-10-30T01:30:00Z")),
            ("20261101a", DateTimeOffset.Parse("2026-11-01T10:00:00Z")),
            ("20261105a", DateTimeOffset.Parse("2026-11-05T10:00:00Z")),
            ("20261105a", DateTimeOffset.Parse("2026-11-05T12:00:00Z")),
        };

        ReleaseUploads.Previous(ledger, "20261105a").Should().Be("20261101a");
        ReleaseUploads.Previous(ledger.Reverse(), "20261105a").Should().Be("20261101a", "the ledger is read by time, not by file order");
        ReleaseUploads.Previous(ledger, "20261101a").Should().Be("20261105a", "a rollback's previous is the release it rolled back from");
        ReleaseUploads.Previous([("20261105a", DateTimeOffset.UtcNow)], "20261105a").Should().BeNull();
    }

    [Theory]
    [InlineData("20261101a", "the live corpus says it is release 20261101a, not 20261105a")]
    [InlineData(null, "the live corpus says it is no release")]
    [InlineData("", "the live corpus says it is no release")]
    public void NothingGoesWhenTheLiveCorpusIsNotTheReleaseJustPublished(string? serving, string why)
    {
        var files = new[] { "20261001a.dump", "20261101a.dump", "20261105a.dump" };

        var plan = ReleaseUploads.Choose(files, published: "20261105a", serving: serving, previous: "20261101a");

        plan.Refusal.Should().StartWith(why);
        plan.Remove.Should().BeEmpty();
        Kept(plan).Should().Equal(files);
    }

    [Fact]
    public void NothingGoesWhenTheLedgerNamesNoReleaseServedBefore()
    {
        var plan = ReleaseUploads.Choose(["20261001a.dump", "20261105a.dump"], "20261105a", "20261105a", previous: null);

        plan.Refusal.Should().Contain("names no release served before 20261105a");
        plan.Remove.Should().BeEmpty();
    }

    [Fact]
    public void NothingGoesWhenTheJustPublishedDumpIsNotInTheListing()
    {
        var plan = ReleaseUploads.Choose(["20261001a.dump", "20261101a.dump"], "20261105a", "20261105a", "20261101a");

        plan.Refusal.Should().Contain("20261105a.dump is not among the files listed");
        plan.Remove.Should().BeEmpty();
    }

    [Theory]
    [InlineData("20261105a", "../x", "20261105a")]
    [InlineData("latest", "20261101a", "latest")]
    public void NothingGoesWhenANameIsNotAReleaseName(string published, string previous, string serving)
    {
        var plan = ReleaseUploads.Choose(["20261001a.dump", $"{published}.dump"], published, serving, previous);

        plan.Refusal.Should().Contain("not a release name");
        plan.Remove.Should().BeEmpty();
    }

    [Theory]
    [InlineData("20261029a")]
    [InlineData(null)]
    public void ATargetThatKeepsThePreviousCopyMustAgreeWithTheLedger(string? previousOnTarget)
    {
        var files = new[] { "20261029a.dump", "20261101a.dump", "20261105a.dump" };

        var refused = ReleaseUploads.Choose(files, "20261105a", "20261105a", "20261101a", keepsPrevious: true, previousOnTarget);
        var agreed = ReleaseUploads.Choose(files, "20261105a", "20261105a", "20261101a", keepsPrevious: true, "20261101a");

        refused.Refusal.Should().Contain("the previous copy on the target says");
        refused.Remove.Should().BeEmpty();
        agreed.Remove.Should().Equal("20261029a.dump");
    }

    [Fact]
    public async Task ALocalTargetListsItsReleasesFolderAndRemovesOnlyReleaseDumps()
    {
        var host = new TargetHost(new ReleaseTarget(
            "rehearsal", Local: true, Ssh: "", DataRoot: Path.Combine(_root, "data"), Container: "db",
            Database: "corpus", Reader: "reader", Superuser: "postgres", DatabasePort: 5432,
            ApiContainer: "api", After: null, Password: null, Copies: 1));
        (await host.Releases(CancellationToken.None)).Should().BeEmpty("there is no folder yet");

        Directory.CreateDirectory(host.ReleasesFolder);
        File.WriteAllText(Path.Combine(host.ReleasesFolder, "20261001a.dump"), "0123456789");
        File.WriteAllText(Path.Combine(host.ReleasesFolder, "20261105a.dump"), "01234");
        File.WriteAllText(Path.Combine(host.ReleasesFolder, "notes.txt"), "kept");

        (await host.Releases(CancellationToken.None)).Should().BeEquivalentTo(new Dictionary<string, long>
        {
            ["20261001a.dump"] = 10, ["20261105a.dump"] = 5, ["notes.txt"] = 4,
        });

        var other = () => host.RemoveReleases(["20261001a.dump", "notes.txt"], CancellationToken.None);
        await other.Should().ThrowAsync<InvalidOperationException>().WithMessage("*notes.txt*not a release dump*nothing was removed*");
        File.Exists(Path.Combine(host.ReleasesFolder, "20261001a.dump")).Should().BeTrue("a refused list removes nothing");

        await host.RemoveReleases(["20261001a.dump"], CancellationToken.None);
        Directory.GetFiles(host.ReleasesFolder).Select(Path.GetFileName).Should().BeEquivalentTo("20261105a.dump", "notes.txt");
    }
}
