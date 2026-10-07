using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Endpoints;
using FluentAssertions;
using Microsoft.EntityFrameworkCore.Storage;
using Xunit;

namespace Essenthos.Core.Tests;

/// <summary>
/// The families among a chapter's people: who is one tree with whom, and who is drawn to join them.
/// </summary>
public sealed class ChapterFamilyTreesTests
{
    private static ChapterFamily.Tie Parent(int parent, int child) => new(parent, child, 1);

    private static ChapterFamily.Tie Child(int child, int parent) => new(child, parent, -1);

    private static ChapterFamily.Tie Side(int a, int b) => new(a, b, 0);

    /// <summary>
    /// A great-grandfather and his great-grandson are three steps apart and one tree, with the two
    /// men between them drawn to join them.
    /// </summary>
    [Fact]
    public void ThreeStepsApartIsOneTreeWithEveryoneBetween()
    {
        var trees = ChapterFamily.Trees(new HashSet<int> { 1, 4 }, [Parent(1, 2), Child(3, 2), Parent(3, 4)]);

        trees.Should().ContainSingle();
        trees[0].Named.Should().Equal(1, 4);
        trees[0].Between.Should().Equal(2, 3);
    }

    [Fact]
    public void FourStepsApartIsNoTreeAtAll()
    {
        var trees = ChapterFamily.Trees(
            new HashSet<int> { 1, 5 }, [Parent(1, 2), Side(2, 3), Parent(3, 4), Parent(4, 5)]);

        trees.Should().BeEmpty();
    }

    /// <summary>
    /// Matthew's <em>Joram begat Ozias</em> passes over three kings, and the two are still one line:
    /// a forebear and his descendant four generations down share a tree, with the three between.
    /// </summary>
    [Fact]
    public void AGenealogyThatPassesOverGenerationsIsStillOneLine()
    {
        var trees = ChapterFamily.Trees(
            new HashSet<int> { 1, 5 }, [Parent(1, 2), Child(3, 2), Parent(3, 4), Parent(4, 5)]);

        trees.Should().ContainSingle();
        trees[0].Named.Should().Equal(1, 5);
        trees[0].Between.Should().Equal(2, 3, 4);
    }

    [Fact]
    public void FiveGenerationsApartIsNoTreeAtAll()
    {
        var trees = ChapterFamily.Trees(
            new HashSet<int> { 1, 6 }, [Parent(1, 2), Parent(2, 3), Parent(3, 4), Parent(4, 5), Parent(5, 6)]);

        trees.Should().BeEmpty();
    }

    /// <summary>
    /// Nearness is not only direct: a chain of the chapter's own people, each close to the next,
    /// makes one tree of people further apart than three steps.
    /// </summary>
    [Fact]
    public void AChainOfNearPeopleIsOneTree()
    {
        var trees = ChapterFamily.Trees(
            new HashSet<int> { 1, 3, 6 }, [Parent(1, 2), Parent(2, 3), Parent(3, 4), Parent(4, 5), Parent(5, 6)]);

        trees.Should().ContainSingle();
        trees[0].Named.Should().Equal(1, 3, 6);
        trees[0].Between.Should().Equal(2, 4, 5);
    }

    /// <summary>
    /// Revelation 7 names Naphtali, Reuben and Judah and not Dan: Jacob and Leah are drawn as parents
    /// two of them share, and Bilhah, Naphtali's mother with no other son on the tree, is drawn beside
    /// Jacob too rather than being the one mother left out.
    /// </summary>
    [Fact]
    public void AChildDrawnUnderOneParentIsDrawnWithTheOther()
    {
        const int jacob = 1, bilhah = 2, naphtali = 3, leah = 4, reuben = 5, judah = 6;
        var trees = ChapterFamily.Trees(
            new HashSet<int> { naphtali, reuben, judah },
            [
                Parent(jacob, naphtali), Parent(bilhah, naphtali), Parent(jacob, reuben), Parent(jacob, judah),
                Parent(leah, reuben), Parent(leah, judah), Side(bilhah, jacob), Side(leah, jacob),
            ]);

        trees.Should().ContainSingle();
        trees[0].Named.Should().Equal(naphtali, reuben, judah);
        trees[0].Between.Should().Equal(jacob, bilhah, leah);
    }

    [Fact]
    public void UnconnectedFamiliesAreTreesOfTheirOwnAndALoneManIsInNone()
    {
        var trees = ChapterFamily.Trees(
            new HashSet<int> { 1, 2, 10, 11, 12, 20 },
            [Parent(1, 2), Side(10, 11), Child(12, 10), Parent(20, 21)]);

        trees.Select(t => t.Named).Should().BeEquivalentTo(
            new[] { new[] { 10, 11, 12 }, new[] { 1, 2 } }, options => options.WithStrictOrdering());
    }

    /// <summary>A wife and her husband's brother are two steps apart through him, by marriage and by blood alike.</summary>
    [Fact]
    public void MarriageAndBrotherhoodAreStepsLikeDescent()
    {
        var trees = ChapterFamily.Trees(new HashSet<int> { 1, 3 }, [Side(1, 2), Side(2, 3)]);

        trees.Should().ContainSingle();
        trees[0].Between.Should().Equal(2);
    }

    /// <summary>
    /// Two brothers the text calls brothers are one step apart, and their father is drawn over them
    /// though the chapter never names him.
    /// </summary>
    [Fact]
    public void ASharedParentIsDrawnOverBrothers()
    {
        var trees = ChapterFamily.Trees(new HashSet<int> { 1, 2 }, [Side(1, 2), Child(1, 9), Parent(9, 2), Child(1, 8)]);

        trees.Should().ContainSingle();
        trees[0].Between.Should().Equal(9);
    }

    /// <summary>Only the shortest ways between two people are drawn, not every way three steps allow.</summary>
    [Fact]
    public void OnlyTheShortestWaysAreDrawn()
    {
        var trees = ChapterFamily.Trees(
            new HashSet<int> { 1, 3 }, [Parent(1, 2), Parent(2, 3), Side(1, 7), Side(7, 8), Side(8, 3)]);

        trees[0].Between.Should().Equal(2);
    }
}

/// <summary>The chapter's families as the endpoint answers them, from the corpus.</summary>
[Collection(WitnessDatabaseCollection.Name)]
public sealed class ChapterFamilyEndpointTests : IDisposable
{
    private const int Genesis = 1;

    private readonly AppDbContext _db;
    private readonly IDbContextTransaction _transaction;

    public ChapterFamilyEndpointTests(WitnessDatabase database)
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
    /// Noah and his grandson Gomer are named in the chapter and Japheth is not: he is drawn between
    /// them, his tie to his wife is left off because she is in no tree, and his crown stays.
    /// </summary>
    [Fact]
    public async Task TheChaptersPeopleAreJoinedThroughThoseItDoesNotName()
    {
        var noah = Person("noah");
        var japheth = Person("japheth");
        var gomer = Person("gomer");
        var wife = Person("wife-of-japheth");
        var babel = new Entity { Kind = EntityKind.Place, Slug = "babel", Name = "Babel", SourceId = "test:babel", Source = "test" };
        _db.Entities.Add(babel);
        var loner = Person("loner");
        await _db.SaveChangesAsync();
        Tie(japheth, "son-of", noah);
        Tie(japheth, "father-of", gomer);
        Tie(japheth, "husband-of", wife);
        Tie(japheth, "king-of", babel);
        Tie(gomer, "descendant-of", noah);
        foreach (var named in (Entity[])[noah, gomer, loner])
        {
            NamedAt(named, 10, 1);
        }

        await _db.SaveChangesAsync();

        var family = await ChapterFamily.Of(_db, Genesis, 10, null, default);

        family.Trees.Should().ContainSingle();
        family.Trees[0].Named.Should().Equal("gomer", "noah");
        family.Trees[0].Between.Should().Equal("japheth");
        family.People.Select(p => p.Slug).Should().BeEquivalentTo("noah", "japheth", "gomer");
        family.People.Single(p => p.Slug == "japheth").Ties.Select(t => (t.Type, t.Slug)).Should().BeEquivalentTo(
        [
            ("son-of", "noah"),
            ("father-of", "gomer"),
            ("king-of", "babel"),
        ]);
        family.People.Single(p => p.Slug == "gomer").Ties.Should().NotContain(t => t.Type == "descendant-of");
    }

    /// <summary>
    /// Mary is recorded as the mother of Jesus and of Simon, and the chapter names the two brothers:
    /// she is their mother, drawn over them though the chapter does not name her.
    /// </summary>
    [Fact]
    public async Task AMotherTheChapterDoesNotNameIsDrawnOverHerSons()
    {
        var mary = Person("mary");
        var jesus = Person("jesus");
        var simon = Person("simon");
        await _db.SaveChangesAsync();
        Tie(mary, "mother-of", jesus);
        Tie(mary, "mother-of", simon);
        Tie(simon, "brother-of", jesus);
        NamedAt(jesus, 13, 55);
        NamedAt(simon, 13, 55);
        await _db.SaveChangesAsync();

        var family = await ChapterFamily.Of(_db, Genesis, 13, null, default);

        family.Trees.Should().ContainSingle();
        family.Trees[0].Between.Should().Equal("mary");
    }

    private Entity Person(string slug)
    {
        var entity = new Entity
        {
            Kind = EntityKind.Person,
            Slug = slug,
            Name = char.ToUpperInvariant(slug[0]) + slug[1..],
            Sex = "male",
            SourceId = $"test:{slug}",
            Source = "test",
        };
        _db.Entities.Add(entity);
        return entity;
    }

    private void Tie(Entity from, string type, Entity to) =>
        _db.EntityRelationships.Add(new EntityRelationship
        {
            FromEntityId = from.Id,
            ToEntityId = to.Id,
            Type = type,
            Category = RelationshipCategories.Read,
            CanonicalBook = Genesis,
            CanonicalChapter = 1,
            CanonicalVerse = 1,
            Method = LinkMethod.Manual,
            Source = "read from Scripture by a test",
        });

    private void NamedAt(Entity entity, int chapter, int verse) =>
        _db.EntityVerses.Add(new EntityVerse
        {
            Entity = entity,
            CanonicalBook = Genesis,
            CanonicalChapter = chapter,
            CanonicalVerse = verse,
            Source = "test",
        });
}
