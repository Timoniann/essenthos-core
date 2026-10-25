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
/// Thaddaeus and Judas of James are one man (the owner, 2026-10-25): the shipped descriptor records and
/// the shipped fold, so the Twelve count thirteen with Matthias and the man's line says all of him once.
/// </summary>
[Collection(WitnessDatabaseCollection.Name)]
public sealed class TwelveFoldTests : IDisposable
{
    private static readonly string[] Described = ["thaddaeus", "judas-of-james", "the-twelve", "jesus", "james-4"];

    private readonly AppDbContext _db;

    public TwelveFoldTests(WitnessDatabase database)
    {
        _db = database.NewContext();
        _db.Database.ExecuteSqlRaw("DELETE FROM merged_record");
        _db.Database.ExecuteSqlRaw("DELETE FROM entity");

        _db.Entities.AddRange(
            Record("thaddaeus", "Thaddaeus", EntityKind.Person, (40, 10, 3), (41, 3, 18), (42, 6, 16), (43, 14, 22)),
            Record("judas-of-james", "Judas", EntityKind.Person, (42, 6, 13), (42, 6, 16), (44, 1, 13), (43, 14, 22)),
            Record("the-twelve", "The Twelve", EntityKind.People, (40, 10, 1), (40, 10, 3), (42, 6, 16)),
            Record("jesus", "Jesus", EntityKind.Person, (40, 10, 1), (42, 6, 13)),
            Record("james-4", "James", EntityKind.Person, (42, 6, 16), (44, 1, 13)));
        _db.SaveChanges();
    }

    public void Dispose()
    {
        _db.Database.ExecuteSqlRaw("DELETE FROM merged_record");
        _db.Database.ExecuteSqlRaw("DELETE FROM entity");
        _db.Dispose();
    }

    private static Entity Record(string slug, string name, EntityKind kind, params (int Book, int Chapter, int Verse)[] verses)
    {
        var entity = new Entity { Kind = kind, Slug = slug, Name = name, Sex = kind == EntityKind.Person ? "male" : null, SourceId = slug, Source = "a test" };
        foreach (var (book, chapter, verse) in verses)
        {
            entity.Verses.Add(new EntityVerse { CanonicalBook = book, CanonicalChapter = chapter, CanonicalVerse = verse, Source = "a test" });
        }

        return entity;
    }

    private async Task Describe()
    {
        var shipped = TestResources.Shipped(Path.Combine("Essenthos", "descriptors"));
        var lines = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var file in Directory.EnumerateFiles(shipped, DescriptorFiles.FilePattern).Order(StringComparer.Ordinal))
        foreach (var line in File.ReadLines(file))
        foreach (var slug in Described.Where(slug => line.StartsWith($"{{\"entity\": \"{slug}\",", StringComparison.Ordinal)))
        {
            lines[slug] = line;
        }

        var folder = Path.Combine(Path.GetTempPath(), $"twelve-{Guid.NewGuid():N}");
        Directory.CreateDirectory(folder);
        await File.WriteAllLinesAsync(Path.Combine(folder, "twelve.jsonl"), lines.Values);
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

    [Fact]
    public async Task JudasOfJamesIsFoldedIntoThaddaeusAndHisAddressStillArrives()
    {
        await Describe();
        await new DuplicateRecordLoader(_db, NullLogger<DuplicateRecordLoader>.Instance).Load();

        (await _db.Entities.AnyAsync(e => e.Slug == "judas-of-james")).Should().BeFalse();
        (await _db.MergedRecords.Where(m => m.Slug == "judas-of-james").Select(m => m.Entity!.Slug).SingleAsync())
            .Should().Be("thaddaeus");

        var clauses = await _db.EntityDescriptors.Where(d => d.Entity!.Slug == "thaddaeus")
            .Select(d => new { d.Relation, Target = d.Target!.Slug })
            .ToListAsync();
        clauses.Where(c => c.Relation == DescriptorRelations.MemberOf).Should().ContainSingle();
        clauses.Should().ContainSingle(c => c.Relation == DescriptorRelations.SonOf && c.Target == "james-4");
        clauses.Where(c => c.Relation == DescriptorRelations.ApostleOf).Should().ContainSingle();

        (await _db.EntityDescriptors.CountAsync(d => d.Relation == DescriptorRelations.MemberOf && d.Target!.Slug == "the-twelve"))
            .Should().Be(1);

        var line = string.Concat((await Descriptors.Of(_db, "thaddaeus", "eng", default))!.Parts.Select(p => p.Text));
        line.Should().StartWith("apostle of Jesus").And.Contain("son of James").And.Contain("one of The Twelve");

        _db.ChangeTracker.Clear();
        (await new DuplicateRecordLoader(_db, NullLogger<DuplicateRecordLoader>.Instance).Load()).Folded.Should().Be(0);
    }

    [Fact]
    public void TheMansNotesSayWhatEveryListCallsHimAndAreRendered()
    {
        var notes = SenseReadingFiles.AllRulings().SelectMany(file => file.Rulings)
            .Single(r => r.Existing == "thaddaeus" && r.Says?.Notes is not null).Says!.Notes!;
        notes.Should().Contain("MAT 10:3").And.Contain("MRK 3:18").And.Contain("LUK 6:16").And.Contain("ACT 1:13").And.Contain("JHN 14:22");

        var rendered = NoteTranslationLoader.Read().Single(r => r.Slug == "thaddaeus");
        rendered.English.Should().Be(notes);
        EnglishNotes.Digest(rendered.English).Should().Be(rendered.Sha256);
        NoteTranslationLoader.Read().Should().NotContain(r => r.Slug == "judas-of-james");
    }
}
