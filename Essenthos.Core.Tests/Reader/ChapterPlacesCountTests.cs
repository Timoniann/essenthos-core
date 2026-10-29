using Essenthos.Core.Corpus;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Endpoints;
using FluentAssertions;
using Microsoft.EntityFrameworkCore.Storage;
using Xunit;

namespace Essenthos.Core.Tests.Reader;

/// <summary>
/// The count under a chapter of the places the map can show is the map's own answer: the places whose
/// chapters on the map hold that chapter, a place with no point left out however often it is named.
/// </summary>
[Collection(WitnessDatabaseCollection.Name)]
public sealed class ChapterPlacesCountTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly IDbContextTransaction _transaction;

    public ChapterPlacesCountTests(WitnessDatabase database)
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
    public async Task AChapterCountsTheMapsPlacesItNames()
    {
        var bethel = Place("bethel");
        var ai = Place("ai");
        var negeb = Place("negeb");
        var egypt = Place("egypt");
        _db.SaveChanges();
        Located(bethel);
        Located(ai);
        Located(egypt);
        Naming(bethel, 1, 12, 8);
        Naming(bethel, 1, 12, 8, "another");
        Naming(ai, 1, 12, 8);
        Naming(negeb, 1, 12, 9);
        Naming(egypt, 1, 13, 1);
        Naming(egypt, 1, 12, 10, $"{ShownVerses.Witness} a test");
        _db.SaveChanges();

        var map = await EncyclopediaEndpoints.Map(_db);
        var onTheMap = map.Items.Count(place => place.Chapters.Contains(1_012));

        (await EncyclopediaEndpoints.LocatedIn(_db, 1, 12, default)).Should().Be(2).And.Be(onTheMap);
        (await EncyclopediaEndpoints.LocatedIn(_db, 1, 13, default)).Should().Be(1);
        (await EncyclopediaEndpoints.LocatedIn(_db, 1, 14, default)).Should().Be(0);
    }

    [Fact]
    public void TheBriefListKeepsWhatAReaderPicksATextByAndDropsItsParagraphs()
    {
        var corpus = new CorpusResponse(
            "KJV", "King James Version", "translation", "en", "ltr", true, "public-domain", null, null, null,
            new CoverageResponse(1, 66, [.. Enumerable.Range(1, 66)]))
        {
            Translators = "The translators of 1611",
            Edition = "Cambridge, 1769",
            Aliases = ["AV"],
            About = "A long paragraph.",
            Summary = new Dictionary<string, string> { ["en"] = "What it is." },
            Tagline = new Dictionary<string, string> { ["en"] = "One line." },
            Citation = "A citation.",
            RightsNote = "A note on rights.",
            TermsNote = new Dictionary<string, string> { ["en"] = "A note on terms." },
            PartSources = [],
        };

        var brief = ReadEndpoints.Brief(corpus);

        (brief.Id, brief.Name, brief.Language, brief.Translators, brief.Edition, brief.License)
            .Should().Be(("KJV", "King James Version", "en", "The translators of 1611", "Cambridge, 1769", "public-domain"));
        brief.Aliases.Should().Equal("AV");
        new object?[] { brief.About, brief.Summary, brief.Tagline, brief.Citation, brief.RightsNote, brief.TermsNote, brief.PartSources }
            .Should().AllSatisfy(value => value.Should().BeNull());
    }

    private Entity Place(string slug)
    {
        var entity = new Entity { Kind = EntityKind.Place, Slug = slug, Name = slug, SourceId = $"test:{slug}", Source = "test" };
        _db.Entities.Add(entity);
        return entity;
    }

    private void Located(Entity place) =>
        _db.PlaceLocations.Add(new PlaceLocation
        {
            EntityId = place.Id, Longitude = 35, Latitude = 32, Kind = "point", Score = 500,
            ModernId = "m", CoordinatesSource = "test", Source = "openbible",
        });

    private void Naming(Entity entity, int book, int chapter, int verse, string source = "test") =>
        _db.EntityVerses.Add(new EntityVerse
        {
            EntityId = entity.Id,
            CanonicalBook = book,
            CanonicalChapter = chapter,
            CanonicalVerse = verse,
            Label = entity.Name,
            Disputed = false,
            Source = source,
        });
}
