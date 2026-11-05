using Essenthos.Core.Conversion;
using Essenthos.Core.Products;
using Essenthos.Core.Records;
using FluentAssertions;
using Xunit;

namespace Essenthos.Core.Tests.Records;

/// <summary>
/// Decided rows applied to the files the build reads: a name added or restamped on a word with the owner's stamp and
/// taken off another, a dataset's verse row refiled or withdrawn; a second apply changes nothing, a dry run writes
/// nothing, and a row the files cannot take stops the whole apply.
/// </summary>
public sealed class OwnerRowsTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 11, 5, 12, 0, 0, TimeSpan.Zero);

    private readonly string _resources = Path.Combine(Path.GetTempPath(), "essenthos-products", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_resources))
        {
            Directory.Delete(_resources, recursive: true);
        }
    }

    private static readonly OwnerRows.Row[] Decided =
    [
        new("objects:0", "names/BHSA", OwnerRows.Remove, "11.7:21.33", "boaz", null, null, "H1162", null, "the left pillar"),
        new("objects:0", "names/BHSA", OwnerRows.Add, "11.7:21.33", "boaz-pillar", null, "thing", "H1162", null, "the left pillar"),
        new("objects:49", "names/BHSA", OwnerRows.Add, "11.7:15.6", "jachin-pillar", null, "thing", "H5982", null, "the two pillars"),
        new("eponyms:3", "external/bibledata/references", OwnerRows.Refile, "1.23:3", "hittites", "heth", null, null, "Heth", "the people of the land"),
        new("eponyms:9", "external/bibledata/references", OwnerRows.Refile, "19.14:7", "israelites", "jacob", null, null, "Jacob", "Jacob // Israel"),
        new("eponyms:0", "external/bibledata/references", OwnerRows.Withdraw, "13.19:5", "benammi", null, null, null, "Ammon", "names nobody"),
    ];

    [Fact]
    public void TheRowsAreWrittenWithTheOwnersStampAndASecondApplyChangesNothing()
    {
        var store = Files();

        var outcome = OwnerRows.Apply(store, Decided, dryRun: false, Now);

        (outcome.Applied, outcome.Already, outcome.Refused.Count).Should().Be((6, 0, 0));
        outcome.Written.Should().BeEquivalentTo("names/BHSA", "external/bibledata/references");
        Rows("names/BHSA").Should().BeEquivalentTo(new[]
        {
            new[] { "1.1:1.2", "god", "name", "lexical", "0.99", "BHSA", null },
            ["11.7:15.6", "jachin-pillar", "thing", "manual", null, OwnerRows.Source, "the two pillars"],
            ["11.7:21.33", "boaz-pillar", "thing", "manual", null, OwnerRows.Source, "the left pillar"],
            ["11.7:22.1", "solomon", "name", "lexical", "0.90", "BHSA", null],
        }, options => options.WithStrictOrdering());
        Rows("external/bibledata/references").Should().BeEquivalentTo(new[]
        {
            new[] { null, "hittites", "1.23:3", "Heth", "t", "f", "f" },
            [null, "israelites", "19.14:7", "Israel", "t", "f", "f"],
        }, options => options.WithStrictOrdering(), "Jacob's row at PSA 14:7 goes where the people already hold the dataset's row");

        var again = OwnerRows.Apply(new ProductStore(_resources), Decided, dryRun: false, Now);
        (again.Applied, again.Already, again.Refused.Count, again.Written.Count).Should().Be((0, 6, 0, 0));
    }

    [Fact]
    public void ADryRunWritesNothingAndARowTheFilesCannotTakeStopsEverything()
    {
        var store = Files();
        var before = File.ReadAllText(store.FullPath("names/BHSA", ProductKind.Hand));

        var dry = OwnerRows.Apply(store, Decided, dryRun: true, Now);
        (dry.Applied, dry.Written.Count).Should().Be((6, 0));
        File.ReadAllText(store.FullPath("names/BHSA", ProductKind.Hand)).Should().Be(before);

        OwnerRows.Row[] wrong =
        [
            .. Decided,
            new("objects:118", "names/BHSA", OwnerRows.Add, "11.7:21.33", "steps-of-ahaz", null, "thing", "H4600", null, "a word that is not this one"),
            new("objects:1", "names/BHSA", OwnerRows.Add, "1.1:1.2", "nobody-of-the-name", null, "thing", "H430", null, "no such record"),
        ];
        var refused = OwnerRows.Apply(new ProductStore(_resources), wrong, dryRun: false, Now);
        refused.Refused.Should().HaveCount(2);
        refused.Refused[0].Should().Contain("carries H1162, not the H4600");
        refused.Refused[1].Should().Contain("no such record");
        refused.Written.Should().BeEmpty();
        File.ReadAllText(store.FullPath("names/BHSA", ProductKind.Hand)).Should().Be(before);
    }

    private ProductStore Files()
    {
        var store = new ProductStore(_resources);
        using (var words = store.Create(TextWord.ProductOf("BHSA"), ProductKind.Hand, TextWord.Schema("BHSA"), "test", []))
        {
            words.WriteAll(new[]
            {
                Word("1.1:1.2", "H430"), Word("11.7:15.6", "H5982"), Word("11.7:21.33", "H1162"), Word("11.7:22.1", "H8010"),
            });
        }

        Write(store, "names/BHSA", "own\trecord\trole\tmethod\tconfidence\tsource\tnote",
            "1.1:1.2\tgod\tname\tlexical\t0.99\tBHSA\t\\N",
            "11.7:21.33\tboaz\tname\tlexical\t0.99\tBHSA\t\\N",
            "11.7:21.33\tboaz-pillar\tthing\tmodel-reading\t0.93\ta model\t\\N",
            "11.7:22.1\tsolomon\tname\tlexical\t0.90\tBHSA\t\\N");
        Write(store, "external/bibledata/references", "external_id\trecord\tref\tlabel\tnames\tdisputed\twithheld",
            "person:Heth_1\theth\t1.23:3\tHeth\tt\tf\tf",
            "person:Ben-ammi_1\tbenammi\t13.19:5\tAmmon\tf\tf\tf",
            "person:Jacob_1\tjacob\t19.14:7\tJacob\tt\tf\tf",
            "\\N\tisraelites\t19.14:7\tIsrael\tt\tf\tf");
        Write(store, "records/records", "slug\tkind", "boaz-pillar\tobject", "jachin-pillar\tobject", "hittites\tpeople", "israelites\tpeople",
            "steps-of-ahaz\tobject", "god\tperson", "solomon\tperson");
        store.Save();
        return new ProductStore(_resources);
    }

    private static TextWord Word(string key, string strong) =>
        new(WordKey.Parse(key, "BHSA"), null, "w", " ", null, strong, null, null, null, null, false, null, null, null);

    private void Write(ProductStore store, string product, params string[] lines)
    {
        var path = store.FullPath(product, ProductKind.Hand);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "# producer   by hand\n" + string.Join("\n", lines) + "\n");
    }

    private string?[][] Rows(string product)
    {
        using var reader = new ProductStore(_resources).Open(product);
        return [.. reader.Rows()];
    }
}
