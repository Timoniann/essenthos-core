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
        father.Ties.Select(Who).Should().BeEquivalentTo([("husband-of", false, "sarah"), ("son-of", true, "isaac")]);
        family.People[0].Ties.Select(Who).Should().Equal(("son-of", false, "abraham"));
    }

    /// <summary>
    /// Two readings stating one fact in one word are one tie to a tree, which only asks who.
    /// </summary>
    [Fact]
    public async Task ATieStatedTwiceInOneWordIsOneTie()
    {
        var lot = Person("lot", "male");
        var haran = Person("haran", "male");
        await _db.SaveChangesAsync();
        Tie(lot, "son-of", haran);
        Tie(lot, "son-of", haran, source: "read from Scripture by a second test");
        await _db.SaveChangesAsync();

        var family = await FamilyEndpoints.Family(_db, ["lot"], null, default);

        family.People.Single().Ties.Should().ContainSingle();
    }

    /// <summary>
    /// God is nobody's kin on a tree: "his Son" is not descent, and a tree that read it so stood God
    /// the Father beside Joseph as Jesus's other parent. What else is said of him stays.
    /// </summary>
    [Fact]
    public async Task GodIsNobodysKin()
    {
        var father = Person("yhvh-2", "male");
        var jesus = Person("jesus", "male");
        var mary = Person("mary", "female");
        var james = Person("james-3", "male");
        await _db.SaveChangesAsync();
        Tie(father, "father-of", jesus);
        Tie(jesus, "son-of", mary);
        Tie(james, "servant-of", father);
        await _db.SaveChangesAsync();

        var family = await FamilyEndpoints.Family(_db, ["jesus", "yhvh-2"], null, default);

        family.People[0].Ties.Select(Who).Should().Equal(("son-of", false, "mary"));
        family.People[1].Ties.Select(Who).Should().Equal(("servant-of", true, "james-3"));
    }

    /// <summary>
    /// A tie keeps the verse it was read from, so a tree can say which account gives which father:
    /// Jacob in Matthew 1:16, Heli in Luke 3:23.
    /// </summary>
    [Fact]
    public async Task ATieCarriesTheVerseItWasReadFrom()
    {
        var joseph = Person("joseph-6", "male");
        var jacob = Person("jacob-2", "male");
        var heli = Person("eli-2", "male");
        await _db.SaveChangesAsync();
        Tie(joseph, "son-of", jacob, verse: (40, 1, 16));
        Tie(joseph, "son-of", heli, verse: (42, 3, 23));
        await _db.SaveChangesAsync();

        var ties = (await FamilyEndpoints.Family(_db, ["joseph-6"], null, default)).People.Single().Ties;

        ties.Select(t => (t.Slug, t.Reference?.Slug, t.Reference?.Chapter, t.Reference?.Verse)).Should().Equal(
            ("jacob-2", "matthew", 1, 16),
            ("eli-2", "luke", 3, 23));
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

    private static (string Type, bool Inward, string Slug) Who(FamilyTieResponse tie) => (tie.Type, tie.Inward, tie.Slug);

    private void Tie(
        Entity from,
        string type,
        Entity to,
        string source = "read from Scripture by a test",
        (int Book, int Chapter, int Verse)? verse = null) =>
        _db.EntityRelationships.Add(new EntityRelationship
        {
            FromEntityId = from.Id,
            ToEntityId = to.Id,
            Type = type,
            Category = RelationshipCategories.Read,
            Method = LinkMethod.Manual,
            Source = source,
            CanonicalBook = verse?.Book ?? 1,
            CanonicalChapter = verse?.Chapter ?? 1,
            CanonicalVerse = verse?.Verse ?? 1,
        });
}
