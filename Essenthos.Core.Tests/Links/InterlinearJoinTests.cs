using Essenthos.Core.Corpus;
using Essenthos.Core.Door43;
using Essenthos.Core.Endpoints;
using Essenthos.Core.Loading.Links;
using FluentAssertions;
using Xunit;

namespace Essenthos.Core.Tests.Links;

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

    /// <summary>
    /// BHSA writes a word's points in the order they were keyed, the source in Unicode's canonical
    /// order. The two are one spelling, so the source's count of it picks the word where the bare
    /// letters, which another word of the verse shares, cannot.
    /// </summary>
    [Fact]
    public void ASpellingWhosePointsStandInAnotherOrderIsTheSameSpelling()
    {
        var (pairs, _) = Join(
            [Ukrainian("небо")],
            [Hebrew("\u05E9\u05BC\u05C1\u05B8\u05DE\u05B7\u05D9\u05B4\u05DD"), Hebrew("\u05E9\u05C1\u05B8\u05DE\u05B8\u05D9\u05DD")],
            Span("\u05E9\u05B8\u05BC\u05C1\u05DE\u05B7\u05D9\u05B4\u05DD", "небо", occurrence: 1, occurrences: 1));

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
    public void ANestJoinsItsWordsToEveryOriginalWordOfIt()
    {
        const string nest =
            """
            \c 1
            \v 1 \zaln-s |x-strong="G17220" x-occurrence="1" x-occurrences="1" x-content="ἐν"\*\zaln-s |x-strong="G02250" x-occurrence="1" x-occurrences="1" x-content="ἀληθείᾳ"\*\w поправді|x-occurrence="1" x-occurrences="1"\w*\zaln-e\*\zaln-e\*
            \w вільне|x-occurrence="1" x-occurrences="1"\w*
            """;

        var verse = Usfm3AlignmentReader.Read(nest).Single();
        var pairs = new List<InterlinearPair>();
        var account = new InterlinearJoinAccount();
        InterlinearJoin.Verse("test 1:1", verse,
            [Word(1, Ukrainian("поправді")), Word(2, Ukrainian("вільне"))],
            [Word(101, Greek("ἀγαπῶ")), Word(102, Greek("ἐν")), Word(103, Greek("ἀληθείᾳ"))],
            pairs, account);

        verse.UnalignedWords.Should().Be(1);
        verse.Spans.Single().WordOccurrences.Should().Equal((1, 1));
        pairs.Single().From.Should().Equal(1);
        pairs.Single().To.Should().Equal(102, 103);
        account.SpansOverANestJoined.Should().Be(1);
    }

    [Fact]
    public void ANestWhoseOuterWordTheWitnessLacksIsRefusedWhole()
    {
        // Linking «Then he waited» to עוֹד alone would say עוֹד means it; the source says the two words do.
        var (pairs, account) = Join(
            [English("Then"), English("he"), English("waited")],
            [Hebrew("עֹ֔וד")],
            Span("ע֔וֹד", "Then", "he", "waited") with { Nest = [new AlignedOriginal("H2342a", "וַ⁠יָּ֣חֶל", 1, 1)] });

        pairs.Should().BeEmpty();
        account.SpanFailures[InterlinearSpanFailure.OriginalWordAbsent].Should().Be(1);
    }

    [Fact]
    public void ANestTheWitnessPrintsAsOneWordJoinsThatWord()
    {
        // Genesis 35:7: the source groups בֵּית + אֵל under «Bethel»; BHSA prints בֵּית־אֵל as one word.
        var (pairs, _) = Join(
            [English("Bethel")],
            [Hebrew("אֵל"), Hebrew("בֵּֽית־אֵ֑ל")],
            Span("אֵ֑ל", "Bethel") with { Nest = [new AlignedOriginal("H1008", "בֵּֽית", 1, 1)] });

        pairs.Single().To.Should().Equal(102);
    }

    [Fact]
    public void AnOuterGreekWordOfANestTheWitnessDoesNotPrintIsLeftOut()
    {
        // Matthew 14:10: the source groups τὸν + Ἰωάννην under «John»; Nestle prints no article there.
        var (pairs, account) = Join(
            [English("John")],
            [Greek("ἀπεκεφάλισεν"), Greek("Ἰωάννην")],
            Span("Ἰωάννην", "John") with { Nest = [new AlignedOriginal("G35880", "τὸν", 1, 1)] });

        pairs.Single().To.Should().Equal(102);
        account.NestWordsTheWitnessLacks.Should().Be(1);
    }

    [Fact]
    public void ANestWordTheWitnessPrintsMoreOrLessOftenIsTheOneBesideTheRestOfItsNest()
    {
        // Mark 9:2: the source has three τὸν (before Peter, James and John), Nestle two, so the count places none.
        var (pairs, account) = Join(
            [English("Peter")],
            [Greek("τὸν"), Greek("Πέτρον"), Greek("καὶ"), Greek("τὸν"), Greek("Ἰάκωβον"), Greek("καὶ"), Greek("Ἰωάνην")],
            Span("Πέτρον", "Peter") with { Nest = [new AlignedOriginal("G35880", "τὸν", 1, 3)] });

        pairs.Single().To.Should().Equal(101, 102);
        account.NestWordsPlacedBeside.Should().Be(1);
    }

    [Fact]
    public void TheInnermostGreekWordOfANestIsNeverLeftOut()
    {
        // Matthew 3:14: «John» over ὁ + Ἰωάννης where the witness spells the name otherwise would land on ὁ.
        var (pairs, _) = Join(
            [English("John")],
            [Greek("ὁ"), Greek("Ἰωάνης")],
            Span("Ἰωάννης", "John") with { Nest = [new AlignedOriginal("G35880", "ὁ", 1, 1)] });

        pairs.Should().BeEmpty();
    }

    [Fact]
    public void AWordTheSourceCountsAloneIsNotCountedInsideAnotherPrintedWord()
    {
        // Genesis 10:13: אֶת לוּדִים «the Ludim», then וְאֶת עֲנָמִים. The source counts the first אֶת once,
        // and the אֶת of וְאֶת is another spelling to it; BHSA holds that one as a word after the conjunction.
        var (pairs, _) = Join(
            [English("the"), English("Ludim")],
            [Hebrew("אֶת"), Hebrew("לוּדִים"), Attached(Hebrew("וְ")), Hebrew("אֶת"), Hebrew("עֲנָמִים")],
            Span("לוּדִים", "the", "Ludim") with { Nest = [new AlignedOriginal("H0853", "אֶת", 1, 1)] });

        pairs.Single().From.Should().Equal(1, 2);
        pairs.Single().To.Should().Equal(101, 102);
    }

    [Fact]
    public void PiecesOfAWordTheCorpusPrintsWholeJoinThatWord()
    {
        // Genesis 1:22 and 32:14: «the bird{s}» and «he-goats».
        var (pairs, account) = Join(
            [English("the"), English("birds"), English("multiply"), English("he-goats")],
            [Hebrew("הָעֹוף"), Hebrew("יִרֶב"), Hebrew("תְיָשִׁים")],
            Span("הָעוֹף", "the", "bird", "s") with { Glue = [new(false, []), new(false, ["s"]), new(true, [])] },
            Span("יִרֶב", "multiply"),
            Span("תְיָשִׁים", "he", "goats") with { Glue = [new(false, ["goats"]), new(true, [])] });

        pairs.Select(pair => (string.Join(',', pair.From), pair.To.Single())).Should().Equal(("1,2", 101L), ("3", 102L), ("4", 103L));
        account.TranslatedPiecesJoined.Should().Be(2);
    }

    [Fact]
    public void APieceOfAHyphenatedNameInAnotherSpanJoinsTheNameToItsWordToo()
    {
        // Genesis 10:11: Rehoboth renders רְחֹבֹת and Ir renders עִיר; the corpus prints Rehoboth-Ir.
        var (pairs, _) = Join(
            [English("Rehoboth-Ir"), English("and")],
            [Hebrew("רְחֹבֹת"), Hebrew("עִיר"), Hebrew("וְ")],
            Span("רְחֹבֹת", "Rehoboth") with { Glue = [new(false, ["Ir"])] },
            Span("עִיר", "Ir") with { Glue = [new(true, [])] },
            Span("וְ", "and"));

        pairs.Select(pair => (pair.From.Single(), pair.To.Single())).Should().Equal((1L, 101L), (1L, 102L), (2L, 103L));
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

    private static string English(string word) => $"eng|{word}";

    private static string Greek(string word) => $"grc|{word}";

    private static string Hebrew(string word) => $"hbo|{word}";

    /// <summary>A word printed against the next one, as BHSA prints a conjunction before its word.</summary>
    private static string Attached(string tagged) => $"{tagged}+";

    private static InterlinearWord Word(long id, string tagged)
    {
        var language = tagged[..3];
        var attached = tagged.EndsWith('+');
        var written = attached ? tagged[4..^1] : tagged[4..];
        return new InterlinearWord(id, WordFolding.Fold(written, language), language, written, Attached: attached);
    }
}
