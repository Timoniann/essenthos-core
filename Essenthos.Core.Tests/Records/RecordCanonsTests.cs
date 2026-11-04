using Essenthos.Core.Loading.Frame;
using Essenthos.Core.Names;
using Essenthos.Core.Products;
using Essenthos.Core.Records;
using FluentAssertions;
using Xunit;

namespace Essenthos.Core.Tests.Records;

/// <summary>
/// Which canons attest a record (DOC-0224 §7.2, §8.1, the owner's D4), from the names on the held texts' words and where
/// each word's verse stands: a Hebrew chapter (an edition's own verse numbers and ranges in it, and Proverbs in two books,
/// included), the New Testament, the wider canons' books with a Hebrew book's lettered additions and chapters the Hebrew
/// lacks, and the books only the Ethiopian canon receives.
/// </summary>
public sealed class RecordCanonsTests : IDisposable
{
    private readonly string _resources = Path.Combine(Path.GetTempPath(), "essenthos-products", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_resources))
        {
            Directory.Delete(_resources, recursive: true);
        }
    }

    [Fact]
    public void EachRecordIsAttestedByTheCanonsWhoseVersesNameIt()
    {
        var store = new ProductStore(_resources);
        Records(store, "mordecai", "susanna", "tobias", "enoch", "peter", "jehoiachin", "solomon", "abner");
        Hebrew(store, "17.2:5", "1.5:18", "10.3:9");
        Frame(store, "VULGCLEM", new FrameRow(VerseKey.Parse("27.13:2"), null, [VerseKey.Parse("77.1:2")], "moved"));
        Names(store, "BHSA", ("17.2:5.3", "mordecai", NameOrigins.Settled));
        Names(store, "GRCBRENT", ("17.2:5.2", "mordecai", NameOrigins.Settled), ("17.1:1a.4", "mordecai", NameOrigins.Settled),
            ("17.11:2.1", "mordecai", NameOrigins.Settled), ("70.1:9.1", "tobias", NameOrigins.Settled));
        Names(store, "VULGCLEM", ("27.13:2.4", "susanna", NameOrigins.Settled));
        Names(store, "GEEZ81", ("85.1:9.1", "enoch", NameOrigins.Settled), ("1.5:18.4", "enoch", NameOrigins.Projected),
            ("91.1:1.2", "solomon", NameOrigins.Projected), ("17.2:39.1", "mordecai", NameOrigins.Projected));
        Names(store, "UST", ("10.3:9-10.5", "abner", NameOrigins.Projected));
        Names(store, "KJV", ("43.1:42.10", "peter", NameOrigins.Projected), ("43.1:42.12", "jonah-2", NameOrigins.Projected),
            ("40.1:11.4", "jehoiachin", NameOrigins.BaseReading));

        var outcome = RecordCanons.Write(store, force: false);

        using var reader = store.Open(RecordCanons.Product, RecordCanonRow.Schema);
        reader.ReadAll().Should().Equal(
            new RecordCanonRow("abner", RecordCanons.Hebrew, 1, 1, "UST 10.3:9-10.5"),
            new RecordCanonRow("enoch", RecordCanons.Hebrew, 1, 1, "GEEZ81 1.5:18.4"),
            new RecordCanonRow("enoch", RecordCanons.Ethiopic, 1, 1, "GEEZ81 85.1:9.1"),
            new RecordCanonRow("mordecai", RecordCanons.Hebrew, 3, 3, "BHSA 17.2:5.3"),
            new RecordCanonRow("mordecai", RecordCanons.Deuterocanon, 2, 1, "GRCBRENT 17.1:1a.4"),
            new RecordCanonRow("peter", RecordCanons.GreekNt, 1, 1, "KJV 43.1:42.10"),
            new RecordCanonRow("solomon", RecordCanons.Hebrew, 1, 1, "GEEZ81 91.1:1.2"),
            new RecordCanonRow("susanna", RecordCanons.Deuterocanon, 1, 1, "VULGCLEM 27.13:2.4"),
            new RecordCanonRow("tobias", RecordCanons.Deuterocanon, 1, 1, "GRCBRENT 70.1:9.1"));
        outcome.Should().Be(new RecordCanons.Outcome(true, 9, 7, 12, 0),
            "a record no file holds and a base's reading beside a differs attest nothing");
        store.Save();
        RecordCanons.Write(new ProductStore(_resources), force: false).Written.Should().BeFalse("nothing it reads moved");
    }

    private static void Records(ProductStore store, params string[] slugs)
    {
        var file = RecordFiles.All.Single(f => f.Product == "records/records");
        var columns = file.Schema().Columns.Count;
        using var writer = store.Create(file.Product, file.Kind, file.Schema(), "test", []);
        writer.WriteAll(slugs.Select(slug =>
        {
            var row = new string?[columns];
            row[0] = slug;
            row[1] = "person";
            row[4] = slug;
            return row;
        }));
    }

    private static void Hebrew(ProductStore store, params string[] verses)
    {
        using (var writer = store.Create(VerseManifestRow.ProductOf("BHSA"), ProductKind.Machine, VerseManifestRow.Schema, "test", []))
        {
            writer.WriteAll(verses.Select((verse, at) => new VerseManifestRow(VerseKey.Parse(verse), at + 1, 5, new VerseHashes("a", "b"))));
        }

        Frame(store, "BHSA");
    }

    private static void Frame(ProductStore store, string text, params FrameRow[] rows)
    {
        using var writer = store.Create(FrameFile.Product(text), ProductKind.Machine, FrameRow.Schema, "test", []);
        writer.WriteAll(rows);
    }

    private static void Names(ProductStore store, string text, params (string Own, string Record, string Origin)[] names)
    {
        using var writer = store.Create(NameProducts.Projected(text), ProductKind.Machine, ProjectedName.Schema(text), "test", []);
        writer.WriteAll(names.Select(n => new ProjectedName(WordKey.Parse(n.Own, text), n.Record, NameRoles.Name, n.Origin, null, "manual", null, "test")));
    }
}
