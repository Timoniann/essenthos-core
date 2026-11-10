using Essenthos.Core.Corpus;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Endpoints;
using Essenthos.Core.Loading.CrossReferences;
using Essenthos.Core.Products;
using Essenthos.Core.Records;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Essenthos.Core.Tests.Reader;

/// <summary>
/// OpenBible's pairs and the Treasury's entries read as verses of the shared frame: ranges, ranges
/// into the next book, the Treasury's carried book and chapter, its catchwords and its remarks.
/// </summary>
[Trait(TestCategory.Name, TestCategory.Corpus)]
public sealed class CrossReferenceReadingTests
{
    private const int Genesis = 1;
    private const int Proverbs = 20;
    private const int Isaiah = 23;
    private const int Lamentations = 25;
    private const int Jeremiah = 24;
    private const int Mark = 41;
    private const int John = 43;
    private const int Romans = 45;

    private static readonly VerseAddress GenesisOne = new(Genesis, 1, 1);

    [Fact]
    public void APairIsAVerseToAVerseOrToARunOfThem()
    {
        OpenBibleCrossReferences.Parse("Gen.1.1\tRom.1.19-Rom.1.20\t59")
            .Should().Be(new CrossReferenceRow(GenesisOne, new(Romans, 1, 19), new(Romans, 1, 20), 0, 59));
        OpenBibleCrossReferences.Parse("Gen.1.1\tPs.90.2\t62")!.End.Should().BeNull();
        OpenBibleCrossReferences.Parse("Jer.52.1\tJer.52.1-Lam.1.5\t3")!.End
            .Should().Be(new VerseAddress(Lamentations, 1, 5));
        OpenBibleCrossReferences.Parse("Gen.1.1\tRev.3.14\t-2")!.Votes.Should().Be(-2);
    }

    [Theory]
    [InlineData("Gen.1.1\tRom.1.20-Rom.1.19\t5")]
    [InlineData("Gen.1.1\tWis.1.1\t5")]
    [InlineData("Gen.1.1\tRom.1.19\tmany")]
    public void ALineThatDoesNotReadIsRefused(string line) =>
        OpenBibleCrossReferences.Parse(line).Should().BeNull();

    /// <summary>
    /// The file as it is shipped: nothing may be dropped, and each verse's references come most
    /// voted first.
    /// </summary>
    [Fact]
    public void TheShippedSetReadsWholeAndRanksByVotes()
    {
        var (rows, unread) = OpenBibleCrossReferences.Read(
            TestResources.Path(OpenBibleCrossReferences.Folder, OpenBibleCrossReferences.FileName));

        rows.Should().HaveCountGreaterThan(340_000);
        unread.Should().Be(0);
        var first = rows.Where(r => r.From == GenesisOne).OrderBy(r => r.Rank).ToList();
        first.Select(r => r.Votes).Should().BeInDescendingOrder();
        first.Select(r => r.Rank).Should().Equal(Enumerable.Range(1, first.Count));
    }

    [Fact]
    public void ATreasuryReferenceCarriesItsBookAndChapterForward()
    {
        var (targets, unread) = TreasuryReferences.Targets(
            new VerseAddress(Genesis, 1, 4), "10,12; Pr 8:22-24; 16:4; Mr 13:19; Joh 1:1-3; 6:5-15; 31-59,63");

        unread.Should().Be(0);
        targets.Should().Equal(
            (new VerseAddress(Genesis, 1, 10), null),
            (new VerseAddress(Genesis, 1, 12), null),
            (new VerseAddress(Proverbs, 8, 22), new VerseAddress(Proverbs, 8, 24)),
            (new VerseAddress(Proverbs, 16, 4), null),
            (new VerseAddress(Mark, 13, 19), null),
            (new VerseAddress(John, 1, 1), new VerseAddress(John, 1, 3)),
            (new VerseAddress(John, 6, 5), new VerseAddress(John, 6, 15)),
            (new VerseAddress(John, 6, 31), new VerseAddress(John, 6, 59)),
            (new VerseAddress(John, 6, 63), null));
    }

    /// <summary>
    /// What the shipped sets would send a reader to that no text has: OpenBible nothing once its ESV-only verse is the
    /// frame's, the Treasury only Deuteronomy 23:15's "Phm 10:19", which the loader leaves out.
    /// </summary>
    [Fact]
    public void TheShippedSetsNameOnlyVersesTheEnglishNumberingHasButOne()
    {
        var (openBible, _) = OpenBibleCrossReferences.Read(
            TestResources.Path(OpenBibleCrossReferences.Folder, OpenBibleCrossReferences.FileName));
        var (treasury, _) = TreasuryReferences.Read(TestResources.Folder(TreasuryReferences.Folder));

        openBible.Where(r => !EnglishVerses.Has(r)).Should().BeEmpty();
        treasury.Where(r => !EnglishVerses.Has(r)).Select(r => (r.From, r.To)).Should().Equal(
            (new VerseAddress(5, 23, 15), new VerseAddress(57, 10, 19)));
        treasury.Should().Contain(r => r.From == new VerseAddress(44, 7, 5) && r.To == new VerseAddress(5, 34, 4))
            .And.Contain(r => r.From == new VerseAddress(32, 1, 3) && r.To == new VerseAddress(44, 26, 19))
            .And.Contain(r => r.From == new VerseAddress(22, 5, 6) && r.To == new VerseAddress(Isaiah, 58, 7));
    }

    /// <summary>The Treasury's remarks are not references, and a comma before a book is its slip for a semicolon.</summary>
    [Fact]
    public void ARemarkIsSkippedAndACommaBeforeABookSeparates()
    {
        var (targets, unread) = TreasuryReferences.Targets(GenesisOne, "Ex 11:8; *marg:; 39:2, Jer 25:22");

        unread.Should().Be(0);
        targets.Select(t => t.To).Should().Equal(
            new VerseAddress(2, 11, 8), new VerseAddress(2, 39, 2), new VerseAddress(Jeremiah, 25, 22));
    }

    /// <summary>A reference the entry has already given is given once, under its first catchword.</summary>
    [Fact]
    public void AnEntryFilesItsReferencesUnderTheirCatchwordsAndSkipsTheChapterSummary()
    {
        const string entry =
            "<br /><scripRef passage=\"Ge 1:1\">1</scripRef> God creates heaven and earth;<br />\n<br />" +
            "beginning.<br /><scripRef>Pr 8:22-24; Joh 1:1-3</scripRef><br />God.<br />" +
            "<scripRef>Isa 40:28; Pr 8:22-24</scripRef><br /><scripRef>Joh 1:1</scripRef>\n";

        var (rows, unread) = TreasuryReferences.Entry(GenesisOne, entry);

        unread.Should().Be(0);
        rows.Select(r => (r.To, r.Note)).Should().Equal(
            (new VerseAddress(Proverbs, 8, 22), "beginning"),
            (new VerseAddress(John, 1, 1), "beginning"),
            (new VerseAddress(Isaiah, 40, 28), "God"),
            (new VerseAddress(John, 1, 1), "God"));
    }

    /// <summary>The module as it is shipped, read. What does not read is counted, and must stay a sliver.</summary>
    [Fact]
    public void TheShippedTreasuryReadsAlmostWhole()
    {
        var (rows, unread) = TreasuryReferences.Read(TestResources.Folder(TreasuryReferences.Folder));

        rows.Should().HaveCountGreaterThan(370_000);
        ((double)unread / rows.Count).Should().BeLessThan(0.001, $"{unread} pieces of {rows.Count} did not read");
        rows.Where(r => r.From == GenesisOne).Should().Contain(r =>
            r.To == new VerseAddress(John, 1, 1) && r.End == new VerseAddress(John, 1, 3) && r.Note == "beginning");
    }
}

/// <summary>
/// Parallel passages found by shared runs of dictionary forms: a long shared run across two books is
/// one passage with its verses paired word by word, and neither a formula every chapter repeats nor
/// a repetition within one chapter is a parallel.
/// </summary>
[Trait(TestCategory.Name, TestCategory.Corpus)]
public sealed class ParallelDetectorTests
{
    private static readonly ParallelSettings Settings = new(4, 4, 12);

    /// <summary>A text of made-up lemmas: each verse of a chapter its own words, unless it is given some.</summary>
    private sealed class Text
    {
        private readonly List<LemmaToken> _tokens = [];
        private int _unique;

        public Text Verse(int book, int chapter, int verse, params string[] lemmas)
        {
            _tokens.AddRange(lemmas.Select((lemma, index) => new LemmaToken(new(book, chapter, verse), index + 1, lemma)));
            return this;
        }

        public Text Filler(int book, int chapter, int from, int to)
        {
            for (var verse = from; verse <= to; verse++)
            {
                Verse(book, chapter, verse, [.. Enumerable.Range(0, 8).Select(_ => $"w{_unique++}")]);
            }

            return this;
        }

        public IReadOnlyList<LemmaToken> Tokens => _tokens;
    }

    private static readonly string[] Story =
        ["and", "king", "went", "to", "city", "of", "david", "and", "sat", "on", "throne", "of", "his", "father"];

    private static readonly string[] Sequel =
        ["and", "people", "gave", "him", "gold", "and", "silver", "and", "cedar", "from", "tyre", "for", "house"];

    [Fact]
    public void ALongSharedRunIsOnePassagePairedVerseByVerse()
    {
        var text = new Text()
            .Filler(10, 5, 1, 3).Verse(10, 5, 4, Story).Verse(10, 5, 5, Sequel).Filler(10, 5, 6, 8)
            .Filler(13, 14, 1, 2).Verse(13, 14, 3, [.. Story[..7], "then", .. Story[7..]])
            .Verse(13, 14, 4, Sequel).Filler(13, 14, 5, 6);

        var passages = ParallelDetector.Detect(text.Tokens, Settings);

        passages.Should().ContainSingle();
        passages[0].Verses.Select(v => (v.A, v.B)).Should().Equal(
            (new VerseAddress(10, 5, 4), new VerseAddress(13, 14, 3)),
            (new VerseAddress(10, 5, 5), new VerseAddress(13, 14, 4)));
        passages[0].Verses[0].Words.Should().HaveCount(Story.Length);
        passages[0].Verses[0].Words.Should().Contain((8, 9), "the word Chronicles adds shifts the rest by one");
    }

    [Fact]
    public void AFormulaEveryChapterRepeatsIsNotAParallel()
    {
        var text = new Text();
        for (var chapter = 1; chapter <= 6; chapter++)
        {
            text.Filler(3, chapter, 1, 2).Verse(3, chapter, 3, Story).Filler(3, chapter, 4, 5);
        }

        ParallelDetector.Detect(text.Tokens, Settings).Should().BeEmpty();
    }

    [Fact]
    public void ARepetitionWithinOneChapterIsNotAParallel()
    {
        var text = new Text().Verse(4, 7, 1, Story).Filler(4, 7, 2, 4).Verse(4, 7, 5, Story);

        ParallelDetector.Detect(text.Tokens, Settings).Should().BeEmpty();
    }

    [Fact]
    public void AShortSharedPhraseIsNotAParallel()
    {
        var text = new Text()
            .Filler(1, 1, 1, 2).Verse(1, 1, 3, Story[..8]).Filler(1, 1, 4, 5)
            .Filler(2, 3, 1, 2).Verse(2, 3, 3, Story[..8]).Filler(2, 3, 4, 5);

        ParallelDetector.Detect(text.Tokens, Settings).Should().BeEmpty();
    }

    [Fact]
    public void TheSharedWordsAreWrittenAsPositionPairs() =>
        CrossReferenceFiles.Words([(1, 1), (2, 3), (4, 5)]).Should().Be("1-1 2-3 4-5");
}

/// <summary>The producer of OpenBible's set over a small file of its shape: the file the build loads into cross_reference.</summary>
public sealed class CrossReferenceFileTests : IDisposable
{
    private const string Head = "From Verse\tTo Verse\tVotes\t#www.openbible.info CC-BY 2026-09-21\n";

    private readonly string _resources = Path.Combine(Path.GetTempPath(), "essenthos-products", Guid.NewGuid().ToString("N"));

    public CrossReferenceFileTests() => Directory.CreateDirectory(Path.Combine(_resources, OpenBibleCrossReferences.Folder));

    public void Dispose()
    {
        if (Directory.Exists(_resources))
        {
            Directory.Delete(_resources, recursive: true);
        }
    }

    private string Source => Path.Combine(_resources, OpenBibleCrossReferences.Folder, OpenBibleCrossReferences.FileName);

    private List<string?[]> Rows()
    {
        using var reader = new ProductStore(_resources).Open(CrossReferenceFiles.OpenBibleProduct,
            RecordProducers.File(CrossReferenceFiles.OpenBibleProduct).Schema());
        return [.. reader.ReadAll()];
    }

    private CrossReferenceOutcome Written() => CrossReferenceFiles.OpenBible(new ProductStore(_resources), force: false, NullLogger.Instance);

    [Fact]
    public void ASetIsWrittenRankedByItsVotesAndOnlyAgainWhenItsFileMoves()
    {
        File.WriteAllText(Source, Head + "Gen.1.1\tRev.3.14\t47\nGen.1.1\tRom.1.19-Rom.1.20\t59\nGen.1.1\tJob.1.1\t-3\nGen.1.2\tPs.33.6\t10\n");

        Written().Rows.Should().Be(4);
        Rows().Select(r => (r[0], r[1], r[2], r[3], r[4], r[9])).Should().Equal(
            ("1.1:1", "18.1:1", null, "3", "-3", CrossReferenceSets.OpenBibleSource),
            ("1.1:1", "45.1:19", "45.1:20", "1", "59", CrossReferenceSets.OpenBibleSource),
            ("1.1:1", "66.3:14", null, "2", "47", CrossReferenceSets.OpenBibleSource),
            ("1.1:2", "19.33:6", null, "1", "10", CrossReferenceSets.OpenBibleSource));

        var store = new ProductStore(_resources);
        var file = RecordProducers.File(CrossReferenceFiles.OpenBibleProduct);
        store.NeedsBuild(file.Product, file.Kind, store.Inputs($"{OpenBibleCrossReferences.Folder}/{OpenBibleCrossReferences.FileName}"))
            .Should().BeFalse();
    }

    /// <summary>A pair whose far verse no text has (Philemon 2:10) is not written, and the verse's others close up behind it.</summary>
    [Fact]
    public void AReferenceToAVerseNoTextHasIsLeftOut()
    {
        File.WriteAllText(Source, Head + "Gen.49.8\tPhlm.2.10\t9\nGen.49.8\tPhil.2.10\t5\nGen.49.8\tRev.5.5\t3\n");

        Written();
        Rows().Select(r => (r[1], r[3])).Should().Equal(("50.2:10", "1"), ("66.5:5", "2"));
    }
}

[Trait(TestCategory.Name, TestCategory.Corpus)]
[Collection(WitnessDatabaseCollection.Name)]
public sealed class CrossReferencePassageTests : IDisposable
{
    private readonly AppDbContext _db;

    public CrossReferencePassageTests(WitnessDatabase database)
    {
        _db = database.NewContext();
        Clear();
    }

    public void Dispose()
    {
        Clear();
        _db.Dispose();
    }

    private void Clear() => _db.CrossReferences.ExecuteDelete();

    [Fact]
    public async Task APassageIsFoundByAnyPairOfItsVersesWithItsExtentOnBothSides()
    {
        _db.CrossReferences.AddRange(
            Parallel(10, 22, 2, 19, 18, 2, 1, "1-1 2-2 3-3"),
            Parallel(10, 22, 3, 19, 18, 3, 1, "1-1 2-3"),
            Parallel(19, 18, 2, 10, 22, 2, 2, "1-1 2-2 3-3"),
            Parallel(19, 18, 3, 10, 22, 3, 2, "1-1 3-2"));
        await _db.SaveChangesAsync();

        var passage = await CrossReferenceEndpoints.Passage(_db, (10, 22, 3), (19, 18, 3), default);

        passage.Should().NotBeNull();
        passage!.Text.Should().Be("BHSA");
        passage.Passage.From.First.Verse.Should().Be(2);
        passage.Passage.From.Last.Verse.Should().Be(3);
        passage.Passage.To.First.Slug.Should().Be("psalms");
        passage.Pairs.Select(p => p.Words.Count).Should().Equal(3, 2);

        var set = CrossReferenceSets.Find(CrossReferenceSets.Parallels)!;
        var chapter = await CrossReferenceEndpoints.InChapter(_db, set, 19, 18, default);
        chapter.Verses[0].References[0].Shared.Should().Be(3);
        chapter.Verses[0].References[0].Passage!.To.Last.Verse.Should().Be(3);

        (await CrossReferenceEndpoints.Passage(_db, (10, 22, 2), (19, 18, 3), default)).Should().BeNull();
    }

    private static CrossReference Parallel(int book, int chapter, int verse, int toBook, int toChapter, int toVerse, int passage, string matched) =>
        new()
        {
            Set = CrossReferenceSets.Parallels,
            Source = CrossReferenceSets.ParallelsSource,
            Book = book,
            Chapter = chapter,
            Verse = verse,
            ToBook = toBook,
            ToChapter = toChapter,
            ToVerse = toVerse,
            Rank = 1,
            Passage = passage,
            MatchedIn = "BHSA",
            Matched = matched,
        };
}
