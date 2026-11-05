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

namespace Essenthos.Core.Tests.Witnesses;

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

/// <summary>Ottley's Isaiah read against his printed page, page by page.</summary>
[Trait(TestCategory.Name, TestCategory.Corpus)]
public class OttleyPageTests
{
    private const string Pipeline = "pages-ottley-2026-10-09";

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
    [InlineData(48, 5, "καὶ ἀνήγγειλά σοι τὰ πάλαι πρὶν ἐλθεῖν ἐπὶ σέ· ἀκουστόν σοι ἐποίησα, μὴ εἴπῃς ὅτι Τὰ εἴδωλά μοι "
                       + "ἐποίησαν, καὶ < μὴ εἴπῃς> Τὰ γλυπτὰ καὶ τὰ χωνευτὰ ἐνετείλατό μοι.")]
    [InlineData(38, 17, "εἵλου γάρ μου τὴν ψυχὴν ἵνα μὴ ἀπόληται, καὶ ἀπέριψας ὀπίσω μου πάσας τὰς ἁμαρτίας μου.")]
    [InlineData(49, 13, "εὐφραίνεσθε, οὐρανοί, καὶ ἀγαλλίασθω ἡ γῆ, ῥηξάτωσαν τὰ ὅρη εὐφροσύνην, ὅτι ἠλέησεν ὁ θεὸς τὸν "
                        + "λαὸν αὐτοῦ, καὶ τοὺς ταπεινοὺς τοῦ λαοῦ αὐτοῦ παρεκάλεσεν.")]
    // The stop the transcription read out of the grave accent of διὰ.
    [InlineData(24, 5, "ἡ δὲ γῆ ἠνόμησεν διὰ τοὺς κατοικοῦντας αὐτήν, διότι παρέβησαν τὸν νόμον καὶ ἤλλαξαν τὰ "
                       + "προστάγματα Κυρίου, διαθήκην αἰώνιον.")]
    // A word the transcription read as another.
    [InlineData(19, 6, "καὶ ἐκλείψουσιν οἱ ποταμοὶ καὶ αἱ διώρυγες τοῦ ποταμοῦ, καὶ ξηρανθήσεται πᾶσα συναγωγὴ ὕδατος "
                       + "καὶ ἐν παντὶ ἕλει καλάμου καὶ παπύρου,")]
    // Ottley's angle brackets, which the transcription lost with the word.
    [InlineData(5, 5, "νῦν δὲ ἀναγγελῶ ὑμῖν τί ποιήσω τῷ ἀμπελῶνί μου. ἀφελῶ τὸν φραγμὸν αὐτοῦ καὶ ἔσται εἰς "
                      + "διαρπαγήν, καὶ καθελῶ τὸν τοῖχον αὐτοῦ καὶ ἔσται εἰς < καταπάτημα>.")]
    // A word the transcription doubled, words it lost, and the stop that ends a verse.
    [InlineData(34, 11, "καὶ κατοικήσονται ἐν αὐτῇ ὄρνεα καὶ ἐχῖνοι καὶ ἴβεις καὶ κόρακες· καὶ ἐπιβληθήσεται ἐπ’ αὐτῇ "
                        + "σπαρτίον γεωμετρίας ἐρήμου, καὶ ὀνοκένταυροι οἰκήσουσιν ἐν αὐτῇ.")]
    [InlineData(35, 4, "παρακαλέσατε, οἱ ὀλιγόψυχοι τῇ διανοίᾳ· ἰσχύσατε, μὴ φοβεῖσθε· ἰδοὺ ὁ θεὸς ἡμῶν κρίσιν "
                       + "ἀνταποδώσει καὶ ἀνταποδώσει, αὐτὸς ἥξει καὶ σώσει ἡμᾶς.")]
    [InlineData(35, 3, "ἰσχύσατε, χεῖρες ἀνειμέναι καὶ γόνατα παραλελυμένα.")]
    [InlineData(52, 4, "Οὕτως λέγει Κύριος Εἰς Αἴγυπτον κατέβη ὁ λαός μου τὸ πρότερον παροικῆσαι ἐκεῖ, καὶ εἰς "
                       + "Ἀσσυρίους βίᾳ ἤχθησαν·")]
    // Words the transcription set in the verse beside: the page opens 26:9 at ᾗ and 3:17 at καὶ ταπεινώσει.
    [InlineData(26, 8, "ἡ γὰρ ὁδὸς Κυρίου κρίσις· ἠλπίσαμεν ἐπὶ τῷ ὀνόματί σου καὶ ἐπὶ τῇ μνείᾳ")]
    [InlineData(26, 9, "ᾗ ἐπιθυμεῖ ἡ ψυχὴ ἡμῶν. ( Ὠιδή.) ἐκ νυκτὸς ὀρθρίζει τὸ πνεῦμά μου πρὸς σέ, ὁ θεός, διότι φῶς "
                       + "τὰ προστάγματά σου ἐπὶ τῆς γῆς. δικαιοσύνην μάθετε, οἱ ἐνοικοῦντες ἐπὶ τῆς γῆς.")]
    [InlineData(3, 16, "Τάδε λέγει Κύριος Ἀνθ’ ὧν ὑψώθησαν αἱ θυγατέρες Σιών, καὶ ἐπορεύθησαν ὑψηλῷ τραχήλῳ καὶ "
                       + "νεύμασιν ὀφθαλμῶν, καὶ τῇ πορείᾳ τῶν ποδῶν ἅμα σύρουσαι τοὺς χιτῶνας καὶ τοῖς ποσὶν ἅμα "
                       + "παίζουσαι,")]
    [InlineData(3, 17, "καὶ ταπεινώσει ὁ θεὸς ἀρχούσας θυγατέρας Σιών. καὶ Κύριος ἀποκαλύψει τὸ σχῆμα αὐτῶν")]
    // Verses the converter divides out of the one before them, which the TEI gives no verse of their own.
    [InlineData(2, 20, "τῇ ἡμέρᾳ ἐκείνῃ ἐκβαλεῖ ἄνθρωπος τὰ βδελύγματα αὐτοῦ τὰ ἀργυρᾶ καὶ τὰ χρυσᾶ, ἃ ἐποίησεν "
                       + "προσκυνεῖν, τοῖς ματαίοις καὶ ταῖς νυκτερίσιν,")]
    [InlineData(35, 10, "καὶ συνηγμένοι διὰ Κύριον· ἀποστραφήσονται καὶ ἥξουσιν εἰς Σιὼν μετ’ εὐφροσύνης, καὶ εὐφροσύνη "
                        + "αἰώνιος ὑπὲρ κεφαλῆς αὐτῶν· ἐπὶ γὰρ κεφαλῆς αὐτῶν αἴνεσις καὶ ἀγαλλίαμα, καὶ εὐφροσύνη "
                        + "καταλήμψεται αὐτούς· ἀπέδρα ὀδύνη καὶ λύπη καὶ στεναγμός.")]
    public void TheVerseReadsAsThePagePrintsIt(int chapter, int verse, string printed) =>
        Text(chapter, verse).Should().Be(printed);

    /// <summary>
    /// Every entry still finds the text it was read against, once, in the verse as the transcription's
    /// repairs and the entries before it leave it, and each cites the page and the scan's leaf.
    /// </summary>
    [Fact]
    public void EveryEntryStillFindsWhatItWasReadAgainstAndCitesItsPage()
    {
        var verses = new Dictionary<(int, string), List<string>>();
        foreach (var line in OttleyIsaiah.Lines(TestResources.SweteFolder, OttleyIsaiah.Transcription))
        {
            var reference = line[..line.IndexOf(' ')].Split('.');
            var key = (int.Parse(reference[1]), reference[2]);
            (verses.TryGetValue(key, out var tokens) ? tokens : verses[key] = []).Add(line[(line.IndexOf(' ') + 1)..]);
        }

        OttleyIsaiah.PageEntries.Should().NotBeEmpty();
        OttleyIsaiah.PageEntries.Select(e => e.Round).Should().BeInAscendingOrder();
        OttleyIsaiah.PageEntries.Select(e => e.Source).Should().OnlyHaveUniqueItems();
        foreach (var entry in OttleyIsaiah.Page)
        {
            var tokens = verses[(entry.Chapter, entry.Verse)];
            var digitised = entry.Digitised.Split(' ');
            var found = Enumerable.Range(0, tokens.Count - digitised.Length + 1)
                .Where(at => tokens.Skip(at).Take(digitised.Length).SequenceEqual(digitised)).ToList();
            found.Should().ContainSingle($"{entry.Chapter}:{entry.Verse} \"{entry.Digitised}\"");
            entry.Why.Should().StartWith("Printed in Ottley, vol. 2").And.Contain("scan leaf");

            tokens.RemoveRange(found[0], digitised.Length);
            tokens.InsertRange(found[0], entry.Printed.Length == 0 ? [] : entry.Printed.Split(' '));
        }
    }

    /// <summary>A corpus loaded after any round reads; the reading after the last is the converter's.</summary>
    [Fact]
    public void EveryRoundReads()
    {
        var last = OttleyIsaiah.PageEntries.Max(e => e.Round);
        foreach (var round in Enumerable.Range(0, last + 1))
        {
            var reading = () => OttleyIsaiah.Lines(TestResources.SweteFolder, OttleyIsaiah.Through(round));
            reading.Should().NotThrow();
        }

        OttleyIsaiah.Lines(TestResources.SweteFolder, OttleyIsaiah.Through(last))
            .Should().Equal(OttleyIsaiah.Lines(TestResources.SweteFolder));
    }

    /// <summary>
    /// The script that writes the table places Codex's corrections in the tokens it keeps beside it, which
    /// must be the converter's after the transcription's own repairs, or a correction is placed in a verse
    /// the converter does not read.
    /// </summary>
    [Fact]
    public void ThePipelineReadsTheTranscriptionAsTheConverterDoes()
    {
        var kept = Path.Combine(Checkout(), "Resources", "Essenthos", "page-readings", Pipeline, "transcribed.txt");

        File.ReadAllLines(kept).Should().Equal(OttleyIsaiah.Lines(TestResources.SweteFolder, OttleyIsaiah.Transcription));
    }

    private static string Checkout()
    {
        for (var folder = new DirectoryInfo(AppContext.BaseDirectory); folder is not null; folder = folder.Parent)
        {
            if (File.Exists(Path.Combine(folder.FullName, "Essenthos.Core.sln")))
            {
                return folder.FullName;
            }
        }

        throw new InvalidOperationException("No Essenthos.Core.sln above the test assembly.");
    }
}
