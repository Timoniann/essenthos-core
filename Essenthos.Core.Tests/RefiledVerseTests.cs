using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Loading.Encyclopedia;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Essenthos.Core.Tests;

/// <summary>
/// Nehemiah 12:10, <em>Jeshua begat Joiakim</em>, which the dataset files under the Levite Jeshua
/// of Nehemiah 8:7 rather than the high priest, and Daniel 5:31's Darius the Mede, filed under the
/// Persian. The verse goes to the man it names, and so does everything read off it.
/// </summary>
[Collection(WitnessDatabaseCollection.Name)]
public sealed class RefiledVerseTests : IDisposable
{
    private const string Reading = "read from Scripture by a test";

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

    private void Tie(Entity from, string type, Entity to, int verse, LinkMethod method, string source) =>
        _db.EntityRelationships.Add(new EntityRelationship
        {
            From = from, To = to, Type = type, Category = RelationshipCategories.Explicit,
            CanonicalBook = 16, CanonicalChapter = 12, CanonicalVerse = verse, Method = method,
            Confidence = method == LinkMethod.ModelReading ? 0.9 : null, Source = source,
        });

    private void Clause(Entity entity, int ordinal, string relation, Entity target, int verse) =>
        _db.EntityDescriptors.Add(new EntityDescriptor
        {
            Entity = entity, Ordinal = ordinal, Relation = relation, Target = target,
            CanonicalBook = 16, CanonicalChapter = 12, CanonicalVerse = verse,
            Method = LinkMethod.ModelReading, Confidence = 0.9, Source = Reading,
        });

    private Task<RefiledTieOutcome> Carry() =>
        new RefiledTieLoader(_db, NullLogger<RefiledTieLoader>.Instance).Load();

    [Fact]
    public async Task TheVerseMovesToTheHighPriestAndTheLevitesOwnVersesStay()
    {
        Cites(_levite, 16, 12, 10);
        Cites(_levite, 16, 12, 24);
        await _db.SaveChangesAsync();

        var register = new PersonRegisterLoader(
            _db, new ConfigurationBuilder().Build(), NullLogger<PersonRegisterLoader>.Instance);
        await register.Load(Path.Combine(Path.GetTempPath(), $"no-register-{Guid.NewGuid():N}"));

        var verses = await _db.EntityVerses.AsNoTracking()
            .Select(v => new { v.EntityId, v.CanonicalVerse, v.Source }).ToListAsync();
        verses.Should().BeEquivalentTo(
        [
            new { EntityId = _highPriest.Id, CanonicalVerse = 10, Source = BibleDataLoader.Source },
            new { EntityId = _levite.Id, CanonicalVerse = 24, Source = BibleDataLoader.Source },
        ]);
    }

    [Fact]
    public async Task WhatWasReadOffTheVerseNamesTheHighPriest()
    {
        Tie(_levite, "father", _joiakim, 10, LinkMethod.StatedBySource, BibleDataLoader.Source);
        Tie(_joiakim, "son", _levite, 10, LinkMethod.StatedBySource, BibleDataLoader.Source);
        Tie(_levite, "father-of", _joiakim, 10, LinkMethod.ModelReading, Reading);
        Tie(_joiakim, "son-of", _levite, 10, LinkMethod.ModelReading, Reading);
        Tie(_levite, "son-of", _joiakim, 24, LinkMethod.ModelReading, Reading);
        Clause(_levite, 1, "father-of", _joiakim, 10);
        Clause(_joiakim, 1, "son-of", _levite, 10);
        Clause(_joiakim, 2, "son-of", _highPriest, 26);
        await _db.SaveChangesAsync();

        var outcome = await Carry();

        var ties = await _db.EntityRelationships.AsNoTracking()
            .Select(r => new { r.FromEntityId, r.Type, r.ToEntityId, r.CanonicalVerse }).ToListAsync();
        ties.Should().BeEquivalentTo(
        [
            new { FromEntityId = _highPriest.Id, Type = "father", ToEntityId = _joiakim.Id, CanonicalVerse = (int?)10 },
            new { FromEntityId = _joiakim.Id, Type = "son", ToEntityId = _highPriest.Id, CanonicalVerse = (int?)10 },
            new { FromEntityId = _joiakim.Id, Type = "son-of", ToEntityId = _highPriest.Id, CanonicalVerse = (int?)10 },
            new { FromEntityId = _levite.Id, Type = "son-of", ToEntityId = _joiakim.Id, CanonicalVerse = (int?)24 },
        ], "the Levite's own reading of Nehemiah 12:10 goes, and a verse that is not moved is not touched");

        var clauses = await _db.EntityDescriptors.AsNoTracking()
            .Select(d => new { d.EntityId, d.TargetEntityId, d.CanonicalVerse }).ToListAsync();
        clauses.Should().BeEquivalentTo(
            [new { EntityId = _joiakim.Id, TargetEntityId = _highPriest.Id, CanonicalVerse = 26 }],
            "the clause about Joiakim's father, once it names the high priest, says what his record already says");
        outcome.Should().BeEquivalentTo(new { Moved = 4, Withdrawn = 2, Joined = 1, Renamed = 0 });
    }

    [Fact]
    public async Task AnOwnersDecisionIsLeftAsHeMadeIt()
    {
        Tie(_joiakim, "son-of", _levite, 10, LinkMethod.Manual, "read from Scripture by the project owner");
        await _db.SaveChangesAsync();

        await Carry();

        var tie = await _db.EntityRelationships.AsNoTracking().SingleAsync();
        tie.ToEntityId.Should().Be(_levite.Id);
    }

    [Fact]
    public async Task ANameTheVerseListSettledNamesTheMedeAndOnceSettledStays()
    {
        var aramaic = Corpus.Add(_db, "BHSA", TextKind.CriticalEdition, "arc", (5, 31, ["וְדָרְיָוֶשׁ", "מָדָאָה"]));
        await _db.SaveChangesAsync();
        _db.In(aramaic, 27);
        _db.WordEntities.Add(new WordEntity
        {
            Word = _db.WordAt(aramaic, 5, 31, 1), Entity = _persian, Method = LinkMethod.Lexical, Confidence = 0.96,
            Source = RenderedNameLoader.Source,
        });
        _db.WordEntities.Add(new WordEntity
        {
            Word = _db.WordAt(aramaic, 5, 31, 2), Entity = _persian, Method = LinkMethod.ModelReading, Confidence = 0.9,
            Source = "a reading of the verse",
        });
        await _db.SaveChangesAsync();

        (await Carry()).Renamed.Should().Be(1);
        (await Carry()).Should().BeEquivalentTo(new { Moved = 0, Withdrawn = 0, Joined = 0, Renamed = 0 });

        var named = await _db.WordEntities.AsNoTracking()
            .OrderBy(a => a.WordId).Select(a => a.EntityId).ToListAsync();
        named.Should().Equal([_mede.Id, _persian.Id], "only what the list settled follows the list");
    }
}
