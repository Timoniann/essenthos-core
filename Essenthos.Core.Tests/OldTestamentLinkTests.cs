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
    /// The file writes a name whole where the King James hyphenates it, and its apostrophe straight
    /// where the text's is curly: <em>Bathsheba</em> for <em>Bath-sheba</em> at the head of Psalm 51.
    /// Read as different words, the verse was refused whole.
    /// </summary>
    [Fact]
    public async Task AWordTheFileSpellsWithoutItsHyphenOrWithAStraightApostropheIsTheSameWord()
    {
        foreach (var word in _db.Words.Where(w => w.TextId == _kjv.Id && w.Position >= 3))
        {
            word.Surface = word.Position == 3 ? "be-ginning" : "creat’d";
        }
        await _db.SaveChangesAsync();

        var outcome = await Load(Record(
            Segment(["In", "the", "beginning"], 2),
            Segment(["creat'd"], 3)));

        outcome.Refused.Should().Be(0);
        (await Links()).Should().HaveCount(2);
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

        var links = await _db.Links.Include(l => l.Provenance).ToListAsync();
        links.Should().OnlyContain(l => l.Method == LinkMethod.StatedBySource && l.Confidence == null);

        // Who stated it, not which file it arrived in. This asserted the filename until the
        // mapping's terms were read: the source is CC BY-NC and requires attribution, and a path
        // attributes nobody.
        links.Should().OnlyContain(l => l.Provenance!.Source.Contains("Eliran Wong") && l.Provenance!.Source.Contains("CC BY-NC"));
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
            Segment(["In", "a", "start"], 2),
            Segment(["made"], 3)));

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

    /// <summary>
    /// Two verses the file divides differently from BHSA: neither joins by itself, and the file's
    /// running word numbers between the verses on either side still say which number each word bears.
    /// </summary>
    [Fact]
    public async Task AVerseTheFileDividesDifferentlyIsNumberedByItsRunningWordNumbers()
    {
        Divided(["ב", "ר", "א"], ["ת", "ה"]);

        await Load(Records((1, [1, 2]), (2, [3, 4]), (3, [5, 6, 7]), (4, [8, 9])));

        (await Numbers()).Should().Equal("H1", "H2", "H3", "H4", "H5", "H6", "H7", "H8", "H9");
    }

    /// <summary>
    /// A word the file counts as one and BHSA as two leaves the stretch a word longer than its
    /// numbers: the words whose glosses pair up in order are numbered, and the extra word is not.
    /// </summary>
    [Fact]
    public async Task AWordBhsaSplitsIsLeftWithoutANumberAndTheRestOfTheStretchIsNumbered()
    {
        Divided(["ב", "ר", "א", Split], ["ת", "ה"]);

        await Load(Records((1, [1, 2]), (2, [3, 4]), (3, [5, 6, 7]), (4, [8, 9])));

        (await Numbers()).Should().Equal("H1", "H2", "H3", "H4", "H5", null, "H6", "H7", "H8", "H9");
    }

    /// <summary>The half of a word BHSA writes as two and the file as one, glossed as neither half.</summary>
    private const string Split = "נ";

    /// <summary>
    /// Psalm 51's title, which the Hebrew numbers as two verses at the frame's one address. A corpus
    /// loaded while their words were read by position alone has them interleaved — the word for a
    /// psalm with David's number, David with the verb's — and the next load gives each word the
    /// number the file states for it, takes back what the resolution named from the old number, and
    /// leaves a ruling alone. A third load finds nothing to do.
    /// </summary>
    [Fact]
    public async Task ATitleTheHebrewNumbersAsTwoVersesIsNumberedAgainWhereAnEarlierLoadReadItInterleaved()
    {
        Titled();
        var record = new MappingRecord(1, 1, 1,
        [
            new("H9005", "c1", 1, "to"), new("H4210", "c1", 2, "psalm"), new("H1732", "c1", 3, "David"),
            new("H9003", "c2", 4, "in"), new("H935", "c2", 5, "come"), new("H5416", "c2", 6, "Nathan"),
            new("H2603", "c3", 7, "favour"),
        ],
        [Segment(["Psalm"], 2), Segment(["Nathan"], 6), Segment(["mercy"], 7)]);
        await Load(record);
        (await Numbers()).Should().Equal("H9005", "H4210", "H1732", "H9003", "H935", "H5416", "H2603");

        await Interleave();
        var david = new Entity { Kind = EntityKind.Person, Slug = "david", Name = "David", SourceId = "test:david", Source = "test" };
        _db.Entities.Add(david);
        var psalm = _db.WordAt(_bhsa, 1, 1, 2);
        var english = _db.WordAt(_kjv, 1, 1, 1);
        var name = _db.WordAt(_bhsa, 1, 1, 3);
        _db.WordEntities.AddRange(
            new WordEntity { Word = psalm, Entity = david, Method = LinkMethod.StrongNumber, Confidence = 0.99, Source = Resolution },
            new WordEntity
            {
                Word = english, Entity = david, Method = LinkMethod.StrongNumber, Confidence = 0.99, Source = Resolution,
                Note = $"through BHSA word {psalm.Id}, linked by stated-by-source",
            },
            new WordEntity { Word = name, Entity = david, Method = LinkMethod.Manual, Source = "a ruling" });
        await _db.SaveChangesAsync();

        var outcome = await Load(record);

        outcome.AlreadyLoaded.Should().BeTrue();
        outcome.StrongNumbers.Should().Be(4);
        (await Numbers()).Should().Equal("H9005", "H4210", "H1732", "H9003", "H935", "H5416", "H2603");
        (await _db.WordEntities.AsNoTracking().Select(a => a.Source).ToListAsync()).Should().Equal("a ruling");
        (await Load(record)).StrongNumbers.Should().Be(0);
    }

    /// <summary>
    /// Psalm 60's title: the file gives the Valley of Salt, one word in BHSA, two entries under one
    /// running number, and Aram-naharaim, two words in BHSA, one. The counts agree by accident and a
    /// join by position gave every word between the two its neighbour's number — Joab the
    /// conjunction's, the valley salt's. The verse is numbered by the running numbers and the glosses
    /// instead: each word whose gloss pairs takes its own number, and the two words the file and BHSA
    /// divide differently take none. A corpus loaded with the shifted numbers is given the same ones.
    /// </summary>
    [Fact]
    public async Task AVerseTheFileNumbersTwoEntriesAsOneWordInIsNotNumberedByPosition()
    {
        Clear();
        _kjv = Corpus.Add(_db, "KJV", TextKind.Translation, "eng", (1, 1, ["a"]), (1, 2, ["b"]), (1, 3, ["c"]));
        _bhsa = Corpus.Add(_db, "BHSA", TextKind.ManuscriptTradition, "hbo",
            (1, 1, ["א", "ב"]),
            (1, 2, ["לַ", "מְנַצֵּחַ", "אֲרַם", "נַהֲרַיִם", "וַ", "יֹואָב", "בְּ", "גֵיא־מֶלַח", "שְׁנֵים", "עָשָׂר", "אָלֶף", "אֱלֹהִים", "זְנַחְתָּנוּ", "פְרַצְתָּנוּ"]),
            (1, 3, ["ג", "ד"]));
        _db.SaveChanges();
        string[] glosses =
        [
            "gloss1", "gloss2", "to", "prevail", "Aram", "stream", "and", "Joab", "in", "the Valley of Salt", "two",
            "-teen", "thousand", "god(s)", "reject", "break", "gloss16", "gloss17",
        ];
        var words = _db.Words.Where(w => w.TextId == _bhsa.Id).OrderBy(w => w.Id).ToList();
        for (var i = 0; i < words.Count; i++)
        {
            words[i].Gloss = glosses[i];
        }

        _db.SaveChanges();
        MappingRecord[] records =
        [
            new(1, 1, 1, [new("H1", "c1", 1, "gloss1"), new("H2", "c1", 2, "gloss2")], [Segment(["a"], 1)]),
            new(1, 1, 2,
            [
                new("H9005", "c2", 3, "to"), new("H5329", "c2", 4, "prevail"), new("H763", "c2", 5, "Mesopotamia"),
                new("H9000", "c2", 6, "and"), new("H3097", "c2", 7, "Joab"), new("H9003", "c2", 8, "in"),
                new("H1516", "c2", 9, "the Valley of Salt"), new("H4417", "c2", 9, "the Valley of Salt"),
                new("H8147", "c2", 10, "two"), new("H6240", "c2", 11, "-teen"), new("H505", "c2", 12, "thousand"),
                new("H430", "c2", 13, "god(s)"), new("H2186", "c2", 14, "reject"), new("H6555", "c2", 15, "break"),
            ],
            [Segment(["b"], 3)]),
            new(1, 1, 3, [new("H16", "c3", 16, "gloss16"), new("H17", "c3", 17, "gloss17")], [Segment(["c"], 16)]),
        ];
        string?[] expected =
        [
            "H1", "H2", "H9005", "H5329", null, null, "H9000", "H3097", "H9003", "H1516", "H8147", "H6240", "H505",
            "H430", "H2186", "H6555", "H16", "H17",
        ];

        await Load(records);
        (await Numbers()).Should().Equal(expected);

        await _db.Database.ExecuteSqlRawAsync(
            """
            UPDATE word w SET strong_number = shifted.number
            FROM (VALUES (5, 'H763'), (6, 'H9000'), (7, 'H3097'), (8, 'H9003'), (9, 'H1516'), (10, 'H4417'))
                 AS shifted(position, number), verse v
            WHERE v.id = w.verse_id AND v.number = 2 AND w.text_id = {0} AND w.position = shifted.position - 2
            """,
            _bhsa.Id);

        var outcome = await Load(records);

        outcome.StrongNumbers.Should().Be(6);
        (await Numbers()).Should().Equal(expected);
    }

    private const string Resolution =
        "BHSA's proper-noun marking, and the Strong number the encyclopedia records for the name";

    /// <summary>
    /// BHSA's Psalm 51 title and first verse: the title as the two verses BHSA numbers it, both at the
    /// frame's verse 0, and the King James's first verse, which holds the title.
    /// </summary>
    private void Titled()
    {
        Clear();
        _kjv = Corpus.Add(_db, "KJV", TextKind.Translation, "eng", (1, 1, ["Psalm", "Nathan", "mercy"]));
        _bhsa = Corpus.Add(_db, "BHSA", TextKind.ManuscriptTradition, "hbo",
            (1, 1, ["לַ", "מִזְמֹור", "דָוִד"]), (1, 2, ["בְּ", "בֹוא", "נָתָן"]), (1, 3, ["חָנֵּנִי"]));
        _db.SaveChanges();
        _db.Database.ExecuteSqlRaw(
            """
            UPDATE verse SET sequence = number WHERE text_id = {0};
            UPDATE verse_reference r SET canonical_verse = CASE v.number WHEN 3 THEN 1 ELSE 0 END
            FROM verse v WHERE v.id = r.verse_id AND v.text_id = {0}
            """,
            _bhsa.Id);
    }

    /// <summary>The title's numbers as a load reading its two verses by position alone wrote them.</summary>
    private async Task Interleave() =>
        await _db.Database.ExecuteSqlRawAsync(
            """
            UPDATE word w SET strong_number = interleaved.number
            FROM (VALUES (1, 1, 'H9005'), (2, 1, 'H4210'), (1, 2, 'H1732'), (2, 2, 'H9003'), (1, 3, 'H935'), (2, 3, 'H5416'))
                 AS interleaved(verse, position, number), verse v
            WHERE v.id = w.verse_id AND v.number = interleaved.verse AND w.position = interleaved.position
              AND w.text_id = {0}
            """,
            _bhsa.Id);

    /// <summary>
    /// BHSA's Genesis 1:1-4 with verses 2 and 3 of the lengths given, every word glossed as the file
    /// glosses its running number, and the King James beside it.
    /// </summary>
    private void Divided(string[] second, string[] third)
    {
        Clear();
        _kjv = Corpus.Add(_db, "KJV", TextKind.Translation, "eng",
            (1, 1, ["a"]), (1, 2, ["b"]), (1, 3, ["c"]), (1, 4, ["d"]));
        _bhsa = Corpus.Add(_db, "BHSA", TextKind.ManuscriptTradition, "hbo",
            (1, 1, ["א", "ב"]), (1, 2, second), (1, 3, third), (1, 4, ["ו", "ז"]));
        _db.SaveChanges();
        var running = 0;
        foreach (var word in _db.Words.Where(w => w.TextId == _bhsa.Id).OrderBy(w => w.Id).ToList())
        {
            word.Gloss = word.Surface == Split ? "split" : $"gloss{++running}";
        }

        _db.SaveChanges();
    }

    private static MappingRecord[] Records(params (int Verse, int[] Positions)[] verses) =>
    [
        .. verses.Select(verse => new MappingRecord(1, 1, verse.Verse,
            [.. verse.Positions.Select(Hebrew)], [Segment(["x"], verse.Positions[0])])),
    ];

    private async Task<List<string?>> Numbers()
    {
        _db.ChangeTracker.Clear();
        return await _db.Words
            .Where(w => w.TextId == _bhsa.Id)
            .OrderBy(w => w.Id)
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
