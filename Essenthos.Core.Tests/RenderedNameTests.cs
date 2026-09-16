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
/// 2 Samuel 2, where Abner named nobody in any text: BHSA numbers him H74 and the encyclopedia files
/// him under another number. Each case is one thing the verse list and the King James's spelling have
/// to say before a word whose number no record bears is taken to be a record.
/// </summary>
[Collection(WitnessDatabaseCollection.Name)]
public sealed class RenderedNameTests : IDisposable
{
    private const string BibleData =
        "BibleData by Brady Stephenson, github.com/BradyStephenson/bible-data, CC BY 4.0";

    private readonly AppDbContext _db;
    private readonly RenderedNameLoader _loader;
    private readonly Text _hebrew;
    private readonly Text _english;

    /// <summary>
    /// One verse per case, one Hebrew name and its King James rendering in each: 1 Abner, whose number
    /// no record bears; 2 a word whose number a record does bear; 3 two records spelled like the word;
    /// 4 a record the verse names under another spelling; 5 the ancestor a people is named after.
    /// </summary>
    public RenderedNameTests(WitnessDatabase database)
    {
        _db = database.NewContext();
        Clear();
        _loader = new RenderedNameLoader(_db, NullLogger<RenderedNameLoader>.Instance);

        _hebrew = Corpus.Add(_db, EntityCandidates.Witness, TextKind.CriticalEdition, "hbo",
            (2, 1, ["אבנר"]), (2, 2, ["אבנר"]), (2, 3, ["מיכה"]), (2, 4, ["אבנר"]), (2, 5, ["ישראל"]));
        _english = Corpus.Add(_db, EntityCandidates.Rendering, TextKind.Translation, "eng",
            (2, 1, ["Abner's"]), (2, 2, ["Abner"]), (2, 3, ["Micah"]), (2, 4, ["Abner"]), (2, 5, ["Israel"]));
        _db.SaveChanges();

        Name(1, "H74");
        Name(2, "H7410");
        Name(3, "H4318");
        Name(4, "H74");
        Name(5, "H3479");

        var abner = Add("abner", EntityKind.Person, "Abner", "H7410");
        var micah = Add("micah", EntityKind.Person, "Micah", "H4319");
        var micah2 = Add("micah-2", EntityKind.Person, "Micah", "H4319");
        var abiner = Add("abiner", EntityKind.Person, "Abiner", "H9999");
        var jacob = Add("jacob", EntityKind.Person, "Israel", "H3478");
        var israelites = Add("israelites", EntityKind.People, "Israelites", "H3478");
        israelites.OriginEntityId = jacob.Id;

        Attest(abner, 1);
        Attest(abner, 2);
        Attest(micah, 3);
        Attest(micah2, 3);
        Attest(abiner, 4);
        Attest(jacob, 5);
        _db.SaveChanges();

        for (var verse = 1; verse <= 5; verse++)
        {
            Link(verse);
        }
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

    private void Name(int verse, string number)
    {
        var word = Hebrew(verse);
        word.StrongNumber = number;
        word.Morphology = JsonDocument.Parse("""{"pos": "nmpr", "nameType": "pers"}""");
        _db.SaveChanges();
    }

    private Entity Add(string slug, EntityKind kind, string name, string number)
    {
        var entity = new Entity
        {
            Kind = kind, Slug = slug, Name = name, SourceId = slug, Source = "a test",
            Names = [new EntityName { Label = name, HebrewStrongNumber = number, Kind = "proper name" }],
        };
        _db.Entities.Add(entity);
        _db.SaveChanges();
        return entity;
    }

    private void Attest(Entity entity, int verse) =>
        _db.EntityVerses.Add(new EntityVerse
        {
            Entity = entity, CanonicalBook = 1, CanonicalChapter = 2, CanonicalVerse = verse, Source = BibleData,
        });

    private void Link(int verse)
    {
        var link = new Link
        {
            FromTextId = _english.Id, ToTextId = _hebrew.Id, Relation = LinkRelation.Renders,
            Method = LinkMethod.StatedBySource, Source = "a test",
        };
        _db.Links.Add(link);
        _db.LinkWords.Add(new LinkWord { Link = link, Word = English(verse), Side = LinkSide.From });
        _db.LinkWords.Add(new LinkWord { Link = link, Word = Hebrew(verse), Side = LinkSide.To });
        _db.SaveChanges();
    }

    private Word Hebrew(int verse) => _db.WordAt(_hebrew, 2, verse, 1);

    private Word English(int verse) => _db.WordAt(_english, 2, verse, 1);

    private async Task<Dictionary<long, string>> Load()
    {
        await _loader.Load();
        return await _db.WordEntities.ToDictionaryAsync(a => a.WordId, a => a.Entity!.Slug);
    }

    [Fact]
    public async Task AWordWhoseNumberNoRecordBearsIsTheRecordTheVerseNamesUnderItsRendering()
    {
        var named = await Load();

        named.Should().ContainKey(Hebrew(1).Id).WhoseValue.Should().Be("abner");
        named.Should().ContainKey(English(1).Id).WhoseValue.Should().Be("abner",
            "the answer travels to the rendering it was read from like any other");
    }

    [Fact]
    public async Task AWordWhoseNumberARecordBearsIsNotThisPasssQuestion()
    {
        var named = await Load();
        named.Should().NotContainKey(Hebrew(2).Id);
    }

    [Fact]
    public async Task TwoRecordsSpelledLikeTheWordSayNothingAboutIt()
    {
        var named = await Load();
        named.Should().NotContainKey(Hebrew(3).Id);
    }

    [Fact]
    public async Task ARecordTheVerseNamesUnderAnotherSpellingIsNobody()
    {
        var named = await Load();
        named.Should().NotContainKey(Hebrew(4).Id);
    }

    [Fact]
    public async Task AnEponymANationIsNamedAfterIsLeftAlone()
    {
        var outcome = await _loader.Load();

        var israel = Hebrew(5).Id;
        (await _db.WordEntities.AnyAsync(a => a.WordId == israel)).Should().BeFalse();
        outcome.Eponyms.Should().Be(1);
    }

    [Fact]
    public async Task LoadingTwiceWritesNothingTheSecondTime()
    {
        var first = await _loader.Load();
        var count = await _db.WordEntities.CountAsync();
        var second = await _loader.Load();

        first.Settled.Should().Be(1);
        second.AlreadyLoaded.Should().BeTrue();
        (await _db.WordEntities.CountAsync()).Should().Be(count);
    }
}
