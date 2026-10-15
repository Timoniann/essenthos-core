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

namespace Essenthos.Core.Tests;

/// <summary>
/// The offices the shipped descriptor files read from Scripture, each from a verse that states it and
/// checked against the Hebrew, Aramaic or Greek it rests on: Joshua leads Israel in, Samson judged
/// Israel, Joseph governed Egypt, Nehemiah and Zerubbabel governed Judah.
/// </summary>
public sealed class OfficeReadingTests
{
    private static readonly Lazy<IReadOnlyList<DescriptorRecord>> Records = new(() =>
        DescriptorFiles.Read(TestResources.Shipped(Path.Combine("Essenthos", "descriptors"))).Records);

    private static IReadOnlyList<DescriptorClaimRecord> Claims(string entity) =>
        Records.Value.Single(record => record.Entity == entity).Claims ?? [];

    [Theory]
    [InlineData("joshua", "leader-of", "israelites", "DEU 31:23", "BHSA", "תָּבִיא")]
    [InlineData("moses", "leader-of", "israelites", "ACT 7:35", "NESTLE1904", "ἄρχοντα")]
    [InlineData("caleb", "leader-of", "judahites", "NUM 34:18-19", "BHSA", "נָשִׂיא")]
    [InlineData("joseph", "governor-of", "egypt", "GEN 42:6", "BHSA", "הַשַּׁלִּיט")]
    [InlineData("joab", "commander-of", "israelites", "2SA 20:23", "BHSA", "הַצָּבָא")]
    [InlineData("jeroboam", "king-of", "israelites", "1KI 12:20", "BHSA", "וַיַּמְלִיכוּ")]
    [InlineData("jehu-2", "king-of", "israelites", "2KI 10:36", "BHSA", "מָלַךְ")]
    [InlineData("pilate", "governor-of", "judea", "LUK 3:1", "NESTLE1904", "ἡγεμονεύοντος")]
    [InlineData("samson", "judge-of", "israelites", "JDG 16:30-31", "BHSA", "שָׁפַט")]
    [InlineData("joseph-12", "apostle-of", "jesus", "ACT 14:14", "NESTLE1904", "ἀπόστολοι")]
    [InlineData("zerubbabel-2", "governor-of", "judahites", "HAG 1:1", "BHSA", "פַּחַת")]
    [InlineData("nehemiah", "governor-of", "judahites", "NEH 12:26; NEH 5:14", "BHSA", "הַפֶּחָה")]
    [InlineData("othniel", "judge-of", "israelites", "JDG 3:9-10", "BHSA", "וַיִּשְׁפֹּט")]
    [InlineData("tola-2", "judge-of", "israelites", "JDG 10:1-2", "BHSA", "וַיִּשְׁפֹּט")]
    [InlineData("daniel-2", "governor-of", "babylon", "DAN 2:48", "BHSA", "וְהַשְׁלְטֵהּ")]
    public void TheOfficeIsReadFromTheVerseInItsOriginal(
        string entity, string relation, string target, string reference, string witness, string word)
    {
        var office = Claims(entity).Should().ContainSingle(c => c.Relation == relation && c.Target == target).Subject;

        office.Reference.Should().Be(reference);
        office.DecidedBy.Should().NotBeNull();
        office.Confidence.Should().BeNull();
        office.Witness.Should().Be(witness);
        Letters(office.Original).Should().Contain(Letters(word));
        DescriptorRelations.All.Should().Contain(relation);
        DescriptorSubjects.Admits(relation, EntityKind.Person).Should().BeTrue();
    }

    /// <summary>The consonants and Greek letters alone, since a point's order within a letter is the edition's.</summary>
    private static string Letters(string? text) =>
        new([.. (text ?? string.Empty).Where(c => c is not (>= '֑' and <= 'ׇ'))]);

    /// <summary>Delilah's cry <em>The Philistines be upon thee, Samson</em> (JDG 16:9) makes him no Philistine.</summary>
    [Fact]
    public void SamsonIsNotOneOfThePhilistines() =>
        Claims("samson").Should().NotContain(c => c.Relation == "of-people" && c.Target == "philistines");

    /// <summary>
    /// Gideon refused to rule (JDG 8:23) and no verse says he judged Israel; Ehud and Shamgar are called
    /// deliverers (JDG 3:15, 3:31), not judges. A person the text gives no office has none.
    /// </summary>
    [Theory]
    [InlineData("gideon")]
    [InlineData("ehud")]
    [InlineData("shamgar")]
    public void NoOfficeIsGivenWhereNoVerseStatesOne(string entity) =>
        Claims(entity).Should().NotContain(c => c.Relation == "judge-of" || c.Relation == "leader-of");

    /// <summary>Leading a people is an office said of somebody and never of a place or a people.</summary>
    [Fact]
    public void ALeaderIsSomebody()
    {
        DescriptorSubjects.Admits(DescriptorRelations.LeaderOf, EntityKind.Person).Should().BeTrue();
        DescriptorSubjects.Admits(DescriptorRelations.LeaderOf, EntityKind.Place).Should().BeFalse();
        DescriptorSubjects.Admits(DescriptorRelations.LeaderOf, EntityKind.People).Should().BeFalse();
    }
}

/// <summary>
/// The shipped office records loaded as the descriptor step loads them: every office is stored as a
/// decision, and a person's line says it first.
/// </summary>
[Collection(WitnessDatabaseCollection.Name)]
public sealed class OfficeLineTests : IDisposable
{
    private const string OfficesFile = "zz-zz-zz-zz-zz-zz-zz-zz-zz-offices-0000.jsonl";

    private static readonly HashSet<string> Places =
        ["babylon", "cyprus", "egypt", "hebron-3", "judea", "midian-2", "shamir-3", "zeredah"];

    private static readonly HashSet<string> Peoples = ["ephrathites", "hebrews", "israelites", "issacharites", "judahites"];

    private readonly AppDbContext _db;

    private readonly IReadOnlyList<DescriptorRecord> _records;

    public OfficeLineTests(WitnessDatabase database)
    {
        _db = database.NewContext();
        _db.Database.ExecuteSqlRaw("DELETE FROM entity");

        _records = [.. DescriptorFiles.Read(TestResources.Shipped(Path.Combine("Essenthos", "descriptors"))).Records
            .Where(record => record.File == OfficesFile)];
        var verses = new Dictionary<string, HashSet<(int Book, int Chapter, int Verse)>>(StringComparer.Ordinal);
        foreach (var record in _records)
        foreach (var claim in record.Claims ?? [])
        {
            var cited = Citation.Parse(claim.Reference)!.Verses;
            Of(verses, record.Entity).UnionWith(cited);
            Of(verses, claim.Target).UnionWith(cited);
        }

        foreach (var (slug, at) in verses)
        {
            var entity = new Entity
            {
                Kind = Places.Contains(slug) ? EntityKind.Place : Peoples.Contains(slug) ? EntityKind.People : EntityKind.Person,
                Slug = slug,
                Name = char.ToUpperInvariant(slug[0]) + slug.Split('-')[0][1..],
                SourceId = slug,
                Source = "a test",
            };
            foreach (var (book, chapter, verse) in at)
            {
                entity.Verses.Add(new EntityVerse
                {
                    CanonicalBook = book, CanonicalChapter = chapter, CanonicalVerse = verse, Source = "a test",
                });
            }

            _db.Entities.Add(entity);
        }

        _db.SaveChanges();
    }

    public void Dispose()
    {
        _db.Database.ExecuteSqlRaw("DELETE FROM entity");
        _db.Dispose();
    }

    private static HashSet<(int Book, int Chapter, int Verse)> Of(
        Dictionary<string, HashSet<(int Book, int Chapter, int Verse)>> verses, string slug) =>
        verses.TryGetValue(slug, out var held) ? held : verses[slug] = [];

    private async Task Describe()
    {
        var folder = Path.Combine(Path.GetTempPath(), $"offices-{Guid.NewGuid():N}");
        Directory.CreateDirectory(folder);
        File.Copy(TestResources.Shipped(Path.Combine("Essenthos", "descriptors", OfficesFile)), Path.Combine(folder, OfficesFile));
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
    public async Task EveryOfficeIsStoredAsADecision()
    {
        await Describe();

        foreach (var record in _records)
        {
            var office = record.Claims!.Last();
            (await _db.EntityDescriptors.AnyAsync(d => d.Entity!.Slug == record.Entity && d.Relation == office.Relation
                    && d.Target!.Slug == office.Target && d.Method == LinkMethod.Manual && d.Original == office.Original))
                .Should().BeTrue($"{record.Entity} {office.Relation} {office.Target} ({office.Reference})");
        }
    }

    [Fact]
    public async Task JoshuasLineSaysHeLedIsrael()
    {
        await Describe();

        string.Concat((await Descriptors.Of(_db, "joshua", "eng", default))!.Parts.Select(p => p.Text))
            .Should().Be("leader of Israelites, son of Nun, of the tribe of Ephraim, servant of Moses");
        string.Concat((await Descriptors.Of(_db, "nehemiah", "eng", default))!.Parts.Select(p => p.Text))
            .Should().StartWith("governor of Judahites, son of Hacaliah");
    }
}
