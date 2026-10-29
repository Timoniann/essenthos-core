using System.Net;
using System.Text.Json;
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
        await new AlternativeReasonTranslationLoader(_db, NullLogger<AlternativeReasonTranslationLoader>.Instance)
            .Load(new AlternativeReasonTranslations("a test rendering", [new(Back, Rendered, "Die Maria", "La María")]), default);

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
        var back = answer.GetProperty("alternatives").EnumerateArray().Single();
        back.GetProperty("back").GetBoolean().Should().BeTrue();
        back.GetProperty("slug").GetString().Should().Be(other.Slug);
        back.GetProperty("reason").GetString().Should().Be(Back);
        back.GetProperty("localReason").GetString().Should().Be(Rendered);

        (await Get(client, $"/v1/entities/{mary.Slug}?language=eng")).GetProperty("alternatives").EnumerateArray()
            .Single().GetProperty("localReason").ValueKind.Should().Be(JsonValueKind.Null, "the English is the reason");
        (await Get(client, $"/v1/entities/{mary.Slug}?language=ukr&prose=deu")).GetProperty("alternatives")
            .EnumerateArray().Single().GetProperty("localReason").GetString().Should().Be("Die Maria",
                "the reason is prose, in the interface's language");
    }

    [Fact]
    public async Task AReasonIsRenderedOnceAndARewrittenOneLosesItsRendering()
    {
        Pair();
        var file = new AlternativeReasonTranslations("a test rendering", [new(Back, Rendered, "Die Maria", "La María")]);

        var first = await Loader().Load(file, default);
        first.Written.Should().Be(1);
        first.Unrendered.Should().Be(1, "the forward reason has no entry");
        var row = await _db.EntityAlternatives.AsNoTracking().SingleAsync(a => a.Reason == Back);
        ReasonRenderings.Read(row.ReasonRenderings).Should().BeEquivalentTo(
            new Dictionary<string, string> { ["ukr"] = Rendered, ["deu"] = "Die Maria", ["spa"] = "La María" });
        row.ReasonRenderedBy.Should().Be("a test rendering");

        var again = await Loader().Load(file, default);
        again.Written.Should().Be(0);
        again.Cleared.Should().Be(0);

        await _db.EntityAlternatives.Where(a => a.Id == row.Id)
            .ExecuteUpdateAsync(set => set.SetProperty(a => a.Reason, "A reason said otherwise since."));
        _db.ChangeTracker.Clear();
        var rewritten = await Loader().Load(file, default);
        rewritten.Cleared.Should().Be(1);
        var cleared = await _db.EntityAlternatives.AsNoTracking().SingleAsync(a => a.Id == row.Id);
        cleared.ReasonRenderings.Should().BeNull();
        cleared.ReasonRenderedBy.Should().BeNull();
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
        var file = AlternativeReasonTranslationLoader.Read();
        file.Source.Should().Contain("machine translation");

        var rendered = file.Entries.ToDictionary(entry => entry.English, StringComparer.Ordinal);
        said.Where(reason => !rendered.ContainsKey(reason)).Should().BeEmpty();
        rendered.Keys.Where(english => !said.Contains(english)).Should().BeEmpty();
        file.Entries.Where(entry => entry.Languages().Count != 3).Select(entry => entry.English).Should().BeEmpty();
    }

    private AlternativeReasonTranslationLoader Loader() =>
        new(_db, NullLogger<AlternativeReasonTranslationLoader>.Instance);

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

    private void Clear() => _db.Database.ExecuteSqlRaw("DELETE FROM entity");
}
