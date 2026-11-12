using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Endpoints;
using FluentAssertions;
using Microsoft.EntityFrameworkCore.Storage;
using SkiaSharp;
using Xunit;

namespace Essenthos.Core.Tests.Records;

/// <summary>
/// A place's picture drawn from the site's map: a file per language under <c>places/</c>, read for its
/// digest and size and again when it is replaced, given in the reader's language or else in English,
/// leading the place's pictures with the map's credit, and the picture a list shows of the place.
/// </summary>
[Collection(WitnessDatabaseCollection.Name)]
public sealed class PlacePictureTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly IDbContextTransaction _transaction;
    private readonly string _images = Directory.CreateTempSubdirectory("place-pictures-").FullName;

    public PlacePictureTests(WitnessDatabase database)
    {
        _db = database.NewContext();
        _transaction = _db.Database.BeginTransaction();
    }

    public void Dispose()
    {
        _transaction.Rollback();
        _transaction.Dispose();
        _db.Dispose();
        Directory.Delete(_images, recursive: true);
    }

    [Fact]
    public void APictureIsGivenInTheReadersLanguageOrElseInEnglish()
    {
        Draw("ukr", "dibon", SKColors.SteelBlue);
        Draw("eng", "dibon", SKColors.Olive);
        Draw("eng", "ur", SKColors.Olive);
        var places = new PlacePictures(_images);

        places.For("dibon", "ukr")!.File.Should().Be("places/ukr/dibon.webp");
        places.For("dibon", "deu")!.File.Should().Be("places/eng/dibon.webp");
        places.For("dibon", null)!.File.Should().Be("places/eng/dibon.webp");
        places.For("ur", "ukr")!.File.Should().Be("places/eng/ur.webp");
        places.For("jericho", "ukr").Should().BeNull();
        places.For("../ukr/dibon", "ukr").Should().BeNull();

        var dibon = places.For("dibon", "ukr")!;
        dibon.Width.Should().Be(20);
        dibon.Height.Should().Be(30);
        dibon.Digest.Should().MatchRegex("^[0-9a-f]{12}$");
    }

    [Fact]
    public void ARedrawnPictureIsReadAgain()
    {
        Draw("ukr", "dibon", SKColors.SteelBlue);
        var places = new PlacePictures(_images);
        var before = places.For("dibon", "ukr")!.Digest;

        var path = Path.Combine(_images, "places", "ukr", "dibon.webp");
        Draw("ukr", "dibon", SKColors.Firebrick, width: 24);
        File.SetLastWriteTimeUtc(path, File.GetLastWriteTimeUtc(path).AddSeconds(5));

        places.For("dibon", "ukr")!.Digest.Should().NotBe(before);
        places.For("dibon", "ukr")!.Width.Should().Be(24);
    }

    [Fact]
    public async Task ItLeadsThePlacesPicturesWithTheMapsCreditAndTheOthersFollowInTheGallery()
    {
        var dibon = Entity(EntityKind.Place, "dibon");
        await _db.SaveChangesAsync();
        _db.EntityImages.Add(new EntityImage
        {
            EntityId = dibon.Id, Kind = "public", Role = "primary", File = "openbible/dibon.jpg", Digest = "0123456789ab",
            Width = 512, Height = 512, Credit = "A photographer", Licence = "CC BY 4.0", Source = "OpenBible.info",
        });
        await _db.SaveChangesAsync();
        Draw("ukr", "dibon", SKColors.SteelBlue);
        var map = new PlacePictures(_images).For("dibon", "ukr");

        var pictures = await ImageEndpoints.Of(_db, dibon.Id, default, language: "ukr", map: map);

        pictures.Select(p => (p.Kind, p.Role)).Should().Equal(("map", "primary"), ("public", "gallery"));
        var lead = pictures[0];
        lead.Url.Should().StartWith("/v1/images/places/ukr/dibon.webp?v=");
        lead.Credit.Should().Contain("OpenStreetMap").And.Contain("OpenBible.info");
        lead.Licence.Should().Contain("ODbL").And.Contain("CC BY 4.0");
        (await ImageEndpoints.Of(_db, dibon.Id, default)).Select(p => p.Kind).Should().Equal("public");
    }

    [Fact]
    public async Task AListShowsThePlaceByItsPictureInTheReadersLanguage()
    {
        var dibon = Entity(EntityKind.Place, "dibon");
        await _db.SaveChangesAsync();
        _db.EntityImages.Add(new EntityImage
        {
            EntityId = dibon.Id, Kind = "public", Role = "primary", File = "openbible/dibon.jpg", Digest = "0123456789ab",
            Width = 512, Height = 512, Credit = "A photographer", Licence = "CC BY 4.0", Source = "OpenBible.info",
        });
        await _db.SaveChangesAsync();
        Draw("deu", "dibon", SKColors.SteelBlue);
        var places = new PlacePictures(_images);

        (await ImageEndpoints.Leading(_db, ["dibon"], default))["dibon"].Kind.Should().Be("public");
        var shown = (await ImageEndpoints.Leading(_db, ["dibon"], default, places: places, language: "deu"))["dibon"];
        shown.Kind.Should().Be("map");
        shown.Url.Should().StartWith("/v1/images/places/deu/dibon.webp?v=");
        (shown.Width, shown.Height).Should().Be((20, 30));
    }

    private void Draw(string language, string slug, SKColor colour, int width = 20)
    {
        using var surface = SKSurface.Create(new SKImageInfo(width, 30));
        surface.Canvas.Clear(colour);
        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Webp, 90);
        var path = Path.Combine(_images, "places", language, $"{slug}.webp");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, data.ToArray());
    }

    private Entity Entity(EntityKind kind, string slug)
    {
        var entity = new Entity { Kind = kind, Slug = slug, Name = slug, SourceId = $"test:{slug}", Source = "test" };
        _db.Entities.Add(entity);
        return entity;
    }
}
