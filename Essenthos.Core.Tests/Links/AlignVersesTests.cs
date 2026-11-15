using Essenthos.Core.Mapping;
using Essenthos.Core.Products;
using FluentAssertions;
using Xunit;

namespace Essenthos.Core.Tests.Links;

/// <summary>
/// A method file written again within some verses of its FROM text (<c>align --verses</c>, PRB-1681): the rows of a FROM
/// word in those verses are replaced, every other row stays as the file held it, the header says so and claims no remake,
/// and a verse's hash is renewed only where no row kept from before cites it.
/// </summary>
public sealed class AlignVersesTests : IDisposable
{
    private const string Pair = "RUSV-KJV";

    private readonly string _root = Path.Combine(Path.GetTempPath(), "essenthos-products", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, true);
        }
    }

    [Fact]
    public void Rows_outside_the_verses_stay_and_only_hashes_no_kept_row_cites_are_renewed()
    {
        var store = new ProductStore(_root);
        Manifest(store, "RUSV", ("17.3:13", "r0"), ("17.3:13a", "n1"), ("17.3:13b", "n2"), ("17.4:1", "r4"));
        Manifest(store, "KJV", ("17.13:1", "k1"), ("17.13:2", "k2"));
        var outside = Row("17.3:13.1", "17.13:1.1", 0.5);
        var crossing = Row("17.4:1.1", "17.13:2.1", 0.6);
        using (var writer = store.Create(MappingProducts.Of(Pair, MappingMethods.Aligner), ProductKind.Machine, MappingRow.Schema, "test", []))
        {
            writer.WriteAll([outside, Row("17.3:13a.5", "17.13:1.3", 0.7), crossing]);
        }

        using (var writer = store.Create(MappingProducts.Of(Pair, MappingProducts.Hashes), ProductKind.Machine, VerseHashRow.Schema, "test", []))
        {
            writer.WriteAll([
                new VerseHashRow("KJV", VerseKey.Parse("17.13:1"), "k1"),
                new VerseHashRow("KJV", VerseKey.Parse("17.13:2"), "moved"),
                new VerseHashRow("RUSV", VerseKey.Parse("17.3:13"), "r0"),
                new VerseHashRow("RUSV", VerseKey.Parse("17.3:13a"), "before"),
            ]);
        }

        var made = new[] { Row("17.3:13a.1", "17.13:1.1", 0.9), Row("17.3:13b.2", "17.13:2.2", 0.8), Row("17.3:13.1", "17.13:1.2", 0.4) };
        var (written, held) = MethodFiles.WriteVerses(store, Pair, MappingMethods.Aligner, made, [VerseRange.Parse("17.3:13a..13g")],
            "forge align RUSV KJV --verses 17.3:13a..13g", []);

        using (var reader = store.Open(MappingProducts.Of(Pair, MappingMethods.Aligner), MappingRow.Schema))
        {
            reader.ReadAll().Should().BeEquivalentTo([outside, crossing, made[0], made[1]]);
            reader.Header!.Notes.Should().Contain(note => note.Key == MethodFiles.VersesNote)
                .And.NotContain(note => note.Key == MethodFiles.RemadeNote, "the rows kept from before were not made from these words");
        }

        written.Carried.Should().Be(2);
        written.Gone.Should().Be(1);
        var hashes = VerseHashRow.ReadAll(store, MappingProducts.Of(Pair, MappingProducts.Hashes)).ToDictionary(r => (r.Text, r.Own.ToString()), r => r.Hash);
        hashes[("RUSV", "17.3:13a")].Should().Be("n1", "every row citing it was made now");
        hashes[("RUSV", "17.3:13b")].Should().Be("n2");
        hashes[("KJV", "17.13:2")].Should().Be("moved", "a kept row cites it, and renewing it would let that row stand");
        held.Should().Be(1, "the new row on KJV 13:2 waits with the kept one");
    }

    private static MappingRow Row(string from, string to, double confidence) =>
        new(from, to, "renders", MappingMethods.Aligner, confidence, "SIL.Machine ibm4, symmetrised och", null, null, null);

    private static void Manifest(ProductStore store, string text, params (string Verse, string Hash)[] verses)
    {
        using var writer = store.Create(VerseManifestRow.ProductOf(text), ProductKind.Machine, VerseManifestRow.Schema, "test", []);
        writer.WriteAll(verses.Select((verse, n) => new VerseManifestRow(VerseKey.Parse(verse.Verse), n + 1, 3, new VerseHashes(verse.Hash, "a"))));
    }
}
