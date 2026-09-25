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

        var places = await EncyclopediaEndpoints.PlacesNamed(_db, [At("gerar")], default);

        places.Should().ContainKey(At("gerar")!.Value).WhoseValue.Should().Be("gerar");
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

        var places = await EncyclopediaEndpoints.PlacesNamed(_db, [At("Samaria")], default);

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

        var places = await EncyclopediaEndpoints.PlacesNamed(_db, [At("West of Eden")], default);

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

        var places = await EncyclopediaEndpoints.PlacesNamed(_db, [At("Canaan")], default);

        places.Should().BeEmpty();
    }

    /// <summary>
    /// Two Samarias, and the source names the one a reign begins in at 2 Kings 13:1. The corpus
    /// records one of them at that verse, and that one is the answer.
    /// </summary>
    [Fact]
    public async Task TheVerseTheSourceNamesAPlaceAtTellsItsNamesakesApart()
    {
        var city = Place("samaria", "Samaria");
        Place("samaria-region", "Samaria");
        await _db.SaveChangesAsync();
        Recorded(city, Kings2, 13, 1);
        await _db.SaveChangesAsync();

        var reign = EventLocation.Of("Samaria", Kings2, 13, 1);
        var elsewhere = EventLocation.Of("Samaria", Kings2, 17, 24);
        var places = await EncyclopediaEndpoints.PlacesNamed(_db, [reign, elsewhere], default);

        places.Should().ContainKey(reign!.Value).WhoseValue.Should().Be("samaria");
        places.Should().NotContainKey(elsewhere!.Value);
    }

    /// <summary>
    /// A verse at which both namesakes are recorded settles nothing, and nothing is linked.
    /// </summary>
    [Fact]
    public async Task AVerseBothNamesakesAreRecordedAtSettlesNothing()
    {
        Recorded(Place("jericho", "Jericho"), Kings2, 2, 4);
        Recorded(Place("jericho-2", "Jericho"), Kings2, 2, 4);
        await _db.SaveChangesAsync();

        var places = await EncyclopediaEndpoints.PlacesNamed(_db, [EventLocation.Of("Jericho", Kings2, 2, 4)], default);

        places.Should().BeEmpty();
    }

    /// <summary>
    /// The only place of a name stays the answer when the source names it at a verse the corpus
    /// does not record it at — the verse tells namesakes apart and is not a second test to pass.
    /// </summary>
    [Fact]
    public async Task TheOnlyPlaceOfANameIsStillItWhereverTheSourceNamesIt()
    {
        Place("hebron", "Hebron");
        await _db.SaveChangesAsync();

        var at = EventLocation.Of("Hebron", 1, 37, 14);
        var places = await EncyclopediaEndpoints.PlacesNamed(_db, [at], default);

        places.Should().ContainKey(at!.Value).WhoseValue.Should().Be("hebron");
    }

    private const int Kings2 = 12;

    private static EventLocation? At(string location) => EventLocation.Of(location, null, null, null);

    private void Recorded(Entity place, int book, int chapter, int verse) =>
        _db.EntityVerses.Add(new EntityVerse
        {
            Entity = place,
            CanonicalBook = book,
            CanonicalChapter = chapter,
            CanonicalVerse = verse,
            Source = "test",
        });

    private Entity Place(string slug, string name)
    {
        var place = new Entity
        {
            Kind = EntityKind.Place,
            Slug = slug,
            Name = name,
            SourceId = $"test:{slug}",
            Source = "test",
        };
        _db.Entities.Add(place);
        return place;
    }
}
