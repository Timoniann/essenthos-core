using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Endpoints;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Xunit;

namespace Essenthos.Core.Tests;

/// <summary>
/// A period's span, per reckoning, read without the whole chronology.
///
/// A period states no years of its own: it is anchored to two events, and what those events are
/// dated to is each reckoning's business. That reading stands — the span is still computed from
/// the two events — but until now the only answer carrying it was the timeline, so a reader
/// opening one period downloaded every event in the corpus to see when it ran.
/// </summary>
[Collection(WitnessDatabaseCollection.Name)]
public sealed class PeriodSpanTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly IDbContextTransaction _transaction;

    public PeriodSpanTests(WitnessDatabase database)
    {
        _db = database.NewContext();
        _transaction = _db.Database.BeginTransaction();
    }

    public void Dispose()
    {
        _transaction.Rollback();
        _transaction.Dispose();
        _db.Dispose();
    }

    [Fact]
    public async Task APeriodRunsFromItsOpeningEventToItsClosingOneInEveryReckoningThatStatesBoth()
    {
        var counted = Reckoning("bibledata", 1);
        var ussher = Reckoning("ussher", 2);
        var opens = Happening("beginflood", "The Flood began");
        var closes = Happening("endflood", "The Flood ended");
        await _db.SaveChangesAsync();

        Dated(opens, counted, 1656);
        Dated(closes, counted, 1657);
        Dated(opens, ussher, 1655);
        _db.Periods.Add(new Period
        {
            Slug = "the-flood",
            Name = "The Flood",
            Level = 1,
            StartEvent = opens,
            EndEvent = closes,
            Source = "test",
        });
        await _db.SaveChangesAsync();

        var years = await EncyclopediaEndpoints.EventYears(_db, _db.EventDates, default);
        var period = (await EncyclopediaEndpoints.Periods(_db, years, default)).Should().ContainSingle().Subject;

        period.Years.Should().ContainKey("bibledata").WhoseValue.Should().Equal(1656, 1657);

        // Ussher dates the opening and not the close, and a band running from his year to somebody
        // else's is a duration nobody computed.
        period.Years.Should().NotContainKey("ussher");
    }

    private Chronology Reckoning(string slug, int position)
    {
        var chronology = new Chronology
        {
            Slug = slug,
            Name = slug,
            LastYearBeforeTheCommonEra = 3961,
            Position = position,
        };
        _db.Chronologies.Add(chronology);
        return chronology;
    }

    private Event Happening(string slug, string name)
    {
        var happening = new Event { Slug = slug, Name = name, Source = "test" };
        _db.Events.Add(happening);
        return happening;
    }

    private void Dated(Event happening, Chronology chronology, int year) =>
        _db.EventDates.Add(new EventDate { Event = happening, Chronology = chronology, Year = year });
}
