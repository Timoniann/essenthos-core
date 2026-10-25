using Essenthos.Core.Corpus;
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
/// Michael is the archangel, as Jude calls him (JUD 1:9), and Gabriel an angel of the Lord (LUK 1:11,
/// 1:19): the shipped title, descriptor records and notes loaded as the titles and descriptors steps
/// load them, and the line each record is given in every language the encyclopedia speaks.
/// </summary>
[Collection(WitnessDatabaseCollection.Name)]
public sealed class AngelLineTests : IDisposable
{
    private static readonly string[] Described = ["michael-9", "gabriel", "yhvh"];

    private readonly AppDbContext _db;

    public AngelLineTests(WitnessDatabase database)
    {
        _db = database.NewContext();
        _db.Database.ExecuteSqlRaw("DELETE FROM entity");

        _db.Entities.AddRange(
            Person("michael-9", "Michael", (27, 10, 13), (27, 10, 21), (27, 12, 1), (65, 1, 9), (66, 12, 7)),
            Person("gabriel", "Gabriel", (27, 8, 16), (27, 9, 21), (42, 1, 11), (42, 1, 19), (42, 1, 26)),
            Person("yhvh", "YHVH"));
        _db.SaveChanges();
    }

    public void Dispose()
    {
        _db.Database.ExecuteSqlRaw("DELETE FROM entity");
        _db.Dispose();
    }

    private static Entity Person(string slug, string name, params (int Book, int Chapter, int Verse)[] verses)
    {
        var entity = new Entity
        {
            Kind = EntityKind.Person,
            Slug = slug,
            Name = name,
            SourceId = slug,
            Source = "a test",
        };
        foreach (var (book, chapter, verse) in verses)
        {
            entity.Verses.Add(new EntityVerse
            {
                CanonicalBook = book, CanonicalChapter = chapter, CanonicalVerse = verse, Source = "a test",
            });
        }

        return entity;
    }

    /// <summary>
    /// The titles step, then the descriptors step over each record's line as the shipped files hold it
    /// last: the file that sorts last is the one a load reads.
    /// </summary>
    private async Task Load()
    {
        await new TitleLoader(_db, NullLogger<TitleLoader>.Instance).Load();

        var shipped = TestResources.Shipped(Path.Combine("Essenthos", "descriptors"));
        var lines = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var file in Directory.EnumerateFiles(shipped, DescriptorFiles.FilePattern).Order(StringComparer.Ordinal))
        foreach (var line in File.ReadLines(file))
        foreach (var slug in Described.Where(slug => line.StartsWith($"{{\"entity\": \"{slug}\",", StringComparison.Ordinal)))
        {
            lines[slug] = line;
        }

        lines.Keys.Should().BeEquivalentTo(Described);
        var folder = Path.Combine(Path.GetTempPath(), $"angels-{Guid.NewGuid():N}");
        Directory.CreateDirectory(folder);
        await File.WriteAllLinesAsync(Path.Combine(folder, "angels.jsonl"), lines.Values);
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

    [Fact]
    public async Task MichaelBearsTheTitleOfArchangelAtJude()
    {
        await Load();

        var clause = await _db.EntityDescriptors
            .Where(d => d.Entity!.Slug == "michael-9")
            .Select(d => new { d.Relation, Target = d.Target!.Slug, TargetKind = d.Target.Kind, d.Method, d.Confidence,
                d.CanonicalBook, d.CanonicalChapter, d.CanonicalVerse })
            .SingleAsync();
        clause.Should().BeEquivalentTo(new
        {
            Relation = DescriptorRelations.BearsTitle, Target = "archangel", TargetKind = EntityKind.Title,
            Method = LinkMethod.Manual, Confidence = (double?)null, CanonicalBook = 65, CanonicalChapter = 1, CanonicalVerse = 9,
        });

        (await _db.TitleBearers.Where(b => b.Bearer!.Slug == "michael-9")
                .Select(b => new { Title = b.Title!.Slug, b.CanonicalBook, b.CanonicalChapter, b.CanonicalVerse })
                .SingleAsync())
            .Should().BeEquivalentTo(new { Title = "archangel", CanonicalBook = 65, CanonicalChapter = 1, CanonicalVerse = 9 });
    }

    [Theory]
    [InlineData("eng", "Archangel")]
    [InlineData("ukr", "Архангел")]
    [InlineData("deu", "Erzengel")]
    [InlineData("spa", "Arcángel")]
    public async Task MichaelsLineIsTheTitleInEveryLanguage(string language, string expected)
    {
        await Load();

        (await Line("michael-9", language)).Should().Be(expected);
    }

    [Fact]
    public async Task GabrielIsAnAngelOfTheLordAndNoLongerItsServant()
    {
        await Load();

        var clauses = await _db.EntityDescriptors
            .Where(d => d.Entity!.Slug == "gabriel")
            .Select(d => new { d.Relation, Target = d.Target!.Slug, d.Method, d.Confidence, d.CanonicalChapter, d.CanonicalVerse, d.Citation })
            .ToListAsync();
        clauses.Should().ContainSingle().Which.Should().BeEquivalentTo(new
        {
            Relation = DescriptorRelations.AngelOf, Target = "yhvh", Method = LinkMethod.Manual,
            Confidence = (double?)Citation.ComposedConfidence, CanonicalChapter = 1, CanonicalVerse = 11,
            Citation = "LUK 1:11; LUK 1:19",
        });

        (await Line("gabriel", "ukr")).Should().Be("ангел Господа");
        (await Line("gabriel", "eng")).Should().StartWith("angel of ");
    }

    /// <summary>
    /// What tradition says of Gabriel is said on his record as tradition, and the notes of both angels
    /// and the title reach every language the encyclopedia renders notes in.
    /// </summary>
    [Fact]
    public void TheNotesSayWhatTheTextCallsThemAndAreRendered()
    {
        var says = SenseReadingFiles.AllRulings()
            .SelectMany(file => file.Rulings)
            .Where(r => r.Existing is "michael-9" or "gabriel" && r.Says?.Notes is not null)
            .ToDictionary(r => r.Existing!, r => r.Says!.Notes!);
        says.Keys.Should().BeEquivalentTo(["michael-9", "gabriel"]);
        says["gabriel"].Should().Contain("Tradition counts him among the archangels; the text calls him an angel");
        says["michael-9"].Should().Contain("JUD 1:9").And.Contain("DAN 12:1").And.Contain("REV 12:7");

        var notes = new Dictionary<string, string>(says, StringComparer.Ordinal)
        {
            ["archangel"] = SenseReadingFiles.Titles().Titles.Single(t => t.Slug == "archangel").Notes,
        };
        var rendered = NoteTranslationLoader.Read().Where(r => notes.ContainsKey(r.Slug)).ToList();
        rendered.Select(r => r.Slug).Should().BeEquivalentTo(notes.Keys);
        foreach (var record in rendered)
        {
            record.English.Should().Be(notes[record.Slug]);
            EnglishNotes.Digest(record.English).Should().Be(record.Sha256);
            record.Languages().Select(one => one.Language).Should().Equal("ukr", "deu", "spa");
        }
    }
}
