using Essenthos.Core.Corpus;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Loading.Encyclopedia;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Essenthos.Core.Tests.Records;

/// <summary>
/// A person's sex is written on the record: the dataset's where it gave one, otherwise what the person's
/// relationships say, otherwise the grammar of the name the Greek prints. Nothing else is given one.
/// </summary>
[Collection(WitnessDatabaseCollection.Name)]
public sealed class PersonSexLoaderTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly IDbContextTransaction _transaction;
    private readonly Text _greek;

    public PersonSexLoaderTests(WitnessDatabase database)
    {
        _db = database.NewContext();
        _transaction = _db.Database.BeginTransaction();
        _greek = Corpus.Add(_db, "NESTLE1904", TextKind.CriticalEdition, "grc",
            (25, 1, ["Βερνίκη"]), (25, 2, ["Λεγιών"]), (25, 3, ["Ἀθῆναι"]), (25, 4, ["Ἰουνίαν"]), (25, 5, ["Ἰουνιᾶν"]),
            (25, 6, ["Μαρία"]), (25, 7, ["Μαριὰμ"]));
        _db.SaveChanges();
    }

    public void Dispose()
    {
        _transaction.Rollback();
        _transaction.Dispose();
        _db.Dispose();
    }

    private PersonSexLoader Loader() => new(_db, NullLogger<PersonSexLoader>.Instance);

    private async Task<Entity> Reloaded(Entity entity)
    {
        await _db.Entry(entity).ReloadAsync();
        return entity;
    }

    [Fact]
    public async Task ARelationshipOfThePersonGivesTheSexAndSaysSo()
    {
        var (mother, son) = (Record("mother"), Record("son"));
        await _db.SaveChangesAsync();
        Clause(mother, "mother-of", son);
        Tie(son, "son-of", mother);
        await _db.SaveChangesAsync();

        await Loader().Load(default);

        (await Reloaded(mother)).Should().Match<Entity>(e => e.Sex == "female" && e.SexSource == SexSources.Relationship);
        (await Reloaded(son)).Should().Match<Entity>(e => e.Sex == "male" && e.SexSource == SexSources.Relationship);
    }

    [Fact]
    public async Task RelationshipsThatDisagreeGiveNoneAndSayingNothingGivesNone()
    {
        var (odd, target, silent) = (Record("odd"), Record("target"), Record("silent"));
        await _db.SaveChangesAsync();
        Clause(odd, "son-of", target);
        Clause(odd, "mother-of", target);
        await _db.SaveChangesAsync();

        await Loader().Load(default);

        (await Reloaded(odd)).Sex.Should().BeNull();
        (await Reloaded(silent)).Should().Match<Entity>(e => e.Sex == null && e.SexSource == null && e.NameGender == null);
    }

    /// <summary>Bernice is Βερνίκη, feminine; Legion is a common noun and says nothing of a bearer.</summary>
    [Fact]
    public async Task WhereNoRelationshipSaysTheGreeksGrammarOfTheNameGives()
    {
        var (bernice, legion) = (Record("bernice"), Record("legion"));
        await _db.SaveChangesAsync();
        Named(1, bernice, "G959", "Βερνίκη", "feminine");
        Named(2, legion, "G3003", "λεγεών", "feminine");
        await _db.SaveChangesAsync();

        await Loader().Load(default);

        (await Reloaded(bernice)).Should().Match<Entity>(
            e => e.Sex == "female" && e.SexSource == SexSources.Grammar && e.NameGender == "female");
        (await Reloaded(legion)).Should().Match<Entity>(e => e.Sex == null && e.NameGender == null);
    }

    [Fact]
    public async Task ARelationshipOutranksTheGrammarOfTheName()
    {
        var (man, target) = (Record("man"), Record("target"));
        await _db.SaveChangesAsync();
        Clause(man, "father-of", target);
        Named(1, man, "G959", "Βερνίκη", "feminine");
        await _db.SaveChangesAsync();

        await Loader().Load(default);

        (await Reloaded(man)).Should().Match<Entity>(
            e => e.Sex == "male" && e.SexSource == SexSources.Relationship && e.NameGender == "female");
    }

    [Fact]
    public async Task WordsOfTwoGendersSayNothingAndACarriedWordIsNotAsked()
    {
        var (both, carried) = (Record("both"), Record("carried"));
        await _db.SaveChangesAsync();
        Named(6, both, "G3137", "Μαρία", "feminine");
        Named(7, both, "G3138", "Μαριάμ", "masculine");
        Named(4, carried, "G2458", "Ἰουνία", "feminine", "through KJV");
        await _db.SaveChangesAsync();

        await Loader().Load(default);

        (await Reloaded(both)).Should().Match<Entity>(e => e.Sex == null && e.NameGender == null);
        (await Reloaded(carried)).Should().Match<Entity>(e => e.Sex == null && e.NameGender == null);
    }

    /// <summary>Junias of Romans 16:7: the dataset's male stands, and the feminine of Nestle's word is kept beside it.</summary>
    [Fact]
    public async Task TheDatasetsValueStandsAndTheNamesGenderIsKeptBesideIt()
    {
        var junias = Record("junias");
        junias.Sex = "male";
        await _db.SaveChangesAsync();
        Named(4, junias, "G2458", "Ἰουνία", "feminine");
        await _db.SaveChangesAsync();

        await Loader().Load(default);

        (await Reloaded(junias)).Should().Match<Entity>(
            e => e.Sex == "male" && e.SexSource == null && e.NameGender == "female");
    }

    /// <summary>Athens is feminine in Greek and is no woman; a people or a title is given none either.</summary>
    [Fact]
    public async Task OnlyAPersonIsGivenOne()
    {
        var (athens, people, title, target) = (
            Record("athens", EntityKind.Place), Record("people", EntityKind.People), Record("title", EntityKind.Title), Record("target"));
        await _db.SaveChangesAsync();
        Named(3, athens, "G116", "Ἀθῆναι", "feminine");
        Named(1, people, "G959", "Βερνίκη", "feminine");
        Clause(title, "queen-of", target);
        await _db.SaveChangesAsync();

        await Loader().Load(default);

        (await Reloaded(athens)).Should().Match<Entity>(e => e.Sex == null && e.NameGender == null);
        (await Reloaded(people)).Should().Match<Entity>(e => e.Sex == null && e.NameGender == null);
        (await Reloaded(title)).Should().Match<Entity>(e => e.Sex == null && e.NameGender == null);
    }

    /// <summary>The owner's word on Junia of Romans 16:7 outranks the dataset's male, on the column and on her page.</summary>
    [Fact]
    public async Task APersonsRulingOutranksEverythingAndAWordingOnAnotherRecordIsNotApplied()
    {
        var (junias, stranger) = (Record("junias"), Record("stranger"));
        junias.Sex = "male";
        junias.Source = BibleDataLoader.Source;
        junias.SourceId = "person:Junias_1";
        await _db.SaveChangesAsync();
        _db.EntityVerses.Add(new EntityVerse
        {
            Entity = junias, CanonicalBook = 45, CanonicalChapter = 16, CanonicalVerse = 7,
            Source = "Essenthos, from the words this corpus annotates to the person or the place they name",
        });
        await _db.SaveChangesAsync();

        var rulings = new[]
        {
            new RuledSex("junias", "person:Junias_1", "female", "a test"),
            new RuledSex("stranger", "person:Someone_Else", "female", "a test"),
        };
        await Loader().Load(rulings, default);

        (await Reloaded(junias)).Should().Match<Entity>(e => e.Sex == "female" && e.SexSource == SexSources.Ruling);
        (await Reloaded(stranger)).Sex.Should().BeNull();
        (await OursOnlyRecords.Among(_db, ["junias"], default))["junias"].Sex.Should().Be("female");
        (await Loader().Load(rulings, default)).Written.Should().Be(0);
    }

    [Fact]
    public void JuniaIsAWomanByTheOwnersWord() =>
        OwnNameLoader.Read().Sexes.Should().ContainSingle(r => r.Entity == "junias" && r.Sex == "female");

    [Fact]
    public async Task ASecondRunWritesNothingAndAClauseTakenBackTakesItsSexWithIt()
    {
        var (mother, son, dataset) = (Record("mother"), Record("son"), Record("dataset"));
        dataset.Sex = "male";
        await _db.SaveChangesAsync();
        var clause = Clause(mother, "mother-of", son);
        Clause(dataset, "mother-of", son);
        await _db.SaveChangesAsync();

        (await Loader().Load(default)).Written.Should().Be(1);
        (await Loader().Load(default)).Written.Should().Be(0);

        _db.EntityDescriptors.Remove(clause);
        await _db.SaveChangesAsync();
        (await Loader().Load(default)).Written.Should().Be(1);

        (await Reloaded(mother)).Should().Match<Entity>(e => e.Sex == null && e.SexSource == null);
        (await Reloaded(dataset)).Sex.Should().Be("male");
    }

    private Entity Record(string slug, EntityKind kind = EntityKind.Person)
    {
        var entity = new Entity
        {
            Kind = kind,
            Slug = slug,
            Name = char.ToUpperInvariant(slug[0]) + slug[1..],
            SourceId = $"test:{slug}",
            Source = "test",
        };
        _db.Entities.Add(entity);
        return entity;
    }

    private EntityDescriptor Clause(Entity entity, string relation, Entity target)
    {
        var clause = new EntityDescriptor
        {
            Entity = entity,
            Ordinal = _db.EntityDescriptors.Local.Count(d => d.Entity == entity) + 1,
            Relation = relation,
            Target = target,
            CanonicalBook = 1,
            CanonicalChapter = 1,
            CanonicalVerse = 1,
            Method = LinkMethod.ModelReading,
            Confidence = 0.9,
            Source = "read from Scripture by a test",
        };
        _db.EntityDescriptors.Add(clause);
        return clause;
    }

    private void Tie(Entity from, string type, Entity to) =>
        _db.EntityRelationships.Add(new EntityRelationship
        {
            From = from, To = to, Type = type, Category = RelationshipCategories.Read,
            CanonicalBook = 1, CanonicalChapter = 1, CanonicalVerse = 1, Method = LinkMethod.Manual, Source = "a test",
        });

    private void Named(int verse, Entity entity, string number, string lemma, string gender, string? note = null)
    {
        var word = _db.WordAt(_greek, 25, verse, 1);
        word.StrongNumber = number;
        word.Morphology = System.Text.Json.JsonDocument.Parse($$"""{"pos": "noun", "gender": "{{gender}}"}""");
        if (!_db.StrongEntries.Local.Any(s => s.StrongNumber == number))
        {
            _db.StrongEntries.Add(new StrongEntry { StrongNumber = number, Lemma = lemma });
        }

        _db.WordEntities.Add(new WordEntity
        {
            Word = word, Entity = entity, Method = LinkMethod.Manual, Source = "a test", Note = note,
        });
    }
}
