using System.Text;
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
/// A Greek name no record is numbered by, where one record carries its spelling: what is written and
/// what is left.
/// </summary>
[Collection(WitnessDatabaseCollection.Name)]
public sealed class SpelledNameLoaderTests : IDisposable
{
    private const string Timothy = "Τιμόθεος";

    private const string Apollos = "Ἀπολλώς";

    private const string Paul = "Παῦλος";

    private readonly AppDbContext _db;
    private readonly Text _greek;

    /// <summary>
    /// Chapter 1: Timothy at verse 1, whose record has the spelling and no number; Apollos at verse
    /// 2, whose spelling two records carry; Paul at verse 3, whose number a record is held under;
    /// the adjective τίμιος at verse 4, lower case, under a number nobody holds.
    /// </summary>
    public SpelledNameLoaderTests(WitnessDatabase database)
    {
        _db = database.NewContext();
        Clear();

        _greek = Corpus.Add(_db, NestleTextSource.Slug, TextKind.CriticalEdition, "grc",
            (1, 1, ["Τιμόθεον"]), (1, 2, ["Ἀπολλῶ"]), (1, 3, ["Παῦλος"]), (1, 4, ["τίμιος"]));
        _db.SaveChanges();

        Lemma(1, Timothy, "G5095");
        Lemma(2, Apollos, "G625");
        Lemma(3, Paul, "G3972");
        Lemma(4, "τίμιος", "G5093");

        Add("timothy", "Timothy", Timothy.Normalize(NormalizationForm.FormD), null);
        Add("apollos", "Apollos", Apollos, null);
        Add("apollos-2", "Apollos of Corinth", Apollos, null);
        Add("paul", "Paul", Paul, "G3972");
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

    private void Lemma(int verse, string lemma, string number)
    {
        var word = Word(verse);
        word.Lemma = lemma;
        word.StrongNumber = number;
    }

    private void Add(string slug, string name, string greek, string? number) =>
        _db.Entities.Add(new Entity
        {
            Kind = EntityKind.Person, Slug = slug, Name = name, SourceId = slug, Source = BibleDataLoader.Source,
            Names = [new EntityName { Label = name, Greek = greek, GreekStrongNumber = number, Kind = "proper name" }],
        });

    private Word Word(int verse) => _db.WordAt(_greek, 1, verse, 1);

    private SpelledNameLoader Loader() => new(_db, NullLogger<SpelledNameLoader>.Instance);

    private async Task<Dictionary<long, string>> Ours() =>
        await _db.WordEntities.Where(a => a.Source == SpelledNameLoader.Source)
            .ToDictionaryAsync(a => a.WordId, a => a.Entity!.Slug);

    /// <summary>The dataset writes the accent as a combining mark and the text precomposed; they are one spelling.</summary>
    [Fact]
    public async Task TheNameIsTheOneRecordThatSpellsIt()
    {
        var outcome = await Loader().Load();

        (await Ours()).Should().BeEquivalentTo(new Dictionary<long, string> { [Word(1).Id] = "timothy" });
        var row = await _db.WordEntities.SingleAsync(a => a.WordId == Word(1).Id);
        row.Method.Should().Be(LinkMethod.RuleBased);
        row.Confidence.Should().Be(0.99);
        outcome.Named.Should().Equal(("timothy", 1, 0));
    }

    /// <summary>A second run finds its own rows and writes nothing.</summary>
    [Fact]
    public async Task ItRunsOnce()
    {
        await Loader().Load();

        (await Loader().Load()).AlreadyLoaded.Should().BeTrue();
    }

    /// <summary>A word something already names is never given a second answer.</summary>
    [Fact]
    public async Task AWordAlreadyNamedIsLeft()
    {
        var paul = await _db.Entities.SingleAsync(e => e.Slug == "paul");
        _db.WordEntities.Add(new WordEntity
        {
            WordId = Word(1).Id, EntityId = paul.Id, Method = LinkMethod.Manual, Source = "the owner",
        });
        await _db.SaveChangesAsync();

        await Loader().Load();

        (await Ours()).Should().BeEmpty();
    }
}
