using System.Net;
using System.Text.Json;
using Essenthos.Core.Corpus;
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
/// What a record's page says of the people in its relationship rows: who each one is, in the line
/// their own page leads with, and where two rows cite one verse, the order the King James names
/// them in there.
///
/// <para>
/// Asked of the endpoint over Postgres, because the line is read for the page's people together
/// with the record's own, and the order is read from the King James's words while another text
/// names the same people in the other order.
/// </para>
/// </summary>
[Collection(WitnessDatabaseCollection.Name)]
public sealed class RelationshipRowLineTests : IDisposable
{
    private readonly WitnessDatabase _database;
    private readonly AppDbContext _db;

    public RelationshipRowLineTests(WitnessDatabase database)
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
    public async Task ARowSaysWhoItsPersonIsAndTwoRowsOfOneVerseStandInTheKingJamesOrder()
    {
        var kjv = Corpus.Add(_db, "KJV", TextKind.Translation, "eng", [(2, 3, ["Simon", "and", "Andrew"])]);
        var other = Corpus.Add(_db, "WEB", TextKind.Translation, "eng", [(2, 3, ["Andrew", "and", "Simon"])]);
        _db.SaveChanges();
        var teacher = Record("teacher", EntityKind.Person);
        var simon = Record("simon", EntityKind.Person);
        var andrew = Record("andrew", EntityKind.Person);
        var town = Record("town", EntityKind.Place);
        Names(kjv, simon, 1);
        Names(kjv, andrew, 3);
        Names(other, andrew, 1);
        Names(other, simon, 3);
        Tie(simon, teacher, "disciple-of", 2, 3);
        Tie(andrew, teacher, "disciple-of", 2, 3);
        Tie(teacher, town, "from-place", 2, 4);
        _db.EntityDescriptors.Add(new EntityDescriptor
        {
            EntityId = andrew.Id, TargetEntityId = simon.Id, Relation = DescriptorRelations.BrotherOf, Ordinal = 1,
            CanonicalBook = 1, CanonicalChapter = 2, CanonicalVerse = 3, Source = "a test",
        });
        _db.SaveChanges();

        await using var app = await Host();
        using var client = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };
        var rows = (await Get(client, "/v1/entities/teacher?language=eng")).GetProperty("relationships")
            .EnumerateArray().ToDictionary(row => row.GetProperty("slug").GetString()!);

        rows["simon"].GetProperty("position").GetInt32().Should().Be(1);
        rows["andrew"].GetProperty("position").GetInt32().Should().Be(3, "the King James names Andrew third");
        rows["town"].GetProperty("position").ValueKind.Should().Be(JsonValueKind.Null, "no other row cites its verse");

        var own = (await Get(client, "/v1/entities/andrew?language=eng")).GetProperty("descriptor")
            .GetProperty("parts").EnumerateArray().Select(part => part.GetProperty("text").GetString());
        rows["andrew"].GetProperty("line").GetString().Should().Be(string.Concat(own)).And.Contain("Simon");
        rows["simon"].GetProperty("line").ValueKind.Should().Be(JsonValueKind.Null, "nothing describes him");
        rows["town"].GetProperty("line").ValueKind.Should().Be(JsonValueKind.Null, "a place's line is not read here");
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

    private Entity Record(string slug, EntityKind kind)
    {
        var entity = new Entity
        {
            Kind = kind, Slug = slug, Name = char.ToUpperInvariant(slug[0]) + slug[1..], SourceId = slug, Source = "a test",
        };
        _db.Entities.Add(entity);
        _db.SaveChanges();
        return entity;
    }

    private void Names(Text text, Entity entity, int position)
    {
        _db.WordEntities.Add(new WordEntity
        {
            WordId = _db.WordAt(text, 2, 3, position).Id,
            EntityId = entity.Id,
            Method = LinkMethod.StatedBySource,
            Source = "a test",
            Claims = [new WordEntityClaim { Method = LinkMethod.StatedBySource, Source = "a test" }],
        });
        _db.SaveChanges();
    }

    private void Tie(Entity from, Entity to, string relation, int chapter, int verse)
    {
        _db.EntityRelationships.Add(new EntityRelationship
        {
            FromEntityId = from.Id,
            ToEntityId = to.Id,
            Type = relation,
            Category = RelationshipCategories.Read,
            CanonicalBook = 1,
            CanonicalChapter = chapter,
            CanonicalVerse = verse,
            Method = LinkMethod.Manual,
            Source = "a test",
        });
        _db.SaveChanges();
    }

    private void Clear()
    {
        _db.Database.ExecuteSqlRaw("DELETE FROM text");
        _db.Database.ExecuteSqlRaw("DELETE FROM entity");
    }
}
