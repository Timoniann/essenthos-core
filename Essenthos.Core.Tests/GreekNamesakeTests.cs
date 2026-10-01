using System.Text.Json;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Loading;
using Essenthos.Core.Loading.Encyclopedia;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Essenthos.Core.Tests;

/// <summary>
/// Which Herod a word of Matthew means, where the encyclopedia names only one of them in its verse.
///
/// Two Herods bear Ἡρῴδης, so the resolution by number refuses the name and every translation beside
/// it names nobody. Each case is one statement the verse list has to make, or must not be allowed to
/// make, before a word is taken to be one of them.
/// </summary>
[Collection(WitnessDatabaseCollection.Name)]
public sealed class GreekNamesakeTests : IDisposable
{
    private const int Matthew = 40;

    private const string Herod = "G2264";

    private const string Christ = "G5547";

    private const string BibleData = "BibleData by Brady Stephenson, github.com/BradyStephenson/bible-data, CC BY 4.0";

    private readonly AppDbContext _db;
    private readonly GreekNamesakeLoader _loader;
    private readonly Text _greek;
    private readonly Text _russian;
    private readonly Entity _great;
    private readonly Entity _antipas;

    public GreekNamesakeTests(WitnessDatabase database)
    {
        _db = database.NewContext();
        Clear();
        _loader = new GreekNamesakeLoader(_db, NullLogger<GreekNamesakeLoader>.Instance);

        _greek = Corpus.Add(_db, NestleTextSource.Slug, TextKind.CriticalEdition, "grc",
            (2, 1, ["Ἡρῴδου"]),
            (2, 2, ["Ἡρῴδης"]),
            (2, 3, ["Ἡρῴδης"]),
            (2, 4, ["Χριστός"]));
        _russian = Corpus.Add(_db, "RUSV", TextKind.Translation, "rus", (2, 1, ["Ирода"]));
        _db.SaveChanges();
        _db.In(_greek, Matthew);
        _db.In(_russian, Matthew);

        Word(1, Herod, "Ἡρῴδης");
        Word(2, Herod, "Ἡρῴδης");
        Word(3, Herod, "Ἡρῴδης");
        Word(4, Christ, "Χριστός");

        _db.StrongEntries.Add(new StrongEntry { StrongNumber = Herod, Lemma = "Ἡρῴδης" });
        _db.StrongEntries.Add(new StrongEntry { StrongNumber = Christ, Lemma = "Χριστός" });

        _great = Named("herod", Herod, "proper name");
        _antipas = Named("herod-2", Herod, "proper name");
        var jesus = Named("jesus", Christ, "title");
        Named("messiah", Christ, "proper name");

        // Matthew 2:1 names only the first Herod; 2:2 names both; 2:3 names the first only in a list
        // this corpus wrote itself.
        Attest(_great, 1, BibleData);
        Attest(_great, 2, BibleData);
        Attest(_antipas, 2, BibleData);
        Attest(_great, 3, "Essenthos, from the words this corpus annotates to the person or the place they name");
        Attest(jesus, 4, BibleData);

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
        _db.Database.ExecuteSqlRaw("DELETE FROM strong_entry");
    }

    private void Word(int verse, string number, string lemma)
    {
        var word = _db.WordAt(_greek, 2, verse, 1);
        word.StrongNumber = number;
        word.Lemma = lemma;
        word.Morphology = JsonDocument.Parse("""{"pos": "noun"}""");
        _db.SaveChanges();
    }

    private Entity Named(string slug, string number, string kind)
    {
        var entity = new Entity
        {
            Kind = EntityKind.Person, Slug = slug, Name = slug, SourceId = slug, Source = "a test",
            Names = [new EntityName { Label = slug, GreekStrongNumber = number, Kind = kind }],
        };
        _db.Entities.Add(entity);
        _db.SaveChanges();
        return entity;
    }

    private void Attest(Entity entity, int verse, string source) =>
        _db.EntityVerses.Add(new EntityVerse
        {
            Entity = entity, CanonicalBook = Matthew, CanonicalChapter = 2, CanonicalVerse = verse,
            Source = source,
        });

    private Word Greek(int verse) => _db.WordAt(_greek, 2, verse, 1);

    private async Task<Dictionary<long, WordEntity>> Named() =>
        await _db.WordEntities.AsNoTracking().Include(a => a.Entity).ToDictionaryAsync(a => a.WordId);

    [Fact]
    public async Task TheOneBearerTheVerseNamesIsTheWord()
    {
        var outcome = await _loader.Load();

        var named = await Named();
        named.Should().ContainKey(Greek(1).Id);
        named[Greek(1).Id].Entity!.Slug.Should().Be("herod");
        named[Greek(1).Id].Method.Should().Be(LinkMethod.Lexical,
            "a verse list is a statement about the verse, and reaching the word from it is an inference");
        named[Greek(1).Id].Confidence.Should().Be(0.96);
        outcome.Settled.Should().Be(1);
    }

    [Fact]
    public async Task AVerseNamingTwoOfTheBearersSaysNothingAboutTheWord()
    {
        var outcome = await _loader.Load();

        (await Named()).Should().NotContainKey(Greek(2).Id);
        outcome.Several.Should().Be(1);
    }

    /// <summary>
    /// A list this corpus read back off its own annotations would be the corpus agreeing with
    /// itself, so it names nobody here.
    /// </summary>
    [Fact]
    public async Task AVerseListThisCorpusWroteDoesNotCount()
    {
        var outcome = await _loader.Load();

        (await Named()).Should().NotContainKey(Greek(3).Id);
        outcome.Unlisted.Should().Be(1);
    }

    /// <summary>
    /// A title filed under a person says what he is. Jesus being named in the verse does not make
    /// Χριστός the word that names him, and without the title the name has one bearer and is not a
    /// question this pass asks.
    /// </summary>
    [Fact]
    public async Task ATitleIsNotABearer()
    {
        await _loader.Load();

        (await Named()).Should().NotContainKey(Greek(4).Id);
    }

    [Fact]
    public async Task TheAnswerTravelsToTheTranslationsTheLinksReach()
    {
        var rendering = _db.WordAt(_russian, 2, 1, 1);
        var link = new Link
        {
            FromTextId = _russian.Id, ToTextId = _greek.Id, Relation = LinkRelation.Renders,
            Method = LinkMethod.StrongNumber, Confidence = 0.9, Provenance = new() { Source = "a test" },
        };
        _db.Links.Add(link);
        _db.LinkWords.Add(new LinkWord { Link = link, Word = rendering, Side = LinkSide.From });
        _db.LinkWords.Add(new LinkWord { Link = link, Word = Greek(1), Side = LinkSide.To });
        await _db.SaveChangesAsync();

        await _loader.Load();

        var named = await Named();
        named.Should().ContainKey(rendering.Id);
        named[rendering.Id].Entity!.Slug.Should().Be("herod");
        named[rendering.Id].Confidence.Should().BeApproximately(0.96 * 0.9, 1e-9);
    }

    /// <summary>
    /// The pass is asked again on every load, because the encyclopedia can make a name several
    /// records' after it first ran. A word left alone then is told apart the next time.
    /// </summary>
    [Fact]
    public async Task ANameThatComesToBeSeveralRecordsIsToldApartOnTheNextLoad()
    {
        await _loader.Load();
        (await Named()).Should().NotContainKey(Greek(4).Id);

        Attest(await _db.Entities.SingleAsync(e => e.Slug == "messiah"), 4, BibleData);
        Named("christ-2", Christ, "proper name");
        await _db.SaveChangesAsync();

        var second = await _loader.Load();

        second.Settled.Should().Be(1);
        (await Named())[Greek(4).Id].Entity!.Slug.Should().Be("messiah");
    }

    [Fact]
    public async Task LoadingTwiceWritesNothingTheSecondTime()
    {
        var first = await _loader.Load();
        var count = await _db.WordEntities.CountAsync();
        var second = await _loader.Load();

        first.AlreadyLoaded.Should().BeFalse();
        second.AlreadyLoaded.Should().BeTrue();
        (await _db.WordEntities.CountAsync()).Should().Be(count);
    }
}
