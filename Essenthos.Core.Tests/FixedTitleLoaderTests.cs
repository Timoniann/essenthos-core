using System.Text.Json;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Loading;
using Essenthos.Core.Loading.Encyclopedia;
using Essenthos.Core.Verbs;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Essenthos.Core.Tests;

/// <summary>
/// A title the text fixes to one bearer, written on the words of its shape: what is written, what
/// crosses the links, and what is never touched.
/// </summary>
[Collection(WitnessDatabaseCollection.Name)]
public sealed class FixedTitleLoaderTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly Text _greek;
    private readonly Text _english;
    private readonly Entity _satan;
    private readonly Entity _judas;

    /// <summary>
    /// Verse 1 τοῦ διαβόλου, the devil; verse 2 εἷς διάβολός, a devil; verse 3 τοῦ διαβόλου again,
    /// already named by the owner as somebody else. The King James renders verse 1.
    /// </summary>
    public FixedTitleLoaderTests(WitnessDatabase database)
    {
        _db = database.NewContext();
        Clear();

        _greek = Corpus.Add(_db, NestleTextSource.Slug, TextKind.CriticalEdition, "grc",
            (4, 1, ["τοῦ", "διαβόλου"]), (4, 2, ["εἷς", "διάβολός"]), (4, 3, ["τοῦ", "διαβόλου"]));
        _english = Corpus.Add(_db, "KJV", TextKind.Translation, "eng", (4, 1, ["the", "devil"]));
        _db.SaveChanges();

        foreach (var verse in new[] { 1, 3 })
        {
            Parse(Greek(verse, 1), """{"pos": "det", "case": "genitive", "number": "singular", "form": "T-GSM"}""");
            Parse(Greek(verse, 2), """{"pos": "adj", "case": "genitive", "form": "A-GSM"}""");
        }

        Parse(Greek(2, 1), """{"pos": "adj", "case": "nominative", "number": "singular", "form": "A-NSM"}""");
        Parse(Greek(2, 2), """{"pos": "adj", "case": "nominative", "form": "A-NSM"}""");
        foreach (var verse in new[] { 1, 2, 3 })
        {
            Greek(verse, 2).StrongNumber = "G1228";
        }

        _satan = Add("satan", "Satan");
        _judas = Add("judas", "Judas");
        _db.SaveChanges();

        _db.WordEntities.Add(new WordEntity
        {
            WordId = Greek(3, 2).Id, EntityId = _judas.Id, Method = LinkMethod.Manual, Source = "the owner",
        });
        var link = new Link
        {
            FromTextId = _english.Id, ToTextId = _greek.Id, Relation = LinkRelation.Renders,
            Method = LinkMethod.StatedBySource, Provenance = new() { Source = "a test" },
        };
        _db.Links.Add(link);
        _db.LinkWords.Add(new LinkWord { Link = link, Word = _db.WordAt(_english, 4, 1, 2), Side = LinkSide.From });
        _db.LinkWords.Add(new LinkWord { Link = link, Word = Greek(1, 2), Side = LinkSide.To });
        _db.SaveChanges();
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

    private static void Parse(Word word, string morphology) => word.Morphology = JsonDocument.Parse(morphology);

    private Word Greek(int verse, int position) => _db.WordAt(_greek, 4, verse, position);

    private Entity Add(string slug, string name)
    {
        var entity = new Entity { Kind = EntityKind.Person, Slug = slug, Name = name, SourceId = slug, Source = "a test" };
        _db.Entities.Add(entity);
        return entity;
    }

    private FixedTitleLoader Loader() => new(_db, NullLogger<FixedTitleLoader>.Instance);

    [Theory]
    [MemberData(nameof(FixedTitleWitnessTests.Witnesses), MemberType = typeof(FixedTitleWitnessTests))]
    public async Task TheWitnessVerbCarriesOnlyNewNamesAndKeepsNamedReferencesOnRepeat(string witness)
    {
        _greek.Slug = witness;
        foreach (var book in await _db.Books.ToListAsync())
        {
            book.CanonicalOrdinal = 40;
            book.Name = "Matthew";
            book.Slug = "mat";
        }
        foreach (var reference in await _db.VerseReferences.ToListAsync())
            reference.CanonicalBook = 40;
        if (witness != NestleTextSource.Slug)
        {
            foreach (var verse in new[] { 1, 3 })
            {
                Parse(Greek(verse, 1), """{"robinson":"T-GSM"}""");
                Parse(Greek(verse, 2), """{"robinson":"A-GSM"}""");
            }
            Parse(Greek(2, 1), """{"robinson":"A-NSM"}""");
            Parse(Greek(2, 2), """{"robinson":"A-NSM"}""");
        }
        await _db.SaveChangesAsync();
        var manual = await _db.WordEntities.AsNoTracking().SingleAsync();
        using var services = new ServiceCollection().AddSingleton(_db)
            .AddSingleton(Loader())
            .AddSingleton(new OwnReferenceLoader(_db, NullLogger<OwnReferenceLoader>.Instance))
            .BuildServiceProvider();
        var run = new ForgeRun(services, NullLogger.Instance, TestResources.Folder(""), _db.Database.GetConnectionString()!);
        var verb = ForgeVerbs.Find("fixed-titles")!;
        (await verb.Run(run, ["fixed-titles"])).Should().Be(0);
        var ours = await Ours();
        ours.Should().HaveCount(2);
        ours[Greek(1, 2).Id].EntityId.Should().Be(_satan.Id);
        ours.Should().ContainKey(_db.WordAt(_english, 4, 1, 2).Id);
        ours.Should().NotContainKey(Greek(2, 2).Id);
        (await _db.WordEntities.AsNoTracking().SingleAsync(a => a.Id == manual.Id)).EntityId.Should().Be(_judas.Id);
        var named = await _db.EntityVerses.AsNoTracking().Where(v => v.EntityId == _satan.Id).ToListAsync();
        named.Should().ContainSingle().Which.Names.Should().BeTrue();
        named[0].CanonicalBook.Should().Be(40);
        var before = await FixtureFingerprint();
        (await verb.Run(run, ["fixed-titles"])).Should().Be(0);
        (await FixtureFingerprint()).Should().Be(before);
    }

    private async Task<string> FixtureFingerprint() => string.Join("\n",
        JsonSerializer.Serialize(await _db.WordEntities.AsNoTracking().OrderBy(a => a.Id).ToListAsync()),
        JsonSerializer.Serialize(await _db.WordEntityClaims.AsNoTracking().OrderBy(a => a.Id).ToListAsync()),
        JsonSerializer.Serialize(await _db.EntityVerses.AsNoTracking().OrderBy(a => a.Id).ToListAsync()));

    private async Task<Dictionary<long, WordEntity>> Ours()
    {
        _db.ChangeTracker.Clear();
        return await _db.WordEntities.Where(a => a.Source == FixedTitleLoader.Source)
            .ToDictionaryAsync(a => a.WordId);
    }

    /// <summary>The devil with the article is Satan, rule-based, with its claim, and so is the English word rendering it.</summary>
    [Fact]
    public async Task TheDevilIsSatanAndTheRenderingSaysSo()
    {
        var outcome = await Loader().Load();

        var ours = await Ours();
        ours.Should().ContainKey(Greek(1, 2).Id);
        var row = ours[Greek(1, 2).Id];
        row.EntityId.Should().Be(_satan.Id);
        row.Method.Should().Be(LinkMethod.RuleBased);
        row.Confidence.Should().Be(0.99);
        (await _db.WordEntityClaims.CountAsync(c => c.WordEntityId == row.Id)).Should().Be(1);
        ours.Should().ContainKey(_db.WordAt(_english, 4, 1, 2).Id);
        outcome.Written.Should().Be(2);
        outcome.Missing.Should().Contain(["jesus", "ezekiel"]);
    }

    /// <summary>A devil without the article is left, and a word somebody already named keeps its name.</summary>
    [Fact]
    public async Task ADevilAndAWordAlreadyNamedAreLeft()
    {
        await Loader().Load();

        var ours = await Ours();
        ours.Should().NotContainKey(Greek(2, 2).Id);
        ours.Should().NotContainKey(Greek(3, 2).Id);
        (await _db.WordEntities.SingleAsync(a => a.WordId == Greek(3, 2).Id)).EntityId.Should().Be(_judas.Id);
    }

    /// <summary>A word that names a title only is nobody's yet: the bearer is written beside the title.</summary>
    [Fact]
    public async Task ATitleOnTheWordDoesNotKeepItsBearerOffIt()
    {
        var title = new Entity { Kind = EntityKind.Title, Slug = "adversary", Name = "Adversary", SourceId = "adversary", Source = "a test" };
        _db.Entities.Add(title);
        _db.WordEntities.Add(new WordEntity
        {
            WordId = Greek(1, 2).Id, Entity = title, Method = LinkMethod.RuleBased, Confidence = 0.99, Source = "a title",
        });
        await _db.SaveChangesAsync();

        await Loader().Load();

        (await Ours())[Greek(1, 2).Id].EntityId.Should().Be(_satan.Id);
        (await _db.WordEntities.CountAsync(a => a.WordId == Greek(1, 2).Id)).Should().Be(2);
        (await Ours()).Should().ContainKey(_db.WordAt(_english, 4, 1, 2).Id);
    }

    /// <summary>A later run names the word that has become nobody's since, and writes nothing twice.</summary>
    [Fact]
    public async Task ALaterRunNamesOnlyWhatIsNew()
    {
        await Loader().Load();
        await _db.WordEntities.Where(a => a.WordId == Greek(3, 2).Id).ExecuteDeleteAsync();

        var outcome = await Loader().Load();

        outcome.AlreadyLoaded.Should().BeFalse();
        var ours = await Ours();
        ours.Should().ContainKey(Greek(3, 2).Id);
        ours.Should().HaveCount(3);
        (await _db.WordEntityClaims.CountAsync(c => c.WordEntityId == ours[Greek(1, 2).Id].Id)).Should().Be(1);
    }

    /// <summary>A second run writes nothing.</summary>
    [Fact]
    public async Task ItRunsOnce()
    {
        await Loader().Load();

        (await Loader().Load()).AlreadyLoaded.Should().BeTrue();
    }
}
