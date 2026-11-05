using System.Text.Json.Nodes;
using Essenthos.Core.Build;
using Essenthos.Core.Loading.Encyclopedia;
using Essenthos.Core.Products;
using Essenthos.Core.Records;
using FluentAssertions;
using Xunit;

namespace Essenthos.Core.Tests.Records;

/// <summary>
/// The two record files the owner decides in his console, made from the files the console writes: the pictures
/// from the picture lists and his choices, the Wikipedia links from his answers and the matching's ties in his
/// list. Each reads only what its line of the build's graph declares.
/// </summary>
public sealed class ConsoleFactFilesTests : IDisposable
{
    private static readonly byte[] Jpeg =
    [
        0xFF, 0xD8,
        0xFF, 0xE0, 0x00, 0x04, 0x00, 0x00,
        0xFF, 0xC0, 0x00, 0x11, 0x08, 0x01, 0x80, 0x02, 0x00, 0x03, 0x01, 0x22, 0x00, 0x02, 0x11, 0x01, 0x03,
        0x11, 0x01,
    ];

    private readonly string _root = Path.Combine(Path.GetTempPath(), "essenthos-products", Guid.NewGuid().ToString("N"));

    [Fact]
    public void PicturesFollowTheOwnersWordAndChoicesAndGodIsNeverGivenAFigure()
    {
        var store = new ProductStore(_root);
        Write(store, "records/records", Record("ark", "object"), Record("moses", "person"), Record("yhvh", "person"));
        Write(store, "records/external-ids", ["yhvh", "bibledata", "person:YHVH_1", "same-as", null]);
        Write(store, "records/folds", ["moshe", "moses", "Moshe", null, "manual", null, "test", "the same man"]);
        foreach (var file in new[] { "moses", "moshe", "moses-older", "moses-new", "yhvh", "glory", "ark" })
        {
            Picture($"generated/{file}.jpg");
        }

        Picture("models/ark.jpg");
        Json("Images/generated/manifest.json", """
            {
              "source": "Essenthos, generated", "credit": "Essenthos", "licence": "Essenthos's own work",
              "images": [
                { "entity": "moses", "file": "generated/moses.jpg", "review": "approved", "caption": "Moses", "captions": { "ukr": "Мойсей" } },
                { "entity": "moshe", "file": "generated/moshe.jpg", "role": "gallery" },
                { "entity": "moses", "file": "generated/moses-older.jpg", "review": "approved" },
                { "entity": "moses", "file": "generated/moses-new.jpg", "review": "pending" },
                { "entity": "yhvh", "file": "generated/yhvh.jpg", "review": "approved" },
                { "entity": "yhvh", "file": "generated/glory.jpg", "review": "approved", "glory": true },
                { "entity": "ark", "file": "generated/ark.jpg", "review": "approved", "caption": "The ark", "captions": { "ukr": "Ковчег" } }
              ]
            }
            """);
        Json("Images/models/manifest.json", """
            { "source": "Essenthos, rendered", "credit": "Essenthos", "licence": "Essenthos's own work",
              "images": [{ "entity": "ark", "file": "models/ark.jpg" }] }
            """);
        Json("Essenthos/image-choices.json", """
            { "images": [
                { "entity": "moses", "file": "generated/moses-older.jpg", "hidden": true },
                { "entity": "moses", "file": "generated/moses.jpg", "caption": "Moses on Sinai" },
                { "entity": "ark", "file": "models/ark.jpg", "primary": true }
            ] }
            """);

        var written = RecordProducers.Images(store, store).ToDictionary(w => w.Product, w => w.Rows);

        written["records/facts/images"].Should().Be(5);
        Rows(store, "records/facts/images").Select(r => $"{r[0]} {r[1]} {r[2]} {r[3]} {r[4]} {r[6]}x{r[7]}").Should().Equal(
            "ark generated gallery 0 generated/ark.jpg 512x384",
            "ark rendered primary 0 models/ark.jpg 512x384",
            "moses generated gallery 0 generated/moshe.jpg 512x384",
            "moses generated primary 0 generated/moses.jpg 512x384",
            "yhvh generated primary 0 generated/glory.jpg 512x384");
        Rows(store, "records/i18n/eng/images").Select(r => $"{r[0]} {r[2]} {r[4]}").Should().Equal(
            "ark gallery The ark", "moses primary Moses on Sinai");
        Rows(store, "records/i18n/ukr/images").Select(r => $"{r[0]} {r[2]} {r[4]}").Should().Equal(
            ["ark gallery Ковчег"], "a caption of the owner's own replaces the list's, and its translations with it");
        Rows(store, "records/i18n/deu/images").Should().BeEmpty();
        Graph().Check(store.Manifest).Should().BeEmpty();
    }

    [Fact]
    public void WikipediaLinksAreTheOwnersAnswersElseTheMatchingsTies()
    {
        var store = new ProductStore(_root);
        Write(store, "records/records",
            Record("habakkuk", "person"), Record("micah", "person"), Record("zechariah", "person"), Record("zechariah-2", "person"));
        Directory.CreateDirectory(Path.Combine(_root, WikidataItems.Folder));
        File.WriteAllLines(Path.Combine(_root, WikidataItems.Folder, WikidataItems.ItemsFile),
        [
            Item("Q1", "Habakkuk", """{"enwiki":"Habakkuk","ukwiki":"Авакум","dewiki":"Habakuk"}"""),
            Item("Q2", "Zechariah", """{"enwiki":"Zechariah of Israel"}"""),
            Item("Q3", "Zechariah", """{"enwiki":"Zechariah (prophet)","eswiki":"Zacarías"}"""),
            Item("Q4", "Micah", "{}"),
        ]);
        Json($"Essenthos/review/{WikipediaReviewList.FileName}", """
            { "entries": [
                { "record": "habakkuk", "candidates": [], "tied": { "qid": "Q1", "by": "verse" } },
                { "record": "micah", "candidates": [], "tied": { "qid": "Q4", "by": "name" } },
                { "record": "zechariah", "candidates": [], "decision": { "answer": "Q3", "note": null, "at": "2026-11-05T00:00:00Z" } },
                { "record": "zechariah-2", "candidates": [], "tied": { "qid": "Q2", "by": "kin" },
                  "decision": { "answer": "none", "note": "not him", "at": "2026-11-05T00:00:00Z" } },
                { "record": "folded-since", "candidates": [], "decision": { "answer": "Q1", "note": null, "at": "2026-11-05T00:00:00Z" } }
            ] }
            """);

        var (product, rows) = RecordProducers.Wikipedia(store, store);

        product.Should().Be("records/facts/wikipedia");
        rows.Should().Be(5);
        Rows(store, product).Select(r => string.Join(' ', r)).Should().Equal(
            "habakkuk de Habakuk Q1 verse 0.95",
            "habakkuk en Habakkuk Q1 verse 0.95",
            "habakkuk uk Авакум Q1 verse 0.95",
            "zechariah en Zechariah (prophet) Q3 owner 1",
            "zechariah es Zacarías Q3 owner 1");
        Graph().Check(store.Manifest).Should().BeEmpty();
    }

    [Fact]
    public void WithoutTheOwnersListNoLinkIsWritten()
    {
        var store = new ProductStore(_root);
        Write(store, "records/records", Record("habakkuk", "person"));

        var make = () => RecordProducers.Wikipedia(store, store);

        make.Should().Throw<FileNotFoundException>().WithMessage("*wikipedia-matches.json*");
        store.Exists("records/facts/wikipedia").Should().BeFalse();
    }

    private static ProductGraph Graph() => ProductGraph.Read(new ProductStore(TestResources.Shipped(".")));

    private static string?[] Record(string slug, string kind) => [slug, kind, null, null, slug, null, null, null, null, null, null, null];

    private static string Item(string id, string label, string sitelinks) =>
        new JsonObject
        {
            ["id"] = id,
            ["kind"] = "person",
            ["seeds"] = new JsonArray("class"),
            ["labels"] = new JsonObject { ["en"] = label },
            ["descriptions"] = new JsonObject(),
            ["aliases"] = new JsonObject(),
            ["sitelinks"] = JsonNode.Parse(sitelinks),
            ["claims"] = new JsonObject(),
        }.ToJsonString();

    private void Picture(string file)
    {
        var path = Path.Combine(_root, EntityImageLoader.Folder, file);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, Jpeg);
    }

    private void Json(string relative, string text)
    {
        var path = Path.Combine(_root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }

    private static List<string?[]> Rows(ProductStore store, string product)
    {
        using var reader = store.Open(product, RecordFiles.All.Single(f => f.Product == product).Schema());
        return [.. reader.ReadAll()];
    }

    private static void Write(ProductStore store, string product, params string?[][] rows)
    {
        var file = RecordFiles.All.Single(f => f.Product == product);
        using var writer = store.Create(product, file.Kind, file.Schema(), ProductHeader.DescribeProducer(RecordFiles.Producer), []);
        writer.WriteAll(rows);
        writer.Complete();
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}
