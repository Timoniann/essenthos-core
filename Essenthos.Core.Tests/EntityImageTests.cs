using System.Text.Json;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Endpoints;
using Essenthos.Core.Loading.Encyclopedia;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Essenthos.Core.Tests;

/// <summary>
/// The pictures of people and places: which picture a place is given, that none is ever kept
/// without a credit and a licence, that God is never pictured, and what a page receives.
/// </summary>
[Collection(WitnessDatabaseCollection.Name)]
public sealed class EntityImageTests : IDisposable
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
    private readonly string _resources;

    public EntityImageTests(WitnessDatabase database)
    {
        _db = database.NewContext();
        _transaction = _db.Database.BeginTransaction();

        _resources = Path.Combine(Path.GetTempPath(), $"entity-images-{Guid.NewGuid():n}");
        var gazetteer = Directory.CreateDirectory(Path.Combine(_resources, "OpenBible")).FullName;
        File.WriteAllLines(Path.Combine(gazetteer, "ancient.jsonl"),
        [
            Ancient("a-own", own: Thumb("a-own.i1.jpg", "i1", "Own Credit", "<modern id=\"m1\">Tel Own</modern> from the south"),
                identification: Thumb("a-own.i2.jpg", "i2"), resolution: Thumb("a-own.i3.jpg", "i3")),
            Ancient("a-identified", identification: Thumb("a-identified.i2.jpg", "i2"), resolution: Thumb("x.i3.jpg", "i3")),
            Ancient("a-resolved", resolution: Thumb("a-resolved.i3.jpg", "i3", credit: null)),
            Ancient("a-copyright", own: Thumb("a-copyright.i4.jpg", "i4")),
            Ancient("a-uncredited", own: Thumb("a-uncredited.i5.jpg", "i5", credit: null)),
            Ancient("a-satellite", own: Thumb("a-satellite.satellite.jpg", "i6", "Contains modified Copernicus Sentinel data 2019")),
            Ancient("a-unheld", own: Thumb("a-unheld.i1.jpg", "i1")),
            Ancient("a-nofile", own: Thumb("a-nofile.i1.jpg", "i1")),
        ]);
        File.WriteAllLines(Path.Combine(gazetteer, "image.jsonl"),
        [
            """{"id":"i1","license":"CC-BY-SA-4.0","credit":"Image One","credit_url":"https://commons.wikimedia.org/wiki/File:One.jpg"}""",
            """{"id":"i2","license":"CC-BY-SA-3.0-DE","credit":"Image Two"}""",
            """{"id":"i3","license":"PD","author":"Image Three"}""",
            """{"id":"i4","license":"copyright","credit":"Somebody"}""",
            """{"id":"i5","license":"CC-BY-4.0"}""",
            """{"id":"i6","license":"sentinel"}""",
        ]);

        var images = Path.Combine(_resources, EntityImageLoader.Folder);
        foreach (var file in new[]
                 {
                     "a-own.i1.jpg", "a-identified.i2.jpg", "a-resolved.i3.jpg", "a-copyright.i4.jpg",
                     "a-uncredited.i5.jpg", "a-satellite.satellite.jpg",
                 })
        {
            WriteJpeg(Path.Combine(images, OpenBibleThumbnails.Folder, file));
        }

        WriteJpeg(Path.Combine(images, "generated", "moses.jpg"));
        WriteJpeg(Path.Combine(images, "generated", "moses-older.jpg"));
        WriteJpeg(Path.Combine(images, "generated", "yhvh.jpg"));
        File.WriteAllText(Path.Combine(images, "generated", "manifest.json"),
            """
            {
              "source": "Essenthos, generated",
              "credit": "Essenthos",
              "licence": "Our own work",
              "images": [
                { "entity": "moses", "file": "generated/moses.jpg", "caption": "Moses at eighty", "focus": [0.5, 0.2] },
                { "entity": "moses", "file": "generated/moses-older.jpg" },
                { "entity": "yhvh", "file": "generated/yhvh.jpg" },
                { "entity": "nobody-at-all", "file": "generated/moses.jpg" },
                { "entity": "aaron", "file": "generated/aaron.jpg" },
                { "entity": "moses", "file": "generated/moses.jpg", "credit": null, "licence": null }
              ]
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
    public void APlaceIsGivenTheThumbnailTheGazetteerRecommends()
    {
        var reading = Read();

        reading.Thumbnails.Single(t => t.PlaceId == "a-own").File.Should().Be("a-own.i1.jpg");
        reading.Thumbnails.Single(t => t.PlaceId == "a-identified").File.Should().Be("a-identified.i2.jpg");
        reading.Thumbnails.Single(t => t.PlaceId == "a-resolved").File.Should().Be("a-resolved.i3.jpg");
    }

    [Fact]
    public void ACreditAndALicenceComeWithEveryThumbnailAndOneWithoutEitherIsLeftOut()
    {
        var reading = Read();

        var own = reading.Thumbnails.Single(t => t.PlaceId == "a-own");
        own.Credit.Should().Be("Own Credit");
        own.CreditUrl.Should().Be("https://commons.wikimedia.org/wiki/File:One.jpg");
        (own.Licence, own.LicenceUrl).Should().Be(("CC BY-SA 4.0", "https://creativecommons.org/licenses/by-sa/4.0/"));
        own.Caption.Should().Be("Tel Own from the south");

        reading.Thumbnails.Single(t => t.PlaceId == "a-resolved").Credit.Should().Be("Image Three");
        reading.Thumbnails.Single(t => t.PlaceId == "a-satellite").Licence.Should().Be("Copernicus Sentinel data terms");

        reading.Thumbnails.Select(t => t.PlaceId).Should().NotContain(["a-copyright", "a-uncredited"]);
        reading.Unlicensed.Should().Be(1);
        reading.Uncredited.Should().Be(1);
    }

    [Theory]
    [InlineData("CC-BY-SA-3.0-DE", "CC BY-SA 3.0 DE", "https://creativecommons.org/licenses/by-sa/3.0/de/")]
    [InlineData("CC-BY-2.5", "CC BY 2.5", "https://creativecommons.org/licenses/by/2.5/")]
    [InlineData("CC-BY-SA-3.0-IGO", "CC BY-SA 3.0 IGO", "https://creativecommons.org/licenses/by-sa/3.0/igo/")]
    [InlineData("CC-Zero", "CC0 1.0", "https://creativecommons.org/publicdomain/zero/1.0/")]
    [InlineData("PD", "Public domain", null)]
    public void ALicenceCodeIsReadIntoTheNameAndPageACreditPrints(string code, string name, string? url) =>
        Licences.Of(code).Should().Be(new Licences.Licence(name, url));

    [Theory]
    [InlineData("copyright")]
    [InlineData("CC-BY-NC-ND-4.0")]
    [InlineData(null)]
    public void TermsNobodyHasReadAreNotTakenAsALicence(string? code) =>
        Licences.Of(code).Should().BeNull();

    [Fact]
    public void ThePictureSizeIsReadFromTheHeaderOfEachFormat()
    {
        ImageFiles.Size(Jpeg).Should().Be((512, 384));

        byte[] png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13, 0x49, 0x48, 0x44, 0x52, 0, 0, 2, 0, 0, 0, 1, 0x80];
        ImageFiles.Size(png).Should().Be((512, 384));

        byte[] webp =
        [
            .. "RIFF"u8, 0, 0, 0, 0, .. "WEBP"u8, .. "VP8X"u8, 10, 0, 0, 0, 0, 0, 0, 0,
            0xFF, 0x01, 0x00, 0x7F, 0x01, 0x00,
        ];
        ImageFiles.Size(webp).Should().Be((512, 384));

        ImageFiles.Size("not a picture"u8).Should().BeNull();
    }

    [Fact]
    public async Task TheLoadPicturesThePlacesAndThePortraitsAndNeverGod()
    {
        var own = Entity(EntityKind.Place, "tel-own", openBibleId: "a-own");
        Entity(EntityKind.Place, "tel-nofile", openBibleId: "a-nofile");
        var satellite = Entity(EntityKind.Place, "tel-satellite", openBibleId: "a-satellite");
        var moses = Entity(EntityKind.Person, "moses");
        var god = Entity(EntityKind.Person, "yhvh", sourceId: "person:YHVH_1");
        Entity(EntityKind.Person, "aaron");
        await _db.SaveChangesAsync();

        var outcome = await Loader().Load(_resources);

        outcome.Places.Should().Be(2);
        outcome.Generated.Should().Be(2);
        outcome.Refused.Should().Be(1);
        outcome.Missing.Should().Be(2, "the gazetteer lists a-nofile's thumbnail and the manifest aaron's portrait, and neither file is there");
        outcome.Uncredited.Should().Be(1);

        var rows = await _db.EntityImages.OrderBy(i => i.File).ToListAsync();
        rows.Should().NotContain(i => i.EntityId == god.Id);
        rows.Should().OnlyContain(i => i.Credit.Length > 0 && i.Licence.Length > 0 && i.Width == 512 && i.Height == 384);

        var place = rows.Single(i => i.EntityId == own.Id);
        (place.Kind, place.Role, place.File).Should().Be(("public", "primary", "openbible/a-own.i1.jpg"));
        place.Source.Should().StartWith("OpenBible.info");
        place.Digest.Should().HaveLength(12);

        rows.Single(i => i.EntityId == satellite.Id).Credit.Should().Be("Contains modified Copernicus Sentinel data 2019");

        var portraits = rows.Where(i => i.EntityId == moses.Id).ToList();
        portraits.Should().OnlyContain(i => i.Kind == "generated" && i.Credit == "Essenthos" && i.Licence == "Our own work");
        portraits.Single(i => i.Role == "primary").File.Should().Be("generated/moses.jpg");
        portraits.Single(i => i.Role == "primary").FocusY.Should().Be(0.2);
        portraits.Single(i => i.Role == "gallery").File.Should().Be("generated/moses-older.jpg");
    }

    [Fact]
    public async Task ASecondLoadReplacesTheFirstRatherThanAddingToIt()
    {
        Entity(EntityKind.Place, "tel-own", openBibleId: "a-own");
        await _db.SaveChangesAsync();

        await Loader().Load(_resources);
        var second = await Loader().Load(_resources);

        second.Places.Should().Be(1);
        (await _db.EntityImages.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task APageReceivesItsPicturesOurPortraitFirstAndAListItsLeadingOne()
    {
        var own = Entity(EntityKind.Place, "tel-own", openBibleId: "a-own");
        var moses = Entity(EntityKind.Person, "moses");
        await _db.SaveChangesAsync();
        await Loader().Load(_resources);
        _db.EntityImages.Add(new EntityImage
        {
            EntityId = moses.Id, Kind = "public", Role = "primary", File = "commons/Moses.jpg", Digest = "0123456789ab",
            Width = 300, Height = 600, Credit = "James Tissot", Licence = "Public domain", Source = "Wikimedia Commons",
        });
        await _db.SaveChangesAsync();

        var pictures = await ImageEndpoints.Of(_db, moses.Id, default);

        pictures.Select(p => (p.Kind, p.Role)).Should().Equal(
            ("generated", "primary"), ("public", "primary"), ("generated", "gallery"));
        pictures[1].Url.Should().Be("/v1/images/commons/Moses.jpg?v=0123456789ab");
        pictures[1].Credit.Should().Be("James Tissot");

        var place = (await ImageEndpoints.Of(_db, own.Id, default)).Single();
        place.Url.Should().MatchRegex(@"^/v1/images/openbible/a-own\.i1\.jpg\?v=[0-9a-f]{12}$");
        place.CreditUrl.Should().Be("https://commons.wikimedia.org/wiki/File:One.jpg");

        var leading = await ImageEndpoints.Leading(_db, ["moses", "tel-own", "nobody"], default);
        leading.Keys.Should().BeEquivalentTo(["moses", "tel-own"]);
        leading["moses"].Kind.Should().Be("generated");

        (await ImageEndpoints.Of(_db, moses.Id, default, generated: false)).Should().ContainSingle()
            .Which.Kind.Should().Be("public", "the owner can take our own pictures off the site");
        (await ImageEndpoints.Leading(_db, ["moses"], default, generated: false))["moses"].Kind.Should().Be("public");

        var wire = new JsonSerializerOptions(JsonSerializerDefaults.Web) { TypeInfoResolver = AppJsonSerializerContext.Default };
        var json = JsonSerializer.Serialize(pictures[0], wire);
        json.Should().Contain("\"url\":").And.Contain("\"focusY\":0.2").And.NotContain("Essenthos");
    }

    [Fact]
    public async Task OurPictureReachesAReaderWithoutItsRecordOfHowItWasMadeAndSomebodyElsesKeepsItsCredit()
    {
        var own = Entity(EntityKind.Place, "tel-own", openBibleId: "a-own");
        var moses = Entity(EntityKind.Person, "moses");
        await _db.SaveChangesAsync();
        await Loader().Load(_resources);

        var stored = await _db.EntityImages.SingleAsync(i => i.EntityId == moses.Id && i.Role == "primary");
        stored.Should().BeEquivalentTo(new { Caption = "Moses at eighty", Credit = "Essenthos", Licence = "Our own work" },
            o => o.ExcludingMissingMembers());

        var portrait = (await ImageEndpoints.Of(_db, moses.Id, default))[0];
        portrait.Kind.Should().Be("generated");
        new[] { portrait.Caption, portrait.Credit, portrait.CreditUrl, portrait.Licence, portrait.LicenceUrl, portrait.Source }
            .Should().OnlyContain(field => field == null);
        (portrait.Width, portrait.FocusY).Should().Be((512, 0.2));

        var photograph = (await ImageEndpoints.Of(_db, own.Id, default)).Single();
        (photograph.Credit, photograph.Licence).Should().Be(("Own Credit", "CC BY-SA 4.0"));
        photograph.Source.Should().NotBeNull();
    }

    [Fact]
    public void AnAddressEscapesEachSegmentOfItsPath() =>
        ImageEndpoints.Url("commons/Tissot (1896) é.jpg", "abc").Should()
            .Be("/v1/images/commons/Tissot%20%281896%29%20%C3%A9.jpg?v=abc");

    [Fact]
    public void GodAndTheWordsForGodAreNeverDepicted()
    {
        EntityImageLoader.NeverDepicted(EntityKind.Person, "person:YHVH_2").Should().BeTrue();
        EntityImageLoader.NeverDepicted(EntityKind.Term, "essenthos:term:H430").Should().BeTrue();
        EntityImageLoader.NeverDepicted(EntityKind.Person, "person:Moses_1").Should().BeFalse();
    }

    [Theory]
    [InlineData(EntityKind.Person, "person:YHVH_1", "generated", true, false)]
    [InlineData(EntityKind.Person, "person:YHVH_2", "generated", true, false)]
    [InlineData(EntityKind.Person, "person:YHVH_1", "generated", false, true)]
    [InlineData(EntityKind.Person, "person:YHVH_1", "public", true, true)]
    [InlineData(EntityKind.Person, "person:YHVH_1", "public", false, true)]
    [InlineData(EntityKind.Term, "essenthos:term:H430", "generated", true, true)]
    [InlineData(EntityKind.Person, "person:Moses_1", "generated", false, false)]
    [InlineData(EntityKind.Person, "person:Moses_1", "public", false, false)]
    public void TheOnlyPictureOfGodIsOurOwnOfTheGlory(EntityKind kind, string sourceId, string pictureKind, bool glory, bool refused) =>
        EntityImageLoader.Refused(kind, sourceId, pictureKind, glory).Should().Be(refused);

    [Fact]
    public async Task TheGloryMarkedInTheManifestIsKeptForGodAndNothingElseOfHimIs()
    {
        var god = Entity(EntityKind.Person, "yhvh", sourceId: "person:YHVH_1");
        var father = Entity(EntityKind.Person, "yhvh-2", sourceId: "person:YHVH_2");
        var elohim = Entity(EntityKind.Term, "elohim", sourceId: "essenthos:term:H430");
        await _db.SaveChangesAsync();

        var images = Path.Combine(_resources, EntityImageLoader.Folder);
        WriteJpeg(Path.Combine(images, "generated", "glory.jpg"));
        File.WriteAllText(Path.Combine(images, "generated", "manifest.json"),
            """
            {
              "source": "Essenthos, generated",
              "credit": "Essenthos",
              "licence": "Our own work",
              "images": [
                { "entity": "yhvh", "file": "generated/glory.jpg", "caption": "The glory of God as light", "glory": true },
                { "entity": "yhvh-2", "file": "generated/glory.jpg", "glory": true },
                { "entity": "yhvh", "file": "generated/yhvh.jpg" },
                { "entity": "elohim", "file": "generated/glory.jpg", "glory": true }
              ]
            }
            """);

        var outcome = await Loader().Load(_resources);

        outcome.Refused.Should().Be(2, "a picture of God not marked as the glory and any picture of a word for God are refused");
        var kept = await _db.EntityImages.Where(i => i.EntityId == god.Id || i.EntityId == father.Id || i.EntityId == elohim.Id)
            .ToListAsync();
        kept.Should().HaveCount(2).And.OnlyContain(i => i.File == "generated/glory.jpg" && i.Kind == "generated");
        kept.Select(i => i.EntityId).Should().BeEquivalentTo([god.Id, father.Id]);
    }

    /// <summary>
    /// The owner's word and his choices, as his console writes them: one of our own waiting for his
    /// word or refused by it stays off the site, a picture he hid is left out, and the one he chose
    /// leads with his caption under it.
    /// </summary>
    [Fact]
    public async Task ThePicturesFollowTheOwnersWordAndHisChoices()
    {
        var own = Entity(EntityKind.Place, "tel-own", openBibleId: "a-own");
        var satellite = Entity(EntityKind.Place, "tel-satellite", openBibleId: "a-satellite");
        var moses = Entity(EntityKind.Person, "moses");
        await _db.SaveChangesAsync();

        var images = Path.Combine(_resources, EntityImageLoader.Folder);
        WriteJpeg(Path.Combine(images, "generated", "moses-new.jpg"));
        WriteJpeg(Path.Combine(images, "generated", "moses-refused.jpg"));
        File.WriteAllText(Path.Combine(images, "generated", "manifest.json"),
            """
            {
              "source": "Essenthos, generated", "credit": "Essenthos", "licence": "Our own work",
              "images": [
                { "entity": "moses", "file": "generated/moses.jpg", "review": "approved" },
                { "entity": "moses", "file": "generated/moses-older.jpg" },
                { "entity": "moses", "file": "generated/moses-new.jpg", "review": "pending" },
                { "entity": "moses", "file": "generated/moses-refused.jpg", "review": "rejected" }
              ]
            }
            """);
        Directory.CreateDirectory(Path.Combine(_resources, ImageChoices.Folder));
        File.WriteAllText(Path.Combine(_resources, ImageChoices.Folder, ImageChoices.FileName),
            """
            {
              "about": "The owner's choices.",
              "images": [
                { "entity": "tel-satellite", "file": "openbible/a-satellite.satellite.jpg", "hidden": true },
                { "entity": "moses", "file": "generated/moses-older.jpg", "primary": true, "caption": "Мойсей на Синаї" },
                { "entity": "tel-own", "file": "openbible/a-own.i1.jpg", "caption": "  " },
                { "entity": "nobody-at-all", "file": "openbible/x.jpg", "hidden": true }
              ]
            }
            """);

        var outcome = await Loader().Load(_resources);

        (outcome.Withheld, outcome.Hidden).Should().Be((2, 1));
        (await _db.EntityImages.AnyAsync(i => i.EntityId == satellite.Id)).Should().BeFalse();
        var portraits = await _db.EntityImages.Where(i => i.EntityId == moses.Id).OrderBy(i => i.Role).ToListAsync();
        portraits.Select(i => (i.File, i.Role, i.Caption)).Should().Equal(
            ("generated/moses.jpg", "gallery", null), ("generated/moses-older.jpg", "primary", "Мойсей на Синаї"));
        (await _db.EntityImages.SingleAsync(i => i.EntityId == own.Id)).Caption.Should().Be("Tel Own from the south",
            "a blank caption is no caption of the owner's");
    }

    /// <summary>
    /// A picture on a thing's page is not the thing, and what it is instead is said only in its
    /// caption: the caption a list gives in a reader's language reaches that reader, and anyone else
    /// is given the English.
    /// </summary>
    [Fact]
    public async Task ACaptionReachesAReaderInHisLanguageWhereTheListGivesIt()
    {
        var ark = Entity(EntityKind.Object, "noahs-ark");
        var moses = Entity(EntityKind.Person, "moses");
        await _db.SaveChangesAsync();

        var images = Path.Combine(_resources, EntityImageLoader.Folder);
        File.WriteAllText(Path.Combine(images, "generated", "manifest.json"),
            """
            {
              "source": "Essenthos, generated", "credit": "Essenthos", "licence": "Our own work",
              "images": [
                { "entity": "moses", "file": "generated/moses.jpg", "caption": "Moses at eighty",
                  "captions": { "UKR": " Мойсей у вісімдесят ", "deu": "", "spa": "Moisés a los ochenta" } }
              ]
            }
            """);
        await Loader().Load(_resources);

        var stored = await _db.EntityImages.Include(i => i.Captions).SingleAsync(i => i.EntityId == moses.Id);
        stored.Captions.Select(c => (c.Language, c.Caption)).Should().BeEquivalentTo(
            [("ukr", "Мойсей у вісімдесят"), ("spa", "Moisés a los ochenta")]);

        _db.EntityImages.Add(new EntityImage
        {
            EntityId = ark.Id, Kind = "public", Role = "primary", File = "commons/Durupinar.jpg", Digest = "0123456789ab",
            Width = 1920, Height = 1285, Caption = "The Durupınar formation", Credit = "Zorka Sojka",
            Licence = "CC BY-SA 4.0", Source = "Wikimedia Commons",
            Captions = [new EntityImageCaption { Language = "ukr", Caption = "Формація Дурупинар" }],
        });
        await _db.SaveChangesAsync();

        var ukrainian = (await ImageEndpoints.Of(_db, ark.Id, default, language: "ukr")).Single();
        (ukrainian.Caption, ukrainian.CaptionLanguage).Should().Be(("Формація Дурупинар", "ukr"));

        var german = (await ImageEndpoints.Of(_db, ark.Id, default, language: "deu")).Single();
        (german.Caption, german.CaptionLanguage).Should().Be(("The Durupınar formation", null));

        (await ImageEndpoints.Of(_db, moses.Id, default, language: "ukr")).Single().Caption.Should()
            .BeNull("our own pictures go out bare in every language");

        var wire = new JsonSerializerOptions(JsonSerializerDefaults.Web) { TypeInfoResolver = AppJsonSerializerContext.Default };
        JsonSerializer.Serialize(ukrainian, wire).Should().Contain("\"captionLanguage\":\"ukr\"");
    }

    [Fact]
    public void TheOwnersCaptionTakesTheListsTranslationsWithIt()
    {
        EntityImageLoader.Candidate Candidate(string file) => new(
            1, "noahs-ark", "public", "primary", file, "The list's caption", "Credit", null, "CC BY 4.0", null,
            "Wikimedia Commons", null, false, new Dictionary<string, string> { ["ukr"] = "Підпис списку" });

        var applied = ImageChoices.Apply(
            [Candidate("commons/a.jpg"), Candidate("commons/b.jpg")],
            new ImageChoiceFile([new ImageChoice("noahs-ark", "commons/a.jpg", Caption: "His own caption")]),
            out _);

        (applied[0].Caption, applied[0].Captions).Should().Be(("His own caption", null));
        applied[1].Captions.Should().ContainKey("ukr");
    }

    [Fact]
    public void TheConsoleKnowsGodByTheSameRecordsTheLoaderDoes() =>
        Essenthos.Core.Desk.PortraitBoard.GodSourcePrefix.Should().Be(EntityImageLoader.GodSourcePrefix);

    [Fact]
    public void TheCuratedListCreditsAndLicensesEveryWork()
    {
        using var stream = typeof(EntityImageLoader).Assembly.GetManifestResourceStream(
            "Essenthos.Core.Loading.Encyclopedia.PublicImages.json")!;
        var manifest = EntityImageLoader.Parse(stream, "PublicImages.json");

        manifest.Images.Should().NotBeEmpty();
        manifest.Images.Should().OnlyContain(i =>
            i.Credit != null && i.Licence != null && i.CreditUrl != null && i.Download != null
            && i.File.StartsWith("commons/"));
        manifest.Images.Select(i => i.Entity).Should().OnlyHaveUniqueItems();
        manifest.Images.Select(i => i.Entity).Should().NotContain(["yhvh", "yhvh-2", "elohim"]);
        manifest.Images.Where(i => i.Captions != null).Should().OnlyContain(i =>
            i.Caption != null && new[] { "ukr", "deu", "spa" }.All(i.Captions!.ContainsKey),
            "a caption worth translating is one every reader needs");
    }

    /// <summary>
    /// The gazetteer as fetched: every place it recommends a picture for either gets one with a
    /// credit and a licence, or is counted as left out, and every file named is in the archive.
    /// </summary>
    [Fact]
    public void EveryRealThumbnailIsCreditedLicensedAndInTheArchive()
    {
        var images = Path.Combine(TestResources.OpenBibleFolder, OpenBibleThumbnails.ImageFile);
        var archive = Path.Combine(TestResources.OpenBibleFolder, OpenBibleThumbnails.ArchiveFile);
        if (!File.Exists(images) || !File.Exists(archive))
        {
            return;
        }

        var reading = OpenBibleThumbnails.Read(
            Path.Combine(TestResources.OpenBibleFolder, OpenBibleThumbnails.AncientFile), images);

        using var zip = System.IO.Compression.ZipFile.OpenRead(archive);
        var files = zip.Entries.Select(e => e.Name).ToHashSet(StringComparer.Ordinal);

        reading.Thumbnails.Should().HaveCountGreaterThan(1_300);
        reading.Thumbnails.Should().OnlyContain(t => t.Credit.Length > 0 && t.Licence.Length > 0);
        reading.Thumbnails.Should().OnlyContain(t => files.Contains(t.File));
        reading.Thumbnails.Select(t => t.PlaceId).Should().OnlyHaveUniqueItems();
    }

    private OpenBibleThumbnails.Reading Read() => OpenBibleThumbnails.Read(
        Path.Combine(_resources, "OpenBible", "ancient.jsonl"), Path.Combine(_resources, "OpenBible", "image.jsonl"));

    private EntityImageLoader Loader() => new(_db, NullLogger<EntityImageLoader>.Instance);

    private Entity Entity(EntityKind kind, string slug, string? openBibleId = null, string? sourceId = null)
    {
        var entity = new Entity
        {
            Kind = kind,
            Slug = slug,
            Name = slug,
            SourceId = sourceId ?? $"test:{slug}",
            Source = "test",
            OpenBibleId = openBibleId,
        };
        _db.Entities.Add(entity);
        return entity;
    }

    private static void WriteJpeg(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, Jpeg);
    }

    private static string Thumb(string file, string imageId, string? credit = "Thumb Credit", string? description = null) =>
        JsonSerializer.Serialize(new { file, image_id = imageId, credit, description });

    /// <summary>
    /// A place whose best-scored association resolves to its first identification's first
    /// resolution, with a thumbnail wherever one is given.
    /// </summary>
    private static string Ancient(string id, string? own = null, string? identification = null, string? resolution = null) =>
        "{\"id\":\"" + id + "\"," + Media(own)
        + "\"identifications\":[{" + Media(identification) + "\"resolutions\":[{" + Media(resolution)
        + "\"lonlat\":\"35.0,31.0\"}]}],"
        + "\"modern_associations\":{\"m1\":{\"score\":500,\"identification_ids\":[[0,0]]}}}";

    private static string Media(string? thumbnail) =>
        thumbnail is null ? string.Empty : "\"media\":{\"thumbnail\":" + thumbnail + "},";
}
