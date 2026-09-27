using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Endpoints;
using Essenthos.Core.Loading;
using Essenthos.Core.Loading.Encyclopedia;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Essenthos.Core.Tests;

/// <summary>
/// What the event sources state beside the year from creation, read and kept: the year Ussher
/// printed, the Annals dated by the two of their three columns that agree, and the verse at which
/// BibleData names where each event happened. Loaded from the files themselves, because every
/// figure here is a count of the source's own rows.
/// </summary>
[Trait(TestCategory.Name, TestCategory.Corpus)]
[Collection(WitnessDatabaseCollection.Name)]
public sealed class EventStatementTests : IClassFixture<UssherAnnalsLoadTests.Annals>, IDisposable
{
    private const int UssherZeroPoint = 4003;

    private readonly AppDbContext _db;
    private readonly IDbContextTransaction _transaction;
    private readonly string _folder;

    public EventStatementTests(WitnessDatabase database)
    {
        _db = database.NewContext();
        _transaction = _db.Database.BeginTransaction();
        _folder = Path.GetDirectoryName(TestResources.Path("BibleData2026", "BibleData-Person.csv"))!;
    }

    public void Dispose()
    {
        _transaction.Rollback();
        _transaction.Dispose();
        _db.Dispose();
    }

    /// <summary>
    /// The creation is 4004 BC in Ussher's words and 4003 by the subtraction his zero makes of his
    /// year 1 — and so are 80 more of the 419 events BibleData dates by him, which the corpus served
    /// a year late while the figure he printed sat unread in the file it loaded. The 22 his Annals
    /// date where BibleData's column is blank are read the same way, and five of them fall in the
    /// autumn half of his year.
    /// </summary>
    [Fact]
    public async Task UssherIsServedTheYearHePrinted()
    {
        var dates = await UssherDates(BibleDataLoader.Source);

        dates.Should().HaveCount(419 + 22).And.OnlyContain(d => d.StatedYear < 0);
        dates.Count(d => -d.StatedYear != UssherZeroPoint - d.Year + 1).Should().Be(81 + 5);

        var creation = await _db.Events.Where(e => e.Slug == "creation").Select(EncyclopediaEndpoints.Rows).SingleAsync();
        var ussher = creation.Dates.Single(d => d.Chronology == UssherAnnalsLoader.Chronology);
        ussher.Year.Should().Be(1);
        ussher.BceYear.Should().Be(4004);
        ussher.Era.Should().Be("BCE");
        ussher.Stated.Should().BeTrue();
    }

    /// <summary>
    /// Every paragraph the transcription dates AD 33 carries anno mundi 4046, ten years off the 4036
    /// its Gregorian and Julian Period columns both give. Those two agree, so his reckoning is dated
    /// by them; the anno mundi figure is not repaired, it is left out. And the Gregorian year is the
    /// one served everywhere, since the subtraction is a year late for a paragraph set in the autumn.
    /// </summary>
    [Fact]
    public async Task TheAnnalsAreDatedByTheColumnsThatAgree()
    {
        var annals = await _db.Events.CountAsync(e => e.Source == UssherAnnalsLoader.Source);
        var dates = await UssherDates(UssherAnnalsLoader.Source);

        dates.Should().HaveCount(annals, "every paragraph loaded has two columns that agree on its year");
        dates.Should().OnlyContain(d => d.StatedYear > 0);
        dates.Where(d => d.Year is not null)
            .Should().OnlyContain(d => d.Year - UssherZeroPoint == d.StatedYear || d.Year - UssherZeroPoint == d.StatedYear + 1);

        dates.Count(d => d.Year - UssherZeroPoint == d.StatedYear + 1)
            .Should().Be(77, "the paragraphs set in the autumn, which the subtraction served a year late");

        var passion = dates.Where(d => d.Year is null).ToList();
        passion.Should().HaveCount(91).And.OnlyContain(d => d.StatedYear == 33);
    }

    /// <summary>
    /// A reign BibleData dates and leaves blank in Ussher's column is his all the same where his
    /// Annals date both its ends: Artaxerxes comes to the throne in ¶1178 and dies in ¶1291.
    /// </summary>
    [Fact]
    public async Task TheAnnalsDateWhatBibleDataLeavesBlankInHisColumn()
    {
        var reign = await _db.EventDates
            .Where(d => d.Chronology!.Slug == UssherAnnalsLoader.Chronology
                        && (d.Event!.Slug == "beginartaxerxes1reign" || d.Event.Slug == "endartaxerxes1reign"))
            .OrderBy(d => d.Year)
            .Select(d => new { d.Year, d.StatedYear, d.Citation })
            .ToListAsync();

        reign.Should().Equal(
            new { Year = (int?)3531, StatedYear = (int?)-473, Citation = (string?)"¶1178" },
            new { Year = (int?)3579, StatedYear = (int?)-425, Citation = (string?)"¶1291" });
    }

    /// <summary>Ussher's is the reckoning a reader meets first; BibleData's is second and the base.</summary>
    [Fact]
    public async Task UssherIsTheReckoningAReaderMeetsFirst()
    {
        var reckonings = await _db.Chronologies.OrderBy(c => c.Position).Select(c => new { c.Slug, c.IsDefault }).ToListAsync();

        reckonings[0].Should().Be(new { Slug = "ussher", IsDefault = true });
        reckonings.Should().ContainSingle(c => c.IsDefault);
        reckonings[1].Slug.Should().Be("bibledata");
    }

    /// <summary>
    /// Ishmael's birth is dated at Genesis 16:16 and the source names its Canaan at 16:3; the second
    /// verse is the one that says which Canaan.
    /// </summary>
    [Fact]
    public async Task AnEventKeepsTheVerseItsSourceNamesItsLocationAt()
    {
        var birth = await _db.Events.SingleAsync(e => e.Slug == Slugs.Of("Birth_Ishmael_1"));

        (birth.LocationBook, birth.LocationChapter, birth.LocationVerse).Should().Be((1, 16, 3));
        (birth.CanonicalChapter, birth.CanonicalVerse).Should().Be((16, 16));
        (await _db.Events.CountAsync(e => e.LocationBook != null))
            .Should().Be(145, "147 locations name a verse, one of them 2 Kings 16:34, which has no such verse, and one writes Ezekiel as EZE");
    }

    /// <summary>
    /// A corpus loaded before these columns were read, brought to what a fresh load writes — and a
    /// second pass, like a fresh load, has nothing left to do.
    /// </summary>
    [Fact]
    public async Task ARestatementBringsAnOlderLoadToWhatAFreshOneWrites()
    {
        var fresh = await Snapshot();
        var world = World("Stele, dated by its inception, in Khedivate of Egypt. From Wikidata.", "Khedivate of Egypt");
        await Forget();

        var restated = await Restate();

        restated.Located.Should().Be(fresh.Located.Count);
        restated.Stated.Should().Be(fresh.Stated.Count - fresh.Dated.Count - restated.Filled);
        restated.Filled.Should().Be(22);
        restated.Dated.Should().Be(fresh.Dated.Count);
        restated.Ordered.Should().Be(2, "the older load had BibleData first and the default");
        restated.Described.Should().Be(1);
        (await Snapshot()).Should().BeEquivalentTo(fresh);
        (await _db.Events.SingleAsync(e => e.Id == world.Id)).Description
            .Should().Be("Stele, dated by its inception. From Wikidata.");

        var again = await Restate();
        (again.Located + again.Stated + again.Filled + again.Dated + again.Described + again.Placed + again.Ordered)
            .Should().Be(0);
    }

    private async Task<List<EventDate>> UssherDates(string source) =>
        await _db.EventDates
            .Where(d => d.Chronology!.Slug == UssherAnnalsLoader.Chronology && d.Event!.Source == source)
            .ToListAsync();

    private sealed record State(
        Dictionary<int, (int?, int?, int?)> Located,
        Dictionary<(int, int), int?> Stated,
        Dictionary<int, int?> Dated,
        Dictionary<int, string?> Notes);

    private async Task<State> Snapshot()
    {
        var located = await _db.Events.AsNoTracking().Where(e => e.LocationBook != null)
            .ToDictionaryAsync(e => e.Id, e => (e.LocationBook, e.LocationChapter, e.LocationVerse));
        var stated = await _db.EventDates.AsNoTracking().Where(d => d.StatedYear != null)
            .ToDictionaryAsync(d => (d.EventId, d.ChronologyId), d => d.StatedYear);
        var dated = await _db.EventDates.AsNoTracking().Where(d => d.StatedYear != null && d.Year == null)
            .ToDictionaryAsync(d => d.EventId, d => d.StatedYear);
        var notes = await _db.Events.AsNoTracking().Where(e => e.Source == UssherAnnalsLoader.Source)
            .ToDictionaryAsync(e => e.Id, e => e.Notes);
        return new State(located, stated, dated, notes);
    }

    /// <summary>
    /// What a load before this one wrote: none of the three, no date from the Annals where BibleData's
    /// column is blank, BibleData's reckoning first and the default, and the old sentence on the notes.
    /// </summary>
    private async Task Forget()
    {
        var listed = UssherDatings.Read(_folder).Keys.ToList();
        _db.EventDates.RemoveRange(await _db.EventDates
            .Where(d => d.Chronology!.Slug == UssherAnnalsLoader.Chronology && listed.Contains(d.Event!.Slug))
            .ToListAsync());
        await _db.Chronologies.Where(c => c.Slug == "bibledata")
            .ExecuteUpdateAsync(c => c.SetProperty(x => x.IsDefault, true).SetProperty(x => x.Position, 1));
        await _db.Chronologies.Where(c => c.Slug == UssherAnnalsLoader.Chronology)
            .ExecuteUpdateAsync(c => c.SetProperty(x => x.IsDefault, false).SetProperty(x => x.Position, 2));

        var undated = await _db.EventDates.Where(d => d.StatedYear != null && d.Year == null).ToListAsync();
        var ids = undated.Select(d => d.EventId).ToList();
        foreach (var annal in await _db.Events.Where(e => ids.Contains(e.Id)).ToListAsync())
        {
            annal.Notes = annal.Notes!.Replace(
                UssherAnnalsLoader.Contradiction("4046b", 33, 33),
                UssherAnnalsLoader.Unread("4046b", 33),
                StringComparison.Ordinal);
        }

        _db.EventDates.RemoveRange(undated);
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();

        await _db.EventDates.ExecuteUpdateAsync(d => d.SetProperty(x => x.StatedYear, (int?)null));
        await _db.Events.ExecuteUpdateAsync(e => e
            .SetProperty(x => x.LocationBook, (int?)null)
            .SetProperty(x => x.LocationChapter, (int?)null)
            .SetProperty(x => x.LocationVerse, (int?)null));

        (await _db.Events.CountAsync(e => e.Notes!.Contains(UssherAnnalsLoader.Unread("4046b", 33))))
            .Should().BePositive("the notes an older load wrote are what the pass has to rewrite");
    }

    private async Task<EventRestatementOutcome> Restate()
    {
        _db.ChangeTracker.Clear();
        var outcome = await new EventRestatementLoader(_db, NullLogger<EventRestatementLoader>.Instance)
            .Load(_folder, Path.Combine(AppContext.BaseDirectory, "Resources", "WorldHistory"));
        _db.ChangeTracker.Clear();
        return outcome;
    }

    private Event World(string description, string region)
    {
        var one = new Event
        {
            Slug = "world-stele-of-the-test",
            Name = "A stele of the test",
            Description = description,
            Realm = Realms.World,
            Region = region,
            Source = WorldHistoryLoader.Source,
        };
        _db.Events.Add(one);
        _db.SaveChanges();
        return one;
    }
}
