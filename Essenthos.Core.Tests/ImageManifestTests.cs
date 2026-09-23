using Essenthos.Core.Publishing;
using FluentAssertions;
using Xunit;

namespace Essenthos.Core.Tests;

/// <summary>
/// The pictures a publication sends beside a release: what a target lacks, what reaches its images
/// folder, and whether every picture a release names is there before it is swapped in.
/// </summary>
public sealed class ImageManifestTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("images-").FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private string Source => Path.Combine(_root, "Images");

    private void Picture(string path, string bytes)
    {
        var file = Path.Combine(Source, path);
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, bytes);
    }

    private ReleaseTarget LocalTarget() => new(
        "rehearsal", Local: true, Ssh: "", DataRoot: Path.Combine(_root, "data"), Container: "db",
        Database: "corpus_dev", Reader: "reader", Superuser: "postgres", DatabasePort: 5432,
        ApiContainer: "api", After: null, Password: null);

    [Fact]
    public void AFolderIsReadByItsPathsWithForwardSlashes()
    {
        Picture("openbible/a.jpg", "a");
        Picture("commons/b.jpg", "b");

        var manifest = ImageManifest.Read(Source);

        manifest.Keys.Should().BeEquivalentTo("openbible/a.jpg", "commons/b.jpg");
        manifest["openbible/a.jpg"].Should().Be("ca978112ca1bbdcafac231b39a23dc4da786eff8147c4e72b9807785afee48bb");
    }

    [Fact]
    public void AFolderThatIsNotThereHasNoPictures() =>
        ImageManifest.Read(Path.Combine(_root, "nowhere")).Should().BeEmpty();

    [Fact]
    public void AServersListingIsReadAsTheSamePaths()
    {
        var listing = "ca978112ca1bbdcafac231b39a23dc4da786eff8147c4e72b9807785afee48bb  ./openbible/a.jpg\n" +
                      "3E23E8160039594A33894F6564E1B1348BBD7A0088D42C4ACB73EEAED59C009D *./commons/b.jpg\n";

        ImageManifest.Parse(listing).Should().BeEquivalentTo(new Dictionary<string, string>
        {
            ["openbible/a.jpg"] = "ca978112ca1bbdcafac231b39a23dc4da786eff8147c4e72b9807785afee48bb",
            ["commons/b.jpg"] = "3e23e8160039594a33894f6564e1b1348bbd7a0088d42c4acb73eeaed59c009d",
        });
    }

    [Fact]
    public void OnlyWhatTheTargetLacksOrHasWithOtherBytesIsSent()
    {
        var here = new Dictionary<string, string> { ["a.jpg"] = "1", ["b.jpg"] = "2", ["c.jpg"] = "3" };
        var there = new Dictionary<string, string> { ["a.jpg"] = "1", ["b.jpg"] = "old", ["gone.jpg"] = "9" };

        ImageManifest.ToSend(here, there).Should().Equal("b.jpg", "c.jpg");
    }

    [Fact]
    public void APictureTheReleaseNamesIsMissingDifferingOrThere()
    {
        var there = new Dictionary<string, string>
        {
            ["openbible/a.jpg"] = "ca978112ca1bbdcafac231b39a23dc4da786eff8147c4e72b9807785afee48bb",
            ["commons/b.jpg"] = "3e23e8160039594a33894f6564e1b1348bbd7a0088d42c4acb73eeaed59c009d",
        };
        var rows = ImageManifest.Rows("openbible/a.jpg|ca978112ca1b\ncommons/b.jpg|000000000000\ngenerated/c.png|111111111111\n");

        var (missing, differing) = ImageManifest.Check(rows, there);

        missing.Should().Equal("generated/c.png");
        differing.Should().Equal("commons/b.jpg");
    }

    /// <summary>The rehearsal's path: the pictures land in the data root's folder for the corpus, and a second send moves nothing.</summary>
    [Fact]
    public async Task ALocalTargetReceivesThePicturesInItsCorpusFolder()
    {
        Picture("openbible/a.jpg", "a");
        Picture("commons/b.jpg", "b");
        var host = new TargetHost(LocalTarget());

        var here = ImageManifest.Read(Source);
        await host.SendImages(Source, ImageManifest.ToSend(here, await host.Images(CancellationToken.None)), CancellationToken.None);

        host.ImagesFolder.Should().Be(Path.Combine(_root, "data", "images", "corpus_dev"));
        File.ReadAllText(Path.Combine(host.ImagesFolder, "commons", "b.jpg")).Should().Be("b");
        ImageManifest.ToSend(here, await host.Images(CancellationToken.None)).Should().BeEmpty();
    }
}
