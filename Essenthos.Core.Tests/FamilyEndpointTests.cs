using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Endpoints;
using FluentAssertions;
using Microsoft.EntityFrameworkCore.Storage;
using Xunit;

namespace Essenthos.Core.Tests;

/// <summary>
/// Many people at once, with only what a family tree is drawn from.
/// </summary>
[Collection(WitnessDatabaseCollection.Name)]
public sealed class FamilyEndpointTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly IDbContextTransaction _transaction;

    public FamilyEndpointTests(WitnessDatabase database)
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

    /// <summary>
    /// A father recorded once is a son on the other page: Isaac's row names Abraham, and Abraham's
    /// side of it is read inward, exactly as the entity page reads it.
    /// </summary>
    [Fact]
    public async Task EachPersonHasEveryTieFromTheirOwnSide()
    {
        var abraham = Person("abraham", "male");
        var sarah = Person("sarah", "female");
        var isaac = Person("isaac", "male");
        await _db.SaveChangesAsync();
        Tie(isaac, "son-of", abraham);
        Tie(abraham, "husband-of", sarah);
        await _db.SaveChangesAsync();

        var family = await FamilyEndpoints.Family(_db, ["isaac", "abraham"], null, default);

        family.People.Select(p => p.Slug).Should().Equal("isaac", "abraham");
        var father = family.People[1];
        father.Sex.Should().Be("male");
        father.Ties.Should().BeEquivalentTo(
        [
            new FamilyTieResponse("husband-of", false, "sarah"),
            new FamilyTieResponse("son-of", true, "isaac"),
        ]);
        family.People[0].Ties.Should().Equal(new FamilyTieResponse("son-of", false, "abraham"));
    }

    /// <summary>
    /// Two witnesses stating one fact in one word are one tie to a tree, which only asks who.
    /// </summary>
    [Fact]
    public async Task ATieStatedTwiceInOneWordIsOneTie()
    {
        var lot = Person("lot", "male");
        var haran = Person("haran", "male");
        await _db.SaveChangesAsync();
        Tie(lot, "son-of", haran);
        Tie(lot, "son-of", haran, source: "test:second");
        await _db.SaveChangesAsync();

        var family = await FamilyEndpoints.Family(_db, ["lot"], null, default);

        family.People.Single().Ties.Should().ContainSingle();
    }

    [Fact]
    public async Task AnUnknownSlugIsLeftOut()
    {
        Person("moses", "male");
        await _db.SaveChangesAsync();

        var family = await FamilyEndpoints.Family(_db, ["moses", "nobody-at-all"], null, default);

        family.People.Select(p => p.Slug).Should().Equal("moses");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" , ")]
    public void ARequestNamingNobodyIsRefused(string? slugs) =>
        FamilyEndpoints.Requested(slugs).Should().BeNull();

    [Fact]
    public void ARequestNamesEachPersonOnce() =>
        FamilyEndpoints.Requested("moses, aaron,moses").Should().Equal("moses", "aaron");

    [Fact]
    public void ARequestForMoreThanOneRingIsRefused() =>
        FamilyEndpoints.Requested(string.Join(',', Enumerable.Range(0, FamilyEndpoints.MostPeople + 1)
            .Select(i => $"p{i}"))).Should().BeNull();

    private Entity Person(string slug, string sex)
    {
        var entity = new Entity
        {
            Kind = EntityKind.Person,
            Slug = slug,
            Name = char.ToUpperInvariant(slug[0]) + slug[1..],
            Sex = sex,
            SourceId = $"test:{slug}",
            Source = "test",
        };
        _db.Entities.Add(entity);
        return entity;
    }

    private void Tie(Entity from, string type, Entity to, string source = "test") =>
        _db.EntityRelationships.Add(new EntityRelationship
        {
            FromEntityId = from.Id,
            ToEntityId = to.Id,
            Type = type,
            Category = "explicit",
            Method = LinkMethod.StatedBySource,
            Source = source,
        });
}
