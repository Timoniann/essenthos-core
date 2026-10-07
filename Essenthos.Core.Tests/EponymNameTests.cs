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
/// Reuben and Israel where nothing says whether the sentence means the man or the tribe: the
/// ancestor, on the owner's ruling, and the children of Israel the people.
/// </summary>
[Collection(WitnessDatabaseCollection.Name)]
public sealed class EponymNameTests : IDisposable
{
    private const string Reuben = "H7205";

    private const string Israel = "H3478";

    private const string Son = "H1121";

    private const string King = "H4428";

    private readonly AppDbContext _db;
    private readonly EponymNameLoader _loader;
    private readonly Text _hebrew;

    public EponymNameTests(WitnessDatabase database)
    {
        _db = database.NewContext();
        Clear();
        _loader = new EponymNameLoader(_db, NullLogger<EponymNameLoader>.Instance);

        _hebrew = Corpus.Add(_db, EntityCandidates.Witness, TextKind.CriticalEdition, "hbo",
            (1, 20, ["בני", "ראובן"]),
            (1, 21, ["בני", "ישראל", "מלך", "ישראל", "ישראל"]));
        _db.AddBook(_hebrew, 2, "Exodus", (32, 13, ["ליצחק", "ולישראל"]));
        _db.AddBook(_hebrew, 11, "1 Kings", (12, 16, ["לאהליך", "ישראל"]));
        _db.AddBook(_hebrew, 13, "1 Chronicles", (5, 1, ["בכור", "ישראל"]));
        _db.SaveChanges();

        Word(1, 20, 1, Son, "subs");
        Word(1, 20, 2, Reuben, "nmpr");
        Word(1, 21, 1, Son, "subs");
        Word(1, 21, 2, Israel, "nmpr");
        Word(1, 21, 3, King, "subs");
        Word(1, 21, 4, Israel, "nmpr");
        Word(1, 21, 5, Israel, "nmpr");
        In(2, 32, 13, 1).StrongNumber = "H3327";
        Word(In(2, 32, 13, 2), Israel, "nmpr");
        Word(In(11, 12, 16, 2), Israel, "nmpr");
        In(13, 5, 1, 1).StrongNumber = "H1060";
        In(13, 5, 1, 1).Morphology = JsonDocument.Parse("""{"pos": "subs", "number": "sg", "state": "c"}""");
        Word(In(13, 5, 1, 2), Israel, "nmpr");
        _db.SaveChanges();

        var reuben = Record("reuben", EntityKind.Person, Reuben, null);
        Record("reubenites", EntityKind.People, Reuben, reuben);
        var jacob = Record("jacob", EntityKind.Person, Israel, null);
        Record("israelites", EntityKind.People, Israel, jacob);
    }

    public void Dispose()
    {
        Clear();
        _db.Dispose();
    }

    private void Clear()
    {
        _db.Database.ExecuteSqlRaw("DELETE FROM text");
        _db.Database.ExecuteSqlRaw("DELETE FROM entity");
    }

    private void Word(int chapter, int verse, int position, string number, string pos) =>
        Word(_db.WordAt(_hebrew, chapter, verse, position), number, pos);

    private Word In(int book, int chapter, int verse, int position) =>
        _db.Words.Single(w => w.TextId == _hebrew.Id && w.Verse!.Book!.CanonicalOrdinal == book
                              && w.Verse.ChapterNumber == chapter && w.Verse.Number == verse && w.Position == position);

    private static void Word(Word word, string number, string pos)
    {
        word.StrongNumber = number;
        word.Morphology = JsonDocument.Parse($$"""{"pos": "{{pos}}", "nameType": "pers,gens,topo"}""");
    }

    private Entity Record(string slug, EntityKind kind, string number, Entity? origin)
    {
        var entity = new Entity
        {
            Kind = kind, Slug = slug, Name = slug, SourceId = slug, Source = "a test", OriginEntityId = origin?.Id,
            Names = [new EntityName { Label = slug, HebrewStrongNumber = number, Kind = "proper name" }],
        };
        _db.Entities.Add(entity);
        _db.SaveChanges();
        return entity;
    }

    private Word Hebrew(int chapter, int verse, int position) => _db.WordAt(_hebrew, chapter, verse, position);

    private async Task<Dictionary<long, WordEntity>> Named() =>
        await _db.WordEntities.AsNoTracking().Include(a => a.Entity).ToDictionaryAsync(a => a.WordId);

    /// <summary><em>The children of Reuben</em> of Numbers 1:20 are counted as a tribe and are Reuben's.</summary>
    [Fact]
    public async Task ATribesNameIsItsAncestorForNow()
    {
        var outcome = await _loader.Load();

        var named = await Named();
        named[Hebrew(1, 20, 2).Id].Entity!.Slug.Should().Be("reuben");
        named[Hebrew(1, 20, 2).Id].Method.Should().Be(LinkMethod.RuleBased);
        named[Hebrew(1, 21, 5).Id].Entity!.Slug.Should().Be("jacob", "in Genesis Israel is the man");
        outcome.Eponyms.Should().Be(4);
    }

    /// <summary>The one phrase the ruling names otherwise: the children of Israel are the people.</summary>
    [Fact]
    public async Task TheChildrenOfIsraelAreThePeople()
    {
        var outcome = await _loader.Load();

        (await Named())[Hebrew(1, 21, 2).Id].Entity!.Slug.Should().Be("israelites");
        outcome.Children.Should().Be(2, "the children of Israel, and the nation of 1 Kings 12:16");
    }

    /// <summary>
    /// Israel standing alone outside Genesis is the nation — <em>to your tents, O Israel</em> — and
    /// stays the man among the fathers and as the father of his firstborn.
    /// </summary>
    [Fact]
    public async Task IsraelTheNationIsThePeopleAndTheFatherStaysTheMan()
    {
        await _loader.Load();

        var named = await Named();
        named[In(11, 12, 16, 2).Id].Entity!.Slug.Should().Be("israelites");
        named[In(2, 32, 13, 2).Id].Entity!.Slug.Should().Be("jacob", "Abraham, Isaac and Israel are the fathers");
        named[In(13, 5, 1, 2).Id].Entity!.Slug.Should().Be("jacob", "Reuben is the firstborn of the man");
    }

    /// <summary>What the rule wrote as the man on the nation is taken back and written again as the people.</summary>
    [Fact]
    public async Task TheManWrittenOnTheNationIsTakenBack()
    {
        await _loader.Load();
        var word = In(11, 12, 16, 2);
        var jacob = await _db.Entities.SingleAsync(e => e.Slug == "jacob");
        await _db.WordEntities.Where(a => a.WordId == word.Id).ExecuteDeleteAsync();
        _db.WordEntities.Add(new WordEntity
        {
            WordId = word.Id, EntityId = jacob.Id, Method = LinkMethod.RuleBased, Confidence = 0.7,
            Source = EponymNameLoader.Source, Note = "H3478, a name the tribe shares with its ancestor, so the ancestor",
        });
        await _db.SaveChangesAsync();

        var outcome = await _loader.Load();
        var again = await _loader.Load();

        outcome.AlreadyLoaded.Should().BeFalse();
        (await Named())[word.Id].Entity!.Slug.Should().Be("israelites");
        again.AlreadyLoaded.Should().BeTrue();
    }

    /// <summary><em>The king of Israel</em> is neither the man nor the tribe, and is left as it was.</summary>
    [Fact]
    public async Task ARealmIsLeftAlone()
    {
        var outcome = await _loader.Load();

        (await Named()).Should().NotContainKey(Hebrew(1, 21, 4).Id);
        outcome.Realms.Should().Be(1);
    }

    /// <summary>A word something already named keeps its answer, and a second boot writes nothing.</summary>
    [Fact]
    public async Task AWordAlreadyNamedIsNotContestedAndASecondBootWritesNothing()
    {
        var reubenites = await _db.Entities.SingleAsync(e => e.Slug == "reubenites");
        _db.WordEntities.Add(new WordEntity
        {
            WordId = Hebrew(1, 20, 2).Id, EntityId = reubenites.Id, Method = LinkMethod.ModelReading,
            Confidence = 0.9, Source = "a reading",
        });
        await _db.SaveChangesAsync();

        await _loader.Load();
        (await Named())[Hebrew(1, 20, 2).Id].Entity!.Slug.Should().Be("reubenites");

        (await _loader.Load()).AlreadyLoaded.Should().BeTrue();
    }
}
