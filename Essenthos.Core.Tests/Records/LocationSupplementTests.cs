using System.Text.Json;
using System.Security.Cryptography;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Loading.Encyclopedia;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Essenthos.Core.Tests.Records;

[Trait("Category", "Corpus")]
[Collection(WitnessDatabaseCollection.Name)]
public sealed class LocationSupplementTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly string _folder = TestResources.Folder("OpenBible");
    private readonly LocationSupplement _supplement = LocationSupplement.Read();

    public LocationSupplementTests(WitnessDatabase database)
    {
        _db = database.NewContext();
        _db.Database.ExecuteSqlRaw("DELETE FROM entity");
    }

    public void Dispose() => _db.Dispose();

    private OpenBibleLocationLoader.Reading Reading() => OpenBibleLocationLoader.Read(
        Path.Combine(_folder, "ancient.jsonl"), Path.Combine(_folder, "modern.jsonl"));

    [Fact]
    public void NebosOriginalMissingPointIsReproducedThenTheSameCandidateIsLocated()
    {
        var source = Reading();
        var before = source.Candidates["aefaa2d"].ToArray();
        before[0].Site.Should().Be("Jabal al Naba");
        before[0].Withheld.Should().Be("openstreetmap");
        source.Located.Should().NotContain(p => p.PlaceId == "aefaa2d");
        var fixedReading = _supplement.Apply(source);
        var after = fixedReading.Candidates["aefaa2d"];
        after.Select(c => (c.Rank,c.Score,c.Site)).Should().Equal(before.Select(c => (c.Rank,c.Score,c.Site)));
        after.Skip(1).Should().Equal(before.Skip(1));
        after[0].Latitude.Should().BeApproximately(31 + 45/60.0 + 44/3600.0, 1e-10);
        after[0].Longitude.Should().BeApproximately(35 + 44/60.0 + 46/3600.0, 1e-10);
        after[0].CoordinatesSource.Should().Be("pcgn");
        after[0].CoordinatesDataset.Should().Be("pcgn");
        after[0].CoordinatesCredit.Should().Contain("PCGN").And.Contain("Open Government Licence");
        fixedReading.Located.Single(p => p.PlaceId == "aefaa2d").ModernId.Should().Be("m25a76c");
    }

    [Fact]
    public void AllOtherCandidateReadingsArePreservedIncludingUnknownPlaces()
    {
        var source = Reading();
        var before = source.Candidates.ToDictionary(p => p.Key,p => p.Value.ToArray());
        var patchedIds = _supplement.Points.Select(p => p.ModernId).ToHashSet();
        var result = _supplement.Apply(source);
        result.Candidates.Keys.Should().BeEquivalentTo(before.Keys);
        foreach (var (id, candidates) in result.Candidates)
            candidates.Where(c => !patchedIds.Contains(c.ModernId!)).Should()
                .Equal(before[id].Where(c => !patchedIds.Contains(c.ModernId!)));
        result.Located.Should().NotContain(p => p.PlaceId == "af3daeb", "the Garden/Eden uncertainty has not been solved by a coordinate patch");
    }

    [Fact]
    public void AnUnmatchedModernIdentityCannotReceiveAnotherSitesCoordinates()
    {
        var bad = _supplement with { Points = [new IndependentPoint("m25a76c","Rujm Siyaghah",35.7,31.7,"pcgn","test source")] };
        var act = () => bad.Apply(Reading());
        act.Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void IndependentCoordinatesAreBoundToTheActualHeldSources()
    {
        var sourceFolder = TestResources.Folder("PlaceLocationSources/2026-10-26");
        Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(Path.Combine(sourceFolder,"pcgn-jordan-2025.pdf"))))
            .Should().Be("b114fe09f4d593b6bd9bdd5cb3443b63f6564232a9109887d4899bcad2b86d16");
        var paper = File.ReadAllText(Path.Combine(sourceFolder,"drews-han-2010.xml"));
        var coordinate = paper.IndexOf("32.3543",StringComparison.Ordinal);
        coordinate.Should().BeGreaterThan(0);
        var rowStart = paper.LastIndexOf("<tr>",coordinate,StringComparison.Ordinal);
        var rowEnd = paper.IndexOf("</tr>",coordinate,StringComparison.Ordinal);
        paper[rowStart..rowEnd].Should().Contain("Tell Abu Sefeh").And.Contain("30.8606");
        var nileDeltaSite = _supplement.Points.Single(p=>p.ModernId=="m83f9b8");
        (nileDeltaSite.Lat,nileDeltaSite.Lon).Should().Be((30.8606,32.3543));
        var found = new HashSet<string>();
        foreach (var line in File.ReadLines(TestResources.Path("Wikidata","items.jsonl")))
        {
            using var doc = JsonDocument.Parse(line);
            var item = doc.RootElement;
            var id = item.GetProperty("id").GetString();
            if (id is not ("Q616837" or "Q2046004")) continue;
            found.Add(id);
            var point = item.GetProperty("claims").GetProperty("P625")[0].GetProperty("value");
            if (id=="Q616837")
            {
                item.GetProperty("lastrevid").GetInt64().Should().Be(2521611391);
                var patch = _supplement.Points.Single(p=>p.ModernId=="m5d9754");
                (patch.Lat,patch.Lon).Should().Be((point.GetProperty("lat").GetDouble(),point.GetProperty("lon").GetDouble()));
            }
            else
            {
                item.GetProperty("lastrevid").GetInt64().Should().Be(2549290202);
                var region = _supplement.Regions.Single(r=>r.Slug=="wilderness-of-judah");
                (region.Lat,region.Lon).Should().Be((point.GetProperty("lat").GetDouble(),point.GetProperty("lon").GetDouble()));
            }
        }
        found.Should().BeEquivalentTo(["Q616837","Q2046004"]);
    }

    [Fact]
    public async Task TheSupplementReachesTheDatabaseAndATwinWithoutTheGazetteersIdBorrowsNoPoint()
    {
        Place("shushan",null);
        Place("susa","a033b84");
        Place("mountnebo","aefaa2d");
        Place("wilderness-of-judah",null);
        await _db.SaveChangesAsync();
        var loader = new OpenBibleLocationLoader(_db,NullLogger<OpenBibleLocationLoader>.Instance);
        await loader.Load(_folder);
        var nebo = await _db.Entities.Include(e => e.Location).SingleAsync(e => e.Slug=="mountnebo");
        nebo.Location!.CoordinatesDataset.Should().Be("pcgn");
        nebo.Location.Source.Should().StartWith("OpenBible.info").And.Contain("PCGN");
        (await _db.Entities.Include(e => e.Location).SingleAsync(e => e.Slug=="susa")).Location.Should().NotBeNull();
        (await _db.Entities.Include(e => e.Location).SingleAsync(e => e.Slug=="shushan")).Location
            .Should().BeNull("a record is located by its own gazetteer id, never by its twin's");
        var region = await _db.Entities.Include(e=>e.Location).SingleAsync(e=>e.Slug=="wilderness-of-judah");
        region.Location!.Kind.Should().Be("representative-point");
        region.Location.Score.Should().BeNull("Wikidata supplies no scholarly agreement score");
        region.Location.CoordinatesDataset.Should().Be("wikidata");
        var before = await Fingerprint();
        (await loader.Load(_folder)).AlreadyLoaded.Should().BeTrue();
        (await Fingerprint()).Should().Equal(before);
    }

    private void Place(string slug,string? gazetteer) => _db.Entities.Add(new Entity
    {
        Slug=slug,Name=slug,Kind=EntityKind.Place,Source="test",SourceId="test:"+slug,OpenBibleId=gazetteer,
    });

    private async Task<string[]> Fingerprint() =>
    [
        .. await _db.PlaceLocations.AsNoTracking().OrderBy(l=>l.EntityId)
            .Select(l=>l.EntityId+"|"+l.ModernId+"|"+l.Longitude+"|"+l.Latitude+"|"+l.Score+"|"+l.CoordinatesSource+"|"+l.CoordinatesDataset+"|"+l.Source).ToArrayAsync(),
        .. await _db.PlaceCandidates.AsNoTracking().OrderBy(l=>l.EntityId).ThenBy(l=>l.Rank)
            .Select(l=>l.EntityId+"|"+l.Rank+"|"+l.ModernId+"|"+l.Longitude+"|"+l.Latitude+"|"+l.Score+"|"+l.CoordinatesSource+"|"+l.CoordinatesDataset+"|"+l.Source).ToArrayAsync(),
    ];
}
