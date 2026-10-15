using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Endpoints;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Essenthos.Core.Tests;

/// <summary>
/// A half-sibling is said by the parent the two share where the corpus records one: Abram is
/// Sarah's <em>єдинокровний брат</em>, both being Terah's children.
/// </summary>
[Collection(WitnessDatabaseCollection.Name)]
public sealed class HalfSiblingPhrasingTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly Dictionary<string, Entity> _people = [];

    public HalfSiblingPhrasingTests(WitnessDatabase database)
    {
        _db = database.NewContext();
        Clear();
        foreach (var (slug, sex) in new[]
                 {
                     ("terah", "male"), ("abram", "male"), ("sarai", "female"), ("leah", "female"),
                     ("zilpah", "female"), ("gad", "male"), ("dinah", "female"), ("nobody", "male"),
                     ("other", "female"),
                 })
        {
            var person = new Entity
            {
                Kind = EntityKind.Person, Slug = slug, Name = slug, Sex = sex, SourceId = slug, Source = "a test",
            };
            _db.Entities.Add(person);
            _people[slug] = person;
        }

        _db.SaveChanges();
        foreach (var (slug, genitive) in new[] { ("sarai", "Сари"), ("dinah", "Дини"), ("other", "Іншої") })
        {
            _db.EntityNameForms.Add(new EntityNameForm
            {
                EntityId = _people[slug].Id, Language = "ukr", GrammaticalCase = GrammaticalCases.Genitive,
                Form = genitive, Method = LinkMethod.ModelReading, Confidence = 1, Source = "a test",
            });
        }

        _db.SaveChanges();
    }

    public void Dispose()
    {
        Clear();
        _db.Dispose();
    }

    private void Clear() => _db.Database.ExecuteSqlRaw("DELETE FROM entity");

    private void Tie(string from, string type, string to) =>
        _db.EntityRelationships.Add(new EntityRelationship
        {
            FromEntityId = _people[from].Id, ToEntityId = _people[to].Id, Type = type,
            Category = RelationshipCategories.Read, CanonicalBook = 1, CanonicalChapter = 11, CanonicalVerse = 27,
            Method = LinkMethod.ModelReading, Confidence = 0.9, Source = "a test",
        });

    private void Describe(string slug, string relation, string target) =>
        _db.EntityDescriptors.Add(new EntityDescriptor
        {
            EntityId = _people[slug].Id, TargetEntityId = _people[target].Id, Relation = relation, Ordinal = 1,
            CanonicalBook = 1, CanonicalChapter = 20, CanonicalVerse = 12,
            Method = LinkMethod.ModelReading, Confidence = 0.9, Source = "a test",
        });

    private async Task<string> Line(string slug, string language) =>
        string.Concat((await Descriptors.Of(_db, slug, language, default))!.Parts.Select(p => p.Text));

    [Fact]
    public async Task AFatherBothAreTheChildrenOfMakesThemHalfSiblingsByTheFather()
    {
        Tie("abram", DescriptorRelations.SonOf, "terah");
        Tie("sarai", DescriptorRelations.DaughterOf, "terah");
        Describe("abram", DescriptorRelations.HalfBrotherOf, "sarai");
        await _db.SaveChangesAsync();

        (await Line("abram", "ukr")).Should().Be("єдинокровний брат Сари");
        (await Descriptors.Of(_db, "abram", "ukr", default))!.Claims.Single().SharedParent.Should().Be(SharedParents.Father);
        (await Line("abram", "eng")).Should().Be("half-brother of sarai");
        (await Line("abram", "deu")).Should().StartWith("Halbbruder");
        (await Line("abram", "spa")).Should().StartWith("medio hermano");
    }

    [Fact]
    public async Task AMotherOnlyOneIsRecordedTheChildOfIsTheSharedParent()
    {
        Tie("zilpah", DescriptorRelations.MotherOf, "gad");
        Describe("gad", DescriptorRelations.HalfBrotherOf, "dinah");
        await _db.SaveChangesAsync();

        (await Line("gad", "ukr")).Should().Be("єдиноутробний брат Дини");
    }

    [Fact]
    public async Task AFatherOnEitherSideComesBeforeAMother()
    {
        Tie("leah", DescriptorRelations.MotherOf, "dinah");
        Tie("gad", DescriptorRelations.SonOf, "terah");
        Describe("gad", DescriptorRelations.HalfBrotherOf, "dinah");
        await _db.SaveChangesAsync();

        (await Line("gad", "ukr")).Should().Be("єдинокровний брат Дини");
    }

    [Fact]
    public async Task WithNoParentKnownTheRelationIsSaidWithoutChoosingOne()
    {
        Describe("nobody", DescriptorRelations.HalfSisterOf, "other");
        await _db.SaveChangesAsync();

        (await Line("nobody", "ukr")).Should().Be("неповнорідна сестра Іншої");
    }
}
