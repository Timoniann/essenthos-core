using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using Essenthos.Core.Build;
using Essenthos.Core.Context;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Descriptions;
using Essenthos.Core.Endpoints;
using Essenthos.Core.Products;
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
/// A row of the record list carries the line under its name as the build stored it for the reader's pair of languages
/// (<c>context_line</c>), which is the line the descriptors compose live, cut to what a row draws; a pair the build did not
/// store, or a record it stored no line for, is composed live. The cards of records named by slug come in the order asked,
/// with the slugs no record has.
/// </summary>
[Collection(WitnessDatabaseCollection.Name)]
public sealed class EntityCardTests : IDisposable
{
    private readonly WitnessDatabase _database;
    private readonly AppDbContext _db;
    private readonly string _root = Path.Combine(Path.GetTempPath(), "essenthos-products", Guid.NewGuid().ToString("N"));

    public EntityCardTests(WitnessDatabase database)
    {
        _database = database;
        _db = database.NewContext();
        Directory.CreateDirectory(_root);
        Clear();
    }

    public void Dispose()
    {
        Clear();
        _db.Dispose();
        Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public async Task AListRowsLineIsTheStoredOneAndTheComposedOne()
    {
        var jacob = Record("jacob-c", "Jacob", "male");
        var joseph = Record("joseph-c", "Joseph", "male");
        var other = Record("joseph-d", "Joseph", "male");
        var mary = Record("mary-c", "Mary", "female");
        var jesus = Record("jesus-c", "Jesus", "male");
        Record("lonely-c", "Lonely", "male");
        Says(joseph, "son-of", jacob, 1);
        Says(other, "brother-of", jesus, 1);
        Says(jesus, "son-of", mary, 1);
        Says(jesus, "son-of", joseph, 2);
        Says(jesus, "brother-of", other, 3);
        Says(mary, "wife-of", joseph, 1);
        Form(joseph, "ukr", GrammaticalCases.Nominative, "Йосип");
        Form(joseph, "ukr", GrammaticalCases.Genitive, "Йосипа");
        Form(mary, "ukr", GrammaticalCases.Genitive, "Марії");
        _db.EntityDistinguishers.Add(new EntityDistinguisher
        {
            EntityId = jacob.Id, Language = "ukr", Text = "батько дванадцятьох", English = "father of twelve", Source = "a test",
        });
        jacob.Distinguisher = "father of twelve";
        _db.SaveChanges();
        await Build();

        await using var app = await Host();
        using var client = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };
        foreach (var (names, prose) in new[] { ("eng", "eng"), ("ukr", "ukr"), ("eng", "ukr"), ("hbo", "ukr") })
        {
            var list = await Get(client, $"/v1/entities?take=100&language={names}&prose={prose}");
            var items = list.GetProperty("items").EnumerateArray().ToList();
            items.Should().HaveCount(6);
            var composed = ContextDescriptors.Slim(
                await Descriptors.Of(_db, items.Select(i => i.GetProperty("slug").GetString()!), prose, default, names));
            foreach (var item in items)
            {
                var slug = item.GetProperty("slug").GetString()!;
                var because = $"{slug} in names {names}, words {prose}";
                item.GetProperty("descriptor").ValueKind.Should().Be(JsonValueKind.Null, because);
                if (composed.Lines.TryGetValue(slug, out var line))
                {
                    Same(item.GetProperty("line"), line).Should().BeTrue(because);
                    item.TryGetProperty("firstVerse", out var first).Should().BeTrue();
                    first.ValueKind.Should().Be(JsonValueKind.Null, because);
                }
                else
                {
                    item.TryGetProperty("line", out _).Should().BeFalse(because);
                }
            }

            list.GetProperty("records").EnumerateObject().Select(r => r.Name)
                .Should().BeEquivalentTo(composed.Records.Keys, $"names {names}, words {prose}");
            var jacobRow = items.Single(i => i.GetProperty("slug").GetString() == jacob.Slug);
            jacobRow.GetProperty("localDistinguisher").ValueKind
                .Should().Be(prose == "ukr" ? JsonValueKind.String : JsonValueKind.Null);
            items.Single(i => i.GetProperty("slug").GetString() == "lonely-c").GetProperty("firstVerse").GetProperty("chapter").GetInt32()
                .Should().Be(1, "a record with no line of any kind is told apart by the first verse that names it");
        }

        (await _db.ContextLines.CountAsync(l => l.Names == "hbo")).Should().Be(0, "the build holds no names in that language");

        // A line that says a target otherwise than the first row pointing at it (a title in the plural) keeps it with it.
        var stored = await _db.ContextLines.SingleAsync(l => l.Slug == mary.Slug && l.Names == "eng" && l.Prose == "eng");
        var body = ContextLines.Read(stored.Body);
        body.Records![joseph.Slug] = body.Records[joseph.Slug] with { Name = "Josephs" };
        await _db.ContextLines.Where(l => l.Slug == mary.Slug && l.Names == "eng" && l.Prose == "eng")
            .ExecuteUpdateAsync(s => s.SetProperty(l => l.Body, ContextLines.Write(body)));
        var said = await Get(client, "/v1/entities?take=100&language=eng&prose=eng");
        said.GetProperty("records").GetProperty(joseph.Slug).GetProperty("name").GetString().Should().Be("Joseph");
        var rows = said.GetProperty("items").EnumerateArray().ToDictionary(i => i.GetProperty("slug").GetString()!);
        rows[mary.Slug].GetProperty("records").GetProperty(joseph.Slug).GetProperty("name").GetString().Should().Be("Josephs");
        rows[mary.Slug].GetProperty("records").EnumerateObject().Should().ContainSingle();
        rows[jesus.Slug].TryGetProperty("records", out _).Should().BeFalse();

        // A record the build stored no line for is composed: the stored lines are what the rows carry otherwise.
        await _db.ContextLines.Where(l => l.Slug == jesus.Slug).ExecuteDeleteAsync();
        var row = (await Get(client, "/v1/entities?take=100&language=ukr&prose=ukr")).GetProperty("items").EnumerateArray()
            .Single(i => i.GetProperty("slug").GetString() == jesus.Slug);
        var live = ContextDescriptors.Slim(await Descriptors.Of(_db, [jesus.Slug], "ukr", default, "ukr")).Lines[jesus.Slug];
        Same(row.GetProperty("line"), live).Should().BeTrue();
        await _db.ContextLines.Where(l => l.Slug == mary.Slug && l.Names == "ukr" && l.Prose == "ukr")
            .ExecuteUpdateAsync(s => s.SetProperty(l => l.Body, ContextLines.Write(ContextLineBody.Empty)));
        (await Get(client, "/v1/entities?take=100&language=ukr&prose=ukr")).GetProperty("items").EnumerateArray()
            .Single(i => i.GetProperty("slug").GetString() == mary.Slug).TryGetProperty("line", out _)
            .Should().BeFalse("the row carries the stored line, not one composed beside it");
    }

    [Fact]
    public async Task TheCardsOfRecordsNamedComeInTheOrderAskedWithTheSlugsNoRecordHas()
    {
        var jacob = Record("jacob-c", "Jacob", "male");
        var joseph = Record("joseph-c", "Joseph", "male");
        Says(joseph, "son-of", jacob, 1);
        Form(joseph, "ukr", GrammaticalCases.Nominative, "Йосип");
        _db.SaveChanges();
        await Build();

        await using var app = await Host();
        using var client = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };
        var cards = await Get(client, "/v1/entities?slugs=joseph-c,nobody,jacob-c,joseph-c&language=ukr&prose=ukr");
        cards.GetProperty("total").GetInt32().Should().Be(2);
        cards.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("slug").GetString()).Should().Equal("joseph-c", "jacob-c");
        cards.GetProperty("unknown").EnumerateArray().Select(u => u.GetString()).Should().Equal("nobody");
        cards.GetProperty("items")[0].GetProperty("localName").GetString().Should().Be("Йосип");

        var listed = (await Get(client, "/v1/entities?take=100&language=ukr&prose=ukr")).GetProperty("items").EnumerateArray()
            .Single(i => i.GetProperty("slug").GetString() == "joseph-c");
        cards.GetProperty("items")[0].GetRawText().Should().Be(listed.GetRawText(), "a card is the row the list shows");
        cards.GetProperty("records").GetRawText().Should().Contain("\"jacob-c\"");
        (await Get(client, "/v1/entities?slugs=jacob-c")).TryGetProperty("unknown", out _).Should().BeFalse();

        foreach (var refused in new[]
                 {
                     "/v1/entities?slugs=jacob-c&take=5", "/v1/entities?slugs=jacob-c&kind=person", "/v1/entities?slugs=jacob-c&q=ja",
                     "/v1/entities?slugs=", "/v1/entities?slugs=" + string.Join(',', Enumerable.Range(0, 101).Select(n => $"r{n}")),
                 })
        {
            (await client.GetAsync(refused)).StatusCode.Should().Be(HttpStatusCode.BadRequest, refused);
        }
    }

    private async Task Build()
    {
        var services = new ServiceCollection();
        services.AddScoped(_ => _database.NewContext());
        await using var provider = services.BuildServiceProvider(validateScopes: true);
        await using var connection = _database.NewConnection();
        await new ContextBuild(connection.ConnectionString, new ProductStore(_root), provider, NullLogger.Instance).Run(false, default);
    }

    private async Task<WebApplication> Host()
    {
        var settings = Path.Combine(Path.GetTempPath(), $"essenthos-settings-{Guid.NewGuid():N}.json");
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddScoped(_ => _database.NewContext());
        builder.Services.AddSingleton(new SiteSettingsFile(settings, NullLogger<SiteSettingsFile>.Instance));
        builder.Services.AddPageServices();
        builder.Services.Configure<JsonOptions>(options =>
            options.SerializerOptions.TypeInfoResolverChain.Insert(0, AppJsonSerializerContext.Default));
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        var app = builder.Build();
        app.MapGroup("/v1").MapEncyclopedia();
        await app.StartAsync();
        return app;
    }

    private static bool Same(JsonElement sent, ContextDescriptorResponse line) =>
        JsonNode.DeepEquals(
            JsonNode.Parse(sent.GetRawText()),
            JsonNode.Parse(JsonSerializer.Serialize(line, AppJsonSerializerContext.Default.ContextDescriptorResponse)));

    private static async Task<JsonElement> Get(HttpClient client, string path)
    {
        var response = await client.GetAsync(path);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
    }

    private Entity Record(string slug, string name, string sex)
    {
        var entity = new Entity { Kind = EntityKind.Person, Slug = slug, Name = name, Sex = sex };
        _db.Entities.Add(entity);
        _db.SaveChanges();
        _db.EntityVerses.Add(new EntityVerse { EntityId = entity.Id, CanonicalBook = 40, CanonicalChapter = 1, CanonicalVerse = 2, Source = "test" });
        _db.SaveChanges();
        return entity;
    }

    private void Form(Entity entity, string language, string grammaticalCase, string form) =>
        _db.EntityNameForms.Add(new EntityNameForm
        {
            EntityId = entity.Id, Language = language, GrammaticalCase = grammaticalCase, Form = form,
            Method = LinkMethod.ModelReading, Confidence = 1, Source = "a test",
        });

    private void Says(Entity subject, string relation, Entity target, int ordinal)
    {
        _db.EntityDescriptors.Add(new EntityDescriptor
        {
            EntityId = subject.Id,
            TargetEntityId = target.Id,
            Relation = relation,
            Ordinal = ordinal,
            CanonicalBook = 40,
            CanonicalChapter = 1,
            CanonicalVerse = 2,
            Method = LinkMethod.Manual,
            Source = "a test",
        });
        _db.SaveChanges();
    }

    private void Clear()
    {
        _db.Database.ExecuteSqlRaw("DELETE FROM context_line");
        _db.Database.ExecuteSqlRaw("DELETE FROM context_chapter");
        _db.Database.ExecuteSqlRaw("DELETE FROM entity");
    }
}
