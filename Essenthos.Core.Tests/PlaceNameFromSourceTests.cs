using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Endpoints;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Xunit;

namespace Essenthos.Core.Tests;

/// <summary>
/// A place no verse of any text stands behind, which the corpus holds no name for in a reader's
/// language, is named by its source alone. The API says so, for every language but English, and the
/// index that offers records a verse stands behind never lists it.
/// </summary>
[Collection(WitnessDatabaseCollection.Name)]
public sealed class PlaceNameFromSourceTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly IDbContextTransaction _transaction;
    private readonly Entity _adasa;
    private readonly Entity _abila;
    private readonly Entity _jericho;
    private readonly Entity _carthage;

    public PlaceNameFromSourceTests(WitnessDatabase database)
    {
        _db = database.NewContext();
        _transaction = _db.Database.BeginTransaction();

        _adasa = Place("adasa", "Adasa");
        _abila = Place("abila", "Abila");
        _jericho = Place("jericho", "Jericho");
        _carthage = Place("carthage", "Carthage");
        _db.SaveChanges();

        _db.EntityVerses.Add(new EntityVerse
        {
            EntityId = _jericho.Id, CanonicalBook = 6, CanonicalChapter = 2, CanonicalVerse = 1, Source = "a test",
        });
        _db.EntityNameForms.AddRange(
            Form(_abila, "ukr", "Авіла"),
            Form(_jericho, "ukr", "Єрихон"),
            Form(_jericho, "deu", "Jericho"));
        foreach (var place in new[] { _adasa, _abila, _jericho, _carthage })
        {
            _db.PlaceLocations.Add(new PlaceLocation
            {
                EntityId = place.Id, Longitude = 35, Latitude = 31, Kind = "point", Score = 500,
                ModernId = "m", CoordinatesSource = "test", Source = "openbible",
            });
        }

        _db.SaveChanges();
    }

    public void Dispose()
    {
        _transaction.Rollback();
        _transaction.Dispose();
        _db.Dispose();
    }

    /// <summary>
    /// A place with no verse and no name in the language is the source's alone; a place with a name
    /// in the language, or a verse, is not.
    /// </summary>
    [Theory]
    [InlineData("ukr", new[] { "adasa", "carthage" })]
    [InlineData("deu", new[] { "adasa", "abila", "carthage" })]
    [InlineData("spa", new[] { "adasa", "abila", "carthage" })]
    public async Task APlaceNoVerseAndNoNameInTheLanguageStandsBehindIsTheSourcesAlone(string language, string[] expected)
    {
        var ids = new[] { _adasa, _abila, _jericho, _carthage }.Select(p => p.Id).ToList();
        var names = await EntityNames.Of(_db, ids, language, CancellationToken.None);

        var bySource = await EntityNames.NamedOnlyBySource(_db, ids, language, names, CancellationToken.None);

        var slugs = await _db.Entities.Where(e => bySource.Contains(e.Id)).Select(e => e.Slug).OrderBy(s => s).ToListAsync();
        slugs.Should().Equal(expected.OrderBy(s => s));
    }

    /// <summary>English names a place by its headword, so there is no other name to be missing.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("eng")]
    public async Task InEnglishNoNameIsTheSourcesAlone(string? language)
    {
        var ids = new[] { _adasa.Id, _carthage.Id };

        var bySource = await EntityNames.NamedOnlyBySource(_db, ids, language, new Dictionary<int, string>(), CancellationToken.None);

        bySource.Should().BeEmpty();
    }

    [Fact]
    public async Task TheMapSaysWhichPlacesAreNamedByTheSourceAlone()
    {
        var ukrainian = await EncyclopediaEndpoints.Map(_db, "ukr");
        var english = await EncyclopediaEndpoints.Map(_db, "eng");

        ukrainian.Items.Where(p => p.NameFromSource).Select(p => p.Slug).Should().BeEquivalentTo("adasa", "carthage");
        ukrainian.Items.Single(p => p.Slug == "abila").LocalName.Should().Be("Авіла");
        english.Items.Should().OnlyContain(p => !p.NameFromSource);
    }

    /// <summary>
    /// The places the API calls the source's alone are exactly places the index does not offer, so a
    /// Latin headword is never filed among the Cyrillic names of an index.
    /// </summary>
    [Fact]
    public async Task ThePlacesNamedByTheSourceAloneAreNeverInTheIndex()
    {
        var listed = await EncyclopediaEndpoints.Listed(_db.Entities).Select(e => e.Slug).ToListAsync();
        var map = await EncyclopediaEndpoints.Map(_db, "ukr");

        var bySource = map.Items.Where(p => p.NameFromSource).Select(p => p.Slug).ToList();

        bySource.Should().NotBeEmpty();
        bySource.Should().NotIntersectWith(listed);
        listed.Should().Equal("jericho");
    }

    private Entity Place(string slug, string name)
    {
        var place = new Entity
        {
            Kind = EntityKind.Place, Slug = slug, Name = name, SourceId = $"test:{slug}", Source = "test",
        };
        _db.Entities.Add(place);
        return place;
    }

    private static EntityNameForm Form(Entity entity, string language, string form) =>
        new()
        {
            Entity = entity, Language = language, GrammaticalCase = GrammaticalCases.Nominative, Form = form,
            Method = LinkMethod.ModelReading, Confidence = 1, Source = "a test",
        };
}
