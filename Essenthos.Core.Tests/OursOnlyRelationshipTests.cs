using Essenthos.Core.Configuration;
using Essenthos.Core.Corpus;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Endpoints;
using Essenthos.Core.Loading.Encyclopedia;
using FluentAssertions;
using Microsoft.EntityFrameworkCore.Storage;
using Xunit;

namespace Essenthos.Core.Tests;

/// <summary>
/// Relationships as this project reads them, since 2026-09-29 the only way they are shown: a tie
/// only BibleData states leaves the page, the card and the tree, and one it shares with ours is
/// ours alone.
/// </summary>
[Collection(WitnessDatabaseCollection.Name)]
public sealed class OursOnlyRelationshipTests : IDisposable
{
    private const string OwnerDecided = "read from Scripture by the project owner, decided 2026-09-29";

    private readonly AppDbContext _db;
    private readonly IDbContextTransaction _transaction;

    public OursOnlyRelationshipTests(WitnessDatabase database)
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
    public void NoSwitchBringsTheDatasetBack() =>
        SiteSettings.Knows("relationshipsOursOnly").Should().BeFalse();

    /// <summary>
    /// Lot is Haran's son by both witnesses and Milcah's brother by BibleData alone. The
    /// page keeps the first as ours with no credit to BibleData beside it, and drops the second.
    /// </summary>
    [Fact]
    public async Task ATieOnlyTheDatasetStatesLeavesThePageAndASharedOneIsOursAlone()
    {
        var (lot, haran, milcah) = (Person("lot", "male"), Person("haran", "male"), Person("milcah", "female"));
        await _db.SaveChangesAsync();
        Tie(lot, "son", haran, BibleDataLoader.Source);
        Tie(lot, "son-of", haran, OwnerDecided);
        Tie(lot, "brother", milcah, BibleDataLoader.Source);
        await _db.SaveChangesAsync();

        var both = await Relationships.Of(_db, lot.Id, null, default);
        var ours = await Relationships.Of(_db, lot.Id, null, default, oursOnly: true);

        both.Select(r => r.Slug).Should().BeEquivalentTo(["haran", "milcah"]);
        both.Single(r => r.Slug == "haran").Corroboration.Should().NotBeEmpty();
        ours.Should().ContainSingle().Which.Should().Match<EntityRelationshipResponse>(
            r => r.Slug == "haran" && r.Type == "son-of" && r.Source == OwnerDecided && r.Corroboration!.Count == 0);
    }

    /// <summary>The tree asks only who: a relative only BibleData names is not drawn.</summary>
    [Fact]
    public async Task TheTreeDrawsNoRelativeOnlyTheDatasetNames()
    {
        var (lot, haran, milcah) = (Person("lot", "male"), Person("haran", "male"), Person("milcah", "female"));
        await _db.SaveChangesAsync();
        Tie(lot, "son-of", haran, OwnerDecided);
        Tie(lot, "brother", milcah, BibleDataLoader.Source);
        await _db.SaveChangesAsync();

        var both = await FamilyEndpoints.Family(_db, ["lot"], null, default);
        var ours = await FamilyEndpoints.Family(_db, ["lot"], null, default, oursOnly: true);

        both.People.Single().Ties.Select(t => t.Slug).Should().BeEquivalentTo(["haran", "milcah"]);
        ours.People.Single().Ties.Select(t => (t.Type, t.Slug)).Should().Equal(("son-of", "haran"));
    }

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

    private void Tie(Entity from, string type, Entity to, string source) =>
        _db.EntityRelationships.Add(new EntityRelationship
        {
            FromEntityId = from.Id,
            ToEntityId = to.Id,
            Type = type,
            Category = "explicit",
            Method = LinkMethod.StatedBySource,
            Source = source,
            CanonicalBook = 1,
            CanonicalChapter = 11,
            CanonicalVerse = 27,
        });
}
