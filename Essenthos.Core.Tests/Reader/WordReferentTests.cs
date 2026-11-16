using System.Text.Json;
using Essenthos.Core.Corpus;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Database.Entities.Enums;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Xunit;

namespace Essenthos.Core.Tests.Reader;

/// <summary>
/// Whom a pronoun refers to when a chapter is read: the record its antecedent names, on the original's word and on the
/// translation's word rendering it, followed through a pronoun that refers to a pronoun, and never a record the word names
/// itself.
/// </summary>
[Collection(WitnessDatabaseCollection.Name)]
public sealed class WordReferentTests : IDisposable
{
    private const string Macula = "MACULA Greek Linguistic Datasets, available at https://github.com/Clear-Bible/macula-greek/";

    private readonly AppDbContext _db;
    private readonly IDbContextTransaction _transaction;
    private readonly Text _greek;
    private readonly Text _english;

    public WordReferentTests(WitnessDatabase database)
    {
        _db = database.NewContext();
        _transaction = _db.Database.BeginTransaction();

        // Mark 5:6-7 cut down: Jesus ... him (αὐτόν) ... he (αὐτός, referring to αὐτόν) ... the man (ἄνθρωπος) ... him (αὐτῷ, to the man)
        // ... Jesus ... Legion beside a pronoun ... them (Jesus and the man) ... the man's name (a noun the source refers back).
        _greek = Corpus.Add(_db, "NESTLE1904", TextKind.PrintedEdition, "grc",
            (5, 6, ["Ἰησοῦν", "αὐτόν", "αὐτός", "ἄνθρωπος", "αὐτῷ", "Ἰησοῦ", "αὐτῷ", "αὐτοῖς", "ὄνομα"]),
            (5, 7, ["γὰρ", "μοι", "μοι", "σοι", "αὐτῷ"]));
        _english = Corpus.Add(_db, "KJV", TextKind.Translation, "eng",
            (5, 6, ["Jesus", "him", "he", "man", "him", "Jesus", "Legion", "them", "name", "answered"]),
            (5, 7, ["for", "for", "me", "me", "unto", "thee", "to", "him"]));
        _db.SaveChanges();

        var jesus = Record("jesus");
        var anointed = Record("anointed");
        var legion = Record("legion");
        _db.SaveChanges();
        _db.WordEntities.Add(new WordEntity
        {
            WordId = Greek(1).Id, EntityId = anointed.Id, Method = LinkMethod.StatedBySource, Source = "a test", Role = WordEntityRoles.Title,
        });
        Name(Greek(1), jesus);
        Name(English(1), jesus);
        Name(Greek(6), jesus);
        Name(English(6), jesus);
        Name(English(7), legion);
        for (var position = 1; position <= 9; position++)
        {
            Align(English(position), Greek(position));
            Classed(Greek(position), position is 1 or 4 or 6 or 9 ? "noun" : "pron");
        }

        Refers(Greek(2), Greek(1));
        Refers(Greek(3), Greek(2));
        Refers(Greek(5), Greek(4));
        Refers(Greek(6), Greek(1));
        Refers(Greek(7), Greek(1));
        Refers(Greek(8), Greek(1));
        Refers(Greek(8), Greek(4));
        Refers(Greek(9), Greek(1));
        Align(English(10), Greek(3));
        Align(English(10), Greek(4));

        // Verse 7, all of it referring to Jesus: "for me" in one run for μοι, beside a "for" that renders γάρ and a "me" that
        // renders μοι alone; "unto thee" in one run for σοι, neither word rendering anything else; "to him" for αὐτῷ, "to"
        // marked by the mapping as going with "him".
        Classed(Greek(1, 7), "conj");
        for (var position = 2; position <= 5; position++)
        {
            Classed(Greek(position, 7), "pron");
            Refers(Greek(position, 7), Greek(1));
        }

        Align(English(1, 7), Greek(1, 7));
        Align([English(2, 7), English(3, 7)], Greek(2, 7));
        Align([English(4, 7)], Greek(3, 7));
        Align([English(5, 7), English(6, 7)], Greek(4, 7));
        Align([English(7, 7), English(8, 7)], Greek(5, 7), attached: English(7, 7), head: English(8, 7));
        _db.SaveChanges();
    }

    public void Dispose()
    {
        _transaction.Rollback();
        _transaction.Dispose();
        _db.Dispose();
    }

    [Fact]
    public async Task APronounRefersToTheRecordItsAntecedentNames()
    {
        var word = await Read(_greek, 2);
        word.Referents.Should().BeEquivalentTo([new ReferentResponse("person", "jesus", "Ἰησοῦν", Macula)]);
        word.Entities.Should().BeEmpty("a pronoun refers to Jesus; it does not name him");
    }

    [Fact]
    public async Task AnAntecedentNamingATitleAndItsBearerGivesTheBearer() =>
        (await Read(_greek, 2)).Referents.Should().ContainSingle().Which.Slug.Should().Be("jesus");

    [Fact]
    public async Task TheTranslationsWordRenderingThePronounRefersToTheSameRecord() =>
        (await Read(_english, 2)).Referents.Should().ContainSingle().Which.Slug.Should().Be("jesus");

    [Fact]
    public async Task APronounReferringToAPronounIsFollowedToTheName()
    {
        var word = await Read(_greek, 3);
        word.Referents.Should().ContainSingle().Which.Should().Be(new ReferentResponse("person", "jesus", "Ἰησοῦν", Macula));
    }

    [Fact]
    public async Task AnAntecedentNamingNobodyGivesNothing() =>
        (await Read(_english, 5)).Referents.Should().BeNull();

    [Fact]
    public async Task AWordNamingTheRecordItRefersToIsNotToldItAgain() =>
        (await Read(_english, 6)).Referents.Should().BeNull();

    [Fact]
    public async Task APronounReferringToSeveralWordsIsGivenNothingWhenOneNamesNobody() =>
        (await Read(_english, 8)).Referents.Should().BeNull("them is Jesus and the man; Jesus alone would be wrong");

    [Fact]
    public async Task AWordRenderingThePronounTogetherWithAnotherWordIsGivenNothing() =>
        (await Read(_english, 10)).Referents.Should().BeNull("answered renders the verb with the pronoun");

    [Fact]
    public async Task ANounTheSourceRefersBackIsGivenNothing() =>
        (await Read(_greek, 9)).Referents.Should().BeNull();

    [Fact]
    public async Task ANameLinkedTogetherWithAPronounIsNotThePronoun() =>
        (await Read(_english, 7)).Referents.Should().BeNull("the word names Legion; the pronoun it is linked with refers to Jesus");

    [Fact]
    public async Task APronounReferringToSeveralRecordsShowsThemInTheOrderTheSourceListsThem()
    {
        var demoniac = Record("demoniac");
        _db.SaveChanges();
        Name(Greek(4), demoniac);
        _db.SaveChanges();
        var (word, jesus, man) = (Greek(8).Id, Greek(1).Id, Greek(4).Id);
        await _db.WordReferents.Where(r => r.WordId == word)
            .ExecuteUpdateAsync(set => set.SetProperty(r => r.Ordinal, r => r.ReferentWordId == man ? 0 : 1));

        (await Read(_greek, 8)).Referents!.Select(r => r.Slug).Should().Equal(["demoniac", "jesus"], "MACULA lists the man first");
        jesus.Should().BeLessThan(man, "the order is the source's, not the words'");
    }

    [Fact]
    public async Task APrepositionInOneRunWithThePronounIsNotThePronoun()
    {
        (await Read(_english, 2, 7)).Referents.Should().BeNull("for of 'for me' renders γάρ elsewhere in the chapter");
        (await Read(_english, 3, 7)).Referents.Should().ContainSingle().Which.Slug.Should().Be("jesus");
    }

    [Fact]
    public async Task ARunWhoseWordsRenderNothingElseIsGivenNothing()
    {
        (await Read(_english, 5, 7)).Referents.Should().BeNull("nothing tells unto from thee");
        (await Read(_english, 6, 7)).Referents.Should().BeNull("nothing tells unto from thee");
    }

    [Fact]
    public async Task AWordTheMappingAttachesToThePronounIsNotThePronoun()
    {
        (await Read(_english, 7, 7)).Referents.Should().BeNull("to goes with him");
        (await Read(_english, 8, 7)).Referents.Should().ContainSingle().Which.Slug.Should().Be("jesus");
    }

    private async Task<TextWordResponse> Read(Text text, int position, int verse = 6)
    {
        var chapter = await ChapterReading.ReadChapter(_db, text.Id, 1, 5, default);
        return chapter.Single(v => v.Number == verse).Words[position - 1];
    }

    private Word Greek(int position, int verse = 6) => _db.WordAt(_greek, 5, verse, position);

    private Word English(int position, int verse = 6) => _db.WordAt(_english, 5, verse, position);

    private Entity Record(string slug)
    {
        var entity = new Entity { Kind = EntityKind.Person, Slug = slug, Name = char.ToUpperInvariant(slug[0]) + slug[1..] };
        _db.Entities.Add(entity);
        return entity;
    }

    private void Name(Word word, Entity entity) =>
        _db.WordEntities.Add(new WordEntity
        {
            WordId = word.Id, EntityId = entity.Id, Method = LinkMethod.StatedBySource, Source = "a test", Role = WordEntityRoles.Name,
        });

    private void Classed(Word word, string kind) =>
        _db.WordParsings.Add(new WordParsing
        {
            WordId = word.Id, Morphology = JsonDocument.Parse($$"""{"class":"{{kind}}"}"""), Method = LinkMethod.StatedBySource, Source = Macula,
        });

    private void Refers(Word word, Word referent) =>
        _db.WordReferents.Add(new WordReferent { WordId = word.Id, ReferentWordId = referent.Id, Method = LinkMethod.StatedBySource, Source = Macula });

    private void Align(Word from, Word to) => Align([from], to);

    private void Align(Word[] from, Word to, Word? attached = null, Word? head = null)
    {
        var link = new Link
        {
            FromTextId = from[0].TextId,
            ToTextId = to.TextId,
            Relation = LinkRelation.Renders,
            Method = LinkMethod.Aligner,
            Confidence = 0.9,
            Provenance = new() { Source = "a test" },
        };
        _db.Links.Add(link);
        foreach (var word in from)
        {
            _db.LinkWords.Add(word == attached
                ? new LinkWord { Link = link, Word = word, Side = LinkSide.From, Role = LinkWordRole.Attached, HeadWord = head }
                : new LinkWord { Link = link, Word = word, Side = LinkSide.From });
        }

        _db.LinkWords.Add(new LinkWord { Link = link, Word = to, Side = LinkSide.To });
    }
}
