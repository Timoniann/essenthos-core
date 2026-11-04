using Essenthos.Core.Conversion;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Loading;
using Essenthos.Core.Products;
using Essenthos.Core.Products.Tsv;
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

    /// <summary>The corpus's ids for the two verses as the source reads them, 101-110 and 111-116, and a ceiling of 1000.</summary>
    private void CorpusIds()
    {
        string[][] verses = [[.. "In the beginning God created the heaven and the earth.".Split(' ')], ["And", "the", "earth", "was", "without", "form."]];
        var store = new ProductStore(_resources);
        var id = 101L;
        using (var writer = store.Create(TextWordId.ProductOf(Slug), ProductKind.Machine, TextWordId.Schema(Slug), "test", []))
        {
            writer.WriteAll(verses.SelectMany((words, verse) =>
                words.Select((surface, at) => new TextWordId(new WordKey(Slug, new VerseKey(1, 1, verse + 1), at + 1), id++, surface))));
        }

        store.Save();
        WordIdCeiling.Raise(_resources, 1000);
    }

    private Dictionary<string, (long? Id, string Surface)> IdsOf(string verse) =>
        Words(new ProductStore(_resources)).Where(w => w.Key.Verse.ToString() == verse).ToDictionary(w => w.Key.Own, w => (w.Id, w.Surface));

    private List<WordIdChange> IdReport()
    {
        using var reader = TsvFormat.Instance.Open(TextWordIds.ReportOf(_resources, Slug), WordIdChange.Schema);
        return reader.ReadAll();
    }

    [Fact]
    public void AnUnchangedVerseTakesItsIdsByKey()
    {
        CorpusIds();
        TextConverter.Convert(new ProductStore(_resources), Text()).Ids!.ByKey.Should().Be(16);

        Words(new ProductStore(_resources)).Select(w => w.Id).Should().Equal(Enumerable.Range(101, 16).Select(id => (long?)id));
        File.Exists(TextWordIds.ReportOf(_resources, Slug)).Should().BeFalse("no id was given out or lost");
    }

    [Fact]
    public void AWordAddedAtTheHeadOfAVerseTakesANewIdAndTheOthersKeepTheirs()
    {
        CorpusIds();
        Corrections(new TextCorrection("1.1:2", TextCorrection.Prepend, null, "Lo, ", "a word the edition prints"));
        var outcome = TextConverter.Convert(new ProductStore(_resources), Text());

        IdsOf("1.1:2").Should().Equal(new Dictionary<string, (long?, string)>
        {
            ["1.1:2.1"] = (1001, "Lo"), ["1.1:2.2"] = (111, "And"), ["1.1:2.3"] = (112, "the"), ["1.1:2.4"] = (113, "earth"),
            ["1.1:2.5"] = (114, "was"), ["1.1:2.6"] = (115, "without"), ["1.1:2.7"] = (116, "form."),
        });
        outcome.Ids.Should().Be(new WordIdOutcome(10, 6, 1, 0, true, 1));
        IdReport().Should().Equal(new WordIdChange("new", "1.1:2.1", 1001, "Lo", null));
        WordIdCeiling.Read(_resources).Should().Be(1001);
    }

    [Fact]
    public void AWordDroppedFromAVerseTakesItsIdAwayAndTheWordsAfterItKeepTheirs()
    {
        CorpusIds();
        Corrections(new TextCorrection("1.1:1.2", TextCorrection.Drop, "the", null, "not the edition's"));
        var outcome = TextConverter.Convert(new ProductStore(_resources), Text());

        IdsOf("1.1:1").Values.Should().Equal(
            (101L, "In"), (103L, "beginning"), (104L, "God"), (105L, "created"), (106L, "the"), (107L, "heaven"), (108L, "and"), (109L, "the"), (110L, "earth."));
        outcome.Ids.Should().Be(new WordIdOutcome(7, 8, 0, 1, false, 0));
        IdReport().Should().Equal(new WordIdChange("gone", "1.1:1.2", 102, "the", "ids"));
        WordIdCeiling.Read(_resources).Should().Be(1000, "no id was given out");
    }

    [Fact]
    public void TheFirstWordDroppedAndOneAppendedMovesNoIdToAnotherWord()
    {
        CorpusIds();
        Corrections(
            new TextCorrection("1.1:2.1", TextCorrection.Drop, "And", null, "not the edition's"),
            new TextCorrection("1.1:2", TextCorrection.Append, null, "Amen.", "a subscription"));
        TextConverter.Convert(new ProductStore(_resources), Text());

        IdsOf("1.1:2").Values.Should().Equal((112L, "the"), (113L, "earth"), (114L, "was"), (115L, "without"), (116L, "form."), (1001L, "Amen"));
        IdReport().Should().BeEquivalentTo([new WordIdChange("gone", "1.1:2.1", 111, "And", "ids"), new WordIdChange("new", "1.1:2.6", 1001, "Amen", null)]);
    }

    [Fact]
    public void AnIdGivenOutIsKeptByTheNextConversionAndAWordPutBackTakesItsOldIdAgain()
    {
        CorpusIds();
        Corrections(
            new TextCorrection("1.1:2", TextCorrection.Prepend, null, "Lo, ", "a word the edition prints"),
            new TextCorrection("1.1:1.2", TextCorrection.Drop, "the", null, "not the edition's"));
        TextConverter.Convert(new ProductStore(_resources), Text());
        TextConverter.Convert(new ProductStore(_resources), Text(), force: true);

        IdsOf("1.1:2")["1.1:2.1"].Should().Be((1001L, "Lo"), "an id once given is the word's");
        WordIdCeiling.Read(_resources).Should().Be(1001);

        Corrections(new TextCorrection("1.1:2", TextCorrection.Prepend, null, "Lo, ", "a word the edition prints"));
        TextConverter.Convert(new ProductStore(_resources), Text());

        IdsOf("1.1:1")["1.1:1.2"].Should().Be((102L, "the"), "the word the corpus held is back");
        IdsOf("1.1:2")["1.1:2.1"].Should().Be((1001L, "Lo"));
    }

    [Fact]
    public void WordsArePairedByTheirLongestCommonRunAndInOrderBetweenEqualGaps()
    {
        TextWordIds.Match(["a", "b", "c", "d"], ["a", "x", "c", "d"]).Should().Equal([0, 1, 2, 3], "a word put right is still the word");
        TextWordIds.Match(["a", "b", "c"], ["x", "a", "b", "c"]).Should().Equal(-1, 0, 1, 2);
        TextWordIds.Match(["a", "b", "c"], ["b", "c", "y"]).Should().Equal(1, 2, -1);
        TextWordIds.Match(["βου", "τυρον", "και"], ["βουτυρον", "και"]).Should().Equal([-1, 2], "two words read as one are neither");
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
