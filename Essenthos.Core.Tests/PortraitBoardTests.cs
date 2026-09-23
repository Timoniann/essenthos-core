using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Desk;
using Essenthos.Core.Loading.Encyclopedia;
using System.Text.Json.Nodes;
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
    private readonly ChangeLog _log;

    public PortraitBoardTests(WitnessDatabase database)
    {
        _db = database.NewContext();
        _transaction = _db.Database.BeginTransaction();
        _paths = new DeskPaths(_root, Path.Combine(_root, "Resources"), _root);
        _log = new ChangeLog(_paths);
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

    [Theory]
    [InlineData(null, new string[0], PortraitBoard.NotStarted)]
    [InlineData("{}", new string[0], PortraitBoard.Ready)]
    [InlineData("{ \"status\": \"needs-owner-decision\" }", new string[0], PortraitBoard.Ready)]
    [InlineData("{ \"status\": \"no-portrait\" }", new[] { "approved" }, PortraitBoard.NoPortrait)]
    [InlineData(null, new[] { "none" }, PortraitBoard.Approved)]
    [InlineData(null, new[] { "approved", "pending" }, PortraitBoard.Generated)]
    [InlineData(null, new[] { "rejected" }, PortraitBoard.Rejected)]
    public void APortraitStandsWhereItsBriefSaysOrWhereTheOwnerReviewedIt(string? brief, string[] reviews, string status)
    {
        var listed = reviews
            .Select(r => (JsonNode)(r == "none" ? new JsonObject() : new JsonObject { ["review"] = r }))
            .ToList();

        PortraitBoard.StatusOf(brief is null ? null : JsonNode.Parse(brief), listed).Should().Be(status);
    }

    [Fact]
    public async Task ABriefFieldAndAStatusAreWrittenIntoTheBriefsFileAndLogged()
    {
        var david = Person("david", verses: 60);
        await _db.SaveChangesAsync();
        File.WriteAllText(Path.Combine(_paths.Generated, PortraitBoard.BriefsFile),
            "[\r\n    {\r\n        \"slug\": \"saul\",\r\n        \"age\": \"about forty\"\r\n    }\r\n]\r\n");
        var editor = Editor();

        var changed = await editor.SetField("david", "moment", new BriefFieldRequest(JsonValue.Create("Грає на арфі перед Саулом"), "краще так"), default);
        changed.Problem.Should().BeNull();
        changed.Detail!.Brief!["moment"]!.GetValue<string>().Should().Be("Грає на арфі перед Саулом");
        changed.Detail.Person.Status.Should().Be(PortraitBoard.Ready, "a brief written for somebody with none makes it ready");

        (await editor.SetField("david", "avoid", new BriefFieldRequest(new JsonArray("halo", "crown"), null), default)).Problem.Should().BeNull();
        (await editor.SetStatus("david", new PortraitStatusRequest(PortraitBoard.ToGenerate, null), default)).Detail!.Person.Status
            .Should().Be(PortraitBoard.ToGenerate);
        (await editor.SetField("david", "slug", new BriefFieldRequest(JsonValue.Create("goliath"), null), default)).Problem.Should().NotBeNull();
        (await editor.SetStatus("david", new PortraitStatusRequest("finished", null), default)).Problem.Should().NotBeNull();

        var text = File.ReadAllText(Path.Combine(_paths.Generated, PortraitBoard.BriefsFile));
        text.Should().Contain("\r\n        \"slug\": \"david\"", "the file keeps its four-space indentation and its line endings");
        text.Should().Contain("Грає на арфі");
        var briefs = JsonNode.Parse(text)!.AsArray();
        briefs.Should().HaveCount(2);
        briefs[1]!["status"]!.GetValue<string>().Should().Be(PortraitBoard.ToGenerate);
        briefs[1]!["avoid"]!.AsArray().Select(a => a!.GetValue<string>()).Should().Equal("halo", "crown");
        briefs[1]!["verses"]!.GetValue<int>().Should().Be(60);

        var logged = _log.Read().Entries.Reverse().ToList();
        logged.Select(e => (e.Action, e.Target)).Should().Equal(
            ("brief", "person/david moment"), ("brief", "person/david avoid"), ("status", "person/david"));
        (logged[0].Note, logged[2].Before!.GetValue<string>(), logged[2].After!.GetValue<string>()).Should()
            .Be(("краще так", PortraitBoard.Ready, PortraitBoard.ToGenerate));
        david.Id.Should().BePositive();
    }

    [Fact]
    public async Task AnUploadedPortraitWaitsForTheOwnersWordAndHisWordIsWhatTheLoaderReads()
    {
        Person("david", verses: 60);
        await _db.SaveChangesAsync();
        var editor = Editor();

        using (var notAPicture = new MemoryStream("<html>"u8.ToArray()))
        {
            (await editor.Upload("david", "david.png", false, null, notAPicture, default)).Problem.Should().Contain("WebP, PNG or JPEG");
        }

        using var webp = new MemoryStream([.. "RIFF"u8, 0, 0, 0, 0, .. "WEBP"u8, .. "VP8X"u8]);
        var uploaded = await editor.Upload("david", "C:/Downloads/david final.webp", false, "перший варіант", webp, default);

        uploaded.Problem.Should().BeNull();
        File.Exists(Path.Combine(_paths.Generated, "david.webp")).Should().BeTrue();
        var image = uploaded.Detail!.Images.Should().ContainSingle().Which;
        (image.File, image.Kind, image.Review, image.Loaded).Should().Be(("generated/david.webp", "generated", "pending", false));
        uploaded.Detail.Person.Status.Should().Be(PortraitBoard.Generated);
        var manifest = JsonNode.Parse(File.ReadAllText(Path.Combine(_paths.Generated, PortraitBoard.ManifestFile)))!;
        manifest["credit"]!.GetValue<string>().Should().NotBeNullOrEmpty("a manifest written for the first picture credits it");
        EntityImageLoader.Parse(new MemoryStream(File.ReadAllBytes(Path.Combine(_paths.Generated, PortraitBoard.ManifestFile))), "manifest")
            .Images.Single().Review.Should().Be(ImageChoices.Pending);

        webp.Position = 0;
        (await editor.Upload("david", "again.webp", false, null, webp, default)).Detail!.Images.Select(i => i.File)
            .Should().Equal("generated/david.webp", "generated/david-2.webp");

        var approved = await editor.SetStatus("david", new PortraitStatusRequest(PortraitBoard.Approved, null), default);
        approved.Detail!.Images.Should().OnlyContain(i => i.Review == ImageChoices.Approved, "approving the portrait approves what waited");
        var rejected = await editor.SetReview("david", new PortraitReviewRequest("generated/david-2.webp", "rejected", "не той вік"), default);
        rejected.Detail!.Images.Select(i => i.Review).Should().Equal(ImageChoices.Approved, ImageChoices.Rejected);

        var logged = _log.Read().Entries.Reverse().ToList();
        logged.Select(e => (e.Action, e.Needs)).Should().Equal(
            ("upload", null), ("upload", null), ("status", "images"), ("review", "images"));
        logged[0].After!["from"]!.GetValue<string>().Should().Be("david final.webp");
    }

    [Fact]
    public async Task GodIsGivenOnlyThePictureOfTheGloryAndNobodyElseIsGivenIt()
    {
        Person("yhvh", verses: 70, sourceId: "person:YHVH_1");
        Person("satan", verses: 50);
        await _db.SaveChangesAsync();
        var editor = Editor();
        byte[] png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13];

        (await editor.Upload("yhvh", "face.png", false, null, new MemoryStream(png), default)).Problem.Should().Contain("never given a face");
        (await editor.Upload("satan", "light.png", true, null, new MemoryStream(png), default)).Problem.Should().Contain("Only a record of God");
        (await editor.SetStatus("satan", new PortraitStatusRequest(PortraitBoard.NoPortrait, "RUL-0188"), default)).Problem.Should().BeNull();
        (await editor.Upload("satan", "satan.png", false, null, new MemoryStream(png), default)).Problem.Should().Contain("not portrayed");

        var glory = await editor.Upload("yhvh", "glory.png", true, null, new MemoryStream(png), default);
        glory.Detail!.Images.Single().Should().Match<PortraitImage>(i => i.Glory && i.File == "generated/yhvh.png");
        Directory.GetFiles(_paths.Generated, "*.png").Select(Path.GetFileName).Should().Equal("yhvh.png");
    }

    [Fact]
    public async Task EveryPicturedPersonAndPlaceIsListedWithTheOwnersChoicesAboutTheirPictures()
    {
        var david = Person("david", verses: 60);
        var hebron = new Entity { Kind = EntityKind.Place, Slug = "hebron", Name = "Hebron", SourceId = "test:hebron", Source = "test" };
        _db.Entities.Add(hebron);
        Person("nobody", verses: 1);
        await _db.SaveChangesAsync();
        _db.EntityImages.Add(Image(david.Id, "public", "commons/david.jpg"));
        _db.EntityImages.Add(Image(hebron.Id, "public", "openbible/hebron.jpg"));
        await _db.SaveChangesAsync();
        var choices = new PictureChoices(_paths, _log);
        await choices.Choose("hebron", new PictureChoiceRequest("openbible/hebron.jpg", false, false, "Хеврон з півдня", null));
        await choices.Choose("hebron", new PictureChoiceRequest("openbible/hebron-old.jpg", true, false, null, null));

        var list = await new PortraitBoard(_db, _paths).Pictured(default);

        list.Entities.Select(e => (e.Slug, e.Kind, e.Pictures, e.Hidden, e.Chosen)).Should().Equal(
            ("david", "person", 1, 0, 0), ("hebron", "place", 1, 1, 2));
        var set = await new PortraitBoard(_db, _paths).Pictures("hebron", default);
        set!.Images.Select(i => (i.File, i.Loaded, i.Choice?.Hidden, i.Choice?.Caption)).Should().Equal(
            ("openbible/hebron.jpg", true, false, "Хеврон з півдня"),
            ("openbible/hebron-old.jpg", false, true, null));
    }

    private PortraitEditor Editor() => new(new PortraitBoard(_db, _paths), _paths, _log);

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
