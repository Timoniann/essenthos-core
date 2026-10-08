using System.Security.Cryptography;
using System.Text;
using Essenthos.Core.Corpus;
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
/// The export on a server: checked against its own manifest before anything is sent, sent in the order
/// that never lists a file that is not there, and cleared of what no manifest names any more last.
/// Run against a target on this machine, which is the rehearsal of the real one with nothing but the
/// ssh prefix missing.
/// </summary>
public sealed class DownloadsPublicationTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("downloads-").FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private string Export => Path.Combine(_root, "export");

    private string Served => Path.Combine(_root, "data", "downloads_dev");

    /// <summary>A text's two files in <paramref name="folder"/>, named by their checksums, as the exporter names them.</summary>
    private static ExportedText Text(string folder, string slug, string bytes)
    {
        ExportedFile Put(string stem, string extension, string content)
        {
            var data = Encoding.UTF8.GetBytes(content);
            var hash = Convert.ToHexStringLower(SHA256.HashData(data));
            var path = $"texts/{slug}/{stem}.{hash[..12]}{extension}";
            Directory.CreateDirectory(Path.Combine(folder, "texts", slug));
            File.WriteAllBytes(Path.Combine(folder, path), data);
            return new ExportedFile(path, data.Length, hash);
        }

        return new ExportedText(
            slug, slug, null, "eng", "translation", "public-domain", "Public Domain", null, null, null, 1, 1,
            Put(slug, ".jsonl.gz", bytes), Put("ATTRIBUTION", ".txt", $"{slug} says who made it"));
    }

    private DownloadsManifest Write(string folder, params ExportedText[] texts)
    {
        Directory.CreateDirectory(folder);
        var manifest = new DownloadsManifest(
            DownloadsManifest.CurrentFormat, DateTimeOffset.UtcNow, TextExporter.Fingerprint(texts),
            new ExportedCorpus(null, null, "m", "v"), texts, []);
        File.WriteAllText(Path.Combine(folder, DownloadsManifest.FileName), manifest.ToJson());
        return manifest;
    }

    private Publisher Publisher()
    {
        Directory.CreateDirectory(Path.Combine(_root, "Resources"));
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Dataset:ResourcesPath"] = Path.Combine(_root, "Resources"),
            ["Export:Path"] = Export,
            ["Publish:Targets:rehearsal-dev:Local"] = "true",
            ["Publish:Targets:rehearsal-dev:DataRoot"] = Path.Combine(_root, "data"),
            ["Publish:Targets:rehearsal-dev:Container"] = "db",
            ["Publish:Targets:rehearsal-dev:Database"] = "corpus_dev",
            ["Publish:Targets:rehearsal-dev:Reader"] = "reader",
            ["Publish:Targets:rehearsal-dev:DatabasePort"] = "5432",
            ["Publish:Targets:rehearsal-dev:ApiContainer"] = "api",
        }).Build();
        var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=192.0.2.1;Port=1;Database=never;Username=never;Timeout=1").Options);
        return new Publisher(
            db, new CorpusCheck(db, NullLogger<CorpusCheck>.Instance), configuration,
            new HostingEnvironment { ContentRootPath = _root }, NullLoggerFactory.Instance, NullLogger<Publisher>.Instance);
    }

    private static TargetHost Host(string root) => new(new ReleaseTarget(
        "rehearsal-dev", Local: true, Ssh: "", DataRoot: root, Container: "db", Database: "corpus_dev", Reader: "reader",
        Superuser: "postgres", DatabasePort: 5432, ApiContainer: "api", After: null, Password: null));

    private IEnumerable<string> OnServer() =>
        Directory.Exists(Served)
            ? Directory.EnumerateFiles(Served, "*", SearchOption.AllDirectories)
                .Select(file => Path.GetRelativePath(Served, file).Replace('\\', '/')).Order(StringComparer.Ordinal)
            : [];

    [Fact]
    public void TheDownloadsFolderIsBesideThePicturesAndDevsHasItsOwn()
    {
        Host(Path.Combine(_root, "data")).DownloadsFolder.Should().Be(Served);
        new ReleaseTarget("prod", true, "", "/srv", "db", "corpus", "r", "postgres", 5432, "api", null, null).DownloadsName
            .Should().Be("downloads");
        new ReleaseTarget("prod", true, "", "/srv", "db", "corpus", "r", "postgres", 5432, "api", null, null, Downloads: "files").DownloadsName
            .Should().Be("files");
    }

    [Fact]
    public void AWholeExportPassesAndACorruptedOrMissingFileDoesNot()
    {
        var manifest = Write(Export, Text(Export, "KJV", "the king james"));
        DownloadsPublication.Check(Export).Problems.Should().BeEmpty();

        var file = manifest.Texts.Single().File.Path;
        File.WriteAllText(Path.Combine(Export, file), "changed after the manifest was written");
        DownloadsPublication.Check(Export).Problems.Should().ContainSingle().Which.Should().Contain(file);

        File.Delete(Path.Combine(Export, file));
        DownloadsPublication.Check(Export).Problems.Should().ContainSingle().Which.Should().Contain("not in");
    }

    [Fact]
    public void AFileOfTheRightSizeWithOtherBytesIsRefused()
    {
        var manifest = Write(Export, Text(Export, "KJV", "the king james"));
        var file = manifest.Texts.Single().File;
        File.WriteAllText(Path.Combine(Export, file.Path), new string('x', (int)file.Bytes));

        DownloadsPublication.Check(Export).Problems.Should().ContainSingle().Which.Should().Contain("checksum");
    }

    [Theory]
    [InlineData("../../etc/passwd")]
    [InlineData("/etc/passwd")]
    [InlineData("texts\\KJV\\a.txt")]
    [InlineData("C:/windows/a.txt")]
    [InlineData("texts//a.txt")]
    public void APathThatLeavesTheFolderIsRefused(string path)
    {
        Directory.CreateDirectory(Export);
        var file = new ExportedFile(path, 1, new string('0', 64));
        var manifest = new DownloadsManifest(
            DownloadsManifest.CurrentFormat, DateTimeOffset.UtcNow, "f", new ExportedCorpus(null, null, "m", "v"),
            [new ExportedText("X", "X", null, "eng", "translation", "public-domain", "PD", null, null, null, 1, 1, file, file)], []);
        File.WriteAllText(Path.Combine(Export, DownloadsManifest.FileName), manifest.ToJson());

        DownloadsPublication.Check(Export).Problems.Should().NotBeEmpty().And.OnlyContain(p => p.Contains("not a path inside"));
    }

    [Fact]
    public void AManifestThatNamesNothingIsRefusedBecauseItWouldTakeEveryDownloadAway()
    {
        Write(Export);

        DownloadsPublication.Check(Export).Problems.Should().ContainSingle().Which.Should().Contain("names no file");
    }

    [Fact]
    public void ThePlanSendsWhatIsNewOrChangedAndRemovesWhatNoManifestNamesButNeverTheManifest()
    {
        var manifest = Write(Export, Text(Export, "KJV", "the king james"), Text(Export, "ASV", "the american standard"));
        var kjv = manifest.Texts.Single(t => t.Slug == "KJV");
        var there = new Dictionary<string, string>
        {
            [kjv.File.Path] = kjv.File.Sha256,
            [kjv.Attribution.Path] = new string('f', 64),
            ["texts/KJV/KJV.0123456789ab.jsonl.gz"] = "old",
            [DownloadsManifest.FileName] = "any",
        };

        var plan = DownloadsPublication.Plan(manifest, there);

        var asv = manifest.Texts.Single(t => t.Slug == "ASV");
        plan.Send.Should().BeEquivalentTo(new[] { kjv.Attribution.Path, asv.File.Path, asv.Attribution.Path });
        plan.Remove.Should().Equal("texts/KJV/KJV.0123456789ab.jsonl.gz");
    }

    [Fact]
    public async Task APublicationSendsEverythingThenReplacesTheManifestAndTakesAwayOnlyWhatNothingNamesAnyMore()
    {
        var first = Write(Export, Text(Export, "KJV", "the king james"), Text(Export, "ASV", "the american standard"));
        var publisher = Publisher();
        var host = Host(Path.Combine(_root, "data"));

        (await publisher.SendDownloads(host, null, CancellationToken.None)).Should().BeTrue();

        OnServer().Should().BeEquivalentTo(
            [DownloadsManifest.FileName, .. DownloadsPublication.Files(first).Select(f => f.Path)]);
        File.ReadAllText(Path.Combine(Served, DownloadsManifest.FileName)).Should().Be(File.ReadAllText(Path.Combine(Export, DownloadsManifest.FileName)));

        // The King James changes, the American Standard is no longer exported.
        Directory.Delete(Export, recursive: true);
        var second = Write(Export, Text(Export, "KJV", "the king james, corrected"));

        (await publisher.SendDownloads(host, null, CancellationToken.None)).Should().BeTrue();

        OnServer().Should().BeEquivalentTo(
            [DownloadsManifest.FileName, .. DownloadsPublication.Files(second).Select(f => f.Path)],
            "the corrected text's new files are there and every file the old manifest alone named is gone");
        Directory.Exists(Path.Combine(Served, "texts", "ASV")).Should().BeFalse("the folder it leaves empty goes too");
        File.ReadAllText(Path.Combine(Served, DownloadsManifest.FileName)).Should().Contain(second.Fingerprint);
    }

    [Fact]
    public async Task ASecondPublicationOfTheSameExportChangesNothing()
    {
        Write(Export, Text(Export, "KJV", "the king james"));
        var publisher = Publisher();
        var host = Host(Path.Combine(_root, "data"));
        await publisher.SendDownloads(host, null, CancellationToken.None);
        var stamps = OnServer().ToDictionary(f => f, f => File.GetLastWriteTimeUtc(Path.Combine(Served, f)));

        (await publisher.SendDownloads(host, null, CancellationToken.None)).Should().BeTrue();

        OnServer().ToDictionary(f => f, f => File.GetLastWriteTimeUtc(Path.Combine(Served, f))).Should().Equal(stamps);
    }

    [Fact]
    public async Task AnExportThatDoesNotMatchItsManifestSendsNothingAndLeavesTheServersDownloadsAlone()
    {
        var good = Write(Export, Text(Export, "KJV", "the king james"));
        var publisher = Publisher();
        var host = Host(Path.Combine(_root, "data"));
        await publisher.SendDownloads(host, null, CancellationToken.None);
        var served = OnServer().ToList();

        Directory.Delete(Export, recursive: true);
        var next = Write(Export, Text(Export, "KJV", "a corrected king james"));
        File.WriteAllText(Path.Combine(Export, next.Texts.Single().File.Path), "damaged on the way");

        (await publisher.SendDownloads(host, null, CancellationToken.None)).Should().BeFalse();

        OnServer().Should().Equal(served);
        File.ReadAllText(Path.Combine(Served, DownloadsManifest.FileName)).Should().Contain(good.Fingerprint);
    }

    [Fact]
    public async Task WithNoExportAndNoneAskedForTheServersDownloadsAreLeftAsTheyAre()
    {
        Directory.CreateDirectory(Served);
        File.WriteAllText(Path.Combine(Served, "kept.txt"), "x");

        (await Publisher().SendDownloads(Host(Path.Combine(_root, "data")), null, CancellationToken.None)).Should().BeTrue();
        (await Publisher().PublishDownloads("rehearsal-dev", null, dryRun: false, CancellationToken.None)).Should().Be(1, "asked for by name, a missing export is an error");

        File.Exists(Path.Combine(Served, "kept.txt")).Should().BeTrue();
    }

    [Fact]
    public async Task ADryRunChecksTheExportAndWritesNothing()
    {
        Write(Export, Text(Export, "KJV", "the king james"));

        (await Publisher().PublishDownloads("rehearsal-dev", null, dryRun: true, CancellationToken.None)).Should().Be(0);

        Directory.Exists(Served).Should().BeFalse();
    }
}
