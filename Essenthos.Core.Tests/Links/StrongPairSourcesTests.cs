using Essenthos.Core.Build;
using Essenthos.Core.Loading.Links;
using Essenthos.Core.Mapping;
using Essenthos.Core.Products;
using FluentAssertions;
using Xunit;

namespace Essenthos.Core.Tests.Links;

/// <summary>
/// The producers of the <c>strong</c> and the King James's <c>lexical</c> method files from files (PRB-1479): which pairs
/// they speak about, which rows are theirs, how a laid edition's tags meet the words product's numbers, and that the
/// declared graph covers what they read.
/// </summary>
public sealed class StrongPairSourcesTests : IDisposable
{
    private sealed record Row(string Value);

    private static readonly ProductSchema<Row> Schema = new ProductSchema<Row>().Text("value", r => r.Value).Reads(r => new Row(r.Text(0)));

    private readonly string _root = Path.Combine(Path.GetTempPath(), "essenthos-strong-sources", Guid.NewGuid().ToString("N"));

    private static IPairSource Source(string method, string pair) =>
        PairSources.All.Single(source => source.Method == method && source.States(pair));

    [Theory]
    [InlineData("LUTH1912-TR1894")]
    [InlineData("RUSV-BHSA")]
    [InlineData("CUVS-WH1881")]
    [InlineData("RV1909-RP2018")]
    [InlineData("RLT2018-TR1550")]
    [InlineData("KJV-TISCH")]
    public void Every_numbering_the_corpus_matched_has_one_strong_source(string pair) =>
        PairSources.All.Count(source => source.Method == MappingMethods.Strong && source.States(pair)).Should().Be(1);

    [Theory]
    [InlineData(MappingMethods.Strong, "KJV-BHSA")]
    [InlineData(MappingMethods.Strong, "RUSV-WH1881")]
    [InlineData(MappingMethods.Lexical, "RUSV-NESTLE1904")]
    public void A_pair_no_load_matched_has_none(string method, string pair) =>
        PairSources.All.Should().NotContain(source => source.Method == method && source.States(pair));

    [Fact]
    public void The_King_James_function_words_speak_only_of_the_Greek() =>
        KingJamesGreekSource.All.Should().NotContain(source => source.States("KJV-BHSA"));

    [Fact]
    public void A_source_makes_the_rows_of_its_numbering_under_any_name_of_the_witness()
    {
        var synodal = Source(MappingMethods.Strong, "RUSV-RP2018");
        synodal.Makes(SynodalStrongLinkLoader.Credit + ", matched within the verse against ROBINSONPIERPONT2018").Should().BeTrue();
        synodal.Makes(SynodalStrongLinkLoader.Credit + ", matched within the verse against RP2018").Should().BeTrue();
        synodal.Makes("the Strong numbers LUTH1912 carries, matched within the verse against RP2018").Should().BeFalse();

        Source(MappingMethods.Strong, "LUTH1912-TR1894").Makes("the Strong numbers LUTH1912 carries, matched within the verse against SCRIVENER1894")
            .Should().BeTrue();
    }

    [Fact]
    public void The_King_James_sources_tell_their_rows_from_the_italics()
    {
        var strong = Source(MappingMethods.Strong, "KJV-TR1894");
        var lexical = Source(MappingMethods.Lexical, "KJV-TR1894");

        strong.Makes("Zefania KJV+ Strong numbers, matched within the verse against scrivener1894").Should().BeTrue();
        strong.Makes("Zefania KJV+ italics, the King James translators' own mark that they supplied the word").Should().BeFalse();
        lexical.Makes("the untagged English function words, matched to the morphology scrivener1894 states").Should().BeTrue();
        lexical.Makes("Zefania KJV+ Strong numbers, matched within the verse against scrivener1894").Should().BeFalse();
        strong.Makes("the untagged English function words, matched to the morphology scrivener1894 states").Should().BeFalse();
    }

    [Fact]
    public void A_number_the_words_product_holds_otherwise_stands_over_the_laid_tag()
    {
        var laid = new Dictionary<long, WordTag>
        {
            [0] = new(["H1", "H2"], 5),
            [1] = new(["H1", "H2"], 5),
            [2] = new(["H3"], 6),
            [3] = new(["H7"], 9),
        };

        var (tags, set, cleared) = StrongNumberSource.Overlay(laid, ["H1", "H1", "H9", null, "H8"]);

        tags[0].Should().Be(laid[0]);
        tags[1].Should().Be(laid[1]);
        tags[2].Numbers.Should().Equal("H9");
        tags[2].Unit.Should().Be(6);
        tags.Should().NotContainKey(3);
        tags[4].Numbers.Should().Equal("H8");
        tags[4].Unit.Should().BeNegative();
        (set, cleared).Should().Be((2, 1));
    }

    [Theory]
    [InlineData("22:1", 22, 1)]
    [InlineData("9a:3", 9, 3)]
    public void A_stated_note_reads_as_the_address_the_text_prints(string content, int chapter, int verse) =>
        StrongNumberSource.Address(content).Should().Be(new Essenthos.Core.XmlBible.VerseAddress(chapter, verse));

    [Fact]
    public void The_declared_graph_covers_what_the_strong_and_lexical_producers_read()
    {
        var graph = ProductGraph.Read(new ProductStore(TestResources.Shipped(".")));
        graph.Find("Essenthos/mapping/RUSV-BHSA/strong.essx")!.Value.Declaration.Producer.Should().Be("strong-pairs");
        graph.Find("Essenthos/mapping/KJV-TR1894/lexical.essx")!.Value.Declaration.Producer.Should().Be("lexical-pairs");

        var store = new ProductStore(_root);
        string[] strong =
        [
            "Essenthos/texts/RUSV/words.essx", "Essenthos/texts/RUSV/frame.essx", "Essenthos/texts/RUSV/notes.essx",
            "Essenthos/texts/NESTLE1904/words.essx", "Essenthos/texts/NESTLE1904/frame.essx", "Essenthos/mapping/RUSV-NESTLE1904/stated.essx",
            StrongNumberSource.GreekDictionary, "SynodalStrong/RSTE_verse_words.xml",
        ];
        string[] luther =
        [
            "Essenthos/texts/LUTH1912/words.essx", "Essenthos/texts/LUTH1912/frame.essx", "Essenthos/texts/LUTH1912/text.tsv",
            "Essenthos/texts/RP2018/words.essx", "Essenthos/texts/RP2018/frame.essx", "Essenthos/texts/RP2018/text.tsv", "Luther1912/01.usfm",
            "Essenthos/texts/BHSA/words.essx", "Essenthos/texts/NESTLE1904/words.essx", "Essenthos/texts/LUTH1912/corrections.tsv",
            "Essenthos/texts/LUTH1912/ids.essx",
        ];
        string[] laid = ["ChineseUnion1919/ChiUn/module.txt", "Segond1910Strong/FreSegond1910/module.txt", "ReinaValera1909/01.usfm"];
        string[] lexical =
        [
            "Essenthos/texts/KJV/words.essx", "Essenthos/texts/KJV/frame.essx", "Essenthos/texts/KJV/corrections.tsv",
            "Essenthos/texts/TR1894/words.essx", "Essenthos/texts/TR1894/frame.essx", KingJamesGreekSource.Zefania, StrongNumberSource.GreekDictionary,
        ];
        foreach (var file in strong.Concat(luther).Concat(laid).Concat(lexical))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(_root, file))!);
            File.WriteAllText(Path.Combine(_root, file), file);
        }

        Make(store, "mapping/RUSV-NESTLE1904/strong", strong);
        Make(store, "mapping/LUTH1912-RP2018/strong", [.. luther.Where(file => !file.StartsWith("Luther1912/", StringComparison.Ordinal)), "Luther1912"]);
        Make(store, "mapping/CUV-BHSA/strong", "ChineseUnion1919/ChiUn");
        Make(store, "mapping/LSG1910-BHSA/strong", "Segond1910Strong/FreSegond1910", "ReinaValera1909");
        Make(store, "mapping/KJV-TR1894/strong", lexical);
        Make(store, "mapping/KJV-TR1894/lexical", lexical);

        graph.Check(store.Manifest, _ => []).Should().BeEmpty();
    }

    private static void Make(ProductStore store, string product, params string[] inputs)
    {
        using var writer = store.Create(product, ProductKind.Machine, Schema, "test", store.Inputs(inputs));
        writer.Write(new Row(product));
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
