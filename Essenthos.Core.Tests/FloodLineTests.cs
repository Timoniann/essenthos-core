using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Endpoints;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Essenthos.Core.Tests;

/// <summary>
/// Someone who lived before the Flood is the ancestor, in his line, only of people born before it:
/// everyone born after it descends from Adam and from Noah alike, so naming them says nothing about
/// either. Noah's sons were born before it and keep the descendants their own lines name.
/// </summary>
[Collection(WitnessDatabaseCollection.Name)]
public sealed class FloodLineTests : IDisposable
{
    private readonly AppDbContext _db;

    private readonly Dictionary<string, Entity> _people = new(StringComparer.Ordinal);

    private readonly Dictionary<int, int> _ordinals = [];

    private int _verse;

    public FloodLineTests(WitnessDatabase database)
    {
        _db = database.NewContext();
        Clear();

        foreach (var (slug, name) in new[]
                 {
                     ("adam", "Adam"), ("seth", "Seth"), ("enos", "Enos"), ("kenan", "Kenan"), ("enoch-2", "Enoch"),
                     ("methuselah", "Methuselah"), ("lamech", "Lamech"), ("noah", "Noah"), ("shem", "Shem"),
                     ("ham", "Ham"), ("japheth", "Japheth"), ("arphaxad", "Arphaxad"), ("eber", "Eber"),
                     ("abram", "Abram"), ("cain", "Cain"), ("enoch", "Enoch"), ("irad", "Irad"),
                 })
        {
            Person(slug, name, "male");
        }

        Person("eve", "Eve", "female");
        _db.SaveChanges();

        string[][] lines =
        [
            ["adam", "seth", "enos", "kenan", "enoch-2", "methuselah", "lamech", "noah", "shem", "arphaxad", "eber", "abram"],
            ["noah", "ham"],
            ["noah", "japheth"],
            ["adam", "cain", "enoch", "irad"],
        ];
        foreach (var line in lines)
        {
            for (var at = 1; at < line.Length; at++)
            {
                Related(line[at], DescriptorRelations.SonOf, line[at - 1]);
            }
        }

        Related("seth", DescriptorRelations.SonOf, "eve");

        Tie("adam", DescriptorRelations.FatherOf, "seth");
        Tie("adam", DescriptorRelations.AncestorOf, "abram");
        Tie("eve", DescriptorRelations.WifeOf, "adam");
        Tie("lamech", DescriptorRelations.SonOf, "methuselah");
        Tie("noah", DescriptorRelations.SonOf, "lamech");
        Tie("shem", DescriptorRelations.SonOf, "noah");
        Tie("cain", DescriptorRelations.SonOf, "adam");
        Tie("eber", DescriptorRelations.SonOf, "arphaxad");

        foreach (var (slug, verses) in new[]
                 {
                     ("abram", 50), ("noah", 40), ("shem", 20), ("cain", 15), ("enoch-2", 10), ("ham", 9),
                     ("methuselah", 8), ("lamech", 7), ("eber", 6), ("japheth", 6), ("arphaxad", 5), ("enoch", 3),
                     ("enos", 3), ("irad", 2), ("kenan", 2),
                 })
        {
            Named(slug, verses);
        }

        _db.SaveChanges();
    }

    public void Dispose()
    {
        Clear();
        _db.Dispose();
    }

    [Fact]
    public async Task AdamIsTheAncestorOfThoseBornBeforeTheFlood()
    {
        (await Line("adam")).Should().Be("husband of Eve, father of Seth and Cain, ancestor of Noah, Shem, Enoch, Ham and others");

        var adam = (await Descriptors.Of(_db, "adam", "eng", default))!;
        adam.Claims.Should().NotContain(c => c.Target.Slug == "abram" || c.Target.Slug == "arphaxad" || c.Target.Slug == "eber");
    }

    [Fact]
    public async Task EveIsTheAncestorOfThoseBornBeforeTheFlood() =>
        (await Line("eve")).Should().Be("wife of Adam, ancestor of Noah, Shem, Enoch, Ham and others");

    [Fact]
    public async Task NoahNamesNoDescendantBeyondHisSons()
    {
        var noah = (await Descriptors.Of(_db, "noah", "eng", default))!;

        string.Concat(noah.Parts.Select(p => p.Text)).Should().Be("son of Lamech, father of Shem");
        noah.Claims.Should().NotContain(c => c.Relation == DescriptorRelations.AncestorOf);
    }

    [Fact]
    public async Task NoahsFatherIsTheAncestorOfNoahsSonsAndOfNobodyAfterThem() =>
        (await Line("lamech")).Should().Be("son of Methuselah, father of Noah, ancestor of Shem, Ham and Japheth");

    [Fact]
    public async Task NoahsSonsKeepTheirDescendants() =>
        (await Line("shem")).Should().Contain("ancestor of Abram and Eber");

    [Fact]
    public async Task CainsLineEndedInTheFloodAndKeepsItsOwn() =>
        (await Line("cain")).Should().Contain("ancestor of Irad");

    [Fact]
    public async Task SomebodyAfterTheFloodIsTheAncestorOfWhomeverTheTreeReaches() =>
        (await Line("arphaxad")).Should().Contain("ancestor of Abram");

    private async Task<string> Line(string slug) =>
        string.Concat((await Descriptors.Of(_db, slug, "eng", default))!.Parts.Select(p => p.Text));

    private void Person(string slug, string name, string sex)
    {
        var person = new Entity { Kind = EntityKind.Person, Slug = slug, Name = name, Sex = sex, SourceId = slug, Source = "a test" };
        _db.Entities.Add(person);
        _people[slug] = person;
    }

    private void Tie(string subject, string relation, string target)
    {
        var id = _people[subject].Id;
        _ordinals[id] = _ordinals.GetValueOrDefault(id) + 1;
        _db.EntityDescriptors.Add(new EntityDescriptor
        {
            EntityId = id,
            TargetEntityId = _people[target].Id,
            Relation = relation,
            Ordinal = _ordinals[id],
            CanonicalBook = 1,
            CanonicalChapter = 5,
            CanonicalVerse = ++_verse,
            Method = LinkMethod.Manual,
            Source = "a test",
        });
    }

    private void Related(string from, string type, string to) =>
        _db.EntityRelationships.Add(new EntityRelationship
        {
            FromEntityId = _people[from].Id,
            ToEntityId = _people[to].Id,
            Type = type,
            Category = RelationshipCategories.Read,
            CanonicalBook = 1,
            CanonicalChapter = 5,
            CanonicalVerse = ++_verse,
            Method = LinkMethod.Manual,
            Source = "a test",
        });

    private void Named(string slug, int verses)
    {
        for (var verse = 1; verse <= verses; verse++)
        {
            _db.EntityVerses.Add(new EntityVerse
            {
                EntityId = _people[slug].Id, CanonicalBook = 9, CanonicalChapter = 1, CanonicalVerse = verse, Source = "a test",
            });
        }
    }

    private void Clear()
    {
        _db.Database.ExecuteSqlRaw("DELETE FROM text");
        _db.Database.ExecuteSqlRaw("DELETE FROM entity");
    }
}
