using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Loading;
using Essenthos.Core.Loading.Encyclopedia;
using Essenthos.Core.Loading.Links;
using Essenthos.Core.Swete;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using Xunit.Abstractions;

namespace Essenthos.Core.Tests;

/// <summary>The words Swete printed and the transcription lost, put back where the witnesses agree.</summary>
[Trait(TestCategory.Name, TestCategory.Corpus)]
public class SweteRestorationTests(Swete swete) : IClassFixture<Swete>
{
    private const int Genesis = 1;

    [Theory]
    [InlineData(9, 28, "Ἔζησεν δὲ Νῶε μετὰ τὸν κατακλυσμὸν τριακόσια πεντήκοντα ἔτη.")]
    [InlineData(11, 22, "Καὶ ἔζησεν Σερούχ ἑκατὸν τριάκοντα ἴτη, καὶ ἐγέννησεν τὸν Ναχώρ.")]
    [InlineData(11, 25, "καὶ ἔζησεν Ναχὼρ μετὰ τὸ γεννῆσαι αὐτὸν τὸν Θαρά ἴτη ἑκατὸν εἴκοσι ἐννέα, καὶ ἐγέννησεν υἱοὺς καὶ θυγατέρας, καὶ ἀπέθανεν.")]
    [InlineData(12, 4, "καὶ ἐπορεύθη Ἀβρὰμ καθάπερ ἐλάλησεν αὐτῷ κύριος, καὶ ᾤχετο μετ’ αὐτοῦ Λώτ· Ἀβρὰμ δὲ ἦν ἐτῶν ἑβδομήκοντα πέντε ὅτε ἐξῆλθεν ἐκ Χαρράν.")]
    public void TheVerseReadsAsSwetePrintedIt(int chapter, int verse, string printed) =>
        Swete.Text(swete.Verse(Genesis, chapter, verse)).Should().Be(printed);

    /// <summary>
    /// With the lost words back, Genesis can be read from Swete alone: every number is found, every
    /// life of chapter 5 closes and Noah's does too, which the transcription's three hundred did not.
    /// Codex Alexandrinus then reads as it is known to — Methuselah at 187, Arphaxad's 430, Eber's 370,
    /// Nahor begetting at 79 and living 129 after.
    /// </summary>
    [Fact]
    public void GenesisReadsWholeFromSwete()
    {
        var read = SeptuagintReckoning.Read(Genesis5To12());

        read.Problems.Should().BeEmpty();
        read.Values["noah-after"].Value.Should().Be(350);
        read.Values["abram-leaves-haran"].Value.Should().Be(75);
        read.Values["methuselah-begets"].Value.Should().Be(187);
        read.Values["arphaxad-after"].Value.Should().Be(430);
        read.Values["eber-after"].Value.Should().Be(370);
        read.Values["nahor-begets"].Value.Should().Be(79);
        read.Values["nahor-after"].Value.Should().Be(129);
    }

    /// <summary>
    /// What reading all of it from Swete does to the Alexandrinus reckoning: Nahor's hundred years
    /// fewer bring Abram a century nearer the Flood than Brenton's numbers would with Methuselah's alone.
    /// </summary>
    [Fact]
    public void ReadWholeFromSweteAbramIsBornACenturyEarlier()
    {
        var years = SeptuagintReckoning.Compute(SeptuagintReckoning.Read(Genesis5To12()).Values, id => id);

        years["Begin_Flood"].Year.Should().Be(2263);
        years[SeptuagintReckoning.AbramBorn].Year.Should().Be(3396);
    }

    [Fact]
    public void ARestorationThatNoLongerFindsItsWordsStopsTheRead()
    {
        var act = () => SweteRestorations.Apply("01.Genesis", ["1.9.28 τριακόσια", "1.9.28 πεντήκοντα"]).ToList();

        act.Should().Throw<InvalidOperationException>().WithMessage("*9:28*");
    }

    [Fact]
    public void EveryOtherLinePassesThroughUnchanged()
    {
        string[] lines = ["40.1.1 Ὅρασις", "40.1.1 Ἀβδειού.", "40.1.2 ἰδοὺ"];

        SweteRestorations.Apply("40.Abdias", lines).Should().Equal(lines);
    }

    /// <summary>
    /// Brenton for Exodus and Kings, as the reckoning reads them; Genesis wholly from Swete.
    /// </summary>
    private Func<int, int, int, string?> Genesis5To12()
    {
        var brenton = SeptuagintTextSource.Read(TestResources.SeptuagintFolder);
        return (book, chapter, verse) =>
        {
            var source = book == Genesis ? swete.Source : brenton;
            var draft = source.Books.Single(b => b.CanonicalOrdinal == book)
                .Chapters.SingleOrDefault(c => c.Number == chapter)?
                .Verses.FirstOrDefault(v => v.Number == verse && v.Label.Length == 0);
            return draft is null ? null : string.Join(' ', draft.Words.Select(word => word.Surface));
        };
    }
}

/// <summary>The same words written into a Swete loaded before they were restored.</summary>
[Trait(TestCategory.Name, TestCategory.Corpus)]
[Collection(WitnessDatabaseCollection.Name)]
public sealed class SweteRestorationLoadTests : IDisposable
{
    private const string Genesis = "01.Genesis";

    private readonly AppDbContext _db;
    private readonly SweteRestorationLoader _loader;
    private readonly SweteBook _digitised;
    private readonly SweteBook _marked;
    private readonly SweteBook _restored;

    public SweteRestorationLoadTests(WitnessDatabase database)
    {
        _db = database.NewContext();
        Clear();
        _loader = new SweteRestorationLoader(_db, NullLogger<SweteRestorationLoader>.Instance);

        var path = Path.Combine(TestResources.SweteFolder, SweteTextSource.FileName(Genesis));
        _digitised = SweteReader.Read(File.ReadLines(path), keepChapterMarkers: true);
        _marked = SweteReader.Read(SweteRestorations.Apply(Genesis, File.ReadLines(path)), keepChapterMarkers: true);
        _restored = SweteReader.Read(SweteRestorations.Apply(Genesis, File.ReadLines(path)));
    }

    public void Dispose()
    {
        Clear();
        _db.Dispose();
    }

    private void Clear() => _db.Database.ExecuteSqlRaw("DELETE FROM text");

    private static IEnumerable<(int Chapter, int Verse)> Restored =>
        SweteRestorations.All.Where(r => r.Book == Genesis).Select(r => (r.Chapter, r.Verse)).Distinct();

    private static IReadOnlyList<SweteWord> Words(SweteBook book, int chapter, int verse) =>
        book.Chapters.Single(c => c.Number == chapter).Verses.Single(v => v.Number == verse && v.Label.Length == 0).Words;

    private Text Loaded(SweteBook book)
    {
        var text = Corpus.Add(_db, SweteTextSource.Slug, TextKind.CriticalEdition, "grc",
            [.. Restored.Select(at => (at.Chapter, at.Verse,
                Words(book, at.Chapter, at.Verse).Select(w => w.Surface).ToArray()))]);
        _db.SaveChanges();

        foreach (var (chapter, verse) in Restored)
        {
            var words = Words(book, chapter, verse);
            for (var at = 0; at < words.Count; at++)
            {
                _db.WordAt(text, chapter, verse, at + 1).Trailer = words[at].Trailer;
            }
        }

        text.RightsNote = "CC BY-SA 4.0.";
        _db.SaveChanges();
        return text;
    }

    private string Read(Text text, int chapter, int verse) =>
        string.Concat(_db.Words.AsNoTracking()
            .Where(w => w.TextId == text.Id && w.Verse!.ChapterNumber == chapter && w.Verse.Number == verse)
            .OrderBy(w => w.Position)
            .Select(w => w.Surface + w.Trailer)
            .ToList());

    private static string Expected(SweteBook book, int chapter, int verse) =>
        string.Concat(Words(book, chapter, verse).Select(w => w.Surface + w.Trailer));

    [Fact]
    public async Task EveryVerseReadsAsRestoredAndSaysSo()
    {
        var text = Loaded(_digitised);

        var outcome = await _loader.Load(TestResources.SweteFolder);

        outcome.Verses.Should().Be(Restored.Count());
        foreach (var (chapter, verse) in Restored)
        {
            // The chapter numbers stay for the edition boundary pass to take out.
            Read(text, chapter, verse).Should().Be(Expected(_marked, chapter, verse), $"{chapter}:{verse}");
        }

        var row = await _db.Texts.AsNoTracking().SingleAsync(t => t.Id == text.Id);
        row.RightsNote.Should().Be(
            $"CC BY-SA 4.0. {SweteRestorations.Note} {SweteCorrections.Note} {SwetePage.Note} {SweteCorrections.FiguresNote} "
            + SweteSettled.Note);
        var restored = await _db.Words.AsNoTracking().SingleAsync(w => w.TextId == text.Id && w.Surface == "πεντήκοντα"
                                                                       && w.Verse!.ChapterNumber == 9);
        restored.NormalisedText.Should().Be("πεντηκοντα", "a word written here is searchable at once");
    }

    [Fact]
    public async Task ASecondRunFindsNothingToDo()
    {
        Loaded(_digitised);
        await _loader.Load(TestResources.SweteFolder);

        var again = await _loader.Load(TestResources.SweteFolder);

        again.Verses.Should().Be(0);
    }

    [Fact]
    public async Task ACorpusLoadedFromTheRestoredReaderIsLeftAlone()
    {
        Loaded(_restored);

        (await _loader.Load(TestResources.SweteFolder)).Verses.Should().Be(0);
    }

    /// <summary>
    /// A word both readings hold keeps its row and its link; a misread token that goes takes the
    /// matcher's link it stood in alone with it, rather than leaving a link that names words on one
    /// side only.
    /// </summary>
    [Fact]
    public async Task WhatBothReadingsShareKeepsItsLinks()
    {
        var text = Loaded(_digitised);
        var brenton = Corpus.Add(_db, "GRCBRENT", TextKind.CriticalEdition, "grc", (11, 25, ["αὐτὸν"]));
        _db.SaveChanges();

        var kept = _db.WordAt(text, 11, 25, 7);
        var misread = _db.WordAt(text, 11, 25, 9);
        kept.Surface.Should().Be("αὐτὸν");
        misread.Surface.Should().Be("αὐτὸν");
        var equals = Link(text, brenton, LinkRelation.Equals, kept, _db.WordAt(brenton, 11, 25, 1));
        var expands = Link(text, brenton, LinkRelation.Expands, misread, null, SeptuagintLinkLoader.Source);

        await _loader.Load(TestResources.SweteFolder);

        (await _db.Links.AnyAsync(l => l.Id == equals.Id)).Should().BeTrue();
        (await _db.Words.AnyAsync(w => w.Id == kept.Id && w.Position == 7)).Should().BeTrue();
        (await _db.Links.AnyAsync(l => l.Id == expands.Id)).Should().BeFalse();
    }

    /// <summary>
    /// Anything but a matcher's link on a token the restoration replaces is somebody's statement about
    /// it, and the pass stops rather than delete it unread.
    /// </summary>
    [Fact]
    public async Task ATokenOtherEvidenceStandsOnStopsThePass()
    {
        var text = Loaded(_digitised);
        var brenton = Corpus.Add(_db, "GRCBRENT", TextKind.CriticalEdition, "grc", (11, 25, ["αὐτὸν"]));
        _db.SaveChanges();
        var misread = _db.WordAt(text, 11, 25, 9);
        var stated = Link(text, brenton, LinkRelation.Expands, misread, null);
        var before = await _db.Words.AsNoTracking().OrderBy(w => w.Id).Select(w => new { w.Id, w.Position, w.Surface }).ToListAsync();

        var restoring = () => _loader.Load(TestResources.SweteFolder);

        await restoring.Should().ThrowAsync<InvalidOperationException>().WithMessage("*protected*");
        (await _db.Links.AnyAsync(l => l.Id == stated.Id)).Should().BeTrue();
        (await _db.Words.AsNoTracking().OrderBy(w => w.Id).Select(w => new { w.Id, w.Position, w.Surface }).ToListAsync())
            .Should().BeEquivalentTo(before, o => o.WithStrictOrdering());
    }

    /// <summary>
    /// A correction that leaves the verse as many words long rewrites the word in place: it is the
    /// same word the edition prints, so its row, and everything standing on it, stays.
    /// </summary>
    [Fact]
    public async Task ACorrectedLetterKeepsItsRow()
    {
        var text = Loaded(_digitised);
        var numeral = _db.WordAt(text, 32, 1, 1);
        numeral.Surface.Should().Be("XXXIIεἰς");

        await _loader.Load(TestResources.SweteFolder);

        var corrected = await _db.Words.AsNoTracking().SingleAsync(w => w.Id == numeral.Id);
        corrected.Surface.Should().Be("εἰς");
        corrected.Position.Should().Be(1);
        corrected.NormalisedText.Should().Be("εισ");
    }

    private Link Link(Text from, Text to, LinkRelation relation, Word fromWord, Word? toWord, string source = "a test")
    {
        var link = new Link
        {
            FromTextId = from.Id, ToTextId = to.Id, Relation = relation, Method = LinkMethod.Lexical,
            Confidence = 0.9, Provenance = new() { Source = source },
        };
        _db.Links.Add(link);
        _db.LinkWords.Add(new LinkWord { Link = link, Word = fromWord, Side = LinkSide.From });
        if (toWord is not null)
        {
            _db.LinkWords.Add(new LinkWord { Link = link, Word = toWord, Side = LinkSide.To });
        }

        _db.SaveChanges();
        return link;
    }
}

/// <summary>
/// The corrections a rule settles in every book: a Latin letter for the Greek one it looks like, the
/// margin number run into a verse's first word, two words run together.
/// </summary>
[Trait(TestCategory.Name, TestCategory.Corpus)]
public class SweteCorrectionTests(Swete swete) : IClassFixture<Swete>
{
    [Theory]
    [InlineData(1, 9, 1, "Καὶ")]
    [InlineData(19, 1, 1, "ΜΑΚΑΡΙΟΣ")]
    [InlineData(20, 3, 22, "ἔσται")]
    public void TheVerseOpensWithItsWordAndNotItsNumber(int book, int chapter, int verse, string first)
    {
        var words = Words(book, chapter, verse, verse == 22 && book == 20 ? "a" : "");

        words[0].Should().Be(first);
    }

    [Theory]
    [InlineData(1, 40, 12, "’Ιωσήφ")]
    [InlineData(13, 12, 40, "Νεφθαλεὶ")]
    [InlineData(26, 30, 8, "Αἴγυπτον")]
    [InlineData(1, 6, 21, "βρωμάτων")]
    [InlineData(1, 6, 22, "κύριος")]
    public void TheWordReadsInGreekLetters(int book, int chapter, int verse, string word) =>
        Words(book, chapter, verse, "").Should().Contain(word);

    /// <summary>
    /// Where no rule settles a Latin letter the token is left as the transcription reads it: Psalm 63
    /// opens Bἰς where every witness has Εἰς, so the letter on the page is not a B at all.
    /// </summary>
    [Fact]
    public void AMisreadLetterIsLeftAsItIs() =>
        Words(19, 63, 1, "")[0].Should().Be("Bἰς");

    [Fact]
    public void EveryCorrectionChangesSomethingAndSaysWhy()
    {
        SweteCorrections.All.Should().NotBeEmpty();
        SweteCorrections.All.Should().OnlyContain(c => c.Digitised != c.Printed && c.Why.Length > 0);
    }

    [Fact]
    public void NoCorrectionIsMadeInAVerseRestoredByHand()
    {
        var byHand = SweteRestorations.All.Except(SweteCorrections.All).Except(SwetePage.All).Except(SweteSettled.All)
            .Select(r => (r.Book, r.Chapter, r.Verse, r.Label)).ToHashSet();

        SweteCorrections.All.Should().NotContain(c => byHand.Contains(ValueTuple.Create(c.Book, c.Chapter, c.Verse, c.Label)));
    }

    private IReadOnlyList<string> Words(int book, int chapter, int verse, string label) =>
        [.. swete.Book(book).Chapters.Single(c => c.Number == chapter).Verses
            .Single(v => v.Number == verse && v.Label == label).Words.Select(w => w.Surface)];
}

/// <summary>
/// The words read back off the printed page: what the page prints where the transcription has none,
/// and nothing where the page prints the verse as the transcription does.
/// </summary>
[Trait(TestCategory.Name, TestCategory.Corpus)]
public class SwetePageTests(Swete swete) : IClassFixture<Swete>
{
    private const int Genesis = 1;

    [Theory]
    [InlineData(12, 9, "καὶ ἀπῆρεν Ἀβρὰμ καὶ πορευθεὶς ἐστρατοπέδευσεν ἐν τῇ ἐρήμῳ.")]
    [InlineData(13, 4, "εἰς τὸν τόπον τοῦ θυσιαστηρίου οὗ")]
    [InlineData(25, 33, "καὶ εἶπεν αὐτῷ Ἰακώβ Ὄμοσόν μοι σήμερον.")]
    public void TheVerseReadsAsThePagePrintsIt(int chapter, int verse, string printed) =>
        Swete.Text(swete.Verse(Genesis, chapter, verse)).Should().Contain(printed);

    /// <summary>
    /// At 26:1 the page prints ἐγενήθη where Brenton and GLAUx read ἐγένετο: what goes back is the
    /// page's word, not the witnesses'.
    /// </summary>
    [Fact]
    public void ThePagesWordGoesInWhereItIsNotTheWitnesses() =>
        Swete.Text(swete.Verse(Genesis, 26, 1)).Should().Contain("ὃς ἐγενήθη ἐν τῷ χρόνῳ");

    /// <summary>
    /// At 29:25 Brenton and GLAUx read ἐδούλευσα παρὰ σοί, and the page prints ἐδούλευσα σοί as the
    /// transcription does: a reading of the manuscript, left as it is.
    /// </summary>
    [Fact]
    public void AVerseThePagePrintsAsTheTranscriptionDoesIsLeft() =>
        Swete.Text(swete.Verse(Genesis, 29, 25)).Should().Contain("ἐδούλευσα σοί;");

    [Fact]
    public void EveryRestorationAddsWordsAndCitesItsPage()
    {
        SwetePage.All.Should().NotBeEmpty();
        SwetePage.All.Where(r => Words(r.Printed) <= Words(r.Digitised) || !r.Why.Contains("vol.", StringComparison.Ordinal))
            .Should().BeEmpty();
    }

    private static int Words(string tokens) => tokens.Split(' ').Length;
}

/// <summary>
/// Every restoration and correction written into a whole Swete loaded before them, as the corpus
/// this machine holds was: each verse must come out as a cold load reads it.
/// </summary>
[Trait(TestCategory.Name, TestCategory.Corpus)]
[Collection(WitnessDatabaseCollection.Name)]
public sealed class SweteCorrectionLoadTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly ITestOutputHelper _output;

    public SweteCorrectionLoadTests(WitnessDatabase database, ITestOutputHelper output)
    {
        _db = database.NewContext();
        _output = output;
        _db.Database.ExecuteSqlRaw("DELETE FROM text");
    }

    public void Dispose()
    {
        _db.Database.ExecuteSqlRaw("DELETE FROM text");
        _db.Dispose();
    }

    /// <summary>How many words the reader makes of tokens: one with neither a letter nor a figure is punctuation.</summary>
    private static int Words(string tokens) => tokens.Split(' ').Count(token => token.Any(char.IsLetterOrDigit));

    [Fact]
    public async Task AWholeSweteLoadedBeforeThemReadsAsACorpusLoadedAfter()
    {
        await new CorpusLoader(_db, NullLogger<CorpusLoader>.Instance)
            .Load(SweteTextSource.Read(TestResources.SweteFolder, restored: false));
        // As a load does after writing a text: the checks below plan against these statistics.
        await _db.Database.ExecuteSqlRawAsync("ANALYZE");
        var loader = new SweteRestorationLoader(_db, NullLogger<SweteRestorationLoader>.Instance);

        var outcome = await loader.Load(TestResources.SweteFolder);
        _output.WriteLine(outcome.ToString());

        var verses = SweteRestorations.All.Where(r => SweteTextSource.Reads(r.Book))
            .Select(r => (r.Book, r.Chapter, r.Verse, r.Label)).Distinct().ToList();
        outcome.Verses.Should().Be(verses.Count);
        // The Genesis words add 17, which their entries do not count because two of them divide a token.
        outcome.Words.Should().Be(17 + SweteRestorations.All.Except(SweteRestorations.Earlier[0])
            .Where(r => SweteTextSource.Reads(r.Book)).Sum(r => Words(r.Printed) - Words(r.Digitised)));

        // The restorations leave the chapter numbers standing; they go in the boundary pass below.
        var marked = SweteTextSource.Read(TestResources.SweteFolder, chapterMarkers: false);
        var cold = SweteTextSource.Read(TestResources.SweteFolder);
        var text = await _db.Texts.SingleAsync(t => t.Slug == SweteTextSource.Slug);
        foreach (var (book, chapter, verse, label) in verses)
        {
            var (canonical, placed) = SweteTextSource.Placed(book, chapter);
            var expected = string.Concat(marked.Books.Single(b => b.CanonicalOrdinal == canonical)
                .Chapters.Single(c => c.Number == placed).Verses.Single(v => v.Number == verse && v.Label == label)
                .Words.Select(w => w.Surface + w.Trailer));
            var stored = string.Concat(await _db.Words.AsNoTracking()
                .Where(w => w.TextId == text.Id && w.Verse!.Book!.CanonicalOrdinal == canonical
                            && w.Verse.ChapterNumber == placed && w.Verse.Number == verse && w.Verse.Label == label)
                .OrderBy(w => w.Position)
                .Select(w => w.Surface + w.Trailer)
                .ToListAsync());
            stored.Should().Be(expected, $"{book} {chapter}:{verse}{label}");
        }

        (await loader.Load(TestResources.SweteFolder)).Verses.Should().Be(0);

        // The chapter numbers are the edition boundary pass's, and after it the whole text is the cold one.
        await _db.Texts.Where(t => t.Id == text.Id).ExecuteUpdateAsync(s => s.SetProperty(t => t.RightsNote,
            "Earlier notes. " + SweteRestorations.SecondSamuelMarkerNote));
        _db.ChangeTracker.Clear();
        var boundaries = new EditionBoundaryRepairLoader(_db);
        var taken = await boundaries.Load(TestResources.Folder(string.Empty));
        _output.WriteLine(taken.ToString());
        taken.RemovedWords.Should().Be(59);
        taken.RewrittenWords.Should().Be(3);
        var numbered = marked.Books.SelectMany(b => b.Chapters.SelectMany(c => c.Verses.Select(v => (b, c, v))))
            .Select(x => (x.b.CanonicalOrdinal, x.c.Number, x.v.Number, x.v.Label, Marked: x.v.Words,
                Cold: cold.Books.Single(b => b.CanonicalOrdinal == x.b.CanonicalOrdinal).Chapters.Single(c => c.Number == x.c.Number)
                    .Verses.Single(v => v.Number == x.v.Number && v.Label == x.v.Label).Words))
            .Where(x => !x.Marked.SequenceEqual(x.Cold))
            .ToList();
        numbered.Should().HaveCount(61);
        foreach (var (canonical, chapter, verse, label, _, expected) in numbered)
        {
            (await _db.Words.AsNoTracking()
                    .Where(w => w.TextId == text.Id && w.Verse!.Book!.CanonicalOrdinal == canonical
                                && w.Verse.ChapterNumber == chapter && w.Verse.Number == verse && w.Verse.Label == label)
                    .OrderBy(w => w.Position).Select(w => w.Surface + w.Trailer).ToListAsync())
                .Should().Equal(expected.Select(w => w.Surface + w.Trailer), $"{canonical} {chapter}:{verse}{label}");
        }

        (await _db.Texts.AsNoTracking().SingleAsync(t => t.Id == text.Id)).RightsNote.Should()
            .Be("Earlier notes. " + SweteRestorations.ChapterMarkersNote);
        var again = await boundaries.Load(TestResources.Folder(string.Empty));
        (again.RemovedWords, again.MovedWords, again.RewrittenWords).Should().Be((0, 0, 0));
    }
}
