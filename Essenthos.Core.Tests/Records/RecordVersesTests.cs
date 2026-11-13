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
            ["levites", "1.6:1", "Levites", "t", "f", "f", Credits.OurPeopleWords],
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
            ("ham", "19.78:51", "f", "t", Credits.OpenBible),
            ("mizraim", "1.10:6", "t", "f", Credits.OpenBible),
            ("mizraim", "1.50:11", "f", "f", Credits.OpenBible));
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

    [Fact]
    public void APsalmTitleATranslationPrintsInItsFirstVerseIsListedOnceAtTheTitle()
    {
        // BHSA numbers Psalm 3's title as its verse 1, which the frame stands at the title (3:0); the King James prints the
        // title inside its verse 1, which the frame stands at 3:1 and at the title.
        var store = Store(
            Name("19.3:1.3", "david", "name", "strong-number"),
            Name("19.3:1.7", "absalom", "name", "strong-number"),
            Name("19.3:2.1", "yhvh", "name", "strong-number"),
            Name("19.18:1.5", "yhvh", "name", "strong-number"),
            Name("19.18:2.3", "yhvh", "name", "strong-number"));
        Frame(store, "BHSA",
            new FrameRow(VerseKey.Parse("19.3:1"), null, [VerseKey.Parse("19.3:0")], "title"),
            new FrameRow(VerseKey.Parse("19.3:2"), 9, [VerseKey.Parse("19.3:1")], "moved"),
            new FrameRow(VerseKey.Parse("19.18:1"), null, [VerseKey.Parse("19.18:0")], "title"),
            new FrameRow(VerseKey.Parse("19.18:2"), 50, [VerseKey.Parse("19.18:1")], "moved"));
        Frame(store, "KJV",
            new FrameRow(VerseKey.Parse("19.3:1"), null, [VerseKey.Parse("19.3:1"), VerseKey.Parse("19.3:0")], "title"),
            new FrameRow(VerseKey.Parse("19.4:1"), null, [VerseKey.Parse("19.4:1"), VerseKey.Parse("19.4:0")], "title"),
            new FrameRow(VerseKey.Parse("19.18:1"), null, [VerseKey.Parse("19.18:1"), VerseKey.Parse("19.18:0")], "title"));
        WriteNames(store, "KJV",
            Name("19.3:1.4", "david", "name", "strong-number", text: "KJV"),
            Name("19.3:1.9", "absalom", "name", "strong-number", text: "KJV"),
            Name("19.3:1.12", "yhvh", "name", "strong-number", text: "KJV"),
            Name("19.4:1.4", "david", "name", "manual", text: "KJV"),
            Name("19.18:1.8", "yhvh", "name", "strong-number", text: "KJV"));
        RecordVerses.Write(store, force: true);

        Rows(store).Where(r => r[1]!.StartsWith("19.", StringComparison.Ordinal)).Select(r => (r[0], r[1])).Should().Equal(
            [("absalom", "19.3:0"), ("david", "19.3:0"), ("david", "19.4:1"), ("yhvh", "19.3:1"), ("yhvh", "19.18:0"), ("yhvh", "19.18:1")],
            "a name in the title stays at the title and one in the psalm's first line at its verse; a verse no single-address "
            + "verse names the record at stays at its primary address; a record the title and the first line both name keeps both");
    }

    [Fact]
    public void AVerseWhosePrimaryAddressIsTheTitleListsTheFirstLinesNameAtItsVerse()
    {
        // BHSA's Psalm 51 title is its verses 1 and 2, both standing at the title; the Ge'ez prints the title and the first
        // line in its verse 1, which the frame stands at the title first.
        var store = Store(
            Name("19.51:1.3", "david", "name", "strong-number"),
            Name("19.51:2.2", "nathan", "name", "strong-number"),
            Name("19.51:3.2", "yhvh", "name", "strong-number"));
        Frame(store, "BHSA",
            new FrameRow(VerseKey.Parse("19.51:1"), null, [VerseKey.Parse("19.51:0")], "title"),
            new FrameRow(VerseKey.Parse("19.51:2"), null, [VerseKey.Parse("19.51:0")], "title"),
            new FrameRow(VerseKey.Parse("19.51:3"), 21, [VerseKey.Parse("19.51:1")], "moved"));
        Frame(store, "GEEZ81",
            new FrameRow(VerseKey.Parse("19.51:1"), null, [VerseKey.Parse("19.51:0"), VerseKey.Parse("19.51:1")], "title"));
        WriteNames(store, "GEEZ81",
            Name("19.51:1.2", "david", "name", "rule-based", text: "GEEZ81"),
            Name("19.51:1.9", "yhvh", "name", "rule-based", text: "GEEZ81"));
        RecordVerses.Write(store, force: true);

        Rows(store).Where(r => r[1]!.StartsWith("19.", StringComparison.Ordinal)).Select(r => (r[0], r[1])).Should().Equal(
            [("david", "19.51:0"), ("nathan", "19.51:0"), ("yhvh", "19.51:1")],
            "the Ge'ez verse's God is the first line's, which BHSA names at verse 1, not the title's");
    }

    [Fact]
    public void ANameCarriedFromAPsalmTitleIsListedAtTheTitleWhereTheTextsFrameDoesNotKnowItsVerseHoldsIt()
    {
        // Young's Psalm 3:1 prints the title and the first line, and its frame stands it at 3:1 alone.
        var store = Store(Name("19.3:1.3", "david", "name", "strong-number"), Name("19.3:2.1", "yhvh", "name", "strong-number"));
        Frame(store, "BHSA",
            new FrameRow(VerseKey.Parse("19.3:1"), null, [VerseKey.Parse("19.3:0")], "title"),
            new FrameRow(VerseKey.Parse("19.3:2"), 9, [VerseKey.Parse("19.3:1")], "moved"));
        WriteNames(store, "YLT",
            Name("19.3:1.4", "david", "name", "strong-number", text: "YLT", through: "BHSA 19.3:1.3"),
            Name("19.3:1.12", "yhvh", "name", "strong-number", text: "YLT", through: "BHSA 19.3:2.1"),
            Name("19.3:2.3", "david", "name", "strong-number", text: "YLT", through: "BHSA 19.3:1.3"));
        RecordVerses.Write(store, force: true);

        Rows(store).Where(r => r[1]!.StartsWith("19.", StringComparison.Ordinal)).Select(r => (r[0], r[1])).Should().Equal(
            [("david", "19.3:0"), ("david", "19.3:2"), ("yhvh", "19.3:1")],
            "the name carried from the title stands at the title; the first line's stays at verse 1; a verse past the first "
            + "keeps its own address whatever it was carried from");
    }

    [Fact]
    public void ThingsOneRulingNamesTogetherAreEachListedWhereAWordNamesThemAll()
    {
        const string decision = "the owner's decision";
        var store = Store(
            Name("11.7:21.5", "jachin-pillar", "thing", "manual", source: decision),
            Name("11.7:21.5", "boaz-pillar", "thing", "manual", source: decision),
            Name("11.7:22.5", "jachin-pillar", "thing", "manual", source: decision),
            Name("11.7:22.5", "boaz-pillar", "thing", "manual", source: "another decision"),
            Name("11.7:23.1", "korah", "name", "manual", source: decision),
            Name("11.7:23.1", "dathan", "name", "manual", source: decision));
        WriteNames(store, "KJV",
            Name("11.7:15.4", "jachin-pillar", "thing", "manual", source: decision, text: "KJV", through: "BHSA 11.7:15.6"),
            Name("11.7:15.4", "boaz-pillar", "thing", "manual", source: decision, text: "KJV", through: "BHSA 11.7:15.6"),
            Name("11.7:16.3", "jachin-pillar", "thing", "manual", source: decision, text: "KJV", through: "BHSA 11.7:16.10"),
            Name("11.7:16.3", "boaz-pillar", "thing", "manual", source: decision, text: "KJV", through: "BHSA 11.7:17.14"));
        RecordVerses.Write(store, force: true);

        Rows(store).Select(r => (r[0], r[1])).Should().Equal(
            [("boaz-pillar", "11.7:15"), ("boaz-pillar", "11.7:21"), ("jachin-pillar", "11.7:15"), ("jachin-pillar", "11.7:21")],
            "both pillars are listed where one decision names them on one word, read there or carried through one word; two "
            + "decisions, a word carrying each through another word, and two men are nobody");
    }

    private ProductStore Store(params ProjectedName[] names)
    {
        var store = new ProductStore(_root);
        Write(store, "records/records",
            Record("anamim", "people", "Anamim"), Record("anointed", "title"), Record("azazel", "term"), Record("boaz", "person"), Record("boaz-pillar", "object"),
            Record("jachin-pillar", "object"), Record("dathan", "person"), Record("david", "person"), Record("ham", "person"), Record("izhar", "person"), Record("korah", "person"),
            Record("levi", "person"), Record("levites", "people", "Levites"), Record("mizraim", "person"), Record("moses", "person"),
            Record("absalom", "person"), Record("nathan", "person"), Record("yhvh", "person"));
        Write(store, "records/facts/titles", ["anointed", "david", "1.3:1", null, "test"]);
        Write(store, "records/facts/relationships");
        Write(store, "records/facts/verses");
        Frame(store, "BHSA", new FrameRow(VerseKey.Parse("1.5:2"), null, [VerseKey.Parse("1.5:9")], "renumbered"));

        WriteNames(store, names);
        RecordVerses.Write(store, force: true);
        return store;
    }

    private static void Frame(ProductStore store, string text, params FrameRow[] rows)
    {
        using var frame = store.Create(FrameFile.Product(text), ProductKind.Machine, FrameRow.Schema, "test", []);
        frame.WriteAll(rows);
        frame.Complete();
    }

    private static void WriteNames(ProductStore store, params ProjectedName[] names) => WriteNames(store, "BHSA", names);

    private static void WriteNames(ProductStore store, string text, params ProjectedName[] names)
    {
        using var writer = store.Create(NameProducts.Projected(text), ProductKind.Machine, ProjectedName.Schema(text), "test", []);
        writer.WriteAll(names);
        writer.Complete();
    }

    private static ProjectedName Name(
        string own, string record, string role, string method, double? confidence = null, string source = "test",
        string origin = NameOrigins.Settled, string text = "BHSA", string? through = null) =>
        new(WordKey.Parse(own, text), record, role, origin, through, method, confidence, source);

    private static TextWord Word(string key, string strong) =>
        new(WordKey.Parse(key, "BHSA"), "w", " ", null, strong, null, null, null, null, false, null, null, null);

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
