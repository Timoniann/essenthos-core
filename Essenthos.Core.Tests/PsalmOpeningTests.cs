using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Loading;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Essenthos.Core.Tests;

/// <summary>
/// Writing the head of a psalm into a text that was loaded without it.
///
/// The corpus here is Psalm 3 in miniature: a Hebrew text whose first verse is the title and whose
/// second is the body, and a translation that prints the body alone. Everything the change has to be
/// true of is visible in it — the words landing before what was already there, the verse coming to
/// stand at the title address as well as its own, and a second run costing nothing.
/// </summary>
[Collection(WitnessDatabaseCollection.Name)]
public sealed class PsalmOpeningTests : IDisposable
{
    private const int Psalms = 19;

    private readonly AppDbContext _db;
    private readonly PsalmOpeningLoader _loader;

    public PsalmOpeningTests(WitnessDatabase database)
    {
        _db = database.NewContext();
        Clear();
        _loader = new PsalmOpeningLoader(_db, NullLogger<PsalmOpeningLoader>.Instance);
    }

    public void Dispose()
    {
        Clear();
        _db.Dispose();
    }

    /// <summary>
    /// The loader opens its own transaction, so an outer one would not be the thing under test.
    /// Everything under a text goes with the text.
    /// </summary>
    private void Clear() => _db.Database.ExecuteSqlRaw("DELETE FROM text");

    [Fact]
    public async Task TheTitleIsWrittenBeforeTheWordsTheVerseAlreadyHad()
    {
        var english = Translation("KJV", (3, 1, ["Lord,", "how"]));
        NumberTheTitleApart(Hebrew(), chapter: 3);

        var outcome = await _loader.Load("KJV", [Title(3, "A", "Psalm")], "from somewhere");

        outcome.Psalms.Should().Be(1);
        outcome.Words.Should().Be(2);
        Surfaces(english, 3, 1).Should().Equal("A", "Psalm", "Lord,", "how");
    }

    /// <summary>
    /// The verse holds a title and a body where the Hebrew holds two verses, so it stands at both
    /// addresses. That is what makes the title words reachable from the Hebrew title verse at all.
    /// </summary>
    [Fact]
    public async Task TheVerseComesToStandAtTheTitleAddressAsWell()
    {
        var english = Translation("KJV", (3, 1, ["Lord,", "how"]));
        NumberTheTitleApart(Hebrew(), chapter: 3);

        var outcome = await _loader.Load("KJV", [Title(3, "A", "Psalm")], "from somewhere");

        outcome.Placed.Should().Be(1);
        Addresses(english, 3, 1).Should().BeEquivalentTo([(0, false), (1, true)]);
    }

    /// <summary>
    /// For 53 of the 116 psalms the King James gives a title, the Hebrew keeps that title inside
    /// its own first verse and the frame has no row above it. The words still belong in the verse;
    /// there is simply nothing for it to also stand at.
    /// </summary>
    [Fact]
    public async Task APsalmTheFrameHasNoTitleVerseForGetsTheWordsAndNoSecondAddress()
    {
        var english = Translation("KJV", (3, 1, ["Lord,", "how"]));
        Hebrew();

        var outcome = await _loader.Load("KJV", [Title(3, "A", "Psalm")], "from somewhere");

        outcome.Psalms.Should().Be(1);
        outcome.Placed.Should().Be(0);
        Surfaces(english, 3, 1).Should().Equal("A", "Psalm", "Lord,", "how");
        Addresses(english, 3, 1).Should().BeEquivalentTo([(1, true)]);
    }

    /// <summary>
    /// Ohienko's case: the verse holds the title and the line that followed it was lost, so the
    /// words go after what is there rather than before it.
    /// </summary>
    [Fact]
    public async Task ALineTheDigitisationLostIsWrittenAfterTheTitleItFollowed()
    {
        var ukrainian = Translation("UBIO", (3, 1, ["Псалом", "Давидів."]));
        NumberTheTitleApart(Hebrew(), chapter: 3);

        var outcome = await _loader.Load(
            "UBIO",
            [new PsalmOpening(3, PsalmOpeningPlace.AfterTheVerse, Words("Господи,", "Боже"))],
            "restored from a complete copy");

        outcome.Words.Should().Be(2);
        Surfaces(ukrainian, 3, 1).Should().Equal("Псалом", "Давидів.", "Господи,", "Боже");
        Addresses(ukrainian, 3, 1).Should().BeEquivalentTo([(0, false), (1, true)]);
    }

    /// <summary>
    /// It runs on every start against a database it has already written to, so a second run has to
    /// cost nothing rather than write the title twice.
    /// </summary>
    [Fact]
    public async Task ASecondRunWritesNothing()
    {
        var english = Translation("KJV", (3, 1, ["Lord,", "how"]));
        NumberTheTitleApart(Hebrew(), chapter: 3);

        await _loader.Load("KJV", [Title(3, "A", "Psalm")], "from somewhere");
        var again = await _loader.Load("KJV", [Title(3, "A", "Psalm")], "from somewhere");

        again.Psalms.Should().Be(0);
        again.Placed.Should().Be(0);
        Surfaces(english, 3, 1).Should().Equal("A", "Psalm", "Lord,", "how");
    }

    /// <summary>
    /// CC BY-SA asks that a modification be indicated by whoever passes the text on, and RUL-0181
    /// asks for the source to be named whatever the licence says. The text's own row is where a
    /// reader of this corpus is told.
    /// </summary>
    [Fact]
    public async Task TheTextSaysOnItsOwnRowWhereTheWordsCameFrom()
    {
        Translation("KJV", (3, 1, ["Lord,", "how"]));
        Hebrew();

        await _loader.Load("KJV", [Title(3, "A", "Psalm")], "Taken from the 1769 text.");
        await _loader.Load("KJV", [Title(3, "A", "Psalm")], "Taken from the 1769 text.");

        _db.Texts.Single(t => t.Slug == "KJV").RightsNote
            .Should().Be("Taken from the 1769 text.");
    }

    /// <summary>
    /// A psalm the loaded text does not carry is the two editions disagreeing about which psalm is
    /// which, and writing the words anywhere would be a guess about that.
    /// </summary>
    [Fact]
    public async Task APsalmTheTextDoesNotCarryStopsTheLoad()
    {
        Translation("KJV", (3, 1, ["Lord,", "how"]));

        var writing = () => _loader.Load("KJV", [Title(9, "A", "Psalm")], "from somewhere");

        await writing.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Psalm 9:1*");
    }

    /// <summary>
    /// The real thing, at its real size: the King James this corpus serves, loaded from bible4u's
    /// file, with the 116 superscriptions eBible's 1769 text prints written into it. What it asks
    /// is the whole claim of PRB-0225 — that the psalm a reader opens now reads as the King James
    /// prints it — and it asks it of the files rather than of a fixture.
    /// </summary>
    [Fact]
    public async Task ThePsalmsOfTheLoadedKingJamesComeToReadAsTheKingJamesPrintsThem()
    {
        if (!Directory.Exists(TestResources.KingJames2006Folder) ||
            !File.Exists(TestResources.Bible4u("KJV")))
        {
            return;
        }

        var loaded = Bible4uTextSource.Read(TestResources.Bible4u("KJV"), "KJV");
        var psalter = new TextSource(
            loaded.Definition,
            [.. loaded.Books.Where(book => book.CanonicalOrdinal == Psalms)]);
        await new CorpusLoader(_db, NullLogger<CorpusLoader>.Instance).Load(psalter);
        PlaceAtTheirOwnAddresses();

        var outcome = await _loader.Load(
            "KJV",
            LostPsalmOpenings.KingJamesSuperscriptions(TestResources.KingJames2006Folder),
            "from the 1769 text");

        outcome.Psalms.Should().Be(116);
        outcome.Words.Should().Be(1034);
        Verse(3, 1).Should().StartWith("A Psalm of David, when he fled from Absalom his son. Lord,");
        Verse(51, 1).Should().StartWith(
            "To the chief Musician, A Psalm of David, when Nathan the prophet came unto him, after he " +
            "had gone in to Bath-sheba. Have mercy upon me");

        // The reference the encyclopedia states at Psalm 60:1 names a place the psalm prints in its
        // title alone, and until now no verse of this text held the word.
        Verse(60, 1).Should().Contain("Aram-naharaim");
    }

    /// <summary>
    /// The same for Ohienko, where what was lost is not a title but a line of the psalm: his Psalm
    /// 7 has eighteen verses and the file loaded here has seventeen, because the digitisation it
    /// belongs to dropped the second. PRB-0298.
    /// </summary>
    [Fact]
    public async Task OhienkoPsalmSevenComesToHoldTheLineHisPrintingHas()
    {
        if (!Directory.Exists(TestResources.OhienkoWikisourceFolder) ||
            !File.Exists(TestResources.Bible4u("UKR")))
        {
            return;
        }

        var loaded = Bible4uTextSource.Read(TestResources.Bible4u("UKR"), "UKR");
        await new CorpusLoader(_db, NullLogger<CorpusLoader>.Instance).Load(new TextSource(
            loaded.Definition,
            [.. loaded.Books.Where(book => book.CanonicalOrdinal == Psalms)]));
        PlaceAtTheirOwnAddresses();

        var outcome = await _loader.Load(
            Bible4uTextSource.Ohienko,
            LostPsalmOpenings.OhienkoLostLine(TestResources.OhienkoWikisourceFolder),
            "restored from the Wikisource transcription");

        outcome.Psalms.Should().Be(1);
        Verse(7, 1).Should().Be(
            "Жалібна пісня Давидова, яку він співав Господеві в справі веніямінівця Куща. " +
            "Господи, Боже мій, — я до Тебе вдаюся: спаси Ти мене від усіх моїх напасників, і визволь мене, ");
    }

    private void PlaceAtTheirOwnAddresses()
    {
        foreach (var verse in _db.Verses.Where(v => v.Book!.CanonicalOrdinal == Psalms).ToList())
        {
            _db.VerseReferences.Add(new VerseReference
            {
                VerseId = verse.Id,
                CanonicalBook = Psalms,
                CanonicalChapter = verse.ChapterNumber,
                CanonicalVerse = verse.Number,
                IsPrimary = true,
            });
        }

        _db.SaveChanges();
    }

    private string Verse(int chapter, int number) => string.Concat(_db.Words
        .Where(w => w.Verse!.ChapterNumber == chapter && w.Verse.Number == number
                    && w.Verse.Book!.CanonicalOrdinal == Psalms)
        .OrderBy(w => w.Position)
        .Select(w => w.Surface + w.Trailer));

    private Text Translation(string slug, params (int Chapter, int Verse, string[] Words)[] verses)
    {
        var text = Corpus.Add(_db, slug, TextKind.Translation, "eng", verses);
        _db.SaveChanges();
        return _db.In(text, Psalms);
    }

    /// <summary>The Hebrew, whose first verse is the title and whose second is the body.</summary>
    private Text Hebrew()
    {
        var text = Corpus.Add(_db, "BHSA", TextKind.ManuscriptTradition, "hbo",
            (3, 1, ["מִזְמֹור"]),
            (3, 2, ["יְהוָה"]));
        _db.SaveChanges();
        return _db.In(text, Psalms);
    }

    private void NumberTheTitleApart(Text hebrew, int chapter)
    {
        foreach (var reference in _db.VerseReferences
                     .Where(r => r.Verse!.TextId == hebrew.Id && r.Verse.ChapterNumber == chapter))
        {
            reference.CanonicalVerse--;
        }

        _db.SaveChanges();
    }

    private static PsalmOpening Title(int psalm, params string[] words) =>
        new(psalm, PsalmOpeningPlace.BeforeTheVerse, Words(words));

    private static IReadOnlyList<WordDraft> Words(params string[] words) =>
        [.. words.Select(word => new WordDraft(word, " "))];

    private List<string> Surfaces(Text text, int chapter, int verse) =>
    [
        .. _db.Words
            .Where(w => w.TextId == text.Id && w.Verse!.ChapterNumber == chapter && w.Verse.Number == verse)
            .OrderBy(w => w.Position)
            .Select(w => w.Surface),
    ];

    private List<(int Verse, bool Primary)> Addresses(Text text, int chapter, int verse) =>
    [
        .. _db.VerseReferences
            .Where(r => r.Verse!.TextId == text.Id && r.Verse.ChapterNumber == chapter && r.Verse.Number == verse)
            .Select(r => new { r.CanonicalVerse, r.IsPrimary })
            .AsEnumerable()
            .Select(r => (r.CanonicalVerse, r.IsPrimary)),
    ];
}
