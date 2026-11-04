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
