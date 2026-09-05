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
/// Which words end up saying whom they name, and — the half that matters more — which words are
/// left saying nothing.
///
/// Every case here is one the data invites you to get wrong. A name twenty-three men share, a name
/// nobody in the encyclopedia bears, a city that appears in a man's title, a word BHSA marks as a
/// place whose only entity is a person, and a name BHSA declines to classify at all: each of them
/// would produce an annotation if the join were written the obvious way, and each of those
/// annotations would be a confident wrong answer a reader could not tell from scholarship.
///
/// <para>
/// They are asked of Postgres because the loader is a set of statements rather than a loop, and
/// what is under test is what those statements select.
/// </para>
/// </summary>
[Collection(WitnessDatabaseCollection.Name)]
public sealed class EntityAnnotationTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly EntityAnnotationLoader _loader;
    private readonly Text _hebrew;
    private readonly Text _english;

    /// <summary>
    /// Genesis 1, one verse per case, so a failure names the case rather than a position. The
    /// English is one word per verse standing opposite the Hebrew, plus a last word standing
    /// opposite two of them at once.
    /// </summary>
    public EntityAnnotationTests(WitnessDatabase database)
    {
        _db = database.NewContext();
        _db.Database.ExecuteSqlRaw("DELETE FROM text");
        _db.Database.ExecuteSqlRaw("DELETE FROM entity");
        _loader = new EntityAnnotationLoader(_db, NullLogger<EntityAnnotationLoader>.Instance);

        _hebrew = Corpus.Add(_db, EntityAnnotationLoader.Witness, TextKind.CriticalEdition, "hbo",
            (1, 1, ["משה"]),
            (1, 2, ["זכריה"]),
            (1, 3, ["ירושלם"]),
            (1, 4, ["מלך"]),
            (1, 5, ["כנען"]),
            (1, 6, ["ישראל"]),
            (1, 7, ["פלמוני"]));

        _english = Corpus.Add(_db, "kjv", TextKind.Translation, "eng",
            (1, 1, ["Moses"]),
            (1, 2, ["Zechariah"]),
            (1, 8, ["Both"]));

        _db.SaveChanges();

        Annotate(1, "H4872", "pers");
        Annotate(2, "H2148", "pers");
        Annotate(3, "H3389", "topo");
        Annotate(4, "H4428", "pers");
        Annotate(5, "H3667", "topo");
        Annotate(6, "H3478", "pers,gens,topo");
        Annotate(7, "H8888", "pers");

        var moses = Person("moses", "Moses", "H4872");
        Person("zechariah-1", "Zechariah", "H2148");
        Person("zechariah-2", "Zechariah", "H2148");
        Place("jerusalem", "Jerusalem", "H3389");

        // A title is not a name. Its Strong numbers are the numbers of its words, so reading them
        // as names puts the city on the man who is called king of it.
        var adonizedek = Person("adonizedek", "Adonizedek", null);
        Name(adonizedek, "King of Jerusalem", "H4428,H3389", "title");

        // The land, whose only entity is the man it is named after. BHSA marks the word topo and
        // the encyclopedia answers with a person, and those are not the same claim.
        Person("canaan", "Canaan", "H3667");

        // The name BHSA itself declines to classify: person, people or place, on every occurrence.
        Person("jacob", "Israel", "H3478");

        _db.SaveChanges();

        // The encyclopedia's own answer to the same question, for the one verse it speaks about.
        _db.EntityVerses.Add(new EntityVerse
        {
            EntityId = moses.Id,
            CanonicalBook = 1,
            CanonicalChapter = 1,
            CanonicalVerse = 1,
            Source = "a test",
        });
        _db.SaveChanges();
    }

    public void Dispose()
    {
        _db.Database.ExecuteSqlRaw("DELETE FROM text");
        _db.Database.ExecuteSqlRaw("DELETE FROM entity");
        _db.Dispose();
    }

    private void Annotate(int verse, string number, string nameType)
    {
        var word = _db.WordAt(_hebrew, 1, verse, 1);
        word.StrongNumber = number;
        word.Morphology = JsonDocument.Parse($$"""{"pos": "subs", "nameType": "{{nameType}}"}""");
        _db.SaveChanges();
    }

    private Entity Person(string slug, string name, string? number) =>
        Add(slug, name, EntityKind.Person, number);

    private Entity Place(string slug, string name, string? number) =>
        Add(slug, name, EntityKind.Place, number);

    private Entity Add(string slug, string name, EntityKind kind, string? number)
    {
        var entity = new Entity
        {
            Kind = kind, Slug = slug, Name = name, SourceId = slug, Source = "a test",
        };
        _db.Entities.Add(entity);

        if (number is not null)
        {
            Name(entity, name, number, "name");
        }

        return entity;
    }

    private void Name(Entity entity, string label, string number, string kind) =>
        _db.EntityNames.Add(new EntityName
        {
            Entity = entity, Label = label, HebrewStrongNumber = number, Kind = kind,
        });

    /// <summary>
    /// A link the King James's own mapping states, which carries no confidence of its own, and one
    /// an aligner proposed, which does. What each is worth has to reach the annotation.
    /// </summary>
    private void Link(Word from, Word to, LinkMethod method, double? confidence)
    {
        var link = new Link
        {
            FromTextId = from.TextId,
            ToTextId = to.TextId,
            Relation = LinkRelation.Renders,
            Method = method,
            Confidence = confidence,
            Source = "a test",
        };
        _db.Links.Add(link);
        _db.LinkWords.Add(new LinkWord { Link = link, Word = from, Side = LinkSide.From });
        _db.LinkWords.Add(new LinkWord { Link = link, Word = to, Side = LinkSide.To });
        _db.SaveChanges();
    }

    private async Task<Dictionary<long, string>> Load()
    {
        await _loader.Load();
        return await _db.WordEntities
            .ToDictionaryAsync(a => a.WordId, a => a.Entity!.Slug);
    }

    private Word Hebrew(int verse) => _db.WordAt(_hebrew, 1, verse, 1);

    [Fact]
    public async Task ANameOnlyOnePersonBearsIsAnnotatedWithThatPerson()
    {
        var named = await Load();
        named.Should().ContainKey(Hebrew(1).Id).WhoseValue.Should().Be("moses");
    }

    /// <summary>
    /// The Zechariah case, which is the whole reason this loader stops where it does. The number
    /// is a name and the name is several men's, and nothing about the number says which.
    /// </summary>
    [Fact]
    public async Task ANameSeveralPeopleShareIsLeftUnannotated()
    {
        var named = await Load();
        named.Should().NotContainKey(Hebrew(2).Id);
    }

    [Fact]
    public async Task ANameNobodyInTheEncyclopediaBearsIsLeftUnannotated()
    {
        var named = await Load();
        named.Should().NotContainKey(Hebrew(7).Id);
    }

    [Fact]
    public async Task APlaceNameResolvesToThePlace()
    {
        var named = await Load();
        named.Should().ContainKey(Hebrew(3).Id).WhoseValue.Should().Be("jerusalem");
    }

    /// <summary>
    /// <em>King of Jerusalem</em> records the numbers of both its words against Adonizedek. Read as
    /// a name, that makes H4428 — the common noun <em>king</em> — his name, and would annotate every
    /// king in the Hebrew Bible as him.
    /// </summary>
    [Fact]
    public async Task TheWordsOfATitleAreNotReadAsNames()
    {
        var named = await Load();
        named.Should().NotContainKey(Hebrew(4).Id);
    }

    /// <summary>
    /// The city keeps its own name even though a man's title contains it. Excluding the title has
    /// to exclude it from both directions of the count, or the city would be contested by the man.
    /// </summary>
    [Fact]
    public async Task ATitleDoesNotContestTheNameItContains()
    {
        var named = await Load();
        named.Should().ContainKey(Hebrew(3).Id).WhoseValue.Should().Be("jerusalem");
    }

    /// <summary>
    /// The land of Canaan, annotated as the person Canaan, is the mistake the old schema shipped.
    /// BHSA marks this occurrence a place and the encyclopedia holds only the man, so the two do
    /// not agree and nothing is written.
    /// </summary>
    [Fact]
    public async Task APlaceWhoseOnlyEntityIsAPersonIsLeftUnannotated()
    {
        var named = await Load();
        named.Should().NotContainKey(Hebrew(5).Id);
    }

    /// <summary>
    /// BHSA's name type belongs to the lemma, not to the occurrence: every occurrence of Israel is
    /// marked person, people and place at once, which says the name can be any of them and never
    /// that it is one here.
    /// </summary>
    [Fact]
    public async Task ANameBhsaWillNotClassifyIsLeftUnannotated()
    {
        var named = await Load();
        named.Should().NotContainKey(Hebrew(6).Id);
    }

    [Fact]
    public async Task AnAnnotationTravelsToTheWordThatRendersIt()
    {
        Link(Hebrew(1), _db.WordAt(_english, 1, 1, 1), LinkMethod.StatedBySource, null);

        var named = await Load();
        named.Should().ContainKey(_db.WordAt(_english, 1, 1, 1).Id).WhoseValue.Should().Be("moses");
    }

    /// <summary>
    /// A word can only be as sure as the step that reached it. A rendering an aligner proposed at
    /// 0.5 cannot carry an annotation as firmly as one the translators' own mapping states.
    /// </summary>
    [Fact]
    public async Task WhatTheLinkIsWorthReachesTheAnnotation()
    {
        var stated = _db.WordAt(_english, 1, 1, 1);
        Link(Hebrew(1), stated, LinkMethod.Aligner, 0.5);

        await _loader.Load();

        var carried = await _db.WordEntities.SingleAsync(a => a.WordId == stated.Id);
        var seed = await _db.WordEntities.SingleAsync(a => a.WordId == Hebrew(1).Id);
        carried.Confidence.Should().BeApproximately(seed.Confidence!.Value * 0.5, 1e-9);
    }

    /// <summary>
    /// One English word standing opposite two Hebrew names is a word the corpus cannot resolve:
    /// the links say it renders Moses and they say it renders Jerusalem, and choosing between them
    /// is the judgement this loader refuses to make.
    /// </summary>
    [Fact]
    public async Task AWordReachedFromTwoNamesIsLeftUnannotated()
    {
        var both = _db.WordAt(_english, 1, 8, 1);
        Link(Hebrew(1), both, LinkMethod.StatedBySource, null);
        Link(Hebrew(3), both, LinkMethod.StatedBySource, null);

        var named = await Load();
        named.Should().NotContainKey(both.Id);
    }

    /// <summary>
    /// A word linked to an unresolved name stays unresolved. Carrying travels along the links from
    /// what the Hebrew resolved to, so a name nothing could settle reaches nobody.
    /// </summary>
    [Fact]
    public async Task AWordRenderingAnUnresolvedNameIsLeftUnannotated()
    {
        var zechariah = _db.WordAt(_english, 1, 2, 1);
        Link(Hebrew(2), zechariah, LinkMethod.StatedBySource, null);

        var named = await Load();
        named.Should().NotContainKey(zechariah.Id);
    }

    /// <summary>
    /// Every annotation names what asserted it. An annotation with no claim is one nobody can weigh
    /// afterwards, which is the failure the link claims were already caught by once.
    /// </summary>
    [Fact]
    public async Task EveryAnnotationCarriesAClaim()
    {
        await _loader.Load();

        var unclaimed = await _db.WordEntities
            .CountAsync(a => !_db.WordEntityClaims.Any(c => c.WordEntityId == a.Id));
        unclaimed.Should().Be(0);
    }

    /// <summary>
    /// The encyclopedia's list of verses is compiled from a reading of the text rather than from
    /// Strong numbers, so where it names the same entity in the same verse it is a second and
    /// independent answer — and two answers are worth recording as two.
    /// </summary>
    [Fact]
    public async Task AVerseTheEncyclopediaAgreesAboutCarriesASecondClaim()
    {
        await _loader.Load();

        var corroborated = await _db.WordEntities.SingleAsync(a => a.WordId == Hebrew(1).Id);
        var alone = await _db.WordEntities.SingleAsync(a => a.WordId == Hebrew(3).Id);

        var both = await _db.WordEntityClaims.CountAsync(c => c.WordEntityId == corroborated.Id);
        var one = await _db.WordEntityClaims.CountAsync(c => c.WordEntityId == alone.Id);

        both.Should().Be(2);
        one.Should().Be(1);
        corroborated.Confidence.Should().BeGreaterThan(alone.Confidence!.Value);
    }

    /// <summary>
    /// The start-up pipeline runs on every boot, so a second run has to be free and has to change
    /// nothing.
    /// </summary>
    [Fact]
    public async Task RunningAgainWritesNothingFurther()
    {
        await _loader.Load();
        var first = await _db.WordEntities.CountAsync();

        var again = await _loader.Load();

        again.AlreadyLoaded.Should().BeTrue();
        (await _db.WordEntities.CountAsync()).Should().Be(first);
    }

    /// <summary>
    /// The counts the loader reports are the ones a reader is asked to trust, so they are counted
    /// rather than estimated: one number resolves for each of Moses, Jerusalem and Canaan, one is
    /// several men's, and one is nobody's.
    /// </summary>
    [Fact]
    public async Task TheOutcomeCountsWhatItRefusedAsWellAsWhatItWrote()
    {
        var outcome = await _loader.Load();

        outcome.Contested.Should().Be(1);
        outcome.Unanswered.Should().Be(2);
        outcome.ByText.Should().ContainSingle().Which.Text.Should().Be(EntityAnnotationLoader.Witness);
    }
}
