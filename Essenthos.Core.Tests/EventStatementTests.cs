using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Endpoints;
using Essenthos.Core.Loading;
using Essenthos.Core.Loading.Encyclopedia;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
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
    /// a year late while the figure he printed sat unread in the file it loaded. The 23 his Annals
    /// date where BibleData's column is blank are read the same way, and five of them fall in the
    /// autumn half of his year.
    /// </summary>
    [Fact]
    public async Task UssherIsServedTheYearHePrinted()
    {
        var dates = await UssherDates(BibleDataLoader.Source);

        dates.Should().HaveCount(419 + 23).And.OnlyContain(d => d.StatedYear < 0);
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

    private async Task<List<EventDate>> UssherDates(string source) =>
        await _db.EventDates
            .Where(d => d.Chronology!.Slug == UssherAnnalsLoader.Chronology && d.Event!.Source == source)
            .ToListAsync();
}
