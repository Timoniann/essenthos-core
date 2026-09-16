using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Loading.Links;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Essenthos.Core.Tests;

/// <summary>
/// What the mapping file states, and the two readings of an English span with no words in it. The
/// file is linear and the claim is not, so this is where a faithful load and a wrong one part.
/// </summary>
[Collection(WitnessDatabaseCollection.Name)]
public sealed class OldTestamentLinkTests : IDisposable
{
    private readonly AppDbContext _db;
    private Text _kjv = null!;
    private Text _bhsa = null!;

    public OldTestamentLinkTests(WitnessDatabase database)
    {
        _db = database.NewContext();
        Clear();
        Seed();
    }

    public void Dispose()
    {
        Clear();
        _db.Dispose();
    }

    private void Clear() => _db.Database.ExecuteSqlRaw("DELETE FROM text");

    /// <summary>
    /// One verse of each text. The Hebrew is בְּ רֵאשִׁית בָּרָא אֵת: a prefix, a noun, a verb and
    /// the object marker, which is the shape Genesis 1:1 opens with.
    /// </summary>
    private void Seed()
    {
        _kjv = Corpus.Add(_db, "KJV", TextKind.Translation, "eng",
            (1, 1, ["In", "the", "beginning", "created"]));
        _bhsa = Corpus.Add(_db, "BHSA", TextKind.ManuscriptTradition, "hbo",
            (1, 1, ["בְּ", "רֵאשִׁית", "בָּרָא", "אֵת"]));
        _db.SaveChanges();
    }

    /// <summary>
    /// An English phrase over one Hebrew word, the ordinary case, and the one that has to stay one
    /// link rather than three.
    /// </summary>
    [Fact]
    public async Task AnEnglishPhraseRenderingOneHebrewWordIsOneLink()
    {
        await Load(Record(
            Segment(["In", "the", "beginning"], 2),
            Segment(["created"], 3)));

        var links = await Links();
        links.Should().HaveCount(2);
        links[0].English.Should().Equal("In", "the", "beginning");
        links[0].Hebrew.Should().Equal("רֵאשִׁית");
    }

    /// <summary>
    /// Two Hebrew words rendered by one English phrase: the second has no English of its own and
    /// stands next to the first, so it joins that link instead of starting one. Isaiah 53:5 is the
    /// case — מן plus פשע are "for our transgressions", and the file can only say it this way.
    /// </summary>
    [Fact]
    public async Task TwoAdjacentHebrewWordsUnderOnePhraseAreOneLinkNamingBoth()
    {
        await Load(Record(
            Segment(["In", "the", "beginning"], 2),
            Segment([], 3),
            Segment(["created"], 4)));

        var links = await Links();
        links.Should().HaveCount(2);
        links[0].English.Should().Equal("In", "the", "beginning");
        links[0].Hebrew.Should().BeEquivalentTo(["רֵאשִׁית", "בָּרָא"]);
        links[0].Relation.Should().Be(LinkRelation.Renders);
    }

    /// <summary>
    /// A Hebrew word the English does not render at all — the object marker has no English word.
    /// It stands away from the phrase before it, so it gets its own link with an empty English side
    /// rather than being attached to whatever happened to precede it. Saying that "created" renders
    /// the object marker would be a claim about the wrong word.
    /// </summary>
    [Fact]
    public async Task AHebrewWordTheEnglishDoesNotRenderGetsItsOwnLinkWithNothingOnTheEnglishSide()
    {
        await Load(Record(
            Segment(["In", "the", "beginning"], 2),
            Segment(["created"], 3),
            Segment([], 1)));

        var links = await Links();
        var omission = links.Single(l => l.English.Count == 0);
        omission.Hebrew.Should().Equal("בְּ");
        omission.Relation.Should().Be(LinkRelation.Omits);
        links.Should().NotContain(l => l.English.Contains("created") && l.Hebrew.Count > 1);
    }

    /// <summary>
    /// Every one of these correspondences is stated by a file, so none of them carries a
    /// confidence. The database refuses one that does.
    /// </summary>
    [Fact]
    public async Task EveryLinkSaysASourceStatedItAndNoneCarriesAConfidence()
    {
        await Load(Record(
            Segment(["In", "the", "beginning"], 2),
            Segment(["created"], 3)));

        var links = await _db.Links.ToListAsync();
        links.Should().OnlyContain(l => l.Method == LinkMethod.StatedBySource && l.Confidence == null);

        // Who stated it, not which file it arrived in. This asserted the filename until PRB-0179:
        // the source is CC BY-NC and requires attribution, and a path attributes nobody.
        links.Should().OnlyContain(l => l.Source.Contains("Eliran Wong") && l.Source.Contains("CC BY-NC"));
    }

    /// <summary>
    /// A verse whose words do not line up is refused whole. A link built on a misalignment is a
    /// claim about the wrong words, and it would look exactly like a correct one.
    /// </summary>
    [Fact]
    public async Task AVerseWhoseWordsDoNotLineUpIsRefusedRatherThanGuessedAt()
    {
        var outcome = await Load(new MappingRecord(1, 1, 1,
            [Hebrew(1), Hebrew(2)],
            [Segment(["In"], 1)]));

        outcome.Refused.Should().Be(1);
        outcome.Links.Should().Be(0);
        (await _db.Links.CountAsync()).Should().Be(0);
    }

    /// <summary>
    /// The check the file's own comments promised and nothing ran. The counts agree, so the load
    /// before this one wrote four links naming the wrong Hebrew word each, and it would have gone
    /// on doing that for as long as the file and BHSA divided a verse differently.
    /// </summary>
    [Fact]
    public async Task AVerseTheTwoDivideDifferentlyIsCaughtByItsGlossesEvenThoughTheCountsAgree()
    {
        Gloss("gloss2", "gloss3", "gloss4", "gloss5");

        var outcome = await Load(Record(
            Segment(["In", "the"], 1),
            Segment(["beginning"], 2),
            Segment(["created"], 3)));

        outcome.Refused.Should().Be(1);
        outcome.GlossRefused.Should().Be(1);
        outcome.Links.Should().Be(0);
    }

    [Fact]
    public async Task AVerseWhoseGlossesAgreeIsLoadedAndSaysHowFarTheCheckReached()
    {
        Gloss("gloss1", "gloss2", "gloss3", "gloss4");

        var outcome = await Load(Record(
            Segment(["In", "the", "beginning"], 2),
            Segment(["created"], 3)));

        outcome.GlossRefused.Should().Be(0);
        outcome.GlossesCompared.Should().Be(4);
        outcome.Links.Should().Be(2);
    }

    /// <summary>
    /// A witness with no glosses cannot answer, and the outcome says the check reached nothing
    /// rather than reporting a pass it never earned.
    /// </summary>
    [Fact]
    public async Task AWitnessWithNoGlossesLeavesTheCheckWithNothingToCompare()
    {
        var outcome = await Load(Record(
            Segment(["In", "the", "beginning"], 2),
            Segment(["created"], 3)));

        outcome.GlossesCompared.Should().Be(0);
        outcome.Links.Should().Be(2);
    }

    /// <summary>
    /// The numbers are a claim about the Hebrew words, and the Hebrew join is what makes them
    /// trustworthy. A verse the King James words differently from the file loses its links and keeps
    /// its numbers: 388 verses of BHSA, Genesis 5:3 among them, had none for this reason alone.
    /// </summary>
    [Fact]
    public async Task AVerseWhoseEnglishDoesNotLineUpStillGivesTheHebrewItsNumbers()
    {
        var outcome = await Load(Record(
            Segment(["In", "the", "start"], 2),
            Segment(["created"], 3)));

        outcome.Links.Should().Be(0);
        outcome.Refused.Should().Be(1);
        (await Numbers()).Should().Equal("H1", "H2", "H3", "H4");
    }

    /// <summary>
    /// A psalm's superscription is verse 0 of the frame, and the file, numbering as the King James
    /// does, counts its Hebrew into verse 1. Read against verse 1 alone the counts never agree, and
    /// the superscription and the verse after it went unnumbered in 63 psalms.
    /// </summary>
    [Fact]
    public async Task APsalmSuperscriptionIsReadTogetherWithTheVerseTheFileCountsItInto()
    {
        var superscription = Superscribe("מִזְמֹור", "לְ", "דָוִד");

        var outcome = await Load(new MappingRecord(1, 1, 1,
            [.. Enumerable.Range(1, 7).Select(Hebrew)],
            [Segment(["A", "Psalm", "In", "the", "beginning", "created"], 5)]));

        outcome.Refused.Should().Be(1);
        var numbered = await _db.Words
            .Where(w => w.TextId == _bhsa.Id)
            .OrderBy(w => w.VerseId != superscription).ThenBy(w => w.Position)
            .Select(w => w.StrongNumber)
            .ToListAsync();
        numbered.Should().Equal("H1", "H2", "H3", "H4", "H5", "H6", "H7");
    }

    /// <summary>
    /// A corpus whose links were written before the numbers could be recovered gets them on the next
    /// load, and a number already there is not touched.
    /// </summary>
    [Fact]
    public async Task ALoadedCorpusIsGivenTheNumbersItLacksAndKeepsTheOnesItHas()
    {
        await Load(Record(
            Segment(["In", "the", "beginning"], 2),
            Segment(["created"], 3)));
        await _db.Database.ExecuteSqlRawAsync(
            "UPDATE word SET strong_number = CASE position WHEN 1 THEN 'H7225' ELSE NULL END WHERE text_id = {0}",
            _bhsa.Id);

        var outcome = await Load(Record(
            Segment(["In", "the", "beginning"], 2),
            Segment(["created"], 3)));

        outcome.AlreadyLoaded.Should().BeTrue();
        outcome.StrongNumbers.Should().Be(3);
        (await Numbers()).Should().Equal("H7225", "H2", "H3", "H4");
    }

    private async Task<List<string?>> Numbers()
    {
        _db.ChangeTracker.Clear();
        return await _db.Words
            .Where(w => w.TextId == _bhsa.Id)
            .OrderBy(w => w.Position)
            .Select(w => w.StrongNumber)
            .ToListAsync();
    }

    /// <summary>A verse 0 in BHSA's first chapter, the frame's place for a superscription.</summary>
    private int Superscribe(params string[] words)
    {
        var first = _db.VerseAt(_bhsa, 1, 1);
        var verse = new Verse
        {
            TextId = _bhsa.Id,
            BookId = first.BookId,
            ChapterId = first.ChapterId,
            ChapterNumber = 1,
            Number = 0,
        };
        _db.Verses.Add(verse);
        _db.VerseReferences.Add(new VerseReference
        {
            Verse = verse, CanonicalBook = 1, CanonicalChapter = 1, CanonicalVerse = 0, IsPrimary = true,
        });
        for (var position = 0; position < words.Length; position++)
        {
            _db.Words.Add(new Word
            {
                TextId = _bhsa.Id,
                Verse = verse,
                Position = position + 1,
                Surface = words[position],
                Trailer = " ",
            });
        }

        _db.SaveChanges();
        return verse.Id;
    }

    private void Gloss(params string[] glosses)
    {
        for (var position = 0; position < glosses.Length; position++)
        {
            _db.WordAt(_bhsa, 1, 1, position + 1).Gloss = glosses[position];
        }

        _db.SaveChanges();
    }

    private async Task<LinkOutcome> Load(params MappingRecord[] records)
    {
        Place(_kjv);
        Place(_bhsa);
        _db.SaveChanges();
        _db.ChangeTracker.Clear();

        return await new OldTestamentLinkLoader(_db, NullLogger<OldTestamentLinkLoader>.Instance).Load(records);
    }

    private void Place(Text text)
    {
        var verse = _db.VerseAt(text, 1, 1);
        if (_db.VerseReferences.Any(r => r.VerseId == verse.Id))
        {
            return;
        }

        _db.VerseReferences.Add(new VerseReference
        {
            VerseId = verse.Id, CanonicalBook = 1, CanonicalChapter = 1, CanonicalVerse = 1, IsPrimary = true,
        });
    }

    private static MappingRecord Record(params EnglishSegment[] segments) =>
        new(1, 1, 1, [Hebrew(1), Hebrew(2), Hebrew(3), Hebrew(4)], segments);

    /// <summary>Positions are the file's running word index; within a verse they are consecutive.</summary>
    private static HebrewEntry Hebrew(int position) => new($"H{position}", "c1", position, $"gloss{position}");

    private static EnglishSegment Segment(string[] words, int rendersPosition) =>
        new([.. words.Select(word => new EnglishWord(word, false))], Hebrew(rendersPosition));

    private async Task<List<(List<string> English, List<string> Hebrew, LinkRelation Relation)>> Links()
    {
        var links = await _db.Links.OrderBy(l => l.Id).ToListAsync();
        var members = await _db.LinkWords.Include(w => w.Word).ToListAsync();

        return links
            .Select(link => (
                members.Where(m => m.LinkId == link.Id && m.Side == LinkSide.From)
                    .Select(m => m.Word!.Surface).ToList(),
                members.Where(m => m.LinkId == link.Id && m.Side == LinkSide.To)
                    .Select(m => m.Word!.Surface).ToList(),
                link.Relation))
            .ToList();
    }
}
