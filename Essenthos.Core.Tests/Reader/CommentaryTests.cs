using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Essenthos.Core.Build;
using Essenthos.Core.Commentary;
using Essenthos.Core.Corpus;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Endpoints;
using Essenthos.Core.Sword;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Essenthos.Core.Tests.Reader;

/// <summary>
/// A commentary module's markup read into blocks of marked text, keeping every word and dropping only presentation.
/// </summary>
public sealed class CommentMarkupTests
{
    private static string Osis(string markup)
    {
        var comment = new CommentBuilder();
        var unread = new Dictionary<string, int>();
        CommentMarkup.Osis(markup, comment, unread);
        unread.Should().BeEmpty();
        return comment.Json()!;
    }

    private static string Html(string markup)
    {
        var comment = new CommentBuilder();
        CommentMarkup.Html(markup, comment);
        return comment.Json()!;
    }

    [Fact]
    public void MilestoneParagraphsHeadingsAndMarksBecomeBlocksAndPieces()
    {
        var json = Osis(
            "<div eID=\"gen22\" type=\"x-p\"/> <title type=\"x-s3\">The Creation. (<hi type=\"small-caps\">b. c.</hi> 4004.)</title> " +
            "<div sID=\"gen23\" type=\"x-p\"/> <hi type=\"super\">1</hi> In the beginning God created the heaven and the earth. " +
            "<div eID=\"gen23\" type=\"x-p\"/> <div sID=\"gen24\" type=\"x-p\"/> I. In its epitome, " +
            "<reference osisRef=\"Gen.1.1-Gen.1.2\">ver. 1, 2</reference>, the <hi type=\"italic\">Maker</hi> of all. <div eID=\"gen24\" type=\"x-p\"/>");

        json.Should().Be(
            "[{\"h\":[\"The Creation. (\",{\"t\":\"b. c.\",\"sc\":1},\" 4004.)\"]}," +
            "{\"p\":[{\"t\":\"1\",\"sup\":1},\" In the beginning God created the heaven and the earth.\"]}," +
            "{\"p\":[\"I. In its epitome, \",{\"t\":\"ver. 1, 2\",\"r\":[1,1,1,1,2]},\", the \",{\"t\":\"Maker\",\"i\":1},\" of all.\"]}]");
    }

    [Fact]
    public void LinesOfVerseNotesAndEntitiesAreKept()
    {
        var json = Osis(
            "<lg sID=\"g1\"/> <l level=\"1\" sID=\"g2\"/>\"He that is drunken<l eID=\"g2\" level=\"1\"/> <lg eID=\"g1\"/> " +
            "the Hagiographa,<note placement=\"foot\">Hil. Megil. c. 2, &amp;#167; 11.</note> for which &amp; more");

        json.Should().Be(
            "[{\"l\":[\"\\\"He that is drunken\"]},{\"p\":[\"the Hagiographa,\",{\"t\":\"Hil. Megil. c. 2, § 11.\",\"n\":1},\" for which & more\"]}]");
    }

    [Fact]
    public void TheMarkersTheConversionLeftAreNoWordsOfTheComment()
    {
        var json = Osis(
            "<div sID=\"a\" type=\"x-p\"/> make us all so! &lt;&lt;&lt; <div eID=\"a\" type=\"x-p\"/> <chapter eID=\"Deut.34\"/> " +
            "<title type=\"x-s3\">Unabridged</title> <div eID=\"b\" type=\"book\"/>");

        json.Should().Be("[{\"p\":[\"make us all so!\"]}]");
    }

    [Theory]
    [InlineData("Eccl.12.13", new[] { 21, 12, 13 })]
    [InlineData("Gen.5", new[] { 1, 5 })]
    [InlineData("John.1.3 John.1.10", new[] { 43, 1, 3 })]
    [InlineData("Jer.52.1-Lam.1.5", new[] { 24, 52, 1 })]
    [InlineData("Sir.6.14", new[] { 72, 6, 14 })]
    [InlineData("Lu.2.1", new[] { 42, 2, 1 })]
    public void AReferenceIsTheVerseItNames(string osisRef, int[] verse) =>
        CommentMarkup.Verse(osisRef).Should().Equal(verse);

    [Theory]
    [InlineData("Xyz.1.1")]
    [InlineData("Gen.one.1")]
    [InlineData("")]
    public void AReferenceThatDoesNotReadCitesNothing(string osisRef) =>
        CommentMarkup.Verse(osisRef).Should().BeNull();

    [Fact]
    public void OlderModulesHtmlKeepsHeadingsParagraphsAndNumberedItems()
    {
        var json = Html(
            "<h4><b><font color=#0000FF>Text: Psalm 1,1-6</font></b></h4> \n<P>Der 1 Psalm beschreibt den Weg.</P>\n" +
            "<ol>\n<li>\nDer Weg der Gerechten,</li>\n<li>\nder Weg der <i>Gottlosen</i>,</li>\n</ol>\n<p>Was hat also ein Gerechter f&uuml;r einen Sinn?<br>Nach dem 1 Vers");

        json.Should().Be(
            "[{\"h\":[{\"t\":\"Text: Psalm 1,1-6\",\"b\":1}]},{\"p\":[\"Der 1 Psalm beschreibt den Weg.\"]}," +
            "{\"p\":[\"1. Der Weg der Gerechten,\"]},{\"p\":[\"2. der Weg der \",{\"t\":\"Gottlosen\",\"i\":1},\",\"]}," +
            "{\"p\":[\"Was hat also ein Gerechter für einen Sinn?\"]},{\"p\":[\"Nach dem 1 Vers\"]}]");
    }

    [Fact]
    public void Windows1252BytesAreTheirCharacters() =>
        SwordModule.Windows1252([0x96, 0x20, 0xFC, 0x91, 0x41], 0, 5).Should().Be("– ü‘A");
}

/// <summary>
/// A module's slots read into comments over their spans, on a module made here: a comment over several verses is one
/// row over the run, a chapter's heading is its introduction, and a book's and its first chapter's introductions
/// standing before the first verse's comment are taken off it.
/// </summary>
public sealed class CommentaryModuleTests : IDisposable
{
    private readonly string _resources = Path.Combine(Path.GetTempPath(), "essenthos-commentary", Guid.NewGuid().ToString("n"));

    public void Dispose()
    {
        if (Directory.Exists(_resources))
        {
            Directory.Delete(_resources, recursive: true);
        }
    }

    [Fact]
    public void RunsHeadingsAndIntroductionsBecomeRowsOverTheirSpans()
    {
        const string first =
            "<div type=\"x-milestone\" subType=\"x-preverse\" sID=\"pv1\"/><title type=\"x-ms\">Genesis</title> " +
            "<div sID=\"p1\" type=\"x-p\"/> The argument of the book. <div eID=\"p1\" type=\"x-p\"/> <div sID=\"i1\" type=\"introduction\"/> " +
            "<div sID=\"p2\" type=\"x-p\"/> We have three things in this chapter. <div type=\"x-milestone\" subType=\"x-preverse\" eID=\"pv1\"/> " +
            "<div eID=\"p2\" type=\"x-p\"/> <div eID=\"i1\" type=\"introduction\"/> <div sID=\"p3\" type=\"x-p\"/> " +
            "<hi type=\"super\">1</hi> In the beginning. <hi type=\"super\">2</hi> And the earth. In these verses. <div eID=\"p3\" type=\"x-p\"/>";
        WriteModule(_resources, "TEST", new Dictionary<(int, int, int), string>
        {
            [(1, 0, 0)] = "<div type=\"book\" sID=\"b\"/> <milestone type=\"x-usfm-toc1\" n=\"Genesis\"/>",
            [(1, 1, 1)] = first,
            [(1, 1, 2)] = first,
            [(1, 1, 3)] = "<div sID=\"p4\" type=\"x-p\"/> Light. <div eID=\"p4\" type=\"x-p\"/>",
            [(1, 2, 0)] = "<div sID=\"p5\" type=\"x-p\"/> This chapter is an appendix. <div eID=\"p5\" type=\"x-p\"/>",
            [(1, 2, 1)] = "<div sID=\"p6\" type=\"x-p\"/> The sabbath. <div eID=\"p6\" type=\"x-p\"/>",
        }, linked: [((1, 1, 1), (1, 1, 2)), ((1, 2, 1), (1, 2, 2)), ((1, 2, 1), (1, 2, 3))]);

        var rows = CommentaryReader.Read(_resources, new CommentaryWorks.Work("test", "henry", "en", "TEST", "Test commentary"));

        rows.Select(r => (r.Book, r.FirstChapter, r.FirstVerse, r.LastChapter, r.LastVerse, Words(r.Body))).Should().Equal(
            (1, null, null, null, null, "Genesis The argument of the book."),
            (1, 1, null, 1, null, "We have three things in this chapter."),
            (1, 1, 1, 1, 2, "1 In the beginning. 2 And the earth. In these verses."),
            (1, 1, 3, 1, 3, "Light."),
            (1, 2, null, 2, null, "This chapter is an appendix."),
            (1, 2, 1, 2, 3, "The sabbath."));
        rows.Select(r => r.Sequence).Should().Equal(1, 2, 3, 4, 5, 6);
    }

    private static string Words(string body) =>
        string.Join(' ', JsonDocument.Parse(body).RootElement.EnumerateArray()
            .SelectMany(block => block.EnumerateObject().First().Value.EnumerateArray())
            .Select(piece => piece.ValueKind == JsonValueKind.String ? piece.GetString() : piece.GetProperty("t").GetString())
            .Select(text => text!.Trim()));

    /// <summary>A zCom4 module in the King James numbering: each address's entry, and verses pointing at another's.</summary>
    internal static void WriteModule(
        string resources,
        string module,
        Dictionary<(int Book, int Chapter, int Verse), string> entries,
        ((int, int, int) To, (int, int, int) From)[] linked)
    {
        var folder = Path.Combine(resources, CommentaryReader.Folder, module);
        var data = Path.Combine(folder, "modules", "comments", "zcom4", "test");
        Directory.CreateDirectory(Path.Combine(folder, "mods.d"));
        Directory.CreateDirectory(data);
        File.WriteAllText(Path.Combine(folder, "mods.d", "test.conf"),
            "[TEST]\nDataPath=./modules/comments/zcom4/test/\nModDrv=zCom4\nSourceType=OSIS\nEncoding=UTF-8\nCompressType=ZIP\nVersification=KJV\n");

        var (oldTestament, newTestament) = SwordModule.Layout("KJV");
        foreach (var (testament, books) in new[] { ("ot", oldTestament), ("nt", newTestament) })
        {
            var slots = new List<(int, int, int)> { (0, 0, 0), (0, 0, 0) };
            foreach (var (book, counts) in books)
            {
                slots.Add((book, 0, 0));
                for (var chapter = 1; chapter <= counts.Length; chapter++)
                {
                    slots.Add((book, chapter, 0));
                    slots.AddRange(Enumerable.Range(1, counts[chapter - 1]).Select(verse => (book, chapter, verse)));
                }
            }

            var text = new MemoryStream();
            var at = new Dictionary<(int, int, int), (int Offset, int Size)>();
            foreach (var (address, markup) in entries.Where(e => slots.Contains(e.Key)))
            {
                var bytes = Encoding.UTF8.GetBytes(markup);
                at[address] = ((int)text.Length, bytes.Length);
                text.Write(bytes);
            }

            foreach (var (to, from) in linked.Where(l => at.ContainsKey(l.To)))
            {
                at[from] = at[to];
            }

            var raw = text.ToArray();
            var compressed = new MemoryStream();
            using (var zlib = new ZLibStream(compressed, CompressionLevel.Optimal, leaveOpen: true))
            {
                zlib.Write(raw);
            }

            using (var blocks = new BinaryWriter(File.Create(Path.Combine(data, $"{testament}.bzs"))))
            {
                blocks.Write(0u);
                blocks.Write((uint)compressed.Length);
                blocks.Write((uint)raw.Length);
            }

            File.WriteAllBytes(Path.Combine(data, $"{testament}.bzz"), compressed.ToArray());
            using var index = new BinaryWriter(File.Create(Path.Combine(data, $"{testament}.bzv")));
            foreach (var slot in slots)
            {
                var (offset, size) = at.GetValueOrDefault(slot);
                index.Write(0u);
                index.Write((uint)offset);
                index.Write((uint)size);
            }
        }
    }
}

/// <summary>
/// The two commentaries as CrossWire publishes them, read whole: Matthew Henry comments on every verse of the King
/// James numbering exactly once, and Rieger's reflections stand at the passages his own headings name.
/// </summary>
[Trait(TestCategory.Name, TestCategory.Corpus)]
public sealed partial class CommentaryCorpusTests
{
    private static string Resources => Path.GetDirectoryName(TestResources.Path("MANIFEST.json"))!;

    [Fact]
    public void HenryCommentsOnEveryVerseOnceAndIntroducesEveryBook()
    {
        var unread = new Dictionary<string, int>();
        var rows = CommentaryReader.Read(Resources, CommentaryWorks.Find("henry")!, unread);

        unread.Should().BeEmpty();
        rows.Count(r => r.FirstChapter is null).Should().Be(66);
        rows.Where(r => r.FirstChapter is null).Select(r => r.Book).Should().OnlyHaveUniqueItems();
        var covered = new Dictionary<(int, int, int), int>();
        foreach (var row in rows.Where(r => r.FirstVerse is not null))
        {
            row.FirstChapter.Should().Be(row.LastChapter, "Henry writes no comment across a chapter");
            for (var verse = row.FirstVerse!.Value; verse <= row.LastVerse; verse++)
            {
                covered[(row.Book, row.FirstChapter!.Value, verse)] = covered.GetValueOrDefault((row.Book, row.FirstChapter!.Value, verse)) + 1;
            }
        }

        var verses = SwordModule.Chapters("KJV").Sum(book => book.Sum());
        covered.Should().HaveCount(verses).And.OnlyContain(c => c.Value == 1);

        foreach (var correction in CommentaryReader.Corrections.Where(c => c.Commentary == "henry"))
        {
            var (book, chapter, verse, _, last) = correction.To;
            var row = rows.Single(r => r.Book == book && r.FirstChapter == chapter && r.FirstVerse == verse);
            row.LastVerse.Should().Be(last, correction.Why);
            var printed = Printed(row.Body);
            (printed.Min(), printed.Max()).Should().Be((verse, last), correction.Why);
        }

        var genesis = rows.Single(r => r is { Book: 1, FirstChapter: 1, FirstVerse: 1 });
        (genesis.LastVerse, genesis.Body).Should().Match<(int?, string)>(g => g.Item1 == 2 && g.Item2.Contains("In these verses we have the work of creation"));
        rows.Single(r => r is { Book: 1, FirstChapter: 1, FirstVerse: null }).Body.Should().Contain("We have three things in this chapter");
        rows.Single(r => r is { Book: 1, FirstChapter: null }).Body.Should().Contain("PREFACE");
    }

    /// <summary>
    /// Every reflection is headed with its passage, "Text: Psalm 1,1-6", and stands there; the ones the module put a verse
    /// late (the end of Hosea and Jonah 1-2, numbered as the Hebrew numbers them) stand where the correction puts them.
    /// </summary>
    [Fact]
    public void RiegersReflectionsStandAtThePassagesTheirHeadingsName()
    {
        var unread = new Dictionary<string, int>();
        var rows = CommentaryReader.Read(Resources, CommentaryWorks.Find("rieger")!, unread);

        unread.Should().BeEmpty();
        rows.Should().HaveCount(731).And.OnlyContain(r => r.FirstVerse != null);
        var corrected = CommentaryReader.Corrections.Where(c => c.Commentary == "rieger").Select(c => c.To).ToHashSet();
        foreach (var row in rows)
        {
            var span = new CommentaryReader.Span(row.Book, row.FirstChapter!.Value, row.FirstVerse!.Value, row.LastChapter!.Value, row.LastVerse!.Value);
            if (corrected.Contains(span))
            {
                continue;
            }

            var heading = Heading().Match(row.Body);
            heading.Success.Should().BeTrue(row.Body[..Math.Min(80, row.Body.Length)]);
            var c1 = int.Parse(heading.Groups[1].Value);
            var v1 = int.Parse(heading.Groups[2].Value);
            var c2 = heading.Groups[4].Success ? int.Parse(heading.Groups[3].Value) : c1;
            var v2 = heading.Groups[4].Success ? int.Parse(heading.Groups[4].Value) : heading.Groups[3].Success ? int.Parse(heading.Groups[3].Value) : v1;
            (span.FirstChapter, span.FirstVerse, span.LastChapter, span.LastVerse).Should().Be((c1, v1, c2, v2), heading.Value);
        }
    }

    /// <summary>The verse numbers a comment prints raised, before the verses it expounds.</summary>
    private static List<int> Printed(string body) =>
    [
        .. JsonDocument.Parse(body).RootElement.EnumerateArray()
            .SelectMany(block => block.EnumerateObject().First().Value.EnumerateArray())
            .Where(piece => piece.ValueKind == JsonValueKind.Object && piece.TryGetProperty("sup", out _))
            .Select(piece => int.TryParse(piece.GetProperty("t").GetString(), out var number) ? number : 0)
            .Where(number => number > 0),
    ];

    [GeneratedRegex(@"Text: [^""]*? (\d+),(\d+)(?:-(\d+)(?:,(\d+))?)?""")]
    private static partial Regex Heading();
}

/// <summary>The comments beside a verse: the one whose passage holds it, wherever the passage starts; without a verse, the introductions.</summary>
[Collection(WitnessDatabaseCollection.Name)]
public sealed class CommentaryEndpointTests : IDisposable
{
    private readonly AppDbContext _db;

    public CommentaryEndpointTests(WitnessDatabase database)
    {
        _db = database.NewContext();
        _db.CommentaryEntries.ExecuteDelete();
        _db.CommentaryEntries.AddRange(
            Entry(1, null, null, null, null, "book"),
            Entry(2, 1, null, 1, null, "chapter one"),
            Entry(3, 1, 1, 1, 2, "verses one and two"),
            Entry(4, 1, 3, 1, 5, "verses three to five"),
            Entry(5, 2, null, 2, null, "chapter two"),
            Entry(6, 1, 31, 2, 3, "across the chapter"));
        _db.SaveChanges();
    }

    public void Dispose()
    {
        _db.CommentaryEntries.ExecuteDelete();
        _db.Dispose();
    }

    [Theory]
    [InlineData(1, 1, "verses one and two")]
    [InlineData(1, 2, "verses one and two")]
    [InlineData(1, 4, "verses three to five")]
    [InlineData(1, 31, "across the chapter")]
    [InlineData(2, 2, "across the chapter")]
    public async Task AVerseIsGivenTheCommentWhosePassageHoldsIt(int chapter, int verse, string expected)
    {
        var passage = await CommentaryEndpoints.Passage(_db, "henry", 1, chapter, verse, verse, default);

        passage.Entries.Should().ContainSingle().Which.Body.GetRawText().Should().Contain(expected);
        passage.Entries[0].Kind.Should().Be("verses");
    }

    [Fact]
    public async Task VersesPickedTogetherAreGivenEveryCommentTouchingThem()
    {
        var passage = await CommentaryEndpoints.Passage(_db, "henry", 1, 1, 2, 4, default);

        passage.Entries.Select(e => e.First!.Verse).Should().Equal(1, 3);
        passage.Through.Should().Be(4);
    }

    [Fact]
    public async Task APassageSaysWhereItStartsAndEnds()
    {
        var entry = (await CommentaryEndpoints.Passage(_db, "henry", 1, 2, 1, 1, default)).Entries.Single();

        (entry.First!.Chapter, entry.First.Verse, entry.Last!.Chapter, entry.Last.Verse).Should().Be((1, 31, 2, 3));
        entry.First.Slug.Should().Be("genesis");
    }

    [Fact]
    public async Task WithoutAVerseTheChapterAndTheBookAreIntroduced()
    {
        var passage = await CommentaryEndpoints.Passage(_db, "henry", 1, 2, null, null, default);

        passage.Entries.Select(e => (e.Kind, e.Body.GetRawText())).Should().Equal(
            ("book", "[{\"p\":[\"book\"]}]"),
            ("chapter", "[{\"p\":[\"chapter two\"]}]"));
        (await CommentaryEndpoints.Passage(_db, "rieger", 1, 1, 1, 1, default)).Entries.Should().BeEmpty();
    }

    private static CommentaryEntry Entry(int sequence, int? firstChapter, int? firstVerse, int? lastChapter, int? lastVerse, string text) =>
        new()
        {
            Commentary = "henry",
            Source = CommentaryWorks.HenrySource,
            Book = 1,
            FirstChapter = firstChapter,
            FirstVerse = firstVerse,
            LastChapter = lastChapter,
            LastVerse = lastVerse,
            Sequence = sequence,
            Body = $"[{{\"p\":[\"{text}\"]}}]",
        };
}

/// <summary>The comments go from the product the tables producer writes into <c>commentary_entry</c> as they were read.</summary>
[Collection(WitnessDatabaseCollection.Name)]
public sealed class CommentaryTableTests(WitnessDatabase database) : IDisposable
{
    private readonly string _resources = Path.Combine(Path.GetTempPath(), "essenthos-commentary", Guid.NewGuid().ToString("n"));

    public void Dispose()
    {
        if (Directory.Exists(_resources))
        {
            Directory.Delete(_resources, recursive: true);
        }
    }

    [Fact]
    public async Task TheTableHoldsEveryCommentOfTheProduct()
    {
        foreach (var work in CommentaryWorks.All.Where(w => w.Kind == CommentaryWorks.Format.Sword))
        {
            CommentaryModuleTests.WriteModule(_resources, work.Module, new Dictionary<(int, int, int), string>
            {
                [(43, 0, 0)] = $"<div sID=\"a\" type=\"x-p\"/> {work.Id} on John. <div eID=\"a\" type=\"x-p\"/>",
                [(43, 3, 16)] = $"<div sID=\"b\" type=\"x-p\"/> {work.Id} on <hi type=\"italic\">so loved</hi>. <div eID=\"b\" type=\"x-p\"/>",
            }, linked: [((43, 3, 16), (43, 3, 17))]);
        }

        var lopukhin = Path.Combine(_resources, CommentaryReader.Folder, "Lopukhin");
        Directory.CreateDirectory(lopukhin);
        using (var zip = ZipFile.Open(Path.Combine(lopukhin, "Lopuhin_Old_Tastement_29-Joe.zip"), ZipArchiveMode.Create))
        using (var entry = new StreamWriter(zip.CreateEntry("Joel.fb2").Open(), new UTF8Encoding(false)))
        {
            entry.Write(LopukhinReaderTests.Joel);
        }

        var store = new Essenthos.Core.Products.ProductStore(_resources);
        LopukhinReaderTests.Synodal(store);
        TableProducts.Write(store, new HashSet<string> { "commentary_entry" }, force: false).Single().Rows.Should().Be(8);
        await using var db = database.NewContext();
        await db.CommentaryEntries.ExecuteDeleteAsync();

        var loaded = await CarriedTables.Load(database.NewConnection().ConnectionString, store, new HashSet<string> { "commentary_entry" }, default);

        loaded.Single().Loaded.Should().Be(8);
        var rows = await db.CommentaryEntries.OrderBy(e => e.Id).ToListAsync();
        rows.Select(e => (e.Commentary, e.Book, e.FirstChapter, e.FirstVerse, e.LastChapter, e.LastVerse, e.Sequence)).Should().Equal(
            ("henry", 43, null, null, null, null, 1),
            ("henry", 43, 3, 16, 3, 17, 2),
            ("rieger", 43, null, null, null, null, 1),
            ("rieger", 43, 3, 16, 3, 17, 2),
            ("lopukhin", 29, null, null, null, null, 1),
            ("lopukhin", 29, 1, null, 1, null, 2),
            ("lopukhin", 29, 1, 2, 1, 3, 3),
            ("lopukhin", 29, 1, 4, 1, 4, 4));
        rows[1].Body.Should().Be("[{\"p\":[\"henry on \",{\"t\":\"so loved\",\"i\":1},\".\"]}]");
        rows.Select(e => e.Source).Should().Equal(
            CommentaryWorks.HenrySource, CommentaryWorks.HenrySource, CommentaryWorks.RiegerSource, CommentaryWorks.RiegerSource,
            CommentaryWorks.LopukhinSource, CommentaryWorks.LopukhinSource, CommentaryWorks.LopukhinSource, CommentaryWorks.LopukhinSource);
        await db.CommentaryEntries.ExecuteDeleteAsync();
    }
}

/// <summary>
/// Lopukhin's Explanatory Bible as its FictionBook books print it, on a book made here: the introduction, a chapter's outline,
/// the printed verses (no part of a comment) placed through the corpus's Synodal, comments by the verses they open with, a
/// note standing where it is pointed at, a cited verse, and the author's signature closing the introduction.
/// </summary>
public sealed class LopukhinReaderTests : IDisposable
{
    private readonly string _resources = Path.Combine(Path.GetTempPath(), "essenthos-lopukhin", Guid.NewGuid().ToString("n"));

    public void Dispose()
    {
        if (Directory.Exists(_resources))
        {
            Directory.Delete(_resources, recursive: true);
        }
    }

    internal const string Joel = """
        <?xml version="1.0" encoding="utf-8"?>
        <FictionBook xmlns="http://www.gribuser.ru/xml/fictionbook/2.0" xmlns:xlink="http://www.w3.org/1999/xlink">
        <description><title-info><book-title>Толковая Библия. Книга пророка Иоиля.</book-title></title-info></description>
        <body name="Книга пророка Иоиля">
        <title><p>Толковая Библия</p><p>Книга пророка Иоиля</p></title>
        <section><title><p>О КНИГЕ ПРОРОКА ИОИЛЯ</p></title>
        <p>Пророк Иоиль был сын Вафуила [<a xlink:href="#n29-Joel_1" type="note">1</a>].</p></section>
        <section><title><p>КНИГА ПРОРОКА ИОИЛЯ</p></title>
        <section><title><p>Глава I</p></title>
        <poem><stanza><v><emphasis>1–3. Призыв к старцам.</emphasis></v></stanza></poem>
        <poem><stanza>
        <v id="n29-Joel_I_1"><cite><strong>1. Слово Господне, которое было к Иоилю, сыну Вафуила.</strong></cite></v>
        <v id="n29-Joel_I_2"><cite><strong>2. Слушайте это, старцы, и внимайте, все жители земли сей!</strong></cite></v>
        <v id="n29-Joel_I_3"><cite><strong>3. Передайте об этом детям вашим.</strong></cite></v>
        </stanza></poem>
        <p>2–3. Евр. <emphasis>sekenim</emphasis> (ср. <a xlink:href="#n29-Joel_II_1" type="note">II:1</a>).</p>
        <p>Дальнейшими словами пророк указывает на тяжесть бедствия.</p>
        <empty-line/>
        </section>
        <section><title><p>Глава II</p></title>
        <poem><stanza>
        <v id="n29-Joel_II_1"><cite><strong>1. И будет после того, излию от Духа Моего на всякую плоть.</strong></cite></v>
        </stanza></poem>
        <p>Это пророчество исполнилось в день Пятидесятницы.</p>
        <empty-line/>
        <p><emphasis>Н. П. Розанов</emphasis></p>
        </section></section>
        <section><title><p>Примечания</p></title>
        <p id="n29-Joel_1">1. Евр. pethuel.</p></section>
        </body>
        </FictionBook>
        """;

    /// <summary>The corpus's Synodal for the book: the verse the book prints as II:1 is its 1:4.</summary>
    internal static void Synodal(Essenthos.Core.Products.ProductStore store) =>
        Essenthos.Core.Tests.Links.ProductCorpus.Text(store, LopukhinReader.Synodal, "ru",
        [
            ("29.1:1", ["Слово", "Господне", "которое", "было", "к", "Иоилю", "сыну", "Вафуила"]),
            ("29.1:2", ["Слушайте", "это", "старцы", "и", "внимайте", "все", "жители", "земли", "сей"]),
            ("29.1:3", ["Передайте", "об", "этом", "детям", "вашим"]),
            ("29.1:4", ["И", "будет", "после", "того", "излию", "от", "Духа", "Моего", "на", "всякую", "плоть"]),
        ]);

    [Fact]
    public void CommentsStandOverTheVersesTheyOpenWithWhereTheSynodalPlacesThem()
    {
        var store = new Essenthos.Core.Products.ProductStore(_resources);
        Synodal(store);
        var tally = new LopukhinReader.Tally();
        var unread = new Dictionary<string, int>();
        var rows = LopukhinReader.Read([("Lopuhin_Old_Tastement_29-Joe", XDocument.Parse(Joel))],
            Essenthos.Core.Mapping.ProductText.Read(store, LopukhinReader.Synodal), CommentaryWorks.Find("lopukhin")!, unread, tally);

        unread.Should().BeEmpty();
        rows.Select(r => (r.Book, r.FirstChapter, r.FirstVerse, r.LastChapter, r.LastVerse)).Should().Equal(
            (29, (int?)null, (int?)null, (int?)null, (int?)null),
            (29, 1, null, 1, null),
            (29, 1, 2, 1, 3),
            (29, 1, 4, 1, 4));
        rows[0].Body.Should().Be(
            "[{\"h\":[\"О КНИГЕ ПРОРОКА ИОИЛЯ\"]},{\"p\":[\"Пророк Иоиль был сын Вафуила \",{\"t\":\"Евр. pethuel.\",\"n\":1},\".\"]}," +
            "{\"p\":[{\"t\":\"Н. П. Розанов\",\"i\":1}]}]");
        rows[1].Body.Should().Be("[{\"l\":[{\"t\":\"1–3. Призыв к старцам.\",\"i\":1}]}]");
        rows[2].Body.Should().Be(
            "[{\"p\":[\"2–3. Евр. \",{\"t\":\"sekenim\",\"i\":1},\" (ср. \",{\"t\":\"II:1\",\"r\":[29,1,4]},\").\"]}," +
            "{\"p\":[\"Дальнейшими словами пророк указывает на тяжесть бедствия.\"]}]");
        rows[3].Body.Should().Be("[{\"p\":[\"Это пророчество исполнилось в день Пятидесятницы.\"]}]");
        tally.Signatures.Should().Equal("29: Н. П. Розанов");
        tally.Unplaced.Should().BeEmpty();
        (tally.Read - tally.Printed - tally.Titles - tally.Markers - tally.WithheldWords - tally.DroppedWords - (tally.Kept - tally.NotesWritten))
            .Should().Be(0, "every word of the book is written, printed above a comment, a title or a note's marker");
        tally.Titles.Should().Be(Fb2Book.Count("Толковая Библия Книга пророка Иоиля КНИГА ПРОРОКА ИОИЛЯ Глава I Глава II"));
    }

    [Theory]
    [InlineData("2–3. Евр.", 2, 3)]
    [InlineData("15 и 16. Речь", 15, 16)]
    [InlineData("45, 46. В 45 ст.", 45, 46)]
    [InlineData("6–14, 17–19, 26–27. Принося", 6, 27)]
    [InlineData("38—39. (См. Мк I:29–31)", 38, 39)]
    public void ACommentOpensWithTheVersesItIsOn(string text, int first, int last) =>
        Fb2Book.Opening(text).Should().Be((first, last));

    [Theory]
    [InlineData("4 стих знакомит с причиной")]
    [InlineData("11, 12 и 13 псалмы написаны")]
    [InlineData("Евр. sekenim")]
    public void TextThatOnlyStartsWithANumberOpensNoComment(string text) => Fb2Book.Opening(text).Should().BeNull();

    [Theory]
    [InlineData("n29-Joel_I_1", 29, 1, 1)]
    [InlineData("n19-Ps_50_3", 19, 50, 3)]
    [InlineData("n25-Phm_24", 57, 1, 24)]
    [InlineData("n01-Mf_ХI_5", 40, 11, 5)]
    [InlineData("n42-Jud_XVI_25", 71, 16, 25)]
    [InlineData("n12-Jud_I_25", 65, 1, 25)]
    public void AVerseIdNamesItsBookChapterAndVerse(string id, int book, int chapter, int verse) =>
        Fb2Book.VerseOf(id).Should().Be((book, chapter, verse));
}

/// <summary>
/// Lopukhin's Explanatory Bible read from the archives the fetch pins, its printed verses placed through the corpus's Synodal.
/// </summary>
[Trait(TestCategory.Name, TestCategory.Corpus)]
public sealed class LopukhinCorpusTests
{
    private static string Resources => Path.GetDirectoryName(TestResources.Path("MANIFEST.json"))!;

    private static readonly Lazy<(List<CommentaryRow> Rows, LopukhinReader.Tally Tally, Dictionary<string, int> Unread)> Reading = new(() =>
    {
        var unread = new Dictionary<string, int>();
        var tally = new LopukhinReader.Tally();
        var rows = LopukhinReader.Read(new Essenthos.Core.Products.ProductStore(Resources), CommentaryWorks.Find("lopukhin")!, unread, tally);
        return (rows, tally, unread);
    });

    /// <summary>
    /// Every word of the books is written into a comment, or is the Synodal's verse printed above one, a title, a note's marker,
    /// or a book held back; the verses found in no verse of the corpus's Synodal are the ones it does not print.
    /// </summary>
    [Fact]
    public void EveryWordIsWrittenOrAccountedFor()
    {
        var (rows, tally, unread) = Reading.Value;

        unread.Should().BeEmpty();
        (tally.Read - tally.Printed - tally.Titles - tally.Markers - tally.WithheldWords - tally.DroppedWords - (tally.Kept - tally.NotesWritten))
            .Should().Be(0);
        tally.Unplaced.Where(u => !u.StartsWith("69.", StringComparison.Ordinal)).Select(u => u.Split(' ')[0]).Should().BeEquivalentTo(
        [
            // Joshua 24:34-36 and Proverbs' further lines are the Greek's, in brackets in the Synodal and absent from the corpus's;
            // the corpus's Synodal has no Psalm 151; Esther 8:12 is the Greek's addition at the verse.
            "6.24:34", "6.24:35", "6.24:36", "17.8:12", "20.4:28", "20.4:29", "20.13:14", "20.18:8",
            "19.151:1", "19.151:2", "19.151:3", "19.151:4", "19.151:5", "19.151:6", "19.151:7",
        ]);
        tally.Dropped.Should().BeLessThanOrEqualTo(1, "only Psalm 151's comment stands over verses the corpus does not hold");
        tally.Signatures.Should().HaveCount(18).And.Contain("40: Священник М. Фивейский").And.Contain("80: Епископ Иосиф")
            .And.Contain("27: Магистр богословия / священник А. В. Петровский");
        rows.Should().NotContain(r => LopukhinReader.Withheld.ContainsKey(r.Book));
    }

    [Theory]
    [InlineData(19, 50, 3, 19, 51, 1)]
    [InlineData(19, 133, 1, 19, 134, 1)]
    [InlineData(45, 14, 24, 45, 16, 25)]
    [InlineData(28, 14, 1, 28, 13, 16)]
    [InlineData(6, 5, 16, 6, 6, 1)]
    [InlineData(18, 39, 31, 18, 40, 1)]
    [InlineData(21, 4, 17, 21, 5, 1)]
    [InlineData(9, 24, 1, 9, 23, 29)]
    [InlineData(4, 13, 1, 4, 12, 16)]
    [InlineData(22, 7, 1, 22, 6, 13)]
    [InlineData(27, 3, 24, 93, 1, 1)]
    [InlineData(27, 13, 1, 77, 1, 1)]
    [InlineData(57, 1, 24, 57, 1, 25)]
    [InlineData(64, 1, 15, 64, 1, 14)]
    public void AVerseNumberedTheSynodalsWayStandsWhereTheCorpusSynodalStands(int book, int chapter, int verse, int toBook, int toChapter, int toVerse)
        => Placement.Value.Of(book, chapter, verse)![0].Should().Be(new Essenthos.Core.Products.VerseKey(toBook, toChapter, toVerse));

    private static readonly Lazy<SynodalPlacement> Placement = new(() => SynodalPlacement.Place(
        LopukhinReader.Archives(Resources, CommentaryWorks.Find("lopukhin")!)
            .SelectMany(a => Fb2Book.Read(LopukhinReader.Load(a), Fb2Book.ArchiveBook(Path.GetFileNameWithoutExtension(a)), null))
            .Where(b => b.Book is not null)
            .SelectMany(b => b.Printed.Select(p => (b.Book!.Value, p))),
        Essenthos.Core.Mapping.ProductText.Read(new Essenthos.Core.Products.ProductStore(Resources), LopukhinReader.Synodal),
        new LopukhinReader.Tally()));

    /// <summary>Every book of the sixty-six it is read for has its introduction, and its comments reach most verses.</summary>
    [Fact]
    public void EveryBookIsIntroducedAndMostVersesHaveAComment()
    {
        var rows = Reading.Value.Rows;
        var held = LopukhinReader.Withheld.Keys.ToHashSet();
        // 2 Samuel, 2 Kings, Nehemiah, 2 Timothy and Titus have none of their own: the book before introduces them.
        int[] introducedBefore = [10, 12, 16, 55, 56];
        rows.Where(r => r.FirstChapter is null && r.Book <= 66).Select(r => r.Book).Should()
            .BeEquivalentTo(Enumerable.Range(1, 66).Where(b => !held.Contains(b) && !introducedBefore.Contains(b)));
        var covered = rows.Where(r => r is { Book: <= 66, FirstVerse: not null } && r.FirstChapter == r.LastChapter)
            .SelectMany(r => Enumerable.Range(r.FirstVerse!.Value, r.LastVerse!.Value - r.FirstVerse.Value + 1).Select(v => (r.Book, r.FirstChapter, v)))
            .Distinct().Count();
        covered.Should().BeGreaterThan(27_000, "of the 31,102 verses of the King James numbering, Jonah's and 2 Thessalonians' held back");
        rows.Where(r => r.FirstVerse is not null).Should().OnlyContain(r =>
            r.FirstChapter < r.LastChapter || (r.FirstChapter == r.LastChapter && r.FirstVerse <= r.LastVerse));
    }
}
