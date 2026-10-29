using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Loading.Encyclopedia;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Essenthos.Core.Tests.Records;

/// <summary>
/// The shipped location supplement on a gazetteer of four lines: Mount Nebo, whose likeliest site's
/// point is OpenStreetMap's and is measured by the UK's names committee instead; Susa, which the
/// gazetteer places and Strong's Shushan beside it does not hold; and the Wilderness of Judah, which
/// the gazetteer does not place and Wikidata does, with no score.
/// </summary>
[Collection(WitnessDatabaseCollection.Name)]
public sealed class LocationSupplementFixtureTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly string _folder = Directory.CreateTempSubdirectory("essenthos-locations-").FullName;

    public LocationSupplementFixtureTests(WitnessDatabase database)
    {
        _db = database.NewContext();
        _db.Database.ExecuteSqlRaw("DELETE FROM entity");
        File.WriteAllLines(Path.Combine(_folder, "ancient.jsonl"),
        [
            """{"id":"aefaa2d","friendly_id":"Mount Nebo","identifications":[{"id_source":"modern","resolutions":[{"lonlat":"35.72,31.77","lonlat_type":"point","modern_basis_id":"m25a76c","type":"mountain"}]},{"id_source":"modern","resolutions":[{"lonlat":"35.70,31.76","lonlat_type":"point","modern_basis_id":"m0000aa","type":"mountain"}]}],"modern_associations":{"m25a76c":{"name":"Jabal al Naba","score":812,"identification_ids":[[0,0]]},"m0000aa":{"name":"Rujm Siyaghah","score":40,"identification_ids":[[1,0]]}}}""",
            """{"id":"a033b84","friendly_id":"Susa","identifications":[{"id_source":"modern","resolutions":[{"lonlat":"48.25778,32.18922","lonlat_type":"point","modern_basis_id":"m0000bb","type":"settlement"}]}],"modern_associations":{"m0000bb":{"name":"Shush","score":1000,"identification_ids":[[0,0]]}}}""",
        ]);
        File.WriteAllLines(Path.Combine(_folder, "modern.jsonl"),
        [
            """{"id":"m25a76c","coordinates_source":{"type":"osm"}}""",
            """{"id":"m0000aa","coordinates_source":{"type":"wikidata"}}""",
            """{"id":"m0000bb","coordinates_source":{"type":"wikidata"}}""",
        ]);
    }

    public void Dispose()
    {
        _db.Database.ExecuteSqlRaw("DELETE FROM entity");
        _db.Dispose();
        Directory.Delete(_folder, recursive: true);
    }

    private OpenBibleLocationLoader Loader() => new(_db, NullLogger<OpenBibleLocationLoader>.Instance);

    private async Task Places()
    {
        foreach (var (slug, gazetteer) in new[]
                 {
                     ("mountnebo", "aefaa2d"), ("susa", "a033b84"), ("shushan", (string?)null), ("wilderness-of-judah", null),
                 })
        {
            _db.Entities.Add(new Entity
            {
                Slug = slug, Name = slug, Kind = EntityKind.Place, Source = "test", SourceId = "test:" + slug,
                OpenBibleId = gazetteer,
            });
        }

        await _db.SaveChangesAsync();
    }

    [Fact]
    public async Task APointAnotherSourceMeasuredIsCreditedToItAndKeepsTheGazetteersScore()
    {
        await Places();
        await Loader().Load(_folder);

        var nebo = await _db.Entities.Include(e => e.Location).Include(e => e.Candidates).SingleAsync(e => e.Slug == "mountnebo");
        nebo.Location!.Score.Should().Be(812);
        nebo.Location.CoordinatesSource.Should().Be("pcgn");
        nebo.Location.CoordinatesDataset.Should().Be("pcgn");
        nebo.Location.Latitude.Should().BeApproximately(31 + 45 / 60.0 + 44 / 3600.0, 1e-10);
        nebo.Location.Source.Should().StartWith("OpenBible.info").And.Contain("Open Government Licence");

        var other = nebo.Candidates.Single(c => c.Rank == 1);
        other.CoordinatesDataset.Should().BeNull("the gazetteer's own point is the gazetteer's");
        other.Source.Should().NotContain("coordinates:");
    }

    [Fact]
    public async Task ARegionTheSourceScoresNotHasNoScoreRatherThanZero()
    {
        await Places();
        await Loader().Load(_folder);

        var region = await _db.Entities.Include(e => e.Location).Include(e => e.Candidates)
            .SingleAsync(e => e.Slug == "wilderness-of-judah");
        region.Location!.Score.Should().BeNull();
        region.Location.CoordinatesDataset.Should().Be("wikidata");
        region.Candidates.Should().ContainSingle().Which.Score.Should().BeNull();
    }

    [Fact]
    public async Task ARecordWithoutTheGazetteersIdBorrowsNoPointFromItsTwin()
    {
        await Places();
        await Loader().Load(_folder);

        (await _db.PlaceLocations.CountAsync(l => l.Entity!.Slug == "susa")).Should().Be(1);
        (await _db.PlaceLocations.CountAsync(l => l.Entity!.Slug == "shushan")).Should().Be(0);
        (await _db.PlaceCandidates.CountAsync(c => c.Entity!.Slug == "shushan")).Should().Be(0);
    }

    [Fact]
    public async Task ASecondLoadWritesNothing()
    {
        await Places();
        (await Loader().Load(_folder)).AlreadyLoaded.Should().BeFalse();
        var before = await Fingerprint();

        (await Loader().Load(_folder)).AlreadyLoaded.Should().BeTrue();
        (await Fingerprint()).Should().Equal(before);
    }

    private async Task<string[]> Fingerprint() =>
    [
        .. await _db.PlaceLocations.AsNoTracking().OrderBy(l => l.EntityId)
            .Select(l => l.EntityId + "|" + l.Score + "|" + l.CoordinatesDataset + "|" + l.Source).ToArrayAsync(),
        .. await _db.PlaceCandidates.AsNoTracking().OrderBy(c => c.EntityId).ThenBy(c => c.Rank)
            .Select(c => c.EntityId + "|" + c.Rank + "|" + c.Score + "|" + c.CoordinatesDataset + "|" + c.Source).ToArrayAsync(),
    ];
}
