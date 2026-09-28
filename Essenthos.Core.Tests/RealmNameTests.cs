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
/// <em>The king of Israel</em> and <em>the land of Judah</em>, where the name after a realm's word is
/// the people, on the owner's ruling of 2026-09-28.
/// </summary>
[Collection(WitnessDatabaseCollection.Name)]
public sealed class RealmNameTests : IDisposable
{
    private const string Israel = "H3478";

    private const string Amalek = "H6002";

    private const string King = "H4428";

    private const string Land = "H776";

    private const string Son = "H1121";

    private const string Tribal = "pers,gens,topo";

    private readonly AppDbContext _db;
    private readonly RealmNameLoader _loader;
    private readonly Text _hebrew;
    private readonly Text _english;

    public RealmNameTests(WitnessDatabase database)
    {
        _db = database.NewContext();
        Clear();
        _loader = new RealmNameLoader(_db, NullLogger<RealmNameLoader>.Instance);

        _hebrew = Corpus.Add(_db, EntityCandidates.Witness, TextKind.CriticalEdition, "hbo",
            (1, 1, ["מלך", "ישראל", "בני", "ישראל"]),
            (1, 2, ["ארץ", "ישראל", "מלך", "עמלק"]),
            (1, 3, ["מלך", "ישראל"]));
        _english = Corpus.Add(_db, "KJV", TextKind.Translation, "eng",
            (1, 1, ["king", "Israel"]),
            (1, 2, ["land", "Israel"]));
        _db.SaveChanges();

        Word(1, 1, 1, King, "subs", "c");
        Word(1, 1, 2, Israel, "nmpr");
        Word(1, 1, 3, Son, "subs", "c");
        Word(1, 1, 4, Israel, "nmpr");
        Word(1, 2, 1, Land, "subs", "c");
        Word(1, 2, 2, Israel, "nmpr");
        Word(1, 2, 3, King, "subs", "c");
        Word(1, 2, 4, Amalek, "nmpr", nameType: "pers,gens");
        Word(1, 3, 1, King, "subs", "a");
        Word(1, 3, 2, Israel, "nmpr");
        _db.SaveChanges();

        var jacob = Record("jacob", EntityKind.Person, Israel, null);
        Record("israelites", EntityKind.People, Israel, jacob);
        Record("amalek", EntityKind.Person, Amalek, null);
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

    private void Word(int chapter, int verse, int position, string number, string pos, string? state = null,
        string nameType = Tribal)
    {
        var word = _db.WordAt(_hebrew, chapter, verse, position);
        word.StrongNumber = number;
        word.Morphology = JsonDocument.Parse(pos == "nmpr"
            ? $$"""{"pos": "nmpr", "nameType": "{{nameType}}"}"""
            : $$"""{"pos": "{{pos}}", "state": "{{state}}"}""");
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

    private void Link(Word rendering, Word original)
    {
        var link = new Link
        {
            FromTextId = _english.Id, ToTextId = _hebrew.Id, Relation = LinkRelation.Renders,
            Method = LinkMethod.StrongNumber, Confidence = 0.9, Source = "a test",
        };
        _db.Links.Add(link);
        _db.LinkWords.Add(new LinkWord { Link = link, Word = rendering, Side = LinkSide.From });
        _db.LinkWords.Add(new LinkWord { Link = link, Word = original, Side = LinkSide.To });
    }

    private async Task<Dictionary<long, WordEntity>> Named() =>
        await _db.WordEntities.AsNoTracking().Include(a => a.Entity).ToDictionaryAsync(a => a.WordId);

    [Fact]
    public async Task TheNameAfterAKingOrALandIsThePeople()
    {
        var outcome = await _loader.Load();

        var named = await Named();
        named[Hebrew(1, 1, 2).Id].Entity!.Slug.Should().Be("israelites");
        named[Hebrew(1, 1, 2).Id].Method.Should().Be(LinkMethod.RuleBased);
        named[Hebrew(1, 1, 2).Id].Confidence.Should().Be(0.9);
        named[Hebrew(1, 2, 2).Id].Entity!.Slug.Should().Be("israelites");
        outcome.Settled.Should().Be(2);
    }

    /// <summary><em>The sons of Israel</em> may be the man's sons, and this rule says nothing of them.</summary>
    [Fact]
    public async Task TheNameAfterAWordTheManCanBeMeantByIsLeftAlone()
    {
        await _loader.Load();

        (await Named()).Should().NotContainKey(Hebrew(1, 1, 4).Id);
    }

    /// <summary>A king standing beside the name rather than governing it is not the construct.</summary>
    [Fact]
    public async Task OnlyTheConstructGoverns()
    {
        await _loader.Load();

        (await Named()).Should().NotContainKey(Hebrew(1, 3, 2).Id);
    }

    /// <summary>No people record bears Amalek's number, so the word is counted and named for the list.</summary>
    [Fact]
    public async Task ANameNoPeopleBearsIsListed()
    {
        var outcome = await _loader.Load();

        (await Named()).Should().NotContainKey(Hebrew(1, 2, 4).Id);
        outcome.Unheld.Should().Be(1);
        outcome.UnheldNames.Should().ContainSingle().Which.Number.Should().Be(Amalek);
    }

    [Fact]
    public async Task AWordAlreadyNamedIsNotContested()
    {
        var jacob = await _db.Entities.SingleAsync(e => e.Slug == "jacob");
        _db.WordEntities.Add(new WordEntity
        {
            WordId = Hebrew(1, 1, 2).Id, EntityId = jacob.Id, Method = LinkMethod.ModelReading,
            Confidence = 0.9, Source = "a reading",
        });
        await _db.SaveChangesAsync();

        var outcome = await _loader.Load();

        (await Named())[Hebrew(1, 1, 2).Id].Entity!.Slug.Should().Be("jacob");
        outcome.Spoken.Should().Be(1);
    }

    [Fact]
    public async Task ThePeopleTravelsToTheTranslationTheLinksReach()
    {
        var rendering = _db.WordAt(_english, 1, 1, 2);
        Link(rendering, Hebrew(1, 1, 2));
        await _db.SaveChangesAsync();

        await _loader.Load();

        (await Named())[rendering.Id].Entity!.Slug.Should().Be("israelites");
    }

    /// <summary>A translation word that already names somebody keeps that as its only answer.</summary>
    [Fact]
    public async Task ATranslationWordAlreadyNamingSomebodyElseIsLeft()
    {
        var rendering = _db.WordAt(_english, 1, 2, 2);
        Link(rendering, Hebrew(1, 2, 2));
        var jacob = await _db.Entities.SingleAsync(e => e.Slug == "jacob");
        _db.WordEntities.Add(new WordEntity
        {
            Word = rendering, EntityId = jacob.Id, Method = LinkMethod.Lexical, Confidence = 0.6,
            Source = "a rendering",
        });
        await _db.SaveChangesAsync();

        var outcome = await _loader.Load();

        (await _db.WordEntities.Where(a => a.WordId == rendering.Id).Select(a => a.Entity!.Slug).ToListAsync())
            .Should().Equal("jacob");
        outcome.Declined.Should().Be(1);
    }

    [Fact]
    public async Task ASecondRunDoesNothing()
    {
        await _loader.Load();

        (await _loader.Load()).AlreadyLoaded.Should().BeTrue();
    }
}
