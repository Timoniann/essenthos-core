using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Endpoints;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Essenthos.Core.Tests;

/// <summary>
/// A person's line says the children a verse counts and the ones the records name among them, more
/// children than are named where a verse says so, a child's place where a verse states it, the
/// brothers and sisters the parents give, and the descendants the family tree reaches.
/// </summary>
[Collection(WitnessDatabaseCollection.Name)]
public sealed class FamilyKinLineTests : IDisposable
{
    private const string Male = "male";

    private const string Female = "female";

    private readonly AppDbContext _db;

    private readonly Dictionary<string, Entity> _people = new(StringComparer.Ordinal);

    private readonly Dictionary<int, int> _ordinals = [];

    private int _verse;

    public FamilyKinLineTests(WitnessDatabase database)
    {
        _db = database.NewContext();
        Clear();

        Person("gideon", "Gideon", Male, "Гедеон", "Гедеона");
        Person("jether", "Jether", Male, "Єтер", "Єтера");
        Person("abimelech", "Abimelech", Male, "Авімелех", "Авімелеха");
        Person("jotham", "Jotham", Male, "Йотам", "Йотама");
        Person("jair", "Jair", Male, "Яір", "Яіра");
        Person("job", "Job", Male, "Йов", "Йова");
        Person("jemimah", "Jemimah", Female, "Ємима", "Ємими");
        Person("keziah", "Keziah", Female, "Кеція", "Кеції");
        Person("kerenhappuch", "Kerenhappuch", Female, "Керен-Гаппух", "Керен-Гаппух");
        Person("ibzan", "Ibzan", Male, "Івцан", "Івцана");
        Person("abdon", "Abdon", Male, "Авдон", "Авдона");
        Person("adam", "Adam", Male, "Адам", "Адама");
        Person("cain", "Cain", Male, "Каїн", "Каїна");
        Person("abel", "Abel", Male, "Авель", "Авеля");
        Person("seth", "Seth", Male, "Сиф", "Сифа");
        Person("jesse", "Jesse", Male, "Єссей", "Єссея");
        string[] sons = ["eliab", "abinadab", "shammah", "nethanel", "raddai", "ozem", "david"];
        foreach (var son in sons)
        {
            Person(son, char.ToUpperInvariant(son[0]) + son[1..], Male);
        }

        Person("zeruiah", "Zeruiah", Female);
        Person("benjamin", "Benjamin", Male);
        Person("bela", "Bela", Male);
        Person("kish", "Kish", Male);
        Person("saul", "Saul", Male);
        Person("jonathan", "Jonathan", Male);
        Person("mephibosheth", "Mephibosheth", Male);
        Person("paul", "Paul", Male);
        Person("reuel", "Reuel", Male);
        Person("one", "One", Male);
        Person("two", "Two", Male);
        Person("three", "Three", Male);
        Person("four", "Four", Male);
        _db.SaveChanges();
        AddForms();

        foreach (var son in new[] { "jether", "abimelech", "jotham" })
        {
            Tie(son, DescriptorRelations.SonOf, "gideon");
        }

        Count("gideon", sons: 70);
        Count("jair", sons: 30);

        foreach (var daughter in new[] { "jemimah", "keziah", "kerenhappuch" })
        {
            Tie(daughter, DescriptorRelations.DaughterOf, "job");
        }

        Count("job", sons: 7, daughters: 3, family: "before");
        Count("job", sons: 7, daughters: 3, family: "after");
        Count("ibzan", sons: 30, daughters: 30);
        Count("ibzan", sons: 20);
        Count("abdon", sons: 40, grandsons: 30);

        foreach (var son in new[] { "cain", "abel", "seth" })
        {
            Tie("adam", DescriptorRelations.FatherOf, son);
        }

        More("adam", sons: true, daughters: true);

        foreach (var son in sons)
        {
            Tie(son, DescriptorRelations.SonOf, "jesse");
        }

        Tie("zeruiah", DescriptorRelations.SisterOf, "david");
        Count("jesse", sons: 8);
        Place("david", 7, "jesse");
        Place("eliab", 1, "jesse");

        foreach (var son in new[] { "one", "two", "three", "four" })
        {
            Tie(son, DescriptorRelations.SonOf, "reuel");
        }

        // Benjamin's tree: Bela his son; Kish of his tribe, and Saul, Jonathan and Mephibosheth after
        // him; Paul only by the owner's reading.
        Tie("bela", DescriptorRelations.SonOf, "benjamin");
        Tie("benjamin", DescriptorRelations.AncestorOf, "paul", LinkMethod.Manual);
        Related("bela", DescriptorRelations.SonOf, "benjamin");
        Related("kish", DescriptorRelations.OfTribe, "benjamin");
        Related("saul", DescriptorRelations.SonOf, "kish");
        Related("jonathan", DescriptorRelations.SonOf, "saul");
        Related("mephibosheth", DescriptorRelations.SonOf, "jonathan");
        Named("bela", 20);
        Named("paul", 12);
        Named("saul", 10);
        Named("jonathan", 8);
        Named("kish", 3);
        Named("mephibosheth", 2);
        _db.SaveChanges();
    }

    public void Dispose()
    {
        Clear();
        _db.Dispose();
    }

    [Theory]
    [InlineData("eng", "father of 70 sons, among them Jether, Abimelech and Jotham")]
    [InlineData("ukr", "батько 70 синів, з них названі Єтер, Авімелех та Йотам")]
    public async Task AVersesCountIsSaidAndTheNamedAreNamedAmongThem(string language, string expected) =>
        (await Line("gideon", language)).Should().Be(expected);

    [Fact]
    public async Task TheCountIsHandedToAClientWithItsNamesAndItsVerse()
    {
        var gideon = (await Descriptors.Of(_db, "gideon", "eng", default))!;

        var count = gideon.Counted.Should().ContainSingle().Which;
        count.Relation.Should().Be(DescriptorRelations.FatherOf);
        count.Text.Should().Be("father of 70 sons, among them Jether, Abimelech and Jotham");
        count.Parts!.Where(p => p.Entity is not null).Select(p => p.Entity!.Slug).Should().Equal("jether", "abimelech", "jotham");
        count.References.Should().ContainSingle().Which.Chapter.Should().Be(8);
        count.At.Should().BeNull();
    }

    [Fact]
    public async Task ACountOfChildrenNoneOfThemNamedIsTheWholeLine()
    {
        var jair = (await Descriptors.Of(_db, "jair", "eng", default))!;

        string.Concat(jair.Parts.Select(p => p.Text)).Should().Be("father of 30 sons");
        jair.Counted.Should().ContainSingle().Which.At.Should().Be(0);
        (await Line("jair", "ukr")).Should().Be("батько 30 синів");
    }

    [Theory]
    [InlineData("eng", "father of fourteen sons and six daughters, among them Jemimah, Keziah and Kerenhappuch")]
    [InlineData("ukr", "батько чотирнадцяти синів та шести дочок, з них названі Ємима, Кеція та Керен-Гаппух")]
    public async Task CountsOfTwoFamiliesAddUp(string language, string expected) =>
        (await Line("job", language)).Should().Be(expected);

    [Fact]
    public async Task TwoCountsOfTheSameChildrenSayTheLarger() =>
        (await Line("ibzan", "eng")).Should().Be("father of 30 sons and 30 daughters");

    [Theory]
    [InlineData("eng", "father of 40 sons, grandfather of 30 grandsons")]
    [InlineData("ukr", "батько 40 синів, дід 30 онуків")]
    [InlineData("deu", "Vater von 40 Söhnen, Großvater von 30 Enkeln")]
    [InlineData("spa", "padre de 40 hijos, abuelo de 30 nietos")]
    public async Task GrandsonsAreCountedOnlyWhereAVerseCountsThem(string language, string expected) =>
        (await Line("abdon", language)).Should().Be(expected);

    [Theory]
    [InlineData("eng", "father of Cain, Abel, Seth and other sons and daughters")]
    [InlineData("ukr", "батько Каїна, Авеля, Сифа та інших синів і дочок")]
    public async Task MoreChildrenThanNamedAreSaidAsTheVerseSaysIt(string language, string expected) =>
        (await Line("adam", language)).Should().Be(expected);

    [Theory]
    [InlineData("eng", "father of eight sons, seven of them named")]
    [InlineData("ukr", "батько восьми синів, з них названо сім")]
    [InlineData("deu", "Vater von acht Söhnen, davon sieben genannt")]
    [InlineData("spa", "padre de ocho hijos, siete de ellos nombrados")]
    public async Task ACountAboveMoreNamedThanAreListedSaysHowManyAreNamed(string language, string expected) =>
        (await Line("jesse", language)).Should().Be(expected);

    [Theory]
    [InlineData("eng", "father of four named sons")]
    [InlineData("ukr", "батько чотирьох названих синів")]
    public async Task WithoutACountTheChildrenCountedAreTheNamed(string language, string expected) =>
        (await Line("reuel", language)).Should().Be(expected);

    [Fact]
    public async Task APlaceAmongTheChildrenIsSaidWhereAVerseStatesItAndTheParentsGiveTheBrothers()
    {
        (await Line("david", "eng")).Should().Be("seventh son of Jesse, brother of six brothers and a sister");
        (await Line("eliab", "eng")).Should().StartWith("firstborn son of Jesse, brother of");
        (await Line("abinadab", "eng")).Should().StartWith("son of Jesse, brother of");
        (await Line("david", "ukr")).Should().StartWith("сьомий син Єссея");
        (await Line("david", "deu")).Should().StartWith("siebter Sohn Jesse");
    }

    [Fact]
    public async Task APlaceIsHandedToAClientAsTheWordsOfItsParentsRun()
    {
        var david = (await Descriptors.Of(_db, "david", "eng", default))!;

        david.Counted.Should().Contain(c => c.Relation == DescriptorRelations.SonOf && c.Text == "seventh son of Jesse");
        david.Claims.Should().Contain(c => c.Relation == DescriptorRelations.BrotherOf && c.Target.Slug == "eliab"
                                           && c.Source.Contains("parents") && c.Verses!.Count == 2);
    }

    [Fact]
    public async Task TheDescendantsAreTheMostNamedTheTreeReachesBelowTheChildren()
    {
        (await Line("benjamin", "eng")).Should().Be("father of Bela, ancestor of Paul, Saul, Jonathan, Kish and others");

        var benjamin = (await Descriptors.Of(_db, "benjamin", "eng", default))!;
        benjamin.Claims.Should().Contain(c => c.Relation == DescriptorRelations.AncestorOf && c.Target.Slug == "jonathan"
                                              && c.Source.Contains("tree"));
        benjamin.Claims.Should().NotContain(c => c.Target.Slug == "mephibosheth");
        benjamin.Counted.Should().Contain(c => c.Relation == DescriptorRelations.AncestorOf
                                               && c.Text == "ancestor of Paul, Saul, Jonathan, Kish and others");
    }

    private async Task<string> Line(string slug, string language) =>
        string.Concat((await Descriptors.Of(_db, slug, language, default))!.Parts.Select(p => p.Text));

    private void Person(string slug, string name, string? sex, string? nominative = null, string? genitive = null)
    {
        var person = new Entity
        {
            Kind = EntityKind.Person, Slug = slug, Name = name, Sex = sex, SourceId = slug, Source = "a test",
        };
        if (nominative is not null)
        {
            _forms.Add((person, GrammaticalCases.Nominative, nominative));
            _forms.Add((person, GrammaticalCases.Genitive, genitive!));
        }

        _db.Entities.Add(person);
        _people[slug] = person;
    }

    private readonly List<(Entity Person, string Case, string Form)> _forms = [];

    private void AddForms()
    {
        foreach (var (person, grammaticalCase, form) in _forms)
        {
            _db.EntityNameForms.Add(new EntityNameForm
            {
                EntityId = person.Id, Language = "ukr", GrammaticalCase = grammaticalCase, Form = form,
                Method = LinkMethod.ModelReading, Confidence = 1, Source = "a test",
            });
        }
    }

    private void Tie(string subject, string relation, string target, LinkMethod method = LinkMethod.ModelReading)
    {
        var id = _people[subject].Id;
        _ordinals[id] = _ordinals.GetValueOrDefault(id) + 1;
        _db.EntityDescriptors.Add(new EntityDescriptor
        {
            EntityId = id,
            TargetEntityId = _people[target].Id,
            Relation = relation,
            Ordinal = _ordinals[id],
            CanonicalBook = 13,
            CanonicalChapter = 2,
            CanonicalVerse = ++_verse,
            Method = method,
            Confidence = method == LinkMethod.Manual ? null : 0.9,
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
            CanonicalBook = 9,
            CanonicalChapter = 9,
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

    private void Count(string slug, int? sons = null, int? daughters = null, int? grandsons = null, string? family = null) =>
        Statement(slug, KinStatementKinds.Count, s =>
        {
            s.Sons = sons;
            s.Daughters = daughters;
            s.Grandsons = grandsons;
            s.Family = family;
        });

    private void More(string slug, bool sons, bool daughters) =>
        Statement(slug, KinStatementKinds.More, s =>
        {
            s.MoreSons = sons;
            s.MoreDaughters = daughters;
        });

    private void Place(string slug, int ordinal, string parent) =>
        Statement(slug, KinStatementKinds.Order, s =>
        {
            s.Ordinal = ordinal;
            s.ParentEntityId = _people[parent].Id;
        });

    private void Statement(string slug, string kind, Action<EntityKinStatement> fill)
    {
        var row = new EntityKinStatement
        {
            EntityId = _people[slug].Id,
            Kind = kind,
            CanonicalBook = 7,
            CanonicalChapter = 8,
            CanonicalVerse = 30,
            Method = LinkMethod.Manual,
            Source = "read from Scripture by a test",
        };
        fill(row);
        _db.EntityKinStatements.Add(row);
    }

    private void Clear()
    {
        _db.Database.ExecuteSqlRaw("DELETE FROM text");
        _db.Database.ExecuteSqlRaw("DELETE FROM entity");
    }
}
