using System.Text.Json;
using Essenthos.Core.Configuration;
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

    private const string AReading = "a reading of the verse";

    private readonly AppDbContext _db;
    private readonly RealmNameLoader _loader;
    private readonly Text _hebrew;
    private readonly Text _english;
    private readonly string _resources;

    public RealmNameTests(WitnessDatabase database)
    {
        _db = database.NewContext();
        Clear();
        _resources = Path.Combine(Path.GetTempPath(), $"essenthos-realm-{Guid.NewGuid():N}");
        _loader = new RealmNameLoader(
            _db, new ReviewLists(_db.Database.GetDbConnection().Database), NullLogger<RealmNameLoader>.Instance);

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
        if (Directory.Exists(_resources))
        {
            Directory.Delete(_resources, recursive: true);
        }
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

    private Word English(int chapter, int verse, int position) => _db.WordAt(_english, chapter, verse, position);

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

    /// <summary>An annotation and the claim standing on it, as every pass writes them.</summary>
    private WordEntity Annotate(Word word, string slug, LinkMethod method, string source, string? note = null)
    {
        var confidence = method is LinkMethod.Manual or LinkMethod.StatedBySource ? (double?)null : 0.8;
        var annotation = new WordEntity
        {
            WordId = word.Id, EntityId = _db.Entities.Single(e => e.Slug == slug).Id, Method = method,
            Confidence = confidence, Source = source, Note = note,
            Claims = [new WordEntityClaim { Method = method, Confidence = confidence, Source = source, Note = note }],
        };
        _db.WordEntities.Add(annotation);
        _db.SaveChanges();
        return annotation;
    }

    private async Task<Dictionary<long, WordEntity>> Named() =>
        await _db.WordEntities.AsNoTracking().Include(a => a.Entity).ToDictionaryAsync(a => a.WordId);

    private async Task<List<string>> NamedAt(Word word) =>
        await _db.WordEntities.Where(a => a.WordId == word.Id).Select(a => a.Entity!.Slug).ToListAsync();

    private string Review => new ReviewLists(_db.Database.GetDbConnection().Database)
        .For(_resources, RealmNameLoader.ReviewFile, _db.Database.GetDbConnection().Database);

    [Fact]
    public async Task TheNameAfterAKingOrALandIsThePeople()
    {
        var outcome = await _loader.Load(_resources);

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
        await _loader.Load(_resources);

        (await Named()).Should().NotContainKey(Hebrew(1, 1, 4).Id);
    }

    /// <summary>A king standing beside the name rather than governing it is not the construct.</summary>
    [Fact]
    public async Task OnlyTheConstructGoverns()
    {
        await _loader.Load(_resources);

        (await Named()).Should().NotContainKey(Hebrew(1, 3, 2).Id);
    }

    /// <summary>No people record bears Amalek's number, so the word is counted and named for the list.</summary>
    [Fact]
    public async Task ANameNoPeopleBearsIsListed()
    {
        var outcome = await _loader.Load(_resources);

        (await Named()).Should().NotContainKey(Hebrew(1, 2, 4).Id);
        outcome.Unheld.Should().Be(1);
        outcome.UnheldNames.Should().ContainSingle().Which.Number.Should().Be(Amalek);
    }

    /// <summary>
    /// A model that read the ancestor gave the answer the ruling overturns, and gives way — with the
    /// word it carried that reading to, and a line in the review list for each.
    /// </summary>
    [Fact]
    public async Task AModelsReadingOfTheAncestorGivesWayWithWhatItWasCarriedTo()
    {
        var rendering = English(1, 1, 2);
        Link(rendering, Hebrew(1, 1, 2));
        Annotate(Hebrew(1, 1, 2), "jacob", LinkMethod.ModelReading, AReading);
        Annotate(rendering, "jacob", LinkMethod.ModelReading, AReading,
            $"through bhsa word {Hebrew(1, 1, 2).Id}, linked by strong-number");

        var outcome = await _loader.Load(_resources);

        (await NamedAt(Hebrew(1, 1, 2))).Should().Equal("israelites");
        (await NamedAt(rendering)).Should().Equal("israelites");
        outcome.Replaced.Should().Be(2);
        var review = await File.ReadAllTextAsync(Review);
        review.Should().Contain("\"replaced\": \"jacob\"").And.Contain("\"now\": \"israelites\"");
    }

    /// <summary>A reading the verse list corroborates carries a source's word, and stands.</summary>
    [Fact]
    public async Task AReadingASourceCorroboratesStands()
    {
        var reading = Annotate(Hebrew(1, 1, 2), "jacob", LinkMethod.ModelReading, AReading);
        _db.WordEntityClaims.Add(new WordEntityClaim
        {
            WordEntityId = reading.Id, Method = LinkMethod.StatedBySource, Source = "the verse list",
        });
        await _db.SaveChangesAsync();

        var outcome = await _loader.Load(_resources);

        (await NamedAt(Hebrew(1, 1, 2))).Should().Equal("jacob");
        outcome.Spoken.Should().Be(1);
        outcome.Replaced.Should().Be(0);
    }

    /// <summary>A reading of somebody other than the ancestor is a reading this rule does not overturn.</summary>
    [Fact]
    public async Task AReadingOfSomebodyElseStands()
    {
        Annotate(Hebrew(1, 1, 2), "amalek", LinkMethod.ModelReading, AReading);

        await _loader.Load(_resources);

        (await NamedAt(Hebrew(1, 1, 2))).Should().Equal("amalek");
    }

    [Fact]
    public async Task APersonsWordIsNotContested()
    {
        Annotate(Hebrew(1, 1, 2), "jacob", LinkMethod.Manual, "a ruling");

        var outcome = await _loader.Load(_resources);

        (await NamedAt(Hebrew(1, 1, 2))).Should().Equal("jacob");
        outcome.Spoken.Should().Be(1);
    }

    [Fact]
    public async Task ThePeopleTravelsToTheTranslationTheLinksReach()
    {
        var rendering = English(1, 1, 2);
        Link(rendering, Hebrew(1, 1, 2));
        await _db.SaveChangesAsync();

        await _loader.Load(_resources);

        (await Named())[rendering.Id].Entity!.Slug.Should().Be("israelites");
    }

    /// <summary>What only the name consensus said of a translation word gives way to the people.</summary>
    [Fact]
    public async Task AWordOnlyTheConsensusNamedGivesWay()
    {
        var rendering = English(1, 2, 2);
        Link(rendering, Hebrew(1, 2, 2));
        Annotate(rendering, "jacob", LinkMethod.Lexical, NameConsensusPass.Source);

        var outcome = await _loader.Load(_resources);

        (await NamedAt(rendering)).Should().Equal("israelites");
        outcome.Replaced.Should().Be(1);
        outcome.Declined.Should().Be(0);
    }

    /// <summary>
    /// Where the consensus found somebody other than the ancestor, the link reached the wrong word,
    /// and the consensus stands.
    /// </summary>
    [Fact]
    public async Task AWordTheConsensusNamedAsSomebodyElseIsLeft()
    {
        var rendering = English(1, 2, 2);
        Link(rendering, Hebrew(1, 2, 2));
        Annotate(rendering, "amalek", LinkMethod.Lexical, NameConsensusPass.Source);

        var outcome = await _loader.Load(_resources);

        (await NamedAt(rendering)).Should().Equal("amalek");
        outcome.Declined.Should().Be(1);
    }

    /// <summary>A translation word something stronger named keeps that as its only answer.</summary>
    [Fact]
    public async Task ATranslationWordSomethingStrongerNamedIsLeft()
    {
        var rendering = English(1, 2, 2);
        Link(rendering, Hebrew(1, 2, 2));
        Annotate(rendering, "jacob", LinkMethod.RuleBased, "Essenthos, on the project owner's ruling of 2026-09-16");

        var outcome = await _loader.Load(_resources);

        (await NamedAt(rendering)).Should().Equal("jacob");
        outcome.Declined.Should().Be(1);
    }

    [Fact]
    public async Task ASecondRunDoesNothing()
    {
        await _loader.Load(_resources);

        (await _loader.Load(_resources)).AlreadyLoaded.Should().BeTrue();
    }
}
