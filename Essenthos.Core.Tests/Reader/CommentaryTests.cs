using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
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
        var passage = await CommentaryEndpoints.Passage(_db, "henry", 1, chapter, verse, default);

        passage.Entries.Should().ContainSingle().Which.Body.GetRawText().Should().Contain(expected);
        passage.Entries[0].Kind.Should().Be("verses");
    }

    [Fact]
    public async Task APassageSaysWhereItStartsAndEnds()
    {
        var entry = (await CommentaryEndpoints.Passage(_db, "henry", 1, 2, 1, default)).Entries.Single();

        (entry.First!.Chapter, entry.First.Verse, entry.Last!.Chapter, entry.Last.Verse).Should().Be((1, 31, 2, 3));
        entry.First.Slug.Should().Be("genesis");
    }

    [Fact]
    public async Task WithoutAVerseTheChapterAndTheBookAreIntroduced()
    {
        var passage = await CommentaryEndpoints.Passage(_db, "henry", 1, 2, null, default);

        passage.Entries.Select(e => (e.Kind, e.Body.GetRawText())).Should().Equal(
            ("book", "[{\"p\":[\"book\"]}]"),
            ("chapter", "[{\"p\":[\"chapter two\"]}]"));
        (await CommentaryEndpoints.Passage(_db, "rieger", 1, 1, 1, default)).Entries.Should().BeEmpty();
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
        foreach (var work in CommentaryWorks.All)
        {
            CommentaryModuleTests.WriteModule(_resources, work.Module, new Dictionary<(int, int, int), string>
            {
                [(43, 0, 0)] = $"<div sID=\"a\" type=\"x-p\"/> {work.Id} on John. <div eID=\"a\" type=\"x-p\"/>",
                [(43, 3, 16)] = $"<div sID=\"b\" type=\"x-p\"/> {work.Id} on <hi type=\"italic\">so loved</hi>. <div eID=\"b\" type=\"x-p\"/>",
            }, linked: [((43, 3, 16), (43, 3, 17))]);
        }

        var store = new Essenthos.Core.Products.ProductStore(_resources);
        TableProducts.Write(store, new HashSet<string> { "commentary_entry" }, force: false).Single().Rows.Should().Be(4);
        await using var db = database.NewContext();
        await db.CommentaryEntries.ExecuteDeleteAsync();

        var loaded = await CarriedTables.Load(database.NewConnection().ConnectionString, store, new HashSet<string> { "commentary_entry" }, default);

        loaded.Single().Loaded.Should().Be(4);
        var rows = await db.CommentaryEntries.OrderBy(e => e.Id).ToListAsync();
        rows.Select(e => (e.Commentary, e.Book, e.FirstChapter, e.FirstVerse, e.LastChapter, e.LastVerse, e.Sequence)).Should().Equal(
            ("henry", 43, null, null, null, null, 1),
            ("henry", 43, 3, 16, 3, 17, 2),
            ("rieger", 43, null, null, null, null, 1),
            ("rieger", 43, 3, 16, 3, 17, 2));
        rows[1].Body.Should().Be("[{\"p\":[\"henry on \",{\"t\":\"so loved\",\"i\":1},\".\"]}]");
        rows.Select(e => e.Source).Should().Equal(
            CommentaryWorks.HenrySource, CommentaryWorks.HenrySource, CommentaryWorks.RiegerSource, CommentaryWorks.RiegerSource);
        await db.CommentaryEntries.ExecuteDeleteAsync();
    }
}
