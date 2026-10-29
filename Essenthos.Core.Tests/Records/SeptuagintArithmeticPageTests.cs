using System.Net;
using System.Text.Json;
using Essenthos.Core.Corpus;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Endpoints;
using Essenthos.Core.Loading.Encyclopedia;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Essenthos.Core.Tests.Records;

/// <summary>
/// The arithmetic this corpus works out itself reaches the page in its parts, with the events named in
/// the reader's language where a rendering is held, so the page says it in that language rather than
/// showing our English sentence.
/// </summary>
[Collection(WitnessDatabaseCollection.Name)]
public sealed class SeptuagintArithmeticPageTests : IDisposable
{
    private const string Sentence = "Birth of Peleg is Birth of Eber (2,661) + 134 (Genesis 11:16) = 2,795";

    private readonly WitnessDatabase _database;
    private readonly AppDbContext _db;

    public SeptuagintArithmeticPageTests(WitnessDatabase database)
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
    public async Task TheArithmeticIsServedInItsPartsWithTheEventsNamedInTheReadersLanguage()
    {
        var chronology = new Chronology { Slug = "septuagint", Name = "Septuagint (Brenton)", Position = 4, LastYearBeforeTheCommonEra = 5000 };
        var peleg = new Event
        {
            Slug = "birthofpeleg", Name = "Birth of Peleg", Kind = "Birth", Realm = Realms.Scripture, Source = "a test",
            CanonicalBook = 1, CanonicalChapter = 11, CanonicalVerse = 16,
        };
        _db.AddRange(chronology, peleg);
        await _db.SaveChangesAsync();
        _db.EventDates.Add(new EventDate
        {
            EventId = peleg.Id,
            ChronologyId = chronology.Id,
            Year = 2795,
            Calculation = Sentence,
            CalculationParts = Arithmetic.Write(new Arithmetic(Arithmetic.Counted, 2795)
            {
                Event = "Birth of Peleg", From = "Birth of Eber", FromYear = 2661, Terms = [new ArithmeticTerm(1, 134, 1, 11, 16)],
            }),
        });
        await _db.SaveChangesAsync();
        await new TextTranslationLoader(_db, NullLogger<TextTranslationLoader>.Instance).Load(
            new Dictionary<(string Language, string Sha), (string Text, string Source)>
            {
                [("ukr", EnglishNotes.Digest("Birth of Peleg"))] = ("Народження Пелеґа", "a test rendering"),
                [("ukr", EnglishNotes.Digest("Birth of Eber"))] = ("Народження Евера", "a test rendering"),
            },
            1,
            default);

        await using var app = await Host();
        using var client = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };

        var ukrainian = (await Get(client, "/v1/events/birthofpeleg?language=ukr")).GetProperty("dates").EnumerateArray().Single();
        ukrainian.GetProperty("calculation").GetString().Should().Be(Sentence);
        ukrainian.GetProperty("localCalculation").ValueKind.Should().Be(JsonValueKind.Null, "nothing machine-translates our own sentence");
        var working = ukrainian.GetProperty("working");
        working.GetProperty("kind").GetString().Should().Be(Arithmetic.Counted);
        working.GetProperty("event").GetString().Should().Be("Народження Пелеґа");
        working.GetProperty("from").GetString().Should().Be("Народження Евера");
        working.GetProperty("fromYear").GetInt32().Should().Be(2661);
        working.GetProperty("year").GetInt32().Should().Be(2795);
        var term = working.GetProperty("terms").EnumerateArray().Single();
        (term.GetProperty("sign").GetInt32(), term.GetProperty("value").GetInt32(), term.GetProperty("book").GetInt32(),
                term.GetProperty("chapter").GetInt32(), term.GetProperty("verse").GetInt32())
            .Should().Be((1, 134, 1, 11, 16));

        var english = (await Get(client, "/v1/events/birthofpeleg?language=eng")).GetProperty("dates").EnumerateArray().Single();
        english.GetProperty("working").GetProperty("event").GetString().Should().Be("Birth of Peleg");
        english.TryGetProperty("parts", out _).Should().BeFalse("the stored JSON is read into the parts, not served beside them");
    }

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
        _db.Database.ExecuteSqlRaw("DELETE FROM text_translation");
    }
}
