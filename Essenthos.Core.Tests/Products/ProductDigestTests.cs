using Essenthos.Core.Products;
using Essenthos.Core.Products.Essx;
using Essenthos.Core.Products.Tsv;
using FluentAssertions;
using Xunit;

namespace Essenthos.Core.Tests.Products;

/// <summary>
/// A product's hash is its rows and header without its provenance: made again by another Forge, or from inputs that moved
/// without changing what it holds, it keeps its hash, and nothing made from it is made again.
/// </summary>
public sealed class ProductDigestTests : IDisposable
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

    private const string Source = "bible4u/kjv.xml";

    private readonly string _root = Path.Combine(Path.GetTempPath(), "essenthos-products", Guid.NewGuid().ToString("N"));

    public ProductDigestTests()
    {
        Directory.CreateDirectory(Path.Combine(_root, "bible4u"));
        File.WriteAllText(Path.Combine(_root, "bible4u", "kjv.xml"), "<kjv/>");
    }

    public static TheoryData<string> Formats => new() { "essx", "tsv" };

    private ProductStore Store(string format) => new(_root, format == "tsv" ? TsvFormat.Instance : null);

    private static string Forge(string commit) => $"forge convert KJV   (Essenthos.Forge 1.0.0+{commit})";

    private static string Make(ProductStore store, string product, string producer, IReadOnlyList<ProductInput> inputs, IEnumerable<Word> rows)
    {
        using (var writer = store.Create(product, ProductKind.Machine, Schema, producer, inputs))
        {
            writer.WriteAll(rows);
        }

        return store.Manifest.Get(store.Find(product)!)!.Sha256;
    }

    private void MoveSource(string text)
    {
        var path = Path.Combine(_root, "bible4u", "kjv.xml");
        File.WriteAllText(path, text);
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddMinutes(1));
    }

    [Theory]
    [MemberData(nameof(Formats))]
    public void AnotherForgeWritingTheSameRowsLeavesTheHash(string format)
    {
        var store = Store(format);
        var inputs = store.Inputs(Source);
        var before = Make(store, "texts/KJV/words", Forge("2e96249e"), inputs, Words);

        var after = Make(store, "texts/KJV/words", Forge("7c68b2c3-dirty"), inputs, Words);

        after.Should().Be(before);
        using var reader = store.Open("texts/KJV/words", Schema);
        reader.Header!.Producer.Should().Be(Forge("7c68b2c3-dirty"), "the file still says what wrote it");
    }

    [Theory]
    [MemberData(nameof(Formats))]
    public void AProductMadeAgainFromAMovedInputWithTheSameRowsStopsTheCascade(string format)
    {
        var store = Store(format);
        var words = Make(store, "texts/KJV/words", Forge("a"), store.Inputs(Source), Words);
        var consumerInputs = new[] { store.ProductInput("texts/KJV/words") };
        Make(store, "texts/KJV/index/forms", "forge indexes", consumerInputs, Words);

        MoveSource("<kjv version='2'/>");
        var moved = new ProductStore(_root, store.MachineFormat);
        var inputs = moved.Inputs(Source);
        moved.NeedsBuild("texts/KJV/words", ProductKind.Machine, inputs).Should().BeTrue();
        Make(moved, "texts/KJV/words", Forge("a"), inputs, Words).Should().Be(words);

        moved.NeedsBuild("texts/KJV/index/forms", ProductKind.Machine, [moved.ProductInput("texts/KJV/words")]).Should().BeFalse();
        moved.NeedsBuild("texts/KJV/words", ProductKind.Machine, moved.Inputs(Source)).Should().BeFalse("the manifest records the inputs it was made from now");
        using var reader = moved.Open("texts/KJV/words", Schema);
        reader.Header!.Inputs.Should().Equal(inputs, "the header names the inputs as they stand, hashes and all");
    }

    [Theory]
    [MemberData(nameof(Formats))]
    public void RowsThatMovedMoveTheHashAndWhatReadsThem(string format)
    {
        var store = Store(format);
        var words = Make(store, "texts/KJV/words", Forge("a"), store.Inputs(Source), Words);
        Make(store, "texts/KJV/index/forms", "forge indexes", [store.ProductInput("texts/KJV/words")], Words);

        Make(store, "texts/KJV/words", Forge("a"), store.Inputs(Source), [Words[0], Words[1] with { Surface = "The" }]).Should().NotBe(words);

        store.NeedsBuild("texts/KJV/index/forms", ProductKind.Machine, [store.ProductInput("texts/KJV/words")]).Should().BeTrue();
    }

    [Theory]
    [MemberData(nameof(Formats))]
    public void ANoteOrAnInputAddedMovesTheHash(string format)
    {
        var store = Store(format);
        var plain = Make(store, "texts/KJV/words", Forge("a"), store.Inputs(Source), Words);

        using (var writer = store.Create("texts/KJV/words", ProductKind.Machine, Schema, Forge("a"), store.Inputs(Source),
                   header => header.Notes.Add(new("tradition", "kjv"))))
        {
            writer.WriteAll(Words);
        }

        store.Manifest.Get(store.Find("texts/KJV/words")!)!.Sha256.Should().NotBe(plain);
        Directory.CreateDirectory(Path.Combine(_root, "Strong"));
        File.WriteAllText(Path.Combine(_root, "Strong", "greek.xml"), "<g/>");
        Make(store, "texts/KJV/words", Forge("a"), store.Inputs(Source, "Strong/greek.xml"), Words).Should().NotBe(plain);
    }

    [Fact]
    public void AFileThatIsNoProductIsItsBytes()
    {
        ProductDigest.Of(Path.Combine(_root, "bible4u", "kjv.xml")).Should().Be(InputHasher.Sha256Of(Path.Combine(_root, "bible4u", "kjv.xml")));
    }

    [Fact]
    public void AHandFileIsReadWholeButForItsProvenance()
    {
        var path = Path.Combine(_root, "rows.tsv");
        File.WriteAllText(path, "# product    Essenthos/rows.tsv\n# inputs     a/b.xml@00ff   c@11\n# producer   by hand\nown\tsurface\n1.1:1.1\tIn\n");
        var digest = ProductDigest.Of(path);

        File.WriteAllText(path, "# product    Essenthos/rows.tsv\n# inputs     a/b.xml@aaaa   c@22\n# producer   by hand\nown\tsurface\n1.1:1.1\tIn\n");
        ProductDigest.Of(path).Should().Be(digest);

        File.WriteAllText(path, "# product    Essenthos/rows.tsv\n# inputs     a/b.xml@aaaa   c@22\n# producer   by hand\n# a comment\nown\tsurface\n1.1:1.1\tIn\n");
        ProductDigest.Of(path).Should().NotBe(digest);
        File.WriteAllText(path, "# product    Essenthos/rows.tsv\n# inputs     a/b.xml@aaaa   c@22\n# producer   by hand\nown\tsurface\n1.1:1.1\tin\n");
        ProductDigest.Of(path).Should().NotBe(digest);
    }

    [Fact]
    public void AManifestEntryOfTheWholeFileIsTheSameFileAndIsRecordedAgainByItsDigest()
    {
        var store = Store("essx");
        var digest = Make(store, "texts/KJV/words", Forge("a"), store.Inputs(Source), Words);
        var relative = store.Find("texts/KJV/words")!;
        var whole = InputHasher.Sha256Of(store.FullPath(relative));
        whole.Should().NotBe(digest);
        store.Manifest.Rehash([(relative, whole)]);

        var reopened = new ProductStore(_root);
        ProductIntegrity.CheckEssx(reopened, 2).Should().ContainSingle().Which.Problem.Should().BeNull();

        BuildManifest.Load(_root).Get(relative)!.Sha256.Should().Be(digest);
    }

    [Fact]
    public void AHashMemoryOfAnotherRuleIsNotTrustedForProducts()
    {
        var store = Store("essx");
        var digest = Make(store, "texts/KJV/words", Forge("a"), store.Inputs(Source), Words);
        store.Save();
        var cache = Path.Combine(_root, InputHasher.CacheProduct);
        var lines = File.ReadAllLines(cache)
            .Where(line => !line.StartsWith("# digest", StringComparison.Ordinal))
            .Select(line => line.Contains("words.essx", StringComparison.Ordinal) ? line[..line.LastIndexOf('\t')] + "\t" + new string('0', 64) : line);
        File.WriteAllText(cache, string.Join('\n', lines) + "\n");

        var hasher = new InputHasher(_root);
        hasher.Hash(store.Find("texts/KJV/words")!).Should().Be(digest);
        hasher.Hashed.Should().Be(1, "the source is still taken from the memory, the product is read again");
        hasher.Hash(Source);
        hasher.Hashed.Should().Be(1);
    }

    [Fact]
    public void TheEssxDigestIsTheOneItsVerifyComputes()
    {
        var store = Store("essx");
        var digest = Make(store, "texts/KJV/words", Forge("a"), store.Inputs(Source), Words);
        using var reader = EssxReader.Open(store.FullPath(store.Find("texts/KJV/words")!));

        reader.Verify(out _, out var whole, out var verified).Should().BeTrue();

        verified.Should().Be(digest);
        reader.Digest().Should().Be(digest);
        whole.Should().Be(InputHasher.Sha256Of(reader.Path));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}
