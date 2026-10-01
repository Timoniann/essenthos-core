using System.IO.Compression;
using System.Text;
using Essenthos.Core.Loading;
using Essenthos.Core.Loading.Links;
using Essenthos.Core.Sword;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Essenthos.Core.Tests;

/// <summary>
/// The Strong-numbered CrossWire modules beyond the Chinese: that a module in SWORD's German or
/// Segond numbering is read at the addresses that numbering gives, that a rendering the edition
/// prints in pieces is one rendering, and that only the Faith Hope Love foundation's prefix numbers
/// are translated.
/// </summary>
public sealed class CrossWireStrongTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"sword-{Guid.NewGuid():n}");

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    /// <summary>
    /// The German numbering is the Hebrew's in the Old Testament: Joel has four chapters, Malachi
    /// three, and the last verse of Malachi is 3:24. Read in the King James's numbering, the same
    /// index would be refused, because it has a different number of slots.
    /// </summary>
    [Fact]
    public void AGermanModuleIsReadAtTheHebrewAddresses()
    {
        Module("German", new()
        {
            [(29, 4, 1)] = "Denn siehe, in jenen Tagen",
            [(39, 3, 24)] = "der soll das Herz der Väter",
            [(47, 13, 13)] = "Die Gnade des Herrn",
        });

        var verses = SwordModule.Verses(_folder);

        verses.Should().HaveCount(3);
        verses[(29, 4, 1)].Should().Be("Denn siehe, in jenen Tagen");
        verses[(39, 3, 24)].Should().Be("der soll das Herz der Väter");
        verses[(47, 13, 13)].Should().Be("Die Gnade des Herrn");
    }

    [Fact]
    public void ASegondModuleIsReadAtTheSegondAddresses()
    {
        Module("Segond", new()
        {
            [(29, 3, 1)] = "Car voici, en ces jours",
            [(39, 4, 6)] = "Il ramènera le cœur des pères",
        });

        var verses = SwordModule.Verses(_folder);

        verses[(29, 3, 1)].Should().Be("Car voici, en ces jours");
        verses[(39, 4, 6)].Should().Be("Il ramènera le cœur des pères");
    }

    [Fact]
    public void AVersificationNobodyTabulatedIsRefused()
    {
        Module("Luther", new() { [(1, 1, 1)] = "Am Anfang" });

        var read = () => SwordModule.Verses(_folder);

        read.Should().Throw<NotSupportedException>().WithMessage("*Luther versification*");
    }

    /// <summary>
    /// <em>afin</em> and <em>que</em> are two elements carrying one split mark and one number: one
    /// rendering of ἵνα, so they share a unit, and a word between them does not.
    /// </summary>
    [Fact]
    public void ARenderingPrintedInPiecesIsOneUnit()
    {
        var unit = 0;
        var words = SwordTextSource.Segment(
            OsisVerse.Parse(
                """<w lemma="strong:G2443" type="x-split-1890">afin</w> <w lemma="strong:G2443" type="x-split-1890">que</w> <w lemma="strong:G3956">quiconque</w> croit"""),
            SwordSegmentation.Spaced,
            ref unit,
            out _,
            fhlPrefixes: false);

        words.Select(w => w.Surface).Should().Equal("afin", "que", "quiconque", "croit");
        words[0].Unit.Should().Be(words[1].Unit);
        words[2].Unit.Should().NotBe(words[0].Unit);
        words[3].Numbers.Should().BeEmpty();
        string.Concat(words.Select(w => w.Surface + w.Trailer)).Should().Be("afin que quiconque croit");
    }

    /// <summary>
    /// Outside FHL's modules a number above the Hebrew dictionary is a parsing code or nothing at
    /// all, and a number without its testament letter names no dictionary: none of them is a lexeme.
    /// </summary>
    [Theory]
    [InlineData("strong:H09001 strong:H03978", new[] { "H3978" })]
    [InlineData("strong:H8804 strong:H0853 strong:H01254", new[] { "H853", "H1254" })]
    [InlineData("strong:G5656 strong:G25", new[] { "G25" })]
    [InlineData("strong:505", new string[0])]
    [InlineData("strong:G3588 lemma.TR:ο", new[] { "G3588" })]
    public void OnlyFhlsPrefixNumbersAreTranslated(string lemma, string[] expected) =>
        UnionStrongNumbers.Read(lemma, fhlPrefixes: false).Should().Equal(expected);

    [Fact]
    public void ANumberingIsNamedByItsModuleOrItsText()
    {
        CrossWireStrongLinkLoader.Named([]).Should().HaveCount(CrossWireStrongLinkLoader.All.Count);
        CrossWireStrongLinkLoader.Named(["gersch", "LSG1910", "--apply"]).Select(n => n.Module)
            .Should().Equal("GerSch", "FreSegond1910");

        var unknown = () => CrossWireStrongLinkLoader.Named(["RusVZh"]);
        unknown.Should().Throw<ArgumentException>().WithMessage("*RusVZh*");
    }

    /// <summary>
    /// Every numbering is credited by a prefix no other dataset claims, because the credit is the
    /// only place a link records whose numbers it was drawn from.
    /// </summary>
    [Fact]
    public void EveryNumberingHasItsOwnCreditAndADeclaration()
    {
        var credits = CrossWireStrongLinkLoader.All.Select(n => n.Credit).ToList();

        credits.Should().OnlyHaveUniqueItems().And.NotContain(UnionStrongLinkLoader.Credit);
        foreach (var credit in credits)
        {
            Essenthos.Core.Corpus.Datasets.All.Should().ContainSingle(dataset => dataset.Prefix == credit, credit);
        }
    }

    /// <summary>
    /// Writes a zText module holding these verses in the named numbering, one block per testament,
    /// the way SWORD's own writer lays one out: every slot of the numbering has an index entry, and
    /// a verse the module does not hold is an entry of size zero.
    /// </summary>
    private void Module(string versification, Dictionary<(int Book, int Chapter, int Verse), string> verses)
    {
        var data = Path.Combine(_folder, "modules", "texts", "ztext", "test");
        Directory.CreateDirectory(data);
        Directory.CreateDirectory(Path.Combine(_folder, "mods.d"));
        File.WriteAllText(
            Path.Combine(_folder, "mods.d", "test.conf"),
            $"[Test]\nDataPath=./modules/texts/ztext/test/\nModDrv=zText\nCompressType=ZIP\nVersification={versification}\n");

        var chapters = Chapters(versification);
        Testament(data, "ot", 1, 39, chapters, verses);
        Testament(data, "nt", 40, 66, chapters, verses);
    }

    private static void Testament(
        string data, string name, int first, int last, IReadOnlyList<int[]> chapters,
        Dictionary<(int, int, int), string> verses)
    {
        var text = new MemoryStream();
        var index = new BinaryWriter(File.Create(Path.Combine(data, $"{name}.bzv")));
        void Entry(int offset, int size)
        {
            index.Write(0u);
            index.Write((uint)offset);
            index.Write((ushort)size);
        }

        Entry(0, 0);
        Entry(0, 0);
        for (var book = first; book <= last; book++)
        {
            Entry(0, 0);
            for (var chapter = 1; chapter <= chapters[book - 1].Length; chapter++)
            {
                Entry(0, 0);
                for (var verse = 1; verse <= chapters[book - 1][chapter - 1]; verse++)
                {
                    var bytes = verses.TryGetValue((book, chapter, verse), out var written)
                        ? Encoding.UTF8.GetBytes(written)
                        : [];
                    Entry((int)text.Length, bytes.Length);
                    text.Write(bytes);
                }
            }
        }

        index.Dispose();

        var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.Optimal, leaveOpen: true))
        {
            zlib.Write(text.ToArray());
        }

        File.WriteAllBytes(Path.Combine(data, $"{name}.bzz"), compressed.ToArray());
        using var blocks = new BinaryWriter(File.Create(Path.Combine(data, $"{name}.bzs")));
        blocks.Write(0u);
        blocks.Write((uint)compressed.Length);
        blocks.Write((uint)text.Length);
    }

    private static IReadOnlyList<int[]> Chapters(string versification) =>
        versification is "German" or "Segond" ? SwordModule.Chapters(versification) : SwordModule.Chapters("KJV");
}

/// <summary>
/// The modules themselves, on disk: each reads as a complete Bible, every verse reads back as the
/// module prints it, and the Segond's numbers go onto the words of the Segond loaded from eBible.
/// </summary>
[Trait(TestCategory.Name, TestCategory.Corpus)]
public sealed class CrossWireStrongCorpusTests(ITestOutputHelper output)
{
    private static string Module(string name) => TestResources.Folder(SwordTextSource.Texts[name].Folder);

    [Theory]
    [InlineData("FreJND", "JND2024", "Au commencement Dieu créa les cieux et la terre.")]
    [InlineData("GerSch", "SCH1951", "Im Anfang schuf Gott den Himmel und die Erde.")]
    [InlineData("RLT", "RLT2018", "In the beginning God created the heaven and the earth.")]
    public void TheModuleIsACompleteBible(string module, string slug, string genesis)
    {
        var text = SwordTextSource.Read(Module(module));

        text.Definition.Slug.Should().Be(slug);
        text.Books.Should().HaveCount(66);
        string.Concat(text.Books[0].Chapters[0].Verses[0].Words.Select(w => w.Surface + w.Trailer))
            .Should().Be(genesis);
        text.Books.Sum(book => book.Chapters.Sum(chapter => chapter.Verses.Count)).Should().BeGreaterThan(31_000);
    }

    /// <summary>
    /// The German numbering is the Hebrew's, so Joel 3:1 is the outpouring of the Spirit that the King
    /// James numbers 2:28, and a psalm's title is its first verse.
    /// </summary>
    [Theory]
    [InlineData("FreJND", "répandrai mon Esprit")]
    [InlineData("GerSch", "meinen Geist ausgieße")]
    public void TheOldTestamentIsNumberedAsTheHebrewIs(string module, string spirit)
    {
        var text = SwordTextSource.Read(Module(module));
        string Verse(int book, int chapter, int verse) => string.Concat(
            text.Books[book - 1].Chapters[chapter - 1].Verses.Single(v => v.Number == verse).Words
                .Select(w => w.Surface + w.Trailer));

        text.Books[28].Chapters.Should().HaveCount(4);
        text.Books[38].Chapters.Should().HaveCount(3);
        Verse(29, 3, 1).Should().Contain(spirit);
        Verse(19, 3, 1).Should().Contain("David");
    }

    [Theory]
    [InlineData("FreJND")]
    [InlineData("GerSch")]
    [InlineData("RLT")]
    public void TheNumbersAreOnTheWordsTheyStandOn(string module)
    {
        var numbers = SwordTextSource.Numbers(Module(module));
        var words = numbers.Values.SelectMany(verse => verse).ToList();

        output.WriteLine(
            $"{module}: {numbers.Count} verses, {words.Count} words, {words.Count(word => word.Numbers.Count > 0)} carrying a number");
        words.Count(word => word.Numbers.Count > 0).Should().BeGreaterThan(words.Count / 3);
        words.SelectMany(word => word.Numbers).Where(number => !InDictionary(number)).Should().BeEmpty();
    }

    private static bool InDictionary(string number) =>
        int.Parse(number[1..]) <= (number[0] == 'H' ? 8674 : 5624);

    /// <summary>
    /// <c>FreSegond1910</c> is Richard Lemay's digitisation of the 1910 Segond and the corpus loads
    /// eBible's. The numbers can only be laid onto words the two write the same, so this measures how
    /// much of the loaded Segond they reach, without a database: the same layer the run uses, over the
    /// words eBible's files hold.
    /// </summary>
    [Fact]
    public void TheSegondsNumbersGoOntoTheSegondLoadedFromEbible()
    {
        var loaded = EbibleTextSource.Read(TestResources.Folder("Segond1910"));
        long id = 0;
        var corpus = loaded.Books
            .SelectMany(book => book.Chapters.SelectMany(chapter => chapter.Verses.Select(verse => new CorpusVerse(
                book.CanonicalOrdinal,
                chapter.Number,
                verse.Number,
                [],
                [.. verse.Words.Select(word => new CorpusWord(++id, word.Surface))]))))
            .ToList();

        var laid = SynodalStrongLayer.Lay(
            SwordTextSource.Numbers(TestResources.Folder(SwordTextSource.Numberings["FreSegond1910"].Folder)),
            corpus);

        output.WriteLine($"{corpus.Count} verses, {corpus.Sum(v => v.Words.Count)} words: {laid}");
        laid.Refused.Should().BeLessThan(corpus.Count / 50, laid.ToString());
        laid.TaggedWords.Should().BeGreaterThan(300_000, laid.ToString());
    }
}
