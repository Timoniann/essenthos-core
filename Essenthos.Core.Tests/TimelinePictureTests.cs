using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Endpoints;
using FluentAssertions;
using Microsoft.EntityFrameworkCore.Storage;
using Xunit;

namespace Essenthos.Core.Tests;

/// <summary>
/// The pictures the timeline shows beside its events: the encyclopedia's own, never a new one. The
/// person's first, then the place the event's page links to, and nothing where neither has one.
/// </summary>
[Collection(WitnessDatabaseCollection.Name)]
public sealed class TimelinePictureTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly IDbContextTransaction _transaction;

    public TimelinePictureTests(WitnessDatabase database)
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

    [Fact]
    public async Task AnEventTakesItsPersonsPictureThenItsPlacesAndOtherwiseNone()
    {
        var abraham = Entity(EntityKind.Person, "abraham-pictured", "Abraham");
        Entity(EntityKind.Person, "lot-unpictured", "Lot");
        var gerar = Entity(EntityKind.Place, "gerar-pictured", "Gerar of the test");
        await _db.SaveChangesAsync();
        Picture(abraham, "commons/Abraham.jpg");
        Picture(gerar, "openbible/gerar.jpg");
        await _db.SaveChangesAsync();

        var pictures = await TimelinePictures.Of(
            _db,
            [("abraham-pictured", "Gerar of the test"), ("lot-unpictured", "Gerar of the test"), ("lot-unpictured", null)],
            ["abraham-pictured", null],
            generated: true,
            default);

        pictures.ForEvent("abraham-pictured", "Gerar of the test")!.Url.Should().StartWith("/v1/images/commons/Abraham.jpg");
        pictures.ForEvent("lot-unpictured", "Gerar of the test")!.Url.Should().StartWith("/v1/images/openbible/gerar.jpg");
        pictures.ForEvent(null, "gerar of the test")!.Url.Should().StartWith("/v1/images/openbible/gerar.jpg");
        pictures.ForEvent("lot-unpictured", null).Should().BeNull();
        pictures.ForEvent(null, "West of Gerar of the test").Should().BeNull();
        pictures.ForPeriod("abraham-pictured")!.Kind.Should().Be("public");
        pictures.ForPeriod(null).Should().BeNull();
    }

    /// <summary>The owner can switch our own pictures off, and the timeline follows the switch.</summary>
    [Fact]
    public async Task OurOwnPicturesStayOutWhenTheyAreSwitchedOff()
    {
        var abraham = Entity(EntityKind.Person, "abraham-generated", "Abraham");
        await _db.SaveChangesAsync();
        _db.EntityImages.Add(new EntityImage
        {
            EntityId = abraham.Id, Kind = "generated", Role = "primary", File = "generated/abraham.webp",
            Digest = "0123456789ab", Width = 512, Height = 512, Credit = "Essenthos", Licence = "Ours", Source = "Essenthos",
        });
        await _db.SaveChangesAsync();

        var on = await TimelinePictures.Of(_db, [("abraham-generated", null)], [], generated: true, default);
        var off = await TimelinePictures.Of(_db, [("abraham-generated", null)], [], generated: false, default);

        on.ForEvent("abraham-generated", null)!.Kind.Should().Be("generated");
        off.ForEvent("abraham-generated", null).Should().BeNull();
    }

    private Entity Entity(EntityKind kind, string slug, string name)
    {
        var entity = new Entity { Kind = kind, Slug = slug, Name = name, SourceId = $"test:{slug}", Source = "test" };
        _db.Entities.Add(entity);
        return entity;
    }

    private void Picture(Entity entity, string file) =>
        _db.EntityImages.Add(new EntityImage
        {
            EntityId = entity.Id, Kind = "public", Role = "primary", File = file, Digest = "0123456789ab",
            Width = 300, Height = 400, Credit = "Somebody", Licence = "Public domain", Source = "Wikimedia Commons",
        });
}
