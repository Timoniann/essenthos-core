using Essenthos.Core.Conversion;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Loading;
using Essenthos.Core.Products;
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
