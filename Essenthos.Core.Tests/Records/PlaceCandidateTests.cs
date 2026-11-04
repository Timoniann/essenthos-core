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

namespace Essenthos.Core.Tests.Records;

/// <summary>
/// Every answer the gazetteer records to where a place was, held and ranked by its own score: the
/// sites beside the likeliest one, an area round a site, another ancient place it is taken to be,
/// and that it is no located place at all — which, where it outranks every site, leaves the place
/// without a point rather than at a site its source doubts.
/// </summary>
[Collection(WitnessDatabaseCollection.Name)]
public sealed class PlaceCandidateTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly IDbContextTransaction _transaction;
    private readonly string _folder;

    public PlaceCandidateTests(WitnessDatabase database)
    {
        _db = database.NewContext();
        _transaction = _db.Database.BeginTransaction();
        _folder = Path.Combine(Path.GetTempPath(), $"openbible-candidates-{Guid.NewGuid():n}");
        Directory.CreateDirectory(_folder);
        Write(Sinai, Bether, Babylon, Havilah);
    }

    public void Dispose()
    {
        _transaction.Rollback();
        _transaction.Dispose();
        _db.Dispose();
        Directory.Delete(_folder, recursive: true);
    }

    /// <summary>A mountain the gazetteer gives four sites for, one of them in Arabia, and two of them doubted.</summary>
    private static readonly string Sinai = Place("a-sinai",
        Site("m-musa", "Jebel Musa", 465, "33.973333,28.539722", "point", "mountain",
            tags: """{"authority_traditional":14,"authority_usually":2,"confidence_likely":4}"""),
        Site("m-karkom", "Har Karkom", 103, "34.75,30.283333", "point", "mountain",
            tags: """{"confidence_possible":6}"""),
        Site("m-lawz", "Jabal al Lawz", 4, "35.4,28.65", "point", "mountain",
            tags: """{"authority_scholar":1}"""),
        Site("m-osm", "Jebel Osm", -24, "35.6,27.9", "point", "mountain"));

    /// <summary>A word some translations print as a place, which the gazetteer most likely takes for none.</summary>
    private static readonly string Bether = Place("a-bether",
        Site("m-bittir", "Battir", -87, "35.13,31.73", "point", "settlement"),
        Special("not_a_place", 628),
        Special("not_a_place", 12),
        Special("recursive", 900));

    /// <summary>Another name for Rome, and less likely for Jerusalem.</summary>
    private static readonly string Babylon = Place("a-babylon-2",
        Site("m-rome", "Rome", 917, "12.4852,41.8922", "point", "settlement", through: "a-rome"),
        Site("m-jerusalem", "Jerusalem", 22, "35.234167,31.776667", "point", "settlement", through: "a-jerusalem"));

    /// <summary>A land known only as an area: about 200 km round one site, a region beside another.</summary>
    private static readonly string Havilah = Place("a-havilah",
        Site("m-babylonia", "Babylon", 328, "44.42,32.54", "representative point", "region", relation: "<"),
        Site("m-sanaa", "Sanaa", -49, "44.2145,15.3551", "center", "region", relation: ">", radius: 200_000));

    [Fact]
    public void EverySiteIsHeldInTheOrderOfItsScore()
    {
        var sinai = Read().Candidates["a-sinai"];

        sinai.Select(c => (c.Rank, c.Site, c.Score)).Should().Equal(
            (0, "Jebel Musa", 465), (1, "Har Karkom", 103), (2, "Jabal al Lawz", 4), (3, "Jebel Osm", -24));
        sinai[0].Grounds.Should().Equal("traditional", "usually");
        sinai[1].Grounds.Should().BeEmpty();
        sinai[2].Grounds.Should().Equal("scholar");
        (sinai[2].Longitude, sinai[2].Latitude, sinai[2].Kind).Should().Be((35.4, 28.65, "point"));
    }

    /// <summary>
    /// A doubted site whose point the gazetteer credits to OpenStreetMap is still an answer, by its
    /// name and score, and has no point.
    /// </summary>
    [Fact]
    public void ASiteWhosePointIsOpenStreetMapsIsHeldWithoutIt()
    {
        var withheld = Read().Candidates["a-sinai"].Single(c => c.Site == "Jebel Osm");

        withheld.Withheld.Should().Be("openstreetmap");
        (withheld.Longitude, withheld.Latitude, withheld.Kind, withheld.CoordinatesSource)
            .Should().Be(((double?)null, (double?)null, (string?)null, (string?)null));
    }

    /// <summary>
    /// The gazetteer's likeliest answer for Bether is that it is no place, so it gets no point; its
    /// one doubted site is held below that answer, and the answer that says nothing is not held.
    /// </summary>
    [Fact]
    public void ANoPlaceAnswerThatOutranksEverySiteLeavesThePlaceWithoutAPoint()
    {
        var reading = Read();
        var bether = reading.Candidates["a-bether"];

        reading.Located.Select(l => l.PlaceId).Should().NotContain("a-bether");
        reading.NoSite.Should().Be(1);
        bether.Select(c => (c.Rank, c.Special, c.Site, c.Score)).Should().Equal(
            (0, "not-a-place", null, 628), (1, null, "Battir", -87));
    }

    [Fact]
    public void AnAnswerThatTakesThePlaceForAnotherNamesThatPlace()
    {
        var babylon = Read().Candidates["a-babylon-2"];

        babylon.Select(c => c.Through).Should().Equal("a-rome", "a-jerusalem");
    }

    [Fact]
    public void AnAreaKeepsHowItStandsToItsSiteAndItsSize()
    {
        var havilah = Read().Candidates["a-havilah"];

        (havilah[0].Kind, havilah[0].Relation, havilah[0].SiteType, havilah[0].RadiusMeters)
            .Should().Be(("representative-point", "in", "region", (int?)null));
        (havilah[1].Kind, havilah[1].Relation, havilah[1].RadiusMeters)
            .Should().Be(("center", "around", (int?)200_000));
    }

    /// <summary>
    /// The load writes every record's answers and its point, and a second load over the same files
    /// writes nothing; changed files replace what the first wrote.
    /// </summary>
    [Fact]
    public async Task TheLoadHoldsEveryAnswerAndASecondLoadWritesNothing()
    {
        var sinai = Entity("a-sinai", "Mount Sinai");
        var bether = Entity("a-bether", "Bether");
        await _db.SaveChangesAsync();
        var loader = new OpenBibleLocationLoader(_db, NullLogger<OpenBibleLocationLoader>.Instance);

        var first = await loader.Load(_folder);
        var second = await loader.Load(_folder);

        first.AlreadyLoaded.Should().BeFalse();
        (first.Located, first.Candidates, first.NoSite).Should().Be((1, 6, 1));
        second.AlreadyLoaded.Should().BeTrue();
        (await _db.PlaceLocations.Select(l => l.EntityId).ToListAsync()).Should().Equal(sinai.Id);
        (await _db.PlaceCandidates.CountAsync(c => c.EntityId == bether.Id)).Should().Be(2);

        Write(Place("a-sinai", Site("m-karkom", "Har Karkom", 600, "34.75,30.283333", "point", "mountain")));
        var third = await loader.Load(_folder);

        third.AlreadyLoaded.Should().BeFalse();
        _db.ChangeTracker.Clear();
        var location = await _db.PlaceLocations.SingleAsync();
        (location.EntityId, location.ModernId, location.Score).Should().Be((sinai.Id, "m-karkom", 600));
        (await _db.PlaceCandidates.Select(c => c.Site).ToListAsync()).Should().Equal("Har Karkom");
    }

    /// <summary>
    /// A place's page lists every answer, the likeliest marked, with the ancient place an answer
    /// takes it to be named and linked; the map carries the other sites only when asked, and a
    /// place whose likeliest answer is no site comes with them, named, so its sites can be.
    /// </summary>
    [Fact]
    public async Task ThePageListsEveryAnswerAndTheMapTheOtherSitesWhenAsked()
    {
        var babylon = Entity("a-babylon-2", "Babylon");
        babylon.Slug = "babylon-2";
        Entity("a-rome", "Rome");
        Entity("a-sinai", "Mount Sinai").Slug = "mount-sinai";
        Entity("a-bether", "Bether");
        await _db.SaveChangesAsync();
        await new OpenBibleLocationLoader(_db, NullLogger<OpenBibleLocationLoader>.Instance).Load(_folder);

        var answers = await EncyclopediaEndpoints.Candidates(_db, babylon.Id, null, CancellationToken.None);

        answers.Should().NotBeNull();
        answers!.Select(a => (a.Site, a.Likeliest, a.Confidence, a.Through?.Slug)).Should().Equal(
            ("Rome", true, 0.917, "rome"), ("Jerusalem", false, 0.022, null));

        var plain = await EncyclopediaEndpoints.Map(_db);
        plain.Variants.Should().BeNull();
        plain.Unlocated.Should().BeNull();

        var map = await EncyclopediaEndpoints.Map(_db, variants: true);
        map.Items.Select(i => i.Slug).Should().BeEquivalentTo("babylon-2", "mount-sinai");
        map.Variants!.Select(v => (v.Slug, v.Site)).Should().Equal(
            ("babylon-2", "Jerusalem"), ("bether", "Battir"), ("mount-sinai", "Har Karkom"),
            ("mount-sinai", "Jabal al Lawz"));
        map.Unlocated!.Select(u => u.Slug).Should().Equal("bether");
    }

    private OpenBibleLocationLoader.Reading Read() =>
        OpenBibleLocationLoader.Read(Path.Combine(_folder, "ancient.jsonl"), Path.Combine(_folder, "modern.jsonl"));

    private void Write(params string[] places)
    {
        File.WriteAllLines(Path.Combine(_folder, "ancient.jsonl"), places);
        File.WriteAllLines(Path.Combine(_folder, "modern.jsonl"),
        [
            Modern("m-musa"), Modern("m-karkom"), Modern("m-lawz"), Modern("m-bittir"), Modern("m-rome"),
            Modern("m-jerusalem"), Modern("m-babylonia"), Modern("m-sanaa"),
            """{"id":"m-osm","coordinates_source":{"type":"osm"},"geojson_roles":{}}""",
        ]);
    }

    private static string Modern(string id) =>
        $$$"""{"id":"{{{id}}}","coordinates_source":{"type":"wikidata"},"geojson_roles":{}}""";

    private sealed record Answer(
        string? Modern,
        string? Name,
        int Score,
        string? LonLat,
        string? Type,
        string? SiteType,
        string? Tags,
        string? Through,
        string? Relation,
        int? Radius,
        string? Special);

    private static Answer Site(
        string modern,
        string name,
        int score,
        string lonLat,
        string type,
        string siteType,
        string? tags = null,
        string? through = null,
        string? relation = null,
        int? radius = null) =>
        new(modern, name, score, lonLat, type, siteType, tags, through, relation, radius, null);

    private static Answer Special(string special, int score) =>
        new(null, null, score, null, null, null, null, null, null, null, special);

    /// <summary>
    /// One ancient place written the way the gazetteer writes it: an identification per answer,
    /// each site with one resolution and a modern association carrying the score.
    /// </summary>
    private static string Place(string id, params Answer[] answers)
    {
        var identifications = answers.Select(a =>
        {
            var identification = new Dictionary<string, object?>
            {
                ["id_source"] = a.Special is not null ? "special" : a.Through is not null ? "ancient" : "modern",
                ["score"] = new { time_total = a.Score },
                ["votes"] = new { tags = JsonSerializer.Deserialize<JsonElement>(a.Tags ?? "{}") },
            };
            if (a.Special is not null)
            {
                identification["special"] = a.Special;
                identification["resolutions"] = new[] { new { special = a.Special } };
                return identification;
            }

            identification["id"] = a.Through ?? a.Modern;
            if (a.Relation is not null)
            {
                identification["modifier"] = a.Relation;
            }

            if (a.Radius is not null)
            {
                identification["geometry_radius_meters"] = a.Radius;
            }

            identification["resolutions"] = new[]
            {
                new { lonlat = a.LonLat, lonlat_type = a.Type, modern_basis_id = a.Modern, type = a.SiteType },
            };
            return identification;
        }).ToList();

        var associations = answers
            .Select((a, i) => (a, i))
            .Where(x => x.a.Modern is not null)
            .ToDictionary(
                x => x.a.Modern!,
                x => new { identification_ids = new[] { new[] { x.i, 0 } }, name = x.a.Name, score = x.a.Score });

        return JsonSerializer.Serialize(new
        {
            id,
            friendly_id = id,
            identifications,
            modern_associations = associations,
        });
    }

    private Entity Entity(string openBibleId, string name)
    {
        var entity = new Entity
        {
            Kind = EntityKind.Place,
            Slug = name.ToLowerInvariant().Replace(' ', '-'),
            Name = name,
            OpenBibleId = openBibleId,
            SourceId = $"test:{openBibleId}",
            Source = "test",
            ExternalIds = { new RecordExternalId { Dataset = "openbible", ExternalId = openBibleId, Relation = RecordExternalRelations.SameAs } },
        };
        _db.Entities.Add(entity);
        return entity;
    }
}
