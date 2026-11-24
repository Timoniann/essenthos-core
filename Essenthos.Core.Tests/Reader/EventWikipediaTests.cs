using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Endpoints;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Xunit;

namespace Essenthos.Core.Tests.Reader;

/// <summary>
/// A world event links to the article its Wikidata item has in the reader's language, and to the English one
/// where their Wikipedia has none; a Scripture event links to none.
/// </summary>
[Collection(WitnessDatabaseCollection.Name)]
public sealed class EventWikipediaTests : IDisposable
{
    private static readonly SiteSettingsFile Settings = new(
        Path.Combine(Path.GetTempPath(), $"no-settings-{Guid.NewGuid():N}.json"),
        Microsoft.Extensions.Logging.Abstractions.NullLogger<SiteSettingsFile>.Instance);

    private readonly AppDbContext _db;
    private readonly IDbContextTransaction _transaction;

    public EventWikipediaTests(WitnessDatabase database)
    {
        _db = database.NewContext();
        _transaction = _db.Database.BeginTransaction();
        var boyne = new Event { Slug = "brunaboinne", Name = "Brú na Bóinne", Realm = Realms.World, Source = "Wikidata, query.wikidata.org, CC0" };
        var flood = new Event { Slug = "beginflood", Name = "The Flood began", Source = "BibleData" };
        _db.Events.AddRange(boyne, flood);
        _db.SaveChanges();
        _db.EventWikipedia.AddRange(
            new EventWikipedia { EventId = boyne.Id, Language = "en", Title = "Brú na Bóinne", Qid = "Q339489" },
            new EventWikipedia { EventId = boyne.Id, Language = "uk", Title = "Бру-на-Бойн", Qid = "Q339489" });
        _db.SaveChanges();
    }

    public void Dispose()
    {
        _transaction.Rollback();
        _transaction.Dispose();
        _db.Dispose();
    }

    [Theory]
    [InlineData("ukr", "uk", "Бру-на-Бойн", "https://uk.wikipedia.org/wiki/%D0%91%D1%80%D1%83-%D0%BD%D0%B0-%D0%91%D0%BE%D0%B9%D0%BD")]
    [InlineData("eng", "en", "Brú na Bóinne", "https://en.wikipedia.org/wiki/Br%C3%BA_na_B%C3%B3inne")]
    [InlineData("deu", "en", "Brú na Bóinne", "https://en.wikipedia.org/wiki/Br%C3%BA_na_B%C3%B3inne")]
    public async Task AWorldEventLinksToTheArticleOfTheReadersLanguageOrTheEnglishOne(
        string language, string wikipedia, string title, string url)
    {
        var shown = await EncyclopediaEndpoints.EventDetail(_db, Settings, "brunaboinne", language, language, default);

        shown!.Wikipedia.Should().Be(new EntityWikipediaResponse(wikipedia, title, url));
    }

    [Fact]
    public async Task AScriptureEventLinksToNoArticle()
    {
        var shown = await EncyclopediaEndpoints.EventDetail(_db, Settings, "beginflood", "ukr", "ukr", default);

        shown!.Wikipedia.Should().BeNull();
    }

    [Fact]
    public async Task ARemovedEventTakesItsArticlesWithIt()
    {
        await _db.Events.Where(e => e.Slug == "brunaboinne").ExecuteDeleteAsync();

        (await _db.EventWikipedia.CountAsync(w => w.Qid == "Q339489")).Should().Be(0);
    }
}
