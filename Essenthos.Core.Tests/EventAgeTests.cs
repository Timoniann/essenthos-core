using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Endpoints;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Xunit;

namespace Essenthos.Core.Tests;

/// <summary>
/// A person's age at an event, given beside each reckoning's year and counted in that reckoning.
///
/// The source states one age per event, and it is BibleData's: Abram leaves Ur at 70 in its year
/// 2080 and Haran at 75 in 2085. Ussher puts both departures in his year 2083, so a page that showed
/// Ussher's years beside BibleData's ages had Abram 70 and 75 in one year. These are Abram's events
/// as the corpus holds them.
/// </summary>
[Collection(WitnessDatabaseCollection.Name)]
public sealed class EventAgeTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly IDbContextTransaction _transaction;
    private readonly Chronology _bibleData;
    private readonly Chronology _ussher;
    private readonly Entity _abram;
    private readonly Entity _isaac;

    public EventAgeTests(WitnessDatabase database)
    {
        _db = database.NewContext();
        _transaction = _db.Database.BeginTransaction();

        _ussher = new Chronology { Slug = "ussher", Name = "Ussher", LastYearBeforeTheCommonEra = 4003, IsDefault = true, Position = 1 };
        _bibleData = new Chronology { Slug = "bibledata", Name = "BibleData", LastYearBeforeTheCommonEra = 3961, Position = 2 };
        _db.Chronologies.AddRange(_bibleData, _ussher);
        _abram = Person("abram");
        _isaac = Person("isaac");
        _db.SaveChanges();

        Happened(_abram, "birthabram1", "Birth", age: 0, bibleData: 2010, ussher: 2008);
        Happened(_abram, "abramdepartedur", "Travel", age: 70, bibleData: 2080, ussher: 2083);
        Happened(_abram, "abrammetmelchizedek", "Meeting", age: 74, bibleData: 2084, ussher: 2092);
        Happened(_abram, "abramdepartedharan", "Travel", age: 75, bibleData: 2085, ussher: 2083);
        Happened(_abram, "promisetoabram1", "Covenant", age: 75, bibleData: 2085, ussher: 2092);
        Happened(_abram, "abrammarriedhagar", "Marriage", age: 85, bibleData: 2095, ussher: 2093);
        Happened(_abram, "abrahamcircumcised", "Unique", age: 99, bibleData: 2109, ussher: 2107);
        Happened(_abram, "deathabram1", "Death", age: 175, bibleData: 2185, ussher: 2183);

        // Ussher dates Isaac's sacrifice but not his birth, so no age can be counted in his years.
        Happened(_isaac, "birthisaac1", "Birth", age: 0, bibleData: 2110, ussher: null);
        Happened(_isaac, "isaacoffered", "Unique", age: 25, bibleData: 2135, ussher: 2141);
        _db.SaveChanges();
    }

    public void Dispose()
    {
        _transaction.Rollback();
        _transaction.Dispose();
        _db.Dispose();
    }

    [Fact]
    public async Task EachReckoningsAgeIsCountedFromItsOwnYearOfTheBirth()
    {
        var events = await Events(_abram);

        Age(events, "abramdepartedur", "ussher").Should().Be(75);
        Age(events, "abramdepartedharan", "ussher").Should().Be(75);
        Age(events, "abrammetmelchizedek", "ussher").Should().Be(84);
        Age(events, "abramdepartedur", "bibledata").Should().Be(70);
        Age(events, "abramdepartedharan", "bibledata").Should().Be(75);
    }

    [Theory]
    [InlineData("ussher")]
    [InlineData("bibledata")]
    public async Task OnePersonsEventsAreInTheSameOrderByYearAndByAge(string reckoning)
    {
        var dated = (await Events(_abram))
            .Select(e => e.Dates.Single(d => d.Chronology == reckoning))
            .ToList();

        dated.Should().OnlyContain(d => d.AgeAtEvent != null);
        dated.OrderBy(d => d.Year).ThenBy(d => d.AgeAtEvent).Select(d => d.AgeAtEvent)
            .Should().BeInAscendingOrder();
        dated.Should().OnlyContain(d => d.Year - d.AgeAtEvent == dated.Single(b => b.AgeAtEvent == 0).Year);
    }

    [Fact]
    public async Task WithoutTheReckoningsYearOfTheBirthItGivesNoAge()
    {
        var offered = (await Events(_isaac)).Single(e => e.Slug == "isaacoffered");

        offered.Dates.Single(d => d.Chronology == "ussher").AgeAtEvent.Should().BeNull();
        offered.Dates.Single(d => d.Chronology == "bibledata").AgeAtEvent.Should().Be(25);
    }

    private static int? Age(IEnumerable<EventResponse> events, string slug, string reckoning) =>
        events.Single(e => e.Slug == slug).Dates.Single(d => d.Chronology == reckoning).AgeAtEvent;

    private async Task<List<EventResponse>> Events(Entity person)
    {
        var rows = await EncyclopediaEndpoints.InOrder(_db.Events.Where(e => e.EntityId == person.Id))
            .Select(EncyclopediaEndpoints.Rows)
            .ToListAsync();
        var aged = await EncyclopediaEndpoints.Aged(_db, rows, default);
        return [.. aged.Select(row => EncyclopediaEndpoints.Event(row, new Dictionary<EventLocation, string>()))];
    }

    private Entity Person(string slug)
    {
        var person = new Entity { Kind = EntityKind.Person, Slug = slug, Name = slug, SourceId = slug, Source = "a test" };
        _db.Entities.Add(person);
        return person;
    }

    private void Happened(Entity person, string slug, string kind, int age, int bibleData, int? ussher)
    {
        var happened = new Event
        {
            Slug = slug, Name = slug, Kind = kind, Source = "test", EntityId = person.Id,
            YearFromCreation = bibleData, AgeAtEvent = age,
        };
        _db.Events.Add(happened);
        _db.SaveChanges();
        _db.EventDates.Add(new EventDate { EventId = happened.Id, ChronologyId = _bibleData.Id, Year = bibleData });
        if (ussher is not null)
        {
            _db.EventDates.Add(new EventDate { EventId = happened.Id, ChronologyId = _ussher.Id, Year = ussher });
        }
    }
}
