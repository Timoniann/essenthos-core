using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Endpoints;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Essenthos.Core.Tests.Records;

/// <summary>
/// A people's line says whom it came from, the man a link: from a clause read off a verse where its
/// record has one, and otherwise from the origin its record names, which Strong's Dictionary states
/// for a gentilic and the owner's file for a tribe, citing no verse.
/// </summary>
[Collection(WitnessDatabaseCollection.Name)]
public sealed class PeopleAncestorLineTests : IDisposable
{
    private const string Strong = "Strong's Hebrew dictionary by James Strong, 1890, public domain";
    private const string Tribes = "Essenthos, on the project owner's decision of 2026-09-06 that a people is a kind of entity";

    private readonly AppDbContext _db;

    public PeopleAncestorLineTests(WitnessDatabase database)
    {
        _db = database.NewContext();
        _db.Database.ExecuteSqlRaw("DELETE FROM entity");

        var moab = Record("moab", EntityKind.Person, "Moab");
        var gad = Record("gad", EntityKind.Person, "Gad");
        var anak = Record("anak", EntityKind.Person, "Anak");
        var moabites = Record("moabites", EntityKind.People, "Moabites", moab);
        moabites.Claims.Add(new EntityClaim { Method = LinkMethod.StatedBySource, Source = Strong, Note = "H4125, patronymic of H4124" });
        Record("gadites", EntityKind.People, "Gadites", gad);
        var anakim = Record("anakim", EntityKind.People, "Anakim", anak);
        _db.SaveChanges();

        _db.EntityDescriptors.Add(new EntityDescriptor
        {
            EntityId = anakim.Id, Ordinal = 1, Relation = DescriptorRelations.DescendantsOf, TargetEntityId = anak.Id,
            CanonicalBook = 4, CanonicalChapter = 13, CanonicalVerse = 22, Method = LinkMethod.ModelReading,
            Confidence = 0.9, Source = "read from Scripture by a test",
        });
        _db.SaveChanges();
    }

    public void Dispose()
    {
        _db.Database.ExecuteSqlRaw("DELETE FROM entity");
        _db.Dispose();
    }

    [Theory]
    [InlineData("eng", "descendants of Moab")]
    [InlineData("ukr", "нащадки Moab")]
    public async Task AGentilicsLineNamesTheManStrongDerivesItFromCitingNoVerse(string language, string expected)
    {
        var line = (await Descriptors.Of(_db, "moabites", language, default))!;

        string.Concat(line.Parts.Select(p => p.Text)).Should().Be(expected);
        line.Parts.Should().Contain(p => p.Entity != null && p.Entity.Slug == "moab");
        line.Claims.Should().ContainSingle().Which.Should().Match<DescriptorClaimResponse>(c =>
            c.Relation == DescriptorRelations.DescendantsOf && c.Reference == null
            && c.Method == "stated-by-source" && c.Source == Strong);
    }

    [Fact]
    public async Task ATribesLineNamesItsForebearOnTheOwnersFile()
    {
        var line = (await Descriptors.Of(_db, "gadites", "eng", default))!;

        line.Claims.Should().ContainSingle().Which.Should().Match<DescriptorClaimResponse>(c =>
            c.Target.Slug == "gad" && c.Method == "manual" && c.Source == Tribes && c.Reference == null);
    }

    /// <summary>The Anakim's verse says it; the origin is not said beside it a second time.</summary>
    [Fact]
    public async Task AVerseThatStatesTheDescentIsSaidAlone()
    {
        var line = (await Descriptors.Of(_db, "anakim", "eng", default))!;

        line.Claims.Should().ContainSingle().Which.Reference.Should().NotBeNull();
    }

    private Entity Record(string slug, EntityKind kind, string name, Entity? origin = null)
    {
        var entity = new Entity
        {
            Kind = kind, Slug = slug, Name = name, SourceId = slug, Origin = origin,
            Source = kind == EntityKind.People && slug == "gadites" ? Tribes : "a test",
        };
        _db.Entities.Add(entity);
        return entity;
    }
}
