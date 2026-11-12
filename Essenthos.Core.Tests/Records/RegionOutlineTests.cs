using System.Globalization;
using System.Text.Json;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Endpoints;
using Essenthos.Core.Loading.Encyclopedia;
using Essenthos.Core.Products;
using Essenthos.Core.Records;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace Essenthos.Core.Tests.Records;

/// <summary>
/// A region drawn by its shape: the gazetteer's own drawing of an area, or OpenStreetMap's outline of a
/// modern feature where it gives one, each owner's shapes in a file of its own and both in one table;
/// paths and the circles round a site are not shapes of an area and are not taken.
/// </summary>
[Collection(WitnessDatabaseCollection.Name)]
public sealed class RegionOutlineTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly string _resources;
    private readonly string _openBible;

    public RegionOutlineTests(WitnessDatabase database)
    {
        _db = database.NewContext();
        _resources = Path.Combine(Path.GetTempPath(), "essenthos-products", Guid.NewGuid().ToString("N"));
        _openBible = Path.Combine(_resources, "OpenBible");
        Directory.CreateDirectory(Path.Combine(_openBible, OpenBibleOutlines.Folder));
        Directory.CreateDirectory(Path.Combine(_resources, "Essenthos", "records", "facts"));

        File.WriteAllLines(Path.Combine(_openBible, "ancient.jsonl"),
        [
            Place("a-moab", "m-kerak", "35.7,31.18", "representative point", null, ("geometry", "gbands.isobands")),
            Place("a-caphtor", "m-cyprus", "33.2,35.0", "representative point", null,
                ("geometry", "grough.geometry"), ("precise", "gosm.geometry")),
            Place("a-way", "m-road", "35.5,31.0", "representative point", null, ("geometry", "gpath.geometry")),
            Place("a-havilah", "m-sanaa", "44.2,15.4", "center", ">", ("center", "m-sanaa.200000m"), ("geometry", "gcircle.geometry")),
            Place("a-gibeah", "m-hebron", "35.1,31.5", "representative point", "<", ("geometry", "gprob.geometry")),
        ]);
        File.WriteAllLines(Path.Combine(_openBible, "modern.jsonl"),
        [
            .. new[] { "m-kerak", "m-cyprus", "m-road", "m-sanaa", "m-hebron" }
                .Select(id => $$$"""{"id":"{{{id}}}","coordinates_source":{"type":"wikidata"},"geojson_roles":{}}"""),
        ]);
        File.WriteAllLines(Path.Combine(_openBible, OpenBibleOutlines.IndexFile),
        [
            """{"format":"kml","geometry":"isobands","id":"gbands","isobands_geojson_file":"gbands.isobands.geojson"}""",
            """{"format":"kml","geometry":"rough_boundary","id":"grough","geojson_file":"grough.geometry.geojson"}""",
            """{"format":"osm","geometry":"polygon","id":"gosm","geojson_file":"gosm.geometry.geojson","osm_version":3}""",
            """{"format":"kml","geometry":"rough_path","id":"gpath","geojson_file":"gpath.geometry.geojson"}""",
            """{"format":"kml","geometry":"probability","id":"gprob","geojson_file":"gprob.geometry.geojson"}""",
        ]);
        Shape("gbands.isobands.geojson", "MultiPolygon",
            $"[[{Square(35.7, 31.2, 0.6)}],[{Square(35.7, 31.2, 0.4)}],[{Square(35.7, 31.2, 0.2)}]]",
            """{"min_confidence":10,"max_confidence":90}""");
        Shape("grough.geometry.geojson", "Polygon", $"[{Square(33.2, 35.0, 1.5)}]");
        Shape("gosm.geometry.geojson", "Polygon", $"[{Circle(33.2, 35.0, 0.8, 400)}]");
        Shape("gpath.geometry.geojson", "LineString", "[[35.4,31.0],[35.6,31.1]]");
        Shape("gprob.geometry.geojson", "Polygon", $"[{Square(35.1, 31.5, 0.05)}]");

        var records = Path.Combine(_resources, "Essenthos", "records");
        File.WriteAllLines(Path.Combine(records, "records.tsv"),
        [
            "slug\tkind\tsubtype\tplace_kind\tname\tsex\tsex_source\tname_gender\ttribe\torigin\tanother_name_for\tbyname",
            .. Slugs.Select(s => $"{s}\tplace\t\\N\t\\N\t{s}\t\\N\t\\N\t\\N\t\\N\t\\N\t\\N\t\\N"),
        ]);
        File.WriteAllLines(Path.Combine(records, "external-ids.tsv"),
        [
            "slug\tdataset\texternal_id\trelation\tnote",
            .. Slugs.Select(s => $"{s}\topenbible\ta-{s}\tsame-as\t\\N"),
        ]);
        File.WriteAllLines(Path.Combine(records, "facts", "location-supplements.tsv"),
            ["modern_id\tdataset\tsite\tlon\tlat\tregion_of\tcredit\tby"]);
    }

    private static readonly string[] Slugs = ["caphtor", "gibeah", "havilah", "moab", "way"];

    public void Dispose()
    {
        _db.PlaceCandidates.ExecuteDelete();
        _db.PlaceLocations.ExecuteDelete();
        _db.PlaceOutlines.ExecuteDelete();
        _db.RecordExternalIds.ExecuteDelete();
        _db.Entities.ExecuteDelete();
        _db.Dispose();
        Directory.Delete(_resources, recursive: true);
    }

    [Fact]
    public void TheIndexTakesTheAreasAndLeavesThePaths()
    {
        var index = Index();

        index.Values.Select(e => (e.Id, e.Kind, e.OpenStreetMap)).Should().BeEquivalentTo(new[]
        {
            ("gbands", "confidence-bands", false), ("grough", "rough-boundary", false), ("gosm", "precise", true),
            ("gprob", "probable-area", false),
        });
    }

    /// <summary>
    /// Cyprus is drawn by OpenStreetMap's coast, not by the gazetteer's boundary round it, which only
    /// says where a label would go; a road and a circle round a site are no shapes of an area.
    /// </summary>
    [Fact]
    public void AnAnswerIsDrawnByOpenStreetMapsOutlineWhereGivenElseByTheGazetteersOwn()
    {
        var reading = OpenBibleLocationLoader.Read(
            Path.Combine(_openBible, "ancient.jsonl"), Path.Combine(_openBible, "modern.jsonl"), Index());

        reading.Candidates.ToDictionary(p => p.Key, p => p.Value.Single().Outline).Should().Equal(new Dictionary<string, string?>
        {
            ["a-moab"] = "gbands", ["a-caphtor"] = "gosm", ["a-way"] = null, ["a-havilah"] = null, ["a-gibeah"] = "gprob",
        });
    }

    [Fact]
    public void BandsAreSpreadFromTheLeastToTheMostSure()
    {
        var index = Index();

        var bands = OpenBibleOutlines.Read(Path.Combine(_openBible, OpenBibleOutlines.Folder, "gbands.isobands.geojson"), index["gbands"]);

        bands.Select(b => b.Confidence).Should().Equal(10, 50, 90);
        OpenBibleOutlines.Read(Path.Combine(_openBible, OpenBibleOutlines.Folder, "grough.geometry.geojson"), index["grough"])
            .Should().ContainSingle().Which.Confidence.Should().BeNull();
    }

    [Fact]
    public void ASimplifiedRingStaysClosedAndWithinItsTolerance()
    {
        var ring = Enumerable.Range(0, 401).Select(i => CirclePoint(33.2, 35.0, 0.8, 400, i)).ToList();

        foreach (var tolerance in new[] { OpenBibleOutlines.FineTolerance, OpenBibleOutlines.CoarseTolerance })
        {
            var simple = OpenBibleOutlines.Simplify(ring, tolerance);

            simple.Count.Should().BeLessThan(ring.Count).And.BeGreaterThan(3);
            simple[0].Should().Be(simple[^1]);
            ring.Max(p => Enumerable.Range(0, simple.Count - 1).Min(i => FromSegment(p, simple[i], simple[i + 1])))
                .Should().BeLessThanOrEqualTo(tolerance + 1e-12);
        }

        OpenBibleOutlines.Simplify(ring, OpenBibleOutlines.CoarseTolerance).Count
            .Should().BeLessThan(OpenBibleOutlines.Simplify(ring, OpenBibleOutlines.FineTolerance).Count);
    }

    /// <summary>
    /// The producer writes the gazetteer's shapes and OpenStreetMap's to their own files, each saying
    /// its licence; the load puts both in the one table, and the API draws a place by its likeliest
    /// answer's shape, except where that answer only puts it somewhere inside a wider land.
    /// </summary>
    [Fact]
    public async Task TheTwoOwnersShapesGoToTwoFilesAndLoadIntoOneTable()
    {
        foreach (var slug in Slugs)
        {
            _db.Entities.Add(new Entity
            {
                Kind = EntityKind.Place,
                Slug = slug,
                Name = slug,
                OpenBibleId = $"a-{slug}",
                SourceId = $"test:{slug}",
                Source = "test",
            });
        }

        await _db.SaveChangesAsync();

        var store = new ProductStore(_resources);
        var written = RecordProducers.Locations(store, store, _openBible).ToDictionary(w => w.Product, w => w.Rows);
        store.Save();

        written[RecordProducers.OwnOutlines].Should().Be(2);
        written[RecordProducers.OpenStreetMapOutlines].Should().Be(1);
        Ids(store, RecordProducers.OwnOutlines).Should().Equal("gbands", "gprob");
        Ids(store, RecordProducers.OpenStreetMapOutlines).Should().Equal("gosm");
        using (var reader = store.Open(RecordProducers.OpenStreetMapOutlines, RecordProducers.File(RecordProducers.OpenStreetMapOutlines).Schema()))
        {
            reader.Header!.Notes.Should().Contain(n => n.Key == "licence" && n.Value.StartsWith("ODbL 1.0", StringComparison.Ordinal));
        }

        var files = RecordFiles.All
            .Where(f => f.Product is "records/facts/locations" or "records/facts/location-candidates"
                        || f.Product.StartsWith("records/facts/region-outlines", StringComparison.Ordinal))
            .ToList();
        await using (var connection = new NpgsqlConnection(_db.Database.GetConnectionString()))
        {
            await connection.OpenAsync();
            await using var transaction = await connection.BeginTransactionAsync();
            await RecordFiles.LoadFiles(connection, transaction, store, files, default);
            await transaction.CommitAsync();
        }

        var coarse = await EncyclopediaEndpoints.Outlines(_db, fine: false, null, default);
        coarse.Items.Select(o => (o.Id, o.Kind, o.By)).Should().Equal(
            ("gbands", "confidence-bands", "openbible"), ("gosm", "precise", "openstreetmap"), ("gprob", "probable-area", "openbible"));
        coarse.Items[0].Bands.Select(b => b.Confidence).Should().Equal(10, 50, 90);
        coarse.Items[1].Box.Should().HaveCount(4);

        var fine = await EncyclopediaEndpoints.Outlines(_db, fine: true, ["gosm"], default);
        fine.Items.Should().ContainSingle();
        fine.Items[0].Bands[0].Rings[0].Length.Should().BeGreaterThan(coarse.Items[1].Bands[0].Rings[0].Length);

        var map = await EncyclopediaEndpoints.Map(_db, variants: true);
        map.Items.ToDictionary(i => i.Slug, i => i.Outline).Should().Equal(new Dictionary<string, string?>
        {
            ["caphtor"] = "gosm", ["gibeah"] = null, ["havilah"] = null, ["moab"] = "gbands", ["way"] = null,
        });

        var gibeah = await _db.Entities.SingleAsync(e => e.Slug == "gibeah");
        var answers = await EncyclopediaEndpoints.Candidates(_db, gibeah.Id, null, default);
        answers!.Single().Outline.Should().Be("gprob");
    }

    private Dictionary<string, OpenBibleOutlines.Entry> Index() =>
        OpenBibleOutlines.Index(Path.Combine(_openBible, OpenBibleOutlines.IndexFile));

    private static List<string> Ids(ProductStore store, string product)
    {
        using var reader = store.Open(product, RecordProducers.File(product).Schema());
        return [.. reader.ReadAll().Select(r => r[0]!)];
    }

    private void Shape(string file, string type, string coordinates, string properties = "{}") =>
        File.WriteAllText(Path.Combine(_openBible, OpenBibleOutlines.Folder, file),
            $$$"""{"type":"Feature","properties":{{{properties}}},"geometry":{"type":"{{{type}}}","coordinates":{{{coordinates}}}}}""");

    private static string Square(double lon, double lat, double half) =>
        string.Create(CultureInfo.InvariantCulture,
            $"[[{lon - half},{lat - half}],[{lon + half},{lat - half}],[{lon + half},{lat + half}],[{lon - half},{lat + half}],[{lon - half},{lat - half}]]");

    private static string Circle(double lon, double lat, double radius, int steps) =>
        "[" + string.Join(",", Enumerable.Range(0, steps + 1).Select(i => CirclePoint(lon, lat, radius, steps, i))
            .Select(p => string.Create(CultureInfo.InvariantCulture, $"[{p.Lon},{p.Lat}]"))) + "]";

    private static (double Lon, double Lat) CirclePoint(double lon, double lat, double radius, int steps, int i)
    {
        var angle = 2 * Math.PI * (i % steps) / steps;
        return (lon + (radius * Math.Cos(angle)), lat + (radius * Math.Sin(angle)));
    }

    private static double FromSegment((double Lon, double Lat) p, (double Lon, double Lat) a, (double Lon, double Lat) b)
    {
        var (dx, dy) = (b.Lon - a.Lon, b.Lat - a.Lat);
        var length = (dx * dx) + (dy * dy);
        var t = length == 0 ? 0 : Math.Clamp((((p.Lon - a.Lon) * dx) + ((p.Lat - a.Lat) * dy)) / length, 0, 1);
        var (x, y) = (a.Lon + (t * dx) - p.Lon, a.Lat + (t * dy) - p.Lat);
        return Math.Sqrt((x * x) + (y * y));
    }

    /// <summary>A place with one answer, a site whose one resolution carries the drawn roles given.</summary>
    private static string Place(string id, string modern, string lonLat, string type, string? modifier, params (string Role, string Feature)[] roles)
    {
        var identification = new Dictionary<string, object?>
        {
            ["id_source"] = "modern",
            ["id"] = modern,
            ["score"] = new { time_total = 500 },
            ["resolutions"] = new[]
            {
                new
                {
                    lonlat = lonLat,
                    lonlat_type = type,
                    modern_basis_id = modern,
                    type = "region",
                    geojson_roles = roles.ToDictionary(r => r.Role, r => new { id = r.Feature }),
                },
            },
        };
        if (modifier is not null)
        {
            identification["modifier"] = modifier;
        }

        return JsonSerializer.Serialize(new
        {
            id,
            friendly_id = id,
            identifications = new[] { identification },
            modern_associations = new Dictionary<string, object>
            {
                [modern] = new { identification_ids = new[] { new[] { 0, 0 } }, name = modern, score = 500 },
            },
        });
    }
}
