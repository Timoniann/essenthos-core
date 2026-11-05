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

    [Fact]
    public void RecordRowsMakeARecordAWordMayThenNameAndMoveAClauseToIt()
    {
        var store = Files();
        Write(store, "records/records", "slug\tkind\tsubtype\tplace_kind\tname\tsex\tsex_source\tname_gender\ttribe\torigin\tanother_name_for\tbyname",
            "altar-of-incense\tobject\tfurnishing\t\\N\tAltar of Incense\t\\N\t\\N\t\\N\t\\N\t\\N\t\\N\t\\N",
            "boaz-pillar\tobject\tstructure\t\\N\tBoaz\t\\N\t\\N\t\\N\t\\N\t\\N\t\\N\t\\N");
        Write(store, "records/i18n/eng/records", "slug\tline\tnotes\tmodern", "altar-of-incense\tthe acacia altar\t\\N\t\\N");
        Write(store, "records/i18n/ukr/lines", "slug\tline\tenglish\tby", "altar-of-incense\tжертівник з акації\tthe acacia altar\tmodel");
        Write(store, "records/i18n/ukr/notes", "slug\tnotes\tenglish_sha256\tby");
        Write(store, "records/facts/known-for", "slug\tordinal\tby", "altar-of-incense\t0\tmodel", "altar-of-incense\t1\tmodel");
        store.Save();

        var decided = new OwnerRows.Decided(
            [new("objects:37", "names/BHSA", OwnerRows.Add, "11.7:22.1", "solomons-golden-altar", null, "thing", "H8010", null, "the altar of gold")],
            [
                new("objects:37", "records/i18n/ukr/lines", OwnerRows.Put, "solomons-golden-altar", """{"line":"золотий жертівник"}""", "the altar of gold"),
                new("objects:37", "records/i18n/ukr/notes", OwnerRows.Put, "solomons-golden-altar", """{"notes":"Соломон обклав його золотом."}""", "the altar of gold"),
                new("objects:37", "records/records", OwnerRows.Put, "solomons-golden-altar",
                    """{"kind":"object","subtype":"furnishing","name":"Solomon's Golden Altar"}""", "the altar of gold"),
                new("objects:37", "records/i18n/eng/records", OwnerRows.Put, "solomons-golden-altar",
                    """{"line":"the altar of gold","notes":"Solomon overlaid it with gold."}""", "the altar of gold"),
                new("objects:37", "records/facts/known-for", OwnerRows.Put, "solomons-golden-altar", """{"ordinal":0,"by":"moved"}""", "Uzziah's altar"),
                new("objects:37", "records/facts/known-for", OwnerRows.Drop, "altar-of-incense", """{"ordinal":1}""", "Uzziah's altar"),
            ]);

        var outcome = OwnerRows.Apply(new ProductStore(_resources), decided, dryRun: false, Now);

        (outcome.Applied, outcome.Already, outcome.Refused.Count).Should().Be((7, 0, 0));
        Rows("records/records").Select(r => (r[0], r[4])).Should().Equal(
            ("altar-of-incense", "Altar of Incense"), ("boaz-pillar", "Boaz"), ("solomons-golden-altar", "Solomon's Golden Altar"));
        Rows("records/i18n/ukr/lines")[1].Should().Equal("solomons-golden-altar", "золотий жертівник", "the altar of gold", OwnerRows.Source);
        Rows("records/i18n/ukr/notes")[0][2].Should().Be(Essenthos.Core.Corpus.EnglishNotes.Digest("Solomon overlaid it with gold."));
        Rows("records/facts/known-for").Should().BeEquivalentTo(new[]
        {
            new[] { "altar-of-incense", "0", "model" },
            ["solomons-golden-altar", "0", "moved"],
        }, options => options.WithStrictOrdering());
        Rows("names/BHSA").Should().ContainEquivalentOf(
            new[] { "11.7:22.1", "solomons-golden-altar", "thing", "manual", null, OwnerRows.Source, "the altar of gold" });

        var again = OwnerRows.Apply(new ProductStore(_resources), decided, dryRun: false, Now);
        (again.Applied, again.Already, again.Written.Count).Should().Be((0, 7, 0));

        var wrong = new OwnerRows.Decided([],
        [
            new("objects:42", "records/names", OwnerRows.Put, "solomons-lavers", """{"n":1,"label":"Lavers"}""", "no such record"),
            new("objects:42", "records/records", OwnerRows.Put, "solomons-lavers", """{"kind":"object","colour":"bronze"}""", "no such column"),
        ]);
        Write(store, "records/names", "slug\tn\tlabel\tkind\thebrew\thebrew_transliterated\tgreek\tgreek_transliterated\thebrew_strong\tgreek_strong\taspect_of\tby");
        store.Save();
        var refused = OwnerRows.Apply(new ProductStore(_resources), wrong, dryRun: false, Now);
        refused.Refused.Should().HaveCount(2);
        refused.Refused.Should().Contain(r => r.Contains("'colour' is no column")).And.Contain(r => r.Contains("no such record"));
        refused.Written.Should().BeEmpty();
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
