using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Loading.Encyclopedia;
using Essenthos.Core.Loading.Links;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Essenthos.Core.Tests;

/// <summary>
/// King James verses the mapping file states and the word-for-word reading could not line up, so
/// that none of their words reached a Hebrew word and none of their names was carried: a name the
/// loaded text spells otherwise, a verse the file and BHSA divide in different places, a word the
/// file numbers twice, and a corpus loaded before the reading could line them up.
/// </summary>
[Collection(WitnessDatabaseCollection.Name)]
public sealed class UnlinkedKingJamesVerseTests : IDisposable
{
    private readonly AppDbContext _db;
    private Text _kjv = null!;
    private Text _bhsa = null!;

    public UnlinkedKingJamesVerseTests(WitnessDatabase database)
    {
        _db = database.NewContext();
        Clear();
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

    /// <summary>
    /// Genesis 14:5: bible4u prints <em>Emins</em> where the file, with the 1769, prints
    /// <em>Emims</em>. Read word against word the verse was refused whole, and the Emim, named on the
    /// Hebrew, were named on nothing in the King James.
    /// </summary>
    [Fact]
    public async Task ANameTheLoadedTextSpellsOtherwiseIsLinkedAndItsNameCarries()
    {
        Seed(
            [(14, 5, ["and", "the", "Emins", "in", "Shaveh", "Kiriathaim"])],
            [(14, 5, ["וְ", "אֶת", "הָ", "אֵימִים", "בְּ", "שָׁוֵה", "קִרְיָתָיִם"])]);
        var emim = new Entity { Kind = EntityKind.People, Slug = "emims", Name = "Emims", SourceId = "emims", Source = "a test" };
        _db.Entities.Add(emim);
        _db.SaveChanges();
        _db.WordEntities.Add(new WordEntity
        {
            WordId = _db.WordAt(_bhsa, 14, 5, 4).Id,
            EntityId = emim.Id,
            Method = LinkMethod.StatedBySource,
            Source = "a test",
            Claims = [new WordEntityClaim { Method = LinkMethod.StatedBySource, Source = "a test" }],
        });
        _db.SaveChanges();

        var outcome = await Load(new MappingRecord(1, 14, 5, Entries(1, 2, 3, 4, 5, 6, 7),
        [
            Segment(["and"], 1),
            Segment(["the"], 3),
            Segment(["Emims"], 4),
            Segment(["in", "Shaveh"], 5),
            Segment(["Kiriathaim"], 7),
        ]));

        outcome.Refused.Should().Be(0);
        (await HebrewOf(_db.WordAt(_kjv, 14, 5, 3))).Should().Equal("אֵימִים");

        await Carrier().Carry();
        var named = await _db.WordEntities.Where(a => a.WordId == _db.WordAt(_kjv, 14, 5, 3).Id)
            .Select(a => a.Entity!.Slug).ToListAsync();
        named.Should().Equal("emims");
    }

    /// <summary>
    /// 1 Kings 18:33 and 34: the King James ends verse 33 where BHSA has already begun verse 34.
    /// Neither verse's Hebrew lines up with the file's, and the file's running numbers, which are
    /// BHSA's own, say which word each entry is — across the boundary where the two put it apart.
    /// </summary>
    [Fact]
    public async Task AVerseTheFileDividesDifferentlyIsLinkedThroughTheRunningNumbersAcrossTheVerseBoundary()
    {
        Seed(
            [(1, 1, ["a", "b"]), (1, 2, ["c", "d"]), (1, 3, ["e", "f"]), (1, 4, ["g", "h"])],
            [(1, 1, ["א", "ב"]), (1, 2, ["ג", "ד", "ה"]), (1, 3, ["ו"]), (1, 4, ["ז", "ח"])]);

        var outcome = await Load(
            Record(1, 1, 2),
            Record(2, 3, 4),
            Record(3, 5, 6),
            Record(4, 7, 8));

        outcome.Refused.Should().Be(0);
        outcome.NumberedVerses.Should().Be(2);
        (await HebrewOf(_db.WordAt(_kjv, 1, 2, 2))).Should().Equal("ד");
        (await HebrewOf(_db.WordAt(_kjv, 1, 3, 1))).Should().Equal("ה");
        (await HebrewOf(_db.WordAt(_kjv, 1, 3, 2))).Should().Equal("ו");
    }

    /// <summary>
    /// Joshua 15:32: the file gives En-rimmon two Strong numbers and so two entries under one running
    /// number, where BHSA has one word. The bare marker of the first entry says nothing the second
    /// does not: it is not the rest of the phrase before it, and not a word left unrendered.
    /// </summary>
    [Fact]
    public async Task AWordTheFileNumbersTwiceIsOneWordAndItsBareMarkerAddsNothing()
    {
        Seed(
            [(1, 1, ["a"]), (1, 2, ["and", "Enrimmon", "all"]), (1, 3, ["b"])],
            [(1, 1, ["א"]), (1, 2, ["וְ", "עַיִן רִמּוֹן", "כָּל"]), (1, 3, ["ב"])]);

        var outcome = await Load(
            Record(1, 1),
            new MappingRecord(1, 1, 2, Entries(2, 3, 3, 4),
            [
                Segment(["and"], 2),
                Segment([], 3),
                Segment(["Enrimmon"], 3),
                Segment(["all"], 4),
            ]),
            Record(3, 5));

        outcome.Refused.Should().Be(0);
        (await HebrewOf(_db.WordAt(_kjv, 1, 2, 1))).Should().Equal("וְ");
        (await HebrewOf(_db.WordAt(_kjv, 1, 2, 2))).Should().Equal("עַיִן רִמּוֹן");
        var omissions = await _db.Links.CountAsync(l => l.Relation == LinkRelation.Omits);
        omissions.Should().Be(0);
    }

    /// <summary>
    /// A corpus loaded before the reading could line a verse up keeps its links and lacks that
    /// verse's. The load relinks every verse with none, leaves the linked ones as they are, and a
    /// second time finds nothing to do.
    /// </summary>
    [Fact]
    public async Task ALoadedCorpusIsGivenTheLinksOfTheVersesItHasNoneOfAndASecondTimeNothing()
    {
        Seed(
            [(14, 4, ["in", "the", "year"]), (14, 5, ["and", "the", "Emins"])],
            [(14, 4, ["בְּ", "שְׁנַת"]), (14, 5, ["וְ", "הָ", "אֵימִים"])]);
        MappingRecord[] records =
        [
            new(1, 14, 4, Entries(1, 2), [Segment(["in", "the"], 1), Segment(["year"], 2)]),
            new(1, 14, 5, Entries(3, 4, 5), [Segment(["and"], 3), Segment(["the"], 4), Segment(["Emims"], 5)]),
        ];
        await Load(records);
        var kept = await _db.Links.Where(l => l.Words!.Any(w => w.Word!.Verse!.Number == 4))
            .Select(l => l.Id).OrderBy(id => id).ToListAsync();
        await _db.Database.ExecuteSqlRawAsync(
            "DELETE FROM link WHERE id IN (SELECT link_id FROM link_word lw JOIN word w ON w.id = lw.word_id " +
            "JOIN verse v ON v.id = w.verse_id WHERE v.number = 5)");

        var loader = new OldTestamentLinkLoader(_db, NullLogger<OldTestamentLinkLoader>.Instance);
        var relinked = await loader.Relink(records, null, null);
        var again = await loader.Relink(records, null, null);

        relinked.Should().Be(3);
        again.Should().Be(0);
        (await HebrewOf(_db.WordAt(_kjv, 14, 5, 3))).Should().Equal("אֵימִים");
        (await _db.Links.Where(l => l.Words!.Any(w => w.Word!.Verse!.Number == 4))
                .Select(l => l.Id).OrderBy(id => id).ToListAsync())
            .Should().Equal(kept);
    }

    /// <summary>Both texts in Genesis, every Hebrew word glossed as the file glosses its running number.</summary>
    private void Seed(
        (int Chapter, int Verse, string[] Words)[] english,
        (int Chapter, int Verse, string[] Words)[] hebrew)
    {
        _kjv = Corpus.Add(_db, "KJV", TextKind.Translation, "eng", english);
        _bhsa = Corpus.Add(_db, "BHSA", TextKind.ManuscriptTradition, "hbo", hebrew);
        _db.SaveChanges();
        var running = 0;
        foreach (var word in _db.Words.Where(w => w.TextId == _bhsa.Id).OrderBy(w => w.Id).ToList())
        {
            word.Gloss = $"gloss{++running}";
        }

        _db.SaveChanges();
        _db.ChangeTracker.Clear();
    }

    private async Task<LinkOutcome> Load(params MappingRecord[] records) =>
        await new OldTestamentLinkLoader(_db, NullLogger<OldTestamentLinkLoader>.Instance).Load(records);

    /// <summary>A verse of Genesis 1 whose English words, one per entry, are the letters the seed gave them.</summary>
    private MappingRecord Record(int verse, params int[] positions) =>
        new(1, 1, verse, Entries(positions),
        [
            .. positions.Select((position, i) =>
                Segment([_db.WordAt(_kjv, 1, verse, i + 1).Surface], position)),
        ]);

    /// <summary>Entries at the running numbers given, glossed as BHSA glosses the word at each.</summary>
    private static List<HebrewEntry> Entries(params int[] positions) =>
        [.. positions.Select(position => new HebrewEntry($"H{position}", "c1", position, $"gloss{position}"))];

    private static EnglishSegment Segment(string[] words, int rendersPosition) =>
        new(
            [.. words.Select(word => new EnglishWord(word, false))],
            new HebrewEntry($"H{rendersPosition}", "c1", rendersPosition, $"gloss{rendersPosition}"));

    /// <summary>The Hebrew words the links of one King James word reach.</summary>
    private async Task<List<string>> HebrewOf(Word english)
    {
        var links = await _db.LinkWords.Where(lw => lw.WordId == english.Id && lw.Side == LinkSide.From)
            .Select(lw => lw.LinkId).ToListAsync();
        return await _db.LinkWords.Where(lw => links.Contains(lw.LinkId) && lw.Side == LinkSide.To)
            .OrderBy(lw => lw.WordId)
            .Select(lw => lw.Word!.Surface)
            .ToListAsync();
    }

    private AnnotationCarrier Carrier() =>
        new(
            _db,
            new CrossedNameLoader(_db, NullLogger<CrossedNameLoader>.Instance),
            new ForeignNames(_db, NullLogger<ForeignNames>.Instance),
            new EqualTwinNames(_db, NullLogger<EqualTwinNames>.Instance),
            new PronounReferents(_db, NullLogger<PronounReferents>.Instance),
            NullLogger<AnnotationCarrier>.Instance);
}
