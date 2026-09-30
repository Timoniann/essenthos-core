using Essenthos.Core.Corpus;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Endpoints;
using Essenthos.Core.Loading.Encyclopedia;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Xunit;

namespace Essenthos.Core.Tests;

/// <summary>
/// Verse references as this project reads them, the only way a reader is shown them: a verse only
/// BibleData lists leaves the page, the counts and the chapter, and one it shares with ours is ours
/// alone, without the dataset's label. The rows stay in the table.
/// </summary>
[Collection(WitnessDatabaseCollection.Name)]
public sealed class OursOnlyReferenceTests : IDisposable
{
    private const string Ours = "Essenthos, from the words this corpus annotates to the person or the place they name";

    private const int FirstKings = 11;

    private const int Ecclesiastes = 21;

    private readonly AppDbContext _db;
    private readonly IDbContextTransaction _transaction;
    private readonly Entity _solomon;

    /// <summary>
    /// Solomon: 1 Kings 1:10 named by our words and listed by BibleData; Ecclesiastes 1:1 and 1:12,
    /// where the text says the Preacher, listed by BibleData alone, the second as disputed.
    /// </summary>
    public OursOnlyReferenceTests(WitnessDatabase database)
    {
        _db = database.NewContext();
        _transaction = _db.Database.BeginTransaction();

        _solomon = new Entity
        {
            Kind = EntityKind.Person, Slug = "solomon", Name = "Solomon", SourceId = "test:solomon", Source = "test",
        };
        _db.Entities.Add(_solomon);
        _db.SaveChanges();

        Listed(FirstKings, 1, 10, null, Ours);
        Listed(FirstKings, 1, 10, "Solomon", BibleDataLoader.Source);
        Listed(Ecclesiastes, 1, 1, "the Preacher", BibleDataLoader.Source);
        Listed(Ecclesiastes, 1, 12, "the Preacher", BibleDataLoader.Source, disputed: true);
        _db.SaveChanges();
    }

    public void Dispose()
    {
        _transaction.Rollback();
        _transaction.Dispose();
        _db.Dispose();
    }

    [Fact]
    public void TheWitnessIsTheDatasetsOwnSource() =>
        BibleDataLoader.Source.Should().StartWith(ShownVerses.Witness);

    [Fact]
    public async Task ThePageCountsOnlyTheVersesOursState()
    {
        var tally = await _db.Entities.Where(e => e.Id == _solomon.Id).Select(EncyclopediaEndpoints.Tally).SingleAsync();
        var summary = await _db.Entities.Where(e => e.Id == _solomon.Id).Select(EncyclopediaEndpoints.Summary).SingleAsync();

        tally.Should().Be(new EntityTally(References: 1, Mentions: 1, Disputed: 0));
        summary.References.Should().Be(1);
        summary.Mentions.Should().Be(1);
    }

    [Fact]
    public async Task TheVerseListHoldsNoVerseOnlyTheDatasetLists()
    {
        var shown = await _db.EntityVerses.Shown().Where(v => v.EntityId == _solomon.Id).ToListAsync();

        shown.Should().ContainSingle().Which.Should().Match<EntityVerse>(
            v => v.CanonicalBook == FirstKings && v.Source == Ours && v.Label == null);
        (await _db.EntityVerses.CountAsync(v => v.EntityId == _solomon.Id)).Should().Be(4);
    }

    [Fact]
    public async Task TheChapterDoesNotCallHimByTheDatasetsLabel()
    {
        (await ChapterSalience.NamesUsed(_db, Ecclesiastes, 1, default)).Should().BeEmpty();
        (await ChapterSalience.NamesUsed(_db, FirstKings, 1, default)).Should().BeEmpty();
    }

    [Fact]
    public async Task TheLayerCountsNoVerseOnlyTheDatasetLists()
    {
        var people = (await EncyclopediaEndpoints.Coverage(_db)).Layers.Single(layer => layer.Kind == "person");

        people.References.Should().Be(1);
        people.Books.Books.Should().Equal(FirstKings);
        people.Sources.Should().ContainSingle().Which.Source.Should().Be(Ours);
    }

    /// <summary>
    /// Shallum is listed in three verses by the dataset alone and Solomon in one by ours: the index
    /// puts Solomon first by verses, by mentions and among the matches of a search.
    /// </summary>
    [Theory]
    [InlineData("verses", null)]
    [InlineData("mentions", null)]
    [InlineData(null, "s")]
    public async Task TheIndexOrdersByTheVersesOursState(string? sort, string? q)
    {
        var shallum = new Entity
        {
            Kind = EntityKind.Person, Slug = "shallum", Name = "Shallum", SourceId = "test:shallum", Source = "test",
        };
        _db.Entities.Add(shallum);
        await _db.SaveChangesAsync();
        foreach (var verse in new[] { 13, 14, 15 })
        {
            _db.EntityVerses.Add(new EntityVerse
            {
                EntityId = shallum.Id, CanonicalBook = 12, CanonicalChapter = 15, CanonicalVerse = verse,
                Source = BibleDataLoader.Source,
            });
        }

        await _db.SaveChangesAsync();

        var ordered = await EncyclopediaEndpoints
            .Ordered(_db, EntityNames.Localised(_db, _db.Entities, null), sort, q, null)
            .Select(l => l.Entity.Slug)
            .ToListAsync();

        ordered.Should().Equal("solomon", "shallum");
    }

    private void Listed(int book, int chapter, int verse, string? label, string source, bool disputed = false) =>
        _db.EntityVerses.Add(new EntityVerse
        {
            EntityId = _solomon.Id, CanonicalBook = book, CanonicalChapter = chapter, CanonicalVerse = verse,
            Label = label, Source = source, Disputed = disputed,
        });
}
