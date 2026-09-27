using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Loading;
using Essenthos.Core.Loading.Frame;
using Essenthos.Core.Swete;
using FluentAssertions;
using Xunit;

namespace Essenthos.Core.Tests;

/// <summary>A First1KGreek TEI edition read into the one-token-per-line form, on an edition made up here.</summary>
[Trait(TestCategory.Name, TestCategory.Corpus)]
public class First1KGreekReaderTests
{
    private static List<string> Read(string body)
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path,
                $"""
                <TEI xmlns="http://www.tei-c.org/ns/1.0"><text><body>
                <div type="edition" n="urn">{body}</div>
                </body></text></TEI>
                """);
            return [.. First1KGreekReader.Lines(path, 48)];
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void NotesHeadsAndPageMarksAreNotText() =>
        Read("""
             <pb n="1"/><head>ΗΣΑΙΑΣ</head>
             <div type="textpart" subtype="chapter" n="1">
             <div type="textpart" subtype="verse" n="1"><p>ὅρασις <note type="marginal">B</note>
             ἣν <note type="footnote">1 ιδεν Q</note><pb n="2"/><lb n="2"/> εἶδεν</p></div>
             </div>
             """).Should().Equal("48.1.1 ὅρασις", "48.1.1 ἣν", "48.1.1 εἶδεν");

    /// <summary>An oracle's title stands between verses and opens the verse after it, as the page prints it.</summary>
    [Fact]
    public void MaterialOutsideAVerseOpensTheVerseAfterIt() =>
        Read("""
             <div type="textpart" subtype="chapter" n="15">
             <p>Τὸ ῥῆμα.</p>
             <div type="textpart" subtype="verse" n="1"><p>Νυκτὸς</p></div>
             <p>τέλος</p>
             </div>
             """).Should().Equal("48.15.1 Τὸ", "48.15.1 ῥῆμα.", "48.15.1 Νυκτὸς", "48.15.1 τέλος");

    /// <summary>A corrector's addition is a bracket on the page, and the comma after it is the word's.</summary>
    [Fact]
    public void AnInlineMarkIsNotAWordBoundary() =>
        Read("""
             <div type="textpart" subtype="chapter" n="5">
             <div type="textpart" subtype="verse" n="19"><p>τοῦ <add>Ἰσραήλ</add>, ἵνα</p></div>
             </div>
             """).Should().Equal("48.5.19 τοῦ", "48.5.19 Ἰσραήλ,", "48.5.19 ἵνα");

    [Fact]
    public void AWordDividedAtTheLineEndIsOneWord() =>
        Read("""
             <div type="textpart" subtype="chapter" n="5">
             <div type="textpart" subtype="verse" n="30"><p>θαλάσσης κυμαι-
             νούσης·</p></div>
             </div>
             """).Should().Equal("48.5.30 θαλάσσης", "48.5.30 κυμαινούσης·");

    /// <summary>
    /// The oxia and the tonos are one accent. The transcription writes the first and every other book
    /// of the edition the second, and a word spelled with each would be two words to every comparison.
    /// </summary>
    [Fact]
    public void TheAccentIsWrittenAsTheRestOfTheEditionWritesIt() =>
        Read("""
             <div type="textpart" subtype="chapter" n="1">
             <div type="textpart" subtype="verse" n="1"><p>Ἠσα&#x1F77;ας&#x0387;</p></div>
             </div>
             """).Should().Equal("48.1.1 Ἠσαίας·");

    [Fact]
    public void AnOpeningBracketStandsAlone() =>
        Read("""
             <div type="textpart" subtype="chapter" n="7">
             <div type="textpart" subtype="verse" n="3"><p>ὁ &lt;υἱός&gt; σου</p></div>
             </div>
             """).Should().Equal("48.7.3 ὁ", "48.7.3 <", "48.7.3 υἱός>", "48.7.3 σου");
}

[Trait(TestCategory.Name, TestCategory.Corpus)]
public class EditionRepairTests
{
    private static readonly string[] Lines =
    [
        "48.9.9 λέγοντες", "48.9.9 10", "48.9.9 Πλίνθοι", "48.9.9 πεπτώκασιν,",
        "48.9.11 καὶ",
        "48.38.15 Κύριε,", "48.38.16 εἵλου",
    ];

    [Fact]
    public void ADivisionOpensAVerseAtTheWordsItNamesAndDropsTheFigure() =>
        EditionRepairs.Apply("test", Lines[..5],
            [EditionRepair.Divide(9, "9", "10 Πλίνθοι", "Πλίνθοι", "10", "figure")])
            .Should().Equal("48.9.9 λέγοντες", "48.9.10 Πλίνθοι", "48.9.10 πεπτώκασιν,", "48.9.11 καὶ");

    /// <summary>Renumbered in the order listed, so a run of misnumbered verses is moved from its end.</summary>
    [Fact]
    public void ARenumberingMovesEveryWordAtTheAddress() =>
        EditionRepairs.Apply("test", Lines[5..],
            [
                EditionRepair.Renumber(38, "16", 38, "17", "one"),
                EditionRepair.Renumber(38, "15", 38, "16", "two"),
            ])
            .Should().Equal("48.38.16 Κύριε,", "48.38.17 εἵλου");

    [Fact]
    public void AReplacementWithNothingTakesTheWordsOut() =>
        EditionRepairs.Apply("test", Lines[..4], [EditionRepair.Replace(9, "9", "10", "", "figure")])
            .Should().Equal("48.9.9 λέγοντες", "48.9.9 Πλίνθοι", "48.9.9 πεπτώκασιν,");

    /// <summary>A repair that no longer finds what it was written against stops the read and names itself.</summary>
    [Theory]
    [InlineData("11")]
    [InlineData("12")]
    public void ARepairThatFindsNothingStopsTheRead(string verse)
    {
        var act = () => EditionRepairs.Apply("test", Lines[..5],
            [EditionRepair.Replace(9, verse, "πεπτώκασιν,", "x", "gone")]);

        act.Should().Throw<InvalidOperationException>().WithMessage($"*9:{verse}*gone*");
    }
}

/// <summary>Swete's Isaiah as the corpus reads it, from the transcription of his own volume.</summary>
[Trait(TestCategory.Name, TestCategory.Corpus)]
public class SweteIsaiahTests(Swete swete) : IClassFixture<Swete>
{
    private const int Isaiah = 23;

    private BookDraft Book => swete.Book(Isaiah);

    /// <summary>
    /// Swete's and not Ottley's: it opens with his capitals and spells Uzziah as he does, where the
    /// file of the same number in the folder is Ottley's Alexandrinus.
    /// </summary>
    [Fact]
    public void ItIsSwetesIsaiah()
    {
        var opening = Swete.Text(swete.Verse(Isaiah, 1, 1));

        opening.Should().StartWith("ΟΡΑΣΙΣ ἣν").And.Contain("Ὀζείου").And.NotContain("προφήτης");
        Book.Position.Should().Be(44, "Swete prints Isaiah after the Twelve");
    }

    /// <summary>
    /// Every verse Swete numbers is a verse here. The Greek has no 2:22 and no 56:12, and Swete
    /// gives 38:15 no number, its words ending 38:14.
    /// </summary>
    [Fact]
    public void EveryVerseItNumbersIsAVerse()
    {
        Book.Chapters.Should().HaveCount(66);
        Book.Chapters.Sum(chapter => chapter.Verses.Count).Should().Be(1289);
        Book.Chapters.Sum(chapter => chapter.Verses.Sum(verse => verse.Words.Count)).Should().Be(26970);

        foreach (var chapter in Book.Chapters.Where(chapter => chapter.Number != 38))
        {
            chapter.Verses.Select(verse => verse.Number)
                .Should().Equal(Enumerable.Range(1, chapter.Verses.Count), $"chapter {chapter.Number}");
        }

        Book.Chapters.Single(chapter => chapter.Number == 38).Verses.Select(verse => verse.Number)
            .Should().NotContain(15).And.Contain([14, 16, 17, 22]);
    }

    [Theory]
    [InlineData(9, 10, "ΙΙλίνθοι πεπτώκασιν")]
    [InlineData(13, 20, "οὐ κατοικηθήσεται")]
    [InlineData(22, 25, "τῇ ἡμέρᾳ ἐκείνῃ.")]
    [InlineData(35, 4, "παρακαλέσατε, οἱ ὀλιγόψυχοι")]
    [InlineData(38, 16, "Κύριε, περὶ αὐτῆς")]
    [InlineData(38, 17, "κἵλου γάρ μου")]
    [InlineData(52, 3, "ὅτι τάδε λέγει Κύριος")]
    [InlineData(65, 22, "οὐ μὴ οἰκοδομήσουσιν")]
    public void AVerseTheTranscriptionRanOnOpensWhereTheEditionOpensIt(int chapter, int verse, string opening) =>
        Swete.Text(swete.Verse(Isaiah, chapter, verse)).Should().StartWith(opening);

    [Fact]
    public void TheEndOf31_9IsItsOwn() =>
        Swete.Text(swete.Verse(Isaiah, 31, 9)).Should().EndWith("ἀλώσεται. Τάδε λέγει Κύριος Μακάριος ὃς ἔχει ἐν Σειὼν σπέρμα καὶ οἰκείους ἐν Ἰερουσαλήμ.");

    /// <summary>No verse number is left in the text, and no Latin letter but the one no Greek letter looks like.</summary>
    [Fact]
    public void NoFigureAndNoLatinLookAlikeIsLeft()
    {
        var surfaces = Book.Chapters.SelectMany(c => c.Verses).SelectMany(v => v.Words).Select(w => w.Surface).ToList();

        surfaces.Where(s => s.Any(char.IsAsciiDigit) && s.Any(char.IsLetter))
            .Should().BeEmpty();
        surfaces.Where(s => s.Any(char.IsAsciiLetter)).Should().Equal("nἅμα");
    }

    /// <summary>
    /// The frame puts every verse where the edition numbers it: Swete numbers Isaiah as the English
    /// does, 9:1 where the Hebrew has 8:23 and 64:1 where it has the end of 63:19, and the frame is the
    /// English numbering.
    /// </summary>
    [Fact]
    public void EveryVerseStandsAtItsOwnAddressInTheFrame()
    {
        var frame = TvtmsReader.Read(TestResources.Tvtms).Frame(Versification.Septuagint, EditionShape.Of(
            swete.Source.Books.SelectMany(book => book.Chapters.SelectMany(chapter => chapter.Verses.Select(verse =>
                (book.CanonicalOrdinal, chapter.Number, verse.Number, verse.Label,
                    verse.Words.Sum(word => word.Surface.Length)))))));

        var moved = Book.Chapters
            .SelectMany(chapter => chapter.Verses.Select(verse => (chapter.Number, verse.Number)))
            .Where(verse => !frame.Resolve(Isaiah, verse.Item1, verse.Item2)
                .SequenceEqual([new CanonicalReference(Isaiah, verse.Item1, verse.Item2)]))
            .ToList();

        moved.Should().BeEmpty();
    }
}

/// <summary>Ottley's Isaiah, Codex Alexandrinus, a witness of its own.</summary>
[Trait(TestCategory.Name, TestCategory.Corpus)]
public class OttleyTests
{
    private const int Isaiah = 23;

    private static readonly Lazy<TextSource> Source = new(() => OttleyTextSource.Read(TestResources.SweteFolder));

    private static BookDraft Book => Source.Value.Books.Single();

    private static string Text(int chapter, int verse) =>
        Swete.Text(Book.Chapters.Single(c => c.Number == chapter).Verses.Single(v => v.Number == verse));

    [Fact]
    public void ItIsAManuscriptOfItsOwnWithItsLicenceRecorded()
    {
        var definition = OttleyTextSource.Definition;

        definition.Validate();
        definition.Slug.Should().Be("OTTLEY");
        definition.Kind.Should().Be(TextKind.ManuscriptTradition);
        definition.Redistribution.Should().Be(Redistribution.ShareAlike);
        definition.RightsNote.Should().Contain("Modified:");
        Book.CanonicalOrdinal.Should().Be(Isaiah);
    }

    /// <summary>
    /// Ottley numbers by the Hebrew and gives no number where the Greek has no counterpart: no 38:15,
    /// no 40:7, and, as in every Greek Isaiah, no 2:22 and no 56:12.
    /// </summary>
    [Fact]
    public void EveryVerseOttleyNumbersIsAVerseAndNoOther()
    {
        Book.Chapters.Should().HaveCount(66);
        Book.Chapters.Sum(chapter => chapter.Verses.Count).Should().Be(1288);
        Book.Chapters.Sum(chapter => chapter.Verses.Sum(verse => verse.Words.Count)).Should().Be(27162);

        var missing = Book.Chapters.SelectMany(chapter =>
            Enumerable.Range(1, chapter.Verses.Max(v => v.Number))
                .Except(chapter.Verses.Select(v => v.Number))
                .Select(verse => $"{chapter.Number}:{verse}"));
        missing.Should().Equal("38:15", "40:7");
    }

    [Fact]
    public void TheManuscriptsTitleAndColophonAreNotWordsOfAVerse()
    {
        Text(1, 1).Should().StartWith("Ὅρασις ἥν εἶδεν Ἠσαίας").And.Contain("Ὀζίου");
        Text(66, 24).Should().EndWith("πάσῃ σαρκί.");
    }

    [Theory]
    [InlineData(2, 20, "τῇ ἡμέρᾳ ἐκείνῃ ἐκβαλεῖ", "προσκυνεῖν, τοῖς ματαίοις καὶ ταῖς νυκτερίσιν,")]
    [InlineData(2, 21, "τοῦ εἰσελθεῖν εἷς τὰς τρώγλας", "θραῦσαι τὴν γῆν.")]
    [InlineData(12, 6, "ἀγαλλιᾶσθε καὶ εὐφραίνεσθε", "ἐν μέσῳ σου.")]
    [InlineData(44, 28, "ὁ λέγων Κύρῳ φρονεῖν", "θεμελιώσω.")]
    public void AVerseTheTranscriptionRanOnOpensWhereOttleyNumbersIt(int chapter, int verse, string opening, string end) =>
        Text(chapter, verse).Should().StartWith(opening).And.EndWith(end);

    /// <summary>
    /// A word the file lost where the verse is left without it, which Brenton, Swete and GLAUx read
    /// and Ottley's own apparatus records no manuscript lacking; and πορεύσονται whole, without the
    /// letters of the foot-note the transcription read into it at the foot of the page.
    /// </summary>
    [Theory]
    [InlineData(53, 1, "καὶ ὃ βραχίων Κυρίου τίνι ἀπεκαλύφθη;")]
    [InlineData(5, 5, "καὶ ἔσται εἷς καταπάτημα")]
    [InlineData(35, 3, "καὶ γόνατα παραλελυμένα")]
    public void AWordTheFileLostIsBack(int chapter, int verse, string end) =>
        Text(chapter, verse).Should().EndWith(end);

    [Fact]
    public void AFootNoteReadIntoAWordIsTakenOut() =>
        Text(35, 9).Should().Contain("ἀλλὰ πορεύσονται ἐν αὐτῇ").And.NotContain("ευρον");

    [Fact]
    public void NoPlaceholderAndNoLatinLetterIsLeft() =>
        Book.Chapters.SelectMany(c => c.Verses).SelectMany(v => v.Words)
            .Where(w => w.Surface.Any(char.IsAsciiLetterOrDigit))
            .Should().BeEmpty();
}
