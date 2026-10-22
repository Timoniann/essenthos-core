using Essenthos.Core.Corpus;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Endpoints;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Essenthos.Core.Tests.Reader;

/// <summary>
/// A name is found by a word of it, not by letters inside another word; in Latin letters for a
/// Cyrillic name; by the familiar form a reader knows; and, where nothing is called that, by a near
/// spelling. Same-named rows are told apart by their first verse.
/// </summary>
[Collection(WitnessDatabaseCollection.Name)]
public sealed class NameSearchTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly Entity _reuben;

    public NameSearchTests(WitnessDatabase database)
    {
        _db = database.NewContext();
        Clear();

        var ohienko = Corpus.Add(_db, "UBIO", TextKind.Translation, "ukr", (1, 1, ["слово"]));
        var louis = Corpus.Add(_db, "LSG1910", TextKind.Translation, "fra", (1, 1, ["mot"]));

        _reuben = Record("reuben", "Reuben");
        var cherubim = Record("cherubim", "Cherubim");
        var hannah = Record("hannah", "Hannah");
        var john = Record("john", "John");
        var rameses = Record("rameses", "Rameses", EntityKind.Place);
        var bethShemesh = Record("bethshemesh", "Beth-shemesh", EntityKind.Place);
        _db.SaveChanges();

        Printed(ohienko, _reuben, "Рувим");
        Printed(ohienko, cherubim, "Херувими");
        Printed(ohienko, hannah, "Анна");
        Printed(ohienko, john, "Іоанна");
        Printed(ohienko, rameses, "Рамесес");
        Printed(louis, rameses, "Ramsès");
        Printed(ohienko, bethShemesh, "Бет-Шемеш");

        foreach (var (chapter, verse) in new[] { (49, 3), (29, 32), (35, 23) })
        {
            _db.EntityVerses.Add(new EntityVerse
            {
                EntityId = _reuben.Id, CanonicalBook = 1, CanonicalChapter = chapter, CanonicalVerse = verse,
                Source = "a test",
            });
        }

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

    private Entity Record(string slug, string name, EntityKind kind = EntityKind.Person)
    {
        var entity = new Entity { Kind = kind, Slug = slug, Name = name, SourceId = slug, Source = "a test" };
        _db.Entities.Add(entity);
        return entity;
    }

    private void Printed(Text text, Entity entity, string form) =>
        _db.EntityRenderings.Add(new EntityRendering
        {
            EntityId = entity.Id, TextId = text.Id, Form = form, Folded = NameFolding.Fold(form),
            Occurrences = 3, Heading = true,
        });

    private async Task<List<string>> Found(string typed) =>
        await EntityNames.Matching(_db, _db.Entities, typed).Select(e => e.Slug).OrderBy(slug => slug).ToListAsync();

    [Theory]
    [InlineData("Рувим", "reuben")]
    [InlineData("Анна", "hannah")]
    [InlineData("Шемеш", "bethshemesh")]
    [InlineData("бет", "bethshemesh")]
    public async Task ANameIsFoundByAWordOfItAndNotByLettersInsideAnother(string typed, string slug) =>
        (await Found(typed)).Should().Equal(slug);

    [Theory]
    [InlineData("Рамсес")]
    [InlineData("Раамсес")]
    [InlineData("Ramses")]
    public async Task TheFamiliarFormAndTheLatinSpellingFindTheRecord(string typed) =>
        (await Found(typed)).Should().Equal("rameses");

    [Fact]
    public async Task ANearSpellingIsLookedForOnlyWhereNothingIsCalledThat()
    {
        (await Found("Рамесесс")).Should().BeEmpty();

        var near = await EntityNames.Near(_db, _db.Entities, "Рамесесс", CancellationToken.None);

        (await _db.Entities.Where(e => near.Contains(e.Id)).Select(e => e.Slug).ToListAsync()).Should().Equal("rameses");
        (await EntityNames.Near(_db, _db.Entities, "Рум", CancellationToken.None)).Should().BeEmpty("three letters are too few to guess from");
    }

    [Fact]
    public async Task ARecordWithNoLineIsToldApartByItsFirstVerse()
    {
        var first = await EncyclopediaEndpoints.FirstVerses(_db, [_reuben.Id], CancellationToken.None);

        first[_reuben.Id].Should().Be(new VerseRefResponse(1, "Genesis", "genesis", 29, 32));
    }

    [Theory]
    [InlineData("Рамсес", new[] { "ramses" })]
    [InlineData("Голіаф", new[] { "goliaf", "holiaf" })]
    [InlineData("Ramses", new string[0])]
    public void ACyrillicNameIsSpelledInLatinLetters(string typed, string[] spellings) =>
        NameTransliteration.Latin(typed).Should().Equal(spellings);
}
