using System.Text.Json;
using Essenthos.Core.Corpus;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Endpoints;
using Essenthos.Core.Loading.Encyclopedia;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Essenthos.Core.Tests;

/// <summary>
/// What a chapter's panel and a book's count when the owner's ruling puts Jacob on <em>Israel</em> in
/// <em>the children of Israel</em>: the verse is a mention of the people the word for <em>sons</em>
/// names, not of the man, while his own sons, and Jacob where he acts, still count for him.
/// </summary>
[Collection(WitnessDatabaseCollection.Name)]
public sealed class ContextSonsOfTests : IDisposable
{
    private const string Son = "H1121";
    private const string Israel = "H3478";
    private const string Jacob = "H3290";
    private const string Reuben = "H7205";
    private const string OurWords = "Essenthos, from the words this corpus annotates to the person or the place they name";

    private readonly AppDbContext _db;
    private readonly Text _hebrew;
    private readonly Text _english;
    private readonly Dictionary<string, Entity> _records = [];

    public ContextSonsOfTests(WitnessDatabase database)
    {
        _db = database.NewContext();
        Clear();

        _hebrew = Corpus.Add(_db, EntityCandidates.Witness, TextKind.CriticalEdition, "hbo",
            (35, 1, ["ויאמר", "יעקב"]),
            (46, 5, ["ובני", "ישראל"]),
            (46, 9, ["ובני", "ראובן"]));
        _db.AddBook(_hebrew, 2, "Exodus", (1, 7, ["ובני", "ישראל", "פרו"]), (1, 8, ["ובני", "ישראל", "יעקב"]));
        _english = Corpus.Add(_db, "KJV", TextKind.Translation, "eng", (46, 5, ["the", "sons", "of", "Israel"]));
        _db.AddBook(_english, 2, "Exodus", (1, 7, ["the", "children", "of", "Israel"]));
        _db.SaveChanges();

        Hebrew(1, 46, 5, 1, Son, "subs", construct: true);
        Hebrew(1, 46, 5, 2, Israel, "nmpr");
        Hebrew(1, 46, 9, 1, Son, "subs", construct: true);
        Hebrew(1, 46, 9, 2, Reuben, "nmpr");
        Hebrew(2, 1, 7, 1, Son, "subs", construct: true);
        Hebrew(2, 1, 7, 2, Israel, "nmpr");
        Hebrew(2, 1, 8, 1, Son, "subs", construct: true);
        Hebrew(2, 1, 8, 2, Israel, "nmpr");
        Hebrew(2, 1, 8, 3, Jacob, "nmpr");
        Hebrew(1, 35, 1, 2, Jacob, "nmpr");
        _db.SaveChanges();

        Link(At(_english, 2, 1, 7, 2), At(_hebrew, 2, 1, 7, 1));
        Link(At(_english, 2, 1, 7, 4), At(_hebrew, 2, 1, 7, 2));
        Link(At(_english, 1, 46, 5, 2), At(_hebrew, 1, 46, 5, 1));
        Link(At(_english, 1, 46, 5, 4), At(_hebrew, 1, 46, 5, 2));

        var jacob = Record("jacob", EntityKind.Person, Israel, null);
        Record("israelites", EntityKind.People, Israel, jacob);
        var reuben = Record("reuben", EntityKind.Person, Reuben, null);
        Record("reubenites", EntityKind.People, Reuben, reuben);
    }

    public void Dispose()
    {
        Clear();
        _db.Dispose();
    }

    private void Clear()
    {
        _db.Database.ExecuteSqlRaw("DELETE FROM link");
        _db.Database.ExecuteSqlRaw("DELETE FROM text");
        _db.Database.ExecuteSqlRaw("DELETE FROM entity");
    }

    private Word At(Text text, int book, int chapter, int verse, int position) =>
        _db.Words.Single(w => w.TextId == text.Id && w.Verse!.Book!.CanonicalOrdinal == book
                              && w.Verse.ChapterNumber == chapter && w.Verse.Number == verse && w.Position == position);

    private void Hebrew(int book, int chapter, int verse, int position, string number, string pos, bool construct = false)
    {
        var word = At(_hebrew, book, chapter, verse, position);
        word.StrongNumber = number;
        word.Morphology = JsonDocument.Parse(construct
            ? $$"""{"pos": "{{pos}}", "state": "c", "number": "pl"}"""
            : $$"""{"pos": "{{pos}}", "nameType": "pers,gens,topo"}""");
    }

    private void Link(Word translation, Word original)
    {
        var link = new Link
        {
            FromTextId = translation.TextId, ToTextId = original.TextId, Relation = LinkRelation.Renders,
            Method = LinkMethod.StatedBySource, Provenance = new() { Source = "a test" },
        };
        _db.Links.Add(link);
        _db.LinkWords.Add(new LinkWord { Link = link, Word = translation, Side = LinkSide.From });
        _db.LinkWords.Add(new LinkWord { Link = link, Word = original, Side = LinkSide.To });
        _db.SaveChanges();
    }

    private Entity Record(string slug, EntityKind kind, string? number, Entity? origin)
    {
        var entity = new Entity
        {
            Kind = kind, Slug = slug, Name = slug, SourceId = slug, Source = "a test", OriginEntityId = origin?.Id,
            Names = [new EntityName { Label = slug.Split('-')[0], HebrewStrongNumber = number, Kind = "proper name" }],
        };
        _db.Entities.Add(entity);
        _db.SaveChanges();
        _records[slug] = entity;
        return entity;
    }

    private void Annotate(Word word, string slug)
    {
        _db.WordEntities.Add(new WordEntity
        {
            WordId = word.Id, EntityId = _records[slug].Id, Method = LinkMethod.RuleBased, Confidence = 0.9,
            Source = "a test",
        });
        _db.SaveChanges();
    }

    private async Task Load()
    {
        await new EponymReadingLoader(_db, NullLogger<EponymReadingLoader>.Instance)
            .Load(new EponymReadings("a model", "2026-10-08", []));
        await new OwnReferenceLoader(_db, NullLogger<OwnReferenceLoader>.Instance).Load();
    }

    private async Task<Dictionary<string, int[]>> Panel(int book, int chapter) =>
        (await ContextEndpoints.Context(_db, book, chapter, null, default))
        .Entities.ToDictionary(e => e.Slug, e => e.Verses.ToArray());

    /// <summary>
    /// <em>The children of Israel were fruitful</em>: the panel lists the Israelites the word for
    /// <em>children</em> names, and not Jacob, whom <em>Israel</em> still names on the word.
    /// </summary>
    [Fact]
    public async Task TheChildrenOfIsraelAreTheIsraelitesInThePanelAndNotJacob()
    {
        await Load();

        var panel = await Panel(2, 1);

        panel.Should().ContainKey("israelites");
        panel["israelites"].Should().Equal(7, 8);
        panel.Should().NotContainKey("jacob");
        (await Annotations.AllOf(_db, At(_hebrew, 2, 1, 7, 2).Id, default))
            .Select(e => e.Slug).Should().Equal("jacob");
    }

    /// <summary>
    /// The man's own page keeps the verse his name stands in; only the panel's count leaves it to the
    /// people. A verse some other word names him in still counts for him, and only that verse.
    /// </summary>
    [Fact]
    public async Task HisVerseListKeepsTheVerseAndTheVerseAnotherWordNamesHimInStillCounts()
    {
        Annotate(At(_hebrew, 2, 1, 8, 3), "jacob");
        await Load();

        (await _db.EntityVerses.Where(v => v.Entity!.Slug == "jacob" && v.CanonicalBook == 2)
                .Select(v => v.CanonicalVerse).Distinct().OrderBy(v => v).ToListAsync())
            .Should().Equal(7, 8);
        var panel = await Panel(2, 1);
        panel["jacob"].Should().Equal(8);
        panel["israelites"].Should().Equal(7, 8);
    }

    /// <summary>
    /// His own sons, named in the passage, are <em>sons</em> of nobody, so Genesis 46 still counts the
    /// name for Jacob, and Reuben's too; Jacob where he acts counts as ever.
    /// </summary>
    [Fact]
    public async Task HisOwnSonsAndTheManWhereHeActsStillCountForHim()
    {
        Annotate(At(_hebrew, 1, 35, 1, 2), "jacob");
        await Load();

        var genesis46 = await Panel(1, 46);
        genesis46["jacob"].Should().Equal(5);
        genesis46["reuben"].Should().Equal(9);
        genesis46.Should().NotContainKey("israelites");
        (await Panel(1, 35))["jacob"].Should().Equal(1);
    }

    /// <summary>The book's panel counts the same way: the Israelites for the verses, Jacob only where he is named otherwise.</summary>
    [Fact]
    public async Task TheBookCountsTheSameWay()
    {
        Annotate(At(_hebrew, 2, 1, 8, 3), "jacob");
        await Load();

        var about = await BookAboutEndpoints.About(_db, 2, null, default);

        about.People.Select(p => (p.Slug, p.Verses)).Should().Equal(("jacob", 1));
    }
}
