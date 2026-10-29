using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Endpoints;
using Essenthos.Core.Loading.Encyclopedia;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Essenthos.Core.Tests.Records;

/// <summary>
/// Whom a people came from, where Strong derives its name from a man's: one man of the name is the
/// ancestor, of several the one the people's record names, and otherwise none.
/// </summary>
[Collection(WitnessDatabaseCollection.Name)]
public sealed class PeopleAncestorLoaderTests : IDisposable
{
    private readonly AppDbContext _db;

    /// <summary>
    /// The shapes the corpus holds: the Moabites from Moab, whose name a land bears too; the
    /// Simeonites, whose tribe record names Simeon among the men of his name; the Hebrews from an
    /// Eber the corpus holds five of; the Anakim, whose descent a verse already states; the
    /// Adullamites from a place; the Agagites from a name Strong could not say was a man's or a
    /// place's; and the Edomites from a name only a land bears here.
    /// </summary>
    public PeopleAncestorLoaderTests(WitnessDatabase database)
    {
        _db = database.NewContext();
        _db.Database.ExecuteSqlRaw("DELETE FROM entity");
        _db.Database.ExecuteSqlRaw("DELETE FROM strong_gentilic");

        Record("moab", EntityKind.Person, "H4124");
        Record("moab-2", EntityKind.Place, "H4124");
        var simeon = Record("simeon", EntityKind.Person, "H8095");
        Record("simeon-2", EntityKind.Person, "H8095");
        Record("eber", EntityKind.Person, "H5677");
        Record("eber-2", EntityKind.Person, "H5677");
        var anak = Record("anak", EntityKind.Person, "H6061");
        Record("adullam", EntityKind.Place, "H5725");
        Record("agag", EntityKind.Person, "H90");
        Record("agag-land", EntityKind.Place, "H90");
        Record("edom", EntityKind.Place, "H123");

        People("moabites", "H4125", "patronymic", "H4124");
        People("simeonites", "H8099", "patronymic", "H8095", origin: simeon);
        People("hebrews", "H5680", "patronymic", "H5677");
        var anakim = People("anakim", "H6062", "patronymic", "H6061");
        People("adullamites", "H5726", "patrial", "H5725");
        People("agagites", "H91", "patronymic or patrial", "H90");
        People("edomites", "H130", "patronymic", "H123");
        _db.SaveChanges();

        _db.EntityRelationships.Add(new EntityRelationship
        {
            FromEntityId = anakim.Id, ToEntityId = anak.Id, Type = "descendants-of", Category = RelationshipCategories.Read,
            CanonicalBook = 5, CanonicalChapter = 9, CanonicalVerse = 2, Method = LinkMethod.ModelReading,
            Confidence = 0.9, Source = "read from Scripture by a test",
        });
        _db.SaveChanges();
    }

    public void Dispose()
    {
        _db.Database.ExecuteSqlRaw("DELETE FROM strong_gentilic");
        _db.Database.ExecuteSqlRaw("DELETE FROM entity");
        _db.Dispose();
    }

    private PeopleAncestorLoader Loader() => new(_db, NullLogger<PeopleAncestorLoader>.Instance);

    private async Task<List<(string People, string Ancestor)>> Derived()
    {
        _db.ChangeTracker.Clear();
        return (await _db.EntityRelationships
                .Where(r => r.Method == LinkMethod.StatedBySource && r.Source == PeopleAncestorLoader.Source)
                .Select(r => new { From = r.From!.Slug, To = r.To!.Slug, r.Type, r.CanonicalBook })
                .ToListAsync())
            .Select(r =>
            {
                r.Type.Should().Be("descendants-of");
                r.CanonicalBook.Should().BeNull("Strong's statement is no verse's");
                return (r.From, r.To);
            })
            .OrderBy(r => r.From, StringComparer.Ordinal)
            .ToList();
    }

    [Fact]
    public async Task APeopleComesFromTheOneManItsNameIsDerivedFromAndAnAmbiguousNameIsLeftOpen()
    {
        var first = await Loader().Load();

        (await Derived()).Should().Equal(("moabites", "moab"), ("simeonites", "simeon"));
        first.Read.Should().Be(1, "the Anakim's descent is read from a verse");
        var ambiguous = first.Ambiguous.Should().ContainSingle().Which;
        ambiguous.People.Should().Be("hebrews");
        ambiguous.Persons.Should().Equal("eber", "eber-2");
        first.Unheld.Should().Equal("edomites");

        var second = await Loader().Load();
        second.Written.Should().Be(0);
        second.Withdrawn.Should().Be(0);
    }

    [Fact]
    public async Task TheDerivationGivesWayOnceAVerseStatesTheDescent()
    {
        await Loader().Load();
        var moabites = await _db.Entities.SingleAsync(e => e.Slug == "moabites");
        var moab = await _db.Entities.SingleAsync(e => e.Slug == "moab");
        _db.EntityRelationships.Add(new EntityRelationship
        {
            FromEntityId = moabites.Id, ToEntityId = moab.Id, Type = "descendants-of", Category = RelationshipCategories.Read,
            CanonicalBook = 1, CanonicalChapter = 19, CanonicalVerse = 37, Method = LinkMethod.Manual,
            Source = "read from Scripture by a test",
        });
        await _db.SaveChangesAsync();

        var outcome = await Loader().Load();

        outcome.Withdrawn.Should().Be(1);
        (await Derived()).Should().Equal(("simeonites", "simeon"));
    }

    /// <summary>
    /// The Moabites' line says whom they came from, Moab a link, in the reader's language; Strong's
    /// statement cites no verse. The Anakim's line, whose descent a verse states, is not said twice.
    /// </summary>
    [Theory]
    [InlineData("eng", "descendants of moab")]
    [InlineData("ukr", "нащадки moab")]
    public async Task APeoplesLineNamesTheManItCameFrom(string language, string expected)
    {
        await Loader().Load();

        var line = (await Descriptors.Of(_db, "moabites", language, default))!;

        string.Concat(line.Parts.Select(p => p.Text)).Should().Be(expected);
        line.Parts.Should().Contain(p => p.Entity != null && p.Entity.Slug == "moab");
        line.Claims.Should().ContainSingle().Which.Should().Match<DescriptorClaimResponse>(c =>
            c.Relation == "descendants-of" && c.Reference == null && c.Method == "stated-by-source");
        (await Descriptors.Of(_db, "anakim", "eng", default)).Should().BeNull(
            "a relationship is no clause of the line, and Strong's is not written beside a verse's");
    }

    private Entity Record(string slug, EntityKind kind, string number)
    {
        var entity = new Entity { Kind = kind, Slug = slug, Name = slug, SourceId = slug, Source = "a test" };
        entity.Names.Add(new EntityName { Label = slug, HebrewStrongNumber = number, Kind = "proper name" });
        _db.Entities.Add(entity);
        return entity;
    }

    private Entity People(string slug, string number, string kind, string from, Entity? origin = null)
    {
        var entity = new Entity
        {
            Kind = EntityKind.People, Slug = slug, Name = slug, SourceId = slug, Source = "a test", Origin = origin,
        };
        _db.Entities.Add(entity);
        _db.StrongGentilics.Add(new StrongGentilic
        {
            StrongNumber = number, OriginNumber = from, Kind = kind, Statement = $"{kind} from {from}",
            Source = PeopleAncestorLoader.Source, People = entity,
        });
        return entity;
    }
}
