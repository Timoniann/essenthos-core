using Essenthos.Core.Database.Entities;
using Essenthos.Core.Loading.Encyclopedia;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Essenthos.Core.Tests.Records;

/// <summary>
/// A region the gazetteer stands on a town's point stands inside its own drawn extent on the map
/// (PRB-1438): Judea is not one spot under Jerusalem's dot. A place that really stands in the town keeps
/// the town's point, and so does a region whose extent nobody drew.
/// </summary>
public sealed class RegionPointsTests(ITestOutputHelper output)
{
    private static readonly OpenBibleOutlines.Band Square =
        new(null, [[(35.0, 31.0), (36.0, 31.0), (36.0, 32.0), (35.0, 32.0), (35.0, 31.0)]]);

    private static PlaceLocation Location(int entity, string kind, string modern, double lon = 35.2, double lat = 31.7) =>
        new() { EntityId = entity, Longitude = lon, Latitude = lat, Kind = kind, ModernId = modern, CoordinatesSource = "test", Source = "test" };

    private static PlaceCandidate Answer(int entity, string modern, string? siteType, string? outline) =>
        new() { EntityId = entity, Rank = 1, ModernId = modern, Site = "Town", SiteType = siteType, OutlineId = outline, Source = "test" };

    [Fact]
    public void ARegionOnATownsPointMovesInsideItsExtentAndTheTownStays()
    {
        List<PlaceLocation> locations =
        [
            Location(1, "point", "m-town"),
            Location(2, "representative-point", "m-town"),
            Location(3, "settlement", "m-town"),
        ];
        var candidates = new[]
        {
            Answer(1, "m-town", "settlement", null),
            Answer(2, "m-town", "region", "g-land"),
            Answer(3, "m-town", "structure", null),
        };

        var moved = RegionPoints.Move(locations, candidates, id => id == "g-land" ? [Square] : null);

        moved.Should().ContainSingle().Which.EntityId.Should().Be(2);
        locations[1].Longitude.Should().BeApproximately(35.5, 0.03);
        locations[1].Latitude.Should().BeApproximately(31.5, 0.03);
        locations[1].Kind.Should().Be("representative-point");
        locations[0].Longitude.Should().Be(35.2);
        locations[2].Longitude.Should().Be(35.2);
    }

    [Fact]
    public void ARegionWithNoDrawnExtentOrOnASitesOwnPointStays()
    {
        List<PlaceLocation> locations =
        [
            Location(1, "point", "m-town"),
            Location(2, "representative-point", "m-town"),
            Location(3, "representative-point", "m-land", 36.0, 33.0),
            Location(4, "representative-point", "m-town"),
        ];
        var candidates = new[]
        {
            Answer(1, "m-town", "settlement", null),
            Answer(2, "m-town", "region", null),
            Answer(3, "m-land", "region", "g-land"),
            Answer(4, "m-town", "region", "g-probable"),
        };

        var moved = RegionPoints.Move(locations, candidates, id => id == "g-land" ? [Square] : null);

        moved.Should().BeEmpty();
        locations.Select(l => l.Longitude).Should().Equal(35.2, 35.2, 36.0, 35.2);
    }

    [Fact]
    public void TheMiddleOfABentLandIsInsideIt()
    {
        // An L: the box's centre (35.75, 31.75) is outside it.
        var bent = new OpenBibleOutlines.Band(null,
            [[(35.0, 31.0), (36.5, 31.0), (36.5, 31.5), (35.5, 31.5), (35.5, 32.5), (35.0, 32.5), (35.0, 31.0)]]);

        var (lon, lat) = RegionPoints.Middle([bent])!.Value;

        (lon < 35.5 || lat < 31.5).Should().BeTrue($"({lon}, {lat}) lies in one arm of the L");
    }

    [Fact]
    public void TheBandAboutAsSureAsNotIsTheOneMeasured()
    {
        var wide = new OpenBibleOutlines.Band(10, [[(30.0, 20.0), (40.0, 20.0), (40.0, 30.0), (30.0, 30.0), (30.0, 20.0)]]);
        var likely = new OpenBibleOutlines.Band(50, [[(30.0, 20.0), (32.0, 20.0), (32.0, 22.0), (30.0, 22.0), (30.0, 20.0)]]);

        var (lon, lat) = RegionPoints.Middle([wide, likely])!.Value;

        lon.Should().BeApproximately(31.0, 0.1);
        lat.Should().BeApproximately(21.0, 0.1);
    }

    /// <summary>
    /// The real gazetteer: Judea, Beyond the River, Babylonia and the Negev leave their towns' points
    /// for points inside their own extents; Jerusalem, Babylon and Beersheba keep theirs.
    /// </summary>
    [Fact]
    [Trait(TestCategory.Name, TestCategory.Corpus)]
    public void TheRealRegionsLeaveTheirTownsPoints()
    {
        var folder = TestResources.OpenBibleFolder;
        var index = OpenBibleOutlines.Index(Path.Combine(folder, OpenBibleOutlines.IndexFile));
        var read = OpenBibleLocationLoader.Read(Path.Combine(folder, "ancient.jsonl"), Path.Combine(folder, "modern.jsonl"), index);
        var ids = read.Located.Select(l => l.PlaceId).Concat(read.Candidates.Keys).Distinct().Order(StringComparer.Ordinal).ToList();
        var places = ids.Select((id, i) => new OpenBibleLocationLoader.Place(i, id, id)).ToList();
        var (locations, candidates, _) = OpenBibleLocationLoader.Locate(read, new LocationSupplement([], []), places);
        var before = locations.ToDictionary(l => l.EntityId, l => (l.Longitude, l.Latitude));

        var moved = RegionPoints.Move(locations, candidates, id =>
            index.TryGetValue(id, out var entry) && entry.Kind != "probable-area"
                ? OpenBibleOutlines.Read(Path.Combine(folder, OpenBibleOutlines.Folder, entry.File), entry)
                : null);

        foreach (var (entity, fromLon, fromLat, lon, lat) in moved)
        {
            output.WriteLine($"{places[entity].Slug}\t{fromLon},{fromLat}\t->\t{lon},{lat}");
        }

        int Of(string openBibleId) => places.Single(p => p.OpenBibleId == openBibleId).Id;
        var judea = Of("a149f13");
        moved.Should().Contain(m => m.EntityId == judea);
        var point = locations.Single(l => l.EntityId == judea);
        point.Longitude.Should().BeInRange(34.48, 35.65);
        point.Latitude.Should().BeInRange(31.21, 33.1);
        (point.Longitude, point.Latitude).Should().NotBe(before[judea]);
        locations.Where(l => l.Kind != "representative-point")
            .Count(l => before[l.EntityId].Longitude != l.Longitude || before[l.EntityId].Latitude != l.Latitude)
            .Should().Be(0);
    }
}
