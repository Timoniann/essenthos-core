using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Endpoints;
using Essenthos.Core.Loading.Encyclopedia;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Essenthos.Core.Tests;

/// <summary>
/// A teacher's line names the companies he taught and some members of each, and says there were
/// disciples beyond them; a member's line says which company he was one of; and four brothers are
/// named rather than counted.
/// </summary>
[Collection(WitnessDatabaseCollection.Name)]
public sealed class CompanyLineTests : IDisposable
{
    private const string Male = "male";

    private readonly AppDbContext _db;

    private readonly Dictionary<string, Entity> _people = new(StringComparer.Ordinal);

    private readonly Dictionary<int, int> _ordinals = [];

    private int _verse;

    public CompanyLineTests(WitnessDatabase database)
    {
        _db = database.NewContext();
        Clear();

        Person("jesus", "Jesus", ("ukr", "Ісус", "Ісуса"), ("deu", "Jesus", "Jesu"), ("spa", "Jesús", null));
        Person("simon", "Simon", ("ukr", "Симон", "Симона"), ("deu", "Simon", null), ("spa", "Simón", null));
        Person("andrew", "Andrew", ("ukr", "Андрій", "Андрія"), ("deu", "Andreas", null), ("spa", "Andrés", null));
        Person("james", "James", ("ukr", "Яків", "Якова"), ("deu", "Jakobus", null), ("spa", "Jacobo", null));
        Person("john-2", "John", ("ukr", "Іван", "Івана"), ("deu", "Johannes", null), ("spa", "Juan", null));
        Person("philip", "Philip", ("ukr", "Пилип", "Пилипа"), ("deu", "Philippus", null), ("spa", "Felipe", null));
        Person("judas", "Judas", ("ukr", "Юда", "Юди"), ("deu", "Judas", null), ("spa", "Judas", null));
        Person("nathanael", "Nathanael");
        Person("mary-3", "Mary", sex: "female");
        Person("joseph-8", "Joseph");
        Person("james-3", "James", ("ukr", "Яків", "Якова"));
        Person("joseph-7", "Joses", ("ukr", "Йосія", "Йосії"));
        Person("simon-3", "Simon", ("ukr", "Симон", "Симона"));
        Person("judas-2", "Judas", ("ukr", "Юда", "Юди"));
        Person("reuben", "Reuben");
        foreach (var brother in new[] { "one", "two", "three", "four", "five" })
        {
            Person(brother, char.ToUpperInvariant(brother[0]) + brother[1..]);
        }

        Company("the-twelve", "The Twelve",
            ("eng", "the Twelve", null), ("ukr", "Дванадцятеро", "Дванадцятьох"), ("deu", "die Zwölf", "der Zwölf"),
            ("spa", "los Doce", null));
        Company("the-seventy", "The Seventy",
            ("eng", "the Seventy", null), ("ukr", "Сімдесятеро", "Сімдесятьох"), ("deu", "die Siebzig", "der Siebzig"),
            ("spa", "los Setenta", null));
        _db.SaveChanges();
        AddForms();

        Tie("jesus", DescriptorRelations.TeacherOf, "the-twelve");
        Tie("jesus", DescriptorRelations.TeacherOf, "the-seventy");
        foreach (var (member, verse) in new[] { ("simon", 2), ("andrew", 2), ("james", 2), ("john-2", 2), ("philip", 3), ("judas", 4) })
        {
            Tie(member, DescriptorRelations.MemberOf, "the-twelve");
            Related(member, DescriptorRelations.MemberOf, "the-twelve", verse);
        }

        foreach (var disciple in new[] { "philip", "nathanael", "mary-3", "joseph-8" })
        {
            Related(disciple, DescriptorRelations.DiscipleOf, "jesus", 9);
        }

        Named("simon", 20);
        Named("john-2", 15);
        Named("james", 10);
        Named("andrew", 5);
        Named("philip", 30);
        Named("judas", 25);

        foreach (var brother in new[] { "james-3", "joseph-7", "simon-3", "judas-2" })
        {
            Tie(brother, DescriptorRelations.BrotherOf, "jesus");
        }

        foreach (var brother in new[] { "one", "two", "three", "four", "five" })
        {
            Tie(brother, DescriptorRelations.BrotherOf, "reuben");
        }

        _db.SaveChanges();
    }

    public void Dispose()
    {
        Clear();
        _db.Dispose();
    }

    /// <summary>
    /// The companies are named with the members their verses list first, the most named first among
    /// the members of one verse, and the disciples the records give beyond them are said to be many.
    /// </summary>
    [Theory]
    [InlineData("eng", "teacher of the Twelve (Simon, John, James, Andrew and others), the Seventy and many other disciples")]
    [InlineData("ukr", "учитель Дванадцятьох (Симон, Іван, Яків, Андрій та інші), Сімдесятьох та багатьох інших учнів")]
    [InlineData("deu", "Lehrer der Zwölf (Simon, Johannes, Jakobus, Andreas und andere), der Siebzig und vieler anderer Jünger")]
    [InlineData("spa", "maestro de los Doce (Simón, Juan, Jacobo, Andrés y otros), los Setenta y muchos otros discípulos")]
    public async Task ATeacherIsTheTeacherOfTheCompaniesWithSomeOfTheirMembersAndOfManyMore(string language, string expected) =>
        (await Line("jesus", language)).Should().EndWith(expected);

    [Fact]
    public async Task TheCompaniesAreHandedToAClientAsTheWordsOfTheTeachersRun()
    {
        var jesus = (await Descriptors.Of(_db, "jesus", "eng", default))!;

        var taught = jesus.Counted.Should().ContainSingle(c => c.Relation == DescriptorRelations.TeacherOf).Which;
        taught.Parts!.Where(p => p.Entity is not null).Select(p => p.Entity!.Slug).Should()
            .Equal("the-twelve", "simon", "john-2", "james", "andrew", "the-seventy");
        taught.References!.Select(v => v.Verse).Should().HaveCount(2);
        jesus.Claims.Where(c => c.Relation == DescriptorRelations.TeacherOf).Select(c => c.Target.Slug).Should()
            .Equal("the-twelve", "the-seventy");
    }

    [Theory]
    [InlineData("eng", "one of the Twelve")]
    [InlineData("ukr", "один із Дванадцятьох")]
    [InlineData("deu", "einer der Zwölf")]
    [InlineData("spa", "uno de los Doce")]
    public async Task AMemberIsOneOfTheCompany(string language, string expected) =>
        (await Line("simon", language)).Should().Be(expected);

    [Theory]
    [InlineData("eng", "brother of James, Joses, Simon and Judas, ")]
    [InlineData("ukr", "брат Якова, Йосії, Симона та Юди, ")]
    public async Task FourBrothersAreNamed(string language, string expected) =>
        (await Line("jesus", language)).Should().StartWith(expected);

    [Fact]
    public async Task FiveBrothersAreCounted() =>
        (await Line("reuben", "eng")).Should().Be("brother of five brothers");

    /// <summary>
    /// Jesus is the teacher of the Twelve and the Seventy and no longer of four men; the high priest of
    /// Hebrews is one of his titles, not an office of his life, and is not in his line; and each of the
    /// Twelve, Matthias with them, is one of them.
    /// </summary>
    [Fact]
    public void TheShippedClausesSayJesusTaughtTheCompaniesAndHeldNoPriesthood()
    {
        var records = DescriptorFiles.Read(TestResources.Shipped(Path.Combine("Essenthos", "descriptors"))).Records
            .ToDictionary(r => r.Entity, StringComparer.Ordinal);
        var jesus = records["jesus"].Claims!;

        jesus.Should().NotContain(c => c.Relation == DescriptorRelations.HighPriestOf || c.Relation == DescriptorRelations.PriestOf);
        jesus.Where(c => c.Relation == DescriptorRelations.TeacherOf).Select(c => (c.Target, c.Reference)).Should()
            .Equal(("the-twelve", "MAT 10:1"), ("the-seventy", "LUK 10:1"));

        string[] twelve =
        [
            "simon", "andrew", "james", "john-2", "philip", "bartholomew", "thomas", "matthew", "james-2", "thaddaeus",
            "simon-2", "judas", "judas-of-james", "matthias",
        ];
        foreach (var member in twelve)
        {
            records[member].Claims.Should().ContainSingle(c => c.Relation == DescriptorRelations.MemberOf, member)
                .Which.Target.Should().Be("the-twelve");
        }

        records.Values.SelectMany(r => (r.Claims ?? []).Where(c => c.Relation == DescriptorRelations.MemberOf).Select(_ => r.Entity))
            .Should().BeEquivalentTo(twelve);
    }

    /// <summary>
    /// The companies are records of their own, named in every language their pages are read in, and
    /// their number names them only where it stands for them.
    /// </summary>
    [Theory]
    [InlineData("the-twelve", "G1427", 40, 10, 1, true)]
    [InlineData("the-twelve", "G1427", 42, 22, 3, true)]
    [InlineData("the-twelve", "G1427", 46, 15, 5, true)]
    [InlineData("the-twelve", "G1427", 40, 19, 28, false)]
    [InlineData("the-twelve", "G1427", 42, 22, 30, false)]
    [InlineData("the-twelve", "G1427", 41, 5, 42, false)]
    [InlineData("the-twelve", "G1427", 66, 21, 14, false)]
    [InlineData("the-seventy", "G1440", 42, 10, 1, true)]
    [InlineData("the-seventy", "G1440", 42, 10, 17, true)]
    public void TheCompaniesAreNamedWhereTheirNumberStandsForThem(string slug, string strong, int book, int chapter, int verse, bool named)
    {
        var record = ThingFiles.Read(ThingSet.Narratives).Records.Single(r => r.Slug == slug);

        record.EntityKind.Should().Be(EntityKind.People);
        record.Names.Should().ContainKeys(["eng", "ukr", "deu", "rus", "spa"]);
        record.Forms!["ukr"].Should().ContainKey(GrammaticalCases.Genitive);
        record.Occurrences!.Any(rule => rule.Strong == strong && rule.Admits(book, chapter, verse, 1)).Should().Be(named);
    }

    /// <summary>The Lord who sends the seventy and whom they answer is Jesus, as Luke's narrative has him.</summary>
    [Fact]
    public void TheLordWhoSentTheSeventyIsJesus()
    {
        var readings = PassageReadingFiles.Read(TestResources.Shipped(PassageReadingFiles.DefaultFolder))
            .Where(r => r.Reference is "LUK 10:1" or "LUK 10:17")
            .ToList();

        readings.Should().HaveCount(2).And.OnlyContain(r => r.Record == "jesus" && r.Text == "NESTLE1904"
                                                            && r.Strong == "G2962" && r.Write && r.DecidedBy != null);
        readings.Select(r => (r.Position, r.Surface)).Should().Equal((6, "Κύριος"), (8, "Κύριε"));
    }

    private async Task<string> Line(string slug, string language) =>
        string.Concat((await Descriptors.Of(_db, slug, language, default))!.Parts.Select(p => p.Text));

    private readonly List<(Entity Entity, string Language, string Case, string Form)> _forms = [];

    private void Person(string slug, string name, params (string Language, string Nominative, string? Genitive)[] forms) =>
        Person(slug, name, Male, forms);

    private void Person(string slug, string name, string sex, params (string Language, string Nominative, string? Genitive)[] forms) =>
        Add(new Entity { Kind = EntityKind.Person, Slug = slug, Name = name, Sex = sex, SourceId = slug, Source = "a test" }, forms);

    private void Company(string slug, string name, params (string Language, string Nominative, string? Genitive)[] forms) =>
        Add(new Entity { Kind = EntityKind.People, Slug = slug, Name = name, SourceId = slug, Source = "a test" }, forms);

    private void Add(Entity entity, (string Language, string Nominative, string? Genitive)[] forms)
    {
        foreach (var (language, nominative, genitive) in forms)
        {
            _forms.Add((entity, language, GrammaticalCases.Nominative, nominative));
            if (genitive is not null)
            {
                _forms.Add((entity, language, GrammaticalCases.Genitive, genitive));
            }
        }

        _db.Entities.Add(entity);
        _people[entity.Slug] = entity;
    }

    private void AddForms()
    {
        foreach (var (entity, language, grammaticalCase, form) in _forms)
        {
            _db.EntityNameForms.Add(new EntityNameForm
            {
                EntityId = entity.Id, Language = language, GrammaticalCase = grammaticalCase, Form = form,
                Method = LinkMethod.ModelReading, Confidence = 1, Source = "a test",
            });
        }
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
            CanonicalBook = 40,
            CanonicalChapter = 10,
            CanonicalVerse = ++_verse,
            Method = LinkMethod.Manual,
            Source = "a test",
        });
    }

    private void Related(string from, string type, string to, int verse) =>
        _db.EntityRelationships.Add(new EntityRelationship
        {
            FromEntityId = _people[from].Id,
            ToEntityId = _people[to].Id,
            Type = type,
            Category = RelationshipCategories.Read,
            CanonicalBook = 40,
            CanonicalChapter = 10,
            CanonicalVerse = verse,
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
