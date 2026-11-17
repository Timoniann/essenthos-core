using Essenthos.Core.Conversion;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Loading;
using Essenthos.Core.Products;
using Essenthos.Core.XmlBible;
using FluentAssertions;
using Xunit;

namespace Essenthos.Core.Tests.Products;

/// <summary>
/// <c>forge convert</c> on a text small enough to read: the corrections it applies, the stages that fill the
/// words, the products it writes, and the two promises of the build: a text whose inputs did not move is not
/// made again, and a one-letter correction moves one verse's text hash and nothing else.
/// </summary>
public sealed class TextConversionTests : IDisposable
{
    private const string Slug = "TESTTEXT";

    private readonly string _resources = Path.Combine(Path.GetTempPath(), "essenthos-products", Guid.NewGuid().ToString("N"));

    public TextConversionTests()
    {
        Directory.CreateDirectory(Path.Combine(_resources, "Test"));
        File.WriteAllText(Path.Combine(_resources, "Test", "source.txt"), "In the beginning God created the heaven and the earth.");
    }

    public void Dispose()
    {
        if (Directory.Exists(_resources))
        {
            Directory.Delete(_resources, recursive: true);
        }
    }

    private static readonly TextDefinition Definition = new(
        Slug, "A test text", null, TextKind.Translation, "en", TextDirection.LeftToRight, Versification.English, 1611,
        "https://example.org", null, "Public Domain", null, Redistribution.PublicDomain, null);

    /// <summary>Genesis 1:1-2, the first verse read from the source file so that editing it moves an input.</summary>
    private static CataloguedText Text() => new(Slug, ["Test/source.txt"], (resources, _) =>
    {
        var first = File.ReadAllText(Path.Combine(resources, "Test", "source.txt")).Split(' ');
        List<WordDraft> Words(string[] tokens) =>
            [.. tokens.Select((token, at) => new WordDraft(token, at == tokens.Length - 1 ? string.Empty : " "))];
        return new TextSource(Definition,
        [
            new BookDraft(1, 1, "Genesis", "gen",
            [
                new ChapterDraft(1,
                [
                    new VerseDraft(1, Words(first)),
                    new VerseDraft(2, Words(["And", "the", "earth", "was", "without", "form."])),
                ]),
            ]),
        ]);
    });

    private void Corrections(params TextCorrection[] rows)
    {
        var store = new ProductStore(_resources);
        using var writer = store.Create(TextCorrections.ProductOf(Slug), ProductKind.Hand, TextCorrection.Schema, "test", []);
        writer.WriteAll(rows);
        store.Save();
    }

    private List<VerseManifestRow> Manifest(ProductStore store)
    {
        using var reader = store.Open(VerseManifestRow.ProductOf(Slug), VerseManifestRow.Schema);
        return reader.ReadAll();
    }

    private List<TextWord> Words(ProductStore store)
    {
        using var reader = store.Open(TextWord.ProductOf(Slug), TextWord.Schema(Slug));
        return reader.ReadAll();
    }

    [Fact]
    public void AConversionWritesEveryWordWithItsKeyAndEveryVerseWithItsHashes()
    {
        var store = new ProductStore(_resources);
        var outcome = TextConverter.Convert(store, Text());

        outcome.Unchanged.Should().BeFalse();
        var words = Words(store);
        words.Should().HaveCount(16);
        words[0].Key.Own.Should().Be("1.1:1.1");
        words[0].Normalised.Should().Be("in");
        words[^1].Key.Own.Should().Be("1.1:2.6");
        Manifest(store).Select(v => (v.Own.ToString(), v.Sequence, v.Words)).Should().Equal(("1.1:1", 1, 10), ("1.1:2", 2, 6));
        TextConverter.Lengths(store, Slug)[VerseKey.Parse("1.1:2")].Should().Be(26, "the frame reads a verse's letters, trailers left out");
    }

    [Fact]
    public void ATextWhoseInputsDidNotMoveIsNotMadeAgain()
    {
        TextConverter.Convert(new ProductStore(_resources), Text()).Unchanged.Should().BeFalse();
        new ProductStore(_resources).Save();

        TextConverter.Convert(new ProductStore(_resources), Text()).Unchanged.Should().BeTrue();

        File.WriteAllText(Path.Combine(_resources, "Test", "source.txt"), "In the beginning God made the heaven and the earth.");
        TextConverter.Convert(new ProductStore(_resources), Text()).Unchanged.Should().BeFalse("the source moved");
    }

    [Fact]
    public void AOneLetterCorrectionMovesOneVersesTextHashAndNothingElse()
    {
        var store = new ProductStore(_resources);
        Corrections(new TextCorrection("1.1:2.5", TextCorrection.Letters, "without", "withuot", "test"));
        TextConverter.Convert(store, Text());
        var before = Manifest(store);

        Corrections(new TextCorrection("1.1:2.5", TextCorrection.Letters, "without", "withont", "test"));
        TextConverter.Convert(store, Text()).Unchanged.Should().BeFalse("the corrections file is an input");
        var after = Manifest(store);

        VerseManifests.Diff(before.ToDictionary(v => v.Own, v => v.Hashes), after.ToDictionary(v => v.Own, v => v.Hashes)).Should().Equal((VerseKey.Parse("1.1:2"), VerseChange.Text));
        before.Zip(after).Count(pair => pair.First.Hashes.Annotation != pair.Second.Hashes.Annotation).Should().Be(0);
    }

    [Fact]
    public void CorrectionsPutRightLettersDropAWordAndAddWordsAtEitherEnd()
    {
        Corrections(
            new TextCorrection("1.1:1.4", TextCorrection.Letters, "God", "GOD", "capitals"),
            new TextCorrection("1.1:1.2", TextCorrection.Drop, "the", null, "not the edition's"),
            new TextCorrection("1.1:2", TextCorrection.Prepend, null, "A title. ", "a superscription"),
            new TextCorrection("1.1:2", TextCorrection.Append, null, "Amen.", "a subscription"));
        var store = new ProductStore(_resources);
        TextConverter.Convert(store, Text());

        var words = Words(store);
        string Verse(string verse) => string.Concat(words.Where(w => w.Key.Verse.ToString() == verse).Select(w => w.Surface + w.Trailer));
        Verse("1.1:1").Should().Be("In beginning GOD created the heaven and the earth.");
        Verse("1.1:2").Should().Be("A title. And the earth was without form. Amen.");
    }

    [Fact]
    public void AVerseWhoseEveryWordIsDroppedIsNotPrinted()
    {
        Corrections([.. "And the earth was without form.".Split(' ')
            .Select((word, at) => new TextCorrection($"1.1:2.{at + 1}", TextCorrection.Drop, word, null, "another verse's words"))]);
        var store = new ProductStore(_resources);
        TextConverter.Convert(store, Text());

        Manifest(store).Select(v => (v.Own.ToString(), v.Sequence)).Should().Equal(("1.1:1", 1));
        Words(store).Should().NotContain(w => w.Key.Verse.ToString() == "1.1:2");
    }

    [Fact]
    public void APassageTheSourceDoesNotNumberIsDividedIntoTheVersesItsRowsOpen()
    {
        Corrections(
            new TextCorrection("1.1:1.4", TextCorrection.Divide, "God", "1.1:1b", "where the Greek begins 1:1b"),
            new TextCorrection("1.1:1.7", TextCorrection.Divide, "heaven", "1.1:1c", "where the Greek begins 1:1c"),
            new TextCorrection("1.1:1.2", TextCorrection.Letters, "the", "a", "a letter put right before the division"));
        var store = new ProductStore(_resources);
        TextConverter.Convert(store, Text());

        Manifest(store).Select(v => (v.Own.ToString(), v.Sequence, v.Words)).Should()
            .Equal(("1.1:1", 1, 3), ("1.1:1b", 2, 3), ("1.1:1c", 3, 4), ("1.1:2", 4, 6));
        var words = Words(store);
        string Verse(string verse) => string.Concat(words.Where(w => w.Key.Verse.ToString() == verse).Select(w => w.Surface + w.Trailer));
        Verse("1.1:1").Should().Be("In a beginning ");
        Verse("1.1:1b").Should().Be("God created the ");
        Verse("1.1:1c").Should().Be("heaven and the earth.");
        words.Single(w => w.Key.Own == "1.1:1c.1").Surface.Should().Be("heaven");
    }

    [Fact]
    public void APassagePrintedBeforeTheWordsItsNumberBelongsToGivesTheNumberToItsLastPiece()
    {
        Corrections(
            new TextCorrection("1.1:2.1", TextCorrection.Divide, "And", "1.1:1a", "the addition the source prints first"),
            new TextCorrection("1.1:2.4", TextCorrection.Divide, "was", "1.1:2", "the verse's own words"));
        var store = new ProductStore(_resources);
        TextConverter.Convert(store, Text());

        Manifest(store).Select(v => (v.Own.ToString(), v.Sequence, v.Words)).Should()
            .Equal(("1.1:1", 1, 10), ("1.1:1a", 2, 3), ("1.1:2", 3, 3));
        Words(store).Single(w => w.Key.Own == "1.1:2.1").Surface.Should().Be("was");
    }

    [Theory]
    [InlineData("1.1:1.4", "god", "1.1:1b", "*the word reads \"God\"*")]
    [InlineData("1.1:1.4", "God", "1.1:2", "*the text already has a verse 1.1:2*")]
    [InlineData("1.1:1.4", "God", "1.2:1a", "*not of the chapter the word stands in*")]
    [InlineData("1.1:1.11", "earth.", "1.1:1b", "*the verse has no such word*")]
    public void ADivisionThatDoesNotMeetItsWordOrOpensAVerseTheTextHasStopsTheConversion(string own, string before, string after, string why)
    {
        Corrections(new TextCorrection(own, TextCorrection.Divide, before, after, "test"));

        var convert = () => TextConverter.Convert(new ProductStore(_resources), Text());

        convert.Should().Throw<InvalidOperationException>().WithMessage(why);
    }

    /// <summary>Genesis 1:1-2 with "the" supplied in the first verse and the whole of the second set as supplied.</summary>
    private static CataloguedText Marked() => new(Slug, ["Test/source.txt"], (_, _) => new TextSource(Definition,
    [
        new BookDraft(1, 1, "Genesis", "gen",
        [
            new ChapterDraft(1,
            [
                new VerseDraft(1, [new WordDraft("In", " "), new WordDraft("the", " ", SuppliedSpan: 1), new WordDraft("beginning.", string.Empty)]),
                new VerseDraft(2, [new WordDraft("And", " ", SuppliedSpan: 1), new WordDraft("the", " ", SuppliedSpan: 1), new WordDraft("earth.", string.Empty, SuppliedSpan: 2)]),
            ]),
        ]),
    ]));

    [Fact]
    public void AVerseTheSourceSetsAsSuppliedIsTheEditionsTextAndTheOthersKeepTheirMarks()
    {
        Corrections(new TextCorrection("1.1:2", TextCorrection.Unsupplied, "3", null, "translated from another manuscript"));
        var store = new ProductStore(_resources);
        TextConverter.Convert(store, Marked());

        Words(store).Select(w => (w.Key.Own, w.Supplied)).Should()
            .Equal(("1.1:1.1", null), ("1.1:1.2", 1), ("1.1:1.3", null), ("1.1:2.1", null), ("1.1:2.2", null), ("1.1:2.3", null));
    }

    [Fact]
    public void AnUnsuppliedRowCountingOtherwiseThanTheSourceStopsTheConversion()
    {
        Corrections(new TextCorrection("1.1:2", TextCorrection.Unsupplied, "2", null, "test"));

        var convert = () => TextConverter.Convert(new ProductStore(_resources), Marked());

        convert.Should().Throw<InvalidOperationException>().WithMessage("*the source marks 3 of its words supplied*");
    }

    /// <summary>1 Kings 17:19-23 as a module stores a block under three numbers: 17:20, 17:21 and 17:22 read alike.</summary>
    private static CataloguedText Stored(string third) => new(Slug, ["Test/source.txt"], (_, _) =>
    {
        List<WordDraft> Words(string verse) => [.. verse.Split(' ').Select(token => new WordDraft(token, " "))];
        return new TextSource(Definition,
        [
            new BookDraft(11, 11, "1 Kings", "1ki",
            [
                new ChapterDraft(17,
                [
                    new VerseDraft(19, Words("And he said, Give me thy son.")),
                    new VerseDraft(20, Words("And he cried unto the LORD. See, thy son liveth.")),
                    new VerseDraft(21, Words("And he cried unto the LORD. See, thy son liveth.")),
                    new VerseDraft(22, Words(third)),
                    new VerseDraft(23, Words("And the woman said, Now I know.")),
                ]),
            ]),
        ]);
    });

    [Fact]
    public void ABlockStoredUnderSeveralNumbersIsOneVerseStatingThemAll()
    {
        Corrections(new TextCorrection("11.17:20", TextCorrection.Block, "11.17:21 11.17:22", null, "stored once, linked three times"));
        var store = new ProductStore(_resources);
        TextConverter.Convert(store, Stored("And he cried unto the LORD. See, thy son liveth."));

        Manifest(store).Select(v => (v.Own.ToString(), v.Sequence)).Should().Equal(("11.17:19", 1), ("11.17:20", 2), ("11.17:23", 3));
        using var notes = store.Open(TextNote.ProductOf(Slug), TextNote.Schema);
        notes.ReadAll().Where(n => n.Scope == "stated").Select(n => (n.At, n.Content)).Should()
            .Equal(("11.17:20", "17:20"), ("11.17:20", "17:21"), ("11.17:20", "17:22"));
    }

    [Fact]
    public void ABlockWhoseCopyReadsOtherwiseStopsTheConversion()
    {
        Corrections(new TextCorrection("11.17:20", TextCorrection.Block, "11.17:21 11.17:22", null, "stored once, linked three times"));

        var convert = () => TextConverter.Convert(new ProductStore(_resources), Stored("And the child revived."));

        convert.Should().Throw<InvalidOperationException>().WithMessage("*11.17:22 does not read what 11.17:20 reads*");
    }

    /// <summary>Genesis 1:1 with a Strong number on each content word, one of them wrong (<em>the</em> carrying H776).</summary>
    private static CataloguedText Numbered() => new(Slug, ["Test/source.txt"], (_, _) => new TextSource(Definition,
    [
        new BookDraft(1, 1, "Genesis", "gen",
        [
            new ChapterDraft(1,
            [
                new VerseDraft(1,
                [
                    new WordDraft("In", " "), new WordDraft("the", " ", StrongNumber: "H776"), new WordDraft("beginning", " ", StrongNumber: "H7225"),
                    new WordDraft("God", " ", StrongNumber: "H430"), new WordDraft("created", string.Empty, StrongNumber: "H1254"),
                ]),
            ]),
        ]),
    ]));

    [Fact]
    public void AStrongCorrectionSetsOrClearsTheNumberOfTheWordItNames()
    {
        Corrections(
            new TextCorrection("1.1:1.4", TextCorrection.Strong, "God H430", "H433", "the singular"),
            new TextCorrection("1.1:1.2", TextCorrection.Strong, "the H776", null, "the article renders no word"),
            new TextCorrection("1.1:1.1", TextCorrection.Strong, "In", "H9003", "the preposition"));
        var store = new ProductStore(_resources);
        TextConverter.Convert(store, Numbered());

        Words(store).Select(w => (w.Surface, w.Strong)).Should().Equal(
            ("In", "H9003"), ("the", null), ("beginning", "H7225"), ("God", "H433"), ("created", "H1254"));
    }

    [Theory]
    [InlineData("God H1", "*1.1:1.4*the word carries H430*")]
    [InlineData("Gods H430", "*1.1:1.4*the word reads \"God\"*")]
    [InlineData("God", "*1.1:1.4*the word carries H430*")]
    public void AStrongCorrectionWrittenAgainstAnotherWordOrNumberStopsTheConversion(string before, string message)
    {
        Corrections(new TextCorrection("1.1:1.4", TextCorrection.Strong, before, "H433", "the singular"));

        var convert = () => TextConverter.Convert(new ProductStore(_resources), Numbered());

        convert.Should().Throw<InvalidOperationException>().WithMessage(message);
    }

    [Fact]
    public void AStrongCorrectionGivingTheNumberTheWordAlreadyCarriesStopsTheConversion()
    {
        Corrections(new TextCorrection("1.1:1.4", TextCorrection.Strong, "God H430", "H430", "nothing to put right"));

        var convert = () => TextConverter.Convert(new ProductStore(_resources), Numbered());

        convert.Should().Throw<InvalidOperationException>().WithMessage("*already carries*");
    }

    /// <summary>Real verses of Luther 1912, each with the numbers its file prints, and what the stage leaves of them.</summary>
    [Theory]
    [InlineData("die Völker:H5971 werden:H5921 dazu laufen:H5102", "die Völker:H5971 werden dazu laufen:H5102")]
    [InlineData("die von den Kindern:H1121 Heth:H2845 gekauft ist:H4735", "die von den Kindern:H1121 Heth:H2845 gekauft:H4735 ist")]
    [InlineData("sprach zu ihnen:G4314", "sprach zu:G4314 ihnen")]
    [InlineData("Er:H5921 weissagt:H5012 über:H5921 mich", "Er weissagt:H5012 über:H5921 mich")]
    [InlineData("vor dir:H6440 her geht:H5674", "vor dir:H6440 her geht:H5674")]
    [InlineData("alles was er hatte:H3426", "alles was er hatte:H3426")]
    [InlineData("und alle ihre Habe:H2428", "und alle ihre Habe:H2428")]
    [InlineData("Ist:H518 der HERR:H3068 Gott", "Ist:H518 der HERR:H3068 Gott")]
    [InlineData("so schlecht sind:H7455 vor Bosheit:H7455", "so schlecht sind vor Bosheit:H7455")]
    [InlineData("nannte:H7121 er:H5892 nach", "nannte:H7121 er:H5892 nach")]
    [InlineData("Es:G1161 ist:G2235 schon", "Es ist schon")]
    [InlineData("wie er ihnen befohlen hatte:H6680", "wie er ihnen befohlen:H6680 hatte")]
    [InlineData("Da sprach er:H559 Wohlan", "Da sprach:H559 er Wohlan")]
    [InlineData("denn er fürchtete sich:H3372 zu Zoar", "denn er fürchtete:H3372 sich zu Zoar")]
    [InlineData("und unter sie gekommen waren:H1961", "und unter sie gekommen waren:H1961")]
    [InlineData("Also ward:H2388 Jotham mächtig", "Also ward:H2388 Jotham mächtig")]
    [InlineData("kein Gott neben Mir:H4191", "kein Gott neben Mir:H4191")]
    public void LuthersNumbersOnPronounsAndAuxiliariesThatRenderNeitherMoveToTheHeadOrGo(string read, string placed)
    {
        Dictionary<string, string> classes = new()
        {
            ["H5971"] = "subs", ["H5921"] = "prep", ["H5102"] = "verb", ["H1121"] = "subs", ["H2845"] = "nmpr", ["H4735"] = "subs",
            ["G4314"] = "prep", ["H5012"] = "verb", ["H6440"] = "subs", ["H5674"] = "verb", ["H3426"] = "subs", ["H2428"] = "subs",
            ["H518"] = "conj", ["H3068"] = "nmpr", ["H7455"] = "subs", ["H7121"] = "verb", ["H5892"] = "subs", ["G1161"] = "conj",
            ["G2235"] = "adv", ["H6680"] = "verb", ["H559"] = "verb", ["H3372"] = "verb", ["H1961"] = "verb", ["H2388"] = "verb",
            ["H4191"] = "verb",
        };
        var words = read.Split(' ').Select(token => token.Split(':')).Select(parts => new ConvertedWord
        {
            Surface = parts[0],
            Trailer = " ",
            Strong = parts.Length > 1 ? parts[1] : null,
        }).ToList();

        MisplacedNumbers.Place([new ConvertedVerse(new VerseKey(1, 1, 1), 1, words)], classes);

        string.Join(' ', words.Select(w => w.Strong is null ? w.Surface : $"{w.Surface}:{w.Strong}")).Should().Be(placed);
    }

    [Fact]
    public void ACorrectionThatDoesNotMeetWhatTheSourceReadsStopsTheConversion()
    {
        Corrections(new TextCorrection("1.1:1.4", TextCorrection.Letters, "Lord", "LORD", "the source reads God here"));

        var convert = () => TextConverter.Convert(new ProductStore(_resources), Text());

        convert.Should().Throw<InvalidOperationException>().WithMessage("*1.1:1.4*the word reads \"God\"*");
    }

    [Fact]
    public void WordsJoinedByAnEmptyTrailerShareTheFoldedFormOfThePrintedWord()
    {
        var text = TextConverter.From(new TextSource(Definition with { Language = "hbo" },
        [
            new BookDraft(1, 1, "Genesis", "gen",
                [new ChapterDraft(1, [new VerseDraft(1, [new WordDraft("בְּ", ""), new WordDraft("רֵאשִׁית", " "), new WordDraft("בָּרָא", "")])])]),
        ]));

        new PrintedWords().Apply(text, _resources);

        var words = text.Verses[0].Words;
        words[0].Graphical.Should().Be(words[0].Normalised + words[1].Normalised);
        words[1].Graphical.Should().Be(words[0].Graphical);
        words[2].Graphical.Should().BeNull("a word printed alone has no longer printed form");
    }

    [Fact]
    public void BracketsOfAGreekEditionBecomeADoubtfulSpanAcrossItsVerses()
    {
        var text = TextConverter.From(new TextSource(Definition with { Slug = NestleTextSource.Slug, Language = "grc" },
        [
            new BookDraft(41, 1, "Mark", "mar",
            [
                new ChapterDraft(16,
                [
                    new VerseDraft(8, [new WordDraft("ἐφοβοῦντο", " ")]),
                    new VerseDraft(9, [new WordDraft("[[Ἀναστὰς", " "), new WordDraft("δὲ", " ")]),
                    new VerseDraft(20, [new WordDraft("ἀμήν", ".]] ")]),
                ]),
            ]),
        ]));

        new EditionBrackets().Apply(text, _resources);

        var words = text.Words().Select(w => w.Word).ToList();
        words.Select(w => w.Surface + w.Trailer).Should().Equal("ἐφοβοῦντο ", "Ἀναστὰς ", "δὲ ", "ἀμήν. ");
        words.Select(w => w.Doubtful).Should().Equal(null, 1, 1, 1);
        words.Skip(1).Should().OnlyContain(w => w.Brackets == 2);
    }

    /// <summary>Charles's verses as his reader keeps them, the signs printed in the trailers, put through his stage.</summary>
    private ConvertedText Charles(params (int Chapter, int Verse, string Text)[] verses)
    {
        var text = TextConverter.From(new TextSource(Definition with { Slug = CharlesTextSource.Slug },
        [
            new BookDraft(CharlesTextSource.Enoch, 1, "1 Enoch", "1en",
            [
                .. verses.GroupBy(verse => verse.Chapter).Select(chapter => new ChapterDraft(chapter.Key,
                [
                    .. chapter.Select(verse => new VerseDraft(verse.Verse,
                        [.. VerseWords.Printed(verse.Text).Select(token => new WordDraft(token.Word, token.Trailer, Elided: token.Word.Length == 0))])),
                ])),
            ]),
        ]));
        new CharlesSigns().Apply(text, _resources);
        return text;
    }

    private static string Printed(ConvertedVerse verse) => string.Concat(verse.Words.Select(word => word.Surface + word.Trailer));

    private static string[] Marked(ConvertedVerse verse, Func<ConvertedWord, int?> mark) =>
        [.. verse.Words.Where(word => mark(word) is not null).Select(word => $"{word.Surface}{mark(word)}")];

    [Fact]
    public void CharlesSignsLeaveTheWordsAndMarkTheWordsTheyEnclose()
    {
        var text = Charles(
            (1, 9, "And behold! He cometh with ten thousands of ┌His┐ holy ones To execute judgement upon all, And to destroy "
                   + "┌all┐ the ungodly: And to convict all flesh Of all the works ┌of their ungodliness┐ which they have"),
            (3, 1, "Observe how (in the winter) all the trees ╓seem as though they had withered."),
            (5, 1, "Observe ye╖ how the trees cover themselves: wherefore give ye heed ┌and know┐."),
            (10, 21, "╓And all the children of men shall become righteous,╖ and 〈all〉 nations [shall offer †adoration† and] praise."));

        var (nine, three, five, ten) = (text.Verses[0], text.Verses[1], text.Verses[2], text.Verses[3]);
        Printed(nine).Should().Be("And behold! He cometh with ten thousands of His holy ones To execute judgement upon all, And to destroy "
                                  + "all the ungodly: And to convict all flesh Of all the works of their ungodliness which they have");
        Marked(nine, word => word.NotInEthiopic).Should().Equal("His1", "all2", "of3", "their3", "ungodliness3");
        Printed(three).Should().Be("Observe how in the winter all the trees seem as though they had withered.");
        Marked(three, word => word.Supplied).Should().Equal("in1", "the1", "winter1");
        Marked(three, word => word.NotInGreek).Should().Equal("seem1", "as1", "though1", "they1", "had1", "withered1");
        Marked(five, word => word.NotInGreek).Should().Equal("Observe1", "ye1");
        Marked(five, word => word.NotInEthiopic).Should().Equal("and4", "know4");
        Printed(five).Should().Be("Observe ye how the trees cover themselves: wherefore give ye heed and know.");

        Printed(ten).Should().Be("And all the children of men shall become righteous, and all nations shall offer adoration and praise.");
        ten.Words[0].Surface.Should().Be("And", "the empty word that held the opening sign goes with it");
        Marked(ten, word => word.NotInGreek).Should().HaveCount(9).And.OnlyContain(mark => mark.EndsWith('2'));
        Marked(ten, word => word.Restored).Should().Equal("all1");
        Marked(ten, word => word.Doubtful).Should().Equal("shall1", "offer1", "adoration1", "and1");
        Marked(ten, word => word.Corrupt).Should().Equal("adoration1");
    }

    [Fact]
    public void ACharlesSignWhoseSpanIsUncertainStaysPrintedAndMarksNothing()
    {
        var text = Charles(
            (30, 2, "the colour (?) of fragrant"),
            (75, 5, "[And for the spirit of the dew† when they are opened.]"),
            (76, 1, "and he to]d his two dreams"));

        Printed(text.Verses[0]).Should().Be("the colour (?) of fragrant");
        Printed(text.Verses[1]).Should().Be("And for the spirit of the dew† when they are opened.");
        Marked(text.Verses[1], word => word.Doubtful).Should().HaveCount(11);
        text.Verses[1].Words.Should().OnlyContain(word => word.Corrupt == null);
        Printed(text.Verses[2]).Should().Be("and he to]d his two dreams");
        text.Verses[0].Words.Concat(text.Verses[2].Words).Should().OnlyContain(word => word.Supplied == null && word.Doubtful == null);
    }

    [Fact]
    public void ACharlesSignInsideAWordLeavesItOneWordMarkedWholeWithTheLettersItEncloses()
    {
        var text = Charles(
            (34, 4, "there came the king[s] of Taphu and the king[s] of 'Aresa, and"),
            (34, 7, "Amani-sakir and Ga[ga]'as, and he"),
            (40, 2, "and he to]d his two dreams"),
            (50, 1, "the desert of Sin[ai], which is between Elim and Sinai."),
            (51, 1, "[S]inai is far"),
            (52, 1, "Ki[ng]s are (there)"));

        static string[] Bracketed(ConvertedVerse verse) =>
            [.. verse.Words.Where(word => word.Bracketed is not null).Select(word => $"{word.Surface}:{word.Bracketed}")];

        var verses = text.Verses;
        Printed(verses[0]).Should().Be("there came the kings of Taphu and the kings of 'Aresa, and");
        Marked(verses[0], word => word.Doubtful).Should().Equal("kings1", "kings2");
        Bracketed(verses[0]).Should().Equal("kings:s", "kings:s");
        verses[0].Words.Single(word => word.Surface == "kings" && word.Doubtful == 1).Normalised.Should().Be("kings");
        Printed(verses[1]).Should().Be("Amani-sakir and Gaga'as, and he");
        Marked(verses[1], word => word.Doubtful).Should().Equal("Gaga'as3");
        Bracketed(verses[1]).Should().Equal("Gaga'as:ga");
        Printed(verses[2]).Should().Be("and he to]d his two dreams", "a closing sign with no opening one is a misread letter");
        verses[2].Words.Should().OnlyContain(word => word.Bracketed == null);
        Printed(verses[3]).Should().Be("the desert of Sinai, which is between Elim and Sinai.");
        Bracketed(verses[3]).Should().Equal("Sinai:ai");
        Printed(verses[4]).Should().Be("Sinai is far");
        Bracketed(verses[4]).Should().Equal("Sinai:S");
        Marked(verses[4], word => word.Doubtful).Should().Equal("Sinai5");
        Printed(verses[5]).Should().Be("Kings are there");
        verses[5].Words[0].Surface.Should().Be("Kings", "the empty word made to hold the opening sign goes with it");
        Bracketed(verses[5]).Should().Equal("Kings:ng");
        Marked(verses[5], word => word.Supplied).Should().Equal("there1");
    }

    [Fact]
    public void AMorphologyIsWrittenAsPostgresPrintsAJsonb()
    {
        JsonbText.Canonical("""{"pos":"art","language":"hbo","x":{"bb":1,"a":[1,2]}}""")
            .Should().Be("""{"x": {"a": [1, 2], "bb": 1}, "pos": "art", "language": "hbo"}""");
    }
}

/// <summary>
/// Every source a catalogued text declares is where it says. The converter leaves a missing source out of a text's
/// inputs without a word, so a source that moved (the page readings when generation/ was archived, TSK-1452) would
/// quietly stop deciding whether the text is made again.
/// </summary>
[Trait(TestCategory.Name, TestCategory.Corpus)]
public sealed class TextCatalogueSourceTests
{
    [Fact]
    public void EverySourceATextDeclaresIsOnDisk() =>
        TextCatalogue.All
            .SelectMany(text => text.Sources, (text, source) => (text.Slug, Path: TestResources.Folder(source), Source: source))
            .Where(entry => !File.Exists(entry.Path) && !Directory.Exists(entry.Path))
            .Select(entry => $"{entry.Slug}: {entry.Source}")
            .Should().BeEmpty();
}
