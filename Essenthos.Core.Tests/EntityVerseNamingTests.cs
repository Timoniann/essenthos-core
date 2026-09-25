using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Endpoints;
using Essenthos.Core.Loading;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Xunit;

namespace Essenthos.Core.Tests;

/// <summary>A listed verse names its entity where a word of it is annotated to it, and only concerns it otherwise.</summary>
[Collection(WitnessDatabaseCollection.Name)]
public sealed class EntityVerseNamingTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly IDbContextTransaction _transaction;

    public EntityVerseNamingTests(WitnessDatabase database)
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
    public async Task ZimriIsNamedWhereHisNameIsAndConcernedWhereItIsNot()
    {
        var english = Corpus.Add(_db, "test-english", TextKind.Translation, "eng",
            (9, 14, ["So", "Jehu", "conspired"]),
            (9, 31, ["Had", "Zimri", "peace"]));
        await _db.SaveChangesAsync();
        var zimri = new Entity { Kind = EntityKind.Person, Slug = "zimri", Name = "Zimri", SourceId = "test:zimri", Source = "test" };
        _db.Entities.Add(zimri);
        await _db.SaveChangesAsync();
        _db.WordEntities.Add(new WordEntity
        {
            WordId = _db.WordAt(english, 9, 31, 2).Id, EntityId = zimri.Id, Method = LinkMethod.StatedBySource, Source = "test",
        });
        _db.EntityVerses.AddRange(
            new EntityVerse { EntityId = zimri.Id, CanonicalBook = 1, CanonicalChapter = 9, CanonicalVerse = 14, Source = "test" },
            new EntityVerse { EntityId = zimri.Id, CanonicalBook = 1, CanonicalChapter = 9, CanonicalVerse = 31, Source = "test", Names = false });
        await _db.SaveChangesAsync();

        var changed = await _db.Database.ExecuteSqlRawAsync(DatasetLoader.NamingVerses);
        _db.ChangeTracker.Clear();

        changed.Should().Be(1);
        (await _db.EntityVerses.Where(v => v.EntityId == zimri.Id).OrderBy(v => v.CanonicalVerse)
                .Select(v => v.Names).ToListAsync())
            .Should().Equal(false, true);
        (await _db.Database.ExecuteSqlRawAsync(DatasetLoader.NamingVerses)).Should().Be(0, "a second run changes nothing");

        var order = await EncyclopediaEndpoints.NamingFirst(_db.EntityVerses.Where(v => v.EntityId == zimri.Id)).ToListAsync();
        order.Should().HaveCount(2);
        order[0].Should().BeGreaterThan(order[1], "the verse naming him comes before the earlier verse that only concerns him");
    }
}
