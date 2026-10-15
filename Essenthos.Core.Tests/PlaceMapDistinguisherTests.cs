using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Endpoints;
using FluentAssertions;
using Microsoft.EntityFrameworkCore.Storage;
using Xunit;

namespace Essenthos.Core.Tests;

/// <summary>
/// Two places of one name on the map carry a few words telling them apart: Acts 13 has an Antioch
/// on the Orontes and an Antioch in Pisidia, and two labels both reading «Antioch» tell nobody which.
/// </summary>
[Collection(WitnessDatabaseCollection.Name)]
public sealed class PlaceMapDistinguisherTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly IDbContextTransaction _transaction;

    public PlaceMapDistinguisherTests(WitnessDatabase database)
    {
        _db = database.NewContext();
        _transaction = _db.Database.BeginTransaction();

        var syrian = Place("antioch", "Antioch", "Antioch on the Orontes");
        var pisidian = Place("antioch-2", "Antioch", "Antioch in Pisidia");
        var pisidia = Place("pisidia", "Pisidia", null);
        var tarsus = Place("tarsus", "Tarsus", "Tarsus in Cilicia");
        _db.SaveChanges();

        _db.EntityNameForms.AddRange(
            Form(syrian, GrammaticalCases.Nominative, "Антіохія"),
            Form(pisidian, GrammaticalCases.Nominative, "Антіохія"),
            Form(pisidia, GrammaticalCases.Nominative, "Пісідія"),
            Form(pisidia, GrammaticalCases.Locative, "Пісідії"));
        _db.EntityDescriptors.Add(new EntityDescriptor
        {
            EntityId = pisidian.Id, TargetEntityId = pisidia.Id, Relation = DescriptorRelations.CityIn, Ordinal = 1,
            CanonicalBook = 44, CanonicalChapter = 13, CanonicalVerse = 14,
            Method = LinkMethod.ModelReading, Confidence = 0.9, Source = "a test",
        });
        foreach (var place in new[] { syrian, pisidian, tarsus })
        {
            _db.PlaceLocations.Add(new PlaceLocation
            {
                EntityId = place.Id, Longitude = 35, Latitude = 36, Kind = "point", Score = 500,
                ModernId = "m", CoordinatesSource = "test", Source = "openbible",
            });
        }

        _db.SaveChanges();
    }

    public void Dispose()
    {
        _transaction.Rollback();
        _transaction.Dispose();
        _db.Dispose();
    }

    private Entity Place(string slug, string name, string? distinguisher)
    {
        var place = new Entity
        {
            Kind = EntityKind.Place, Slug = slug, Name = name, Distinguisher = distinguisher,
            SourceId = slug, Source = "a test",
        };
        _db.Entities.Add(place);
        return place;
    }

    private static EntityNameForm Form(Entity entity, string grammaticalCase, string form) =>
        new()
        {
            EntityId = entity.Id, Language = "ukr", GrammaticalCase = grammaticalCase, Form = form,
            Method = LinkMethod.ModelReading, Confidence = 1, Source = "a test",
        };

    private async Task<Dictionary<string, string?>> Told(string? language) =>
        (await EncyclopediaEndpoints.Map(_db, language)).Items.ToDictionary(p => p.Slug, p => p.Distinguisher);

    [Fact]
    public async Task TwoPlacesOfOneNameAreToldApartAndAPlaceOfItsOwnNameIsNot()
    {
        var told = await Told("eng");

        told["antioch-2"].Should().Be("a city in Pisidia");
        told["antioch"].Should().Be("Antioch on the Orontes");
        told["tarsus"].Should().BeNull();
    }

    /// <summary>A Ukrainian map says it in Ukrainian or not at all: never the source's English words.</summary>
    [Fact]
    public async Task AnotherLanguageSaysItInItsOwnWordsOrNotAtAll()
    {
        var told = await Told("ukr");

        told["antioch-2"].Should().Be("місто в Пісідії");
        told["antioch"].Should().BeNull();
        told["tarsus"].Should().BeNull();
    }
}
