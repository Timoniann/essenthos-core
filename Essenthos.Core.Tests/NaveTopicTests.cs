using Essenthos.Core.Database;
using Essenthos.Core.Loading.Encyclopedia;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Essenthos.Core.Tests;

/// <summary>
/// Nave's entries read as lines of verses: the book carried forward, a bare chapter as the whole
/// chapter, runs across a chapter end, the transcription's own spellings of three books, and the
/// heading a nested line is filed under.
/// </summary>
public sealed class NaveEntryTests
{
    private const int Genesis = 1;
    private const int Exodus = 2;
    private const int Numbers = 4;
    private const int FirstSamuel = 9;
    private const int FirstChronicles = 13;
    private const int Ezra = 15;
    private const int SongOfSolomon = 22;
    private const int Jude = 65;

    private static string Cited(IEnumerable<NaveEntries.Citation> citations) =>
        string.Join("; ", citations.Select(c => $"{c.Book} {c.Chapter}:{c.FirstVerse}-{c.LastVerse}"));

    [Fact]
    public void ACitationCarriesItsBookForwardAndABareChapterIsTheWholeOfIt()
    {
        var (heading, citations, unread) = NaveEntries.Read("-Descendants of EXO 6:23,25; 1CH 6:3-15,50-53; 24");

        heading.Should().Be("Descendants of");
        unread.Should().Be(0);
        Cited(citations).Should().Be(
            $"{Exodus} 6:23-23; {Exodus} 6:25-25; {FirstChronicles} 6:3-15; {FirstChronicles} 6:50-53; " +
            $"{FirstChronicles} 24:-");
    }

    [Fact]
    public void WithSeparatesAVerseFromItsQuotation()
    {
        var (_, citations, unread) = NaveEntries.Read("-Doeg slays the priests 1SA 22:20-23; with 22:6-19");

        unread.Should().Be(0);
        Cited(citations).Should().Be($"{FirstSamuel} 22:20-23; {FirstSamuel} 22:6-19");
    }

    /// <summary>A run from one chapter into a later one is its first chapter to the end, the ones between whole, and the last from its first verse.</summary>
    [Fact]
    public void ARunCrossingChapterEndsIsSplitAtEachOne()
    {
        var (_, citations, _) = NaveEntries.Read("-Creation GEN 1:26-3:5");

        Cited(citations).Should().Be($"{Genesis} 1:26-; {Genesis} 2:-; {Genesis} 3:1-5");
    }

    [Fact]
    public void TheTranscriptionsOwnSpellingsOfThreeBooksAreRead()
    {
        var (_, citations, unread) = NaveEntries.Read("-Kiss GEN 29:11; So 5:3; Jude 1:8-13; 1JHN 2:1");

        unread.Should().Be(0);
        citations.Select(c => c.Book).Should().Equal(Genesis, SongOfSolomon, Jude, 62);
    }

    /// <summary>A heading's own words are not a book because they happen to be followed by a number.</summary>
    [Fact]
    public void AHeadingIsTheWordsBeforeTheFirstCodeInCapitals()
    {
        var (heading, citations, _) = NaveEntries.Read("-2. Of the tribe of Simeon, Job 3 times 1CH 4:25,26");

        heading.Should().Be("2. Of the tribe of Simeon, Job 3 times");
        citations.Should().HaveCount(2).And.OnlyContain(c => c.Book == FirstChronicles && c.Chapter == 4);
    }

    [Fact]
    public void WhatDoesNotReadIsCountedAndTheRestIsKept()
    {
        var (_, citations, unread) = NaveEntries.Read("-Praise PSA 47:7: \"Sing ye praises\"; 100:1");

        unread.Should().Be(1);
        Cited(citations).Should().Be("19 100:1-1");
    }

    /// <summary>A nested line is filed under the line above it; a line that only points elsewhere files nothing.</summary>
    [Fact]
    public void ANestedLineIsFiledUnderTheLineAboveIt()
    {
        const string entry = "-See WIND\n-Instances of\n     -Ezra EZR 9:5,6\n     -Nehemiah NEH 1:4\n-Offered GEN 4:4";

        var lines = NaveEntries.Lines(entry).ToList();

        lines.Select(l => l.Heading).Should().Equal("Instances of — Ezra", "Instances of — Nehemiah", "Offered");
        lines[0].Citations.Should().OnlyContain(c => c.Book == Ezra);
    }

    /// <summary>
    /// Every entry of the file as it is shipped, read. Almost nothing may be dropped: what is, is
    /// counted, and a parser change that starts dropping citations shows up here as a number.
    /// </summary>
    [Fact]
    public void TheShippedIndexReadsAlmostWhole()
    {
        var path = TestResources.Path("BibleData2026", NaveTopicLoader.FileName);
        var rows = Essenthos.Core.Loading.Encyclopedia.Csv.Read(path).ToList();
        var lines = rows.SelectMany(row => NaveEntries.Lines(row["entry"])).ToList();

        rows.Should().HaveCountGreaterThan(5000);
        var cited = lines.Sum(l => l.Citations.Count);
        var unread = lines.Sum(l => l.Unread);
        cited.Should().BeGreaterThan(75_000);
        ((double)unread / cited).Should().BeLessThan(0.001, $"{unread} pieces of {cited} did not read");

        var aaron = rows.Single(row => row["subject"] == "AARON");
        NaveEntries.Lines(aaron["entry"])
            .Single(l => l.Heading == "Makes the golden calf")
            .Citations.Should().Contain(new NaveEntries.Citation(Exodus, 32, null, null));
    }

    /// <summary>
    /// A verse the transcription writes after a semicolon, where Nave has a comma, is read as the
    /// verse: Amram is in Exodus 6:20, and Exodus 20 does not mention him.
    /// </summary>
    [Fact]
    public void AListedVerseAfterASemicolonIsReadAsTheVerseNotTheChapter()
    {
        const string entry = "-1. Father of Moses EXO 6:18; 20; NUM 26:58,59";

        Cited(NaveEntries.Read(entry).Citations).Should().StartWith($"{Exodus} 6:18-18; {Exodus} 20:-");
        Cited(NaveEntries.Read(NaveCorrections.Apply("AMRAM", entry)).Citations).Should().Be(
            $"{Exodus} 6:18-18; {Exodus} 6:20-20; {Numbers} 26:58-58; {Numbers} 26:59-59");
    }

    [Fact]
    public void ACorrectionIsOnlyForItsOwnSubject()
    {
        const string entry = "-Lineage of EXO 6:18; 20";

        NaveCorrections.Apply("AARON", entry).Should().Be(entry);
    }

    /// <summary>
    /// Every listed correction finds its citation once in its subject's entries in the file as it is
    /// shipped, and turns a whole chapter into a verse; a new release that fixed or moved one shows
    /// up here.
    /// </summary>
    [Fact]
    public void EveryCorrectionMatchesTheShippedIndexOnceAndTurnsAChapterIntoAVerse()
    {
        var path = TestResources.Path("BibleData2026", NaveTopicLoader.FileName);
        var rows = Essenthos.Core.Loading.Encyclopedia.Csv.Read(path).ToList();

        foreach (var correction in NaveCorrections.All)
        {
            var entries = rows.Where(row => row["subject"].Trim() == correction.Subject).Select(row => row["entry"]).ToList();
            entries.Sum(e => Occurrences(e, correction.Written)).Should().Be(1, correction.Written);

            var before = WholeChapters(entries);
            var after = WholeChapters(entries.Select(e => e.Replace(correction.Written, correction.Read, StringComparison.Ordinal)));
            after.Should().Be(before - 1, correction.Written);
        }

        static int Occurrences(string text, string fragment) =>
            (text.Length - text.Replace(fragment, "", StringComparison.Ordinal).Length) / fragment.Length;

        static int WholeChapters(IEnumerable<string> entries) =>
            entries.SelectMany(NaveEntries.Lines).SelectMany(l => l.Citations).Count(c => c.FirstVerse is null);
    }
}

/// <summary>
/// The loader over a small index of its own shape. Asked of Postgres because the references go in
/// by binary copy, and a copy that got a column's type wrong fails only there.
/// </summary>
[Collection(WitnessDatabaseCollection.Name)]
public sealed class NaveTopicLoadTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly string _folder;

    public NaveTopicLoadTests(WitnessDatabase database)
    {
        _db = database.NewContext();
        Clear();
        _folder = Directory.CreateTempSubdirectory("nave-").FullName;
        File.WriteAllText(
            Path.Combine(_folder, NaveTopicLoader.FileName),
            "\uFEFFsection,subject,entry\n" +
            "A,AARON,\"-Lineage of EXO 6:16-20\n-Makes the golden calf EXO 32; ACT 7:40\n-Rod of, buds NUM 17\"\n" +
            "A,ABASEMENT,\"-See HUMILITY\"\n" +
            "L,LORD'S SUPPER,\"MAT 26:26-28\"\n");
    }

    public void Dispose()
    {
        Clear();
        _db.Dispose();
        Directory.Delete(_folder, recursive: true);
    }

    [Fact]
    public async Task ASubjectIsWrittenWithTheVersesFiledUnderItAndOneThatOnlyPointsElsewhereIsNot()
    {
        var outcome = await new NaveTopicLoader(_db, NullLogger<NaveTopicLoader>.Instance).Load(_folder);

        outcome.Topics.Should().Be(2);
        outcome.CrossReferencesOnly.Should().Be(1);
        outcome.References.Should().Be(5);
        outcome.Unread.Should().Be(0);

        var aaron = await _db.Topics.Include(t => t.References).SingleAsync(t => t.Slug == "aaron");
        aaron.Name.Should().Be("AARON");
        aaron.References.Select(r => (r.CanonicalBook, r.CanonicalChapter, r.FirstVerse, r.LastVerse, r.Heading))
            .Should().BeEquivalentTo(new (int, int, int?, int?, string?)[]
            {
                (2, 6, 16, 20, "Lineage of"),
                (2, 32, null, null, "Makes the golden calf"),
                (44, 7, 40, 40, "Makes the golden calf"),
                (4, 17, null, null, "Rod of, buds"),
            });

        var supper = await _db.Topics.Include(t => t.References).SingleAsync(t => t.Slug == "lordssupper");
        supper.References.Should().ContainSingle().Which.Heading.Should().BeNull();
    }

    [Fact]
    public async Task ASecondBootWritesNothing()
    {
        var loader = new NaveTopicLoader(_db, NullLogger<NaveTopicLoader>.Instance);
        await loader.Load(_folder);

        var again = await loader.Load(_folder);

        again.AlreadyLoaded.Should().BeTrue();
        (await _db.TopicReferences.CountAsync()).Should().Be(5);
    }

    private void Clear()
    {
        _db.TopicReferences.ExecuteDelete();
        _db.Topics.ExecuteDelete();
    }
}
