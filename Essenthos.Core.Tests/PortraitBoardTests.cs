using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Desk;
using FluentAssertions;
using Microsoft.EntityFrameworkCore.Storage;
using Xunit;

namespace Essenthos.Core.Tests;

/// <summary>
/// The console's portrait list: the tier a person's verses put them in, what pictures they have and
/// which of them the corpus already shows, the brief beside the portraits, and God kept apart.
/// </summary>
[Collection(WitnessDatabaseCollection.Name)]
public sealed class PortraitBoardTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly IDbContextTransaction _transaction;
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"portraits-{Guid.NewGuid():n}");
    private readonly DeskPaths _paths;

    public PortraitBoardTests(WitnessDatabase database)
    {
        _db = database.NewContext();
        _transaction = _db.Database.BeginTransaction();
        _paths = new DeskPaths(_root, Path.Combine(_root, "Resources"), _root);
        Directory.CreateDirectory(_paths.Generated);
    }

    public void Dispose()
    {
        _transaction.Rollback();
        _transaction.Dispose();
        _db.Dispose();
        Directory.Delete(_root, recursive: true);
    }

    [Theory]
    [InlineData(50, 1)]
    [InlineData(49, 2)]
    [InlineData(10, 2)]
    [InlineData(9, 3)]
    [InlineData(0, 3)]
    public void APersonsTierIsHowManyVersesNameThem(int verses, int tier) => PortraitBoard.TierOf(verses).Should().Be(tier);

    [Fact]
    public async Task EachPersonIsListedWithTheirVersesPicturesAndBrief()
    {
        var david = Person("david", verses: 60);
        Person("abishag", verses: 12, sex: "female");
        Person("yhvh", verses: 70, sourceId: "person:YHVH_1");
        _db.EntityNameForms.Add(new EntityNameForm
        {
            EntityId = david.Id, Language = "ukr", GrammaticalCase = GrammaticalCases.Nominative, Form = "Давид",
            Method = LinkMethod.Manual, Source = "test",
        });
        _db.EntityImages.Add(Image(david.Id, "public", "commons/david.jpg"));
        await _db.SaveChangesAsync();

        File.WriteAllText(Path.Combine(_paths.Generated, "david.webp"), "a portrait");
        File.WriteAllText(Path.Combine(_paths.Generated, "glory.webp"), "light");
        File.WriteAllText(Path.Combine(_paths.Generated, PortraitBoard.ManifestFile),
            """
            { "source": "Essenthos, generated", "credit": "Essenthos", "licence": "Our own work", "images": [
              { "entity": "david", "file": "generated/david.webp", "caption": "David in his prime" },
              { "entity": "abishag", "file": "generated/abishag.webp" },
              { "entity": "yhvh", "file": "generated/glory.webp", "glory": true }
            ] }
            """);
        File.WriteAllText(Path.Combine(_paths.Generated, PortraitBoard.BriefsFile),
            """[{ "slug": "david", "age": "about thirty", "look": "ruddy, with beautiful eyes (1SA 16:12)" }]""");

        var list = await new PortraitBoard(_db, _paths).List(default);

        list.Manifest.Should().BeTrue();
        list.Briefs.Should().BeTrue();
        var shown = list.People.Where(p => p.Slug is "david" or "abishag" or "yhvh").ToList();
        shown.Select(p => p.Slug).Should().Equal("yhvh", "david", "abishag");

        var davids = shown.Single(p => p.Slug == "david");
        (davids.Verses, davids.Tier, davids.LocalName).Should().Be((60, 1, "Давид"));
        (davids.PublicImage, davids.GeneratedListed, davids.GeneratedLoaded, davids.Brief).Should().Be((true, true, false, true));

        var abishag = shown.Single(p => p.Slug == "abishag");
        (abishag.Tier, abishag.Sex, abishag.GeneratedListed).Should().Be((2, "female", false),
            "a manifest entry whose file is not there is not a portrait");

        var yhvh = shown.Single(p => p.Slug == "yhvh");
        (yhvh.NeverPictured, yhvh.Glory).Should().Be((true, true));

        var detail = await new PortraitBoard(_db, _paths).Detail("david", default);
        detail!.Brief!["age"]!.GetValue<string>().Should().Be("about thirty");
        detail.Images.Select(i => (i.File, i.Kind, i.Loaded)).Should().BeEquivalentTo(
        [
            ("commons/david.jpg", "public", true),
            ("generated/david.webp", "generated", false),
        ]);
        (await new PortraitBoard(_db, _paths).Detail("nobody", default)).Should().BeNull();
    }

    [Fact]
    public async Task ABriefsFileCaughtMidWriteIsReportedAndTheListStillComes()
    {
        Person("david", verses: 60);
        await _db.SaveChangesAsync();
        File.WriteAllText(Path.Combine(_paths.Generated, PortraitBoard.BriefsFile), """[{ "slug": "david", "age": """);

        var list = await new PortraitBoard(_db, _paths).List(default);

        list.Briefs.Should().BeFalse();
        list.Problem.Should().Contain(PortraitBoard.BriefsFile);
        list.People.Should().Contain(p => p.Slug == "david");
    }

    private Entity Person(string slug, int verses, string? sex = "male", string? sourceId = null)
    {
        var entity = new Entity
        {
            Kind = EntityKind.Person,
            Slug = slug,
            Name = slug,
            Sex = sex,
            SourceId = sourceId ?? $"test:{slug}",
            Source = "test",
        };
        _db.Entities.Add(entity);
        _db.SaveChanges();
        for (var verse = 1; verse <= verses; verse++)
        {
            _db.EntityVerses.Add(new EntityVerse
            {
                EntityId = entity.Id,
                CanonicalBook = 9,
                CanonicalChapter = 1 + (verse / 30),
                CanonicalVerse = 1 + (verse % 30),
                Source = "test",
            });
        }

        // A second naming in a verse already counted is one more mention, not one more verse.
        if (verses > 0)
        {
            _db.EntityVerses.Add(new EntityVerse
            {
                EntityId = entity.Id, CanonicalBook = 9, CanonicalChapter = 1, CanonicalVerse = 2, Source = "test",
            });
        }

        return entity;
    }

    private static EntityImage Image(int entity, string kind, string file) => new()
    {
        EntityId = entity,
        Kind = kind,
        Role = "primary",
        File = file,
        Digest = "abcdefabcdef",
        Width = 512,
        Height = 512,
        Credit = "Somebody",
        Licence = "Public domain",
        Source = "test",
    };
}
