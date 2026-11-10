using Essenthos.Core.Conversion;
using Essenthos.Core.Loading.Frame;
using Essenthos.Core.Products;
using FluentAssertions;
using Xunit;

namespace Essenthos.Core.Tests.Witnesses;

/// <summary>
/// A text's frame file records the inputs its check asks for, the list of titled psalms among them, so a second build
/// with nothing moved finds every frame current and writes none.
/// </summary>
public sealed class FrameProductsTests : IDisposable
{
    private const string Slug = "KJV";

    private readonly string _root = Path.Combine(Path.GetTempPath(), "essenthos-products", Guid.NewGuid().ToString("N"));

    public FrameProductsTests()
    {
        Directory.CreateDirectory(Path.Combine(_root, "Versification"));
        File.WriteAllText(Path.Combine(_root, "Versification", "TVTMS.txt"), "#DataStart(Expanded)\nEnglish\tMal.4:1\tMal.3:19\n#DataEnd\n");
        var versification = Path.Combine(_root, "Essenthos", "versification");
        Directory.CreateDirectory(versification);
        foreach (var file in Directory.GetFiles(TestResources.Shipped(Path.Combine("Essenthos", "versification")), "*.tsv"))
        {
            File.Copy(file, Path.Combine(versification, Path.GetFileName(file)));
        }

        var store = new ProductStore(_root);
        using (var declared = store.Create(TextField.ProductOf(Slug), ProductKind.Hand, TextField.Schema, "test", []))
        {
            declared.WriteAll([new TextField("slug", Slug), new TextField("versification", "english")]);
        }

        var verses = new[] { ("1.1:1", "In the beginning"), ("1.1:2", "And the earth"), ("19.3:1", "LORD how are they increased") };
        using (var writer = store.Create(TextWord.ProductOf(Slug), ProductKind.Machine, TextWord.Schema(Slug), "test", []))
        {
            writer.WriteAll(verses.SelectMany(verse => verse.Item2.Split(' ').Select((surface, n) => new TextWord(
                new WordKey(Slug, VerseKey.Parse(verse.Item1), n + 1), null, surface, " ", null, null, null, null, null, null, false, null, null, null))));
        }

        using (var writer = store.Create(VerseManifestRow.ProductOf(Slug), ProductKind.Machine, VerseManifestRow.Schema, "test", []))
        {
            writer.WriteAll(verses.Select((verse, n) => new VerseManifestRow(VerseKey.Parse(verse.Item1), n + 1, verse.Item2.Split(' ').Length,
                VerseHashes.Of(verse.Item2.Split(' ').Select(surface => new VerseWord(surface, " ", null, null, null, null))))));
        }

        using var titles = store.Create(TextTitle.ProductOf(Slug), ProductKind.Machine, TextTitle.Schema, "test", []);
        titles.WriteAll([new TextTitle(VerseKey.Parse("19.3:1"), "test")]);
    }

    private (FrameProducts.Outcome Outcome, ProductStore Store) Build()
    {
        var store = new ProductStore(_root);
        var rules = TvtmsReader.Read(Path.Combine(_root, "Versification", "TVTMS.txt"), VersificationData.Of(_root));
        var outcome = FrameProducts.Write(store, rules, new HashSet<string> { Slug }, force: false, parallel: 1).Should().ContainSingle().Subject;
        store.Save();
        return (outcome, store);
    }

    [Fact]
    public void ASecondBuildWithNothingMovedWritesNoFrame()
    {
        Build().Outcome.Written.Should().BeTrue();

        Build().Outcome.Written.Should().BeFalse();
    }

    [Fact]
    public void TheFrameRecordsTheTitledPsalmsItWasMadeWith()
    {
        var store = Build().Store;

        var entry = store.Manifest.Get(store.Find(FrameFile.Product(Slug))!)!;

        entry.Inputs.Select(input => input.Path).Should().Contain(store.Find(FrameProducts.TitledPsalms));
        using var reader = store.Open(FrameFile.Product(Slug), FrameRow.Schema);
        reader.Header!.Inputs.Should().Equal(entry.Inputs);
    }

    [Fact]
    public void ATitledPsalmAddedMakesTheFrameAgain()
    {
        Build();

        File.AppendAllText(Path.Combine(_root, "Essenthos", "versification", "titled-psalms.tsv"), "19\t1\n");

        Build().Outcome.Written.Should().BeTrue();
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}
