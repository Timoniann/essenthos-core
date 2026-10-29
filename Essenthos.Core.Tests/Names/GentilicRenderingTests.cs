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
/// A gentilic the links did not carry. Ohienko's 1 Chronicles 12:4 writes <em>ґів'онянин Їшмая</em> where
/// the Hebrew writes the man before the Gibeonite, and the aligner paired the gentilic with the man; the
/// word is spelled as the text spells the Gibeonites in 2 Samuel 21 and Gibeon in 1 Chronicles 9.
/// </summary>
[Collection(WitnessDatabaseCollection.Name)]
public sealed class GentilicRenderingTests : IDisposable
{
    private const string Hebrew = "the names the Hebrew marks, a test";

    private const string Ukrainian = "the names the Ukrainian carries, a test";

    private readonly AppDbContext _db;
    private readonly GentilicRenderings _pass;
    private readonly Text _hebrew;
    private readonly Text _ukrainian;
    private readonly Entity _gibeonites;
    private readonly Entity _gibeon;
    private readonly Entity _ishmaiah;
    private readonly Entity _gibeathites;

    public GentilicRenderingTests(WitnessDatabase database)
    {
        _db = database.NewContext();
        Clear();
        _pass = new GentilicRenderings(_db, NullLogger<GentilicRenderings>.Instance);

        _hebrew = Corpus.Add(_db, EntityCandidates.Witness, TextKind.CriticalEdition, "hbo",
            (12, 4, ["יִשְׁמַעְיָה", "הַגִּבְעֹנִי", "גִּבֹּור"]),
            (21, 2, ["הַגִּבְעֹנִים", "לֹא"]),
            (9, 35, ["בְּגִבְעֹון", "יָשְׁבוּ"]),
            (11, 1, ["הַגִּבְעֹנִי", "וְהַגִּבְעֹנִי"]),
            (12, 3, ["שְׁמָעָה", "הַגִּבְעָתִי"]));
        _ukrainian = Corpus.Add(_db, "UBIO", TextKind.Translation, "ukr",
            (12, 4, ["і", "ґів'онянин", "Їшмая", "лицар"]),
            (21, 2, ["а", "ґів'онітяни", "не"]),
            (9, 35, ["і", "в", "Ґів'оні", "сиділи"]),
            (11, 1, ["ґів'онянин", "ґів'онеянин"]),
            (12, 3, ["сини", "ґів'янина", "Шемаї"]));
        _db.SaveChanges();
        _db.Database.ExecuteSqlRaw("UPDATE word SET normalised_text = lower(text)");

        _gibeon = Record(EntityKind.Place, "gibeon", "Gibeon", null);
        _gibeonites = Record(EntityKind.People, "gibeonites", "Gibeonites", _gibeon);
        _ishmaiah = Record(EntityKind.Person, "ishmaiah-2", "Ishmaiah", null);
        _gibeathites = Record(EntityKind.People, "gibeathites", "Gibeathites", null);
        var shemaah = Record(EntityKind.Person, "shemaah", "Shemaah", null);
        _db.StrongGentilics.Add(new StrongGentilic
        {
            StrongNumber = "H1393", OriginNumber = "H1391", Kind = "patrial", OriginEntityId = _gibeon.Id,
            PeopleEntityId = _gibeonites.Id, Statement = "patrial from H1391", Source = "a test",
        });
        foreach (var (chapter, verse, position) in new[] { (12, 4, 2), (21, 2, 1), (11, 1, 1), (11, 1, 2) })
        {
            HebrewWord(chapter, verse, position).StrongNumber = "H1393";
        }

        HebrewWord(12, 4, 1).StrongNumber = "H3460";
        HebrewWord(9, 35, 1).StrongNumber = "H1391";

        Name(HebrewWord(12, 4, 1), _ishmaiah, Hebrew);
        Name(HebrewWord(12, 4, 2), _gibeonites, Hebrew);
        Name(HebrewWord(21, 2, 1), _gibeonites, Hebrew);
        Name(HebrewWord(9, 35, 1), _gibeon, Hebrew);
        Name(HebrewWord(11, 1, 1), _gibeonites, Hebrew);
        Name(HebrewWord(11, 1, 2), _gibeonites, Hebrew);
        Name(UkrainianWord(12, 4, 3), _ishmaiah, Ukrainian);
        Name(UkrainianWord(21, 2, 2), _gibeonites, Ukrainian);
        Name(UkrainianWord(9, 35, 3), _gibeon, Ukrainian);
        HebrewWord(12, 3, 2).StrongNumber = "H1395";
        _db.StrongGentilics.Add(new StrongGentilic
        {
            StrongNumber = "H1395", OriginNumber = "H1390", Kind = "patrial", PeopleEntityId = _gibeathites.Id,
            Statement = "patrial from H1390", Source = "a test",
        });
        Name(HebrewWord(12, 3, 1), shemaah, Hebrew);
        Name(HebrewWord(12, 3, 2), _gibeathites, Hebrew);
        Name(UkrainianWord(12, 3, 3), shemaah, Ukrainian);
        _db.SaveChanges();
    }

    public void Dispose()
    {
        Clear();
        _db.Dispose();
    }

    private void Clear()
    {
        _db.Database.ExecuteSqlRaw("DELETE FROM strong_gentilic");
        _db.Database.ExecuteSqlRaw("DELETE FROM text");
        _db.Database.ExecuteSqlRaw("DELETE FROM entity");
    }

    [Fact]
    public async Task TheWordSpelledAsTheTextSpellsThePeopleIsGivenIt()
    {
        var outcome = await _pass.Load();

        outcome.Written.Should().Be(1);
        NamedOn(UkrainianWord(12, 4, 2)).Should().Equal("gibeonites");
        NamedOn(UkrainianWord(12, 4, 3)).Should().Equal(["ishmaiah-2"], "the man keeps his name");
        NamedOn(UkrainianWord(12, 4, 4)).Should().BeEmpty("лицар is spelled like nothing the text names the Gibeonites or Gibeon with");
        var row = _db.WordEntities.AsNoTracking().Single(a => a.WordId == UkrainianWord(12, 4, 2).Id);
        (row.Method, row.Confidence, row.Source).Should().Be((LinkMethod.Lexical, GentilicRenderings.Confidence, GentilicRenderings.Source));
        _db.WordEntityClaims.AsNoTracking().Count(c => c.WordEntityId == row.Id).Should().Be(1);
    }

    [Fact]
    public async Task ASecondRunWritesAndTakesBackNothing()
    {
        await _pass.Load();
        var second = await _pass.Load();

        (second.Written, second.Withdrawn).Should().Be((0, 0));
        NamedOn(UkrainianWord(12, 4, 2)).Should().Equal("gibeonites");
    }

    [Fact]
    public async Task TwoWordsThatCouldBeItAreLeftAlone()
    {
        await _pass.Load();

        NamedOn(UkrainianWord(11, 1, 1)).Should().BeEmpty("two words of the verse are spelled like the Gibeonites and nothing says which");
        NamedOn(UkrainianWord(11, 1, 2)).Should().BeEmpty();
    }

    [Fact]
    public async Task AVerseWhoseWordsAlreadyNameThePeopleIsLeftAlone()
    {
        Name(UkrainianWord(12, 4, 4), _gibeonites, Ukrainian);
        await _db.SaveChangesAsync();

        await _pass.Load();

        NamedOn(UkrainianWord(12, 4, 2)).Should().BeEmpty();
    }

    [Fact]
    public async Task WhatItGaveIsTakenBackOnceALinkCarriesThePeopleToAnotherWord()
    {
        await _pass.Load();
        Name(UkrainianWord(12, 4, 4), _gibeonites, Ukrainian);
        await _db.SaveChangesAsync();

        var outcome = await _pass.Load();

        outcome.Withdrawn.Should().Be(1);
        NamedOn(UkrainianWord(12, 4, 2)).Should().BeEmpty();
    }

    [Fact]
    public async Task TheCarryNeitherTakesItBackNorCarriesItOn()
    {
        var english = Corpus.Add(_db, "KJV", TextKind.Translation, "eng", (12, 4, ["Ismaiah", "the", "Gibeonite"]));
        await _db.SaveChangesAsync();
        var link = new Link
        {
            FromTextId = _ukrainian.Id, ToTextId = english.Id, Relation = LinkRelation.Renders,
            Method = LinkMethod.Aligner, Confidence = 0.9, Provenance = new() { Source = "a test" },
        };
        _db.Links.Add(link);
        _db.LinkWords.Add(new LinkWord { Link = link, Word = UkrainianWord(12, 4, 2), Side = LinkSide.From });
        _db.LinkWords.Add(new LinkWord { Link = link, Word = _db.WordAt(english, 12, 4, 1), Side = LinkSide.To });
        await _db.SaveChangesAsync();
        await _pass.Load();

        await new AnnotationCarrier(
            _db,
            new CrossedNameLoader(_db, NullLogger<CrossedNameLoader>.Instance),
            new ForeignNames(_db, NullLogger<ForeignNames>.Instance),
            new EqualTwinNames(_db, NullLogger<EqualTwinNames>.Instance),
            new PronounReferents(_db, NullLogger<PronounReferents>.Instance),
            NullLogger<AnnotationCarrier>.Instance).Carry();

        NamedOn(UkrainianWord(12, 4, 2)).Should().Equal("gibeonites");
        NamedOn(_db.WordAt(english, 12, 4, 1)).Should().BeEmpty("the gentilic stands on a word no link reached it from, and goes no further");
    }

    /// <summary>
    /// The Gibeathite of 1 Chronicles 12:3, the only one Scripture names, and Gibeah not a record the
    /// gentilic is tied to: no other verse spells them, and the record's own name in the text's
    /// language is all there is to read the word by.
    /// </summary>
    [Fact]
    public async Task ThePeoplesOwnNameInTheTextsLanguageSpellsIt()
    {
        FormOf(_gibeathites, "ukr", "ґів'яни");
        await _db.SaveChangesAsync();

        var outcome = await _pass.Load();
        var second = await _pass.Load();

        NamedOn(UkrainianWord(12, 3, 2)).Should().Equal("gibeathites");
        NamedOn(UkrainianWord(12, 3, 1)).Should().BeEmpty("сини is spelled like nothing the Gibeathites are called");
        outcome.Written.Should().Be(2);
        (second.Written, second.Withdrawn).Should().Be((0, 0));
    }

    [Fact]
    public async Task ANameInAnotherLanguageSpellsNothing()
    {
        FormOf(_gibeathites, "rus", "ґів'яни");
        await _db.SaveChangesAsync();

        await _pass.Load();

        NamedOn(UkrainianWord(12, 3, 2)).Should().BeEmpty("the form is not the text's language");
    }

    private void FormOf(Entity entity, string language, string form) =>
        _db.EntityNameForms.Add(new EntityNameForm
        {
            EntityId = entity.Id, Language = language, GrammaticalCase = GrammaticalCases.Nominative, Form = form,
            Method = LinkMethod.Manual, Source = "a test",
        });

    private Entity Record(EntityKind kind, string slug, string name, Entity? origin)
    {
        var record = new Entity
        {
            Kind = kind, Slug = slug, Name = name, SourceId = slug, Source = "a test", OriginEntityId = origin?.Id,
        };
        _db.Entities.Add(record);
        _db.SaveChanges();
        return record;
    }

    private Word HebrewWord(int chapter, int verse, int position) => _db.WordAt(_hebrew, chapter, verse, position);

    private Word UkrainianWord(int chapter, int verse, int position) => _db.WordAt(_ukrainian, chapter, verse, position);

    private void Name(Word word, Entity entity, string source) =>
        _db.WordEntities.Add(new WordEntity
        {
            WordId = word.Id, EntityId = entity.Id, Method = LinkMethod.StrongNumber, Confidence = 0.99,
            Source = source, Note = "a test",
            Claims = [new WordEntityClaim { Method = LinkMethod.StrongNumber, Confidence = 0.99, Source = source, Note = "a test" }],
        });

    private List<string> NamedOn(Word word) =>
        [.. _db.WordEntities.AsNoTracking().Where(a => a.WordId == word.Id).Select(a => a.Entity!.Slug).OrderBy(s => s)];
}
