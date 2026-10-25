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
    private const int Psalms = 19;
    private const int Acts = 44;
    private const int Romans = 45;

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

    [Theory]
    [InlineData(1, true)]
    [InlineData(17, true)]
    [InlineData(18, false)]
    [InlineData(19, false)]
    [InlineData(23, false)]
    [InlineData(39, false)]
    [InlineData(40, true)]
    [InlineData(43, true)]
    [InlineData(44, true)]
    [InlineData(45, false)]
    [InlineData(66, false)]
    [InlineData(73, false)]
    public void GenesisToEstherTheGospelsAndActsTellAStory(int book, bool tells) =>
        EncyclopediaEndpoints.TellsAStory(book).Should().Be(tells);

    [Fact]
    public async Task EveryPlaceOfTheStoryIsInTheStoryGroup()
    {
        var places = await EncyclopediaEndpoints.StoryPlaces(_db, _david.Id, null, null, 20);

        places.Items.Should().OnlyContain(p => p.Group == "story");
        places.Items.Select(p => (p.Slug, p.StoryVerses)).Should().Equal(
            ("jerusalem", 3), ("bethlehem", 1), ("hebron", 1), ("ziklag", 0));
    }

    [Fact]
    public async Task APlaceOnlyNamedTogetherOutsideTheStoryBooksComesAfterTheStory()
    {
        var zion = Place("zion", "Zion");
        var rome = Place("rome", "Rome");
        var antioch = Place("antioch", "Antioch");
        _db.SaveChanges();
        foreach (var verse in new[] { 2, 3, 4, 5, 6 })
        {
            Verse(_david, Psalms, 2, verse);
            Verse(zion, Psalms, 2, verse);
        }

        Verse(_david, Romans, 1, 7);
        Verse(rome, Romans, 1, 7);
        Verse(_david, Acts, 11, 26);
        Verse(antioch, Acts, 11, 26);
        _db.SaveChanges();

        var places = await EncyclopediaEndpoints.StoryPlaces(_db, _david.Id, null, null, 20);

        places.Items.Select(p => (p.Slug, p.Group, p.SharedVerses, p.StoryVerses)).Should().Equal(
            ("jerusalem", "story", 3, 3),
            ("antioch", "story", 1, 1),
            ("bethlehem", "story", 1, 1),
            ("hebron", "story", 1, 1),
            ("ziklag", "story", 0, 0),
            ("zion", "other-books", 5, 0),
            ("rome", "other-books", 1, 0));
        places.Total.Should().Be(7);
        places.Items.Single(p => p.Slug == "zion").Roles.Should().Equal("named-together");
    }

    [Fact]
    public async Task APlaceOfTheStoryCountsItsVersesInEachGroupOfBooks()
    {
        Verse(_david, Psalms, 122, 2);
        Verse(_david, Psalms, 122, 3);
        var jerusalem = _db.Entities.Local.Single(e => e.Slug == "jerusalem");
        Verse(jerusalem, Psalms, 122, 2);
        Verse(jerusalem, Psalms, 122, 3);
        _db.SaveChanges();

        var place = (await EncyclopediaEndpoints.StoryPlaces(_db, _david.Id, null, null, 20))
            .Items.Single(p => p.Slug == "jerusalem");

        place.Group.Should().Be("story");
        (place.SharedVerses, place.StoryVerses).Should().Be((5, 3));
        place.Verses.Select(v => v.BookOrdinal).Should().Equal(SecondSamuel, SecondSamuel, FirstKings, Psalms, Psalms);
    }

    [Fact]
    public async Task APlaceARoleGivesIsInTheStoryWhereverItsVersesAre()
    {
        var anathoth = Place("anathoth", "Anathoth");
        _db.SaveChanges();
        Verse(_david, 24, 1, 1);
        Verse(anathoth, 24, 1, 1);
        Tie(_david, anathoth, DescriptorRelations.FromPlace);
        _db.SaveChanges();

        var place = (await EncyclopediaEndpoints.StoryPlaces(_db, _david.Id, null, null, 20))
            .Items.Single(p => p.Slug == "anathoth");

        place.Group.Should().Be("story");
        (place.SharedVerses, place.StoryVerses).Should().Be((1, 0));
        place.Roles.Should().Equal("lived");
    }

    [Theory]
    [InlineData("jeremiah-6", 24, true)]
    [InlineData("jeremiah-6", 25, false)]
    [InlineData("jeremiah-6", 26, false)]
    [InlineData("ezekiel", 26, true)]
    [InlineData("daniel-2", 27, true)]
    [InlineData("jonah", 32, true)]
    [InlineData("amos", 30, true)]
    [InlineData("job", 18, true)]
    [InlineData("saul-2", 45, true)]
    [InlineData("saul-2", 57, true)]
    [InlineData("saul-2", 58, false)]
    [InlineData("simon", 60, true)]
    [InlineData("simon", 45, false)]
    [InlineData("david", 19, false)]
    [InlineData("jeremiah-6", 1, true)]
    public void ABookNamedAfterAPersonTellsTheirStory(string slug, int book, bool tells) =>
        EncyclopediaEndpoints.TellsAStory(book, slug).Should().Be(tells);

    [Fact]
    public async Task APlaceNamedWithAPersonInTheirOwnBookIsInTheStory()
    {
        var jeremiah = Record(EntityKind.Person, "jeremiah-6", "Jeremiah");
        var other = Record(EntityKind.Person, "baruch", "Baruch");
        var tahpanhes = Place("tahpanhes", "Tahpanhes");
        var babylon = Place("babylon", "Babylon");
        _db.SaveChanges();
        Verse(jeremiah, 24, 43, 8);
        Verse(other, 24, 43, 8);
        Verse(tahpanhes, 24, 43, 8);
        Verse(jeremiah, 26, 1, 1);
        Verse(babylon, 26, 1, 1);
        _db.SaveChanges();

        var his = (await EncyclopediaEndpoints.StoryPlaces(_db, jeremiah.Id, null, null, 20))
            .Items.ToDictionary(p => p.Slug);
        var baruchs = await EncyclopediaEndpoints.StoryPlaces(_db, other.Id, null, null, 20);

        (his["tahpanhes"].Group, his["tahpanhes"].StoryVerses).Should().Be(("story", 1));
        his["babylon"].Group.Should().Be("other-books");
        baruchs.Items.Single().Group.Should().Be("other-books");
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
