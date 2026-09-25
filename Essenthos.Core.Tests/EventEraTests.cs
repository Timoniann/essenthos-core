using Essenthos.Core.Corpus;
﻿using Essenthos.Core.Database;
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

    /// <summary>
    /// Whether a source put this event anywhere inside its year travels to the client, because the
    /// list it arrives in is ordered either way and nothing else in the payload says which of the
    /// two orders it is. A reader who cannot tell reads an arbitrary order as a chronology.
    /// </summary>
    [Theory]
    [InlineData(6298, true)]
    [InlineData(null, false)]
    public async Task WhetherASourceStatedWhereItFallsInsideItsYearReachesTheClient(
        int? sequenceInYear, bool sequenced)
    {
        Dated("placed", yearFromCreation: 3991, statedBceYear: null, sequenceInYear: sequenceInYear);

        (await Response("placed")).Sequenced.Should().Be(sequenced);
    }

    /// <summary>
    /// The other half of ordering the events that share a year, and the one the corpus cannot ask
    /// for itself: an event nobody put anywhere inside its year is drawn after every event somebody
    /// did, rather than wherever the database happens to sort a null.
    ///
    /// Built here rather than read off the Annals because the busiest year in the corpus holds
    /// nothing but Annals and every Annal is sequenced, so the mixed year this is about does not
    /// exist yet — it appears the moment a second dated source lands in one of Ussher's years.
    /// </summary>
    [Fact]
    public async Task WhatNoSourceOrderedIsDrawnAfterWhatOneDid()
    {
        const int oneYear = 3991;

        // The pair a text sort puts backwards — ordering-1000 precedes ordering-999 — which is what
        // the slug tiebreak would do and no loaded row exercises, every paragraph number being four
        // digits wide.
        Dated("ordering-none", oneYear, statedBceYear: null);
        Dated("ordering-1000", oneYear, statedBceYear: null, sequenceInYear: 1000);
        Dated("ordering-999", oneYear, statedBceYear: null, sequenceInYear: 999);

        var drawn = await EncyclopediaEndpoints
            .InOrder(_db.Events.Where(e => e.Slug.StartsWith("ordering-")))
            .Select(e => e.Slug)
            .ToListAsync();

        drawn.Should().Equal("ordering-999", "ordering-1000", "ordering-none");
    }

    /// <summary>
    /// Ussher's creation, which is his year 1 and — in his words — 4004 BC, where the subtraction his
    /// zero makes of year 1 is 4003. The year he printed is the answer, and the payload says it is
    /// his rather than worked out.
    /// </summary>
    [Fact]
    public async Task AYearTheReckoningPrintedIsServedAsPrintedAndSaysSo()
    {
        Dated("creation", yearFromCreation: 1, statedBceYear: null, ussherYear: 1, ussherStated: -4004);

        var ussher = (await Response("creation")).Dates.Single(d => d.Chronology == "ussher");

        ussher.BceYear.Should().Be(4004);
        ussher.Era.Should().Be("BCE");
        ussher.Stated.Should().BeTrue();
        (await Response("creation")).Dates.Single(d => d.Chronology == "bibledata").Stated.Should().BeFalse();
    }

    /// <summary>
    /// A date that states only its year of the common era — no year from creation at all — is still
    /// a date, on the page and on the axis.
    /// </summary>
    [Fact]
    public async Task AStatedYearStandsWithoutAYearFromCreation()
    {
        Dated("passion", yearFromCreation: BibleDataZeroPoint + 33, statedBceYear: null, ussherYear: null,
            ussherStated: 33);

        var ussher = (await Response("passion")).Dates.Single(d => d.Chronology == "ussher");
        ussher.Year.Should().BeNull();
        ussher.BceYear.Should().Be(33);
        ussher.Era.Should().Be("CE");

        var years = await EncyclopediaEndpoints.EventYears(
            _db, _db.EventDates.Where(d => d.Event!.Slug == "passion"), default);
        years.Values.Single()["ussher"].Should().Be(UssherZeroPoint + 33);
    }

    /// <summary>
    /// The axis reads a year back into an era by the chronology's zero, so a printed 4004 BC has to
    /// sit where that reading gives 4004 BC and not where the year from creation would put it.
    /// </summary>
    [Theory]
    [InlineData(1, -4004, 0)]
    [InlineData(2513, -1491, 2513)]
    [InlineData(4036, 33, 4036)]
    [InlineData(2513, null, 2513)]
    public void AStatedYearPlacesTheEventOnItsReckoningsAxis(int? year, int? stated, int placed) =>
        EncyclopediaEndpoints.Placed(year, stated, UssherZeroPoint).Should().Be(placed);

    private async Task<EventResponse> Response(string slug)
    {
        var row = await _db.Events.Where(e => e.Slug == slug).Select(EncyclopediaEndpoints.Rows).SingleAsync();
        var places = await EncyclopediaEndpoints.PlacesNamed(_db, [EventLocation.Of(row.Event)], default);
        return EncyclopediaEndpoints.Event(row, places);
    }

    private void Dated(
        string slug,
        int yearFromCreation,
        int? statedBceYear,
        int? ussherYear = null,
        int? sequenceInYear = null,
        int? ussherStated = null)
    {
        var happened = new Event
        {
            Slug = slug,
            Name = slug,
            Source = "test",
            YearFromCreation = yearFromCreation,
            BceYear = statedBceYear,
            SequenceInYear = sequenceInYear,
        };
        _db.Events.Add(happened);
        _db.SaveChanges();

        _db.EventDates.Add(new EventDate
        {
            EventId = happened.Id,
            ChronologyId = _bibleData.Id,
            Year = yearFromCreation,
        });

        if (ussherYear is not null || ussherStated is not null)
        {
            _db.EventDates.Add(new EventDate
            {
                EventId = happened.Id,
                ChronologyId = _ussher.Id,
                Year = ussherYear,
                StatedYear = ussherStated,
            });
        }

        _db.SaveChanges();
    }
}
