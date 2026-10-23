using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Endpoints;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Essenthos.Core.Tests.Records;

/// <summary>
/// A place's own line says where it is: by the land it lies in, or by a neighbour better known than
/// itself, and never by the place its own verse locates by it.
/// </summary>
[Collection(WitnessDatabaseCollection.Name)]
public sealed class PlaceLineTests : IDisposable
{
    private readonly AppDbContext _db;

    private readonly Dictionary<string, Entity> _records = new(StringComparer.Ordinal);

    private readonly Dictionary<int, int> _ordinals = [];

    public PlaceLineTests(WitnessDatabase database)
    {
        _db = database.NewContext();
        Clear();

        Place("bethlehem", "Bethlehem", "settlement", Named(40, 1, 50));
        Place("geruthchimham", "Geruth Chimham", "natural area", (24, 41, 17));
        Place("judea", "Judea", "region", Named(40, 2, 30));
        Place("jerusalem", "Jerusalem", "settlement", Named(19, 1, 120));
        Place("jericho-2", "Jericho", "settlement", (42, 10, 30), (40, 20, 29));
        Place("moriah", "Moriah", "region", (14, 3, 1));
        Place("maon", "Maon", "settlement", (6, 15, 55), (9, 25, 2));
        Place("wilderness-of-maon", "Wilderness of Maon", "region", (9, 23, 24), (9, 23, 25));
        Place("bethlehem-2", "Bethlehem", "settlement", (6, 19, 15));
        Place("kattath", "Kattath", "settlement", (6, 19, 15), (7, 1, 30));
        Person("zebulun", "Zebulun", (1, 30, 20), (1, 35, 23));
        _db.SaveChanges();

        Clause("bethlehem", DescriptorRelations.Near, "geruthchimham", (24, 41, 17), 0.7);
        Clause("geruthchimham", DescriptorRelations.Near, "bethlehem", (24, 41, 17), 0.85);
        Clause("judea", DescriptorRelations.RegionOf, "bethlehem", (40, 2, 1), 0.6);
        Clause("jericho-2", DescriptorRelations.Near, "jerusalem", (42, 10, 30), 0.5);
        Clause("moriah", DescriptorRelations.RegionOf, "jerusalem", (14, 3, 1), 0.55);
        Clause("wilderness-of-maon", DescriptorRelations.RegionOf, "maon", (9, 23, 24), 0.85);
        _db.SaveChanges();

        _db.EntityNameForms.AddRange(
            Form("judea", GrammaticalCases.Locative, "Юдеї"),
            Form("bethlehem", GrammaticalCases.Genitive, "Віфлеєма"),
            Form("zebulun", GrammaticalCases.Genitive, "Завулона"));
        _db.SaveChanges();
    }

    public void Dispose()
    {
        Clear();
        _db.Dispose();
    }

    /// <summary>
    /// Jeremiah 41:17 puts Geruth Chimham near Bethlehem. Bethlehem's line says where it is by the
    /// region Matthew names it in, which was written on Judaea the other way round.
    /// </summary>
    [Fact]
    public async Task BethlehemIsInJudaeaAndGeruthChimhamIsNearBethlehem()
    {
        (await Line("bethlehem")).Should().Be("a city in Judea");
        (await Line("bethlehem", "ukr")).Should().Be("місто в Юдеї");
        (await Line("geruthchimham")).Should().Be("near Bethlehem");

        var bethlehem = (await Descriptors.Of(_db, "bethlehem", "eng", default))!;
        bethlehem.Claims.Should().ContainSingle()
            .Which.Should().Match<DescriptorClaimResponse>(c => c.Relation == DescriptorRelations.CityIn
                                                               && c.Stated == DescriptorRelations.RegionOf
                                                               && c.Target.Slug == "judea");
        (await Descriptors.Of(_db, "judea", "eng", default)).Should().BeNull(
            "Judaea's own line no longer calls it a region of Bethlehem");
    }

    /// <summary>A doubtful <em>near</em> is no place's line: the road from Jerusalem down to Jericho locates neither.</summary>
    [Fact]
    public async Task ADoubtfulNeighbourIsNotWhereAPlaceIs() =>
        (await Descriptors.Of(_db, "jericho-2", "eng", default)).Should().BeNull();

    /// <summary>
    /// A region named after the town is the town's (the wilderness of Maon), and one named far less
    /// than the town is a part of it (Moriah): neither is turned onto the town's line.
    /// </summary>
    [Fact]
    public async Task ARegionOfATownIsTurnedOnlyWhereItIsTheLandTheTownLiesIn()
    {
        (await Descriptors.Of(_db, "jerusalem", "eng", default)).Should().BeNull();
        (await Descriptors.Of(_db, "maon", "eng", default)).Should().BeNull();
        (await Line("moriah")).Should().Be("a region of Jerusalem");
        (await Line("wilderness-of-maon")).Should().Be("a region of Maon");
    }

    /// <summary>
    /// The second Bethlehem is named once, in the list of Zebulun's towns, and its line says so; a
    /// town named elsewhere too is not placed by the list alone.
    /// </summary>
    [Fact]
    public async Task ATownNamedOnlyInATribesInheritanceIsInIt()
    {
        (await Line("bethlehem-2")).Should().Be("a town in the inheritance of Zebulun");
        (await Line("bethlehem-2", "ukr")).Should().Be("місто в наділі Завулона");
        (await Descriptors.Of(_db, "kattath", "eng", default)).Should().BeNull();
    }

    private async Task<string> Line(string slug, string language = "eng") =>
        string.Concat((await Descriptors.Of(_db, slug, language, default))!.Parts.Select(p => p.Text));

    private static (int, int, int)[] Named(int book, int chapter, int verses) =>
        [.. Enumerable.Range(1, verses).Select(verse => (book, chapter, verse))];

    private void Place(string slug, string name, string kind, params (int Book, int Chapter, int Verse)[] verses) =>
        Record(new Entity { Kind = EntityKind.Place, Slug = slug, Name = name, PlaceKind = kind, SourceId = slug, Source = "a test" }, verses);

    private void Person(string slug, string name, params (int Book, int Chapter, int Verse)[] verses) =>
        Record(new Entity { Kind = EntityKind.Person, Slug = slug, Name = name, Sex = "male", SourceId = slug, Source = "a test" }, verses);

    private void Record(Entity entity, (int Book, int Chapter, int Verse)[] verses)
    {
        _db.Entities.Add(entity);
        _records[entity.Slug] = entity;
        foreach (var (book, chapter, verse) in verses)
        {
            entity.Verses.Add(new EntityVerse
            {
                CanonicalBook = book, CanonicalChapter = chapter, CanonicalVerse = verse, Source = "a test",
            });
        }
    }

    private void Clause(string subject, string relation, string target, (int Book, int Chapter, int Verse) at, double confidence)
    {
        var id = _records[subject].Id;
        _ordinals[id] = _ordinals.GetValueOrDefault(id) + 1;
        _db.EntityDescriptors.Add(new EntityDescriptor
        {
            EntityId = id,
            TargetEntityId = _records[target].Id,
            Relation = relation,
            Ordinal = _ordinals[id],
            CanonicalBook = at.Book,
            CanonicalChapter = at.Chapter,
            CanonicalVerse = at.Verse,
            Method = LinkMethod.ModelReading,
            Confidence = confidence,
            Source = "read from Scripture by a test",
        });
    }

    private EntityNameForm Form(string slug, string grammaticalCase, string form) =>
        new()
        {
            EntityId = _records[slug].Id, Language = "ukr", GrammaticalCase = grammaticalCase, Form = form,
            Method = LinkMethod.ModelReading, Confidence = 1, Source = "a test",
        };

    private void Clear()
    {
        _db.Database.ExecuteSqlRaw("DELETE FROM text");
        _db.Database.ExecuteSqlRaw("DELETE FROM entity");
    }
}
