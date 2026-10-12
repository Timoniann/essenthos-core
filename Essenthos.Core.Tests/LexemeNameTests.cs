using System.Text.Json;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Loading.Encyclopedia;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Essenthos.Core.Tests;

/// <summary>
/// Hebrew names nothing answered, given the record their lexeme names elsewhere or the people their
/// descent group is named after; and the ones that must stay unanswered.
/// </summary>
[Collection(WitnessDatabaseCollection.Name)]
public sealed class LexemeNameTests : IDisposable
{
    private const string Mahanaim = "מַחֲנַיִם";
    private const string Ammon = "עַמֹּון";

    private readonly AppDbContext _db;
    private readonly Text _hebrew;
    private readonly Text _english;
    private readonly Entity _place;
    private readonly Entity _people;

    public LexemeNameTests(WitnessDatabase database)
    {
        _db = database.NewContext();
        _db.Database.ExecuteSqlRaw("DELETE FROM text");
        _db.Database.ExecuteSqlRaw("DELETE FROM entity");

        _hebrew = Corpus.Add(_db, EntityCandidates.Witness, TextKind.CriticalEdition, "hbo",
            (1, 1, ["a"]), (1, 2, ["b"]), (1, 3, ["c"]), (1, 4, ["d"]), (1, 5, ["e"]));
        _english = Corpus.Add(_db, "ENGLISH", TextKind.Translation, "eng", (1, 2, ["Mahanaim"]));
        _db.SaveChanges();

        Mark(1, Mahanaim, "H4266", "topo");
        Mark(2, Mahanaim, "H4264", "topo");
        Mark(3, Mahanaim, "H4264", "pers");
        Mark(4, Ammon, "H5983", "gens");
        Mark(5, Ammon, "H5983", "pers,gens");

        _place = Record(EntityKind.Place, "mahanaim");
        var man = Record(EntityKind.Person, "benammi");
        _people = Record(EntityKind.People, "ammonites");
        var land = Record(EntityKind.Place, "ammon");
        _db.SaveChanges();
        _db.EntityNames.Add(new EntityName { EntityId = land.Id, Label = "Ammon", HebrewStrongNumber = "H5983", Kind = "name" });
        _people.OriginEntityId = man.Id;
        _db.EntityNames.Add(new EntityName { EntityId = _place.Id, Label = "Mahanaim", HebrewStrongNumber = "H4266", Kind = "proper name" });
        _db.EntityNames.Add(new EntityName { EntityId = man.Id, Label = "Ben-ammi", HebrewStrongNumber = "H5983", Kind = "proper name" });
        _db.WordEntities.Add(new WordEntity
        {
            WordId = _db.WordAt(_hebrew, 1, 1, 1).Id, EntityId = _place.Id, Method = LinkMethod.StrongNumber,
            Confidence = 0.9, Source = "a resolution by number",
        });

        var link = new Link
        {
            FromTextId = _hebrew.Id, ToTextId = _english.Id, Relation = LinkRelation.Renders,
            Method = LinkMethod.Aligner, Confidence = 0.9, Provenance = new() { Source = "a test" },
        };
        _db.Links.Add(link);
        _db.LinkWords.Add(new LinkWord { Link = link, Word = _db.WordAt(_hebrew, 1, 2, 1), Side = LinkSide.From });
        _db.LinkWords.Add(new LinkWord { Link = link, Word = _db.WordAt(_english, 1, 2, 1), Side = LinkSide.To });
        _db.SaveChanges();
    }

    public void Dispose()
    {
        _db.Database.ExecuteSqlRaw("DELETE FROM text");
        _db.Database.ExecuteSqlRaw("DELETE FROM entity");
        _db.Dispose();
    }

    private void Mark(int verse, string lemma, string number, string nameType)
    {
        var word = _db.WordAt(_hebrew, 1, verse, 1);
        word.Lemma = lemma;
        word.StrongNumber = number;
        word.Morphology = JsonDocument.Parse($$"""{"pos": "nmpr", "nameType": "{{nameType}}"}""");
        _db.SaveChanges();
    }

    private Entity Record(EntityKind kind, string slug)
    {
        var entity = new Entity { Kind = kind, Slug = slug, Name = slug, SourceId = slug, Source = "a test" };
        _db.Entities.Add(entity);
        return entity;
    }

    private Task<LexemeNameOutcome> Load() =>
        new LexemeNameLoader(_db, NullLogger<LexemeNameLoader>.Instance).Load();

    private async Task<string[]> Names(Text text, int verse) =>
        await _db.WordEntities
            .Where(a => a.Word!.TextId == text.Id && a.Word.Verse!.Number == verse)
            .Select(a => a.Entity!.Slug)
            .ToArrayAsync();

    /// <summary>
    /// Mahanaim of the Song of Songs carries the number of <em>camp</em>, which names nobody; every
    /// other occurrence of the lexeme names the town, so this one does, and the translation linked to
    /// it is given the town too.
    /// </summary>
    [Fact]
    public async Task ANameWhoseNumberNamesNobodyIsTheRecordItsLexemeNames()
    {
        var outcome = await Load();

        outcome.ByLexeme.Should().Be(1);
        (await Names(_hebrew, 2)).Should().Equal("mahanaim");
        (await Names(_english, 2)).Should().Equal("mahanaim");
    }

    /// <summary>A lexeme of a town does not make a word BHSA marks as a man into the town.</summary>
    [Fact]
    public async Task AWordMarkedAnotherKindThanTheRecordIsLeftAlone()
    {
        await Load();

        (await Names(_hebrew, 3)).Should().BeEmpty();
    }

    /// <summary>
    /// <em>The children of Ammon</em>: BHSA marks the name the descent group and nothing else, and the
    /// people named after the man who bears the number is the word's answer, though the land bears it
    /// too. Where it marks the name a man and a people at once it says neither, and the word is left.
    /// </summary>
    [Fact]
    public async Task ADescentGroupIsThePeopleNamedAfterItsBearer()
    {
        var outcome = await Load();

        outcome.ByDescent.Should().Be(1);
        (await Names(_hebrew, 4)).Should().Equal("ammonites");
        (await Names(_hebrew, 5)).Should().BeEmpty();
    }

    [Fact]
    public async Task RunningAgainWritesNothing()
    {
        await Load();
        var before = await _db.WordEntities.CountAsync();

        var again = await Load();

        (again.ByLexeme + again.ByDescent).Should().Be(0);
        (await _db.WordEntities.CountAsync()).Should().Be(before);
    }
}
