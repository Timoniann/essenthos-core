using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Endpoints;
using Essenthos.Core.Loading.Encyclopedia;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Essenthos.Core.Tests.Records;

/// <summary>
/// What a line says first about whom the text calls by another word: Jesus the Christ, the God of
/// Israel, the giants and a people of them, the bynames of the Twelve; and Matthias chosen in Judas's
/// place, Zeruiah and Abigail daughters of Jesse, Amos a prophet. The shipped titles, names and
/// descriptor records, loaded as the titles, own-names and descriptors steps load them.
/// </summary>
[Collection(WitnessDatabaseCollection.Name)]
public sealed class CalledLineTests : IDisposable
{
    private static readonly string[] Described =
    [
        "jesus", "yhvh", "israelites", "goliath", "og", "anakim", "matthias", "judas", "zeruiah", "abigail-2",
        "jesse", "amos", "thomas",
    ];

    private readonly AppDbContext _db;

    public CalledLineTests(WitnessDatabase database)
    {
        _db = database.NewContext();
        _db.Database.ExecuteSqlRaw("DELETE FROM entity");

        _db.Entities.AddRange(
            Record("jesus", "Jesus", EntityKind.Person, "male", (40, 1, 16), (42, 24, 19)),
            Record("yhvh", "YHVH", EntityKind.Person, "male", (2, 5, 1)),
            Record("israelites", "Israelites", EntityKind.People, null, (2, 5, 1)),
            Record("goliath", "Goliath", EntityKind.Person, "male", (10, 21, 19)),
            Record("og", "Og", EntityKind.Person, "male", (5, 3, 11)),
            Record("anakim", "Anakim", EntityKind.People, null, (5, 2, 11)),
            Record("matthias", "Matthias", EntityKind.Person, "male", (44, 1, 26)),
            Record("judas", "Judas", EntityKind.Person, "male", (44, 1, 25)),
            Record("zeruiah", "Zeruiah", EntityKind.Person, "female", (13, 2, 16)),
            Record("abigail-2", "Abigail", EntityKind.Person, "female", (13, 2, 16)),
            Record("jesse", "Jesse", EntityKind.Person, "male", (13, 2, 13)),
            Record("amos", "Amos", EntityKind.Person, "male", (30, 7, 14)),
            Record("thomas", "Thomas", EntityKind.Person, "male", (43, 11, 16)));
        _db.SaveChanges();
    }

    public void Dispose()
    {
        _db.Database.ExecuteSqlRaw("DELETE FROM entity");
        _db.Dispose();
    }

    private static Entity Record(
        string slug,
        string name,
        EntityKind kind,
        string? sex,
        params (int Book, int Chapter, int Verse)[] verses)
    {
        var entity = new Entity { Kind = kind, Slug = slug, Name = name, Sex = sex, SourceId = slug, Source = "a test" };
        foreach (var (book, chapter, verse) in verses)
        {
            entity.Verses.Add(new EntityVerse
            {
                CanonicalBook = book, CanonicalChapter = chapter, CanonicalVerse = verse, Source = "a test",
            });
        }

        return entity;
    }

    private async Task Load()
    {
        await new TitleLoader(_db, NullLogger<TitleLoader>.Instance).Load();
        await new OwnNameLoader(_db, NullLogger<OwnNameLoader>.Instance).Load();

        var shipped = TestResources.Shipped(Path.Combine("Essenthos", "descriptors"));
        var lines = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var file in Directory.EnumerateFiles(shipped, DescriptorFiles.FilePattern).Order(StringComparer.Ordinal))
        foreach (var line in File.ReadLines(file))
        foreach (var slug in Described.Where(slug => line.StartsWith($"{{\"entity\": \"{slug}\",", StringComparison.Ordinal)))
        {
            lines[slug] = line;
        }

        var folder = Path.Combine(Path.GetTempPath(), $"called-{Guid.NewGuid():N}");
        Directory.CreateDirectory(folder);
        await File.WriteAllLinesAsync(Path.Combine(folder, "called.jsonl"), lines.Values);
        try
        {
            await new EntityDescriptorLoader(
                    _db,
                    new ConfigurationBuilder()
                        .AddInMemoryCollection(new Dictionary<string, string?> { [DescriptorFiles.ConfigurationKey] = folder })
                        .Build(),
                    NullLogger<EntityDescriptorLoader>.Instance)
                .Load(Path.Combine(Path.GetTempPath(), $"absent-{Guid.NewGuid():N}"));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    private async Task<string> Line(string slug, string language) =>
        string.Concat((await Descriptors.Of(_db, slug, language, default))!.Parts.Select(p => p.Text));

    [Theory]
    [InlineData("eng", "Christ, prophet to the Israelites")]
    [InlineData("ukr", "Христос, пророк ізраїльтян")]
    [InlineData("deu", "Christus, Prophet der Israeliten")]
    [InlineData("spa", "Cristo, profeta de ")]
    public async Task JesussLineSaysChristFirstAndThenProphet(string language, string expected)
    {
        await Load();

        (await Line("jesus", language)).Should().StartWith(expected);
    }

    [Theory]
    [InlineData("eng", "God of the Israelites")]
    [InlineData("ukr", "Бог ізраїльтян")]
    [InlineData("deu", "Gott der Israeliten")]
    [InlineData("spa", "Dios de ")]
    public async Task TheGodOfIsraelsLineSaysGodFirst(string language, string expected)
    {
        await Load();

        (await Line("yhvh", language)).Should().StartWith(expected);
    }

    [Theory]
    [InlineData("goliath", "eng", "Giant")]
    [InlineData("goliath", "ukr", "Велетень")]
    [InlineData("og", "deu", "Riese")]
    [InlineData("og", "spa", "Gigante")]
    [InlineData("anakim", "eng", "Giants")]
    [InlineData("anakim", "ukr", "Велетні")]
    [InlineData("anakim", "deu", "Riesen")]
    public async Task AGiantsLineSaysGiantAndAPeoplesSaysGiants(string slug, string language, string expected)
    {
        await Load();

        var line = await Line(slug, language);
        line.Split(", ")[0].Should().Be(expected);
    }

    [Fact]
    public async Task MatthiasWasChosenInJudassPlaceAndJessesDaughtersAreHis()
    {
        await Load();

        (await Line("matthias", "eng")).Should().Contain("chosen in place of Judas");
        (await Line("matthias", "ukr")).Should().Contain("обраний замість Юди");
        (await Line("zeruiah", "eng")).Should().Contain("daughter of Jesse");
        (await Line("abigail-2", "ukr")).Should().Contain("дочка Єссея");
        (await Line("amos", "eng")).Should().StartWith("prophet to the Israelites");
    }

    [Theory]
    [InlineData("eng", "called Didymus")]
    [InlineData("ukr", "званий Близнюк")]
    [InlineData("deu", "genannt Zwilling")]
    [InlineData("spa", "llamado Dídimo")]
    public async Task ABynameIsSaidFirstInTheTextsWords(string language, string expected)
    {
        await Load();

        var line = (await Descriptors.Of(_db, "thomas", language, default))!;
        string.Concat(line.Parts.Select(p => p.Text)).Should().StartWith(expected);
        line.Counted.Should().ContainSingle(c => c.Relation == DescriptorPhrasings.CalledRelation && c.At == 0)
            .Which.References.Should().ContainSingle(v => v.BookOrdinal == 43 && v.Chapter == 11 && v.Verse == 16);
    }

    [Fact]
    public async Task ASecondLoadOfTheTitlesAndNamesWritesNothing()
    {
        await Load();
        var called = await _db.TitleBearers.Where(b => b.Called != null)
            .Select(b => new { b.Id, b.Called }).OrderBy(b => b.Id).ToListAsync();
        var bynames = await _db.Entities.Where(e => e.Byname != null)
            .Select(e => new { e.Id, e.Byname }).OrderBy(e => e.Id).ToListAsync();
        called.Should().NotBeEmpty();
        bynames.Should().NotBeEmpty();

        _db.ChangeTracker.Clear();
        await new TitleLoader(_db, NullLogger<TitleLoader>.Instance).Load();
        (await new OwnNameLoader(_db, NullLogger<OwnNameLoader>.Instance).Load()).Bynamed.Should().Be(0);

        (await _db.TitleBearers.Where(b => b.Called != null).Select(b => new { b.Id, b.Called }).OrderBy(b => b.Id).ToListAsync())
            .Should().Equal(called);
        (await _db.Entities.Where(e => e.Byname != null).Select(e => new { e.Id, e.Byname }).OrderBy(e => e.Id).ToListAsync())
            .Should().Equal(bynames);
    }
}
