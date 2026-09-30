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
/// Whose the Anointed is where it stands, as the shipped rulings read it: the bearer written beside
/// the title where the text fixes one, and a bearer a rule had already written taken back from a
/// verse the rulings leave open.
///
/// <para>
/// The corpus here holds four of the real verses, because the rulings address verses: Isaiah 45:1
/// names Cyrus, Psalm 2:2 names nobody, John 1:20 is a denial, Matthew 1:16 is Jesus.
/// </para>
/// </summary>
[Collection(WitnessDatabaseCollection.Name)]
public sealed class TitleReadingLoaderTests : IDisposable
{
    private const string Mashiach = "H4899";

    private const string Christos = "G5547";

    private const int Psalms = 19;

    private const int Isaiah = 23;

    private const int Matthew = 40;

    private const int John = 43;

    private readonly AppDbContext _db;
    private readonly Text _hebrew;
    private readonly Text _greek;
    private readonly Text _english;
    private readonly Entity _anointed;
    private readonly Entity _cyrus;
    private readonly Entity _jesus;
    private readonly string _source = SenseReadingFiles.TitleReadings().Source;

    public TitleReadingLoaderTests(WitnessDatabase database)
    {
        _db = database.NewContext();
        Clear();

        _hebrew = NewText(EntityCandidates.Witness, TextKind.CriticalEdition, "hbo");
        _db.AddBook(_hebrew, Isaiah, "Isaiah", (45, 1, ["למשיחו", "לכורש"]));
        _db.AddBook(_hebrew, Psalms, "Psalms", (2, 2, ["משיחו"]));
        _greek = NewText(NestleTextSource.Slug, TextKind.CriticalEdition, "grc");
        _db.AddBook(_greek, John, "John", (1, 20, ["ὁ", "Χριστός"]));
        _db.AddBook(_greek, Matthew, "Matthew", (1, 16, ["Ἰησοῦς", "Χριστός"]));
        _english = NewText("KJV", TextKind.Translation, "eng");
        _db.AddBook(_english, Isaiah, "Isaiah", (45, 1, ["anointed", "Cyrus"]));
        _db.AddBook(_english, John, "John", (1, 20, ["the", "Christ"]));
        _db.SaveChanges();

        _db.WordAt(_hebrew, 45, 1, 1).StrongNumber = Mashiach;
        _db.WordAt(_hebrew, 2, 2, 1).StrongNumber = Mashiach;
        _db.WordAt(_greek, 1, 20, 2).StrongNumber = Christos;
        _db.WordAt(_greek, 1, 16, 2).StrongNumber = Christos;

        _anointed = Add("anointed", EntityKind.Title);
        _cyrus = Add("cyrus", EntityKind.Person);
        _jesus = Add("jesus", EntityKind.Person);
        Link(_db.WordAt(_english, 45, 1, 1), _hebrew, _db.WordAt(_hebrew, 45, 1, 1));
        Link(_db.WordAt(_english, 1, 20, 2), _greek, _db.WordAt(_greek, 1, 20, 2));
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

    private Text NewText(string slug, TextKind kind, string language)
    {
        var text = new Text { Slug = slug, Name = slug, Kind = kind, Language = language };
        _db.Texts.Add(text);
        return text;
    }

    private Entity Add(string slug, EntityKind kind)
    {
        var entity = new Entity { Kind = kind, Slug = slug, Name = slug, SourceId = slug, Source = "a test" };
        _db.Entities.Add(entity);
        return entity;
    }

    private void Link(Word rendering, Text original, Word word)
    {
        var link = new Link
        {
            FromTextId = _english.Id, ToTextId = original.Id, Relation = LinkRelation.Renders,
            Method = LinkMethod.StatedBySource, Source = "a test",
        };
        _db.Links.Add(link);
        _db.LinkWords.Add(new LinkWord { Link = link, Word = rendering, Side = LinkSide.From });
        _db.LinkWords.Add(new LinkWord { Link = link, Word = word, Side = LinkSide.To });
    }

    private TitleReadingLoader Loader() => new(_db, NullLogger<TitleReadingLoader>.Instance);

    private async Task<List<(long Word, int Entity)>> Named()
    {
        _db.ChangeTracker.Clear();
        return [.. (await _db.WordEntities.ToListAsync()).Select(a => (a.WordId, a.EntityId))];
    }

    /// <summary>His anointed, Cyrus: the word names Cyrus by the ruling, and so does the word that renders it.</summary>
    [Fact]
    public async Task TheWordNamesTheBearerTheVerseNames()
    {
        var outcome = await Loader().Load();

        var hebrew = _db.WordAt(_hebrew, 45, 1, 1).Id;
        var english = _db.WordAt(_english, 45, 1, 1).Id;
        (await Named()).Should().BeEquivalentTo([(hebrew, _cyrus.Id), (english, _cyrus.Id)]);
        var row = await _db.WordEntities.SingleAsync(a => a.WordId == hebrew);
        row.Method.Should().Be(LinkMethod.Manual);
        row.Confidence.Should().BeNull();
        row.Source.Should().Be(_source);
        row.Note.Should().StartWith("H4899 at Isaiah 45:1");
        outcome.ByBearer.Should().Contain(("cyrus", 1));
        outcome.TitleOnly.Should().Be(2);
    }

    /// <summary>
    /// A rule had read the Christ of John's denial as Jesus, and the links had carried it. Both are
    /// taken back; the Christ of Matthew 1:16, which the rulings do not list, keeps him.
    /// </summary>
    [Fact]
    public async Task ABearerARuleWroteIsTakenBackFromAVerseLeftOpen()
    {
        var denied = _db.WordAt(_greek, 1, 20, 2);
        var called = _db.WordAt(_greek, 1, 16, 2);
        var rendering = _db.WordAt(_english, 1, 20, 2);
        foreach (var (word, note) in new[]
                 {
                     (denied, "Χριστός, the title the New Testament gives Jesus"),
                     (called, "Χριστός, the title the New Testament gives Jesus"),
                     (rendering, $"through NESTLE1904 word {denied.Id}, linked by stated-by-source"),
                 })
        {
            _db.WordEntities.Add(new WordEntity
            {
                Word = word, Entity = _jesus, Method = LinkMethod.RuleBased, Confidence = 0.99,
                Source = FixedTitleLoader.Source, Note = note,
            });
        }

        _db.WordEntities.Add(new WordEntity
        {
            Word = denied, Entity = _anointed, Method = LinkMethod.RuleBased, Confidence = 0.99,
            Source = TitleLoader.WordSource,
        });
        await _db.SaveChangesAsync();

        var outcome = await Loader().Load();

        outcome.Withdrawn.Should().Be(2);
        var named = await Named();
        named.Should().NotContain((denied.Id, _jesus.Id));
        named.Should().NotContain((rendering.Id, _jesus.Id));
        named.Should().Contain((denied.Id, _anointed.Id));
        named.Should().Contain((called.Id, _jesus.Id));
    }

    /// <summary>A word a reading had already given to the same man keeps its row and gains the ruling as a claim.</summary>
    [Fact]
    public async Task ARulingThatAgreesWithAReadingIsAClaimOnItsRow()
    {
        var word = _db.WordAt(_hebrew, 45, 1, 1);
        _db.WordEntities.Add(new WordEntity
        {
            Word = word, Entity = _cyrus, Method = LinkMethod.ModelReading, Confidence = 0.9, Source = "a reading",
        });
        await _db.SaveChangesAsync();

        await Loader().Load();

        var row = await _db.WordEntities.AsNoTracking().SingleAsync(a => a.WordId == word.Id);
        row.Source.Should().Be("a reading");
        (await _db.WordEntityClaims.CountAsync(c => c.WordEntityId == row.Id && c.Source == _source)).Should().Be(1);
        (await Loader().Load()).AlreadyLoaded.Should().BeTrue();
    }

    /// <summary>The pipeline runs on every boot, and a second boot writes nothing.</summary>
    [Fact]
    public async Task ASecondBootWritesNothing()
    {
        await Loader().Load();
        var ids = await _db.WordEntities.Select(a => a.Id).OrderBy(id => id).ToListAsync();

        var outcome = await Loader().Load();

        outcome.AlreadyLoaded.Should().BeTrue();
        (await _db.WordEntities.Select(a => a.Id).OrderBy(id => id).ToListAsync()).Should().Equal(ids);
    }

    /// <summary>Without the title's record nothing is written.</summary>
    [Fact]
    public async Task WithoutTheTitleNothingIsRead()
    {
        _db.Entities.Remove(_anointed);
        await _db.SaveChangesAsync();

        var outcome = await Loader().Load();

        outcome.Missing.Should().Contain("anointed");
        (await Named()).Should().BeEmpty();
    }

    /// <summary>
    /// The shipped rulings: every reference is a verse, no occurrence is ruled twice, every bearer
    /// is one the title's record joins to it, and mashiach is ruled in each of its 38 verses.
    /// </summary>
    [Fact]
    public void TheShippedRulingsAreWellFormed()
    {
        var rulings = SenseReadingFiles.TitleReadings();
        var title = SenseReadingFiles.Titles().Titles.Single(t => t.Slug == rulings.Title);

        rulings.Readings.Should().OnlyContain(r => r.At.IsVerse && r.At.FromChapter == r.At.ToChapter && r.At.FromVerse == r.At.ToVerse);
        rulings.Readings.Select(r => (r.Reference, r.Strong)).Should().OnlyHaveUniqueItems();
        rulings.Readings.Select(r => r.Strong).Distinct().Should().BeEquivalentTo(title.Words!.Select(w => w.Strong));
        rulings.Readings.Select(r => r.Bearer).OfType<string>().Distinct()
            .Should().BeSubsetOf(title.Bearers!.Select(b => b.Slug));
        rulings.Readings.Count(r => r.Strong == Mashiach).Should().Be(38);
        title.Words.Should().OnlyContain(w => w.Beside);
    }

    /// <summary>
    /// The owner's corrections of 2026-09-30: the shield of 2 Samuel 1:21 is not anointed with oil and
    /// the word there is no title; John 9:22 and both words of Acts 17:3 are the title alone; the
    /// Christ of Revelation 11:15 and 12:10 is the title and Jesus.
    /// </summary>
    [Fact]
    public void TheOwnersCorrectionsOfTheThirtiethAreInTheRulings()
    {
        var rulings = SenseReadingFiles.TitleReadings();
        var title = SenseReadingFiles.Titles().Titles.Single(t => t.Slug == rulings.Title);
        const int Samuel2 = 10;
        const int John = 43;
        const int Acts = 44;
        const int Revelation = 66;

        var mashiach = title.Words!.Single(w => w.Strong == Mashiach);
        mashiach.Admits(Samuel2, 1, 21, 1).Should().BeFalse("the word there is an adjective of the shield");
        mashiach.Admits(Samuel2, 1, 14, 1).Should().BeTrue();
        rulings.Readings.Single(r => r.Reference == "2SA 1:21").Bearer.Should().BeNull();

        var open = rulings.Open("G5547");
        open.Should().Contain(span => span.Holds(John, 9, 22, 1));
        open.Should().Contain(span => span.Holds(Acts, 17, 3, 1));
        open.Should().Contain(span => span.Holds(Acts, 17, 3, 2));
        open.Should().NotContain(span => span.Holds(Revelation, 11, 15, 1));
        open.Should().NotContain(span => span.Holds(Revelation, 12, 10, 1));
        rulings.Readings.Where(r => r.Reference is "REV 11:15" or "REV 12:10").Should().BeEmpty();
    }
}
