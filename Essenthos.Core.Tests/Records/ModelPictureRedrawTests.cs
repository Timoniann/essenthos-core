using System.Security.Cryptography;
using Essenthos.Core.Loading.Encyclopedia;
using Essenthos.Core.Products;
using Essenthos.Core.Records;
using Essenthos.Core.Verbs;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Essenthos.Core.Tests.Records;

/// <summary>
/// A thing's picture drawn again from its changed model is listed under the digest of its new bytes, which is the
/// version tag a reader's browser caches it by; and the renderer is not started when nothing it draws from moved.
/// The renderer here is a stand-in script run by Node that copies the picture it is given.
/// </summary>
public sealed class ModelPictureRedrawTests : IDisposable
{
    /// <summary>The smallest JPEG header that states a size: SOI, then a baseline frame of 512 by 384.</summary>
    private static readonly byte[] Jpeg =
    [
        0xFF, 0xD8,
        0xFF, 0xE0, 0x00, 0x04, 0x00, 0x00,
        0xFF, 0xC0, 0x00, 0x11, 0x08, 0x01, 0x80, 0x02, 0x00, 0x03, 0x01, 0x22, 0x00, 0x02, 0x11, 0x01, 0x03,
        0x11, 0x01,
    ];

    private const string Renderer =
        """
        import { appendFileSync, copyFileSync } from "node:fs"
        import { dirname, join } from "node:path"
        import { fileURLToPath } from "node:url"
        const web = join(dirname(fileURLToPath(import.meta.url)), "../..")
        const out = process.argv[process.argv.indexOf("--out") + 1]
        appendFileSync(join(web, "runs.txt"), "run\n")
        copyFileSync(join(web, "next.jpg"), join(out, "ark.jpg"))
        console.log(JSON.stringify({ models: 1, drawn: 1, kept: 0, removed: 0 }))
        """;

    private readonly string _root = Directory.CreateTempSubdirectory("model-picture-redraw-").FullName;

    private string Resources => Path.Combine(_root, "Resources");

    private string Web => Path.Combine(_root, "web");

    private string Picture => Path.Combine(Resources, EntityImageLoader.Folder, EntityImageLoader.RenderedFolder, "ark.jpg");

    public ModelPictureRedrawTests()
    {
        var store = new ProductStore(Resources);
        Write(store, "records/records", ["ark", "object", null, null, "ark", null, null, null, null, null, null, null]);
        Write(store, "records/external-ids");
        Write(store, "records/folds");
        Directory.CreateDirectory(Path.GetDirectoryName(Picture)!);
        File.WriteAllBytes(Picture, Jpeg);
        File.WriteAllText(Path.Combine(Path.GetDirectoryName(Picture)!, EntityImageLoader.ManifestFile),
            """
            { "source": "Essenthos, rendered", "credit": "Essenthos", "licence": "Essenthos's own work",
              "images": [{ "entity": "ark", "file": "models/ark.jpg" }] }
            """);

        Text("scripts/model-pictures/render.mjs", Renderer);
        Text("scripts/visit/cdp.mjs", "export {}\n");
        Text("package-lock.json", "{}\n");
        Text("src/features/models/ark.ts", "export const cubits = 2.5\n");
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public async Task APictureDrawnAgainIsListedUnderItsNewBytesAndNothingMovedStartsNothing()
    {
        var store = new ProductStore(Resources);
        RecordProducers.Images(store, store).ToList();
        var before = Listed();
        before.Should().Be(Digest(Jpeg));

        Next([.. Jpeg, 0xFF, 0xD9]);
        await Images();
        Runs().Should().Be(1, "nothing records what the pictures were drawn from yet");
        Listed().Should().Be(Digest([.. Jpeg, 0xFF, 0xD9])).And.NotBe(before);

        Next([.. Jpeg, 0x00, 0xFF, 0xD9]);
        await Images();
        Runs().Should().Be(1, "neither the models nor the things moved, and every listed picture is in place");
        Listed().Should().Be(Digest([.. Jpeg, 0xFF, 0xD9]));

        Text("src/features/models/ark.ts", "export const cubits = 3\n");
        await Images();
        Runs().Should().Be(2, "the model moved");
        Listed().Should().Be(Digest([.. Jpeg, 0x00, 0xFF, 0xD9]));

        File.Delete(Picture);
        await Images();
        Runs().Should().Be(3, "a listed picture is missing");
    }

    private async Task Images()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { [ModelPictures.WebConfigurationKey] = Web })
            .Build();
        using var services = new ServiceCollection()
            .AddSingleton<IConfiguration>(configuration)
            .AddLogging()
            .AddScoped<ModelPictures>()
            .BuildServiceProvider();
        var exit = await ForgeVerbs.Find("images")!.Run(new ForgeRun(services, NullLogger.Instance, Resources, ""), ["images"]);
        exit.Should().Be(0);
    }

    private string? Listed()
    {
        var store = new ProductStore(Resources);
        using var reader = store.Open("records/facts/images", RecordFiles.All.Single(f => f.Product == "records/facts/images").Schema());
        List<string?[]> rows = [.. reader.ReadAll()];
        return rows.Single(row => row[4] == "models/ark.jpg")[5];
    }

    private static string Digest(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes))[..12];

    private int Runs() => File.Exists(Path.Combine(Web, "runs.txt")) ? File.ReadAllLines(Path.Combine(Web, "runs.txt")).Length : 0;

    private void Next(byte[] bytes) => File.WriteAllBytes(Path.Combine(Web, "next.jpg"), bytes);

    private void Text(string relative, string text)
    {
        var path = Path.Combine(Web, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }

    private static void Write(ProductStore store, string product, params string?[][] rows)
    {
        var file = RecordFiles.All.Single(f => f.Product == product);
        using var writer = store.Create(product, file.Kind, file.Schema(), ProductHeader.DescribeProducer(RecordFiles.Producer), []);
        writer.WriteAll(rows);
        writer.Complete();
    }
}
