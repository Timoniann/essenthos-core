using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Endpoints;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Essenthos.Core.Tests.Records;

/// <summary>
/// Two different records one line names by the same name are told apart, each by the first clause
/// of its own line that does not name the line's subject: Jehoiakim's son Zedekiah and King
/// Zedekiah, his half-brother.
/// </summary>
[Collection(WitnessDatabaseCollection.Name)]
public sealed class NameClashLineTests : IDisposable
{
    private readonly AppDbContext _db;

    private readonly Dictionary<string, Entity> _records = new(StringComparer.Ordinal);

    private readonly Dictionary<int, int> _ordinals = [];

    public NameClashLineTests(WitnessDatabase database)
    {
        _db = database.NewContext();
        Clear();

        Person("eliakim-2", "Jehoiakim");
        Person("zedekiah-2", "Zedekiah");
        Person("mattaniah", "Zedekiah");
        Person("jehoiachin", "Jehoiachin");
        Person("jerahmeel", "Jerahmeel");
        _db.Entities.Add(_records["jerusalem"] = new Entity
        {
            Kind = EntityKind.Place, Slug = "jerusalem", Name = "Jerusalem", SourceId = "jerusalem", Source = "a test",
        });
        _db.SaveChanges();

        Clause("eliakim-2", DescriptorRelations.FatherOf, "jehoiachin");
        Clause("eliakim-2", DescriptorRelations.FatherOf, "zedekiah-2");
        Clause("eliakim-2", DescriptorRelations.FatherOf, "jerahmeel");
        Clause("eliakim-2", DescriptorRelations.HalfBrotherOf, "mattaniah");
        Clause("zedekiah-2", DescriptorRelations.SonOf, "eliakim-2");
        Clause("zedekiah-2", DescriptorRelations.BrotherOf, "jehoiachin");
        Clause("mattaniah", DescriptorRelations.KingOf, "jerusalem");
        _db.SaveChanges();

        foreach (var (slug, form) in new[]
                 {
                     ("zedekiah-2", "Седекія"), ("mattaniah", "Седекія"), ("jehoiachin", "Єгояхін"),
                     ("jerahmeel", "Єрахмеїл"), ("jerusalem", "Єрусалим"), ("eliakim-2", "Єгояким"),
                 })
        {
            Form(slug, GrammaticalCases.Nominative, form);
        }

        Form("zedekiah-2", GrammaticalCases.Genitive, "Седекії");
        Form("mattaniah", GrammaticalCases.Genitive, "Седекії");
        Form("jehoiachin", GrammaticalCases.Genitive, "Єгояхіна");
        Form("jerahmeel", GrammaticalCases.Genitive, "Єрахмеїла");
        Form("jerusalem", GrammaticalCases.Genitive, "Єрусалима");
        Form("eliakim-2", GrammaticalCases.Genitive, "Єгоякима");
        _db.SaveChanges();
    }

    public void Dispose()
    {
        Clear();
        _db.Dispose();
    }

    [Fact]
    public async Task TheSonAndTheKingAreToldApartInEveryLanguage()
    {
        var english = (await Descriptors.Of(_db, "eliakim-2", "eng", default))!;
        string.Concat(english.Parts.Select(p => p.Text)).Should().Be(
            "father of Jehoiachin, Zedekiah (brother of Jehoiachin and Jerahmeel) and Jerahmeel, half-brother of Zedekiah (king of Jerusalem)");
        english.Claims.Single(c => c.Target.Slug == "mattaniah").Target.Distinction.Should().Be("king of Jerusalem");
        english.Claims.Single(c => c.Target.Slug == "jehoiachin").Target.Distinction.Should().BeNull();

        string.Concat((await Descriptors.Of(_db, "eliakim-2", "ukr", default))!.Parts.Select(p => p.Text))
            .Should().Contain("Седекії (брат Єгояхіна та Єрахмеїла)").And.Contain("Седекії (цар Єрусалима)");
    }

    /// <summary>A line naming one record by a name is not touched, and nor is the record's own line.</summary>
    [Fact]
    public async Task ANameNamedOnceIsLeftAsItIs() =>
        string.Concat((await Descriptors.Of(_db, "zedekiah-2", "eng", default))!.Parts.Select(p => p.Text))
            .Should().Be("son of Jehoiakim, brother of Jehoiachin and Jerahmeel");

    private void Person(string slug, string name)
    {
        var person = new Entity { Kind = EntityKind.Person, Slug = slug, Name = name, Sex = "male", SourceId = slug, Source = "a test" };
        _db.Entities.Add(person);
        _records[slug] = person;
    }

    private void Clause(string subject, string relation, string target)
    {
        var id = _records[subject].Id;
        _ordinals[id] = _ordinals.GetValueOrDefault(id) + 1;
        _db.EntityDescriptors.Add(new EntityDescriptor
        {
            EntityId = id,
            TargetEntityId = _records[target].Id,
            Relation = relation,
            Ordinal = _ordinals[id],
            CanonicalBook = 12,
            CanonicalChapter = 24,
            CanonicalVerse = _ordinals[id],
            Method = LinkMethod.Manual,
            Source = "a test",
        });
    }

    private void Form(string slug, string grammaticalCase, string form) =>
        _db.EntityNameForms.Add(new EntityNameForm
        {
            EntityId = _records[slug].Id, Language = "ukr", GrammaticalCase = grammaticalCase, Form = form,
            Method = LinkMethod.ModelReading, Confidence = 1, Source = "a test",
        });

    private void Clear()
    {
        _db.Database.ExecuteSqlRaw("DELETE FROM text");
        _db.Database.ExecuteSqlRaw("DELETE FROM entity");
    }
}
