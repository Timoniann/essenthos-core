using Essenthos.Core.Corpus;
using Essenthos.Core.Door43;
using Essenthos.Core.Endpoints;
using Essenthos.Core.Loading.Links;
using FluentAssertions;
using Xunit;

namespace Essenthos.Core.Tests;

/// <summary>
/// The join of a Door43 interlinear onto the corpus. Its spans stand in the translation's order, so
/// every verse that reorders the original tests whether the join can find an original word that
/// stands before the last one it found - which is where the first version of it lost a quarter of
/// the Ukrainian gold.
/// </summary>
public class InterlinearJoinTests
{
    [Fact]
    public void AnOriginalWordStandingBeforeTheLastOneJoinedStillJoins()
    {
        var (pairs, account) = Join(
            [Ukrainian("поправді"), Ukrainian("люблю")],
            [Greek("ἀγαπῶ"), Greek("ἐν"), Greek("ἀληθείᾳ")],
            Span("ἀληθείᾳ", "поправді"),
            Span("ἀγαπῶ", "люблю"));

        pairs.Select(pair => (pair.From.Single(), pair.To.Single())).Should().Equal((1L, 103L), (2L, 101L));
        account.SpansJoined.Should().Be(2);
    }

    [Fact]
    public void TheStatedOccurrencePicksTheWordWhenTheWitnessPrintsItAsOftenAsTheSourceCounts()
    {
        var (pairs, _) = Join(
            [Ukrainian("і")],
            [Greek("καὶ"), Greek("λόγος"), Greek("καὶ")],
            Span("καὶ", "і", occurrence: 2, occurrences: 2));

        pairs.Single().To.Should().Equal(103);
    }

    [Fact]
    public void AnOccurrenceCountedOverAnotherEditionIsRefusedRatherThanGuessed()
    {
        var (pairs, account) = Join(
            [Ukrainian("і")],
            [Greek("καὶ"), Greek("λόγος"), Greek("καὶ")],
            Span("καὶ", "і", occurrence: 1, occurrences: 1));

        pairs.Should().BeEmpty();
        account.SpanFailures[InterlinearSpanFailure.OriginalOccurrenceUnresolved].Should().Be(1);
    }

    [Fact]
    public void ASuffixTheSourceCutsOffJoinsTheWordThatKeepsIt()
    {
        var (pairs, _) = Join(
            [Ukrainian("царства")],
            [Hebrew("מַלְכוּתֹ֔ו")],
            Span("מַלְכוּת֔⁠וֹ", "царства"));

        pairs.Single().To.Should().Equal(101);
    }

    [Fact]
    public void AnArticleBhsaHoldsWithNothingPrintedGoesWithTheWordItStandsIn()
    {
        var (pairs, _) = Join(
            [Ukrainian("днів")],
            [Hebrew("בַּ"), Hebrew(""), Hebrew("יָּמִ֖ים")],
            Span("בַּ⁠יָּמִ֖ים", "днів"));

        pairs.Single().To.Should().Equal(101, 102, 103);
    }

    [Fact]
    public void APieceAfterAnApostropheTheCorpusPrintsWholeIsCountedAndNotJoined()
    {
        var (pairs, account) = Join(
            [Ukrainian("Шім'ї"), Ukrainian("сина")],
            [Hebrew("שִׁמְעִ֛י"), Hebrew("בֶּן")],
            Span("שִׁמְעִ֛י", "Шім"),
            Span("בֶּן", "ї", "сина"));

        pairs.Select(pair => pair.From.Single()).Should().Equal(1, 2);
        account.TranslatedFragments.Should().Be(1);
    }

    [Fact]
    public void TheReaderCountsWhatItStatesAndDoesNotKeep()
    {
        const string nest =
            """
            \c 1
            \v 1 \zaln-s |x-strong="G17220" x-occurrence="1" x-occurrences="1" x-content="ἐν"\*\zaln-s |x-strong="G02250" x-occurrence="1" x-occurrences="1" x-content="ἀληθείᾳ"\*\w поправді|x-occurrence="1" x-occurrences="1"\w*\zaln-e\*\zaln-e\*
            \w вільне|x-occurrence="1" x-occurrences="1"\w*
            """;

        var verse = Usfm3AlignmentReader.Read(nest).Single();

        verse.SharedOriginals.Should().Be(1);
        verse.UnalignedWords.Should().Be(1);
        verse.Spans.Single().WordOccurrences.Should().Equal((1, 1));
    }

    private static (List<InterlinearPair> Pairs, InterlinearJoinAccount Account) Join(
        IReadOnlyList<string> translated,
        IReadOnlyList<string> original,
        params AlignmentSpan[] spans)
    {
        var pairs = new List<InterlinearPair>();
        var account = new InterlinearJoinAccount();
        InterlinearJoin.Verse(
            "test 1:1",
            new AlignedVerse(1, 1, spans),
            [.. translated.Select((word, index) => Word(1 + index, word))],
            [.. original.Select((word, index) => Word(101 + index, word))],
            pairs,
            account);
        return (pairs, account);
    }

    private static AlignmentSpan Span(string content, params string[] words) =>
        new("G0", content, words, 1, 1);

    private static AlignmentSpan Span(string content, string word, int occurrence, int occurrences) =>
        new("G0", content, [word], occurrence, occurrences);

    private static string Ukrainian(string word) => $"ukr|{word}";

    private static string Greek(string word) => $"grc|{word}";

    private static string Hebrew(string word) => $"hbo|{word}";

    private static InterlinearWord Word(long id, string tagged)
    {
        var language = tagged[..3];
        var written = tagged[4..];
        return new InterlinearWord(id, WordFolding.Fold(written, language), language, written);
    }
}
