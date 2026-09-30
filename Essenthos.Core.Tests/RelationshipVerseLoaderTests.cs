using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Loading.Encyclopedia;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Essenthos.Core.Tests;

/// <summary>
/// The verses a record's relationships were read from, listed for a record nothing else lists a verse
/// for, and only while nothing else does.
/// </summary>
[Collection(WitnessDatabaseCollection.Name)]
public sealed class RelationshipVerseLoaderTests : IDisposable
{
    private const string OurVerse = "Essenthos, from the words this corpus annotates to the person or the place they name";

    private readonly AppDbContext _db;
    private readonly Entity _son;
    private readonly Entity _father;
    private readonly Entity _stranger;

    /// <summary>
    /// Ludim is the son of Mizraim at Genesis 10:13 and 1 Chronicles 1:11, by our reading; Mizraim has a
    /// verse of ours; the stranger is tied to Mizraim only by a row the dataset states.
    /// </summary>
    public RelationshipVerseLoaderTests(WitnessDatabase database)
    {
        _db = database.NewContext();
        _db.Database.ExecuteSqlRaw("DELETE FROM entity");
        (_son, _father, _stranger) = (Person("lud"), Person("mizraim"), Person("stranger"));
        _db.SaveChanges();
        Tie(_son, "son-of", _father, 1, 10, 13, "read from Scripture by a test");
        Tie(_father, "father-of", _son, 13, 1, 11, "read from Scripture by a test");
        Tie(_stranger, "son-of", _father, 1, 10, 14, BibleDataLoader.Source);
        _db.EntityVerses.Add(new EntityVerse
        {
            Entity = _father, CanonicalBook = 1, CanonicalChapter = 10, CanonicalVerse = 6, Source = OurVerse,
        });
        _db.EntityVerses.Add(new EntityVerse
        {
            Entity = _son, CanonicalBook = 1, CanonicalChapter = 10, CanonicalVerse = 13, Source = BibleDataLoader.Source,
        });
        _db.SaveChanges();
    }

    public void Dispose()
    {
        _db.Database.ExecuteSqlRaw("DELETE FROM entity");
        _db.Dispose();
    }

    private RelationshipVerseLoader Loader() => new(_db, NullLogger<RelationshipVerseLoader>.Instance);

    private async Task<List<(string Slug, int Book, int Chapter, int Verse)>> Listed()
    {
        _db.ChangeTracker.Clear();
        return (await _db.EntityVerses
                .Where(v => v.Source == RelationshipVerseLoader.Source)
                .Select(v => new { v.Entity!.Slug, v.CanonicalBook, v.CanonicalChapter, v.CanonicalVerse })
                .ToListAsync())
            .Select(v => (v.Slug, v.CanonicalBook, v.CanonicalChapter, v.CanonicalVerse))
            .OrderBy(v => v.CanonicalBook)
            .ToList();
    }

    [Fact]
    public async Task ARecordListedNowhereElseIsListedAtTheVersesItsRelationshipsWereReadFrom()
    {
        var first = await Loader().Load();
        var second = await Loader().Load();

        (await Listed()).Should().Equal(("lud", 1, 10, 13), ("lud", 13, 1, 11));
        first.Written.Should().Be(2);
        second.Written.Should().Be(0);
        second.Withdrawn.Should().Be(0);
    }

    [Fact]
    public async Task TheyAreTakenBackOnceTheRecordIsListedAtAVerseOfAnotherKind()
    {
        await Loader().Load();
        _db.EntityVerses.Add(new EntityVerse
        {
            EntityId = _son.Id, CanonicalBook = 1, CanonicalChapter = 10, CanonicalVerse = 13, Source = OurVerse,
        });
        await _db.SaveChangesAsync();

        var outcome = await Loader().Load();

        outcome.Withdrawn.Should().Be(2);
        (await Listed()).Should().BeEmpty();
    }

    [Fact]
    public async Task AVerseNoRelationshipOfTheRecordIsReadFromAnyMoreIsTakenBack()
    {
        await Loader().Load();
        await _db.EntityRelationships.Where(r => r.CanonicalBook == 13).ExecuteDeleteAsync();

        var outcome = await Loader().Load();

        outcome.Withdrawn.Should().Be(1);
        (await Listed()).Should().Equal(("lud", 1, 10, 13));
    }

    private Entity Person(string slug)
    {
        var entity = new Entity
        {
            Kind = EntityKind.Person, Slug = slug, Name = slug, SourceId = slug, Source = BibleDataLoader.Source,
        };
        _db.Entities.Add(entity);
        return entity;
    }

    private void Tie(Entity from, string type, Entity to, int book, int chapter, int verse, string source) =>
        _db.EntityRelationships.Add(new EntityRelationship
        {
            From = from, To = to, Type = type, Category = RelationshipCategories.Read,
            CanonicalBook = book, CanonicalChapter = chapter, CanonicalVerse = verse,
            Method = LinkMethod.Manual, Source = source,
        });
}
