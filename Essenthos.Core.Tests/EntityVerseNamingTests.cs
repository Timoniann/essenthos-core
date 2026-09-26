using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Endpoints;
using System.Text.Json;
using Essenthos.Core.Loading;
using Essenthos.Core.Loading.Encyclopedia;
using Npgsql;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Xunit;

namespace Essenthos.Core.Tests;

/// <summary>A listed verse names its entity where a word of it is annotated to it, and only concerns it otherwise.</summary>
[Collection(WitnessDatabaseCollection.Name)]
public sealed class EntityVerseNamingTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly IDbContextTransaction _transaction;

    public EntityVerseNamingTests(WitnessDatabase database)
    {
        _db = database.NewContext();
        _transaction = _db.Database.BeginTransaction();
    }

    public void Dispose()
    {
        _transaction.Rollback();
        _transaction.Dispose();
        _db.Dispose();
    }

    [Fact]
    public async Task ZimriIsNamedWhereHisNameIsAndConcernedWhereItIsNot()
    {
        var english = Corpus.Add(_db, "test-english", TextKind.Translation, "eng",
            (9, 14, ["So", "Jehu", "conspired"]),
            (9, 31, ["Had", "Zimri", "peace"]));
        await _db.SaveChangesAsync();
        var zimri = new Entity { Kind = EntityKind.Person, Slug = "zimri", Name = "Zimri", SourceId = "test:zimri", Source = "test" };
        _db.Entities.Add(zimri);
        await _db.SaveChangesAsync();
        _db.WordEntities.Add(new WordEntity
        {
            WordId = _db.WordAt(english, 9, 31, 2).Id, EntityId = zimri.Id, Method = LinkMethod.StatedBySource, Source = "test",
        });
        _db.EntityVerses.AddRange(
            new EntityVerse { EntityId = zimri.Id, CanonicalBook = 1, CanonicalChapter = 9, CanonicalVerse = 14, Source = "test" },
            new EntityVerse { EntityId = zimri.Id, CanonicalBook = 1, CanonicalChapter = 9, CanonicalVerse = 31, Source = "test", Names = false });
        await _db.SaveChangesAsync();

        var changed = await _db.Database.ExecuteSqlRawAsync(DatasetLoader.NamingVerses);
        _db.ChangeTracker.Clear();

        changed.Should().Be(1);
        (await _db.EntityVerses.Where(v => v.EntityId == zimri.Id).OrderBy(v => v.CanonicalVerse)
                .Select(v => v.Names).ToListAsync())
            .Should().Equal(false, true);
        (await _db.Database.ExecuteSqlRawAsync(DatasetLoader.NamingVerses)).Should().Be(0, "a second run changes nothing");

        var order = await EncyclopediaEndpoints.NamingFirst(_db.EntityVerses.Where(v => v.EntityId == zimri.Id)).ToListAsync();
        order.Should().HaveCount(2);
        order[0].Should().BeGreaterThan(order[1], "the verse naming him comes before the earlier verse that only concerns him");
    }

    /// <summary>A word for God is named where a word of the verse carries its number, though no word is annotated to it.</summary>
    [Fact]
    public async Task AWordForGodIsNamedWhereItsNumberStands()
    {
        var hebrew = Corpus.Add(_db, "test-hebrew", TextKind.CriticalEdition, "hbo",
            (1, 1, ["בָּרָא", "אֱלֹהִים"]),
            (1, 2, ["וְהָאָרֶץ", "הָיְתָה"]));
        await _db.SaveChangesAsync();
        _db.WordAt(hebrew, 1, 1, 2).StrongNumber = "H430";
        var elohim = new Entity { Kind = EntityKind.Term, Slug = "elohim", Name = "Elohim", SourceId = "test:elohim", Source = "test" };
        elohim.Names.Add(new EntityName { Label = "Elohim", HebrewStrongNumber = "H430" });
        _db.Entities.Add(elohim);
        await _db.SaveChangesAsync();
        _db.EntityVerses.AddRange(
            new EntityVerse { EntityId = elohim.Id, CanonicalBook = 1, CanonicalChapter = 1, CanonicalVerse = 1, Source = "test" },
            new EntityVerse { EntityId = elohim.Id, CanonicalBook = 1, CanonicalChapter = 1, CanonicalVerse = 2, Source = "test" });
        await _db.SaveChangesAsync();

        await _db.Database.ExecuteSqlRawAsync(DatasetLoader.NamingVerses);
        _db.ChangeTracker.Clear();

        (await _db.EntityVerses.Where(v => v.EntityId == elohim.Id).OrderBy(v => v.CanonicalVerse)
                .Select(v => v.Names).ToListAsync())
            .Should().Equal(true, false);
    }

    /// <summary>
    /// <em>He is the father of Moab</em> lists the verse on the Moabites; <em>his father's</em>
    /// sheep at a place of the same name does not, because the father there is not in construct.
    /// </summary>
    [Fact]
    public async Task APeoplesListCitesTheVerseThatStatesItsDescent()
    {
        var bhsa = Corpus.Add(_db, EntityCandidates.Witness, TextKind.CriticalEdition, "hbo",
            (19, 37, ["שמו", "מואב", "הוא", "אבי", "מואב"]),
            (19, 38, ["הוא", "אבי", "בני", "עמון"]),
            (20, 1, ["צאן", "אביו", "מואב"]));
        await _db.SaveChangesAsync();
        Word(bhsa, 19, 37, 2, "H4124", "a");
        Word(bhsa, 19, 37, 4, "H1", "c");
        Word(bhsa, 19, 37, 5, "H4124", "a");
        Word(bhsa, 19, 38, 2, "H1", "c");
        Word(bhsa, 19, 38, 3, "H1121", "c");
        Word(bhsa, 19, 38, 4, "H5983", "a");
        Word(bhsa, 20, 1, 2, "H1", "a");
        Word(bhsa, 20, 1, 3, "H4124", "a");
        var moab = Record("moab", EntityKind.Person, null);
        var benammi = Record("benammi", EntityKind.Person, null);
        await _db.SaveChangesAsync();
        var moabites = Record("moabites", EntityKind.People, moab.Id);
        var ammonites = Record("ammonites", EntityKind.People, benammi.Id);
        await _db.SaveChangesAsync();
        _db.EntityNames.AddRange(
            new EntityName { EntityId = moab.Id, Label = "Moab", HebrewStrongNumber = "H4124" },
            new EntityName { EntityId = ammonites.Id, Label = "Ammon", HebrewStrongNumber = "H5983" });
        await _db.SaveChangesAsync();

        var connection = (NpgsqlConnection)_db.Database.GetDbConnection();
        await using var command = new NpgsqlCommand(PeopleLoader.StatedDescent, connection);
        command.Parameters.AddWithValue("source", PeopleLoader.FromTheStatedDescent);
        command.Parameters.AddWithValue("witness", EntityCandidates.Witness);
        (await command.ExecuteNonQueryAsync()).Should().Be(2);
        (await command.ExecuteNonQueryAsync()).Should().Be(0, "a second run adds nothing");

        (await _db.EntityVerses.Where(v => v.EntityId == moabites.Id).Select(v => v.CanonicalChapter).ToListAsync())
            .Should().Equal(19);
        (await _db.EntityVerses.Where(v => v.EntityId == ammonites.Id).Select(v => v.CanonicalVerse).ToListAsync())
            .Should().Equal(38);
        (await _db.EntityVerses.AnyAsync(v => v.EntityId == moab.Id)).Should().BeFalse();
    }

    /// <summary>Galilee is a name; a Galilean, and a Sidonian are not, and Persis and Herodias are.</summary>
    [Fact]
    public async Task TheGreekGateRefusesGentilicsAndTitles()
    {
        _db.StrongEntries.AddRange(
            Entry("G9056", "Γαλιλαία", "Galilæa (i.e. the heathen circle), a region of Palestine"),
            Entry("G9057", "Γαλιλαῖος", "Galilean or belonging to Galilea"),
            Entry("G9606", "Σιδώνιος", "a Sidonian, i.e. inhabitant of Sidon"),
            Entry("G9069", "Περσίς", "a Persian woman; Persis, a Christian female"),
            Entry("G9266", "Ἡρωδιάς", "Herodias, a woman of the Heodian family"),
            Entry("G9999", "ἄνθρωπος", "a human being"));
        await _db.SaveChangesAsync();

        var names = await _db.Database
            .SqlQueryRaw<string>($"""SELECT strong_number AS "Value" FROM strong_entry lexicon WHERE strong_number IN ('G9056','G9057','G9606','G9069','G9266','G9999') AND {EntityAnnotationLoader.GreekName}""")
            .ToListAsync();

        names.Should().BeEquivalentTo("G9056", "G9069", "G9266");
    }

    private static StrongEntry Entry(string number, string lemma, string definition) =>
        new() { StrongNumber = number, Lemma = lemma, Definition = definition };

    private void Word(Text text, int chapter, int verse, int position, string number, string state)
    {
        var word = _db.WordAt(text, chapter, verse, position);
        word.StrongNumber = number;
        word.Morphology = JsonDocument.Parse($$"""{"pos": "subs", "state": "{{state}}"}""");
    }

    private Entity Record(string slug, EntityKind kind, int? origin)
    {
        var entity = new Entity
        {
            Kind = kind, Slug = slug, Name = slug, SourceId = $"test:{slug}", Source = "test", OriginEntityId = origin,
        };
        _db.Entities.Add(entity);
        return entity;
    }
}
