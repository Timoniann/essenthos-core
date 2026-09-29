using Essenthos.Core.Corpus;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Loading.Encyclopedia;
using FluentAssertions;
using Microsoft.EntityFrameworkCore.Storage;
using Xunit;

namespace Essenthos.Core.Tests;

/// <summary>
/// A record BibleData supplied and this project has read for itself is ours: it loses the dataset's
/// line and notes, and its sex and tribe are what our own rows say and nothing the dataset says.
/// </summary>
[Collection(WitnessDatabaseCollection.Name)]
public sealed class OursOnlyRecordTests : IDisposable
{
    private const string OurVerse = "Essenthos, from the words this corpus annotates to the person or the place they name";

    private readonly AppDbContext _db;
    private readonly IDbContextTransaction _transaction;

    public OursOnlyRecordTests(WitnessDatabase database)
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
    public async Task ARecordWithADescriptorAndAVerseOfOursIsOursAndItsSexIsWhatOurClauseSays()
    {
        var (zebedee, james) = (Supplied("zebedee", "female", "Levi"), Person("james"));
        await _db.SaveChangesAsync();
        Clause(zebedee, "father-of", james);
        Verse(zebedee, OurVerse);
        await _db.SaveChangesAsync();

        var ours = await OursOnlyRecords.Among(_db, ["zebedee", "james"], default);

        ours.Should().ContainKey("zebedee").WhoseValue.Should().Be(new OursOnlyRecords.OwnFacts("male", null));
        ours.Should().NotContainKey("james");
    }

    [Fact]
    public async Task ARecordWhoseOnlyVersesAreTheDatasetsOrThatHasNoDescriptorStaysTheDatasets()
    {
        var (bare, mute, target) = (Supplied("bare", "male", null), Supplied("mute", "male", null), Person("target"));
        await _db.SaveChangesAsync();
        Clause(bare, "father-of", target);
        Verse(bare, BibleDataLoader.Source);
        Verse(mute, OurVerse);
        await _db.SaveChangesAsync();

        (await OursOnlyRecords.Among(_db, ["bare", "mute"], default)).Should().BeEmpty();
    }

    [Fact]
    public async Task OurRowsThatDisagreeAboutSexLeaveItEmptyAndNothingFallsBackToTheDataset()
    {
        var (odd, target) = (Supplied("odd", "male", null), Person("target"));
        await _db.SaveChangesAsync();
        Clause(odd, "son-of", target);
        Clause(odd, "mother-of", target);
        Verse(odd, OurVerse);
        await _db.SaveChangesAsync();

        (await OursOnlyRecords.Among(_db, ["odd"], default)).Single().Value.Sex.Should().BeNull();
    }

    /// <summary>Levi is a tribe because a clause of ours says someone is of it; the Levites are Levi's descendants.</summary>
    [Fact]
    public async Task TheTribeIsThePatriarchOfThePeopleOursSaysTheyAreOfAndEmptyWhereTwoDiffer()
    {
        var (aaron, ehud, both) = (Supplied("aaron", "male", "Judah"), Supplied("ehud", "male", null), Supplied("both", "male", null));
        var (levi, judah) = (Person("levi"), Person("judah"));
        var levites = Person("levites", EntityKind.People);
        await _db.SaveChangesAsync();
        Clause(ehud, "of-tribe", levi);
        Clause(aaron, "of-tribe", levites);
        Clause(levites, "descendants-of", levi);
        Clause(both, "of-tribe", levi);
        Clause(both, "of-tribe", judah);
        foreach (var record in new[] { aaron, ehud, both })
        {
            Verse(record, OurVerse);
        }

        await _db.SaveChangesAsync();

        var ours = await OursOnlyRecords.Among(_db, ["aaron", "ehud", "both"], default);

        ours["aaron"].Tribe.Should().Be("Levi", "ours says the Levites, not the dataset's Judah");
        ours["ehud"].Tribe.Should().Be("Levi");
        ours["both"].Tribe.Should().BeNull();
    }

    [Fact]
    public void ALineIsLeftOutOnlyWhereTheRecordIsOurs()
    {
        var ours = new Dictionary<string, OursOnlyRecords.OwnFacts> { ["zebedee"] = new(null, null) };

        OursOnlyRecords.Line(ours, "zebedee", "father of James").Should().BeNull();
        OursOnlyRecords.Line(ours, "james", "son of Zebedee").Should().Be("son of Zebedee");
    }

    private Entity Supplied(string slug, string? sex, string? tribe)
    {
        var entity = Person(slug);
        entity.Sex = sex;
        entity.Tribe = tribe;
        entity.Source = BibleDataLoader.Source;
        entity.Distinguisher = "a line only BibleData states";
        entity.Notes = "notes only BibleData states";
        return entity;
    }

    private Entity Person(string slug, EntityKind kind = EntityKind.Person)
    {
        var entity = new Entity
        {
            Kind = kind,
            Slug = slug,
            Name = char.ToUpperInvariant(slug[0]) + slug[1..],
            SourceId = $"test:{slug}",
            Source = "test",
        };
        _db.Entities.Add(entity);
        return entity;
    }

    private void Clause(Entity entity, string relation, Entity target) =>
        _db.EntityDescriptors.Add(new EntityDescriptor
        {
            Entity = entity,
            Ordinal = _db.EntityDescriptors.Local.Count(d => d.Entity == entity) + 1,
            Relation = relation,
            Target = target,
            CanonicalBook = 1,
            CanonicalChapter = 1,
            CanonicalVerse = 1,
            Method = LinkMethod.ModelReading,
            Confidence = 0.9,
            Source = "read from Scripture by a test",
        });

    private void Verse(Entity entity, string source) =>
        _db.EntityVerses.Add(new EntityVerse
        {
            Entity = entity, CanonicalBook = 1, CanonicalChapter = 1, CanonicalVerse = 1, Source = source,
        });
}
