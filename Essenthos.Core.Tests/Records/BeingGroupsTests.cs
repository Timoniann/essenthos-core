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

namespace Essenthos.Core.Tests.Records;

/// <summary>
/// The index lists the gods and the creatures apart: a subtype narrows the beings to one sort, and the letters count the
/// same records the list shows.
/// </summary>
[Collection(WitnessDatabaseCollection.Name)]
public sealed class BeingGroupsTests : IDisposable
{
    private readonly WitnessDatabase _database;
    private readonly AppDbContext _db;

    public BeingGroupsTests(WitnessDatabase database)
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
    public async Task ASubtypeNarrowsTheBeingsToOneSort()
    {
        Record("dagon", "Dagon", "god");
        Record("baal", "Baal", "god");
        Record("leviathan", "Leviathan", "creature");
        Record("behemoth", "Behemoth", "creature");
        Record("ark", "Ark", "vessel", EntityKind.Object);

        await using var app = await Host();
        using var client = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };

        Slugs(await Get(client, "/v1/entities?kind=being")).Should().BeEquivalentTo("dagon", "baal", "leviathan", "behemoth");
        Slugs(await Get(client, "/v1/entities?kind=being&subtype=god")).Should().BeEquivalentTo("dagon", "baal");
        var creatures = await Get(client, "/v1/entities?kind=being&subtype=creature");
        creatures.GetProperty("total").GetInt32().Should().Be(2);
        Slugs(creatures).Should().BeEquivalentTo("leviathan", "behemoth");

        var letters = await Get(client, "/v1/entities/letters?kind=being&subtype=creature");
        letters.GetProperty("total").GetInt32().Should().Be(2);
        letters.GetProperty("letters").EnumerateArray()
            .Where(l => l.GetProperty("count").GetInt32() > 0)
            .Select(l => l.GetProperty("letter").GetString())
            .Should().Equal("B", "L");
    }

    private static IEnumerable<string?> Slugs(JsonElement list) =>
        list.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("slug").GetString());

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

    private void Record(string slug, string name, string subtype, EntityKind kind = EntityKind.Being)
    {
        var entity = new Entity { Kind = kind, Slug = slug, Name = name, Subtype = subtype };
        _db.Entities.Add(entity);
        _db.SaveChanges();
        _db.EntityVerses.Add(new EntityVerse
        {
            EntityId = entity.Id, CanonicalBook = 23, CanonicalChapter = 27, CanonicalVerse = 1, Source = "test",
        });
        _db.SaveChanges();
    }

    private void Clear() => _db.Database.ExecuteSqlRaw("DELETE FROM entity");
}
