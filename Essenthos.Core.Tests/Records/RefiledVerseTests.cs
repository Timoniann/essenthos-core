using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Loading.Encyclopedia;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Essenthos.Core.Tests.Records;

/// <summary>
/// Nehemiah 12:10, <em>Jeshua begat Joiakim</em>, which the dataset files under the Levite Jeshua
/// of Nehemiah 8:7 rather than the high priest, and Daniel 5:31's Darius the Mede, filed under the
/// Persian. The verse goes to the man it names, and so does everything read off it.
/// </summary>
[Collection(WitnessDatabaseCollection.Name)]
public sealed class RefiledVerseTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly Entity _levite;
    private readonly Entity _highPriest;
    private readonly Entity _joiakim;
    private readonly Entity _persian;
    private readonly Entity _mede;

    public RefiledVerseTests(WitnessDatabase database)
    {
        _db = database.NewContext();
        Clear();

        _levite = Record("jeshua-7", "person:Jeshua_7");
        _highPriest = Record("jeshua-3", "person:Jeshua_3");
        _joiakim = Record("joiakim", "person:Joiakim_1");
        _persian = Record("darius", "person:Darius_1");
        _mede = Record("darius-2", "person:Darius_2");
        _db.SaveChanges();
    }

    public void Dispose()
    {
        Clear();
        _db.Dispose();
    }

    private void Clear()
    {
        _db.Database.ExecuteSqlRaw("DELETE FROM entity");
        _db.Database.ExecuteSqlRaw("DELETE FROM text");
    }

    private Entity Record(string slug, string sourceId)
    {
        var entity = new Entity
        {
            Kind = EntityKind.Person, Slug = slug, Name = slug.Split('-')[0], SourceId = sourceId,
            Source = BibleDataLoader.Source,
        };
        _db.Entities.Add(entity);
        return entity;
    }

    private void Cites(Entity entity, int book, int chapter, int verse) =>
        _db.EntityVerses.Add(new EntityVerse
        {
            Entity = entity, CanonicalBook = book, CanonicalChapter = chapter, CanonicalVerse = verse,
            Label = entity.Name, Source = BibleDataLoader.Source,
        });

    [Fact]
    public async Task TheVerseMovesToTheHighPriestAndTheLevitesOwnVersesStay()
    {
        Cites(_levite, 16, 12, 10);
        Cites(_levite, 16, 12, 24);
        await _db.SaveChangesAsync();

        var register = new PersonRegisterLoader(
            _db,
            new ConfigurationBuilder().Build(),
            new DuplicateRecordLoader(_db, NullLogger<DuplicateRecordLoader>.Instance),
            NullLogger<PersonRegisterLoader>.Instance);
        await register.Load(Path.Combine(Path.GetTempPath(), $"no-register-{Guid.NewGuid():N}"));

        var verses = await _db.EntityVerses.AsNoTracking()
            .Select(v => new { v.EntityId, v.CanonicalVerse, v.Source }).ToListAsync();
        verses.Should().BeEquivalentTo(
        [
            new { EntityId = _highPriest.Id, CanonicalVerse = 10, Source = BibleDataLoader.Source },
            new { EntityId = _levite.Id, CanonicalVerse = 24, Source = BibleDataLoader.Source },
        ]);
    }
}
