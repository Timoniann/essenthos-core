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
/// 1 Kings 7:14, <em>of the tribe of Naphtali</em>, where the name after the word for a tribe named
/// nobody because the man, the tribe and the territory all bear its number.
///
/// Each case here is one thing that has to be true of the phrase and of the encyclopedia before the
/// word is taken to be the tribe.
/// </summary>
[Collection(WitnessDatabaseCollection.Name)]
public sealed class TribeNameTests : IDisposable
{
    private const string Tribe = "H4294";

    private const string Sceptre = "H7626";

    private const string Naphtali = "H5321";

    private const string Son = "H1121";

    private readonly AppDbContext _db;
    private readonly TribeNameLoader _loader;
    private readonly Text _hebrew;
    private readonly Text _russian;

    public TribeNameTests(WitnessDatabase database)
    {
        _db = database.NewContext();
        Clear();
        _loader = new TribeNameLoader(_db, NullLogger<TribeNameLoader>.Instance);

        _hebrew = Corpus.Add(_db, EntityCandidates.Witness, TextKind.CriticalEdition, "hbo",
            (7, 14, ["מטה", "נפתלי", "בני", "נפתלי"]),
            (7, 15, ["שבט", "נפתלי"]));
        _russian = Corpus.Add(_db, "RUSV", TextKind.Translation, "rus",
            (7, 14, ["колена", "Неффалимова"]));
        _db.SaveChanges();

        Number(7, 14, 1, Tribe);
        Number(7, 14, 2, Naphtali);
        Number(7, 14, 3, Son);
        Number(7, 14, 4, Naphtali);
        Number(7, 15, 1, Sceptre);
        Number(7, 15, 2, Naphtali);
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

    private void Number(int chapter, int verse, int position, string number) =>
        _db.WordAt(_hebrew, chapter, verse, position).StrongNumber = number;

    private Entity Record(string slug, EntityKind kind, string label)
    {
        var entity = new Entity
        {
            Kind = kind, Slug = slug, Name = slug, SourceId = slug, Source = "a test",
            Names = [new EntityName { Label = label, HebrewStrongNumber = Naphtali, Kind = label }],
        };
        _db.Entities.Add(entity);
        _db.SaveChanges();
        return entity;
    }

    private Word Hebrew(int chapter, int verse, int position) =>
        _db.WordAt(_hebrew, chapter, verse, position);

    private async Task<Dictionary<long, WordEntity>> Named() =>
        await _db.WordEntities.AsNoTracking().Include(a => a.Entity).ToDictionaryAsync(a => a.WordId);

    [Fact]
    public async Task TheNameAfterTheWordForATribeIsTheTribe()
    {
        Record("naphtali", EntityKind.Person, "proper name");
        Record("naphtalites", EntityKind.People, "collective");

        var outcome = await _loader.Load();

        var named = await Named();
        named[Hebrew(7, 14, 2).Id].Entity!.Slug.Should().Be("naphtalites");
        named[Hebrew(7, 14, 2).Id].Method.Should().Be(LinkMethod.Lexical,
            "the phrase is what said so, not a source naming the tribe");
        named[Hebrew(7, 14, 2).Id].Confidence.Should().Be(0.9);
        outcome.Settled.Should().Be(2);
    }

    /// <summary>The Hebrew says <em>tribe</em> with two words, and both of them are the construct.</summary>
    [Fact]
    public async Task TheSecondWordForATribeCountsToo()
    {
        Record("naphtali", EntityKind.Person, "proper name");
        Record("naphtalites", EntityKind.People, "collective");

        await _loader.Load();

        (await Named())[Hebrew(7, 15, 2).Id].Entity!.Slug.Should().Be("naphtalites");
    }

    /// <summary>
    /// The same name two words later is the man or the tribe by whatever else decides it, and the
    /// construct says nothing about it.
    /// </summary>
    [Fact]
    public async Task TheSameNameElsewhereInTheVerseIsLeftAlone()
    {
        Record("naphtali", EntityKind.Person, "proper name");
        Record("naphtalites", EntityKind.People, "collective");

        await _loader.Load();

        (await Named()).Should().NotContainKey(Hebrew(7, 14, 4).Id);
    }

    /// <summary>
    /// A reading of the verse stands above a rule about the shape of a phrase, so a word something
    /// has already named keeps its answer.
    /// </summary>
    [Fact]
    public async Task AWordAlreadyNamedIsNotContested()
    {
        var man = Record("naphtali", EntityKind.Person, "proper name");
        Record("naphtalites", EntityKind.People, "collective");
        _db.WordEntities.Add(new WordEntity
        {
            Word = Hebrew(7, 14, 2), Entity = man, Method = LinkMethod.ModelReading, Confidence = 0.99,
            Source = "a reading of the verse",
        });
        await _db.SaveChangesAsync();

        var outcome = await _loader.Load();

        (await Named())[Hebrew(7, 14, 2).Id].Entity!.Slug.Should().Be("naphtali");
        outcome.Spoken.Should().Be(1);
    }

    /// <summary>Two peoples of one name is a choice, and this rule makes none.</summary>
    [Fact]
    public async Task ANameTwoPeoplesBearSaysNothing()
    {
        Record("naphtalites", EntityKind.People, "collective");
        Record("naphtalites-2", EntityKind.People, "collective");

        var outcome = await _loader.Load();

        (await Named()).Should().BeEmpty();
        outcome.Settled.Should().Be(0);
    }

    [Fact]
    public async Task ANameNoPeopleBearsIsCounted()
    {
        Record("naphtali", EntityKind.Person, "proper name");

        var outcome = await _loader.Load();

        outcome.Unheld.Should().Be(2);
        outcome.Settled.Should().Be(0);
    }

    [Fact]
    public async Task TheTribeTravelsToTheTranslationTheLinksReach()
    {
        Record("naphtalites", EntityKind.People, "collective");
        var rendering = _db.WordAt(_russian, 7, 14, 2);
        var link = new Link
        {
            FromTextId = _russian.Id, ToTextId = _hebrew.Id, Relation = LinkRelation.Renders,
            Method = LinkMethod.StrongNumber, Confidence = 0.9, Provenance = new() { Source = "a test" },
        };
        _db.Links.Add(link);
        _db.LinkWords.Add(new LinkWord { Link = link, Word = rendering, Side = LinkSide.From });
        _db.LinkWords.Add(new LinkWord { Link = link, Word = Hebrew(7, 14, 2), Side = LinkSide.To });
        await _db.SaveChangesAsync();

        await _loader.Load();

        (await Named())[rendering.Id].Entity!.Slug.Should().Be("naphtalites");
    }

    [Fact]
    public async Task ASecondRunDoesNothing()
    {
        Record("naphtalites", EntityKind.People, "collective");

        await _loader.Load();
        var again = await _loader.Load();

        again.AlreadyLoaded.Should().BeTrue();
    }
}
