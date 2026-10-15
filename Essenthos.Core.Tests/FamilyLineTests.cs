using Essenthos.Core.Corpus;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Endpoints;
using Essenthos.Core.Loading.Encyclopedia;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Essenthos.Core.Tests;

/// <summary>A count of children or siblings in each language the line is said in.</summary>
public sealed class DescriptorCountTests
{
    [Theory]
    [InlineData("eng", "twelve sons and a daughter")]
    [InlineData("ukr", "дванадцяти синів та дочки")]
    [InlineData("deu", "von zwölf Söhnen und einer Tochter")]
    [InlineData("spa", "doce hijos y una hija")]
    public void TwelveSonsAndADaughter(string language, string said) =>
        DescriptorPhrasings.Counted(language, [(Kin.Son, 12), (Kin.Daughter, 1)]).Should().Be(said);

    [Theory]
    [InlineData("eng", "ten brothers and a sister")]
    [InlineData("ukr", "десяти братів та сестри")]
    [InlineData("deu", "von zehn Brüdern und einer Schwester")]
    [InlineData("spa", "diez hermanos y una hermana")]
    public void TenBrothersAndASister(string language, string said) =>
        DescriptorPhrasings.Counted(language, [(Kin.Brother, 10), (Kin.Sister, 1)]).Should().Be(said);

    [Theory]
    [InlineData("eng", "seventy sons", 70)]
    [InlineData("eng", "five sons", 5)]
    [InlineData("ukr", "п’яти синів", 5)]
    [InlineData("ukr", "21 сина", 21)]
    [InlineData("ukr", "28 синів", 28)]
    [InlineData("ukr", "111 синів", 111)]
    [InlineData("deu", "von 28 Söhnen", 28)]
    [InlineData("spa", "veinte hijos", 20)]
    public void ACountAgreesWithItsNumber(string language, string said, int count)
    {
        var expected = language == "eng" && count == 70 ? "70 sons" : said;
        DescriptorPhrasings.Counted(language, [(Kin.Son, count), (Kin.Daughter, 0)]).Should().Be(expected);
    }

    [Theory]
    [InlineData("eng", "five children")]
    [InlineData("ukr", "п’яти дітей")]
    [InlineData("deu", "von fünf Kindern")]
    [InlineData("spa", "cinco hijos")]
    public void ChildrenOfUnknownSexAreCountedAsChildren(string language, string said) =>
        DescriptorPhrasings.Counted(language, [(Kin.Child, 5)]).Should().Be(said);

    [Fact]
    public void ASonFirstAndManyDaughtersAfter() =>
        DescriptorPhrasings.Counted("ukr", [(Kin.Son, 1), (Kin.Daughter, 4)]).Should().Be("сина та чотирьох дочок");
}

/// <summary>
/// A person's line read from both sides of every family tie, in a fixed order, with many children or
/// siblings counted, and a renamed person called by the name the page heads them with.
/// </summary>
[Collection(WitnessDatabaseCollection.Name)]
public sealed class FamilyLineTests : IDisposable
{
    private const string Male = "male";

    private const string Female = "female";

    private readonly AppDbContext _db;

    private readonly Dictionary<string, Entity> _people = new(StringComparer.Ordinal);

    private readonly Dictionary<int, int> _ordinals = [];

    private int _verse;

    public FamilyLineTests(WitnessDatabase database)
    {
        _db = database.NewContext();
        Clear();

        Person("isaac", "Isaac", Male, ("ukr", "Ісак", "Ісака"), ("deu", "Isaak", "Isaaks"), ("spa", "Isaac", null));
        Person("rebekah", "Rebekah", null, ("ukr", "Ревека", "Ревеки"), ("deu", "Rebekka", "Rebekkas"), ("spa", "Rebeca", null));
        Person("jacob", "Jacob", Male, ("ukr", "Яків", "Якова"));
        Person("esau", "Esau", Male, ("ukr", "Ісав", "Ісава"), ("deu", "Esau", "Esaus"), ("spa", "Esaú", null));
        Person("leah", "Leah", Female, ("ukr", "Лія", "Лії"), ("deu", "Lea", "Leas"), ("spa", "Lea", null));
        Person("rachel", "Rachel", Female, ("ukr", "Рахіль", "Рахілі"), ("deu", "Rahel", "Rahels"), ("spa", "Raquel", null));
        string[] sons = ["reuben", "simeon", "levi", "judah", "dan", "naphtali", "gad", "asher", "issachar", "zebulun", "joseph", "benjamin"];
        foreach (var son in sons)
        {
            Person(son, char.ToUpperInvariant(son[0]) + son[1..], Male);
        }

        Person("dinah", "Dinah", Female);
        Person("manasseh", "Manasseh", Male);
        Person("ephraim", "Ephraim", Male);
        Person("asenath", "Asenath", Female);
        Person("terah", "Terah", Male);
        Person("abram", "Abram", Male);
        Person("sarai", "Sarai", Female);
        Person("haran", "Haran", Male);
        Person("nahor", "Nahor", Male);
        Person("ishmael", "Ishmael", Male);
        Person("nobody", "Nobody", null);
        Person("someone", "Someone", null);
        Person("yhvh", "YHVH", null);
        Person("jesse", "Jesse", Male);
        Person("david", "David", Male);
        Person("israelites", "Israelites", null);
        Person("one", "One", Male);
        Person("two", "Two", Male);
        _db.SaveChanges();
        AddForms();

        // Jacob's own record names his parents, his brother and two of his sons; everything else
        // about him is written on the others' records.
        Tie("jacob", DescriptorRelations.SonOf, "isaac");
        Tie("jacob", DescriptorRelations.SonOf, "rebekah");
        Tie("jacob", DescriptorRelations.BrotherOf, "esau");
        Tie("jacob", DescriptorRelations.ServantOf, "yhvh");
        Tie("jacob", DescriptorRelations.FatherOf, "dan");
        Tie("jacob", DescriptorRelations.FatherOf, "naphtali");
        Tie("rebekah", DescriptorRelations.WifeOf, "isaac");
        Tie("leah", DescriptorRelations.WifeOf, "jacob");
        Tie("rachel", DescriptorRelations.WifeOf, "jacob");
        foreach (var son in sons)
        {
            Tie(son, DescriptorRelations.SonOf, "jacob");
        }

        Tie("dinah", DescriptorRelations.DaughterOf, "jacob");

        // Joseph says his half-brothers himself; Reuben's own record calls Joseph a brother, which
        // the half-brother Joseph's record states outranks on Joseph's line.
        Tie("joseph", DescriptorRelations.SonOf, "jacob");
        Tie("joseph", DescriptorRelations.SonOf, "rachel");
        Tie("joseph", DescriptorRelations.FatherOf, "manasseh");
        foreach (var half in sons.Where(s => s is not ("joseph" or "benjamin")))
        {
            Tie("joseph", DescriptorRelations.HalfBrotherOf, half);
        }

        Tie("joseph", DescriptorRelations.HalfBrotherOf, "dinah");
        Tie("reuben", DescriptorRelations.BrotherOf, "joseph");
        Tie("benjamin", DescriptorRelations.BrotherOf, "joseph");
        Tie("ephraim", DescriptorRelations.SonOf, "joseph");
        Tie("asenath", DescriptorRelations.WifeOf, "joseph");

        Tie("abram", DescriptorRelations.SonOf, "terah");
        Tie("abram", DescriptorRelations.HusbandOf, "sarai");
        Tie("abram", DescriptorRelations.BrotherOf, "haran");
        Tie("abram", DescriptorRelations.BrotherOf, "nahor");
        Tie("abram", DescriptorRelations.HalfBrotherOf, "sarai");
        Tie("abram", DescriptorRelations.AncestorOf, "isaac");
        Tie("sarai", DescriptorRelations.WifeOf, "abram");
        Tie("sarai", DescriptorRelations.HalfSisterOf, "abram");
        Tie("isaac", DescriptorRelations.SonOf, "abram");
        Tie("ishmael", DescriptorRelations.SonOf, "abram");
        Tie("haran", DescriptorRelations.BrotherOf, "abram");

        // A person nothing states the sex of: a child's clause cannot say whether he is the
        // father or the mother, so it is not turned.
        Tie("someone", DescriptorRelations.SonOf, "nobody");
        Tie("isaac", DescriptorRelations.SonOf, "yhvh");

        // Benjamin's own record calls Simeon a brother; the parents both records give say otherwise.
        Tie("benjamin", DescriptorRelations.SonOf, "rachel");
        Tie("simeon", DescriptorRelations.SonOf, "leah");
        Tie("benjamin", DescriptorRelations.BrotherOf, "simeon");

        // An office is said before the family, whatever order the record wrote it in.
        Tie("david", DescriptorRelations.SonOf, "jesse");
        Tie("david", DescriptorRelations.ServantOf, "yhvh");
        Tie("david", DescriptorRelations.KingOf, "israelites");

        // The owner's ruling, written on the other record, outranks a model's reading of this one.
        Tie("one", DescriptorRelations.BrotherOf, "two");
        Tie("two", DescriptorRelations.HalfBrotherOf, "one", LinkMethod.Manual);
        _db.SaveChanges();
    }

    [Fact]
    public async Task ABrotherWithAnotherMotherIsAHalfBrother() =>
        (await Line("benjamin", "eng")).Should().Contain("half-brother of Simeon").And.NotContain("brother of Joseph and Simeon");

    [Fact]
    public async Task AnOfficeComesFirst() =>
        (await Line("david", "eng")).Should().StartWith("king of Israelites, son of Jesse");

    [Fact]
    public async Task TheOwnersRulingOnTheOtherRecordOutranksAReading() =>
        (await Line("one", "eng")).Should().Be("half-brother of Two");

    public void Dispose()
    {
        Clear();
        _db.Dispose();
    }

    [Fact]
    public async Task JacobIsTheFatherOfEverySonAndTheDaughterOthersRecordsName()
    {
        var line = await Line("jacob", "eng");

        line.Should().Be("son of Isaac and Rebekah, husband of Leah and Rachel, father of twelve sons and a daughter, "
                         + "brother of Esau, servant of YHVH");
    }

    [Theory]
    [InlineData("ukr", "син Ісака та Ревеки, чоловік Лії та Рахілі, батько дванадцяти синів та дочки, брат Ісава")]
    [InlineData("deu", "Sohn Isaaks und Rebekkas, Mann Leas und Rahels, Vater von zwölf Söhnen und einer Tochter, Bruder Esaus")]
    [InlineData("spa", "hijo de Isaac y Rebeca, marido de Lea y Raquel, padre de doce hijos y una hija, hermano de Esaú")]
    public async Task JacobsLineInEachLanguage(string language, string expected) =>
        (await Line("jacob", language)).Should().StartWith(expected);

    [Fact]
    public async Task ACountedRunSaysItsWordsAndKeepsEveryClaimBehindIt()
    {
        var jacob = (await Descriptors.Of(_db, "jacob", "eng", default))!;

        jacob.Counted.Should().ContainSingle()
            .Which.Should().Be(new DescriptorCountResponse(DescriptorRelations.FatherOf, "father of twelve sons and a daughter"));
        jacob.Claims.Where(c => c.Relation == DescriptorRelations.FatherOf).Select(c => c.Target.Slug).Distinct()
            .Should().HaveCount(13);
        jacob.Claims.Should().Contain(c => c.Target.Slug == "reuben" && c.Stated == DescriptorRelations.SonOf);
        jacob.Claims.Should().Contain(c => c.Target.Slug == "dan" && c.Stated == null);
        jacob.Claims.Should().Contain(c => c.Target.Slug == "dan" && c.Stated == DescriptorRelations.SonOf);
    }

    [Fact]
    public async Task AbramIsTheFatherOfIsaacAndIshmaelAndAFatherOutranksTheAncestorHisRecordSays()
    {
        var line = await Line("abram", "eng");

        line.Should().Be("son of Terah, husband of Sarai, father of Isaac and Ishmael, brother of Haran and Nahor, "
                         + "half-brother of Sarai");
    }

    [Fact]
    public async Task JosephNamesBenjaminAndCountsTheHalfBrothersHisOwnRecordStates()
    {
        var line = await Line("joseph", "eng");

        line.Should().Be("son of Jacob and Rachel, husband of Asenath, father of Manasseh and Ephraim, "
                         + "half-brother of ten brothers and a sister, brother of Benjamin");
    }

    [Fact]
    public async Task ASexOurOwnRowsStateTurnsAClauseAndAnUnknownOneDoesNot()
    {
        (await Line("rebekah", "eng")).Should().Be("wife of Isaac, mother of Jacob");
        (await Descriptors.Of(_db, "nobody", "eng", default)).Should().BeNull();
    }

    [Fact]
    public async Task GodIsNobodysFatherOnAFamilyLine() =>
        (await Descriptors.Of(_db, "yhvh", "eng", default)).Should().BeNull();

    private async Task<string> Line(string slug, string language) =>
        string.Concat((await Descriptors.Of(_db, slug, language, default))!.Parts.Select(p => p.Text));

    private void Person(string slug, string name, string? sex, params (string Language, string Nominative, string? Genitive)[] forms)
    {
        var person = new Entity
        {
            Kind = EntityKind.Person, Slug = slug, Name = name, Sex = sex, SourceId = slug, Source = "a test",
        };
        foreach (var (language, nominative, genitive) in forms)
        {
            _forms.Add((person, language, GrammaticalCases.Nominative, nominative));
            if (genitive is not null)
            {
                _forms.Add((person, language, GrammaticalCases.Genitive, genitive));
            }
        }

        _db.Entities.Add(person);
        _people[slug] = person;
    }

    private readonly List<(Entity Person, string Language, string Case, string Form)> _forms = [];

    private void AddForms()
    {
        foreach (var (person, language, grammaticalCase, form) in _forms)
        {
            _db.EntityNameForms.Add(new EntityNameForm
            {
                EntityId = person.Id, Language = language, GrammaticalCase = grammaticalCase, Form = form,
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
            CanonicalBook = 1,
            CanonicalChapter = 30,
            CanonicalVerse = ++_verse,
            Method = method,
            Confidence = 0.9,
            Source = "a test",
        });
    }

    private void Clear()
    {
        _db.Database.ExecuteSqlRaw("DELETE FROM text");
        _db.Database.ExecuteSqlRaw("DELETE FROM entity");
    }
}

/// <summary>A renamed person is called in another person's line by the name their page heads them with.</summary>
[Collection(WitnessDatabaseCollection.Name)]
public sealed class RenamedInALineTests : IDisposable
{
    private readonly AppDbContext _db;

    public RenamedInALineTests(WitnessDatabase database)
    {
        _db = database.NewContext();
        Clear();

        var ohienko = Corpus.Add(_db, "UBIO", TextKind.Translation, "ukr", (1, 1, ["слово"]));
        var paul = Person("saul-2", "Saul", ("Saul", BibleDataLoader.LabelKind), ("Paul", BibleDataLoader.LabelKind));
        var barnabas = Person("joseph-12", "Joseph", ("Joseph", BibleDataLoader.LabelKind), ("Barnabas", BibleDataLoader.LabelKind));
        var silas = Person("silas", "Silas");
        var mark = Person("mark", "Mark");
        _db.SaveChanges();

        Forms(paul, "Савл", "Савла");
        Forms(barnabas, "Йосип", "Йосипа");
        Forms(silas, "Сила", "Сили");
        Forms(mark, "Марко", "Марка");
        Printed(ohienko, paul, "Павло", 120);
        Printed(ohienko, paul, "Павла", 40);
        Printed(ohienko, paul, "Павлові", 50);
        Printed(ohienko, paul, "Савл", 15);
        Printed(ohienko, barnabas, "Варнава", 24);
        Printed(ohienko, barnabas, "Варнаві", 9);
        Named(paul, 6, "Paul", from: 100);
        Named(paul, 2, "Saul", from: 200);
        Named(barnabas, 3, "Barnabas", from: 100);
        Clause(silas, DescriptorRelations.CompanionOf, paul, 1);
        Clause(mark, DescriptorRelations.CompanionOf, barnabas, 1);
        _db.SaveChanges();
    }

    public void Dispose()
    {
        Clear();
        _db.Dispose();
    }

    [Theory]
    [InlineData("eng", "companion of Paul")]
    [InlineData("ukr", "товариш Павла")]
    public async Task ACompanionOfPaulIsSaidSo(string language, string expected)
    {
        var silas = (await Descriptors.Of(_db, "silas", language, default))!;

        string.Concat(silas.Parts.Select(p => p.Text)).Should().Be(expected);
        silas.Claims.Single().Target.EnglishName.Should().Be("Paul");
    }

    [Fact]
    public async Task WithoutAPrintedGenitiveTheLineKeepsTheFiledNameRatherThanSayAnEnglishOne()
    {
        var mark = (await Descriptors.Of(_db, "mark", "ukr", default))!;

        string.Concat(mark.Parts.Select(p => p.Text)).Should().Be("товариш Йосипа");
        string.Concat((await Descriptors.Of(_db, "mark", "eng", default))!.Parts.Select(p => p.Text))
            .Should().Be("companion of Barnabas");
    }

    private Entity Person(string slug, string name, params (string Label, string Kind)[] labels)
    {
        var person = new Entity { Kind = EntityKind.Person, Slug = slug, Name = name, SourceId = slug, Source = "a test" };
        foreach (var (label, kind) in labels)
        {
            person.Names.Add(new EntityName { Label = label, Kind = kind });
        }

        _db.Entities.Add(person);
        return person;
    }

    private void Forms(Entity entity, string nominative, string genitive)
    {
        foreach (var (grammaticalCase, form) in new[] { (GrammaticalCases.Nominative, nominative), (GrammaticalCases.Genitive, genitive) })
        {
            _db.EntityNameForms.Add(new EntityNameForm
            {
                EntityId = entity.Id, Language = "ukr", GrammaticalCase = grammaticalCase, Form = form,
                Method = LinkMethod.ModelReading, Confidence = 1, Source = "a test",
            });
        }
    }

    private void Printed(Text text, Entity entity, string form, int occurrences) =>
        _db.EntityRenderings.Add(new EntityRendering
        {
            EntityId = entity.Id, TextId = text.Id, Form = form, Folded = NameFolding.Fold(form),
            Occurrences = occurrences,
        });

    private void Named(Entity entity, int verses, string label, int from)
    {
        for (var verse = from; verse < from + verses; verse++)
        {
            _db.EntityVerses.Add(new EntityVerse
            {
                EntityId = entity.Id, CanonicalBook = 44, CanonicalChapter = 13, CanonicalVerse = verse,
                Label = label, Source = BibleDataLoader.Source,
            });
        }
    }

    private void Clause(Entity subject, string relation, Entity target, int ordinal) =>
        _db.EntityDescriptors.Add(new EntityDescriptor
        {
            EntityId = subject.Id, TargetEntityId = target.Id, Relation = relation, Ordinal = ordinal,
            CanonicalBook = 44, CanonicalChapter = 15, CanonicalVerse = 40,
            Method = LinkMethod.ModelReading, Confidence = 0.9, Source = "a test",
        });

    private void Clear()
    {
        _db.Database.ExecuteSqlRaw("DELETE FROM text");
        _db.Database.ExecuteSqlRaw("DELETE FROM entity");
    }
}
