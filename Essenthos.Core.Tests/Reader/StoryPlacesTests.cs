using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Endpoints;
using FluentAssertions;
using Microsoft.EntityFrameworkCore.Storage;
using Xunit;

namespace Essenthos.Core.Tests.Reader;

/// <summary>
/// The places of a person's story: every place named in a verse that names the person, counted by
/// the verses the two share, and every place a tie or a dated event puts the person in, each with
/// what it was in the story.
/// </summary>
[Collection(WitnessDatabaseCollection.Name)]
public sealed class StoryPlacesTests : IDisposable
{
    private const int FirstSamuel = 9;
    private const int SecondSamuel = 10;
    private const int FirstKings = 11;

    private readonly AppDbContext _db;
    private readonly IDbContextTransaction _transaction;
    private readonly Entity _david;

    public StoryPlacesTests(WitnessDatabase database)
    {
        _db = database.NewContext();
        _transaction = _db.Database.BeginTransaction();

        _david = Record(EntityKind.Person, "david", "David");
        var jonathan = Record(EntityKind.Person, "jonathan", "Jonathan");
        var bethlehem = Place("bethlehem", "Bethlehem");
        var hebron = Place("hebron", "Hebron");
        var jerusalem = Place("jerusalem", "Jerusalem");
        var gath = Place("gath", "Gath");
        var ziklag = Record(EntityKind.Place, "ziklag", "Ziklag");
        var zoar = Place("zoar", "Zoar");
        _db.SaveChanges();

        Verse(_david, FirstSamuel, 16, 4);
        Verse(bethlehem, FirstSamuel, 16, 4);
        Verse(jonathan, FirstSamuel, 16, 4);

        Verse(_david, SecondSamuel, 5, 5);
        Verse(hebron, SecondSamuel, 5, 5);
        Verse(jerusalem, SecondSamuel, 5, 5);
        Verse(jerusalem, SecondSamuel, 5, 5, "a second source");
        Verse(_david, SecondSamuel, 5, 6);
        Verse(jerusalem, SecondSamuel, 5, 6);
        Verse(_david, FirstKings, 2, 10);
        Verse(jerusalem, FirstKings, 2, 10);

        // A naming the witness states is not one the page shows, so it shares no verse.
        Verse(_david, FirstSamuel, 27, 2);
        Verse(gath, FirstSamuel, 27, 2, "BibleData by Brady Stephenson, a test");

        // A place named where David is not.
        Verse(zoar, 1, 19, 22);

        Tie(_david, hebron, DescriptorRelations.LivedIn);
        Tie(_david, ziklag, DescriptorRelations.LivedIn);
        Tie(_david, jerusalem, DescriptorRelations.BuriedIn);
        Tie(_david, jonathan, "friend-of");

        Happened(_david, "david-born", "Birth", "Bethlehem");
        Happened(_david, "david-to-jerusalem", "Travel", "Jerusalem");
        Happened(_david, "david-dies", "Death", "Jerusalem");
        Happened(_david, "david-nowhere", "Battle", "Nowhere Known");

        _db.EntityNameForms.Add(new EntityNameForm
        {
            Entity = jerusalem, Language = "ukr", GrammaticalCase = GrammaticalCases.Nominative, Form = "Єрусалим",
            Method = LinkMethod.ModelReading, Confidence = 1, Source = "a test",
        });
        _db.SaveChanges();
    }

    public void Dispose()
    {
        _transaction.Rollback();
        _transaction.Dispose();
        _db.Dispose();
    }

    [Fact]
    public async Task EveryPlaceOfTheStoryIsListedTheMostSharedVersesFirst()
    {
        var places = await EncyclopediaEndpoints.StoryPlaces(_db, _david.Id, null, null, 20);

        places.Items.Select(p => (p.Slug, p.SharedVerses)).Should().Equal(
            ("jerusalem", 3), ("bethlehem", 1), ("hebron", 1), ("ziklag", 0));
        places.Total.Should().Be(4);
    }

    [Fact]
    public async Task EachPlaceSaysWhatItWasInTheStory()
    {
        var places = (await EncyclopediaEndpoints.StoryPlaces(_db, _david.Id, null, null, 20))
            .Items.ToDictionary(p => p.Slug);

        places["bethlehem"].Roles.Should().Equal("born");
        places["hebron"].Roles.Should().Equal("lived");
        places["ziklag"].Roles.Should().Equal("lived");
        places["jerusalem"].Roles.Should().Equal("event", "died", "buried");
    }

    [Fact]
    public async Task APlaceOnlyNamedAlongsideIsNamedTogether()
    {
        var other = Place("ramah", "Ramah");
        _db.SaveChanges();
        Verse(_david, FirstSamuel, 19, 18);
        Verse(other, FirstSamuel, 19, 18);
        _db.SaveChanges();

        var places = await EncyclopediaEndpoints.StoryPlaces(_db, _david.Id, null, null, 20);

        places.Items.Single(p => p.Slug == "ramah").Roles.Should().Equal("named-together");
    }

    [Fact]
    public async Task TheSharedVersesAreInCanonicalOrderAndCappedAtWhatWasAsked()
    {
        var all = await EncyclopediaEndpoints.StoryPlaces(_db, _david.Id, null, null, 20);
        var two = await EncyclopediaEndpoints.StoryPlaces(_db, _david.Id, null, null, 2);

        all.Items[0].Verses.Select(v => (v.BookOrdinal, v.Chapter, v.Verse)).Should().Equal(
            (SecondSamuel, 5, 5), (SecondSamuel, 5, 6), (FirstKings, 2, 10));
        two.Items[0].Verses.Should().HaveCount(2);
        two.Items[0].SharedVerses.Should().Be(3);
    }

    [Fact]
    public async Task APlaceWithoutAPointIsListedWithoutOne()
    {
        var places = (await EncyclopediaEndpoints.StoryPlaces(_db, _david.Id, null, null, 20))
            .Items.ToDictionary(p => p.Slug);

        places["ziklag"].Lon.Should().BeNull();
        places["ziklag"].Lat.Should().BeNull();
        places["jerusalem"].Lon.Should().Be(35);
        places["jerusalem"].Confidence.Should().Be(0.5);
    }

    [Fact]
    public async Task ThePlacesAreNamedInTheLanguageAskedFor()
    {
        var places = await EncyclopediaEndpoints.StoryPlaces(_db, _david.Id, "ukr", "ukr", 20);

        places.Items.Single(p => p.Slug == "jerusalem").LocalName.Should().Be("Єрусалим");
    }

    [Fact]
    public async Task ARecordWithNoPlacesHasAnEmptyList()
    {
        var nobody = Record(EntityKind.Person, "nobody", "Nobody");
        _db.SaveChanges();

        var places = await EncyclopediaEndpoints.StoryPlaces(_db, nobody.Id, null, null, 20);

        places.Total.Should().Be(0);
        places.Items.Should().BeEmpty();
    }

    private Entity Record(EntityKind kind, string slug, string name)
    {
        var record = new Entity { Kind = kind, Slug = slug, Name = name, SourceId = $"test:{slug}", Source = "test" };
        _db.Entities.Add(record);
        return record;
    }

    private Entity Place(string slug, string name)
    {
        var place = Record(EntityKind.Place, slug, name);
        _db.PlaceLocations.Add(new PlaceLocation
        {
            Entity = place, Longitude = 35, Latitude = 31.7, Kind = "point", Score = 500,
            ModernId = "m", CoordinatesSource = "test", Source = "openbible",
        });
        return place;
    }

    private void Verse(Entity entity, int book, int chapter, int verse, string source = "a test") =>
        _db.EntityVerses.Add(new EntityVerse
        {
            EntityId = entity.Id, CanonicalBook = book, CanonicalChapter = chapter, CanonicalVerse = verse,
            Source = source, Names = true,
        });

    private void Tie(Entity from, Entity to, string type) =>
        _db.EntityRelationships.Add(new EntityRelationship
        {
            From = from, To = to, Type = type, Category = RelationshipCategories.Read,
            CanonicalBook = SecondSamuel, CanonicalChapter = 5, CanonicalVerse = 5,
            Method = LinkMethod.ModelReading, Confidence = 0.9, Source = "read from Scripture by a test",
        });

    private void Happened(Entity person, string slug, string kind, string location) =>
        _db.Events.Add(new Event
        {
            Slug = slug, Name = slug, Kind = kind, Source = "test", EntityId = person.Id, Location = location,
        });
}
