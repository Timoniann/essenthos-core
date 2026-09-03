using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Endpoints;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Xunit;

namespace Essenthos.Core.Tests;

/// <summary>
/// What an entity page means by <em>references</em>.
///
/// The source records one row per naming, and Matthew 20:30 names Jesus three times — so a count
/// of rows is a count of namings, and the page was reporting it as a count of verses. YHVH's page
/// said 9,415 where 8,457 verses name him, and Nebuchadnezzar's was a third over.
///
/// Asked of Postgres rather than of a list in memory, because the answer is a <c>DISTINCT</c> over
/// a composed address and the question is whether the database can be asked it at all.
/// </summary>
[Collection(WitnessDatabaseCollection.Name)]
public sealed class EntityReferenceCountTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly IDbContextTransaction _transaction;
    private readonly Entity _entity;

    public EntityReferenceCountTests(WitnessDatabase database)
    {
        _db = database.NewContext();
        _transaction = _db.Database.BeginTransaction();

        _entity = new Entity
        {
            Kind = EntityKind.Person,
            Slug = "jesus",
            Name = "Jesus",
            SourceId = "essenthos:jesus",
            Source = "test",
        };
        _db.Entities.Add(_entity);
        _db.SaveChanges();

        // Matthew 20:30 as the source writes it: three namings, two of them here, in one verse.
        Naming(40, 20, 30, "Jesus", false);
        Naming(40, 20, 30, "Son of David", false);
        Naming(40, 20, 31, "Lord", true);
        Naming(41, 1, 1, "Christ", false);
        _db.SaveChanges();
    }

    public void Dispose()
    {
        _transaction.Rollback();
        _transaction.Dispose();
        _db.Dispose();
    }

    [Fact]
    public async Task AVerseNamedTwiceIsOneReferenceAndTwoMentions()
    {
        var tally = await _db.Entities
            .Where(e => e.Id == _entity.Id)
            .Select(EncyclopediaEndpoints.Tally)
            .SingleAsync();

        tally.References.Should().Be(3);
        tally.Mentions.Should().Be(4);
        tally.Disputed.Should().Be(1);
    }

    [Fact]
    public async Task TheEntityListCountsTheSameWay()
    {
        var summary = await _db.Entities
            .Where(e => e.Id == _entity.Id)
            .Select(EncyclopediaEndpoints.Summary)
            .SingleAsync();

        summary.References.Should().Be(3);
        summary.Mentions.Should().Be(4);
    }

    /// <summary>
    /// The addresses order as the three columns order, which is what lets a page of them be taken
    /// with <c>SKIP</c> and <c>TAKE</c> instead of assembled in memory.
    /// </summary>
    [Fact]
    public async Task TheReferenceListingPagesByVerse()
    {
        var addresses = await EncyclopediaEndpoints
            .Addresses(_db.EntityVerses.Where(v => v.EntityId == _entity.Id))
            .OrderBy(address => address)
            .ToListAsync();

        addresses.Should().HaveCount(3).And.BeInAscendingOrder();
        addresses[0].Should().BeLessThan(addresses[1]);
    }

    private void Naming(int book, int chapter, int verse, string label, bool disputed) =>
        _db.EntityVerses.Add(new EntityVerse
        {
            EntityId = _entity.Id,
            CanonicalBook = book,
            CanonicalChapter = chapter,
            CanonicalVerse = verse,
            Label = label,
            Disputed = disputed,
            Source = "test",
        });
}
