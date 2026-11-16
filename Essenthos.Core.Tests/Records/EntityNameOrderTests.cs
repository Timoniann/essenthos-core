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
/// A record's names come in the order the record file gives them, whatever a load did to the rows
/// since: the first heads the page and the word card. Egypt's מִצְרַיִם was updated after its insert,
/// moved to the end of the table, and the rare Matsor, labelled Egypt too, headed 697 verses' cards
/// (PRB-1766).
/// </summary>
[Collection(WitnessDatabaseCollection.Name)]
public sealed class EntityNameOrderTests : IDisposable
{
    private readonly WitnessDatabase _database;
    private readonly AppDbContext _db;

    public EntityNameOrderTests(WitnessDatabase database)
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
    public async Task TheNamesComeInTheRecordFilesOrderAfterARowIsUpdated()
    {
        var egypt = new Entity { Kind = EntityKind.Place, Slug = "egypt", Name = "Egypt" };
        _db.Entities.Add(egypt);
        _db.SaveChanges();
        _db.EntityNames.Add(new EntityName { EntityId = egypt.Id, Label = "Egypt", Kind = "proper name", HebrewStrongNumber = "H4714" });
        _db.SaveChanges();
        _db.EntityNames.Add(new EntityName { EntityId = egypt.Id, Label = "Egypt", Kind = "name", HebrewStrongNumber = "H4693" });
        _db.EntityNames.Add(new EntityName { EntityId = egypt.Id, Label = "Rahab", Kind = "name", HebrewStrongNumber = "H7294" });
        _db.SaveChanges();
        await _db.EntityNames.Where(n => n.HebrewStrongNumber == "H4714")
            .ExecuteUpdateAsync(set => set.SetProperty(n => n.Meaning, "Mitsrayim = Egypt"));

        await using var app = await Host();
        using var client = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };
        var response = await client.GetAsync("/v1/entities/egypt?language=ukr");
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var names = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("names");

        names.EnumerateArray().Select(n => n.GetProperty("hebrewStrongNumbers")[0].GetString())
            .Should().Equal("H4714", "H4693", "H7294");
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

    private void Clear() => _db.Database.ExecuteSqlRaw("DELETE FROM entity");
}
