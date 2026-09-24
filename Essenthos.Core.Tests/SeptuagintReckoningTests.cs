using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Loading;
using Essenthos.Core.Loading.Encyclopedia;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Essenthos.Core.Tests;

public class GreekNumeralTests
{
    [Theory]
    [InlineData("Καὶ ἔζησε Μαθουσάλα ἑπτὰ ἔτη καὶ ἑξήκοντα καὶ ἑκατόν· καὶ ἐγέννησε τὸν Λάμεχ.", 167)]
    [InlineData("Καὶ ἔζησε Ναχὼρ ἔτη ἑκατὸν ἑβδομηκονταεννέα, καὶ ἐγέννησε τὸν Θάῤῥα.", 179)]
    [InlineData("Καὶ ἔζησεν Ἀρφαξὰδ ἑκατὸν τριακονταπέντε ἔτη, καὶ ἐγέννησε τὸν Καϊνᾶν.", 135)]
    [InlineData("Νῶε δὲ ἦν ἐτῶν ἑξακοσίων, καὶ ὁ κατακλυσμὸς τοῦ ὕδατος ἐγένετο ἐπὶ τῆς γῆς.", 600)]
    [InlineData("Καὶ ἐγενήθη ἐν τῷ τεσσαρακοστῷ καὶ τετρακοσιοστῷ ἔτει τῆς ἐξόδου", 440)]
    public void ANumberWrittenInWordsIsReadInAnyOrder(string verse, int number) =>
        GreekNumerals.In(verse).Should().StartWith([number]);

    /// <summary>The Greek's Genesis 11:13, which holds Arphaxad's last years and all of Cainan.</summary>
    [Fact]
    public void AVerseStatingThreeNumbersGivesThreeRuns() =>
        GreekNumerals.In(
                "Καὶ ἔζησεν Ἀρφαξὰδ, μετὰ τὸ γεννῆσαι αὐτὸν τὸν Καϊνᾶν, ἔτη τετρακόσια, καὶ ἐγέννησεν υἱοὺς " +
                "καὶ θυγατέρας, καὶ ἀπέθανε. Καὶ ἔζησε Καϊνᾶν ἑκατὸν καὶ τριάκοντα ἔτη, καὶ ἐγέννησε τὸν Σαλά· " +
                "καὶ ἔξησε Καϊνᾶν, μετὰ τὸ γεννῆσαι αὐτὸν τὸν Σαλὰ, ἔτη τριακόσια τριάκοντα")
            .Should().Equal(400, 130, 330);

    /// <summary>Swete's optical reading of the same verse, one letter wrong in three of its numbers.</summary>
    [Theory]
    [InlineData("τριόκοντα", 30)]
    [InlineData("τριοκόσια", 300)]
    [InlineData("ἑπτκόσια", 700)]
    public void AWordOneLetterFromANumberIsThatNumber(string word, int number) =>
        GreekNumerals.Value(GreekNumerals.Tokens(word).Single()).Should().Be(number);

    [Theory]
    [InlineData("ἔζησεν")]
    [InlineData("ἐγέννησεν")]
    [InlineData("ἐνιαυτῷ")]
    public void AWordThatIsNoNumberIsNone(string word) =>
        GreekNumerals.Value(GreekNumerals.Tokens(word).Single()).Should().BeNull();
}

/// <summary>Both editions read once; Swete's is eleven megabytes of one token per line.</summary>
public sealed class SeptuagintEditions
{
    internal Func<int, int, int, string?> Brenton { get; } =
        Verses(SeptuagintTextSource.Read(TestResources.SeptuagintFolder));

    internal Func<int, int, int, string?> Swete { get; } = Verses(SweteTextSource.Read(TestResources.SweteFolder));

    /// <summary>Brenton with Genesis 5:25–26 from Swete, as the Alexandrinus reckoning reads.</summary>
    internal Func<int, int, int, string?> Alexandrinus => SeptuagintReckoningLoader.Edition(
        SeptuagintReckoningLoader.Definitions[1], Brenton, Swete);

    private static Func<int, int, int, string?> Verses(TextSource source)
    {
        var verses = source.Books
            .Where(book => book.CanonicalOrdinal is 1 or 2 or 11)
            .SelectMany(book => book.Chapters.SelectMany(chapter => chapter.Verses
                .Where(verse => verse.Label.Length == 0)
                .Select(verse => (Key: (book.CanonicalOrdinal, chapter.Number, verse.Number),
                    Text: string.Join(' ', verse.Words.Select(word => word.Surface))))))
            .ToDictionary(verse => verse.Key, verse => verse.Text);
        return (book, chapter, verse) => verses.GetValueOrDefault((book, chapter, verse));
    }
}

public class SeptuagintReadingTests(SeptuagintEditions editions) : IClassFixture<SeptuagintEditions>
{
    [Fact]
    public void EveryNumberIsReadAndEveryLifeInGenesisFiveCloses()
    {
        SeptuagintReckoning.Read(editions.Brenton).Problems.Should().BeEmpty();
        SeptuagintReckoning.Read(editions.Alexandrinus).Problems.Should().BeEmpty();
    }

    /// <summary>
    /// The two readings the owner asked to see side by side, and the ones they share. Swete's own
    /// Genesis is read only where the reckoning takes it: its digitisation loses a number word at
    /// 9:28 and at 12:4, which is why the second reckoning is Brenton's but for Methuselah.
    /// </summary>
    [Theory]
    [InlineData("methuselah-begets", 167, 187)]
    [InlineData("methuselah-after", 802, 782)]
    [InlineData("nahor-begets", 179, 179)]
    [InlineData("adam-begets", 230, 230)]
    [InlineData("lamech-begets", 188, 188)]
    [InlineData("arphaxad-begets", 135, 135)]
    [InlineData("cainan-begets", 130, 130)]
    [InlineData("terah-life", 205, 205)]
    public void TheEditionsReadGenesisAsTheyPrintIt(string key, int brenton, int alexandrinus)
    {
        SeptuagintReckoning.Read(editions.Brenton).Values[key].Value.Should().Be(brenton);
        SeptuagintReckoning.Read(editions.Alexandrinus).Values[key].Value.Should().Be(alexandrinus);
    }

    [Fact]
    public void SwetesGenesisAloneWouldComputeADigitisationFault() =>
        SeptuagintReckoning.Read(editions.Swete).Problems.Should().Contain(problem => problem.StartsWith("noah"));

    [Fact]
    public void ExodusAndKingsAreReadFromBrentonForBoth()
    {
        var values = SeptuagintReckoning.Read(editions.Alexandrinus).Values;

        values[SeptuagintReckoning.Sojourn].Value.Should().Be(430);
        values[SeptuagintReckoning.ExodusToTemple].Value.Should().Be(440);
    }

    [Theory]
    [InlineData(true, 2243, 3476)]
    [InlineData(false, 2263, 3496)]
    public void TheFloodAndAbramFallWhereTheGreekPutsThem(bool brenton, int flood, int abram)
    {
        var years = SeptuagintReckoning.Compute(
            SeptuagintReckoning.Read(brenton ? editions.Brenton : editions.Alexandrinus).Values, id => id);

        years["Begin_Flood"].Year.Should().Be(flood);
        years[SeptuagintReckoning.AbramBorn].Year.Should().Be(abram);
    }

    /// <summary>
    /// The famous consequence of 167: Methuselah outlives the Flood by fourteen years. Drawn as it
    /// falls, because the whole reason these reckonings exist is to show the working.
    /// </summary>
    [Fact]
    public void AtOneHundredAndSixtySevenMethuselahOutlivesTheFlood()
    {
        var years = SeptuagintReckoning.Compute(SeptuagintReckoning.Read(editions.Brenton).Values, id => id);

        (years["Death_Methuselah_1"].Year - years["Begin_Flood"].Year).Should().Be(14);
    }

    [Fact]
    public void EveryYearSaysHowItWasReached()
    {
        var years = SeptuagintReckoning.Compute(SeptuagintReckoning.Read(editions.Brenton).Values, id => id);

        years["Birth_Seth_1"].Calculation.Should().Be("Birth_Seth_1 is Birth_Adam_1 (1) + 230 (Genesis 5:3) = 231");
        years["Birth_Seth_1"].Citation.Should().Be("Genesis 5:3");
    }
}

public class SeptuagintSideTests
{
    private const int Exodus = 2515;
    private const int Temple = 2995;

    /// <summary>
    /// BibleData's own arithmetic around Eli: nothing names the Temple, but each step counts from
    /// a year that is counted back from it, so each lands on the Temple's side.
    /// </summary>
    [Fact]
    public void AnEventCountedBackFromTheTempleIsOnItsSide()
    {
        var sides = SeptuagintReckoning.Sides(
        [
            ("samson-begins", 2886, "Abdon_1's time as judge ended = 2886"),
            ("abdon-begins", 2878, "(2878)"),
            ("exodus", Exodus, null),
            ("temple", Temple, null),
            ("david-moves-the-ark", 2958, "the year his reign there ended (2991) - 33 = 2958"),
            ("solomon-reigns", 2991, "the year Solomon began construction on his Temple (2995) - 4 years = 2991"),
            ("kiriath-jearim", 2938, "the year David moved it to Jerusalem (2958) - the years it was in K-j (20) = 2938"),
            ("beth-shemesh", 2937, "the year before it was moved to Kiriath-jearim (2938 - 1) = 2937"),
            ("ark-taken", 2936, "the year it was returned (2937) - 1 = 2936"),
            ("eli-dies", 2936, "the year the ark was taken in battle = 2936"),
            ("eli-born", 2838, "the year he died (2936) - his age when he died (98) = 2838"),
        ], Exodus, Temple);

        sides["eli-born"].Should().Be(SeptuagintSide.Temple);
        sides["eli-dies"].Should().Be(SeptuagintSide.Temple);
        sides.Should().NotContainKey("samson-begins", "nothing it counts from is on either side yet");
    }

    [Fact]
    public void AnEventCountedForwardFromTheExodusIsOnItsSide()
    {
        var sides = SeptuagintReckoning.Sides(
        [
            ("exodus", Exodus, null),
            ("entered-the-land", 2555, "the year of the Exodus (2515) + 40 = 2555"),
            ("conquest-ends", 2562, "the year they entered the Land (2555) + 7 years of conquest = 2562"),
        ], Exodus, Temple);

        sides["conquest-ends"].Should().Be(SeptuagintSide.Exodus);
    }
}

/// <summary>The two reckonings written into a database beside the base one and the world layer.</summary>
[Collection(WitnessDatabaseCollection.Name)]
public sealed class SeptuagintReckoningLoadTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly IDbContextTransaction _transaction;
    private readonly SeptuagintOutcome _outcome;

    public SeptuagintReckoningLoadTests(WitnessDatabase database)
    {
        _db = database.NewContext();
        _transaction = _db.Database.BeginTransaction();

        var resources = Path.GetDirectoryName(TestResources.SeptuagintFolder)!;
        new BibleDataLoader(_db, NullLogger<BibleDataLoader>.Instance)
            .Load(Path.Combine(resources, "BibleData2026")).GetAwaiter().GetResult();
        new WorldHistoryLoader(_db, NullLogger<WorldHistoryLoader>.Instance)
            .Load(Path.Combine(resources, "WorldHistory")).GetAwaiter().GetResult();
        _outcome = new SeptuagintReckoningLoader(_db, NullLogger<SeptuagintReckoningLoader>.Instance)
            .Load(resources).GetAwaiter().GetResult();
    }

    public void Dispose()
    {
        _transaction.Rollback();
        _transaction.Dispose();
        _db.Dispose();
    }

    [Fact]
    public async Task EachReadingIsAReckoningOfItsOwnAndEveryEventItReachesIsDated()
    {
        _outcome.AlreadyLoaded.Should().BeFalse();
        _outcome.Unplaced.Should().Be(0, "every event of the base reckoning is on one side of the arithmetic or the other");

        var brenton = await _db.Chronologies.SingleAsync(c => c.Slug == "septuagint");
        var alexandrinus = await _db.Chronologies.SingleAsync(c => c.Slug == "septuagint-alexandrinus");
        var based = await _db.Chronologies.SingleAsync(c => c.Slug == "bibledata");

        // The Temple keeps its historical year, so the zero moves by what comes before it: 1,466
        // years of genealogy less the forty of 1 Kings 6:1.
        brenton.LastYearBeforeTheCommonEra.Should().Be(based.LastYearBeforeTheCommonEra + 1466 - 40);
        alexandrinus.LastYearBeforeTheCommonEra.Should().Be(based.LastYearBeforeTheCommonEra + 1486 - 40);

        var dated = await _db.EventDates.CountAsync(d => d.ChronologyId == brenton.Id);
        var baseDated = await _db.EventDates.CountAsync(d => d.ChronologyId == based.Id);
        dated.Should().Be(baseDated + 2, "every event the base reckoning dates, and Cainan twice");

        (await _db.EventDates.AllAsync(d => d.ChronologyId != brenton.Id || d.Calculation != null || d.Event!.Realm == Realms.World))
            .Should().BeTrue("a computed year shows its arithmetic");
    }

    [Fact]
    public async Task AWorldEventKeepsItsHistoricalYear()
    {
        var brenton = await _db.Chronologies.SingleAsync(c => c.Slug == "septuagint");
        var based = await _db.Chronologies.SingleAsync(c => c.Slug == "bibledata");
        var world = await _db.Events.Where(e => e.Realm == Realms.World)
            .Select(e => new
            {
                Here = e.Dates.Single(d => d.ChronologyId == brenton.Id).Year,
                There = e.Dates.Single(d => d.ChronologyId == based.Id).Year,
            })
            .ToListAsync();

        world.Should().NotBeEmpty();
        world.Should().OnlyContain(w =>
            w.Here - brenton.LastYearBeforeTheCommonEra == w.There - based.LastYearBeforeTheCommonEra);
    }

    [Fact]
    public async Task CainanIsDatedOnlyWhereTheGreekHasHim()
    {
        var cainan = await _db.Events
            .Where(e => e.Source == SeptuagintReckoningLoader.CainanSource)
            .Select(e => e.Dates.Select(d => d.Chronology!.Slug).ToList())
            .ToListAsync();

        cainan.Should().HaveCount(2);
        cainan.Should().OnlyContain(slugs => slugs.OrderBy(s => s).SequenceEqual(new[] { "septuagint", "septuagint-alexandrinus" }));
    }

    /// <summary>
    /// A period is drawn from its two events, so a reckoning that moved one end past the other would
    /// draw a band running backwards. The forty years 1 Kings 6:1 removes come out of the Philistine
    /// oppression Samson and Eli share, and they must not turn it inside out.
    /// </summary>
    [Fact]
    public async Task NoPeriodRunsBackwards()
    {
        var brenton = await _db.Chronologies.SingleAsync(c => c.Slug == "septuagint");
        var years = await _db.EventDates
            .Where(d => d.ChronologyId == brenton.Id && d.Year != null)
            .ToDictionaryAsync(d => d.EventId, d => d.Year!.Value);
        var periods = await _db.Periods
            .Where(p => p.StartEventId != null && p.EndEventId != null)
            .Select(p => new { p.Name, Start = p.StartEventId!.Value, End = p.EndEventId!.Value })
            .ToListAsync();

        periods
            .Where(p => years.ContainsKey(p.Start) && years.ContainsKey(p.End) && years[p.Start] > years[p.End])
            .Select(p => p.Name)
            .Should().BeEmpty();
    }

    [Fact]
    public async Task LoadingAgainChangesNothing()
    {
        var before = await _db.EventDates.CountAsync();

        var again = await new SeptuagintReckoningLoader(_db, NullLogger<SeptuagintReckoningLoader>.Instance)
            .Load(Path.GetDirectoryName(TestResources.SeptuagintFolder)!);

        again.AlreadyLoaded.Should().BeTrue();
        (await _db.EventDates.CountAsync()).Should().Be(before);
    }
}
