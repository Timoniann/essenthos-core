using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Loading.Encyclopedia;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Essenthos.Core.Tests;

/// <summary>
/// A dataset's record headed by a word that is no name: withdrawn where nothing of ours stands on
/// it, kept where something does unless the owner ruled it out, and never found by a slug that has
/// come to mean another record.
/// </summary>
[Collection(WitnessDatabaseCollection.Name)]
public sealed class WithdrawnRecordTests : IDisposable
{
    private static readonly WithdrawnRecord[] Listed =
    [
        new("waters", "place:waters_1", "a common noun"),
        new("heaven", "place:heaven_1", "a common noun"),
        new("seas", "place:seas_1", "a common noun"),
        new("daughter", "person:Daughter_1", "how she is addressed"),
        new("earth", "place:earth_1", "a common noun", "the owner, on a test day"),
    ];

    private readonly AppDbContext _db;
    private readonly IDbContextTransaction _transaction;

    /// <summary>
    /// Waters with only the dataset's verse; heaven with a line of ours; a record of ours that took
    /// the slug seas; nothing under daughter at all; earth with a line, a name, a verse and a tie of
    /// ours, and a line of another record that names it.
    /// </summary>
    public WithdrawnRecordTests(WitnessDatabase database)
    {
        _db = database.NewContext();
        _transaction = _db.Database.BeginTransaction();

        var waters = Record("waters", "place:waters_1", BibleDataLoader.Source);
        var heaven = Record("heaven", "place:heaven_1", BibleDataLoader.Source);
        var maker = Record("yhvh", "test:yhvh", "test");
        Record("seas", "essenthos:seas", "Essenthos, a test");
        var earth = Record("earth", "place:earth_1", BibleDataLoader.Source);
        var river = Record("river", "test:river", "test");
        _db.SaveChanges();

        _db.EntityVerses.Add(new EntityVerse
        {
            EntityId = waters.Id, CanonicalBook = 1, CanonicalChapter = 1, CanonicalVerse = 2, Label = "waters",
            Source = BibleDataLoader.Source,
        });
        _db.EntityDescriptors.Add(new EntityDescriptor
        {
            EntityId = heaven.Id, Ordinal = 1, Relation = "created-by", TargetEntityId = maker.Id, CanonicalBook = 1,
            CanonicalChapter = 1, CanonicalVerse = 1, Method = LinkMethod.ModelReading, Confidence = 0.9,
            Source = "read from Scripture, a test",
        });
        _db.EntityDescriptors.AddRange(
            Descriptor(earth.Id, maker.Id, 1, "created-by"),
            Descriptor(river.Id, earth.Id, 1, "river-of"));
        _db.EntityNames.Add(new EntityName { EntityId = earth.Id, Label = "the earth" });
        _db.EntityVerses.Add(new EntityVerse
        {
            EntityId = earth.Id, CanonicalBook = 1, CanonicalChapter = 1, CanonicalVerse = 10, Label = "earth",
            Source = "Essenthos, a test",
        });
        _db.EntityRelationships.Add(new EntityRelationship
        {
            FromEntityId = earth.Id, ToEntityId = maker.Id, Type = "created-by", Category = RelationshipCategories.Read,
            CanonicalBook = 1, CanonicalChapter = 1, CanonicalVerse = 1, Method = LinkMethod.ModelReading, Confidence = 0.9,
            Source = "read from Scripture, a test",
        });
        _db.SaveChanges();
    }

    private static EntityDescriptor Descriptor(int entity, int target, int ordinal, string relation) => new()
    {
        EntityId = entity, Ordinal = ordinal, Relation = relation, TargetEntityId = target, CanonicalBook = 1,
        CanonicalChapter = 1, CanonicalVerse = 10, Method = LinkMethod.ModelReading, Confidence = 0.9,
        Source = "read from Scripture, a test",
    };

    public void Dispose()
    {
        _transaction.Rollback();
        _transaction.Dispose();
        _db.Dispose();
    }

    [Fact]
    public async Task ARecordWithNothingOfOursIsWithdrawnAndOneWithALineOfOursIsKept()
    {
        var outcome = await Loader().Load(Listed, default);

        outcome.Withdrawn.Should().Be(2);
        outcome.Kept.Should().Equal("heaven");
        (await Slugs()).Should().BeEquivalentTo(["heaven", "yhvh", "seas", "river"]);
        (await _db.EntityVerses.AnyAsync(v => v.Label == "waters")).Should().BeFalse();
    }

    [Fact]
    public async Task ARecordTheOwnerRuledOutGoesWithEverythingOfOursThatStoodOnItAndLeavesNothingPointingAtIt()
    {
        var outcome = await Loader().Load(Listed, default);

        outcome.RuledOut.Should().ContainSingle().Which.Should().StartWith("earth: ")
            .And.Contain("a line under a name").And.Contain("verses read off our words")
            .And.Contain("relationships");
        (await _db.Entities.AnyAsync(e => e.Slug == "earth")).Should().BeFalse();
        (await _db.EntityNames.AnyAsync(n => n.Label == "the earth")).Should().BeFalse();
        (await _db.EntityVerses.AnyAsync(v => v.Label == "earth")).Should().BeFalse();
        (await _db.EntityRelationships.CountAsync(r => r.Type == "created-by")).Should().Be(0);
        (await _db.EntityDescriptors.CountAsync(d => d.Relation == "river-of")).Should().Be(0);
        (await _db.EntityDescriptors.CountAsync()).Should().Be(1, "only heaven's line is left");
    }

    /// <summary>
    /// A record this corpus wrote itself is withdrawn only where a ruling says so, and only while
    /// nothing of ours stands on it: the Macbannites, made from a dictionary derivation and named by
    /// no text, go; a record of the same kind with a verse of ours is kept and named.
    /// </summary>
    [Fact]
    public async Task ARecordOfOursARulingSaysShouldNotExistIsWithdrawnOnlyWhileNothingStandsOnIt()
    {
        var own = "Essenthos, from the gentilics Strong's Dictionary derives";
        Record("macbannites", "essenthos:macbannites", own);
        var held = Record("calebites", "essenthos:calebites", own);
        _db.SaveChanges();
        _db.EntityVerses.Add(new EntityVerse
        {
            EntityId = held.Id, CanonicalBook = 9, CanonicalChapter = 25, CanonicalVerse = 3, Label = "Calebite",
            Source = "Essenthos, a test reading",
        });
        _db.SaveChanges();
        WithdrawnRecord[] ruled =
        [
            new("macbannites", "essenthos:macbannites", "no text names that people", OwnRecord: "the lead, on a test day"),
            new("calebites", "essenthos:calebites", "ruled by mistake", OwnRecord: "the lead, on a test day"),
        ];

        var outcome = await Loader().Load(ruled, default);

        outcome.Withdrawn.Should().Be(1);
        outcome.Kept.Should().Equal("calebites");
        (await Slugs()).Should().NotContain("macbannites").And.Contain("calebites");
        (await Loader().Load(ruled, default)).Withdrawn.Should().Be(0);
    }

    [Fact]
    public async Task ARecordOfOursNoRulingNamesIsNeverWithdrawn()
    {
        Record("macbannites", "essenthos:macbannites", "Essenthos, from the gentilics Strong's Dictionary derives");
        _db.SaveChanges();

        var outcome = await Loader().Load([new("macbannites", "essenthos:macbannites", "a common noun")], default);

        outcome.Withdrawn.Should().Be(0);
        (await Slugs()).Should().Contain("macbannites");
    }

    [Fact]
    public void TheMacbannitesAreWithdrawnOnTheLeadsRulingAndNotRecreatedByTheGentilicRead()
    {
        WithdrawnRecordLoader.Read().Single(record => record.Slug == "macbannites").OwnRecord
            .Should().NotBeNullOrWhiteSpace();
        PeopleFiles.Read().Refused.Should().Contain(refusal => refusal.Number == "H4344");
    }

    [Fact]
    public async Task ASecondRunWithdrawsNothingMore()
    {
        await Loader().Load(Listed, default);

        (await Loader().Load(Listed, default)).Withdrawn.Should().Be(0);
    }

    [Fact]
    public async Task TheEmbeddedListIsRead()
    {
        var outcome = await Loader().Load();

        outcome.Withdrawn.Should().Be(2, "waters, which nothing stands on, and heaven, which the owner ruled out");
        outcome.RuledOut.Should().ContainSingle().Which.Should().StartWith("heaven: ");
        outcome.Kept.Should().BeEmpty();
    }

    [Fact]
    public void TheGreatMenAndHisSonAreRuledOutAsNoName()
    {
        var listed = WithdrawnRecordLoader.Read().ToDictionary(record => record.Slug);

        listed["haggedolim"].OwnerRuled.Should().NotBeNullOrWhiteSpace();
        listed["beno"].OwnerRuled.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void NoDecisionInTheDescriptorFilesStandsOnARecordRuledOutAsNoName()
    {
        var ruledOut = WithdrawnRecordLoader.Read()
            .Where(record => record.OwnerRuled is not null)
            .Select(record => record.Slug)
            .ToHashSet();
        var (records, _, _) = DescriptorFiles.Read(TestResources.Folder(Path.Combine("Essenthos", "descriptors")));

        var standing = records
            .SelectMany(record => (record.Claims ?? []).Select(claim => (record.Entity, Claim: claim)))
            .Where(pair => pair.Claim.DecidedBy is not null
                && (ruledOut.Contains(pair.Entity) || ruledOut.Contains(pair.Claim.Target)))
            .Select(pair => $"{pair.Entity} {pair.Claim.Relation} {pair.Claim.Target}");

        standing.Should().BeEmpty("a decision on a record that is no name has to stand on the man the verse names");
        records.Single(record => record.Entity == "zabdiel-2").Claims.Should().ContainSingle(claim =>
            claim.Relation == "descendant-of" && claim.Target == "aaron" && claim.DecidedBy != null);
        records.Single(record => record.Entity == "jaaziah").Claims.Should().NotContain(claim => claim.Target == "beno");
    }

    private WithdrawnRecordLoader Loader() => new(_db, NullLogger<WithdrawnRecordLoader>.Instance);

    private async Task<List<string>> Slugs() => await _db.Entities.Select(e => e.Slug).ToListAsync();

    private Entity Record(string slug, string sourceId, string source)
    {
        var entity = new Entity { Kind = EntityKind.Place, Slug = slug, Name = slug, SourceId = sourceId, Source = source };
        _db.Entities.Add(entity);
        return entity;
    }
}
