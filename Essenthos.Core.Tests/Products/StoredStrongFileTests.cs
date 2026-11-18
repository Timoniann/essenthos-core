using Essenthos.Core.Conversion;
using Essenthos.Core.Indexes;
using Essenthos.Core.Loading.Frame;
using Essenthos.Core.Mapping;
using Essenthos.Core.Products;
using FluentAssertions;
using Xunit;

namespace Essenthos.Core.Tests.Products;

/// <summary>
/// What the Strong pages read that the words, the frame and the links decide, as files per text: every numbered word at its
/// verse's primary frame place (<c>strong-occurrences</c>) and the links joining the text to each edition that is not a
/// translation (<c>pair-links</c>), each made again only when its own inputs moved.
/// </summary>
public sealed class StoredStrongFileTests : IDisposable
{
    private readonly string _resources = Path.Combine(Path.GetTempPath(), "essenthos-products", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_resources))
        {
            Directory.Delete(_resources, recursive: true);
        }
    }

    private static void Declare(ProductStore store, string slug, string kind)
    {
        using var writer = store.Create(TextField.ProductOf(slug), ProductKind.Hand, TextField.Schema, "test", []);
        writer.WriteAll([new TextField("slug", slug), new TextField("kind", kind), new TextField("language", "eng")]);
    }

    private static void Words(ProductStore store, string slug, params (VerseKey Verse, string? Strong)[] words)
    {
        using var writer = store.Create(TextWord.ProductOf(slug), ProductKind.Machine, TextWord.Schema(slug), "test", []);
        writer.WriteAll(words.Select((w, at) => new TextWord(new WordKey(slug, w.Verse, at + 1),
            "w", " ", null, w.Strong, null, null, "w", null, false, null, null, null)));
    }

    private static void Frame(ProductStore store, string slug, params FrameRow[] rows)
    {
        using var writer = store.Create(FrameFile.Product(slug), ProductKind.Machine, FrameRow.Schema, "test", []);
        writer.WriteAll(rows);
    }

    private static void Final(ProductStore store, string pair, int links)
    {
        using var writer = store.Create(MappingProducts.Of(pair, MappingProducts.Final), ProductKind.Machine, FinalRow.Schema, "test", []);
        for (var id = 1; id <= links; id++)
        {
            writer.Write(new FinalRow(id, "1.1:1.1", "1.1:1.1", "renders", null, true, "aligner", null, "test"));
            writer.Write(new FinalRow(id, "1.1:1.1", "1.1:1.1", "renders", null, false, "strong", null, "test"));
        }

        writer.Complete();
    }

    private static readonly VerseKey Genesis11 = new(1, 1, 1);
    private static readonly VerseKey Malachi319 = new(39, 3, 19);

    /// <summary>
    /// HEB and GRK are editions, HEB numbered; ENG and TAG are translations, ENG with a frame moving Malachi 3:19 to 4:1, TAG with
    /// none. ENG is linked to HEB both ways, to GRK one way, and to TAG.
    /// </summary>
    private ProductStore Corpus()
    {
        var store = new ProductStore(_resources);
        Declare(store, "HEB", "critical-edition");
        Declare(store, "GRK", "critical-edition");
        Declare(store, "ENG", "translation");
        Declare(store, "TAG", "translation");
        Words(store, "HEB", (Genesis11, "H7225"), (Genesis11, null));
        Words(store, "GRK", (Genesis11, null));
        Words(store, "ENG", (Genesis11, null), (Malachi319, "H2009"), (Malachi319, null));
        Words(store, "TAG", (Genesis11, "H1"));
        Frame(store, "HEB");
        Frame(store, "GRK");
        Frame(store, "ENG", new FrameRow(Malachi319, null, [new VerseKey(39, 4, 1)], "moved"));
        Final(store, "ENG-HEB", 3);
        Final(store, "HEB-ENG", 2);
        Final(store, "ENG-GRK", 1);
        Final(store, "TAG-ENG", 4);
        store.Save();
        return store;
    }

    private static List<T> Read<T>(ProductStore store, string product, ProductSchema<T> schema)
    {
        using var reader = store.Open(product, schema);
        return [.. reader.Rows()];
    }

    [Fact]
    public void ANumberedWordStandsAtItsVersesPrimaryFramePlace()
    {
        var store = Corpus();
        StoredStrongFiles.Make(new IndexContext(store), force: false, parallel: 2);

        Read(store, StoredStrongFiles.Occurrences("ENG"), StrongOccurrenceRow.Schema("ENG"))
            .Should().Equal(new StrongOccurrenceRow(new WordKey("ENG", Malachi319, 2), "H2009", 39, 4, 1));
        Read(store, StoredStrongFiles.Occurrences("HEB"), StrongOccurrenceRow.Schema("HEB"))
            .Should().Equal(new StrongOccurrenceRow(new WordKey("HEB", Genesis11, 1), "H7225", 1, 1, 1));
        store.Exists(StoredStrongFiles.Occurrences("TAG")).Should().BeFalse("the frame places no verse of a text without a frame file");
    }

    [Fact]
    public void ATextCountsItsLinksToEachEditionBothWaysAndNoneToATranslation()
    {
        var store = Corpus();
        StoredStrongFiles.Make(new IndexContext(store), force: false, parallel: 2);

        Read(store, StoredStrongFiles.PairLinks("ENG"), PairLinksRow.Schema)
            .Should().Equal(new PairLinksRow("GRK", 1, false), new PairLinksRow("HEB", 5, true));
        Read(store, StoredStrongFiles.PairLinks("HEB"), PairLinksRow.Schema).Should().BeEmpty("ENG is a translation");
        Read(store, StoredStrongFiles.PairLinks("TAG"), PairLinksRow.Schema).Should().BeEmpty();
    }

    [Fact]
    public void OnlyTheFilesOfTheTextsWhoseInputsMovedAreMadeAgain()
    {
        StoredStrongFiles.Make(new IndexContext(Corpus()), force: false, parallel: 2);
        StoredStrongFiles.Make(new IndexContext(new ProductStore(_resources)), force: false, parallel: 2)
            .Where(o => o.Made).Should().BeEmpty();

        var relinked = new ProductStore(_resources);
        Final(relinked, "ENG-GRK", 2);
        relinked.Save();
        StoredStrongFiles.Make(new IndexContext(new ProductStore(_resources)), force: false, parallel: 2)
            .Where(o => o.Made).Select(o => (o.Text, o.Index)).Should().Equal([("ENG", "pair-links")],
                "GRK counts no links to a translation, so only ENG reads the final");

        var reframed = new ProductStore(_resources);
        Frame(reframed, "ENG");
        reframed.Save();
        StoredStrongFiles.Make(new IndexContext(new ProductStore(_resources)), force: false, parallel: 2)
            .Where(o => o.Made).Select(o => (o.Text, o.Index)).Should().Equal([("ENG", "occurrences")]);

        var renumbered = new ProductStore(_resources);
        Words(renumbered, "GRK", (Genesis11, "G1"));
        renumbered.Save();
        StoredStrongFiles.Make(new IndexContext(new ProductStore(_resources)), force: false, parallel: 2)
            .Where(o => o.Made).Select(o => (o.Text, o.Index)).Should().BeEquivalentTo(
                [("GRK", "occurrences"), ("ENG", "pair-links")], "ENG's row says whether GRK is numbered, and GRK counts links to no edition");
        Read(new ProductStore(_resources), StoredStrongFiles.PairLinks("ENG"), PairLinksRow.Schema)[0].Should().Be(new PairLinksRow("GRK", 2, true));
    }
}
