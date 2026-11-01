using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Endpoints;
using FluentAssertions;
using Microsoft.EntityFrameworkCore.Storage;
using Xunit;

namespace Essenthos.Core.Tests.Reader;

/// <summary>
/// A place's card agrees with its own map: what it is called today is the site the point is at where
/// the stored name names another of its sites or another gazetteer entry by number, an answer from a
/// source that scores nothing is served without a score, a point another source measured says whose
/// it is, and a name carrying one Hebrew number shows the Hebrew.
/// </summary>
[Collection(WitnessDatabaseCollection.Name)]
public sealed class PlaceCardTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly IDbContextTransaction _transaction;

    public PlaceCardTests(WitnessDatabase database)
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

    private static PlaceCandidateResponse Answer(string? site, bool likeliest, string? special = null) =>
        new(site, 35, 31, "point", null, null, null, special, false, 0.4, likeliest, []);

    [Fact]
    public void HorebNamedByAnotherOfItsSitesIsNamedByTheSiteItsPointIsAt()
    {
        PlaceToday.Of("Jabal al Lawz", [Answer("Jebel Musa", true), Answer("Har Karkom", false), Answer("Jabal al Lawz", false)])
            .Should().Be("Jebel Musa");
    }

    [Fact]
    public void ANameThatContradictsNothingStands()
    {
        PlaceToday.Of("Tel Beer Sheva (near Bir es Seba)", [Answer("Tel Beer Sheva", true)]).Should().Be("Tel Beer Sheva (near Bir es Seba)");
        PlaceToday.Of("about 20 km around Dahriya", [Answer("Dahriya", true)]).Should().Be("about 20 km around Dahriya");
        PlaceToday.Of("Northern Egypt", [Answer("Qantir", true), Answer("Tell el Maskhuta", false)]).Should().Be("Northern Egypt");
        PlaceToday.Of("Jebel Musa", [Answer("Jebel Musa", true), Answer("Jabal al Lawz", false)]).Should().Be("Jebel Musa");
    }

    [Fact]
    public void ALineThatIsTheStoredNameOfAnotherSiteSaysTheSiteThePointIsAt()
    {
        PlaceToday.Line("Khirbet Ibn Naser", "Khirbet Ibn Naser", [Answer("Khirbet el Mefjir", true), Answer("Khirbet Ibn Naser", false)])
            .Should().Be("Khirbet el Mefjir");
        PlaceToday.Line("a town of Benjamin", "Khirbet Ibn Naser", [Answer("Khirbet el Mefjir", true), Answer("Khirbet Ibn Naser", false)])
            .Should().Be("a town of Benjamin");
    }

    [Fact]
    public void WithoutALikeliestSiteTheStoredNameStands()
    {
        PlaceToday.Of("Khirbet Haiyan", null).Should().Be("Khirbet Haiyan");
        PlaceToday.Of("within 10 km of Rimmon", [Answer(null, true, "not-a-place"), Answer("Rimmon", false)])
            .Should().Be("within 10 km of Rimmon");
        PlaceToday.Of("Qaryat as Salamiyah", [Answer(null, true, "not-a-proper-name"), Answer("Qaryat as Salamiyah", false)])
            .Should().Be("Qaryat as Salamiyah");
        PlaceToday.Of(null, [Answer("Dahriya", true)]).Should().BeNull();
    }

    [Fact]
    public async Task AnAnswerFromASourceThatScoresNothingHasNoConfidenceAndSaysWhoseItsPointIs()
    {
        var place = new Entity { Kind = EntityKind.Place, Slug = "wilderness", Name = "Wilderness", SourceId = "test:w", Source = "test" };
        _db.Entities.Add(place);
        await _db.SaveChangesAsync();
        _db.PlaceCandidates.Add(new PlaceCandidate
        {
            EntityId = place.Id, Rank = 0, Score = null, ModernId = "wikidata:Q1", Site = "Desert", Longitude = 35.3,
            Latitude = 31.7, Kind = "representative-point", CoordinatesSource = "wikidata", CoordinatesDataset = "wikidata",
            Source = "Wikidata Q1",
        });
        _db.PlaceCandidates.Add(new PlaceCandidate
        {
            EntityId = place.Id, Rank = 1, Score = -120, ModernId = "m1", Site = "Elsewhere", Longitude = 35.2,
            Latitude = 31.6, Kind = "point", CoordinatesSource = "daahl", Source = "OpenBible.info",
        });
        await _db.SaveChangesAsync();

        var answers = await EncyclopediaEndpoints.Candidates(_db, place.Id, "eng", default);

        answers![0].Confidence.Should().BeNull();
        answers[0].CoordinatesBy.Should().Be("wikidata");
        answers[1].Confidence.Should().Be(-0.12);
        answers[1].CoordinatesBy.Should().BeNull();
    }

    [Fact]
    public async Task ANameCarryingOneHebrewNumberGetsTheLexiconsHeadword()
    {
        _db.StrongEntries.Add(new StrongEntry { StrongNumber = "H3405", Lemma = "יְרִיחוֹ", Transliteration = "Yᵉrîychôw" });
        await _db.SaveChangesAsync();

        var lemmas = await EncyclopediaEndpoints.HebrewLemmas(_db, ["H3405", "H3405,H1", null], default);

        lemmas.Should().ContainKey("H3405").WhoseValue.Should().Be(("יְרִיחוֹ", "Yᵉrîychôw"));
        lemmas.Should().HaveCount(1, "a name of several numbers has no one headword");
    }
}
