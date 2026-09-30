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
/// it, kept where something does, and never found by a slug that has come to mean another record.
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
    ];

    private readonly AppDbContext _db;
    private readonly IDbContextTransaction _transaction;

    /// <summary>
    /// Waters with only the dataset's verse; heaven with a line of ours; a record of ours that took
    /// the slug seas; nothing under daughter at all.
    /// </summary>
    public WithdrawnRecordTests(WitnessDatabase database)
    {
        _db = database.NewContext();
        _transaction = _db.Database.BeginTransaction();

        var waters = Record("waters", "place:waters_1", BibleDataLoader.Source);
        var heaven = Record("heaven", "place:heaven_1", BibleDataLoader.Source);
        var maker = Record("yhvh", "test:yhvh", "test");
        Record("seas", "essenthos:seas", "Essenthos, a test");
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
        _db.SaveChanges();
    }

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

        outcome.Withdrawn.Should().Be(1);
        outcome.Kept.Should().Equal("heaven");
        (await Slugs()).Should().BeEquivalentTo(["heaven", "yhvh", "seas"]);
        (await _db.EntityVerses.AnyAsync(v => v.Label == "waters")).Should().BeFalse();
    }

    [Fact]
    public async Task ASecondRunWithdrawsNothingMore()
    {
        await Loader().Load(Listed, default);

        (await Loader().Load(Listed, default)).Withdrawn.Should().Be(0);
    }

    [Fact]
    public async Task TheEmbeddedListIsRead() =>
        (await Loader().Load()).Withdrawn.Should().Be(1);

    private WithdrawnRecordLoader Loader() => new(_db, NullLogger<WithdrawnRecordLoader>.Instance);

    private async Task<List<string>> Slugs() => await _db.Entities.Select(e => e.Slug).ToListAsync();

    private Entity Record(string slug, string sourceId, string source)
    {
        var entity = new Entity { Kind = EntityKind.Place, Slug = slug, Name = slug, SourceId = sourceId, Source = source };
        _db.Entities.Add(entity);
        return entity;
    }
}
