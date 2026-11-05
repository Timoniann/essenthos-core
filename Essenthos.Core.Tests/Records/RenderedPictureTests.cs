using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Endpoints;
using Essenthos.Core.Loading;
using Essenthos.Core.Loading.Encyclopedia;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting.Internal;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Essenthos.Core.Tests.Records;

/// <summary>
/// A thing's picture drawn from its 3D model: it leads the thing's page only where nothing else
/// pictures it or the owner chose it, the owner hides it as any other, only things are given one,
/// and it reaches a reader bare, whatever the switch for generated pictures says.
/// </summary>
[Collection(WitnessDatabaseCollection.Name)]
public sealed class RenderedPictureTests : IDisposable
{
    /// <summary>The smallest JPEG header that states a size: SOI, then a baseline frame of 512 by 384.</summary>
    private static readonly byte[] Jpeg =
    [
        0xFF, 0xD8,
        0xFF, 0xE0, 0x00, 0x04, 0x00, 0x00,
        0xFF, 0xC0, 0x00, 0x11, 0x08, 0x01, 0x80, 0x02, 0x00, 0x03, 0x01, 0x22, 0x00, 0x02, 0x11, 0x01, 0x03,
        0x11, 0x01,
    ];

    private readonly AppDbContext _db;
    private readonly IDbContextTransaction _transaction;
    private readonly string _resources = Directory.CreateTempSubdirectory("rendered-pictures-").FullName;

    public RenderedPictureTests(WitnessDatabase database)
    {
        _db = database.NewContext();
        _transaction = _db.Database.BeginTransaction();

        var images = Path.Combine(_resources, EntityImageLoader.Folder);
        foreach (var file in new[] { "models/menorah.jpg", "models/ark.jpg", "models/laver.jpg", "models/cherubim.jpg", "generated/laver.jpg" })
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(images, file))!);
            File.WriteAllBytes(Path.Combine(images, file), Jpeg);
        }

        File.WriteAllText(Path.Combine(images, "models", "manifest.json"),
            """
            {
              "source": "Essenthos, rendered from its own 3D models", "credit": "Essenthos", "licence": "Essenthos's own work",
              "images": [
                { "entity": "menorah", "file": "models/menorah.jpg", "focus": [0.5, 0.5], "provenance": { "modelDigest": "aa" } },
                { "entity": "ark", "file": "models/ark.jpg", "provenance": { "modelDigest": "bb" } },
                { "entity": "laver", "file": "models/laver.jpg" },
                { "entity": "cherubim", "file": "models/cherubim.jpg" }
              ]
            }
            """);
        File.WriteAllText(Path.Combine(images, "generated", "manifest.json"),
            """
            {
              "source": "Essenthos, generated", "credit": "Essenthos", "licence": "Our own work",
              "images": [{ "entity": "laver", "file": "generated/laver.jpg", "review": "approved" }]
            }
            """);
    }

    public void Dispose()
    {
        _transaction.Rollback();
        _transaction.Dispose();
        _db.Dispose();
        Directory.Delete(_resources, recursive: true);
    }

    [Fact]
    public async Task AModelsPictureLeadsOnlyWhereNothingElsePicturesTheThing()
    {
        var menorah = Entity(EntityKind.Object, "menorah");
        var laver = Entity(EntityKind.Object, "laver");
        var cherubim = Entity(EntityKind.Person, "cherubim");
        await _db.SaveChangesAsync();

        var outcome = await Loader().Load(_resources);

        (outcome.Rendered, outcome.Unthings).Should().Be((2, 1), "the cherubim's record is not a thing");
        (await Pictures(menorah)).Should().Equal(("models/menorah.jpg", "rendered", "primary"));
        (await Pictures(laver)).Should().Equal(
            ("generated/laver.jpg", "generated", "primary"), ("models/laver.jpg", "rendered", "gallery"));
        (await Pictures(cherubim)).Should().BeEmpty();
        (await _db.EntityImages.SingleAsync(i => i.EntityId == menorah.Id)).FocusY.Should().Be(0.5);
    }

    [Fact]
    public async Task TheOwnerLeadsWithItOrHidesItAsWithAnyOther()
    {
        var menorah = Entity(EntityKind.Object, "menorah");
        var laver = Entity(EntityKind.Object, "laver");
        await _db.SaveChangesAsync();
        Choose(
            """{ "entity": "laver", "file": "models/laver.jpg", "primary": true }""",
            """{ "entity": "menorah", "file": "models/menorah.jpg", "hidden": true }""");

        var outcome = await Loader().Load(_resources);

        outcome.Hidden.Should().Be(1);
        (await Pictures(menorah)).Should().BeEmpty();
        (await Pictures(laver)).Should().Equal(
            ("models/laver.jpg", "rendered", "primary"), ("generated/laver.jpg", "generated", "gallery"));
        var page = await ImageEndpoints.Of(_db, laver.Id, default);
        page.Select(p => p.Kind).Should().Equal("rendered", "generated");
    }

    [Fact]
    public async Task AModelsPictureFillsThePlaceOfOneTheOwnerHid()
    {
        var laver = Entity(EntityKind.Object, "laver");
        await _db.SaveChangesAsync();
        Choose("""{ "entity": "laver", "file": "generated/laver.jpg", "hidden": true }""");

        await Loader().Load(_resources);

        (await Pictures(laver)).Should().Equal(("models/laver.jpg", "rendered", "primary"));
    }

    [Fact]
    public async Task ItReachesAReaderBareAndTheSwitchForGeneratedPicturesLeavesIt()
    {
        var menorah = Entity(EntityKind.Object, "menorah");
        await _db.SaveChangesAsync();
        await Loader().Load(_resources);

        var picture = (await ImageEndpoints.Of(_db, menorah.Id, default, generated: false)).Single();
        picture.Kind.Should().Be("rendered");
        new[] { picture.Caption, picture.Credit, picture.CreditUrl, picture.Licence, picture.LicenceUrl, picture.Source }
            .Should().OnlyContain(field => field == null);
        (await ImageEndpoints.Leading(_db, ["menorah"], default, generated: false))["menorah"].Kind.Should().Be("rendered");
    }

    [Fact]
    public async Task WithNoRendererHereNothingIsDrawnAndThePicturesDrawnBeforeStand()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { [ModelPictures.WebConfigurationKey] = Path.Combine(_resources, "no-web") })
            .Build();

        var outcome = await new ModelPictures(_db, configuration, NullLogger<ModelPictures>.Instance).Draw(_resources);

        outcome.Unable.Should().Contain("no renderer");
        File.Exists(Path.Combine(_resources, EntityImageLoader.Folder, "models", "menorah.jpg")).Should().BeTrue();
    }

    private void Choose(params string[] choices)
    {
        Directory.CreateDirectory(Path.Combine(_resources, ImageChoices.Folder));
        File.WriteAllText(Path.Combine(_resources, ImageChoices.Folder, ImageChoices.FileName),
            $$"""{ "images": [{{string.Join(",", choices)}}] }""");
    }

    private async Task<List<(string File, string Kind, string Role)>> Pictures(Entity entity) =>
        (await _db.EntityImages.Where(i => i.EntityId == entity.Id).OrderBy(i => i.Role == "primary" ? 0 : 1).ToListAsync())
        .Select(i => (i.File, i.Kind, i.Role))
        .ToList();

    private EntityImageLoader Loader() => new(_db, NullLogger<EntityImageLoader>.Instance);

    private Entity Entity(EntityKind kind, string slug)
    {
        var entity = new Entity { Kind = kind, Slug = slug, Name = slug, SourceId = $"test:{slug}", Source = "test" };
        _db.Entities.Add(entity);
        return entity;
    }
}
