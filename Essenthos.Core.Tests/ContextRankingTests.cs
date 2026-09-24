using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Endpoints;
using FluentAssertions;
using Microsoft.EntityFrameworkCore.Storage;
using Xunit;

namespace Essenthos.Core.Tests;

/// <summary>
/// What the Chapter Context puts first: the topics most particular to the chapter, the people it
/// names set apart from Nave's readings of it, and the records its words name most.
/// </summary>
[Collection(WitnessDatabaseCollection.Name)]
public sealed class ContextRankingTests : IDisposable
{
    private const int Genesis = 1;

    private const string FromTheWords =
        "Essenthos, from Strong's Dictionary entry for the word and every word BHSA numbers with it";

    private readonly AppDbContext _db;
    private readonly IDbContextTransaction _transaction;
    private readonly Text _hebrew;
    private readonly Text _english;

    public ContextRankingTests(WitnessDatabase database)
    {
        _db = database.NewContext();
        _transaction = _db.Database.BeginTransaction();

        _hebrew = Corpus.Add(_db, "test-hebrew", TextKind.CriticalEdition, "hbo",
            (10, 1, ["אלה", "תולדת", "בני", "נח"]),
            (10, 2, ["בני", "יפת", "גמר"]),
            (10, 3, ["ובני", "גמר"]),
            (11, 1, ["ויהי"]));
        _english = Corpus.Add(_db, "test-english", TextKind.Translation, "eng",
            (10, 1, ["These", "are", "the", "sons", "of", "Noah"]),
            (10, 2, ["The", "sons", "of", "Japheth", "Gomer"]),
            (10, 3, ["And", "the", "sons", "of", "Gomer"]),
            (11, 1, ["And"]));
        _db.SaveChanges();
    }

    public void Dispose()
    {
        _transaction.Rollback();
        _transaction.Dispose();
        _db.Dispose();
    }

    /// <summary>
    /// Two topics filing the same two verses of the chapter, one of them filing fifty verses of the
    /// Bible and the other these two: the chapter is about the rare one.
    /// </summary>
    [Fact]
    public async Task ATopicRareInTheBibleOutranksOneFilingAsMuchOfTheChapterEverywhere()
    {
        var common = Topic("PROVIDENCE", ("", 1, 2));
        foreach (var chapter in Enumerable.Range(12, 50))
        {
            common.References.Add(Cites(chapter, null, null));
        }

        Topic("GENEALOGY", ("", 1, 2));
        await _db.SaveChangesAsync();

        var context = await ContextEndpoints.Context(_db, Genesis, 10, null, default);

        context.Topics.Select(t => t.Name).Should().Equal("Genealogy", "Providence");
    }

    [Fact]
    public void AWholeChapterCitationCountsForLessThanTheVersesItNames()
    {
        var named = ChapterTopics.Relevance(named: 2, whole: false, said: false, length: 3, versesInBible: 10);
        var whole = ChapterTopics.Relevance(named: 0, whole: true, said: false, length: 3, versesInBible: 10);
        var wholeSaid = ChapterTopics.Relevance(named: 0, whole: true, said: true, length: 3, versesInBible: 10);

        whole.Should().BeApproximately(named * ChapterTopics.WholeChapterUnsaid * 3 / 2, 1e-9);
        wholeSaid.Should().BeApproximately(whole * 2 * ChapterTopics.SaidBonus, 1e-9);
        named.Should().BeGreaterThan(whole);
    }

    /// <summary>
    /// <em>Sons</em> is in the English of verse 1 and <em>Descendants</em> is nowhere in it: the one
    /// the words say is marked and comes first.
    /// </summary>
    [Fact]
    public async Task AHeadingTheChaptersWordsSayIsMarkedAndCountsForMore()
    {
        Topic("DESCENDANTS", ("", 1, 1));
        Topic("SONS", ("", 1, 1));
        await _db.SaveChangesAsync();

        var context = await ContextEndpoints.Context(_db, Genesis, 10, null, default);

        context.Topics.Select(t => (t.Name, t.Said)).Should().Equal(("Sons", true), ("Descendants", false));
    }

    /// <summary>
    /// Nave's NOAH is Noah's entry, and belongs on the row of the Noah the chapter names. His JESUS,
    /// THE CHRIST in a chapter that names no Jesus is his reading of it; his list of quotations is no
    /// subject at all; a place he files the whole chapter under and the chapter never names is his
    /// outline of the book. What is left is the chapter's themes.
    /// </summary>
    [Fact]
    public async Task TopicsAreSplitIntoThemesPeopleAndReadings()
    {
        var noah = Record("noah", EntityKind.Person);
        var jesus = Record("jesus", EntityKind.Person);
        Record("rome", EntityKind.Place);
        await _db.SaveChangesAsync();
        Names(_db.WordAt(_english, 10, 1, 6), noah);
        NamedAt(jesus, 12, 1);
        Topic("NOAH", ("Descendants of", 1, 1));
        Topic("JESUS, THE CHRIST", ("Divinity of", 2, 2)).References.Add(Cites(12, 1, 1));
        Topic("QUOTATIONS AND ALLUSIONS", ("", 3, 3)).Slug = "quotationsandallusions";
        Topic("ROME", ("", null, null));
        Topic("SONS", ("", 1, 3));
        await _db.SaveChangesAsync();

        var context = await ContextEndpoints.Context(_db, Genesis, 10, null, default);

        context.Topics.Select(t => (t.Name, t.Group, t.Record)).Should().Equal(
            ("Sons", ChapterTopics.Groups.Theme, null),
            ("Noah", ChapterTopics.Groups.Person, "noah"),
            ("Jesus, the Christ", ChapterTopics.Groups.Reading, "jesus"),
            ("Rome", ChapterTopics.Groups.Reading, null));
    }

    /// <summary>
    /// Gomer is named by the words of two verses; Magog is in three, but only because a source lists
    /// him there, and a verse only a list gives counts half.
    /// </summary>
    [Fact]
    public async Task AVerseOnlyASourceListsCountsHalfOfOneTheWordsName()
    {
        var gomer = Record("gomer", EntityKind.Person);
        var magog = Record("magog", EntityKind.Person);
        await _db.SaveChangesAsync();
        Names(_db.WordAt(_hebrew, 10, 2, 3), gomer);
        Names(_db.WordAt(_hebrew, 10, 3, 2), gomer);
        NamedAt(magog, 10, 1);
        NamedAt(magog, 10, 2);
        NamedAt(magog, 10, 3);
        await _db.SaveChangesAsync();

        var context = await ContextEndpoints.Context(_db, Genesis, 10, null, default);

        context.Entities.Select(e => (e.Slug, e.Verses.Count)).Should().Equal(("gomer", 2), ("magog", 3));
    }

    /// <summary>Of two records equally present, the one found in fewer chapters of the Bible is the more particular.</summary>
    [Fact]
    public async Task ATieGoesToTheRecordFoundInFewerChapters()
    {
        var javan = Record("javan", EntityKind.Person);
        var tiras = Record("tiras", EntityKind.Person);
        await _db.SaveChangesAsync();
        NamedAt(javan, 10, 2);
        NamedAt(javan, 11, 1);
        NamedAt(tiras, 10, 2);
        await _db.SaveChangesAsync();

        var context = await ContextEndpoints.Context(_db, Genesis, 10, null, default);

        context.Entities.Select(e => e.Slug).Should().Equal("tiras", "javan");
    }

    /// <summary>
    /// A source that files a verse under YHVH where the words there are elohim is read off it: the
    /// words name Elohim, and Elohim is not YHVH. The two stay records of the words for God.
    /// </summary>
    [Fact]
    public async Task ASourcesReadingOfOneWordForGodGivesWayToTheWordsNamingAnother()
    {
        var elohim = Record("elohim", EntityKind.Term);
        var yhvh = Record("yhvh", EntityKind.Person);
        await _db.SaveChangesAsync();
        NamedAt(elohim, 10, 1, source: FromTheWords);
        NamedAt(elohim, 10, 2, source: FromTheWords);
        NamedAt(yhvh, 10, 1);
        NamedAt(yhvh, 10, 2);
        NamedAt(yhvh, 10, 3);
        await _db.SaveChangesAsync();

        var context = await ContextEndpoints.Context(_db, Genesis, 10, null, default);

        context.Entities.Select(e => (e.Slug, string.Join(',', e.Verses), e.Group)).Should().Equal(
            ("elohim", "1,2", ChapterSalience.GodGroup),
            ("yhvh", "3", ChapterSalience.GodGroup));
    }

    /// <summary>
    /// The man his record heads Bartholomew is called Nathanael where the chapter speaks of him, and
    /// the row says so; a record the chapter calls by its headword keeps it.
    /// </summary>
    [Fact]
    public async Task ARowCarriesTheNameTheChapterUses()
    {
        var bartholomew = Record("bartholomew", EntityKind.Person);
        var philip = Record("philip", EntityKind.Person);
        await _db.SaveChangesAsync();
        _db.EntityNames.Add(new EntityName { EntityId = bartholomew.Id, Label = "Nathanael", Kind = "proper name" });
        NamedAt(bartholomew, 10, 1, label: "Nathanael");
        NamedAt(bartholomew, 10, 2, label: "Nathanael");
        NamedAt(philip, 10, 3, label: "Philip");
        await _db.SaveChangesAsync();

        var context = await ContextEndpoints.Context(_db, Genesis, 10, null, default);

        context.Entities.Select(e => (e.Slug, e.ChapterName)).Should().Equal(
            ("bartholomew", "Nathanael"),
            ("philip", null));
    }

    [Theory]
    [InlineData("Creation", "created")]
    [InlineData("Birds", "bird")]
    [InlineData("Women", "woman")]
    [InlineData("Offerings", "offered")]
    [InlineData("Tree", "trees")]
    [InlineData("Boxes", "box")]
    public void AHeadingWordAndAVerseWordMeetInOneForm(string heading, string verse) =>
        HeadingWords.Of(heading).Should().Equal(HeadingWords.Stems(verse));

    [Fact]
    public void AHeadingKeepsOnlyTheWordsThatCarryItsMeaning() =>
        HeadingWords.Of("LORD'S SUPPER, THE").Should().Equal("lord", "supper");

    private Topic Topic(string name, params (string? Heading, int? First, int? Last)[] verses)
    {
        var topic = new Topic
        {
            Slug = name.ToLowerInvariant(),
            Name = name,
            Source = "test",
            References =
            [
                .. verses.Select(v =>
                {
                    var cited = Cites(10, v.First, v.Last);
                    cited.Heading = v.Heading is "" ? null : v.Heading;
                    return cited;
                }),
            ],
        };
        _db.Topics.Add(topic);
        return topic;
    }

    private static TopicReference Cites(int chapter, int? first, int? last) =>
        new() { CanonicalBook = Genesis, CanonicalChapter = chapter, FirstVerse = first, LastVerse = last };

    private Entity Record(string slug, EntityKind kind)
    {
        var entity = new Entity
        {
            Kind = kind,
            Slug = slug,
            Name = char.ToUpperInvariant(slug[0]) + slug[1..],
            SourceId = $"test:{slug}",
            Source = "test",
        };
        _db.Entities.Add(entity);
        return entity;
    }

    private void Names(Word word, Entity entity) =>
        _db.WordEntities.Add(new WordEntity
        {
            WordId = word.Id,
            EntityId = entity.Id,
            Method = LinkMethod.StatedBySource,
            Source = $"test:{entity.Slug}",
        });

    private void NamedAt(Entity entity, int chapter, int verse, string? label = null, string source = "test") =>
        _db.EntityVerses.Add(new EntityVerse
        {
            Entity = entity,
            CanonicalBook = Genesis,
            CanonicalChapter = chapter,
            CanonicalVerse = verse,
            Label = label,
            Source = source,
        });
}
