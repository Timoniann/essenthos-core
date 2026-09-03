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
/// What the encyclopedia can say about its own reach.
///
/// The place layer of the loaded corpus states references for Genesis and Exodus and for nothing
/// after them, so Jerusalem's page reports one verse for a city the text names several hundred
/// times. The number is not wrong about the dataset and is badly wrong about the text, and the
/// only thing that separates the two readings is a statement of which books the layer covers.
///
/// Asked of Postgres, because the point of the measure is that the database can be asked it — the
/// verse count is a <c>DISTINCT</c> over a composed address and the mention count is not.
/// </summary>
[Collection(WitnessDatabaseCollection.Name)]
public sealed class EntityCoverageTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly IDbContextTransaction _transaction;

    public EntityCoverageTests(WitnessDatabase database)
    {
        _db = database.NewContext();
        _transaction = _db.Database.BeginTransaction();

        // A person the whole canon names, a place the source stops citing after Exodus, and a
        // place it lists and never cites at all.
        var person = Add(EntityKind.Person, "moses");
        var place = Add(EntityKind.Place, "goshen");
        Add(EntityKind.Place, "seas");
        _db.SaveChanges();

        Naming(person, 1, 1, 1);
        Naming(person, 1, 1, 1);
        Naming(person, 66, 22, 21);
        Naming(place, 1, 45, 10);
        Naming(place, 2, 8, 22);
        _db.SaveChanges();
    }

    public void Dispose()
    {
        _transaction.Rollback();
        _transaction.Dispose();
        _db.Dispose();
    }

    [Fact]
    public async Task ALayerReportsTheBooksItsReferencesFallIn()
    {
        var places = await Layer("place");

        places.Books.Books.Should().Equal(1, 2);
        places.Books.FirstBook.Should().Be(1);
        places.Books.LastBook.Should().Be(2);
    }

    /// <summary>
    /// The two numbers a page shows are different questions: Genesis 1:1 naming Moses twice is one
    /// verse and two mentions, and reporting either under the other's name is the whole defect.
    /// </summary>
    [Fact]
    public async Task AVerseNamedTwiceIsOneReferenceAndTwoMentions()
    {
        var people = await Layer("person");

        people.References.Should().Be(2);
        people.Mentions.Should().Be(3);
    }

    /// <summary>
    /// An entity the source lists and never cites is not an entity the text never mentions, so the
    /// two counts are kept apart.
    /// </summary>
    [Fact]
    public async Task AnEntityWithNoReferenceIsCountedButNotNamed()
    {
        var places = await Layer("place");

        places.Entities.Should().Be(2);
        places.Named.Should().Be(1);
    }

    [Fact]
    public async Task TheCanonIsStatedSoAClientHoldsNoNumberOfItsOwn()
    {
        var coverage = await EncyclopediaEndpoints.Coverage(_db);

        coverage.Canon.Should().Be(BookReferences.CanonBookCount);
        coverage.Layers.Select(l => l.Kind).Should().Equal("person", "place");
    }

    private async Task<EntityLayerCoverageResponse> Layer(string kind) =>
        (await EncyclopediaEndpoints.Coverage(_db)).Layers.Single(l => l.Kind == kind);

    private Entity Add(EntityKind kind, string slug)
    {
        var entity = new Entity
        {
            Kind = kind,
            Slug = slug,
            Name = slug,
            SourceId = $"test:{slug}",
            Source = "test",
        };
        _db.Entities.Add(entity);
        return entity;
    }

    private void Naming(Entity entity, int book, int chapter, int verse) =>
        _db.EntityVerses.Add(new EntityVerse
        {
            EntityId = entity.Id,
            CanonicalBook = book,
            CanonicalChapter = chapter,
            CanonicalVerse = verse,
            Label = entity.Name,
            Disputed = false,
            Source = "test",
        });
}
