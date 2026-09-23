using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Endpoints;
using FluentAssertions;
using Microsoft.EntityFrameworkCore.Storage;
using Xunit;

namespace Essenthos.Core.Tests;

/// <summary>
/// What a chapter speaks of: who and where it names in any of its texts, what happened in it, and
/// which periods it falls in.
/// </summary>
[Collection(WitnessDatabaseCollection.Name)]
public sealed class ContextEndpointTests : IDisposable
{
    private const int Genesis = 1;

    private readonly AppDbContext _db;
    private readonly IDbContextTransaction _transaction;
    private readonly Text _hebrew;
    private readonly Text _english;

    public ContextEndpointTests(WitnessDatabase database)
    {
        _db = database.NewContext();
        _transaction = _db.Database.BeginTransaction();

        _hebrew = Corpus.Add(_db, "test-hebrew", TextKind.CriticalEdition, "hbo",
            (10, 1, ["אלה", "תולדת", "בני", "נח"]),
            (10, 2, ["בני", "יפת", "גמר"]),
            (10, 3, ["ובני", "גמר"]),
            (11, 1, ["ויהי"]));
        _english = Corpus.Add(_db, "test-english", TextKind.Translation, "eng",
            (10, 1, ["These", "are", "the", "sons", "of", "Noah"]),
            (10, 2, ["The", "sons", "of", "Japheth"]),
            (10, 3, ["And", "the", "sons", "of", "him"]),
            (11, 1, ["And"]));
        _db.SaveChanges();
    }

    public void Dispose()
    {
        _transaction.Rollback();
        _transaction.Dispose();
        _db.Dispose();
    }

    /// <summary>
    /// The English says <em>him</em> in verse 3 and the Hebrew says Gomer, so Gomer is named in
    /// verse 3 all the same; and the record's own verse list adds a verse no word was resolved in.
    /// </summary>
    [Fact]
    public async Task ARecordIsNamedWhereverAnyTextOrItsOwnListNamesIt()
    {
        var noah = Record("noah", EntityKind.Person);
        var gomer = Record("gomer", EntityKind.Person);
        await _db.SaveChangesAsync();
        Names(_db.WordAt(_english, 10, 1, 6), noah);
        Names(_db.WordAt(_hebrew, 10, 1, 4), noah);
        Names(_db.WordAt(_hebrew, 10, 2, 3), gomer);
        Names(_db.WordAt(_hebrew, 10, 3, 2), gomer);
        NamedAt(gomer, 10, 1);
        await _db.SaveChangesAsync();

        var context = await ContextEndpoints.Context(_db, Genesis, 10, null, default);

        context.Entities.Select(e => (e.Slug, string.Join(',', e.Verses))).Should().Equal(
            ("gomer", "1,2,3"),
            ("noah", "1"));
    }

    /// <summary>
    /// Two readings of equal standing that name two different men are the corpus saying it does
    /// not know, and the panel does not pick one of them any more than the reader does.
    /// </summary>
    [Fact]
    public async Task AWordTwoEqualReadingsDisagreeOnNamesNobody()
    {
        var japheth = Record("japheth", EntityKind.Person);
        var other = Record("japheth-2", EntityKind.Person);
        await _db.SaveChangesAsync();
        var word = _db.WordAt(_hebrew, 10, 2, 2);
        Names(word, japheth, LinkMethod.ModelReading, 0.8);
        Names(word, other, LinkMethod.ModelReading, 0.8);
        await _db.SaveChangesAsync();

        var context = await ContextEndpoints.Context(_db, Genesis, 10, null, default);

        context.Entities.Should().BeEmpty();
    }

    [Fact]
    public async Task AReferenceTheSourceCannotResolveIsLeftOut()
    {
        var lord = Record("yhvh", EntityKind.Person);
        await _db.SaveChangesAsync();
        NamedAt(lord, 10, 1, disputed: true);
        await _db.SaveChangesAsync();

        var context = await ContextEndpoints.Context(_db, Genesis, 10, null, default);

        context.Entities.Should().BeEmpty();
    }

    [Fact]
    public async Task APlaceCarriesWhereItIsAndAnotherChapterIsNotAsked()
    {
        var shinar = Record("shinar", EntityKind.Place);
        await _db.SaveChangesAsync();
        _db.PlaceLocations.Add(new PlaceLocation
        {
            EntityId = shinar.Id,
            Longitude = 44.4,
            Latitude = 32.5,
            Kind = "point",
            Score = 500,
            ModernId = "test",
            CoordinatesSource = "test",
            Source = "test",
        });
        NamedAt(shinar, 10, 3);
        NamedAt(Record("babel", EntityKind.Place), 11, 1);
        await _db.SaveChangesAsync();

        var context = await ContextEndpoints.Context(_db, Genesis, 10, null, default);

        var place = context.Entities.Should().ContainSingle().Subject;
        place.Kind.Should().Be("place");
        place.Location!.Lon.Should().Be(44.4);
        place.Location.Confidence.Should().Be(0.5);
    }

    /// <summary>
    /// The event that ends one reign begins the next, and the chapter it is set in belongs to the
    /// period it opens; a one-year period holds only its own year.
    /// </summary>
    [Fact]
    public async Task AChapterFallsInThePeriodsItsEventsOpenOrFallInside()
    {
        var begins = Event("reign-begins", 10, 2, 3031);
        Event("first-verse", 10, 1, 3040);
        await _db.SaveChangesAsync();
        Period("united", 0, 2939, 3031);
        Period("divided", 0, 3031, 3376);
        Period("jeroboam", 1, 3031, 3052, opens: begins);
        Period("shamgar", 1, 2749, 2749);
        Period("solomon-lifetime", 2, 2971, 3031, kind: "life");
        await _db.SaveChangesAsync();

        var context = await ContextEndpoints.Context(_db, Genesis, 10, null, default);

        context.Events.Select(e => e.Slug).Should().Equal("first-verse", "reign-begins");
        context.Periods.Select(p => p.Slug).Should().Equal("divided", "jeroboam");
        context.PeriodsFrom.Should().Be(ChapterPeriods.FromItsEvents);
    }

    /// <summary>
    /// A chapter with no event of its own is placed by its dated neighbours in the book, and only in
    /// a period that holds both of them.
    /// </summary>
    [Fact]
    public async Task AChapterWithoutEventsIsPlacedBetweenItsNeighbours()
    {
        Event("before", 9, 1, 2515);
        Event("after", 11, 1, 2516);
        await _db.SaveChangesAsync();
        Period("wilderness", 0, 2515, 2555);
        Period("egypt", 0, 2300, 2515);
        await _db.SaveChangesAsync();

        var context = await ContextEndpoints.Context(_db, Genesis, 10, null, default);

        context.Events.Should().BeEmpty();
        context.Periods.Select(p => p.Slug).Should().Equal("wilderness");
        context.PeriodsFrom.Should().Be(ChapterPeriods.FromTheEventsAround);
    }

    [Fact]
    public async Task OneNeighbourAlonePlacesNothing()
    {
        Event("before", 9, 1, 2515);
        await _db.SaveChangesAsync();
        Period("wilderness", 0, 2300, 2555);
        await _db.SaveChangesAsync();

        var context = await ContextEndpoints.Context(_db, Genesis, 10, null, default);

        context.Periods.Should().BeEmpty();
        context.PeriodsFrom.Should().BeNull();
    }

    private Entity Record(string slug, EntityKind kind)
    {
        var entity = new Entity
        {
            Kind = kind,
            Slug = slug,
            Name = char.ToUpperInvariant(slug[0]) + slug[1..],
            SourceId = $"test:{slug}",
            Source = "test",
        };
        _db.Entities.Add(entity);
        return entity;
    }

    private void Names(Word word, Entity entity, LinkMethod method = LinkMethod.StatedBySource, double? confidence = null) =>
        _db.WordEntities.Add(new WordEntity
        {
            WordId = word.Id,
            EntityId = entity.Id,
            Method = method,
            Confidence = confidence,
            Source = $"test:{method}:{entity.Slug}",
        });

    private void NamedAt(Entity entity, int chapter, int verse, bool disputed = false) =>
        _db.EntityVerses.Add(new EntityVerse
        {
            Entity = entity,
            CanonicalBook = Genesis,
            CanonicalChapter = chapter,
            CanonicalVerse = verse,
            Disputed = disputed,
            Source = "test",
        });

    private Database.Entities.Event Event(string slug, int chapter, int verse, int year)
    {
        var happened = new Database.Entities.Event
        {
            Slug = slug,
            Name = slug,
            YearFromCreation = year,
            CanonicalBook = Genesis,
            CanonicalChapter = chapter,
            CanonicalVerse = verse,
            Source = "test",
        };
        _db.Events.Add(happened);
        return happened;
    }

    private void Period(
        string slug, int level, int start, int end, Database.Entities.Event? opens = null, string kind = "era") =>
        _db.Periods.Add(new Period
        {
            Slug = slug,
            Name = slug,
            Kind = kind,
            Level = level,
            StartYear = start,
            EndYear = end,
            StartEventId = opens?.Id,
            Source = "test",
        });
}
