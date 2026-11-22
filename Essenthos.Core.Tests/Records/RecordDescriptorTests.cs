using System.Net;
using System.Text.Json;
using Essenthos.Core.Build;
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
using Microsoft.Extensions.Options;
using Xunit;

namespace Essenthos.Core.Tests.Records;

/// <summary>
/// The whole descriptors the build stores (<c>record_descriptor</c>) are the ones the descriptors compose, read by the
/// language of words they are phrased in, the language of names and the slug; a pair of languages the build did not make is
/// composed on the request. The record names a reader's texts call every record by are the stored ones.
/// </summary>
[Collection(WitnessDatabaseCollection.Name)]
public sealed class RecordDescriptorTests : IDisposable
{
    private readonly WitnessDatabase _database;
    private readonly AppDbContext _db;
    private readonly string _root = Path.Combine(Path.GetTempPath(), "essenthos-products", Guid.NewGuid().ToString("N"));

    public RecordDescriptorTests(WitnessDatabase database)
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
    public async Task AStoredDescriptorIsTheComposedOneAndAPairNotBuiltIsComposed()
    {
        var (jacob, joseph, _, jesus) = Family();
        await Build();

        var slugs = await _db.Entities.Select(e => e.Slug).ToListAsync();
        (await _db.RecordDescriptors.CountAsync(d => d.Prose == "ukr" && d.Names == "ukr"))
            .Should().Be(slugs.Count, "every record has a row in a pair the build made, with no body where nothing is said");
        (await _db.RecordDescriptors.CountAsync(d => d.Names == "hbo")).Should().Be(0);

        foreach (var (names, prose, built) in new[]
                 {
                     ("eng", "eng", true), ("ukr", "ukr", true), ("ukr", "eng", true), (null, "ukr", true), ("deu", "deu", false), ("hbo", "ukr", false),
                     ("ukr", "fra", true),
                 })
        {
            var because = $"names {names}, words {prose}";
            var composed = await Descriptors.Of(_db, slugs, prose, default, names);
            var (storedProse, storedNames) = RecordDescriptors.Key(prose, names);
            (await _db.RecordDescriptors.AnyAsync(d => d.Prose == storedProse && d.Names == storedNames)).Should().Be(built, because);
            var read = await RecordDescriptors.Of(_db, slugs, prose, default, names);
            read.Keys.Should().BeEquivalentTo(composed.Keys, because);
            foreach (var (slug, descriptor) in composed)
            {
                Json(read[slug]).Should().Be(Json(descriptor), $"{slug} in {because}");
            }

        }

        // What is stored is what is read: a stored body changed is served changed, by the routes that read descriptors.
        var changed = (await RecordDescriptors.Of(_db, jacob.Slug, "eng", default, "eng"))! with
        {
            Parts = [new DescriptorPartResponse("the stored words")],
        };
        await _db.RecordDescriptors.Where(d => d.Slug == jacob.Slug && d.Prose == "eng" && d.Names == "eng")
            .ExecuteUpdateAsync(s => s.SetProperty(d => d.Body, RecordDescriptors.Write(changed)));
        await using var app = await Host();
        using var client = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };
        var page = await Get(client, $"/v1/entities/{joseph.Slug}?language=eng&prose=eng");
        page.GetProperty("relationships").EnumerateArray().Single(r => r.GetProperty("slug").GetString() == jacob.Slug)
            .GetProperty("line").GetString().Should().Be("the stored words");
        (await Get(client, $"/v1/entities/{jacob.Slug}?language=eng&prose=eng"))
            .GetProperty("descriptor").GetProperty("parts")[0].GetProperty("text").GetString().Should().Be("the stored words");
        (await Get(client, $"/v1/entities/{jesus.Slug}?language=eng&prose=eng")).GetProperty("descriptor").ValueKind
            .Should().Be(JsonValueKind.Object);
    }

    [Fact]
    public async Task TheRecordNamesAreTheStoredOnesAndALanguageNotStoredIsComposedAlike()
    {
        var (_, joseph, _, _) = Family();
        await Build();
        foreach (var language in new[] { "eng", "ukr" })
        {
            foreach (var card in await RecordCards.Of(_db, [.. _db.Entities.Select(e => e.Slug)], language, language, default))
            {
                _db.RecordNames.Add(new RecordName
                {
                    Names = language, Slug = card.Slug, Kind = card.Kind, Name = card.Name, LocalName = card.LocalName,
                    Distinguisher = card.Distinguisher, Sex = card.Sex, AlsoCalled = card.AlsoCalled,
                });
            }
        }

        _db.SaveChanges();
        var json = new JsonOptions();
        json.SerializerOptions.TypeInfoResolverChain.Insert(0, AppJsonSerializerContext.Default);
        var scopes = new ServiceCollection().AddScoped(_ => _database.NewContext()).BuildServiceProvider()
            .GetRequiredService<IServiceScopeFactory>();
        var languages = new[] { "eng", "ukr", "UKR", null };
        var stored = new List<(byte[] Body, string Tag)>();
        foreach (var language in languages)
        {
            stored.Add(await new RecordNames(scopes, Options.Create(json)).Of(language, default));
        }

        var names = JsonDocument.Parse(stored[1].Body).RootElement;
        names.GetProperty("names").GetProperty(joseph.Slug).GetString().Should().Be("Йосип");

        await _db.RecordNames.ExecuteDeleteAsync();
        for (var i = 0; i < languages.Length; i++)
        {
            var composed = await new RecordNames(scopes, Options.Create(json)).Of(languages[i], default);
            stored[i].Body.Should().Equal(composed.Body, $"language {languages[i]}");
            stored[i].Tag.Should().Be(composed.Tag);
        }
    }

    private (Entity Jacob, Entity Joseph, Entity Mary, Entity Jesus) Family()
    {
        var jacob = Record("jacob-r", "Jacob", "male");
        var joseph = Record("joseph-r", "Joseph", "male");
        var other = Record("joseph-s", "Joseph", "male");
        var mary = Record("mary-r", "Mary", "female");
        var jesus = Record("jesus-r", "Jesus", "male");
        Record("lonely-r", "Lonely", "male");
        Says(joseph, "son-of", jacob, 1);
        Says(other, "brother-of", jesus, 1);
        Says(jesus, "son-of", mary, 1);
        Says(jesus, "son-of", joseph, 2);
        Says(jesus, "brother-of", other, 3);
        Says(mary, "wife-of", joseph, 1);
        Form(joseph, "ukr", GrammaticalCases.Nominative, "Йосип");
        Form(joseph, "ukr", GrammaticalCases.Genitive, "Йосипа");
        Form(mary, "ukr", GrammaticalCases.Genitive, "Марії");
        _db.EntityRelationships.Add(new EntityRelationship
        {
            FromEntityId = joseph.Id, ToEntityId = jacob.Id, Type = "son-of", Category = RelationshipCategories.Read, Source = "a test",
            CanonicalBook = 40, CanonicalChapter = 1, CanonicalVerse = 2,
        });
        _db.SaveChanges();
        return (jacob, joseph, mary, jesus);
    }

    private static string Json(EntityDescriptorResponse descriptor) =>
        JsonSerializer.Serialize(descriptor, AppJsonSerializerContext.Default.EntityDescriptorResponse);

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
        _db.Database.ExecuteSqlRaw("DELETE FROM record_descriptor");
        _db.Database.ExecuteSqlRaw("DELETE FROM record_name");
        _db.Database.ExecuteSqlRaw("DELETE FROM context_line");
        _db.Database.ExecuteSqlRaw("DELETE FROM context_chapter");
        _db.Database.ExecuteSqlRaw("DELETE FROM entity");
    }
}
