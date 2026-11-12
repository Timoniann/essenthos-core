using Essenthos.Core.Desk;
using Essenthos.Core.Products;
using Essenthos.Core.Products.Essx;
using FluentAssertions;
using Xunit;

namespace Essenthos.Core.Tests.Platform;

/// <summary>
/// The texts page's mapping figures: that the count of the build store says how many of a text's words reach
/// each original and through what, that what it depends on moves when a file does, and that the measured
/// precision people write by hand is read, and a line they broke is named rather than failing the page.
/// </summary>
public sealed class MappingBoardTests : IDisposable
{
    private const string Clear = "Clear Bible Alignments WLCM-TT-manual, BiblioNexus, github.com/Clear-Bible/Alignments, CC BY 4.0, made by hand";

    private static readonly ProductSchema<WordFixture> WordSchema = new ProductSchema<WordFixture>()
        .OwnWordKey("own", w => w.Own)
        .Text("surface", w => w.Surface)
        .Boolean("elided", w => false)
        .OptionalText("strong", w => w.Strong)
        .OptionalInteger("supplied", w => w.Supplied ? 1 : null)
        .Reads(row => new WordFixture(row.WordKey(0, "TT"), row.Text(1), row.TextOrNull(3), row.IsNull(4) is false));

    private static readonly ProductSchema<FinalFixture> FinalSchema = new ProductSchema<FinalFixture>()
        .Integer("id", r => r.Id)
        .Text("from", r => r.From)
        .Text("to", r => r.To)
        .Text("relation", r => "renders")
        .OptionalText("roles", r => null)
        .Boolean("settled", r => r.Settled)
        .Text("method", r => r.Method)
        .OptionalText("confidence", r => r.Confidence)
        .Text("source", r => r.Source)
        .Reads(row => new FinalFixture(row.Integer(0), row.Text(1), row.Text(2), row.Boolean(5), row.Text(6), row.TextOrNull(7), row.Text(8)));

    private readonly string _root = Path.Combine(Path.GetTempPath(), "essenthos-products", $"mapping-{Guid.NewGuid():n}");

    private string Essenthos => Path.Combine(_root, ProductStore.EssenthosFolder);

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public void A_text_is_counted_by_the_words_its_links_reach_and_what_settled_them()
    {
        Seed();

        var snapshot = MappingCount.Count(Essenthos, CancellationToken.None);

        var text = snapshot.Texts.Single(t => t.Text == "TT");
        (text.Name, text.Language, text.Kind).Should().Be(("A Test Translation", "eng", "translation"));
        text.Strong.Should().Be(new StrongSource("laid", false, "laid from a test edition"));
        text.Testaments.Should().ContainSingle().Which.Should().Be(
            new TestamentWords("OT", Words: 4, Supplied: 1, Strong: 1, ThroughLinks: 2),
            "a supplied word has no counterpart, and two of the three words without a number of their own are linked");

        var reach = text.Reach.Should().ContainSingle().Which;
        (reach.Original, reach.Testament, reach.Words, reach.Linked, reach.Agreed).Should().Be(("BHSA", "OT", 4, 3, 2));
        (reach.Reached, reach.OriginalWords).Should().Be((2, 3));
        reach.Methods.Select(m => (m.Method, m.Words)).Should().Equal(("stated", 2), ("strong", 1));
        reach.Methods[0].Sources.Should().Equal(new KeyCount("Clear", 2));
        reach.Methods[1].Sources.Should().Equal(new KeyCount("laid", 1));
        reach.Bands.Should().Equal(
            new KeyCount("stated", 2), new KeyCount("0.9", 0), new KeyCount("0.7", 1), new KeyCount("0.5", 0), new KeyCount("low", 0));

        snapshot.Texts.Single(t => t.Text == "BHSA").Reach.Should().BeEmpty("an original is not measured against itself");
        snapshot.Key.Should().Be(MappingCount.Key(Essenthos));
    }

    [Fact]
    public void What_the_count_depends_on_moves_when_a_final_mapping_is_written_again()
    {
        Seed();
        var before = MappingCount.Key(Essenthos);

        WriteFinal([new FinalFixture(1, "1.1:1.1", "1.1:1.1", true, "stated", null, Clear)]);

        MappingCount.Key(Essenthos).Should().NotBe(before);
    }

    [Theory]
    [InlineData("stated", Clear, "Clear")]
    [InlineData("stated", "Door43 alignment of the Indian Revised Version (Hindi) to unfoldingWord's Hebrew Bible", "Door43")]
    [InlineData("stated", "Berean Standard Bible translation tables, bereanbible.com, public domain", "Berean")]
    [InlineData("composed", "SIL.Machine, aligned as written and as stems and through KJV and through BSB", "via:KJV+BSB")]
    [InlineData("aligner", "SIL.Machine ibm4, symmetrised och, position 1.0000", "direct")]
    [InlineData("strong", "the Strong numbers LUTH1912 carries, matched within the verse against BHSA", "own")]
    [InlineData("evidentia", "EVIDENTIA run 14 (1.0.0+65331eb508a9), supplied-article-spa", "EVIDENTIA")]
    [InlineData("ruling", "a ruling of this project: Exodus 11:3: the King James", "owner")]
    public void A_source_goes_by_its_dataset_or_its_route(string method, string source, string name) =>
        MappingCount.SourceName(method, source).Should().Be(name);

    [Fact]
    public void A_measure_is_read_and_a_row_that_does_not_say_enough_is_named()
    {
        var path = Path.Combine(_root, "measures.tsv");
        Directory.CreateDirectory(_root);
        File.WriteAllText(path, string.Join('\n',
            "# producer   by hand",
            "text\toriginal\ttestament\tmethod\tband\tkind\tprecision\tn\tagainst\tsource\tdate\tnote",
            "TT\tBHSA\tOT\tstrong\t>=0.9\tprecision\t95.1\t214582\tClear\tAUD-0115\t2026-11-12\t\\N",
            "TT\tBHSA\tOT\tstrong\tall\testimate\t91\t\\N\tnobody\tAUD-0115\t2026-11-12\t\\N",
            ""));

        var (measures, problems) = MappingBoard.ReadMeasures(path);

        measures.Should().ContainSingle().Which.Should().Be(new MappingMeasure(
            "TT", "BHSA", "OT", "strong", ">=0.9", "precision", 95.1, 214582, "Clear", "AUD-0115", "2026-11-12", null));
        problems.Should().ContainSingle().Which.Should().Contain("Row 2").And.Contain("precision or agreement").And.Contain("n is");
    }

    [Fact]
    public void A_line_broken_by_hand_is_named_and_the_rows_before_it_are_kept()
    {
        var path = Path.Combine(_root, "measures.tsv");
        Directory.CreateDirectory(_root);
        File.WriteAllText(path, string.Join('\n',
            "text\toriginal\ttestament\tmethod\tband\tkind\tprecision\tn\tagainst\tsource\tdate\tnote",
            "TT\tBHSA\tOT\tall\tall\tagreement\t97.6\t158938\tClear\tAUD-0115\t2026-11-12\t\\N",
            "TT\tBHSA\tOT\tall",
            ""));

        var (measures, problems) = MappingBoard.ReadMeasures(path);

        measures.Should().ContainSingle();
        problems.Should().ContainSingle().Which.Should().Contain("fields");
    }

    [Fact]
    public void The_measures_the_checkout_carries_read_without_a_problem()
    {
        var path = Path.Combine(Checkout(), "Resources", "Essenthos", "mapping", "measures.tsv");

        var (measures, problems) = MappingBoard.ReadMeasures(path);

        problems.Should().BeEmpty();
        measures.Should().NotBeEmpty().And.OnlyContain(m => m.N > 0 && m.Source.Length > 0);
    }

    private static string Checkout()
    {
        for (var folder = new DirectoryInfo(AppContext.BaseDirectory); folder is not null; folder = folder.Parent)
        {
            if (File.Exists(Path.Combine(folder.FullName, "Essenthos.Core.sln")))
            {
                return folder.FullName;
            }
        }

        throw new DirectoryNotFoundException("No essenthos-core checkout above the test assembly.");
    }

    private void Seed()
    {
        WriteWords("BHSA", null,
        [
            new(Key("BHSA", 1), "בְּרֵאשִׁית", "H7225", false),
            new(Key("BHSA", 2), "בָּרָא", "H1254", false),
            new(Key("BHSA", 3), "אֱלֹהִים", "H430", false),
        ]);
        WriteWords("TT", "laid from a test edition",
        [
            new(Key("TT", 1), "In", null, false),
            new(Key("TT", 2), "beginning", "H7225", false),
            new(Key("TT", 3), "the", null, true),
            new(Key("TT", 4), "created", null, false),
            new(Key("TT", 5), "God", null, false),
        ]);
        Directory.CreateDirectory(Path.Combine(Essenthos, "texts", "TT"));
        File.WriteAllText(Path.Combine(Essenthos, "texts", "TT", "text.tsv"), string.Join('\n',
            "# producer   test", "field\tvalue", "slug\tTT", "name\tA Test Translation", "kind\ttranslation", "language\teng", ""));

        WriteFinal(
        [
            new(1, "1.1:1.1 1.1:1.2", "1.1:1.1", true, "stated", null, Clear),
            new(1, "1.1:1.1 1.1:1.2", "1.1:1.1", false, "strong", "0.9", "the Strong numbers laid from a test edition"),
            new(2, "1.1:1.4", "1.1:1.2", true, "strong", "0.7", "the Strong numbers laid from a test edition"),
            new(3, "1.1:1.5", "", true, "stated", null, Clear),
        ]);
    }

    private static WordKey Key(string text, int position) => new(text, new VerseKey(1, 1, "", 1, ""), position);

    private void WriteWords(string slug, string? laid, IReadOnlyList<WordFixture> words)
    {
        var folder = Path.Combine(Essenthos, "texts", slug);
        Directory.CreateDirectory(folder);
        var header = new ProductHeader($"Essenthos/texts/{slug}/words.essx", [], "test");
        if (laid is not null)
        {
            header.Notes.Add(new("strong", laid));
            header.Notes.Add(new("strong-served", "no: a test numbering"));
        }

        using var file = EssxWriter.Create(Path.Combine(folder, "words.essx"), header);
        file.Table("rows", WordSchema).WriteAll(words);
        file.Complete();
    }

    private void WriteFinal(IReadOnlyList<FinalFixture> rows)
    {
        var folder = Path.Combine(Essenthos, "mapping", "TT-BHSA");
        Directory.CreateDirectory(folder);
        using var file = EssxWriter.Create(Path.Combine(folder, "final.essx"), new ProductHeader("Essenthos/mapping/TT-BHSA/final.essx", [], "test"));
        file.Table("rows", FinalSchema).WriteAll(rows);
        file.Complete();
    }

    private sealed record WordFixture(WordKey Own, string Surface, string? Strong, bool Supplied);

    private sealed record FinalFixture(long Id, string From, string To, bool Settled, string Method, string? Confidence, string Source);
}
