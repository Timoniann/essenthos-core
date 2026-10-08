using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Loading;
using Essenthos.Core.Loading.Links;
using Essenthos.Core.Swete;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Essenthos.Core.Tests;

/// <summary>
/// The nine verses of Swete read against the printed page for what its margin let into the text: a
/// siglum or a letter of the chapter's numeral standing as a word, a Latin letter for the Greek one
/// beside it, a word the line's end cut short, a breathing misread, a word divided in two.
/// </summary>
[Trait(TestCategory.Name, TestCategory.Corpus)]
public class SweteMarginTests(Swete swete) : IClassFixture<Swete>
{
    private const int Genesis = 1;
    private const int Deuteronomy = 5;
    private const int FirstSamuel = 9;
    private const int Proverbs = 20;
    private const int Ezekiel = 26;
    private const int Malachi = 39;
    private const int Wisdom = 75;

    [Theory]
    // A siglum of the margin standing as a word, and a breathing the page prints rough.
    [InlineData(Genesis, 11, 4, "καὶ εἶπαν Δεῦτε οἰκοδομήσωμεν ἑαυτοῖς πόλιν καὶ πύργον, οὗ ἡ κεφαλὴ ἔσται ἕως τοῦ οὐρανοῦ, "
                                + "καὶ ποιήσομεν ἑαυτῶν ὄνομα πρὸ τοῦ διασπαρῆναι ἐπὶ προσώπου πάσης τῆς γῆς.")]
    [InlineData(Proverbs, 22, 17, "Λόγοις σοφῶν παράβαλλε σὸν οὖς καὶ ἄκουε ἐμὸν λόγον, τὴν δὲ σὴν καρδίαν ἐπίστησον, "
                                  + "ἵνα γνῷς ὅτι καλοί εἰσιν.")]
    [InlineData(Wisdom, 12, 10, "κρίνων δὲ κατὰ βραχὺ ἐδίδους τόπον μετανοίας, οὐκ ἀγνοῶν ὅτι πονηρὰ ἡ γένεσις αὐτῶν καὶ "
                                + "ἔμφυτος ἡ κακία αὐτῶν, καὶ ὅτι οὐ μὴ ἀλλαγῇ ὁ λογισμὸς αὐτῶν εἰς τὸν αἰῶνα,")]
    [InlineData(Wisdom, 14, 19, "ὁ μὲν γὰρ τάχα κρατοῦντι βουλόμενος ἀρέσαι ἐξεβιάσατο τῇ τέχνῃ τὴν ὁμοιότητα ἐπὶ τὸ κάλλιον·")]
    // A letter of the chapter's numeral standing as a word, and a word the line's end cut short.
    [InlineData(Deuteronomy, 21, 1, "Ἐὰν δὲ εὑρεθῇ τραυματίας ἐν τῇ γῇ ᾗ κύριος ὁ θεός σου δίδωσίν σοι κληρονομῆσαι, "
                                    + "πεπτωκὼς ἐν τῷ πεδίῳ, καὶ οὐκ οἴδασιν τὸν πατάξαντα,")]
    [InlineData(FirstSamuel, 25, 28, "ἆρον δὴ τὸ ἀνόμημα τῆς δούλης σου, ὅτι ποιῶν ποιήσει κύριος τῷ κυρίῳ μου οἶκον πιστόν, "
                                     + "ὅτι πόλεμον κυρίου μου ὁ κύριος πολεμεῖ, καὶ κακία οὐχ εὑρεθήσεται ἐν σοὶ πώποτε.")]
    // A Latin letter for the Greek one, a letter misread, and a word divided in two.
    [InlineData(Malachi, 2, 12, "ἐξολεθρεύσει Κύριος τὸν ἄνθρωπον τὸν ποιοῦντα ταῦτα, ἕως καὶ ταπεινωθῇ ἐκ σκηνωμάτων "
                                + "Ἰακὼβ καὶ ἐκ προσαγόντων θυσίαν τῷ κυρίῳ Παντοκράτορι.")]
    [InlineData(Malachi, 3, 17, "Καὶ ἔσονταί μοι, λέγει Κύριος Παντοκράτωρ, εἰς ἡμέραν ἣν ἐγὼ ποιῶ εἰς περιποίησιν, καὶ "
                                + "αἱρετιῶ αὐτοὺς ὃν τρόπον αἱρετίζει ἄνθρωπος τὸν υἱὸν αὐτοῦ τὸν δουλεύοντα αὐτῷ.")]
    [InlineData(Ezekiel, 34, 12, "ὥσπερ ζητεῖ ὁ ποιμὴν τὸ ποίμνιον αὐτοῦ ἐν ἡμέρᾳ ὅταν ᾖ γνόφος καὶ νεφέλη ἐν μέσῳ προβάτων "
                                 + "διακεχωρισμένων, οὕτως ἐκζητήσω τὰ πρόβατά μου καὶ ἀπελάσω αὐτὰ ἀπὸ παντὸς τόπου οὗ "
                                 + "διεσπάρησαν ἐκεῖ ἐν ἡμέρᾳ νεφέλης καὶ γνόφου.")]
    public void TheVerseReadsAsThePagePrintsIt(int book, int chapter, int verse, string printed) =>
        Swete.Text(swete.Verse(book, chapter, verse)).Should().Be(printed);

    /// <summary>
    /// Every entry still finds the transcription's text it was read against, once, in the verse as the
    /// entries before it leave it: a First1KGreek fetched again with the fault corrected upstream stops
    /// here rather than being corrected twice.
    /// </summary>
    [Fact]
    public void EveryEntryStillFindsWhatTheTranscriptionHas()
    {
        var margin = SweteSettled.All.Except(SweteSettled.First).ToList();
        margin.Should().HaveCount(15);
        foreach (var book in margin.Select(r => r.Book).Distinct())
        {
            var lines = SweteDivisions.Lines(book,
                File.ReadLines(Path.Combine(TestResources.SweteFolder, SweteTextSource.FileName(book)))).ToList();
            var earlier = SweteRestorations.Apply(book, lines, SweteRestorations.Earlier[^1]).ToList();
            foreach (var entry in margin.Where(r => r.Book == book))
            {
                var tokens = earlier.Where(line => line.Split(' ')[0].EndsWith($".{entry.Chapter}.{entry.Verse}", StringComparison.Ordinal))
                    .Select(line => line[(line.IndexOf(' ') + 1)..]).ToList();
                var digitised = entry.Digitised.Split(' ');
                Enumerable.Range(0, tokens.Count - digitised.Length + 1)
                    .Count(at => tokens.Skip(at).Take(digitised.Length).SequenceEqual(digitised))
                    .Should().Be(1, $"{book} {entry.Chapter}:{entry.Verse} \"{entry.Digitised}\"");
            }

            var applying = () => SweteRestorations.Apply(book, lines).ToList();
            applying.Should().NotThrow();
        }
    }

    [Fact]
    public void EveryEntryCitesItsPageAndScan() =>
        SweteSettled.All.Except(SweteSettled.First).Should().OnlyContain(r =>
            r.Why.StartsWith("Printed in Swete, vol. ", StringComparison.Ordinal)
            && r.Why.Contains("swetuoft", StringComparison.Ordinal));
}

/// <summary>The seven verses of Ottley's Isaiah read against his printed page.</summary>
[Trait(TestCategory.Name, TestCategory.Corpus)]
public class OttleyPageTests
{
    private static readonly Lazy<BookDraft> Book =
        new(() => OttleyTextSource.Read(TestResources.SweteFolder).Books.Single());

    private static string Text(int chapter, int verse) =>
        Swete.Text(Book.Value.Chapters.Single(c => c.Number == chapter).Verses.Single(v => v.Number == verse));

    [Theory]
    // The letters the transcription misread: εἷς, ὃ and οὗ for εἰς, ὁ and οὐ, and τὸ. for τὰ.
    [InlineData(2, 19, "εἰσενέγκαντες εἰς τὰ σπήλαια καὶ εἰς τὰς σχισμὰς τῶν πετρῶν καὶ εἰς τὰς τρώγλας τῆς γῆς, ἀπὸ "
                       + "προσώπου τοῦ φόβου Κυρίου καὶ ἀπὸ τῆς δόξης τῆς ἰσχύος αὐτοῦ, ὅταν ἀναστῇ θραῦσαι τὴν γῆν.")]
    [InlineData(35, 9, "καὶ οὐκ ἔσται ἐκεῖ λέων, οὐδὲ τῶν θηρίων τῶν πονηρῶν οὐ μὴ ἀναβῇ ἐπ’ αὐτὴν οὐδὲ μὴ εὑρεθῇ ἐκεῖ, "
                       + "ἀλλὰ πορεύσονται ἐν αὐτῇ λελυτρωμένοι")]
    [InlineData(53, 1, "Κύριε, τίς ἐπίστευσεν τῇ ἀκοῇ ἡμῶν; καὶ ὁ βραχίων Κυρίου τίνι ἀπεκαλύφθη;")]
    // Ottley's angle brackets, which the transcription lost with the word.
    [InlineData(5, 5, "νῦν δὲ ἀναγγελῶ ὑμῖν τί ποιήσω τῷ ἀμπελῶνί μου. ἀφελῶ τὸν φραγμὸν αὐτοῦ καὶ ἔσται εἰς "
                      + "διαρπαγήν, καὶ καθελῶ τὸν τοῖχον αὐτοῦ καὶ ἔσται εἰς < καταπάτημα>.")]
    // A word the transcription doubled, one it lost, and the stop that ends a verse.
    [InlineData(34, 11, "καὶ κατοικήσονται ἐν αὐτῇ ὄρνεα καὶ ἐχῖνοι καὶ ἴβεις καὶ κόρακες· καὶ ἐπιβληθήσεται ἐπ’ αὐτῇ "
                        + "σπαρτίον γεωμετρίας ἐρήμου, καὶ ὀνοκένταυροι οἰκήσουσιν ἐν αὐτῇ.")]
    [InlineData(35, 4, "παρακαλέσατε, οἱ ὀλιγόψυχοι τῇ διανοίᾳ· ἰσχύσατε, μὴ φοβεῖσθε· ἰδοὺ ὁ θεὸς ἡμῶν κρίσιν "
                       + "ἀνταποδώσει καὶ ἀνταποδώσει, αὐτὸς ἥξει καὶ σώσει ἡμᾶς.")]
    [InlineData(35, 3, "ἰσχύσατε, χεῖρες ἀνειμέναι καὶ γόνατα παραλελυμένα.")]
    public void TheVerseReadsAsThePagePrintsIt(int chapter, int verse, string printed) =>
        Text(chapter, verse).Should().Be(printed);

    /// <summary>
    /// Every entry still finds the transcription's text it was read against, once, in the verse as the
    /// repairs before it leave it, and each cites the page and the scan's leaf.
    /// </summary>
    [Fact]
    public void EveryEntryStillFindsWhatTheTranscriptionHasAndCitesItsPage()
    {
        var transcribed = OttleyIsaiah.Lines(TestResources.SweteFolder, OttleyIsaiah.Transcription);
        OttleyIsaiah.Page.Should().HaveCount(12);
        foreach (var entry in OttleyIsaiah.Page)
        {
            var tokens = transcribed.Where(line => line.Split(' ')[0].EndsWith($".{entry.Chapter}.{entry.Verse}", StringComparison.Ordinal))
                .Select(line => line[(line.IndexOf(' ') + 1)..]).ToList();
            var digitised = entry.Digitised.Split(' ');
            Enumerable.Range(0, tokens.Count - digitised.Length + 1)
                .Count(at => tokens.Skip(at).Take(digitised.Length).SequenceEqual(digitised))
                .Should().Be(1, $"{entry.Chapter}:{entry.Verse} \"{entry.Digitised}\"");
            entry.Why.Should().StartWith("Printed in Ottley, vol. 2").And.Contain("scan leaf");
        }

        var reading = () => OttleyIsaiah.Lines(TestResources.SweteFolder);
        reading.Should().NotThrow();
    }
}

/// <summary>
/// The page's readings written into a corpus that holds the verses as the transcription had them, as
/// the corpus this machine holds does: a word put right keeps its row, a letter of the margin goes with
/// the matcher's link on it, a word restored is new, and a second pass writes nothing.
/// </summary>
[Trait(TestCategory.Name, TestCategory.Corpus)]
[Collection(WitnessDatabaseCollection.Name)]
public sealed class PrintedPageLoadTests : IDisposable
{
    private const int Isaiah = 23;

    private readonly AppDbContext _db;
    private readonly SweteRestorationLoader _loader;

    public PrintedPageLoadTests(WitnessDatabase database)
    {
        _db = database.NewContext();
        Clear();
        _loader = new SweteRestorationLoader(_db, NullLogger<SweteRestorationLoader>.Instance);
    }

    public void Dispose()
    {
        Clear();
        _db.Dispose();
    }

    private void Clear() => _db.Database.ExecuteSqlRaw("DELETE FROM text");

    private static IReadOnlyList<SweteWord> Words(SweteBook book, int chapter, int verse) =>
        book.Chapters.Single(c => c.Number == chapter).Verses.Single(v => v.Number == verse && v.Label.Length == 0).Words;

    /// <summary>A text holding the given verses of each book as its reading reads them, trailers and all.</summary>
    private Text Loaded(string slug, params (int Canonical, string Name, SweteBook Reading, IReadOnlyList<(int Chapter, int Verse)> Verses)[] books)
    {
        var text = new Text { Slug = slug, Name = slug, Kind = TextKind.CriticalEdition, Language = "grc", RightsNote = "CC BY-SA 4.0." };
        _db.Texts.Add(text);
        foreach (var (canonical, name, reading, verses) in books)
        {
            _db.AddBook(text, canonical, name, [.. verses.Select(at =>
                (at.Chapter, at.Verse, Words(reading, at.Chapter, at.Verse).Select(w => w.Surface).ToArray()))]);
        }

        _db.SaveChanges();
        foreach (var (canonical, _, reading, verses) in books)
        {
            foreach (var (chapter, verse) in verses)
            {
                var words = Words(reading, chapter, verse);
                var stored = Stored(text, canonical, chapter, verse).ToList();
                for (var at = 0; at < words.Count; at++)
                {
                    stored[at].Trailer = words[at].Trailer;
                }
            }
        }

        _db.SaveChanges();
        return text;
    }

    /// <summary>A book of Swete as the last pass before the margin's letters left it: every verse any entry restores.</summary>
    private static (int, string, SweteBook, IReadOnlyList<(int, int)>) LastPass(string file, int canonical, string name)
    {
        var lines = SweteDivisions.Lines(file,
            File.ReadLines(Path.Combine(TestResources.SweteFolder, SweteTextSource.FileName(file)))).ToList();
        return (canonical, name, SweteReader.Read(SweteRestorations.Apply(file, lines, SweteRestorations.Earlier[^1])),
            [.. SweteRestorations.All.Where(r => r.Book == file && r.Label.Length == 0).Select(r => (r.Chapter, r.Verse)).Distinct()]);
    }

    private IQueryable<Word> Stored(Text text, int canonical, int chapter, int verse) =>
        _db.Words.Where(w => w.TextId == text.Id && w.Verse!.Book!.CanonicalOrdinal == canonical
                             && w.Verse.ChapterNumber == chapter && w.Verse.Number == verse)
            .OrderBy(w => w.Position);

    private string Read(Text text, int canonical, int chapter, int verse) =>
        string.Concat(Stored(text, canonical, chapter, verse).AsNoTracking().Select(w => w.Surface + w.Trailer).ToList());

    private Word At(Text text, int canonical, int chapter, int verse, string surface) =>
        Stored(text, canonical, chapter, verse).Single(w => w.Surface == surface);

    [Fact]
    public async Task SwetesMarginVersesReadAsThePageAndAWordPutRightKeepsItsRow()
    {
        var text = Loaded(SweteTextSource.Slug, LastPass("01.Genesis", 1, "Genesis"), LastPass("47.Malachias", 39, "Malachi"));

        var brenton = Corpus.Add(_db, "GRCBRENT", TextKind.CriticalEdition, "grc", (1, 1, ["ἑαυτοῖς"]));
        _db.SaveChanges();
        var misread = At(text, 1, 11, 4, "ἐαυτοῖς");
        var siglum = At(text, 1, 11, 4, "L");
        var almighty = At(text, 39, 3, 17, "IΙαντοκράτωρ");
        var kept = Link(text, brenton, LinkRelation.Renders, misread, _db.WordAt(brenton, 1, 1, 1), "a test");
        var absence = Link(text, brenton, LinkRelation.Expands, siglum, null, SeptuagintLinkLoader.Source);

        var outcome = await _loader.Load(TestResources.SweteFolder);

        outcome.Verses.Should().Be(3, "Genesis 11:4 and Malachi 2:12 and 3:17");
        outcome.Words.Should().Be(-1, "the siglum of Genesis 11:4 goes");
        Read(text, 1, 11, 4).TrimEnd().Should().EndWith("ἐπὶ προσώπου πάσης τῆς γῆς.").And.Contain("ἑαυτοῖς πόλιν");
        Read(text, 39, 3, 17).Should().Contain("λέγει Κύριος Παντοκράτωρ, εἰς");
        Read(text, 39, 2, 12).Should().Contain("ἐκ σκηνωμάτων Ἰακὼβ").And.Contain("τῷ κυρίῳ Παντοκράτορι.");
        var rewritten = await _db.Words.AsNoTracking().SingleAsync(w => w.Id == misread.Id);
        (rewritten.Surface, rewritten.NormalisedText).Should().Be(("ἑαυτοῖς", "εαυτοισ"));
        (await _db.Words.AsNoTracking().SingleAsync(w => w.Id == almighty.Id)).Surface.Should().Be("Παντοκράτωρ");
        (await _db.Links.AnyAsync(l => l.Id == kept.Id)).Should().BeTrue();
        (await _db.Words.AnyAsync(w => w.Id == siglum.Id)).Should().BeFalse();
        (await _db.Links.AnyAsync(l => l.Id == absence.Id)).Should().BeFalse();
        (await _db.Texts.AsNoTracking().SingleAsync(t => t.Id == text.Id)).RightsNote.Should().Contain(SweteSettled.MarginNote);

        (await _loader.Load(TestResources.SweteFolder)).Verses.Should().Be(0);
    }

    [Fact]
    public async Task OttleysVersesReadAsThePageInBothTextsThatHoldTheBook()
    {
        var transcribed = SweteReader.Read(OttleyIsaiah.Lines(TestResources.SweteFolder, OttleyIsaiah.Transcription));
        var printed = SweteReader.Read(OttleyIsaiah.Lines(TestResources.SweteFolder));
        var verses = OttleyIsaiah.Page.Select(r => (r.Chapter, int.Parse(r.Verse))).Distinct().ToList();
        var ottley = Loaded(OttleyTextSource.Slug, (Isaiah, "Isaiah", transcribed, verses));
        var codex = Loaded(AlexandrinusTextSource.Slug, (Isaiah, "Isaiah", transcribed, verses));
        var into = At(ottley, Isaiah, 2, 19, "τὸ");
        var save = At(ottley, Isaiah, 35, 4, "σώσει");

        var outcome = await _loader.Load(TestResources.SweteFolder);

        outcome.Verses.Should().Be(2 * verses.Count);
        outcome.Words.Should().Be(0, "ἡμᾶς comes in at 35:4 and a doubled καὶ goes at 34:11, in each text");
        foreach (var text in new[] { ottley, codex })
        {
            foreach (var (chapter, verse) in verses)
            {
                Read(text, Isaiah, chapter, verse).Should()
                    .Be(string.Concat(Words(printed, chapter, verse).Select(w => w.Surface + w.Trailer)), $"{text.Slug} {chapter}:{verse}");
            }

            (await _db.Texts.AsNoTracking().SingleAsync(t => t.Id == text.Id)).RightsNote.Should().Contain(OttleyIsaiah.PageNote);
        }

        (await _db.Words.AsNoTracking().SingleAsync(w => w.Id == into.Id)).Surface.Should().Be("τὰ");
        (await _db.Words.AsNoTracking().SingleAsync(w => w.Id == save.Id)).Position.Should().Be(20);

        (await _loader.Load(TestResources.SweteFolder)).Verses.Should().Be(0);
    }

    [Fact]
    public async Task AnIsaiahReadingAsNeitherStopsThePass()
    {
        var transcribed = SweteReader.Read(OttleyIsaiah.Lines(TestResources.SweteFolder, OttleyIsaiah.Transcription));
        var ottley = Loaded(OttleyTextSource.Slug,
            (Isaiah, "Isaiah", transcribed, [.. OttleyIsaiah.Page.Select(r => (r.Chapter, int.Parse(r.Verse))).Distinct()]));
        At(ottley, Isaiah, 53, 1, "βραχίων").Surface = "βραχίονα";
        _db.SaveChanges();

        var correcting = () => _loader.Load(TestResources.SweteFolder);

        await correcting.Should().ThrowAsync<InvalidOperationException>().WithMessage("*53:1*neither*");
        Read(ottley, Isaiah, 53, 1).Should().Contain("καὶ ὃ βραχίονα");
    }

    private Link Link(Text from, Text to, LinkRelation relation, Word fromWord, Word? toWord, string source)
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
