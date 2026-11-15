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
/// A tribe's page knows everyone of the tribe, and the people list narrows to them: a person whose record names the
/// tribe's patriarch, and a person a clause of ours says is of the tribe though the record names none. A people that
/// is not a tribe of Israel lists its members the same way, and is not offered among the tribes.
/// </summary>
[Collection(WitnessDatabaseCollection.Name)]
public sealed class TribeMembersTests : IDisposable
{
    private readonly WitnessDatabase _database;
    private readonly AppDbContext _db;

    public TribeMembersTests(WitnessDatabase database)
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
    public async Task ATribesPageCountsEveryoneOfItAndTheListNarrowsToThem()
    {
        var levi = Record("levi", "Levi");
        Record("levi-2", "Levi");
        var levites = Record("levites", "Levites", EntityKind.People, origin: levi);
        Clause(levites, "descendants-of", levi);
        var aaron = Record("aaron", "Aaron", tribe: "Levi");
        var barnabas = Record("barnabas", "Barnabas");
        Clause(barnabas, "of-tribe", levites);
        var judah = Record("judah", "Judah");
        var judahites = Record("judahites", "Judahites", EntityKind.People, origin: judah);
        Clause(judahites, "descendants-of", judah);
        Clause(Record("caleb", "Caleb"), "of-tribe", judahites);
        Record("uzziah", "Uzziah", tribe: "Judah");
        Record("edomites", "Edomites", EntityKind.People);
        Record("doeg", "Doeg", tribe: "Edomite");

        await using var app = await Host();
        using var client = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };

        var page = await Get(client, "/v1/entities/levites");
        page.GetProperty("members").GetProperty("count").GetInt32().Should().Be(2);
        page.GetProperty("members").GetProperty("tribe").GetBoolean().Should().BeTrue();

        var listed = await Get(client, "/v1/entities?tribe=levites&kind=person");
        listed.GetProperty("total").GetInt32().Should().Be(2, "Aaron's record names Levi and a clause says Barnabas is a Levite");
        listed.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("slug").GetString())
            .Should().BeEquivalentTo(aaron.Slug, barnabas.Slug);

        var edomites = await Get(client, "/v1/entities/edomites");
        edomites.GetProperty("members").GetProperty("count").GetInt32().Should().Be(1, "Doeg's record calls him an Edomite");
        edomites.GetProperty("members").GetProperty("tribe").GetBoolean().Should().BeFalse();
        (await Get(client, "/v1/entities/judahites")).GetProperty("members").GetProperty("count").GetInt32().Should().Be(2);

        var tribes = await Get(client, "/v1/entities/tribes");
        tribes.GetProperty("items").EnumerateArray()
            .Select(t => (t.GetProperty("slug").GetString(), t.GetProperty("members").GetInt32()))
            .Should().BeEquivalentTo([("judahites", 2), ("levites", 2)], "Edom is no tribe of Israel");
        tribes.GetProperty("items").EnumerateArray().Select(t => t.GetProperty("patriarch").GetProperty("slug").GetString())
            .Should().Equal("judah", "levi");

        (await Get(client, "/v1/entities/aaron")).GetProperty("tribeRecord").GetProperty("slug").GetString().Should().Be("levi");
        (await client.GetAsync("/v1/entities?tribe=nobody")).StatusCode.Should().Be(HttpStatusCode.NotFound);
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

    private Entity Record(string slug, string name, EntityKind kind = EntityKind.Person, string? tribe = null, Entity? origin = null)
    {
        var entity = new Entity { Kind = kind, Slug = slug, Name = name, Tribe = tribe, OriginEntityId = origin?.Id };
        _db.Entities.Add(entity);
        _db.SaveChanges();
        return entity;
    }

    private void Clause(Entity subject, string relation, Entity target)
    {
        _db.EntityDescriptors.Add(new EntityDescriptor
        {
            EntityId = subject.Id,
            TargetEntityId = target.Id,
            Relation = relation,
            Ordinal = _db.EntityDescriptors.Count(d => d.EntityId == subject.Id) + 1,
            CanonicalBook = 1,
            CanonicalChapter = 1,
            CanonicalVerse = 1,
            Method = LinkMethod.Manual,
            Source = "a test",
        });
        _db.SaveChanges();
    }

    private void Clear() => _db.Database.ExecuteSqlRaw("DELETE FROM entity");
}
