using Essenthos.Core.Corpus;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Endpoints;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Xunit;

namespace Essenthos.Core.Tests;

[Collection(WitnessDatabaseCollection.Name)]
public sealed class PlaceReferenceReadingTests : IDisposable
{
    private const string OpenBible = "OpenBible.info Bible Geocoding, github.com/openbibleinfo/Bible-Geocoding-Data, CC BY 4.0";

    private readonly AppDbContext _db;
    private readonly IDbContextTransaction _transaction;
    private readonly Entity _tarshish;

    public PlaceReferenceReadingTests(WitnessDatabase database)
    {
        _db = database.NewContext();
        _transaction = _db.Database.BeginTransaction();
        _tarshish = Place("tarshish-4", "a5f43dd");
        var other = Place("other", "another-site");
        _db.Entities.AddRange(_tarshish, other);
        _db.SaveChanges();
        Citation(_tarshish, 26, 10, 9, OpenBible);
        Citation(_tarshish, 26, 27, 25, OpenBible);
        Citation(_tarshish, 32, 1, 3, OpenBible);
        Citation(_tarshish, 26, 10, 9, "another reading");
        Citation(other, 26, 10, 9, OpenBible);
        _db.SaveChanges();
    }

    public void Dispose()
    {
        _transaction.Rollback();
        _transaction.Dispose();
        _db.Dispose();
    }

    [Fact]
    public async Task TheGemstoneCitationIsKeptAsSourceEvidenceWithoutBecomingAPlaceOccurrence()
    {
        var before = await _db.EntityVerses.AsNoTracking().OrderBy(v => v.Id).ToListAsync();
        var shown = await _db.EntityVerses.Shown().ToListAsync();

        shown.Should().HaveCount(4);
        shown.Should().NotContain(v => v.EntityId == _tarshish.Id && v.CanonicalChapter == 10
                                     && v.Source == OpenBible);
        shown.Should().Contain(v => v.EntityId == _tarshish.Id && v.CanonicalChapter == 27);
        shown.Should().Contain(v => v.EntityId == _tarshish.Id && v.CanonicalBook == 32);
        shown.Should().Contain(v => v.EntityId == _tarshish.Id && v.Source == "another reading");
        shown.Should().Contain(v => v.EntityId != _tarshish.Id && v.CanonicalChapter == 10);
        (await _db.EntityVerses.AsNoTracking().OrderBy(v => v.Id).ToListAsync()).Should().BeEquivalentTo(before);
    }

    [Fact]
    public async Task PageAndIndexCountsUseTheSameReadingAsTheVerseList()
    {
        var tally = await _db.Entities.Where(e => e.Id == _tarshish.Id)
            .Select(EncyclopediaEndpoints.Tally).SingleAsync();
        var summary = await _db.Entities.Where(e => e.Id == _tarshish.Id)
            .Select(EncyclopediaEndpoints.Summary).SingleAsync();

        tally.Should().Be(new EntityTally(References: 3, Mentions: 3, Disputed: 0));
        summary.References.Should().Be(3);
        summary.Mentions.Should().Be(3);
    }

    [Fact]
    public async Task CoverageCountsOnlyTheDisplayedReferences()
    {
        var layer = (await EncyclopediaEndpoints.Coverage(_db)).Layers.Single(l => l.Kind == "place");
        layer.Mentions.Should().Be(4);
        layer.Sources.Single(s => s.Dataset == "openbible").Mentions.Should().Be(3);
    }

    private static Entity Place(string slug, string openBibleId) => new()
    {
        Kind = EntityKind.Place, Slug = slug, Name = slug, OpenBibleId = openBibleId,
        SourceId = $"test:{slug}", Source = "test",
    };

    private void Citation(Entity entity, int book, int chapter, int verse, string source) =>
        _db.EntityVerses.Add(new EntityVerse
        {
            Entity = entity, CanonicalBook = book, CanonicalChapter = chapter,
            CanonicalVerse = verse, Source = source,
        });
}
