using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Endpoints;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Xunit;

namespace Essenthos.Core.Tests.Records;

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

    [Fact]
    public async Task APeriodTheRecordsCannotNameTakesTheRenderingHeldOfItsEnglishName()
    {
        var opens = Happening("beginsiege", "The siege began");
        var closes = Happening("endsiege", "The siege ended");
        var era = new Period { Slug = "era-test", Name = "The test era", Kind = "era", Level = 0, Source = "test" };
        _db.Periods.AddRange(
            era,
            new Period
            {
                Slug = "world-trojanwar",
                Name = "Trojan War",
                Kind = "span",
                Level = 1,
                Parent = era,
                StartEvent = opens,
                EndEvent = closes,
                Source = "test",
            });
        _db.TextTranslations.Add(new TextTranslation
        {
            Language = "ukr",
            EnglishSha256 = Essenthos.Core.Corpus.EnglishNotes.Digest("Trojan War"),
            Text = "Троянська війна",
            Source = "a test rendering",
        });
        await _db.SaveChangesAsync();

        var page = await EncyclopediaEndpoints.PeriodDetail(_db, "world-trojanwar", default, "ukr");
        page!.LocalName.Should().Be("Троянська війна");
        page.Parent!.LocalName.Should().BeNull("nothing renders the era's name");

        var outer = await EncyclopediaEndpoints.PeriodDetail(_db, "era-test", default, "ukr");
        outer!.Inside.Should().ContainSingle().Which.LocalName.Should().Be("Троянська війна");

        (await EncyclopediaEndpoints.PeriodDetail(_db, "world-trojanwar", default, "eng"))!.LocalName
            .Should().BeNull("the English is the name");
        (await EncyclopediaEndpoints.PeriodDetail(_db, "world-trojanwar", default, "deu"))!.LocalName
            .Should().BeNull("no German rendering is held");

        var named = await PeriodNames.Of(_db, [("world-trojanwar", "Trojan War", "span", null)], "ukr", default);
        named.Should().ContainKey("world-trojanwar").WhoseValue.Should().Be("Троянська війна");
    }

    [Fact]
    public async Task AnEventCarriesThePeriodsItsYearFallsInAndTheCreditsOfItsSource()
    {
        var counted = Reckoning("bibledata", 1);
        var opens = Happening("beginflood", "The Flood began");
        var closes = Happening("endflood", "The Flood ended");
        var before = Happening("birthnoah", "Noah was born");
        var after = Happening("deathnoah", "Noah died");
        var sacrifice = new Event { Slug = "noahsacrifice", Name = "Noah built an altar", Source = "BibleData by Brady Stephenson, github.com/BradyStephenson/bible-data, CC BY 4.0", YearFromCreation = 1657 };
        _db.Events.Add(sacrifice);
        await _db.SaveChangesAsync();

        Dated(opens, counted, 1656);
        Dated(closes, counted, 1657);
        Dated(before, counted, 1056);
        Dated(after, counted, 1100);
        _db.Periods.AddRange(
            new Period { Slug = "the-flood", Name = "The Flood", Level = 1, StartEvent = opens, EndEvent = closes, Source = "test" },
            new Period { Slug = "an-earlier-span", Name = "An earlier span", Level = 1, StartEvent = before, EndEvent = after, Source = "test" });
        await _db.SaveChangesAsync();

        var settings = new SiteSettingsFile(
            Path.Combine(Path.GetTempPath(), $"no-settings-{Guid.NewGuid():N}.json"),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<SiteSettingsFile>.Instance);
        var shown = await EncyclopediaEndpoints.EventDetail(_db, settings, "noahsacrifice", "eng", "eng", default);

        shown!.Periods!.Select(p => p.Slug).Should().Equal("the-flood");
        shown.Credits!.Select(c => c.Id).Should().Equal("bibledata");
        shown.Credits![0].Licence.Should().Be("CC BY 4.0");
    }

    [Fact]
    public async Task APeriodTitlesItsBoundsInTheReadersLanguageAndCarriesTheCreditOfItsSource()
    {
        var opens = Happening("endconquest", "Israel's conquest of the Land of Canaan ended");
        var closes = Happening("beginsaul", "Saul's reign as king began*");
        _db.Periods.Add(new Period
        {
            Slug = "era-judges",
            Name = "The judges",
            Kind = "era",
            Level = 0,
            StartEvent = opens,
            EndEvent = closes,
            Source = "BibleData by Brady Stephenson, github.com/BradyStephenson/bible-data, CC BY 4.0",
        });
        _db.TextTranslations.AddRange(
            new TextTranslation
            {
                Language = "ukr",
                EnglishSha256 = Essenthos.Core.Corpus.EnglishNotes.Digest("Israel's conquest of the Land of Canaan ended"),
                Text = "Завершення завоювання Ізраїлем землі Ханаанської",
                Source = "a test rendering",
            },
            new TextTranslation
            {
                Language = "ukr",
                EnglishSha256 = Essenthos.Core.Corpus.EnglishNotes.Digest("Saul's reign as king began*"),
                Text = "Початок царювання Саула*",
                Source = "a test rendering",
            });
        await _db.SaveChangesAsync();
        var settings = new SiteSettingsFile(
            Path.Combine(Path.GetTempPath(), $"no-settings-{Guid.NewGuid():N}.json"),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<SiteSettingsFile>.Instance);

        // The names come from the chosen text, the titles from the interface: a reader of the Synodal on a Ukrainian page.
        var page = await EncyclopediaEndpoints.PeriodDetail(_db, "era-judges", default, "rus", "ukr", settings);

        page!.Opens!.LocalName.Should().Be("Завершення завоювання Ізраїлем землі Ханаанської");
        page.Closes!.Name.Should().Be("Saul's reign as king began");
        page.Closes.LocalName.Should().Be("Початок царювання Саула");
        page.Credits!.Select(c => c.Id).Should().Equal("bibledata");

        var english = await EncyclopediaEndpoints.PeriodDetail(_db, "era-judges", default, "eng", "eng", settings);
        english!.Opens!.LocalName.Should().BeNull("the English is the title");
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
