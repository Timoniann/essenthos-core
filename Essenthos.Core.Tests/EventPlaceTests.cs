using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Endpoints;
using FluentAssertions;
using Microsoft.EntityFrameworkCore.Storage;
using Xunit;

namespace Essenthos.Core.Tests;

/// <summary>
/// An event's location, resolved to the place the encyclopedia holds.
///
/// The source writes where an event happened as free text — <em>Gerar</em>, <em>West of Eden</em>
/// — and the encyclopedia holds places as records with pages of their own, so the one screen that
/// names a place was the one screen that could not open it. The words stay as the source wrote
/// them; the slug is the corpus's own answer, and only where there is one.
/// </summary>
[Collection(WitnessDatabaseCollection.Name)]
public sealed class EventPlaceTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly IDbContextTransaction _transaction;

    public EventPlaceTests(WitnessDatabase database)
    {
        _db = database.NewContext();
        _transaction = _db.Database.BeginTransaction();
    }

    public void Dispose()
    {
        _transaction.Rollback();
        _transaction.Dispose();
        _db.Dispose();
    }

    [Fact]
    public async Task ALocationNamingOnePlaceResolvesToIt()
    {
        Place("gerar", "Gerar");
        await _db.SaveChangesAsync();

        var places = await EncyclopediaEndpoints.PlacesNamed(_db, ["gerar"], default);

        places.Should().ContainKey("gerar").WhoseValue.Should().Be("gerar");
    }

    /// <summary>
    /// Two places of one name and the corpus cannot say which the source meant, so it says
    /// nothing: a link that picks one of two Samarias is worse than a name that links nowhere.
    /// </summary>
    [Fact]
    public async Task ALocationTwoPlacesAnswerToResolvesToNeither()
    {
        Place("samaria", "Samaria");
        Place("samaria-region", "Samaria");
        await _db.SaveChangesAsync();

        var places = await EncyclopediaEndpoints.PlacesNamed(_db, ["Samaria"], default);

        places.Should().BeEmpty();
    }

    /// <summary>
    /// The source's own words are not a place name until they are one. <em>West of Eden</em> names
    /// somewhere near a place this corpus holds, and near is not it.
    /// </summary>
    [Fact]
    public async Task WordsAroundAPlaceNameAreNotThatPlace()
    {
        Place("eden", "Eden");
        await _db.SaveChangesAsync();

        var places = await EncyclopediaEndpoints.PlacesNamed(_db, ["West of Eden"], default);

        places.Should().BeEmpty();
    }

    [Fact]
    public async Task APersonOfTheSameNameIsNotAPlace()
    {
        _db.Entities.Add(new Entity
        {
            Kind = EntityKind.Person,
            Slug = "canaan",
            Name = "Canaan",
            SourceId = "test:canaan",
            Source = "test",
        });
        await _db.SaveChangesAsync();

        var places = await EncyclopediaEndpoints.PlacesNamed(_db, ["Canaan"], default);

        places.Should().BeEmpty();
    }

    private void Place(string slug, string name) =>
        _db.Entities.Add(new Entity
        {
            Kind = EntityKind.Place,
            Slug = slug,
            Name = name,
            SourceId = $"test:{slug}",
            Source = "test",
        });
}
