using Essenthos.Core.Conversion;
using Essenthos.Core.Corpus;
using Essenthos.Core.Loading.Encyclopedia;
using Essenthos.Core.Loading.Frame;
using Essenthos.Core.Names;
using Essenthos.Core.Products;
using Essenthos.Core.Records;
using FluentAssertions;
using Xunit;

namespace Essenthos.Core.Tests.Records;

/// <summary>
/// The verses a record's words and relationships put it in, made from the names projection, the frames and the
/// relationships: what the old load's passes left under their two sources, made again whenever a name moves.
/// </summary>
public sealed class RecordVersesTests : IDisposable
{
    private const string Own = "Essenthos, from the words this corpus annotates to the person or the place they name";

    private readonly string _root = Path.Combine(Path.GetTempPath(), "essenthos-products", Guid.NewGuid().ToString("N"));

    [Fact]
    public void AVerseIsListedForEveryRecordAWordNamesAsTheReaderIsToldIt()
    {
        var store = Store(
            Name("1.1:1.1", "moses", "name", "manual"),
            Name("1.1:1.2", "moses", "name", "strong-number", 0.99),
            Name("1.2:1.1", "korah", "name", "strong-number", 0.9),
            Name("1.2:1.1", "dathan", "name", "strong-number", 0.9),
            Name("1.3:1.1", "anointed", "title", "manual"),
            Name("1.3:1.1", "david", "name", "manual"),
            Name("1.4:1.1", "boaz", "name", "manual", origin: NameOrigins.BaseReading),
            Name("1.5:2.1", "moses", "name", "manual"),
            Name("1.6:1.1", "levites", "people", "manual"));

        Rows(store).Should().BeEquivalentTo<string?[]>(
        [
            ["anointed", "1.3:1", null, "t", "f", "f", Own],
            ["david", "1.3:1", null, "t", "f", "f", Own],
            ["levites", "1.6:1", "Levites", "t", "f", "f", PeopleLoader.FromOurOwnWords],
            ["moses", "1.1:1", null, "t", "f", "f", Own],
            ["moses", "1.5:9", null, "t", "f", "f", Own],
        ], options => options.WithStrictOrdering(),
            "two words naming Moses in one verse are one verse; two men named equally surely on one word are nobody; a title "
            + "and its bearer are both named; a base reading names nobody; the frame places 1.5:2 at 1.5:9");
    }

    [Fact]
    public void AManNamedAfterTheSonsOfBesideHisPeopleIsWithheldOnlyWhereEveryWordSoReadsHim()
    {
        var store = Store(
            Name("2.32:26.18", "levites", "people", "rule-based", 0.9, Annotations.SonsOf),
            Name("2.32:26.19", "levi", "name", "rule-based", 0.9, Annotations.SonsOf),
            Name("4.16:8.8", "levites", "people", "rule-based", 0.9, Annotations.SonsOf),
            Name("4.16:8.9", "levi", "name", "rule-based", 0.9, Annotations.SonsOf),
            Name("4.16:8.12", "levi", "name", "manual"));

        Rows(store).Where(r => r[0] == "levi").Select(r => (r[1], r[5])).Should().Equal(("2.32:26", "t"), ("4.16:8", "f"));
    }

    [Fact]
    public void ARecordNoOtherVerseListsIsListedAtTheVersesItsRelationshipsWereReadFrom()
    {
        var store = Store(Name("1.10:6.1", "mizraim", "name", "manual"), Name("1.10:13.1", "mizraim", "name", "manual"));
        Write(store, "records/facts/relationships",
            Relationship("anamim", "descendant-of", "mizraim", "1.10:13"),
            Relationship("korah", "son-of", "izhar", "2.6:21"),
            Relationship("korah", "son-of", "izhar", null));
        Write(store, "records/facts/verses", ["izhar", "2.6:18", "Izhar", "t", "f", "f", "Essenthos, a reading by hand"]);
        RecordVerses.Write(store, force: true);

        Rows(store).Where(r => r[6] == RelationshipVerseLoader.Source).Select(r => (r[0], r[1], r[3])).Should().Equal(
            ("anamim", "1.10:13", "f"), ("korah", "2.6:21", "f"));
    }

    [Fact]
    public void TheGazetteersVersesAreServedUnderItsCreditAndListTheRecord()
    {
        var store = Store(Name("1.10:6.1", "mizraim", "name", "manual"));
        Write(store, "records/facts/relationships", Relationship("mizraim", "son-of", "ham", "1.10:6"));
        Write(store, "external/openbible/references",
            ["a1", "mizraim", "1.10:6", null, "f", "f", "f"],
            ["a1", "mizraim", "1.50:11", null, "t", "f", "f"],
            [null, "ham", "19.78:51", null, "f", "t", "f"]);
        RecordVerses.Write(store, force: true);

        Rows(store).Where(r => r[6] != Own).Select(r => (r[0], r[1], r[3], r[4], r[6])).Should().Equal(
            ("ham", "19.78:51", "f", "t", OpenBiblePlaceLoader.Source),
            ("mizraim", "1.10:6", "t", "f", OpenBiblePlaceLoader.Source),
            ("mizraim", "1.50:11", "f", "f", OpenBiblePlaceLoader.Source));
    }

    [Fact]
    public void AGazetteerVerseOfATermNamesItWhereAWordCarriesItsStrongNumber()
    {
        var store = Store();
        Write(store, "records/names", ["azazel", "1", "Azazel", "term", null, null, null, null, "H5799", null, null, null]);
        using (var words = store.Create(TextWord.ProductOf("BHSA"), ProductKind.Machine, TextWord.Schema("BHSA"), "test", []))
        {
            words.WriteAll([Word("3.16:8.12", "H5799"), Word("3.16:9.1", "H8064")]);
            words.Complete();
        }

        Write(store, "external/openbible/references",
            ["a2", "azazel", "3.16:8", null, "t", "f", "f"], ["a2", "azazel", "3.16:9", null, "t", "f", "f"]);
        RecordVerses.Write(store, force: true);

        Rows(store).Where(r => r[0] == "azazel").Select(r => (r[1], r[3])).Should().Equal(("3.16:8", "t"), ("3.16:9", "f"));
    }

    [Fact]
    public void ANameThatMovesMovesTheVerseAndAnUnmovedStoreIsNotWrittenAgain()
    {
        var store = Store(Name("11.7:21.33", "boaz", "name", "lexical", 0.99));
        RecordVerses.Write(store, force: false).Written.Should().BeFalse("nothing it reads has moved");

        WriteNames(store, Name("11.7:21.33", "boaz-pillar", "thing", "manual"));
        RecordVerses.Write(store, force: false).Written.Should().BeTrue();

        Rows(store).Select(r => (r[0], r[1])).Should().Equal(("boaz-pillar", "11.7:21"));
    }

    private ProductStore Store(params ProjectedName[] names)
    {
        var store = new ProductStore(_root);
        Write(store, "records/records",
            Record("anamim", "people", "Anamim"), Record("anointed", "title"), Record("azazel", "term"), Record("boaz", "person"), Record("boaz-pillar", "object"),
            Record("dathan", "person"), Record("david", "person"), Record("ham", "person"), Record("izhar", "person"), Record("korah", "person"),
            Record("levi", "person"), Record("levites", "people", "Levites"), Record("mizraim", "person"), Record("moses", "person"));
        Write(store, "records/facts/titles", ["anointed", "david", "1.3:1", null, "test"]);
        Write(store, "records/facts/relationships");
        Write(store, "records/facts/verses");
        using (var frame = store.Create(FrameFile.Product("BHSA"), ProductKind.Machine, FrameRow.Schema, "test", []))
        {
            frame.Write(new FrameRow(VerseKey.Parse("1.5:2"), null, [VerseKey.Parse("1.5:9")], "renumbered"));
            frame.Complete();
        }

        WriteNames(store, names);
        RecordVerses.Write(store, force: true);
        return store;
    }

    private static void WriteNames(ProductStore store, params ProjectedName[] names)
    {
        using var writer = store.Create(NameProducts.Projected("BHSA"), ProductKind.Machine, ProjectedName.Schema("BHSA"), "test", []);
        writer.WriteAll(names);
        writer.Complete();
    }

    private static ProjectedName Name(
        string own, string record, string role, string method, double? confidence = null, string source = "test",
        string origin = NameOrigins.Settled) =>
        new(WordKey.Parse(own, "BHSA"), record, role, origin, null, method, confidence, source);

    private static TextWord Word(string key, string strong) =>
        new(WordKey.Parse(key, "BHSA"), null, "w", " ", null, strong, null, null, null, null, false, null, null, null);

    private static string?[] Record(string slug, string kind, string? name = null) =>
        [slug, kind, null, null, name ?? slug, null, null, null, null, null, null, null];

    private static string?[] Relationship(string from, string relation, string to, string? verse) =>
        [from, relation, to, verse, "read", null, "manual", null, "read from Scripture by test", null];

    private static List<string?[]> Rows(ProductStore store)
    {
        using var reader = store.Open(RecordVerses.Product, RecordFiles.All.Single(f => f.Product == RecordVerses.Product).Schema());
        return [.. reader.ReadAll()];
    }

    private static void Write(ProductStore store, string product, params string?[][] rows)
    {
        var file = RecordFiles.All.Single(f => f.Product == product);
        using var writer = store.Create(product, file.Kind, file.Schema(), ProductHeader.DescribeProducer(RecordFiles.Producer), []);
        writer.WriteAll(rows);
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
