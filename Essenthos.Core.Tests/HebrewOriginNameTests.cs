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
/// Matthew 1, where the Old Testament names Matthew writes in Greek named nobody because the
/// encyclopedia records those people under their Hebrew numbers. Each case is one thing Strong's
/// derivation and the verse list have to say before a Greek name is taken to be a Hebrew record.
/// </summary>
[Collection(WitnessDatabaseCollection.Name)]
public sealed class HebrewOriginNameTests : IDisposable
{
    private const int Matthew = 40;

    private const string Esrom = "G2074";

    private const string Phares = "G5329";

    private const string Israel = "G2474";

    private const string Mainan = "G3104";

    private const string BibleData =
        "BibleData by Brady Stephenson, github.com/BradyStephenson/bible-data, CC BY 4.0";

    private readonly AppDbContext _db;
    private readonly HebrewOriginNameLoader _loader;
    private readonly Text _greek;

    public HebrewOriginNameTests(WitnessDatabase database)
    {
        _db = database.NewContext();
        Clear();
        _loader = new HebrewOriginNameLoader(_db, NullLogger<HebrewOriginNameLoader>.Instance);

        _greek = Corpus.Add(_db, NestleTextSource.Slug, TextKind.CriticalEdition, "grc",
            (1, 3, ["Ἐσρώμ", "Φαρὲς"]),
            (1, 4, ["Ἰσραήλ"]),
            (1, 5, ["Μεννὰ"]));
        _db.SaveChanges();
        _db.In(_greek, Matthew);

        Word(3, 1, Esrom);
        Word(3, 2, Phares);
        Word(4, 1, Israel);
        Word(5, 1, Mainan);

        Lexicon(Esrom, "Ἐσρώμ", "of Hebrew origin (H2696);");
        Lexicon(Phares, "Φάρες", "of Hebrew origin (H6557);");
        Lexicon(Israel, "Ἰσραήλ", "of Hebrew origin (H3478);");
        Lexicon(Mainan, "Μαϊνάν", "probably of Hebrew origin;");

        var hezron = Add("hezron", EntityKind.Person, hebrew: "H2696");
        Add("hezron-2", EntityKind.Person, hebrew: "H2696");
        var perez = Add("perez", EntityKind.Person, hebrew: "H6557");
        var peresh = Add("peresh", EntityKind.Person, greek: Phares);
        var jacob = Add("jacob", EntityKind.Person, hebrew: "H3478");
        var israelites = Add("israelites", EntityKind.People, hebrew: "H3478");
        israelites.OriginEntityId = jacob.Id;
        var menna = Add("menna", EntityKind.Person, hebrew: "H9999");

        Attest(hezron, 3);
        Attest(perez, 3);
        Attest(jacob, 4);
        Attest(menna, 5);
        _ = peresh;
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

    private void Word(int verse, int position, string number)
    {
        var word = Greek(verse, position);
        word.StrongNumber = number;
        word.Morphology = JsonDocument.Parse("""{"pos": "noun"}""");
        _db.SaveChanges();
    }

    private void Lexicon(string number, string lemma, string derivation) =>
        _db.StrongEntries.Add(new StrongEntry { StrongNumber = number, Lemma = lemma, Derivation = derivation });

    private Entity Add(string slug, EntityKind kind, string? hebrew = null, string? greek = null)
    {
        var entity = new Entity
        {
            Kind = kind, Slug = slug, Name = slug, SourceId = slug, Source = "a test",
            Names =
            [
                new EntityName
                {
                    Label = slug, HebrewStrongNumber = hebrew, GreekStrongNumber = greek, Kind = "proper name",
                },
            ],
        };
        _db.Entities.Add(entity);
        _db.SaveChanges();
        return entity;
    }

    private void Attest(Entity entity, int verse) =>
        _db.EntityVerses.Add(new EntityVerse
        {
            Entity = entity, CanonicalBook = Matthew, CanonicalChapter = 1, CanonicalVerse = verse,
            Source = BibleData,
        });

    private Word Greek(int verse, int position = 1) => _db.WordAt(_greek, 1, verse, position);

    private async Task<Dictionary<long, string>> Load()
    {
        await _loader.Load();
        return await _db.WordEntities.ToDictionaryAsync(a => a.WordId, a => a.Entity!.Slug);
    }

    [Fact]
    public async Task AGreekNameIsTheHebrewRecordTheVerseNames()
    {
        var named = await Load();
        named.Should().ContainKey(Greek(3).Id).WhoseValue.Should().Be("hezron");
    }

    /// <summary>
    /// The encyclopedia files Φάρες under Peresh the Manassite. Perez, whom Strong derives it from,
    /// is the one the verse names, and a wrong Greek number does not outvote the verse.
    /// </summary>
    [Fact]
    public async Task ARecordBearingTheWrongGreekNumberDoesNotWinOverTheVerse()
    {
        var named = await Load();
        named.Should().ContainKey(Greek(3, 2).Id).WhoseValue.Should().Be("perez");
    }

    [Fact]
    public async Task AnEponymANationIsNamedAfterIsNotTakenOnTheListsWord()
    {
        var outcome = await _loader.Load();

        var israel = Greek(4).Id;
        (await _db.WordEntities.AnyAsync(a => a.WordId == israel)).Should().BeFalse();
        outcome.Eponyms.Should().Be(1);
    }

    [Fact]
    public async Task AHedgedDerivationIsNotJoinedOn()
    {
        var named = await Load();
        named.Should().NotContainKey(Greek(5).Id);
    }

    [Fact]
    public async Task LoadingTwiceWritesNothingTheSecondTime()
    {
        var first = await _loader.Load();
        var count = await _db.WordEntities.CountAsync();
        var second = await _loader.Load();

        first.Settled.Should().Be(2);
        second.AlreadyLoaded.Should().BeTrue();
        (await _db.WordEntities.CountAsync()).Should().Be(count);
    }
}
