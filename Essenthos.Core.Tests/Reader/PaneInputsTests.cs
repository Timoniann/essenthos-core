using Essenthos.Core.Build;
using Essenthos.Core.Loading.Frame;
using Essenthos.Core.Mapping;
using Essenthos.Core.Names;
using Essenthos.Core.Products;
using FluentAssertions;
using Xunit;

namespace Essenthos.Core.Tests.Reader;

/// <summary>
/// A pane's key moves with what reaches its chapter and nothing else: a link's rows count in the chapters of the words it
/// names on each side, a verse the frame moves counts where it stands, a record's rows count where a word names it, and an
/// input read whole moves every key of the text.
/// </summary>
public sealed class PaneInputsTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "essenthos-products", Guid.NewGuid().ToString("N"));

    private static readonly (int Book, int Chapter)[] Chapters = [(1, 1), (1, 2), (1, 3)];

    private static readonly (string From, string To)[] Pairs = [("KJV", "BHSA")];

    public PaneInputsTests() => Directory.CreateDirectory(_root);

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private sealed record Record(string Slug, string Kind, string Name);

    private static readonly ProductSchema<Record> RecordSchema = new ProductSchema<Record>()
        .Text("slug", r => r.Slug).Text("kind", r => r.Kind).Text("name", r => r.Name)
        .Reads(row => new Record(row.Text(0), row.Text(1), row.Text(2)));

    [Fact]
    public void ALinkMovesTheChaptersOfItsWordsOnEachSideAndAMovedVerseCountsWhereItStands()
    {
        var store = Store();
        var kjv = Keys(store, "KJV");
        var bhsa = Keys(store, "BHSA");

        Final(store, confidence: 0.5, moved: 0.4);
        var after = Keys(store, "KJV");
        var bhsaAfter = Keys(store, "BHSA");
        Moved(kjv, after).Should().Equal([(1, 3)], "the link on KJV 1.3:1 changed");
        Moved(bhsa, bhsaAfter).Should().Equal([(1, 3)]);

        Final(store, confidence: 0.5, moved: 0.9);
        Moved(after, Keys(store, "KJV")).Should().Equal([(1, 1)], "KJV 1.2:1 stands at 1.1:32 in the frame");
        Moved(bhsaAfter, Keys(store, "BHSA")).Should().Equal([(1, 2)], "BHSA has no frame, so its 1.2:1 stands in its own chapter");
    }

    [Fact]
    public void ARecordsRowMovesOnlyTheChaptersWhereAWordNamesIt()
    {
        var store = Store();
        var before = Keys(store, "KJV");
        Records(store, new Record("jubal", "person", "Jubal the musician"), new Record("adam", "person", "Adam"));
        Moved(before, Keys(store, "KJV")).Should().Equal([(1, 3)], "only 1.3:1 names jubal");
    }

    [Fact]
    public void AnInputReadWholeMovesEveryKeyAndOneReadByRowMovesNone()
    {
        var store = Store();
        var inputs = new List<ProductInput> { new("Essenthos/texts/KJV/words.essx", "aa"), new("Essenthos/mapping/KJV-BHSA/final.essx", "bb") };
        var paneInputs = PaneInputs.Read(store, ["KJV"], Pairs, 2);
        var before = paneInputs.Keys("KJV", inputs, Chapters);

        paneInputs.Keys("KJV", [inputs[0], inputs[1] with { Sha256 = "cc" }], Chapters).Should().Equal(before);
        paneInputs.Keys("KJV", [inputs[0] with { Sha256 = "dd" }, inputs[1]], Chapters).Values.Should().NotIntersectWith(before.Values);
    }

    private ProductStore Store()
    {
        var store = new ProductStore(_root);
        using (var frame = store.Create(FrameFile.Product("KJV"), ProductKind.Machine, FrameRow.Schema, "test", []))
        {
            frame.Write(new FrameRow(VerseKey.Parse("1.2:1"), null, [VerseKey.Parse("1.1:32")], "moved"));
            frame.Complete();
        }

        Final(store, confidence: 0.4, moved: 0.4);
        using (var names = store.Create(NameProducts.Projected("KJV"), ProductKind.Machine, ProjectedName.Schema("KJV"), "test", []))
        {
            names.Write(new ProjectedName(WordKey.Parse("1.1:1.1", "KJV"), "adam", "name", "projected", "BHSA 1.1:1.1", "strong-number", 0.9, "test"));
            names.Write(new ProjectedName(WordKey.Parse("1.3:1.2", "KJV"), "jubal", "name", "projected", "BHSA 1.3:1.1", "strong-number", 0.9, "test"));
            names.Complete();
        }

        Records(store, new Record("jubal", "person", "Jubal"), new Record("adam", "person", "Adam"));
        return store;
    }

    private static void Final(ProductStore store, double confidence, double moved)
    {
        using var final = store.Create(MappingProducts.Of("KJV-BHSA", MappingProducts.Final), ProductKind.Machine, FinalRow.Schema, "test", []);
        final.Write(new FinalRow(1, "1.1:1.1", "1.1:1.1", "renders", null, true, "aligner", 0.8, "test"));
        final.Write(new FinalRow(2, "1.2:1.1", "1.2:1.1", "renders", null, true, "aligner", moved, "test"));
        final.Write(new FinalRow(3, "1.3:1.1 1.3:1.2", "1.3:1.1", "renders", null, true, "aligner", confidence, "test"));
        final.Complete();
    }

    private static void Records(ProductStore store, params Record[] records)
    {
        using var writer = store.Create("records/records", ProductKind.Hand, RecordSchema, "test", []);
        foreach (var record in records)
        {
            writer.Write(record);
        }

        writer.Complete();
    }

    private static Dictionary<(int Book, int Chapter), string> Keys(ProductStore store, string text) =>
        PaneInputs.Read(store, [text], Pairs, 2).Keys(text, [], Chapters);

    private static List<(int Book, int Chapter)> Moved(Dictionary<(int Book, int Chapter), string> before, Dictionary<(int Book, int Chapter), string> after) =>
        [.. before.Keys.Where(k => before[k] != after[k]).Order()];
}
