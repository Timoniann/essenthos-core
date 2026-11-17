using System.Net;
using System.Text.Json;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Endpoints;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Essenthos.Core.Tests.Reader;

/// <summary>
/// The events list in a reader's order of reckonings: each event shown, and placed, by the first
/// reckoning in the order that dates it; kinds left out on request; a step that walks the same list;
/// and no age on a birth.
/// </summary>
[Collection(WitnessDatabaseCollection.Name)]
public sealed class EventListOrderTests : IDisposable
{
    private readonly WitnessDatabase _database;
    private readonly AppDbContext _db;

    public EventListOrderTests(WitnessDatabase database)
    {
        _database = database;
        _db = database.NewContext();
        Clear();
    }

    public void Dispose()
    {
        Clear();
        _db.Dispose();
    }

    [Fact]
    public async Task EachEventIsPlacedByTheFirstReckoningInTheReadersOrderThatDatesIt()
    {
        await Seed();
        await using var app = await Host();
        using var client = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };

        // Ussher prints 1000 BC for the temple and dates nothing else; BibleData dates the flood to
        // 1500 BCE (year 2461 on its zero of 3961) and the temple to 900 BCE.
        var ussherFirst = await Get(client, "/v1/events?reckonings=ussher,bibledata&take=10");
        Slugs(ussherFirst).Should().Equal("flood", "abram-born", "abram-dies", "temple");
        ShownBy(ussherFirst).Should().Equal("bibledata", "bibledata", "bibledata", "ussher");
        var line = ussherFirst.GetProperty("items")[3];
        line.TryGetProperty("description", out _).Should().BeFalse();
        line.GetProperty("dates")[0].TryGetProperty("calculation", out _).Should().BeFalse();
        line.GetProperty("dates").EnumerateArray().Select(d => d.GetProperty("bceYear").GetInt32()).Should().Equal(1000, 900);
        ussherFirst.GetProperty("centuries").EnumerateArray().Select(c => c.GetProperty("century").GetInt32()).Should().Equal(-16, -14, -12, -10);

        var bibleDataFirst = await Get(client, "/v1/events?reckonings=bibledata,ussher&take=10");
        ShownBy(bibleDataFirst).Should().AllBe("bibledata");

        var noPreference = await Get(client, "/v1/events?take=10");
        noPreference.GetProperty("items").EnumerateArray()
            .Should().OnlyContain(item => item.GetProperty("shownBy").ValueKind == JsonValueKind.Null);
    }

    [Fact]
    public async Task KindsAreLeftOutOrKeptOnRequest()
    {
        await Seed();
        await using var app = await Host();
        using var client = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };

        var withoutLives = await Get(client, "/v1/events?reckonings=ussher,bibledata&notKind=Birth,Death");
        withoutLives.GetProperty("total").GetInt32().Should().Be(2);
        Slugs(withoutLives).Should().Equal("flood", "temple");

        var onlyLives = await Get(client, "/v1/events?kind=Birth,Death");
        Slugs(onlyLives).Should().BeEquivalentTo(["abram-born", "abram-dies"]);
    }

    [Fact]
    public async Task ABirthCarriesNoAgeAndADeathKeepsItsAge()
    {
        await Seed();
        await using var app = await Host();
        using var client = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };

        var born = await Get(client, "/v1/events/abram-born");
        born.GetProperty("ageAtEvent").ValueKind.Should().Be(JsonValueKind.Null);
        born.GetProperty("dates").EnumerateArray()
            .Should().OnlyContain(date => date.GetProperty("ageAtEvent").ValueKind == JsonValueKind.Null);

        var died = await Get(client, "/v1/events/abram-dies");
        died.GetProperty("ageAtEvent").GetInt32().Should().Be(175);
    }

    [Fact]
    public async Task TheStepWalksTheListInTheSameNarrowingAndOrder()
    {
        await Seed();
        await using var app = await Host();
        using var client = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };

        var step = await Get(client, "/v1/events/abram-dies/step?reckonings=ussher,bibledata");
        step.GetProperty("position").GetInt32().Should().Be(3);
        step.GetProperty("total").GetInt32().Should().Be(4);
        step.GetProperty("previous").GetProperty("slug").GetString().Should().Be("abram-born");
        step.GetProperty("next").GetProperty("slug").GetString().Should().Be("temple");
        step.GetProperty("nearSkip").GetInt32().Should().Be(0);
        Slugs(step, "near").Should().Equal("flood", "abram-born", "abram-dies", "temple");
        step.GetProperty("near").EnumerateArray().Last().GetProperty("shownBy").GetString().Should().Be("ussher");

        var three = await Get(client, "/v1/events/abram-dies/step?reckonings=ussher,bibledata&near=3");
        three.GetProperty("nearSkip").GetInt32().Should().Be(1);
        Slugs(three, "near").Should().Equal("abram-born", "abram-dies", "temple");

        var narrowed = await Get(client, "/v1/events/temple/step?reckonings=ussher,bibledata&notKind=Birth,Death");
        narrowed.GetProperty("position").GetInt32().Should().Be(2);
        narrowed.GetProperty("previous").GetProperty("slug").GetString().Should().Be("flood");
        narrowed.GetProperty("next").ValueKind.Should().Be(JsonValueKind.Null);

        var leftOut = await Get(client, "/v1/events/abram-born/step?notKind=Birth");
        leftOut.GetProperty("position").ValueKind.Should().Be(JsonValueKind.Null);
        leftOut.GetProperty("total").GetInt32().Should().Be(3);
        leftOut.GetProperty("near").GetArrayLength().Should().Be(0);

        (await client.GetAsync("/v1/events/nobody/step")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public void SlugsCompareTheirNumbersAsNumbers()
    {
        string[] slugs = ["ussher-1000", "ussher-999", "ussher-10", "ussher-9b", "ussher-9a"];
        slugs.Order(EncyclopediaEndpoints.NumberAwareOrder.Instance)
            .Should().Equal("ussher-9a", "ussher-9b", "ussher-10", "ussher-999", "ussher-1000");
    }

    private async Task Seed()
    {
        var ussher = new Chronology { Slug = "ussher", Name = "Ussher", Position = 1, IsDefault = true, LastYearBeforeTheCommonEra = 4003 };
        var bibleData = new Chronology { Slug = "bibledata", Name = "BibleData", Position = 2, LastYearBeforeTheCommonEra = 3961 };
        var abram = new Entity { Slug = "abram", Name = "Abram", Kind = EntityKind.Person };
        _db.AddRange(ussher, bibleData, abram);
        await _db.SaveChangesAsync();

        Event Made(string slug, string kind, int yearFromCreation, int? age = null) => new()
        {
            Slug = slug, Name = slug, Kind = kind, Realm = Realms.Scripture, Source = "a test",
            YearFromCreation = yearFromCreation, AgeAtEvent = age,
            EntityId = age is null ? null : abram.Id,
        };

        var flood = Made("flood", "Unique", 2461);
        var born = Made("abram-born", "Birth", 2600, 0);
        var dies = Made("abram-dies", "Death", 2775, 175);
        var temple = Made("temple", "Construction", 3062);
        _db.AddRange(flood, born, dies, temple);
        await _db.SaveChangesAsync();

        _db.EventDates.AddRange(
            new EventDate { EventId = flood.Id, ChronologyId = bibleData.Id, Year = 2461 },
            new EventDate { EventId = born.Id, ChronologyId = bibleData.Id, Year = 2600 },
            new EventDate { EventId = dies.Id, ChronologyId = bibleData.Id, Year = 2775 },
            new EventDate { EventId = temple.Id, ChronologyId = bibleData.Id, Year = 3062 },
            new EventDate { EventId = temple.Id, ChronologyId = ussher.Id, Year = 3004, StatedYear = -1000 });
        await _db.SaveChangesAsync();
        await StoredEvents.Order(_db);
    }

    private static IEnumerable<string?> Slugs(JsonElement list, string property = "items") =>
        list.GetProperty(property).EnumerateArray().Select(item => item.GetProperty("slug").GetString());

    private static IEnumerable<string?> ShownBy(JsonElement list) =>
        list.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("shownBy").GetString());

    private async Task<WebApplication> Host()
    {
        var settings = Path.Combine(Path.GetTempPath(), $"essenthos-settings-{Guid.NewGuid():N}.json");
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddScoped(_ => _database.NewContext());
        builder.Services.AddSingleton(new SiteSettingsFile(settings, NullLogger<SiteSettingsFile>.Instance));
        builder.Services.Configure<JsonOptions>(options =>
            options.SerializerOptions.TypeInfoResolverChain.Insert(0, AppJsonSerializerContext.Default));
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        var app = builder.Build();
        app.MapGroup("/v1").MapEncyclopedia();
        await app.StartAsync();
        return app;
    }

    private static async Task<JsonElement> Get(HttpClient client, string path)
    {
        var response = await client.GetAsync(path);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
    }

    private void Clear()
    {
        _db.Database.ExecuteSqlRaw("DELETE FROM event_date");
        _db.Database.ExecuteSqlRaw("DELETE FROM event");
        _db.Database.ExecuteSqlRaw("DELETE FROM chronology");
        _db.Database.ExecuteSqlRaw("DELETE FROM entity WHERE slug = 'abram'");
    }
}
