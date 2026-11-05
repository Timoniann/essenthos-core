using System.Text.Json;
using Essenthos.Core.Corpus;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Loading.Encyclopedia;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Essenthos.Core.Tests.Names;

/// <summary>
/// A place carried onto words that are not its name. Ohienko's 1 Chronicles 13:11 ends
/// <em>Перец-Узза, і так воно зветься</em>; the aligner gave the Hebrew name to <em>так</em> at 0.25 and
/// to <em>зветься</em> at 0.44, and the name itself to the verb before it, so the place stood on
/// <em>so</em> and <em>is called</em> two words after the word the text's own verses name it by.
/// </summary>
[Collection(WitnessDatabaseCollection.Name)]
public sealed class StrayNameTests : IDisposable
{
    private const string Hebrew = "the names the Hebrew marks, a test";

    private readonly AppDbContext _db;
    private readonly StrayNames _strays;
    private readonly Text _hebrew;
    private readonly Text _ukrainian;
    private readonly Entity _perezUzza;
    private readonly Entity _david;

    public StrayNameTests(WitnessDatabase database)
    {
        _db = database.NewContext();
        Clear();
        _strays = new StrayNames(_db, NullLogger<StrayNames>.Instance);

        _hebrew = Corpus.Add(_db, EntityCandidates.Witness, TextKind.CriticalEdition, "hbo",
            (13, 11, ["פָרַץ", "וַיִּקְרָא", "פֶּרֶץ עֻזָּא", "עַד"]),
            (13, 12, ["וַיִּירָא", "דָוִיד", "אֱלֹהִים"]));
        _ukrainian = Corpus.Add(_db, "UBIO", TextKind.Translation, "ukr",
            (13, 11, ["і", "назвав", "Перец-Узза", "і", "так", "воно", "зветься"]),
            (13, 12, ["Давид", "боявся", "Бога", "тому", "він", "молився"]));
        _db.SaveChanges();
        _db.Database.ExecuteSqlRaw("UPDATE word SET normalised_text = lower(text)");
        _db.Database.ExecuteSqlRaw(
            "UPDATE word SET morphology = jsonb_build_object('pos', 'nmpr') WHERE id IN ({0}, {1})",
            HebrewWord(13, 11, 3).Id, HebrewWord(13, 12, 2).Id);

        _perezUzza = Record(EntityKind.Place, "perezuzzah", "Perez-uzza");
        _david = Record(EntityKind.Person, "david", "David");
        Name(HebrewWord(13, 11, 3), _perezUzza, Hebrew, 0.8);
        Name(HebrewWord(13, 12, 2), _david, Hebrew, 0.99);
        Name(UkrainianWord(13, 11, 3), _perezUzza, Annotations.Consensus, 0.99);
        Name(UkrainianWord(13, 12, 1), _david, Annotations.Consensus, 0.99);
        _db.SaveChanges();

        Link(HebrewWord(13, 11, 3), UkrainianWord(13, 11, 5), 0.25);
        Link(HebrewWord(13, 11, 3), UkrainianWord(13, 11, 7), 0.44);
        Link(HebrewWord(13, 11, 1), UkrainianWord(13, 11, 3), 0.97);
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

    [Fact]
    public async Task WhatACarryWroteBeforeIsTakenBackOnceAndOnlyOnce()
    {
        Carried(UkrainianWord(13, 11, 5), _perezUzza, HebrewWord(13, 11, 3), 0.2);
        Carried(UkrainianWord(13, 11, 7), _perezUzza, HebrewWord(13, 11, 3), 0.352);
        await _db.SaveChangesAsync();

        var dry = await _strays.Withdraw(write: false);
        dry.Withdrawn.Should().Be(2);
        NamedOn(UkrainianWord(13, 11, 5)).Should().Equal("perezuzzah");

        var first = await _strays.Withdraw();
        var second = await _strays.Withdraw();

        first.Withdrawn.Should().Be(2);
        first.ByText.Should().Equal(("UBIO", 2));
        second.Withdrawn.Should().Be(0);
        NamedOn(UkrainianWord(13, 11, 5)).Should().BeEmpty();
        NamedOn(UkrainianWord(13, 11, 7)).Should().BeEmpty();
    }

    /// <summary>
    /// The name resolution carries the place over the same faint links each time it runs. Had it written
    /// what this pass takes back, the two would undo each other at every load and the load never settle.
    /// </summary>
    [Fact]
    public async Task TheResolutionDoesNotWriteAgainWhatThisPassTakesBack()
    {
        var name = HebrewWord(13, 11, 3);
        name.StrongNumber = "H6560";
        name.Morphology = JsonDocument.Parse("""{"pos": "nmpr", "nameType": "topo"}""");
        _db.EntityNames.Add(new EntityName { EntityId = _perezUzza.Id, Label = "Perez-uzza", HebrewStrongNumber = "H6560", Kind = "name" });
        await _db.SaveChangesAsync();
        var resolution = new EntityAnnotationLoader(
            _db, NullLogger<EntityAnnotationLoader>.Instance, new PronounReferents(_db, NullLogger<PronounReferents>.Instance), _strays);

        await resolution.Load();
        var taken = await _strays.Withdraw();
        await resolution.Load();

        NamedOn(UkrainianWord(13, 11, 5)).Should().BeEmpty("так is no name, and Перец-Узза stands two words before it");
        NamedOn(UkrainianWord(13, 11, 7)).Should().BeEmpty("зветься is no name either");
        taken.Withdrawn.Should().Be(0, "the resolution never wrote what the pass would take back");
        NamedOn(UkrainianWord(13, 11, 3)).Should().Equal("perezuzzah");
    }

    private Entity Record(EntityKind kind, string slug, string name)
    {
        var record = new Entity { Kind = kind, Slug = slug, Name = name, SourceId = slug, Source = "a test" };
        _db.Entities.Add(record);
        _db.SaveChanges();
        return record;
    }

    private Word HebrewWord(int chapter, int verse, int position) => _db.WordAt(_hebrew, chapter, verse, position);

    private Word UkrainianWord(int chapter, int verse, int position) => _db.WordAt(_ukrainian, chapter, verse, position);

    private void Name(Word word, Entity entity, string source, double confidence) =>
        _db.WordEntities.Add(new WordEntity
        {
            WordId = word.Id, EntityId = entity.Id, Method = LinkMethod.StrongNumber, Confidence = confidence,
            Source = source, Note = "a test",
            Claims = [new WordEntityClaim { Method = LinkMethod.StrongNumber, Confidence = confidence, Source = source, Note = "a test" }],
        });

    private void Carried(Word word, Entity entity, Word through, double confidence) =>
        _db.WordEntities.Add(new WordEntity
        {
            WordId = word.Id, EntityId = entity.Id, Method = LinkMethod.StrongNumber, Confidence = confidence,
            Source = Hebrew, Note = $"through {EntityCandidates.Witness} word {through.Id}, linked by aligner",
        });

    private void Link(Word hebrew, Word ukrainian, double confidence)
    {
        var link = new Link
        {
            FromTextId = ukrainian.TextId, ToTextId = hebrew.TextId, Relation = LinkRelation.Renders,
            Method = LinkMethod.Aligner, Confidence = confidence, Provenance = new() { Source = "a test" },
        };
        _db.Links.Add(link);
        _db.LinkWords.Add(new LinkWord { Link = link, Word = ukrainian, Side = LinkSide.From });
        _db.LinkWords.Add(new LinkWord { Link = link, Word = hebrew, Side = LinkSide.To });
        _db.SaveChanges();
    }

    private List<string> NamedOn(Word word) =>
        [.. _db.WordEntities.AsNoTracking().Where(a => a.WordId == word.Id).Select(a => a.Entity!.Slug).OrderBy(s => s)];
}
