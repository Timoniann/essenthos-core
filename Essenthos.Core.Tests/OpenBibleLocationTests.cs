using System.Text.Json;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Endpoints;
using Essenthos.Core.Loading.Encyclopedia;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Essenthos.Core.Tests;

/// <summary>
/// One point per place, and never one the gazetteer credits to OpenStreetMap.
///
/// The rule is the licence: the repository is CC BY 4.0 except for OpenStreetMap's data, which is
/// ODbL and share-alike, and the credit on each modern location is what tells the two apart. So the
/// real files are checked against their own credits, and a handful of hand-written rows pin each
/// clause of the rule down on its own.
/// </summary>
[Trait(TestCategory.Name, TestCategory.Corpus)]
[Collection(WitnessDatabaseCollection.Name)]
public sealed class OpenBibleLocationTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly IDbContextTransaction _transaction;
    private readonly string _folder;

    public OpenBibleLocationTests(WitnessDatabase database)
    {
        _db = database.NewContext();
        _transaction = _db.Database.BeginTransaction();

        _folder = Path.Combine(Path.GetTempPath(), $"openbible-locations-{Guid.NewGuid():n}");
        Directory.CreateDirectory(_folder);
        File.WriteAllLines(Path.Combine(_folder, "ancient.jsonl"),
        [
            Ancient("a1", ("m-far", 120, "1.0,1.0", "point"), ("m-best", 600, "35.2,31.7", "point")),
            Ancient("a2", ("m-osm", 900, "35.0,31.0", "point")),
            Ancient("a3", ("m-osm-drawn", 900, "36.3,33.5", "representative point")),
            Ancient("a4", ("m-osm-beside", 800, "36.3,33.5", "representative point")),
            Ancient("a5", ("m-google", 700, "34.6,30.7", "point")),
            Ancient("a6", ("m-commercial", 1_100, "35.02,31.79", "settlement")),
            """{"id":"a7","friendly_id":"Nowhere","identifications":[],"modern_associations":{}}""",
        ]);
        File.WriteAllLines(Path.Combine(_folder, "modern.jsonl"),
        [
            Modern("m-far", """{"type":"wikidata"}"""),
            Modern("m-best", """{"type":"daahl"}"""),
            Modern("m-osm", """{"type":"osm","geometry_credit":"osm"}"""),
            Modern("m-osm-drawn", """{"type":"wikidata"}""",
                """{"representative_point":{"geometry_credit":"osm","id":"x"}}"""),
            Modern("m-osm-beside", """{"type":"wikidata"}""",
                """{"precise":{"geometry_credit":"osm","id":"g"},"representative_point":{"id":"r"}}"""),
            Modern("m-google", """{"type":"google_maps"}"""),
            Modern("m-commercial", """{"type":"amudanan"}""", custom: "35.03,31.80"),
        ]);
    }

    public void Dispose()
    {
        _transaction.Rollback();
        _transaction.Dispose();
        _db.Dispose();
        Directory.Delete(_folder, recursive: true);
    }

    [Fact]
    public void TheBestScoredIdentificationGivesThePoint()
    {
        var located = Read().Located.Single(l => l.PlaceId == "a1");

        located.ModernId.Should().Be("m-best");
        (located.Longitude, located.Latitude).Should().Be((35.2, 31.7));
        located.Score.Should().Be(600);
        located.CoordinatesSource.Should().Be("daahl");
        located.Kind.Should().Be("point");
    }

    /// <summary>
    /// A point credited to OpenStreetMap, by its source or on the drawn role it is, is not held —
    /// and the place gets no point rather than its second-best one.
    /// </summary>
    [Fact]
    public void AnOpenStreetMapPointIsNeverTaken()
    {
        var reading = Read();

        reading.Located.Select(l => l.PlaceId).Should().NotContain(["a2", "a3"]);
        reading.OpenStreetMap.Should().Be(2);
    }

    /// <summary>
    /// A river's course drawn from OpenStreetMap beside a point credited to Wikidata does not make
    /// the point OpenStreetMap's.
    /// </summary>
    [Fact]
    public void OpenStreetMapGeometryBesideAPointDoesNotDisqualifyIt()
    {
        var located = Read().Located.Single(l => l.PlaceId == "a4");

        located.Kind.Should().Be("representative-point");
        located.CoordinatesSource.Should().Be("wikidata");
    }

    [Fact]
    public void APointReadOffACommercialMapIsTakenOnlyAsTheGazetteersOwnIndependentPoint()
    {
        var reading = Read();

        reading.Located.Select(l => l.PlaceId).Should().NotContain("a5");
        reading.Commercial.Should().Be(1);

        var own = reading.Located.Single(l => l.PlaceId == "a6");
        (own.Longitude, own.Latitude).Should().Be((35.03, 31.80));
        own.Kind.Should().Be("settlement");
    }

    [Fact]
    public void APlaceWithNoIdentificationHasNoPoint()
    {
        var reading = Read();

        reading.Located.Select(l => l.PlaceId).Should().NotContain("a7");
        reading.Unidentified.Should().Be(1);
    }

    /// <summary>
    /// The files as fetched, checked against their own credits rather than against the rule's code:
    /// no point held names a modern location that credits OpenStreetMap anywhere on its source or
    /// its point.
    /// </summary>
    [Fact]
    public void NoPointFromTheRealGazetteerIsCreditedToOpenStreetMap()
    {
        var ancient = Path.Combine(TestResources.OpenBibleFolder, "ancient.jsonl");
        var modern = Path.Combine(TestResources.OpenBibleFolder, "modern.jsonl");

        var reading = OpenBibleLocationLoader.Read(ancient, modern);

        reading.Located.Should().HaveCount(1_311);
        reading.OpenStreetMap.Should().Be(20);
        reading.Commercial.Should().Be(4);
        reading.Unidentified.Should().Be(7);

        var credited = File.ReadLines(modern)
            .Select(line => JsonDocument.Parse(line).RootElement)
            .Where(root => Credits(root, "coordinates_source") || Credits(root, "geojson_roles", "point")
                           || Credits(root, "geojson_roles", "representative_point"))
            .Select(root => root.GetProperty("id").GetString())
            .ToHashSet();

        credited.Should().NotBeEmpty();
        reading.Located.Where(l => credited.Contains(l.ModernId)).Should().BeEmpty();
        reading.Located.Should().OnlyContain(l => l.CoordinatesSource != "osm" && l.CoordinatesSource != "google_maps");
    }

    [Fact]
    public async Task TheLoadLocatesTheRecordsThatCarryTheIdentifierAndOnlyOnce()
    {
        var located = Place("a1", "Tekoa");
        var unlocated = Place("a2", "Anab");
        _db.SaveChanges();

        var loader = new OpenBibleLocationLoader(_db, NullLogger<OpenBibleLocationLoader>.Instance);
        var first = await loader.Load(_folder);
        var second = await loader.Load(_folder);

        first.AlreadyLoaded.Should().BeFalse();
        first.Located.Should().Be(1);
        first.Unheld.Should().Be(2);
        second.AlreadyLoaded.Should().BeTrue();

        (await _db.PlaceLocations.SingleAsync()).EntityId.Should().Be(located.Id);
        (await _db.PlaceLocations.AnyAsync(l => l.EntityId == unlocated.Id)).Should().BeFalse();
    }

    /// <summary>
    /// The map carries every located place with its verse count, and its confidence as the
    /// gazetteer's score over a thousand.
    /// </summary>
    [Fact]
    public async Task TheMapCarriesEveryLocatedPlaceWithItsReferences()
    {
        var tekoa = Place("a1", "Tekoa");
        tekoa.PlaceKind = "settlement";
        Place("a2", "Anab");
        _db.SaveChanges();
        Naming(tekoa, 10, 14, 2);
        Naming(tekoa, 10, 14, 2, "another");
        Naming(tekoa, 30, 1, 1);
        _db.SaveChanges();

        await new OpenBibleLocationLoader(_db, NullLogger<OpenBibleLocationLoader>.Instance).Load(_folder);
        var map = await EncyclopediaEndpoints.Map(_db);

        map.Total.Should().Be(1);
        var only = map.Items.Should().ContainSingle().Subject;
        (only.Slug, only.Name, only.Lon, only.Lat, only.Kind, only.Confidence, only.References)
            .Should().Be(("tekoa", "Tekoa", 35.2, 31.7, "point", 0.6, 2));
        only.Chapters.Should().Equal(10014, 30001);
        only.PlaceKind.Should().Be("settlement");
        map.Datasets.Should().Equal("openbible");
    }

    private OpenBibleLocationLoader.Reading Read() =>
        OpenBibleLocationLoader.Read(Path.Combine(_folder, "ancient.jsonl"), Path.Combine(_folder, "modern.jsonl"));

    private static bool Credits(JsonElement root, params string[] path)
    {
        var at = root;
        foreach (var step in path)
        {
            if (at.ValueKind != JsonValueKind.Object || !at.TryGetProperty(step, out at))
            {
                return false;
            }
        }

        return at.ValueKind == JsonValueKind.Object
               && ((at.TryGetProperty("geometry_credit", out var credit) && credit.GetString() == "osm")
                   || (at.TryGetProperty("type", out var type) && type.ValueKind == JsonValueKind.String
                       && type.GetString() == "osm"));
    }

    /// <summary>
    /// One ancient place with one identification per modern location it could be, each with one
    /// resolution, scored in its modern associations the way the gazetteer writes them.
    /// </summary>
    private static string Ancient(
        string id,
        params (string Modern, int Score, string LonLat, string Type)[] candidates)
    {
        var identifications = candidates.Select(c => new
        {
            resolutions = new[] { new { lonlat = c.LonLat, lonlat_type = c.Type, modern_basis_id = c.Modern } },
        });
        var associations = candidates
            .Select((c, i) => (c, i))
            .ToDictionary(x => x.c.Modern, x => new { identification_ids = new[] { new[] { x.i, 0 } }, score = x.c.Score });

        return JsonSerializer.Serialize(new
        {
            id,
            friendly_id = id,
            identifications,
            modern_associations = associations,
        });
    }

    private static string Modern(string id, string source, string? roles = null, string? custom = null) =>
        $$"""{"id":"{{id}}","coordinates_source":{{source}},"geojson_roles":{{roles ?? "{}"}}{{(custom is null ? "" : $",\"custom_lonlat\":\"{custom}\"")}}}""";

    private Entity Place(string openBibleId, string name)
    {
        var entity = new Entity
        {
            Kind = EntityKind.Place,
            Slug = name.ToLowerInvariant(),
            Name = name,
            OpenBibleId = openBibleId,
            SourceId = $"test:{openBibleId}",
            Source = "test",
        };
        _db.Entities.Add(entity);
        return entity;
    }

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
