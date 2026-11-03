using Essenthos.Core.Products;
using Essenthos.Core.Products.Tsv;
using FluentAssertions;
using Xunit;

namespace Essenthos.Core.Tests.Products;

public sealed class ProductStoreTests : IDisposable
{
    private sealed record Word(WordKey Key, string Surface);

    private static readonly ProductSchema<Word> Schema = new ProductSchema<Word>()
        .OwnWordKey("own", word => word.Key)
        .Text("surface", word => word.Surface)
        .Reads(row => new Word(WordKey.Parse(row.Text(0), "KJV"), row.Text(1)));

    private static readonly Word[] Words =
    [
        new(WordKey.Parse("KJV 1.1:1.1"), "In"),
        new(WordKey.Parse("KJV 1.1:1.2"), "the"),
    ];

    private readonly string _root = Path.Combine(Path.GetTempPath(), "essenthos-products", Guid.NewGuid().ToString("N"));
    private readonly string _source;

    public ProductStoreTests()
    {
        Directory.CreateDirectory(Path.Combine(_root, "bible4u"));
        _source = Path.Combine(_root, "bible4u", "kjv.xml");
        File.WriteAllText(_source, "<kjv/>");
    }

    /// <summary>The store's rules, exercised on the text format where a test can corrupt a file by appending to it.</summary>
    private ProductStore Store() => new(_root, TsvFormat.Instance);

    private static void Make(ProductStore store, IReadOnlyList<ProductInput> inputs)
    {
        using var writer = store.Create("texts/KJV/words", ProductKind.Machine, Schema, "forge convert KJV", inputs);
        writer.WriteAll(Words);
    }

    [Fact]
    public void AMachineProductIsNamedWithoutAnExtensionAndWrittenUnderEssenthosWithTheFormats()
    {
        var store = Store();
        var inputs = store.Inputs("bible4u/kjv.xml");

        Make(store, inputs);

        var relative = "Essenthos/texts/KJV/words.tsv";
        File.Exists(Path.Combine(_root, "Essenthos", "texts", "KJV", "words.tsv")).Should().BeTrue();
        store.Find("texts/KJV/words").Should().Be(relative);
        using var reader = store.Open("texts/KJV/words", Schema);
        reader.Header!.Product.Should().Be(relative);
        reader.Header.Inputs.Should().Equal(inputs);
        reader.Header.Producer.Should().Be("forge convert KJV");
        reader.Header.Rows.Should().Be(2);
        reader.ReadAll().Should().Equal(Words);
    }

    [Fact]
    public void AHandProductIsAlwaysTsvAndAMachineProductIsEssxUnlessSaidOtherwise()
    {
        var store = new ProductStore(_root);
        store.RelativePath("texts/KJV/corrections", ProductKind.Hand).Should().Be("Essenthos/texts/KJV/corrections.tsv");
        store.RelativePath("texts/KJV/words", ProductKind.Machine).Should().Be("Essenthos/texts/KJV/words.essx");
        Store().RelativePath("texts/KJV/words", ProductKind.Machine).Should().Be("Essenthos/texts/KJV/words.tsv");
    }

    [Fact]
    public void AMachineProductWrittenAsEssxIsFoundReadAndRecordedLikeATsvOne()
    {
        var store = new ProductStore(_root);
        var inputs = store.Inputs("bible4u/kjv.xml");
        Make(store, inputs);

        store.Find("texts/KJV/words").Should().Be("Essenthos/texts/KJV/words.essx");
        store.NeedsBuild("texts/KJV/words", ProductKind.Machine, inputs).Should().BeFalse();
        using var reader = store.Open("texts/KJV/words", Schema);
        reader.Header!.Product.Should().Be("Essenthos/texts/KJV/words.essx");
        reader.Header.Inputs.Should().Equal(inputs);
        reader.Header.Rows.Should().Be(2);
        reader.ReadAll().Should().Equal(Words);
        store.Manifest.Get("Essenthos/texts/KJV/words.essx")!.Rows.Should().Be(2);
    }

    [Fact]
    public void ANameWithAnExtensionOrOutsideTheFolderIsRefused()
    {
        var store = Store();

        var extension = () => store.RelativePath("texts/KJV/words.tsv", ProductKind.Machine);
        var outside = () => store.RelativePath("../words", ProductKind.Machine);

        extension.Should().Throw<ArgumentException>();
        outside.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void ACompletedProductIsInTheManifestWithItsHashAndInputs()
    {
        var store = Store();
        var inputs = store.Inputs("bible4u/kjv.xml");
        Make(store, inputs);

        var entry = store.Manifest.Get("Essenthos/texts/KJV/words.tsv");

        entry.Should().NotBeNull();
        entry!.Rows.Should().Be(2);
        entry.Inputs.Should().Equal(inputs);
        entry.Sha256.Should().Be(InputHasher.Sha256Of(Path.Combine(_root, "Essenthos", "texts", "KJV", "words.tsv")));
        entry.Built.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromMinutes(1));
        BuildManifest.Load(_root).Get(entry.Product).Should().Be(entry, "the manifest is saved as each product completes");
    }

    [Fact]
    public void TheBuildRuleProductMissingEntryMissingInputMovedOrFileChanged()
    {
        var store = Store();
        var inputs = store.Inputs("bible4u/kjv.xml");
        store.NeedsBuild("texts/KJV/words", ProductKind.Machine, inputs).Should().BeTrue("nothing has been made");

        Make(store, inputs);
        store.NeedsBuild("texts/KJV/words", ProductKind.Machine, inputs).Should().BeFalse();
        Store().NeedsBuild("texts/KJV/words", ProductKind.Machine, inputs).Should().BeFalse("a fresh store reads the same manifest");

        File.WriteAllText(_source, "<kjv>changed</kjv>");
        var moved = Store().Inputs("bible4u/kjv.xml");
        Store().NeedsBuild("texts/KJV/words", ProductKind.Machine, moved).Should().BeTrue("an input's hash moved");
        Store().NeedsBuild("texts/KJV/words", ProductKind.Machine, [.. inputs, new ProductInput("x/y.txt", new string('1', 64))]).Should().BeTrue("an input was added");
        Store().NeedsBuild("texts/KJV/words", ProductKind.Machine, []).Should().BeTrue("an input was dropped");

        var product = Path.Combine(_root, "Essenthos", "texts", "KJV", "words.tsv");
        File.AppendAllText(product, "1.1:1.3\tbeginning\n");
        Store().NeedsBuild("texts/KJV/words", ProductKind.Machine, inputs).Should().BeTrue("the file is not the one the manifest recorded");

        File.Delete(product);
        Store().NeedsBuild("texts/KJV/words", ProductKind.Machine, inputs).Should().BeTrue("the file is gone");
    }

    [Fact]
    public void AProductCitesAnotherProductAsAnInputByItsFile()
    {
        var store = Store();
        Make(store, store.Inputs("bible4u/kjv.xml"));

        var input = store.ProductInput("texts/KJV/words");

        input.Path.Should().Be("Essenthos/texts/KJV/words.tsv");
        input.Sha256.Should().Be(store.Manifest.Get(input.Path)!.Sha256);
    }

    [Fact]
    public void AWriterThatDoesNotCompleteChangesNeitherTheFileNorTheManifest()
    {
        var store = Store();
        var inputs = store.Inputs("bible4u/kjv.xml");
        Make(store, inputs);
        var entry = store.Manifest.Get("Essenthos/texts/KJV/words.tsv");

        using (var writer = store.Create("texts/KJV/words", ProductKind.Machine, Schema, "forge convert KJV", inputs))
        {
            writer.Write(new Word(WordKey.Parse("KJV 1.1:1.1"), "Im"));
        }

        store.Manifest.Get("Essenthos/texts/KJV/words.tsv").Should().Be(entry);
        using var reader = store.Open("texts/KJV/words", Schema);
        reader.ReadAll().Should().Equal(Words);
    }

    [Fact]
    public void TheHeaderMayCarryAProducersOwnNotes()
    {
        var store = Store();
        using (var writer = store.Create("texts/KJV/words", ProductKind.Machine, Schema, "forge convert KJV", [],
                   header => header.Notes.Add(new("tradition", "protestant"))))
        {
            writer.WriteAll(Words);
        }

        using var reader = store.Open("texts/KJV/words", Schema);
        reader.Header!.Notes.Should().BeEquivalentTo(new[] { new KeyValuePair<string, string>("tradition", "protestant") });
    }

    [Fact]
    public void TheManifestRoundTripsThroughItsFile()
    {
        var manifest = BuildManifest.Load(_root);
        var header = new ProductHeader("Essenthos/a/b.tsv", [new ProductInput("x/y.txt", new string('2', 64)), new ProductInput("Essenthos/c.tsv", new string('3', 64))], "forge x   (Essenthos.Forge 1.0)") { Rows = 7 };
        var built = new DateTimeOffset(2026, 11, 3, 12, 0, 0, TimeSpan.Zero);

        var entry = manifest.Record(header, new string('4', 64), built);

        BuildManifest.Load(_root).Entries.Should().Equal(entry);
        manifest.Forget("Essenthos/a/b.tsv");
        BuildManifest.Load(_root).Entries.Should().BeEmpty();
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);
}
