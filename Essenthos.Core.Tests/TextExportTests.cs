using System.IO.Compression;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Essenthos.Core.Corpus;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Endpoints;
using Essenthos.Core.Publishing;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting.Internal;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Essenthos.Core.Tests;

/// <summary>
/// The first slice of the bulk export: which texts may be written, what a text's file and its
/// attribution hold, that the manifest's checksums are the files' own, and that the API lists what the
/// export wrote.
/// </summary>
[Collection(WitnessDatabaseCollection.Name)]
public sealed class TextExportTests : IDisposable
{
    private static readonly string[] Ours = ["XPD", "XPM", "XPA", "XSA", "XNC", "XUN", "XPR", "XNL", "XEX", "XEY"];

    private readonly AppDbContext _db;
    private readonly IDbContextTransaction _transaction;
    private readonly string _root = Directory.CreateTempSubdirectory("text-export-").FullName;

    public TextExportTests(WitnessDatabase database)
    {
        _db = database.NewContext();
        _transaction = _db.Database.BeginTransaction();
    }

    public void Dispose()
    {
        _transaction.Rollback();
        _transaction.Dispose();
        _db.Dispose();
        Directory.Delete(_root, recursive: true);
    }

    private TextExporter Exporter()
    {
        var resources = Directory.CreateDirectory(Path.Combine(_root, "Resources")).FullName;
        File.WriteAllText(Path.Combine(resources, "MANIFEST.json"), "{}");
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Dataset:ResourcesPath"] = resources })
            .Build();
        return new TextExporter(_db, configuration, new HostingEnvironment { ContentRootPath = _root }, NullLogger<TextExporter>.Instance);
    }

    private Text Add(string slug, Redistribution redistribution, string? licence = "Public Domain", params string[] verses)
    {
        var text = Corpus.Add(
            _db, slug, TextKind.Translation, "eng",
            [.. (verses.Length == 0 ? ["In the beginning", "And the earth"] : verses).Select((verse, at) => (1, at + 1, verse.Split(' ')))]);
        text.Redistribution = redistribution;
        text.Licence = licence;
        _db.SaveChanges();
        return text;
    }

    private async Task<(List<Text> Included, List<WithheldText> Withheld)> Chosen()
    {
        var (included, withheld) = await Exporter().Choose(CancellationToken.None);
        return ([.. included.Where(t => Ours.Contains(t.Slug))], [.. withheld.Where(w => Ours.Contains(w.Slug))]);
    }

    [Fact]
    public async Task OnlyTextsThatMayBePassedOnAreChosenAndEachOtherOneSaysWhy()
    {
        Add("XPD", Redistribution.PublicDomain);
        Add("XPM", Redistribution.Permitted);
        Add("XPA", Redistribution.PermittedWithAttribution);
        Add("XSA", Redistribution.ShareAlike);
        Add("XNC", Redistribution.NonCommercialOnly);
        Add("XUN", Redistribution.Unknown);
        Add("XPR", Redistribution.Prohibited);
        Add("XNL", Redistribution.PublicDomain, licence: null);

        var (included, withheld) = await Chosen();

        included.Select(t => t.Slug).Should().Equal("XPA", "XPD", "XPM");
        withheld.Select(w => w.Slug).Should().Equal("XNC", "XNL", "XPR", "XSA", "XUN");
        withheld.Single(w => w.Slug == "XSA").Reason.Should().Contain("ShareAlike");
        withheld.Single(w => w.Slug == "XNC").Reason.Should().Contain("non-commercial");
        withheld.Single(w => w.Slug == "XNL").Reason.Should().Contain("no licence");
        withheld.Single(w => w.Slug == "XNL").Redistribution.Should().Be("public-domain");
    }

    [Fact]
    public async Task AFolderNameCannotEscapeTheExport()
    {
        Add("XEX", Redistribution.PublicDomain).Slug = "../XEX";
        _db.SaveChanges();

        var (_, withheld) = await Exporter().Choose(CancellationToken.None);

        withheld.Should().Contain(w => w.Slug == "../XEX" && w.Reason.Contains("folder"));
    }

    [Fact]
    public async Task TheFileHoldsEachVerseInOrderWithoutElidedWordsOrEmptyVersesAndByAddressNeverByRow()
    {
        var text = Add("XPD", Redistribution.PublicDomain, "Public Domain", "In the beginning", "And the earth", "", "Light");
        var second = _db.Verses.Single(v => v.TextId == text.Id && v.Number == 2);
        _db.Words.Add(new Word { Text = text, Verse = second, Position = 9, Surface = "[the]", Trailer = " ", Elided = true });
        _db.Verses.Single(v => v.TextId == text.Id && v.Number == 4).Label = "a";
        _db.SaveChanges();

        var manifest = await Exporter().Write(Path.Combine(_root, "out"), [text], [], CancellationToken.None);

        var lines = await Lines(Path.Combine(_root, "out"), manifest.Texts.Single().File.Path);
        lines.Select(l => l.GetProperty("verse").GetInt32()).Should().Equal(1, 2, 4);
        lines[1].GetProperty("text").GetString().Should().Be("And the earth");
        lines[0].GetProperty("book").GetString().Should().Be("gen");
        lines[0].GetProperty("bookNumber").GetInt32().Should().Be(1);
        lines[0].GetProperty("chapter").GetInt32().Should().Be(1);
        lines[0].TryGetProperty("label", out _).Should().BeFalse();
        lines[2].GetProperty("label").GetString().Should().Be("a");
        lines.SelectMany(l => l.EnumerateObject().Select(p => p.Name)).Distinct()
            .Should().BeEquivalentTo("book", "bookNumber", "chapter", "verse", "label", "text");

        var exported = manifest.Texts.Single();
        exported.Verses.Should().Be(3);
        exported.Books.Should().Be(1);
    }

    [Fact]
    public async Task TheManifestChecksumsAreTheFilesOwnAndTheFingerprintFollowsTheBytes()
    {
        var text = Add("XPD", Redistribution.PublicDomain);

        var first = await Exporter().Write(Path.Combine(_root, "one"), [text], [], CancellationToken.None);
        var again = await Exporter().Write(Path.Combine(_root, "two"), [text], [], CancellationToken.None);

        foreach (var file in first.Texts.SelectMany(t => new[] { t.File, t.Attribution }))
        {
            var bytes = await File.ReadAllBytesAsync(Path.Combine(_root, "one", file.Path));
            file.Bytes.Should().Be(bytes.Length);
            file.Sha256.Should().Be(Convert.ToHexStringLower(SHA256.HashData(bytes)));
        }

        again.Fingerprint.Should().Be(first.Fingerprint, "the same corpus is the same bytes, whenever it is written");

        _db.Words.First(w => w.TextId == text.Id).Surface = "Changed";
        _db.SaveChanges();
        var changed = await Exporter().Write(Path.Combine(_root, "three"), [text], [], CancellationToken.None);
        changed.Fingerprint.Should().NotBe(first.Fingerprint);

        var parsed = DownloadsManifest.Parse(await File.ReadAllTextAsync(Path.Combine(_root, "one", DownloadsManifest.FileName)));
        parsed!.Fingerprint.Should().Be(first.Fingerprint);
        parsed.Format.Should().Be(DownloadsManifest.CurrentFormat);
    }

    [Fact]
    public void TheAttributionCarriesTheLicenceTheCreditAndEverySourceThatLentWords()
    {
        var text = new Text
        {
            Slug = "XPA",
            Name = "An Example Bible",
            NameNative = "Exemplar",
            Kind = TextKind.Translation,
            Language = "eng",
            Translators = "A company of forty",
            RightsHolder = "The Example Society",
            Licence = "CC BY 4.0",
            LicenceUrl = "https://creativecommons.org/licenses/by/4.0/",
            Citation = "Example Society, 1900.",
            RightsNote = "Checked against the printed copy.",
            SourceUrl = "https://example.org/xpa.zip",
            Redistribution = Redistribution.PermittedWithAttribution,
        };
        TextPartSources.Add(text, new TextPartSource(
            "Restored psalms", "Another Society", "Public Domain", "https://example.org/pd", "https://example.org/psalms", "Psalm titles"));

        var attribution = TextExporter.Attribution(text, "texts/XPA/XPA.jsonl.gz");

        attribution.Should().Contain("An Example Bible")
            .And.Contain("Licence: CC BY 4.0")
            .And.Contain("Licence text: https://creativecommons.org/licenses/by/4.0/")
            .And.Contain("Rights holder: The Example Society")
            .And.Contain("How to cite: Example Society, 1900.")
            .And.Contain("About the rights: Checked against the printed copy.")
            .And.Contain("credited as the licence above requires")
            .And.Contain("Restored psalms, Another Society")
            .And.Contain("Covers: Psalm titles")
            .And.Contain("XPA.jsonl.gz holds the text verse by verse");
        attribution.Should().NotContain("Edition:", "a field nothing recorded is not printed empty");
    }

    [Fact]
    public async Task AnExportReplacesTheFolderWholeAndANewRunLeavesNothingOfTheOldBehind()
    {
        Add("XPD", Redistribution.PublicDomain);
        var target = Path.Combine(_root, "downloads");
        Directory.CreateDirectory(Path.Combine(target, "texts", "GONE"));
        await File.WriteAllTextAsync(Path.Combine(target, "texts", "GONE", "old.txt"), "from an earlier export");

        (await Exporter().Export(target, dryRun: true, CancellationToken.None)).Should().Be(0);
        File.Exists(Path.Combine(target, "texts", "GONE", "old.txt")).Should().BeTrue("a dry run writes nothing");

        (await Exporter().Export(target, dryRun: false, CancellationToken.None)).Should().Be(0);

        Directory.Exists(Path.Combine(target, "texts", "GONE")).Should().BeFalse();
        var written = DownloadsManifest.Parse(await File.ReadAllTextAsync(Path.Combine(target, DownloadsManifest.FileName)))!;
        File.Exists(Path.Combine(target, written.Texts.Single().File.Path)).Should().BeTrue();
        Directory.EnumerateFiles(Path.Combine(target, "texts", "XPD")).Should().HaveCount(2, "a finished export leaves no part of a file behind");
        Directory.Exists(target + ".writing").Should().BeFalse();
        Directory.Exists(target + ".previous").Should().BeFalse();
    }

    /// <summary>
    /// A name that never means other bytes is what lets a server keep a copy of the file for good, and
    /// leave an earlier export's files beside a later one's until nothing names them.
    /// </summary>
    [Fact]
    public async Task EachFileIsNamedByItsOwnChecksumSoAChangedTextIsANewName()
    {
        var text = Add("XPD", Redistribution.PublicDomain);
        var first = (await Exporter().Write(Path.Combine(_root, "one"), [text], [], CancellationToken.None)).Texts.Single();

        first.File.Path.Should().Be($"texts/XPD/XPD.{first.File.Sha256[..TextExporter.NameDigits]}.jsonl.gz");
        first.Attribution.Path.Should().Be($"texts/XPD/ATTRIBUTION.{first.Attribution.Sha256[..TextExporter.NameDigits]}.txt");
        (await File.ReadAllTextAsync(Path.Combine(_root, "one", first.Attribution.Path)))
            .Should().Contain($"{Path.GetFileName(first.File.Path)} holds the text verse by verse");

        _db.Words.First(w => w.TextId == text.Id).Surface = "Changed";
        _db.SaveChanges();
        var changed = (await Exporter().Write(Path.Combine(_root, "two"), [text], [], CancellationToken.None)).Texts.Single();

        changed.File.Path.Should().NotBe(first.File.Path);
        changed.Attribution.Path.Should().NotBe(first.Attribution.Path, "the attribution names the file, so it is another file too");
    }

    [Fact]
    public async Task TheCatalogueListsTheManifestWithAddressesOnlyWhereABaseAddressIsSet()
    {
        var text = Add("XPD", Redistribution.PublicDomain);
        var folder = Path.Combine(_root, "catalogue");
        var manifest = await Exporter().Write(folder, [text], [], CancellationToken.None);

        var served = new DownloadsCatalogue(folder, "https://files.example.org/dl/", NullLogger<DownloadsCatalogue>.Instance).Current();
        var unplaced = new DownloadsCatalogue(folder, null, NullLogger<DownloadsCatalogue>.Instance).Current();
        var absent = new DownloadsCatalogue(Path.Combine(_root, "nothing"), "https://files.example.org", NullLogger<DownloadsCatalogue>.Instance).Current();
        var unset = new DownloadsCatalogue(null, "https://files.example.org", NullLogger<DownloadsCatalogue>.Instance).Current();

        served.Fingerprint.Should().Be(manifest.Fingerprint);
        var listed = served.Texts.Single();
        listed.Id.Should().Be("XPD");
        listed.File.Url.Should().Be($"https://files.example.org/dl/{manifest.Texts.Single().File.Path}");
        listed.Attribution.Url.Should().Be($"https://files.example.org/dl/{manifest.Texts.Single().Attribution.Path}");
        listed.File.Sha256.Should().Be(manifest.Texts.Single().File.Sha256);
        unplaced.Texts.Single().File.Url.Should().BeNull();
        absent.Texts.Should().BeEmpty();
        unset.Texts.Should().BeEmpty();
    }

    [Fact]
    public void AManifestPathThatLeavesTheFolderOrNamesAnotherSiteIsNotGivenAnAddress()
    {
        var file = new ExportedFile("../../etc/passwd", 1, "00");
        var manifest = new DownloadsManifest(
            DownloadsManifest.CurrentFormat, DateTimeOffset.UtcNow, "f", new ExportedCorpus(null, null, "m", "v"),
            [new ExportedText("X", "X", null, "eng", "translation", "public-domain", "PD", null, null, null, 1, 1, file, new ExportedFile("https://elsewhere.example/a", 1, "00"))],
            []);

        var listed = new DownloadsCatalogue(null, "https://files.example.org", NullLogger<DownloadsCatalogue>.Instance)
            .Describe(manifest.ToJson()).Texts.Single();

        listed.File.Url.Should().BeNull();
        listed.Attribution.Url.Should().BeNull();
    }

    [Fact]
    public async Task TheEndpointAnswersWithTheListAsJson()
    {
        var text = Add("XPD", Redistribution.PublicDomain);
        var folder = Path.Combine(_root, "served");
        var written = await Exporter().Write(folder, [text], [], CancellationToken.None);

        var builder = WebApplication.CreateSlimBuilder(["--urls=http://127.0.0.1:0"]);
        builder.Services.ConfigureHttpJsonOptions(options =>
            options.SerializerOptions.TypeInfoResolverChain.Insert(0, AppJsonSerializerContext.Default));
        builder.Services.AddSingleton(new DownloadsCatalogue(folder, "/files", NullLogger<DownloadsCatalogue>.Instance));
        await using var app = builder.Build();
        app.MapGroup("/v1").MapDownloads();
        await app.StartAsync();

        using var http = new HttpClient { BaseAddress = new Uri(app.Urls.First()) };
        var response = await http.GetAsync("/v1/downloads");

        response.EnsureSuccessStatusCode();
        response.Headers.CacheControl!.MaxAge.Should().BeGreaterThan(TimeSpan.Zero);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var entry = json.RootElement.GetProperty("texts").EnumerateArray().Single();
        entry.GetProperty("id").GetString().Should().Be("XPD");
        entry.GetProperty("licence").GetString().Should().Be("Public Domain");
        entry.GetProperty("file").GetProperty("url").GetString().Should().Be($"/files/{written.Texts.Single().File.Path}");
        entry.GetProperty("file").GetProperty("sha256").GetString().Should().HaveLength(64);
        json.RootElement.GetProperty("fingerprint").GetString().Should().HaveLength(64);
    }

    private static async Task<List<JsonElement>> Lines(string folder, string path)
    {
        await using var file = File.OpenRead(Path.Combine(folder, path));
        await using var gzip = new GZipStream(file, CompressionMode.Decompress);
        using var reader = new StreamReader(gzip);
        var lines = new List<JsonElement>();
        while (await reader.ReadLineAsync() is { } line)
        {
            lines.Add(JsonDocument.Parse(line).RootElement.Clone());
        }

        return lines;
    }
}
