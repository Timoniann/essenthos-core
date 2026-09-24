using System.Text.Json;
using Essenthos.Core.Database;
using Essenthos.Core.Endpoints;
using Essenthos.Core.Loading.Encyclopedia;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Essenthos.Core.Tests;

/// <summary>
/// The periods of the lands, taken from PeriodO as the source gives them: each under its own
/// authority, with its own ends, and in the land most of its places belong to.
/// </summary>
[Collection(WitnessDatabaseCollection.Name)]
public sealed class PeriodOLoaderTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly IDbContextTransaction _transaction;

    public PeriodOLoaderTests(WitnessDatabase database)
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

    [Theory]
    [InlineData("""{"in":{"year":"-1549"}}""", -1549, -1549)]
    [InlineData("""{"in":{"year":"0030"}}""", 30, 30)]
    [InlineData("""{"in":{"earliestYear":"-1250","latestYear":"-1175"}}""", -1250, -1175)]
    public void ReadsAnEndAsTheSourceWritesIt(string end, int earliest, int latest)
    {
        using var document = JsonDocument.Parse(end);
        PeriodOLoader.Bounds(document.RootElement).Should().Be(new PeriodOLoader.Range(earliest, latest));
    }

    [Fact]
    public void LeavesAnEndWithOnlyOneBoundOpen()
    {
        using var document = JsonDocument.Parse("""{"in":{"earliestYear":"-1250"}}""");
        PeriodOLoader.Bounds(document.RootElement).Should().BeNull();
    }

    [Fact]
    public void DrawsAPeriodInTheLandMostOfItsPlacesBelongTo()
    {
        // King and Stager's Early Bronze: Israel, Egypt, Jordan, Lebanon, Syria, Cyprus, Turkey.
        PeriodOLoader.RegionOf(["Q801", "Q79", "Q810", "Q822", "Q858", "Q229", "Q43"]).Should().Be("levant");
        PeriodOLoader.RegionOf(["Q79"]).Should().Be("egypt");
        PeriodOLoader.RegionOf(["Q41", "Q34374"]).Should().Be("aegean");
    }

    [Fact]
    public void LeavesOutAPeriodThatIsMostlyElsewhere()
    {
        // Greece among France, Germany and Italy is a period of Europe, not of the Aegean.
        PeriodOLoader.RegionOf(["Q41", "Q142", "Q183", "Q38"]).Should().BeNull();
        PeriodOLoader.RegionOf(["Q229"]).Should().BeNull();
    }

    [Fact]
    public void TakesTheLandTheSourceNamesOverTheOneWithTheMostPlaces()
    {
        // EAMENA's Middle Hittite Anatolia lists Iraq, Syria and Turkey; its own words say Anatolia.
        PeriodOLoader.RegionOf(["Q796", "Q858", "Q43"], "Anatolia").Should().Be("anatolia");
        PeriodOLoader.RegionOf(["Q796", "Q858", "Q43"], "Ancient Mesopotamia").Should().Be("mesopotamia");
        PeriodOLoader.RegionOf(["Q801", "Q810"], "Southern Levant").Should().Be("levant");
        // Words naming several lands decide nothing, and the places do.
        PeriodOLoader.RegionOf(["Q794", "Q858", "Q822", "Q43"], "Persia, Syria, Phoenicia, Asia Minor")
            .Should().Be("levant");
    }

    [Fact]
    public void GivesATieToTheLandListedFirst() =>
        PeriodOLoader.RegionOf(["Q79", "Q801"]).Should().Be("levant");

    [Theory]
    [InlineData(new[] { "King, Philip J.", "Stager, Lawrence E." }, "Life in biblical Israel", 2001,
        "King & Stager, Life in biblical Israel (2001)")]
    [InlineData(new[] { "Mazar, Amihai, (1942- ).," }, "Archaeology of the land of the Bible", 1992,
        "Mazar, Archaeology of the land of the Bible (1992)")]
    [InlineData(new[] { "Bagnall", "Talbert", "Bond" }, "Pleiades: A community-built gazetteer", 2025,
        "Bagnall et al., Pleiades (2025)")]
    [InlineData(new[] { "Gates, Charles, 1950-" }, "Ancient cities : the archaeology of urban life", 2003,
        "Gates, Ancient cities (2003)")]
    [InlineData(new string[0], "British Museum", null, "British Museum")]
    public void AttributesAPeriodTheWayADateIsFollowed(string[] creators, string title, int? year, string expected) =>
        PeriodOLoader.Attributed(creators, title, year).Should().Be(expected);

    [Fact]
    public void TakesThePeriodsOfTheLandsAndSaysWhatItLeftOut()
    {
        using var document = JsonDocument.Parse(Dataset);
        var reading = PeriodOLoader.Read(document.RootElement);

        reading.Authorities.Should().ContainSingle().Which.PeriodoId.Should().Be("p0test1");
        var periods = reading.Authorities[0].Periods.ToList();
        periods.Select(p => p.PeriodoId).Should().BeEquivalentTo(["p0test1lb", "p0test1iron"]);

        var iron = periods.Single(p => p.PeriodoId == "p0test1iron");
        iron.StopEarliest.Should().Be(-600);
        iron.StopLatest.Should().Be(-586);
        iron.Labels.Should().Be("""{"de":"Eisenzeit","en":"Iron Age"}""");
        iron.Region.Should().Be("levant");
        iron.CoverageDescription.Should().Be("Southern Levant");

        reading.Outcome.OutsideTheWindow.Should().Be(1);
        reading.Outcome.Elsewhere.Should().Be(1);
        reading.Outcome.Unplaced.Should().Be(1);
        reading.Outcome.OpenEnded.Should().Be(1);
        reading.Outcome.Licensed.Should().Be(1);
    }

    [Fact]
    public async Task LoadsOnceAndServesEachAuthoritysDatesUnresolved()
    {
        var resources = Directory.CreateTempSubdirectory("periodo-");
        try
        {
            Directory.CreateDirectory(Path.Combine(resources.FullName, "PeriodO"));
            await File.WriteAllTextAsync(Path.Combine(resources.FullName, "PeriodO", "d.json"), Dataset);
            var loader = new PeriodOLoader(_db, NullLogger<PeriodOLoader>.Instance);

            var first = await loader.Load(resources.FullName);
            var second = await loader.Load(resources.FullName);

            first.AlreadyLoaded.Should().BeFalse();
            first.Periods.Should().Be(2);
            second.AlreadyLoaded.Should().BeTrue();
            (await _db.PeriodDefinitions.CountAsync()).Should().Be(2);

            var served = await LandPeriodEndpoints.All(_db, default);
            served.Authorities.Should().ContainSingle().Which.Attribution
                .Should().Be("King & Stager, Life in biblical Israel (2001)");
            var bronze = served.Periods.Single(p => p.Id == "p0test1lb");
            bronze.Start.Should().Equal(-1549, -1549);
            bronze.Stop.Should().Equal(-1199, -1199);
            bronze.StartLabel.Should().Be("1550 B.C.E.");
            bronze.Labels.Should().ContainKey("en").WhoseValue.Should().Be("Late Bronze");
            bronze.Uri.Should().Be("http://n2t.net/ark:/99152/p0test1lb");
        }
        finally
        {
            resources.Delete(recursive: true);
        }
    }

    /// <summary>
    /// Two periods to take, and one of every kind to leave: before the window, of Europe, placed
    /// nowhere, open at one end, and under an authority that states its own licence.
    /// </summary>
    private const string Dataset = """
        {
          "authorities": {
            "p0test1": {
              "source": {
                "partOf": {
                  "title": "Life in biblical Israel",
                  "yearPublished": 2001,
                  "creators": [{ "name": "King, Philip J." }, { "name": "Stager, Lawrence E." }]
                },
                "locator": "page xxiii"
              },
              "periods": {
                "p0test1lb": {
                  "label": "Late Bronze", "languageTag": "en",
                  "localizedLabels": { "en": ["Late Bronze"] },
                  "spatialCoverage": [{ "id": "http://www.wikidata.org/entity/Q801", "label": "Israel" }],
                  "start": { "in": { "year": "-1549" }, "label": "1550 B.C.E." },
                  "stop": { "in": { "year": "-1199" }, "label": "1200 B.C.E." }
                },
                "p0test1iron": {
                  "label": "Iron Age", "languageTag": "en",
                  "localizedLabels": { "en": ["Iron Age"], "de": ["Eisenzeit"] },
                  "spatialCoverageDescription": "Southern Levant",
                  "spatialCoverage": [{ "id": "http://www.wikidata.org/entity/Q810", "label": "Jordan" }],
                  "start": { "in": { "year": "-1199" }, "label": "1200 B.C.E." },
                  "stop": { "in": { "earliestYear": "-600", "latestYear": "-586" }, "label": "ca. 600" }
                },
                "p0test1neo": {
                  "label": "Neolithic",
                  "spatialCoverage": [{ "id": "http://www.wikidata.org/entity/Q801", "label": "Israel" }],
                  "start": { "in": { "year": "-9999" } }, "stop": { "in": { "year": "-4499" } }
                },
                "p0test1eu": {
                  "label": "Bronze Age",
                  "spatialCoverage": [
                    { "id": "http://www.wikidata.org/entity/Q41", "label": "Greece" },
                    { "id": "http://www.wikidata.org/entity/Q142", "label": "France" },
                    { "id": "http://www.wikidata.org/entity/Q183", "label": "Germany" }
                  ],
                  "start": { "in": { "year": "-2999" } }, "stop": { "in": { "year": "-1199" } }
                },
                "p0test1none": {
                  "label": "Persian",
                  "start": { "in": { "year": "-538" } }, "stop": { "in": { "year": "-331" } }
                },
                "p0test1open": {
                  "label": "Roman",
                  "spatialCoverage": [{ "id": "http://www.wikidata.org/entity/Q801", "label": "Israel" }],
                  "start": { "in": { "earliestYear": "-63" } }, "stop": { "in": { "year": "323" } }
                }
              }
            },
            "p0test2": {
              "source": { "citation": "Stylistic Classification. Licensed under a Creative Commons Attribution 4.0 International License" },
              "periods": {
                "p0test2x": {
                  "label": "Hallstatt",
                  "spatialCoverage": [{ "id": "http://www.wikidata.org/entity/Q79", "label": "Egypt" }],
                  "start": { "in": { "year": "-799" } }, "stop": { "in": { "year": "-449" } }
                }
              }
            }
          }
        }
        """;
}
