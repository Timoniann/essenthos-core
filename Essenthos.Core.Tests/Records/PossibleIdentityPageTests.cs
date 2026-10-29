using System.Net;
using System.Text.Json;
using Essenthos.Core.Corpus;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Database.Entities.Enums;
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
/// A possible identity on both pages (RUL-0201) reads right on each: the record the question is about is
/// unsettled, and the record it may be is not, since who that one is is not in doubt (PRB-1349). The
/// reason is given in the reader's language where it was rendered, and in English where it was not
/// (PRB-1347).
/// </summary>
[Collection(WitnessDatabaseCollection.Name)]
public sealed class PossibleIdentityPageTests : IDisposable
{
    private const string Forward = "Matthew 27:56 names a Mary, mother of James and Joses, who may be his mother.";
    private const string Back = "The Mary of Matthew 27:56, mother of James and Joses, may be she.";
    private const string Rendered = "Марія з Матвія 27:56, мати Якова й Йосипа, може бути нею.";

    private readonly WitnessDatabase _database;
    private readonly AppDbContext _db;

    public PossibleIdentityPageTests(WitnessDatabase database)
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
    public async Task TheRecordTheQuestionIsAboutIsUnsettledAndTheOneItMayBeIsNot()
    {
        var (mary, other) = Pair();
        var back = EnglishNotes.Digest(Back);
        await new TextTranslationLoader(_db, NullLogger<TextTranslationLoader>.Instance).Load(
            new Dictionary<(string Language, string Sha), (string Text, string Source)>
            {
                [("ukr", back)] = (Rendered, "a test rendering"),
                [("deu", back)] = ("Die Maria", "a test rendering"),
                [("spa", back)] = ("La María", "a test rendering"),
            },
            1,
            default);

        await using var app = await Host();
        using var client = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };

        var asked = await Get(client, $"/v1/entities/{other.Slug}?language=ukr");
        asked.GetProperty("unsettled").GetBoolean().Should().BeTrue("who the Mary of Matthew 27:56 is, is the question");
        var forward = asked.GetProperty("alternatives").EnumerateArray().Single();
        forward.GetProperty("back").GetBoolean().Should().BeFalse();
        forward.GetProperty("localReason").ValueKind.Should().Be(JsonValueKind.Null, "nothing renders this reason");
        forward.GetProperty("reason").GetString().Should().Be(Forward);

        var answer = await Get(client, $"/v1/entities/{mary.Slug}?language=ukr");
        answer.GetProperty("unsettled").GetBoolean().Should().BeFalse("who the mother of Jesus is, is not in doubt");
        var answered = answer.GetProperty("alternatives").EnumerateArray().Single();
        answered.GetProperty("back").GetBoolean().Should().BeTrue();
        answered.GetProperty("slug").GetString().Should().Be(other.Slug);
        answered.GetProperty("reason").GetString().Should().Be(Back);
        answered.GetProperty("localReason").GetString().Should().Be(Rendered);

        (await Get(client, $"/v1/entities/{mary.Slug}?language=eng")).GetProperty("alternatives").EnumerateArray()
            .Single().GetProperty("localReason").ValueKind.Should().Be(JsonValueKind.Null, "the English is the reason");
        (await Get(client, $"/v1/entities/{mary.Slug}?language=ukr&prose=deu")).GetProperty("alternatives")
            .EnumerateArray().Single().GetProperty("localReason").GetString().Should().Be("Die Maria",
                "the reason is prose, in the interface's language");

        await _db.EntityAlternatives.Where(a => a.Reason == Back)
            .ExecuteUpdateAsync(set => set.SetProperty(a => a.Reason, "A reason said otherwise since."));
        (await Get(client, $"/v1/entities/{mary.Slug}?language=ukr")).GetProperty("alternatives").EnumerateArray()
            .Single().GetProperty("localReason").ValueKind.Should().Be(JsonValueKind.Null,
                "a rendering of the old words is not a rendering of the new");
    }

    /// <summary>
    /// Every reason the files give an alternative or its back is rendered in all three languages, and the
    /// file renders nothing no file says any more.
    /// </summary>
    [Fact]
    public void EveryShippedReasonIsRenderedInEveryLanguage()
    {
        var said = SenseReadingFiles.AllRulings()
            .SelectMany(file => file.Rulings)
            .SelectMany(ruling => ruling.Alternatives ?? [])
            .Concat(SenseReadingFiles.Titles().Titles.SelectMany(title => title.Alternatives ?? []))
            .SelectMany(alternative => new[] { alternative.Reason, alternative.Back })
            .OfType<string>()
            .ToHashSet(StringComparer.Ordinal);
        var file = Reasons();
        file.Source.Should().Contain("machine translation");

        var rendered = file.Entries.ToDictionary(entry => entry.English, StringComparer.Ordinal);
        said.Where(reason => !rendered.ContainsKey(reason)).Should().BeEmpty();
        rendered.Keys.Where(english => !said.Contains(english)).Should().BeEmpty();
        file.Entries.Where(entry => entry.Rendered().Count() != 3).Select(entry => entry.English).Should().BeEmpty();
    }

    /// <summary>The shipped renderings of the alternatives' reasons, one file among the records' translations.</summary>
    internal static TextTranslationLoader.TextTranslationFile Reasons()
    {
        var path = TestResources.Shipped(Path.Combine("Essenthos", TextTranslationLoader.Folder, "alternative-reasons.json"));
        return TextTranslationLoader.Read(File.ReadAllText(path), path);
    }

    private (Entity Mary, Entity Other) Pair()
    {
        var mary = Record("mary");
        var other = Record("mary-7");
        _db.EntityAlternatives.Add(new EntityAlternative
        {
            EntityId = other.Id, AlternativeEntityId = mary.Id, Reason = Forward, Source = "Essenthos, a test",
        });
        _db.EntityAlternatives.Add(new EntityAlternative
        {
            EntityId = mary.Id, AlternativeEntityId = other.Id, Reason = Back, Source = "Essenthos, a test", Back = true,
        });
        _db.SaveChanges();
        return (mary, other);
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

    private Entity Record(string slug)
    {
        var entity = new Entity
        {
            Kind = EntityKind.Person, Slug = slug, Name = "Mary", SourceId = slug, Source = "a test", Sex = "female",
        };
        _db.Entities.Add(entity);
        _db.SaveChanges();
        return entity;
    }

    private void Clear()
    {
        _db.Database.ExecuteSqlRaw("DELETE FROM entity");
        _db.Database.ExecuteSqlRaw("DELETE FROM text_translation");
    }
}
