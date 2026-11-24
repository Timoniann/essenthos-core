using Essenthos.Core.Build;
using Essenthos.Core.Mapping;
using Essenthos.Core.Products;
using FluentAssertions;
using Xunit;

namespace Essenthos.Core.Tests.Links;

/// <summary>
/// The compositions the store build draws again after its producers: one whose merge lost composed rows on a moved verse, or
/// whose texts, middle texts' finals or stored answers moved since it was drawn; never for its own pair's final, which the
/// merge makes from the file it writes.
/// </summary>
public sealed class RedrawsTests : IDisposable
{
    private const string Pair = "RUSV-BHSA";

    private readonly string _root = Path.Combine(Path.GetTempPath(), "essenthos-redraws", Guid.NewGuid().ToString("N"));
    private readonly ProductStore _store;
    private readonly Composition _source = new();

    public RedrawsTests()
    {
        _store = new ProductStore(_root);
        ProductCorpus.Text(_store, "RUSV", "rus", ProductCorpus.Genesis(["в", "начале"]));
        ProductCorpus.Text(_store, "BHSA", "hbo", ProductCorpus.Genesis(["בְּ", "רֵאשִׁית"]));
        var graph = Path.Combine(_root, ProductStore.EssenthosFolder, "build", "products.tsv");
        Directory.CreateDirectory(Path.GetDirectoryName(graph)!);
        File.Copy(TestResources.Shipped(Path.Combine(ProductStore.EssenthosFolder, "build", "products.tsv")), graph);
        Final(Pair, "drawn once");
        Final("KJV-BHSA", "the middle text");
        PairSources.Write(new ProductTexts(_store), MappingMethods.Composed, Pair, force: true, _ => { }, [_source]).Written.Should().NotBeNull();
        _store.Save();
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public void A_composition_drawn_from_what_it_reads_is_not_due_though_its_own_final_moved()
    {
        Final(Pair, "merged again from the file it wrote");

        Due().Should().BeEmpty();
    }

    [Fact]
    public void A_composition_is_due_once_a_middle_texts_final_moved_and_names_the_pair_it_waits_for()
    {
        Final("KJV-BHSA", "the middle text, its names placed again");

        var due = Due().Should().ContainSingle().Subject;
        (due.Pair, due.File).Should().Be((Pair, MappingMethods.Composed));
        due.Why.Should().Contain("mapping/KJV-BHSA/final");
        due.Reads.Should().Equal("KJV-BHSA");
    }

    [Fact]
    public void A_composition_is_due_once_its_texts_words_moved()
    {
        ProductCorpus.Text(_store, "RUSV", "rus", ProductCorpus.Genesis(["в", "начале", "сотворил"]));

        Due().Should().ContainSingle().Which.Why.Should().Contain("texts/RUSV/words");
    }

    [Fact]
    public void A_composition_whose_merge_lost_composed_rows_on_a_moved_verse_is_due()
    {
        using (var report = _store.Create(MappingProducts.Of(Pair, MappingProducts.Report), ProductKind.Machine, ReportRow.Schema, "test", []))
        {
            report.WriteAll([new ReportRow("needs", MappingMethods.Composed, "compose", 0,
                $"verses whose text moved lost their composed rows; {Pair} needs compose again")]);
        }

        Due().Should().ContainSingle().Which.Why.Should().StartWith("its merge says: verses whose text moved");
    }

    [Fact]
    public void A_composition_waits_while_a_pair_whose_final_it_reads_is_due_itself()
    {
        var middle = new Redraws.Due("KJV-BHSA", MappingMethods.Composed, "moved", []);
        var through = new Redraws.Due(Pair, MappingMethods.Composed, "moved", ["KJV-BHSA"]);
        var alone = new Redraws.Due("WEB-NESTLE1904", MappingMethods.Composed, "moved", ["KJV-NESTLE1904"]);

        Redraws.Ready([middle, through, alone]).Should().Equal(middle, alone);
        Redraws.Ready([through]).Should().Equal(through);
    }

    private List<Redraws.Due> Due()
    {
        var store = new ProductStore(_root);
        return Redraws.Find(new ProductTexts(store), ProductGraph.Read(store), [_source]);
    }

    private void Final(string pair, string content)
    {
        var path = Path.Combine(_root, ProductStore.EssenthosFolder, MappingProducts.Folder, pair, "final.tsv");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    /// <summary>A composition of RUSV through KJV that reads its own final and the middle text's, as a declared one does.</summary>
    private sealed class Composition : IPairSource
    {
        public string Method => MappingMethods.Composed;

        public string Name => "composed";

        public bool States(string pair) => pair == Pair;

        public bool Makes(string source) => true;

        public IEnumerable<string> Inputs(ProductStore store, string pair) =>
            [$"{ProductStore.EssenthosFolder}/mapping/{Pair}/final.tsv", $"{ProductStore.EssenthosFolder}/mapping/KJV-BHSA/final.tsv"];

        public IEnumerable<MappingRow> Rows(ProductTexts texts, string pair, Action<string> log) =>
            [new("1.1:1.2", "1.1:1.2", "renders", MappingMethods.Composed, 0.5, "SIL.Machine, aligned as stems and through KJV", null, null, null)];
    }
}
