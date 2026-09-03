using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Endpoints;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Xunit;

namespace Essenthos.Core.Tests;

/// <summary>
/// What year an event response gives, and in whose words.
///
/// BibleData ships a <c>bce_year</c> column beside the year from creation, and one payload used to
/// carry both: David's return to Jerusalem answered 980 at the top and 984 in its dates, from the
/// same reckoning of the same year. Two events the reckoning dates without trouble answered
/// nothing at the top because the column was empty, and a Jubilee past the turn said <c>AD</c> at
/// the top and <c>CE</c> below it.
///
/// Asked of Postgres, because the top-level answer is now read off the row the default chronology
/// contributes and the question is whether that row is found at all.
/// </summary>
[Collection(WitnessDatabaseCollection.Name)]
public sealed class EventEraTests : IDisposable
{
    /// <summary>The year from creation that is 1 BCE in BibleData's own reckoning.</summary>
    private const int BibleDataZeroPoint = 3961;

    private const int UssherZeroPoint = 4003;

    private readonly AppDbContext _db;
    private readonly IDbContextTransaction _transaction;
    private readonly Chronology _bibleData;
    private readonly Chronology _ussher;

    public EventEraTests(WitnessDatabase database)
    {
        _db = database.NewContext();
        _transaction = _db.Database.BeginTransaction();

        _bibleData = new Chronology
        {
            Slug = "bibledata",
            Name = "BibleData",
            LastYearBeforeTheCommonEra = BibleDataZeroPoint,
            IsDefault = true,
            Position = 1,
        };

        // Not the default, and 42 years away from it, so a top-level answer that came from the
        // wrong reckoning is a different number rather than the same one by luck.
        _ussher = new Chronology
        {
            Slug = "ussher",
            Name = "Ussher",
            LastYearBeforeTheCommonEra = UssherZeroPoint,
            IsDefault = false,
            Position = 2,
        };

        _db.Chronologies.AddRange(_bibleData, _ussher);
        _db.SaveChanges();
    }

    public void Dispose()
    {
        _transaction.Rollback();
        _transaction.Dispose();
        _db.Dispose();
    }

    /// <summary>
    /// The case that found it. The source's own column says 980 where its own year from creation
    /// says 984, and the response used to print both.
    /// </summary>
    [Fact]
    public async Task TheTopLevelYearIsTheReckoningsAndNotTheSourcesLooseColumn()
    {
        Dated("davidreturnedtojerusalem", yearFromCreation: 2978, statedBceYear: 980, ussherYear: 2981);

        var response = await Response("davidreturnedtojerusalem");

        response.BceYear.Should().Be(984);
        response.Era.Should().Be("BCE");
        response.Dates.Single(d => d.Chronology == "bibledata").BceYear.Should().Be(984);
        response.Dates.Single(d => d.Chronology == "ussher").BceYear.Should().Be(1023);
    }

    /// <summary>
    /// Two events sat in the corpus with a year every reckoning could turn into a BCE date and an
    /// empty column at the top, because the top was the column.
    /// </summary>
    [Fact]
    public async Task AnEventTheSourceNeverDatedStillGetsTheReckoningsAnswer()
    {
        Dated("beginzephaniah3prophesying", yearFromCreation: 3321, statedBceYear: null);

        var response = await Response("beginzephaniah3prophesying");

        response.BceYear.Should().Be(641);
        response.Era.Should().Be("BCE");
    }

    /// <summary>
    /// Past the turn the two places used to speak different vocabularies, so a client switching on
    /// <c>era</c> saw three values for two eras.
    /// </summary>
    [Fact]
    public async Task PastTheTurnBothPlacesSayTheCommonEra()
    {
        Dated("jubilee70", yearFromCreation: 6069, statedBceYear: 2108);

        var response = await Response("jubilee70");

        response.Era.Should().Be("CE");
        response.BceYear.Should().Be(2108);
        response.Dates.Single(d => d.Chronology == "bibledata").Era.Should().Be("CE");
    }

    /// <summary>
    /// The turn itself, which is the one year the comparison can get wrong by one in either
    /// direction: 3,961 is 1 BCE and 3,962 is 1 CE, and there is no year zero between them.
    /// </summary>
    [Theory]
    [InlineData(BibleDataZeroPoint, 1, "BCE")]
    [InlineData(BibleDataZeroPoint + 1, 1, "CE")]
    public async Task TheTurnHasNoYearZero(int yearFromCreation, int expected, string era)
    {
        Dated("turn", yearFromCreation, statedBceYear: null);

        var response = await Response("turn");

        response.BceYear.Should().Be(expected);
        response.Era.Should().Be(era);
    }

    private async Task<EventResponse> Response(string slug) =>
        EncyclopediaEndpoints.Event(
            await _db.Events.Where(e => e.Slug == slug).Select(EncyclopediaEndpoints.Rows).SingleAsync());

    private void Dated(string slug, int yearFromCreation, int? statedBceYear, int? ussherYear = null)
    {
        var happened = new Event
        {
            Slug = slug,
            Name = slug,
            Source = "test",
            YearFromCreation = yearFromCreation,
            BceYear = statedBceYear,
        };
        _db.Events.Add(happened);
        _db.SaveChanges();

        _db.EventDates.Add(new EventDate
        {
            EventId = happened.Id,
            ChronologyId = _bibleData.Id,
            Year = yearFromCreation,
        });

        if (ussherYear is { } year)
        {
            _db.EventDates.Add(new EventDate
            {
                EventId = happened.Id,
                ChronologyId = _ussher.Id,
                Year = year,
            });
        }

        _db.SaveChanges();
    }
}
