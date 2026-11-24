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

    /// <summary>
    /// 2 Samuel 2:14: the source counts יוֹאָ֔ב and יוֹאָ֖ב as two spellings, once each; BHSA puts the holam on the
    /// yod (יֹואָב), so no printed form matches and the bare letters stand twice. The accents tell the two apart.
    /// </summary>
    [Fact]
    public void TwoSpellingsTheAccentsTellApartJoinWhereTheVowelsStandOnAnotherLetter()
    {
        var (pairs, account) = Join(
            [English("Joab"), English("said"), English("Joab")],
            [Hebrew("אֶל"), Hebrew(BhsaJoab + Zaqef + "\u05D1"), Hebrew("יָק֤וּמוּ"), Attached(Hebrew("וַ")), Hebrew("יֹּ֥אמֶר"),
             Hebrew(BhsaJoab + Tipcha + "\u05D1")],
            Span(SourceJoab + Zaqef + "\u05D1", "Joab", occurrence: 1, occurrences: 1),
            Span(SourceJoab + Tipcha + "\u05D1", "Joab", occurrence: 1, occurrences: 1));

        pairs.Select(pair => (pair.From.Single(), pair.To.Single())).Should().Equal((1L, 102L), (3L, 106L));
        account.SpansJoinedOnTheLooseFold.Should().Be(0);
    }

    [Fact]
    public void AccentsThatDoNotTellTheWordsApartStillRefuse()
    {
        var (pairs, account) = Join(
            [English("Joab")],
            [Hebrew(BhsaJoab + Tipcha + "\u05D1"), Attached(Hebrew("וַ")), Hebrew("יֹּ֥אמֶר"), Hebrew(BhsaJoab + Tipcha + "\u05D1")],
            Span(SourceJoab + Tipcha + "\u05D1", "Joab", occurrence: 1, occurrences: 1));

        pairs.Should().BeEmpty();
        account.SpanFailures[InterlinearSpanFailure.OriginalOccurrenceUnresolved].Should().Be(1);
    }

    /// <summary>
    /// 2 Kings 13:25 (UST): «he» over the nest יְהוֹאָשׁ בֶּן יְהוֹאָחָ֗ז. The accents place the second name, and בֶּן,
    /// which the source counts three times and BHSA prints apart twice, is the one place between the two names.
    /// </summary>
    [Fact]
    public void ANestWordTheCountCannotPlaceFillsTheGapBetweenTheRestOfItsNest()
    {
        const string sourceJehoash = "\u05D9\u05B0\u05D4\u05D5\u05B9\u05D0\u05B8\u05A3\u05E9\u05C1";
        const string bhsaJehoash = "\u05D9\u05B0\u05D4\u05B9\u05D5\u05D0\u05B8\u05A3\u05E9\u05C1";
        const string sourceJehoahaz = "\u05D9\u05B0\u05D4\u05D5\u05B9\u05D0\u05B8\u05D7\u05B8";
        const string bhsaJehoahaz = "\u05D9\u05B0\u05D4\u05B9\u05D5\u05D0\u05B8\u05D7\u05B8";
        var (pairs, account) = Join(
            [English("he")],
            [Hebrew(bhsaJehoash), Hebrew("בֶּן"), Hebrew(bhsaJehoahaz + Revia + "\u05D6"), Hebrew("אָבִ֖יו"), Hebrew("בֶּן"),
             Hebrew(bhsaJehoahaz + Merkha + "\u05D6")],
            Span(sourceJehoahaz + Revia + "\u05D6", "he") with
            {
                Nest = [new AlignedOriginal("H3060", sourceJehoash, 1, 1), new AlignedOriginal("H1121a", "בֶּן", 1, 3)],
            });

        pairs.Single().To.Should().Equal(101, 102, 103);
        account.NestWordsPlacedBeside.Should().Be(1);
    }

    private const string Revia = "\u0597";

    private const string Merkha = "\u05A5";

    /// <summary>יוֹאָ up to its accent as the source points it: holam on the vav.</summary>
    private const string SourceJoab = "\u05D9\u05D5\u05B9\u05D0\u05B8";

    /// <summary>The same as BHSA points it: holam on the yod.</summary>
    private const string BhsaJoab = "\u05D9\u05B9\u05D5\u05D0\u05B8";

    private const string Zaqef = "\u0594";

    private const string Tipcha = "\u0596";

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

    /// <summary>
    /// PRB-1793: Joshua 7:3 «about 2,000 men», which the release writes 2, and 000 on two lines: the pieces the reader
    /// joins are the one number the text prints.
    /// </summary>
    [Fact]
    public void TheTwoPiecesOfANumberJoinTheNumberTheTextPrints()
    {
        var spans = Usfm3AlignmentReader.Read(
            "\\c 7\n\\v 3 \\zaln-s |x-strong=\"k:H0505\" x-occurrence=\"1\" x-occurrences=\"1\" x-content=\"כְּ⁠אַלְפַּ֥יִם\"\\*\\w about|x-occurrence=\"1\" x-occurrences=\"1\"\\w*\n"
            + "\\w 2|x-occurrence=\"1\" x-occurrences=\"1\"\\w*,\n\\w 000|x-occurrence=\"1\" x-occurrences=\"1\"\\w*\\zaln-e\\*\n")
            .Single().Spans;

        var (pairs, account) = Join([English("about"), English("2,000"), English("men")], [Hebrew("כְּאַלְפַּיִם")], spans[0]);

        pairs.Single().From.Should().Equal(1, 2);
        account.TranslatedPiecesJoined.Should().Be(1);
    }

    /// <summary>
    /// PRB-1887: Amos 6:3 prints a dash against its first word, and the corpus keeps <c>—the</c> as one word where
    /// the source names <c>the</c>; the span joins that word and not the later <c>the</c>.
    /// </summary>
    [Fact]
    public void ACorpusWordThatOpensWithADashIsTheWordTheSourceNames()
    {
        var (pairs, account) = Join(
            [English("—the"), English("ones"), English("the"), English("day")],
            [Hebrew("הַֽמְנַדִּים"), Hebrew("יֹום")],
            Span("הַֽמְנַדִּים", "the", "ones"),
            Span("יֹום", "the", "day"));

        pairs.Select(pair => (string.Join(',', pair.From), pair.To.Single())).Should().Equal(("1,2", 101L), ("3,4", 102L));
        account.SpansJoined.Should().Be(2);
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

    /// <summary>
    /// PRB-1869 P1: a word the translators set in braces is read as theirs and not linked; a span of nothing else states no
    /// link, and the spans after it still find their words (UST Gen 3:1 «{One day} he asked», «{fruit} from any of»).
    /// </summary>
    [Fact]
    public void WordsTheTranslatorsSuppliedInBracesAreNotLinked()
    {
        const string braced =
            """
            \c 3
            \v 1 {\zaln-s |x-strong="c:H0559" x-occurrence="1" x-occurrences="1" x-content="וַ⁠יֹּ֨אמֶר֙"\*\w One|x-occurrence="1" x-occurrences="1"\w*
            \w day|x-occurrence="1" x-occurrences="1"\w*}
            \w he|x-occurrence="1" x-occurrences="1"\w*
            \w asked|x-occurrence="1" x-occurrences="1"\w*\zaln-e\* {\zaln-s |x-strong="H0398" x-occurrence="1" x-occurrences="1" x-content="תֹֽאכְל֔וּ"\*\w fruit|x-occurrence="1" x-occurrences="1"\w*}\zaln-e\*
            \zaln-s |x-strong="H3605" x-occurrence="1" x-occurrences="1" x-content="כֹּ֖ל"\*\w all|x-occurrence="1" x-occurrences="1"\w*\zaln-e\*
            """;

        var verse = Usfm3AlignmentReader.Read(braced).Single();
        verse.Spans[0].Supplied.Should().Equal(true, true, false, false);
        verse.Spans[2].Supplied.Should().BeNull();

        var pairs = new List<InterlinearPair>();
        var account = new InterlinearJoinAccount();
        InterlinearJoin.Verse("test 3:1", verse,
            [Word(1, English("One")), Word(2, English("day")), Word(3, English("he")), Word(4, English("asked")), Word(5, English("fruit")),
             Word(6, English("all"))],
            [Word(101, Attached(Hebrew("וַ"))), Word(102, Hebrew("יֹּ֨אמֶר֙")), Word(103, Hebrew("תֹֽאכְל֔וּ")), Word(104, Hebrew("כֹּ֖ל"))],
            pairs, account);

        pairs.Select(pair => (string.Join(',', pair.From), string.Join(',', pair.To))).Should().Equal(("3,4", "101,102"), ("6", "104"));
        account.SuppliedWordsLeftOut.Should().Be(3);
        account.SpansOnlySupplied.Should().Be(1);
    }

    /// <summary>
    /// 1 Kings 20:33: BHSA prints בֶן־הֲדַ֔ד as one word, the source prints בֶן and הֲדַ֔ד and aligns each over its own
    /// English. Neither part is a word of the witness, so the two spans are one link of both English words to the name.
    /// </summary>
    [Fact]
    public void TwoSpansOverThePartsOfANameBhsaPrintsAsOneWordAreOneLinkToIt()
    {
        var (pairs, account) = Join(
            [English("Ben"), English("Hadad"), English("came")],
            [Hebrew("אָחִ֣יךָ"), Hebrew("בֶן־הֲדַ֔ד"), Attached(Hebrew("וַ")), Hebrew("יֵּצֵ֤א")],
            Span("בֶן", "Ben"),
            Span("הֲדַ֔ד", "Hadad"),
            Span("וַ⁠יֵּצֵ֤א", "came"));

        pairs.Select(pair => (string.Join(',', pair.From), string.Join(',', pair.To))).Should().Equal(("1,2", "102"), ("3", "103,104"));
        account.CompoundsJoined.Should().Be(1);
        account.SpansJoined.Should().Be(3);
        account.SpanFailures.Values.Sum().Should().Be(0);
    }

    [Fact]
    public void TheNameTheCorpusPrintsAsOneEnglishWordIsLinkedOnce()
    {
        var (pairs, _) = Join(
            [English("Ben-Hadad")],
            [Hebrew("בֶן־הֲדַ֔ד")],
            Span("בֶן", "Ben") with { Glue = [new(false, ["Hadad"])] },
            Span("הֲדַ֔ד", "Hadad") with { Glue = [new(true, [])] });

        pairs.Select(pair => (string.Join(',', pair.From), string.Join(',', pair.To))).Should().Equal(("1", "101"));
    }

    /// <summary>Judges 1:7: the source sets «Adoni» over בֶ֗זֶק and «Bezek» over אֲדֹֽנִי; together they are BHSA's one word.</summary>
    [Fact]
    public void PartsTheSourceAlignsInTheOtherOrderStillJoinTheirWord()
    {
        var (pairs, _) = Join(
            [English("Adoni"), English("Bezek")],
            [Hebrew("אֲדֹֽנִי־בֶ֗זֶק")],
            Span("בֶ֗זֶק", "Adoni"),
            Span("אֲדֹֽנִי", "Bezek"));

        pairs.Select(pair => (string.Join(',', pair.From), string.Join(',', pair.To))).Should().Equal(("1,2", "101"));
    }

    /// <summary>
    /// Joshua 19:27 (UST): «El» over אֵ֥ל found אֶל, «to», by the bare letters, and «Iphtah» nothing. Counted as the source
    /// counts, part by part, אֵ֥ל is the second part of יִפְתַּח־אֵ֥ל.
    /// </summary>
    [Fact]
    public void APartTheBareLettersPutOnAnotherWordJoinsItsNameInstead()
    {
        var (pairs, _) = Join(
            [English("Iphtah"), English("El"), English("to"), English("Cabul")],
            [Hebrew("יִפְתַּח־אֵ֥ל"), Hebrew("אֶל"), Hebrew("כָּב֖וּל")],
            Span("יִפְתַּח", "Iphtah"),
            Span("אֵ֥ל", "El"),
            Span("אֶל", "to"),
            Span("כָּב֖וּל", "Cabul"));

        pairs.Select(pair => (string.Join(',', pair.From), string.Join(',', pair.To))).Should().Equal(("1,2", "101"), ("3", "102"), ("4", "103"));
    }

    /// <summary>
    /// 1 Kings 20:33 (ULT): the source points the first Ben-Hadad's בֶּן with a dagesh and BHSA the second. By their own
    /// counts the parts land on different words, so neither name is joined.
    /// </summary>
    [Fact]
    public void PartsWhoseCountsPlaceThemOnDifferentWordsAreRefused()
    {
        var (pairs, account) = Join(
            [English("Ben"), English("Hadad"), English("to"), English("Ben"), English("Hadad")],
            [Hebrew("בֶן־הֲדַ֔ד"), Hebrew("אֵלָיו֙"), Hebrew("בֶּן־הֲדַ֔ד")],
            Span("בֶּן", "Ben", occurrence: 1, occurrences: 1),
            Span("הֲדַ֔ד", "Hadad", occurrence: 1, occurrences: 2),
            Span("אֵלָיו֙", "to"),
            Span("בֶן", "Ben", occurrence: 1, occurrences: 1),
            Span("הֲדַ֔ד", "Hadad", occurrence: 2, occurrences: 2));

        pairs.Select(pair => (string.Join(',', pair.From), string.Join(',', pair.To))).Should().Equal(("3", "102"));
        account.CompoundsJoined.Should().Be(0);
    }

    /// <summary>Judges 1:27: «Beth» over the nest אֶת + בֵּית, «Shan» over שְׁאָ֣ן.</summary>
    [Fact]
    public void APartInsideANestJoinsWithTheRestOfItsNest()
    {
        var (pairs, _) = Join(
            [English("Beth"), English("Shan")],
            [Hebrew("אֶת"), Hebrew("בֵּית־שְׁאָ֣ן")],
            Span("בֵּית", "Beth") with { Nest = [new AlignedOriginal("H0853", "אֶת", 1, 1)] },
            Span("שְׁאָ֣ן", "Shan"));

        pairs.Select(pair => (string.Join(',', pair.From), string.Join(',', pair.To))).Should().Equal(("1,2", "101,102"));
    }

    /// <summary>Judges 1:33: BHSA prints the second Beth Anath as one word with a space inside.</summary>
    [Fact]
    public void ANameBhsaPrintsWithASpaceInsideJoinsLikeOneWithAMaqaf()
    {
        var (pairs, _) = Join(
            [English("and"), English("Beth"), English("Anath")],
            [Attached(Hebrew("וּ")), Hebrew("בֵ֣ית עֲנָ֔ת")],
            Span("וּ⁠בֵ֣ית", "and", "Beth"),
            Span("עֲנָ֔ת", "Anath"));

        pairs.Select(pair => (string.Join(',', pair.From), string.Join(',', pair.To))).Should().Equal(("1,2,3", "101,102"));
    }

    /// <summary>
    /// Leviticus 25:49: the source counts אוֹ, א֤וֹ and אֽוֹ as three spellings. BHSA puts the holam on the alef, so the
    /// printed forms never match, and with the meteg dropped the accents cannot tell אוֹ from אֽוֹ.
    /// </summary>
    [Fact]
    public void TheMetegTellsApartSpellingsTheAccentsCannot()
    {
        const string bhsaPlain = "אֹו";
        const string bhsaMahapakh = "אֹ֤ו";
        const string bhsaMeteg = "אֹֽו";
        var (pairs, _) = Join(
            [English("or"), English("or"), English("or"), English("or")],
            [Hebrew(bhsaPlain), Hebrew("דֹדֹ֞ו"), Hebrew(bhsaMahapakh), Hebrew(bhsaMeteg), Hebrew(bhsaMeteg)],
            Span("אוֹ", "or", occurrence: 1, occurrences: 1),
            Span("א֤וֹ", "or", occurrence: 1, occurrences: 1),
            Span("אֽוֹ", "or", occurrence: 1, occurrences: 2),
            Span("אֽוֹ", "or", occurrence: 2, occurrences: 2));

        pairs.Select(pair => (pair.From.Single(), pair.To.Single())).Should().Equal((1L, 101L), (2L, 103L), (3L, 104L), (4L, 105L));
    }

    /// <summary>
    /// 2 Timothy 3:13 in the Ukrainian: «А люди лихі» renders πονηροὶ δὲ ἄνθρωποι, and the source ties «А» and «лихі» together
    /// to πονηροὶ δὲ, writing the group's milestones again around each piece because «люди» stands between them.
    /// </summary>
    [Fact]
    public void PiecesOfOneGroupWhoseWordsStandApartAreOneLink()
    {
        const string verse =
            """
            \c 3
            \v 13 \zaln-s |x-strong="G41900" x-lemma="πονηρός" x-morph="Gr,AA,,,,NMP," x-occurrence="1" x-occurrences="1" x-content="πονηροὶ"\*\zaln-s |x-strong="G11610" x-lemma="δέ" x-morph="Gr,CC,,,,,,,," x-occurrence="1" x-occurrences="1" x-content="δὲ"\*\w А|x-occurrence="1" x-occurrences="1"\w*\zaln-e\*\zaln-e\*
            \zaln-s |x-strong="G04440" x-lemma="ἄνθρωπος" x-morph="Gr,N,,,,,NMP," x-occurrence="1" x-occurrences="1" x-content="ἄνθρωποι"\*\w люди|x-occurrence="1" x-occurrences="1"\w*\zaln-e\*
            \zaln-s |x-strong="G41900" x-lemma="πονηρός" x-morph="Gr,AA,,,,NMP," x-occurrence="1" x-occurrences="1" x-content="πονηροὶ"\*\zaln-s |x-strong="G11610" x-lemma="δέ" x-morph="Gr,CC,,,,,,,," x-occurrence="1" x-occurrences="1" x-content="δὲ"\*\w лихі|x-occurrence="1" x-occurrences="1"\w*\zaln-e\*\zaln-e\*
            \zaln-s |x-strong="G25320" x-lemma="καί" x-morph="Gr,CC,,,,,,,," x-occurrence="1" x-occurrences="2" x-content="καὶ"\*\w та|x-occurrence="1" x-occurrences="1"\w*\zaln-e\*
            \zaln-s |x-strong="G11140" x-lemma="γόης" x-morph="Gr,N,,,,,NMP," x-occurrence="1" x-occurrences="1" x-content="γόητες"\*\w дурисвіти|x-occurrence="1" x-occurrences="1"\w*\zaln-e\*
            """;

        var pairs = new List<InterlinearPair>();
        var account = new InterlinearJoinAccount();
        InterlinearJoin.Verse("2TI 3:13", Usfm3AlignmentReader.Read(verse).Single(),
            [Word(1, Ukrainian("А")), Word(2, Ukrainian("люди")), Word(3, Ukrainian("лихі")), Word(4, Ukrainian("та")),
             Word(5, Ukrainian("дурисвіти"))],
            [Word(101, Greek("πονηροὶ")), Word(102, Greek("δὲ")), Word(103, Greek("ἄνθρωποι")), Word(104, Greek("καὶ")),
             Word(105, Greek("γόητες")), Word(106, Greek("προκόψουσιν")), Word(107, Greek("καὶ"))],
            pairs, account);

        pairs.Select(pair => (string.Join(',', pair.From), string.Join(',', pair.To)))
            .Should().Equal(("1,3", "101,102"), ("2", "103"), ("4", "104"), ("5", "105"));
        account.GroupPiecesJoined.Should().Be(1);
        account.SpansJoined.Should().Be(5);
    }

    /// <summary>
    /// 1 Corinthians 1:2 in the Segond alignment counts both «à» as the first of two over the first of three ἐν: the source
    /// copied both counts, so neither says which «à» is which ἐν, and the first is a dative no ἐν stands for. Both are
    /// refused, and the cursor still moves past them.
    /// </summary>
    [Fact]
    public void SpansThatRepeatATranslatedCountOverTheSameOriginalWordAreRefused()
    {
        const string verse =
            """
            \c 1
            \v 2 \zaln-s |x-strong="G1722" x-lemma="ἐν" x-occurrence="1" x-occurrences="3" x-content="ἐν"\*\w à|x-occurrence="1" x-occurrences="2"\w*\zaln-e\* \zaln-s |x-strong="G1577" x-lemma="ἐκκλησία" x-occurrence="1" x-occurrences="1" x-content="ἐκκλησίᾳ"\*\w Église|x-occurrence="1" x-occurrences="1"\w*\zaln-e\* qui est \zaln-s |x-strong="G1722" x-lemma="ἐν" x-occurrence="1" x-occurrences="3" x-content="ἐν"\*\w à|x-occurrence="1" x-occurrences="2"\w*\zaln-e\* \zaln-s |x-strong="G2882" x-lemma="Κόρινθος" x-occurrence="1" x-occurrences="1" x-content="Κορίνθῳ"\*\w Corinthe|x-occurrence="1" x-occurrences="1"\w*\zaln-e\*
            """;

        var pairs = new List<InterlinearPair>();
        var account = new InterlinearJoinAccount();
        InterlinearJoin.Verse("1CO 1:2", Usfm3AlignmentReader.Read(verse).Single(),
            [Word(1, French("à")), Word(2, French("Église")), Word(3, French("qui")), Word(4, French("est")), Word(5, French("à")),
             Word(6, French("Corinthe"))],
            [Word(101, Greek("τῇ")), Word(102, Greek("ἐκκλησίᾳ")), Word(103, Greek("ἐν")), Word(104, Greek("Κορίνθῳ")),
             Word(105, Greek("ἐν")), Word(106, Greek("ἐν"))],
            pairs, account);

        pairs.Select(pair => (string.Join(',', pair.From), string.Join(',', pair.To))).Should().Equal(("2", "102"), ("6", "104"));
        account.SpanFailures[InterlinearSpanFailure.TranslatedCountCopied].Should().Be(2);
        account.GroupPiecesJoined.Should().Be(0);
    }

    /// <summary>Romans 2:14 in the ULT counts «having» 1 of 1 twice, over ἔχοντα and over ἔχοντες: each still names its word.</summary>
    [Fact]
    public void SpansThatRepeatATranslatedCountOverDifferentOriginalWordsStillJoin()
    {
        var (pairs, account) = Join(
            [English("not"), English("having"), English("they"), English("not"), English("having")],
            [Greek("μὴ"), Greek("ἔχοντα"), Greek("οὗτοι"), Greek("μὴ"), Greek("ἔχοντες")],
            Span("μὴ", "not", occurrence: 1, occurrences: 2) with { WordOccurrences = [(1, 2)] },
            Span("ἔχοντα", "having", occurrence: 1, occurrences: 1) with { WordOccurrences = [(1, 1)] },
            Span("οὗτοι", "they", occurrence: 1, occurrences: 1) with { WordOccurrences = [(1, 1)] },
            Span("μὴ", "not", occurrence: 2, occurrences: 2) with { WordOccurrences = [(2, 2)] },
            Span("ἔχοντες", "having", occurrence: 1, occurrences: 1) with { WordOccurrences = [(1, 1)] });

        pairs.Select(pair => (string.Join(',', pair.From), string.Join(',', pair.To)))
            .Should().Equal(("1", "101"), ("2", "102"), ("3", "103"), ("4", "104"), ("5", "105"));
        account.SpanFailures.GetValueOrDefault(InterlinearSpanFailure.TranslatedCountCopied).Should().Be(0);
    }

    /// <summary>
    /// Hosea 1:9 (ULT): the source counts the לֹא of the name לֹא עַמִּי, which BHSA prints as one word, with the verse's
    /// other two, so «not» over לֹא 2 of 3 is the first לֹא BHSA prints alone, not the last.
    /// </summary>
    [Fact]
    public void AWordAlsoStandingInsideANameBhsaPrintsWholeIsCountedWithTheNamesParts()
    {
        var (pairs, _) = Join(
            [English("you"), English("not"), English("my"), English("people"), English("I"), English("will"), English("not"), English("be")],
            [Hebrew("לֹ֣א עַמִּ֑י"), Hebrew("אַתֶּם֙"), Hebrew("לֹ֣א"), Hebrew("עַמִּ֔י"), Hebrew("אָנֹכִ֖י"), Hebrew("לֹא"), Hebrew("אֶהְיֶ֥ה")],
            Span("אַתֶּם֙", "you"),
            Span("לֹ֣א", "not", occurrence: 2, occurrences: 3),
            Span("עַמִּ֔י", "my", "people") with { Occurrence = 2, Occurrences = 2 },
            Span("אָנֹכִ֖י", "I"),
            Span("לֹא", "will", "not") with { Occurrence = 1, Occurrences = 1 },
            Span("אֶהְיֶ֥ה", "be"));

        pairs.Should().Contain(pair => pair.From.SequenceEqual(new long[] { 2 }) && pair.To.SequenceEqual(new long[] { 103 }));
        pairs.Should().NotContain(pair => pair.To.Contains(106) && pair.From.Contains(2));
    }

    /// <summary>
    /// 2 Kings 5:18 (ULT): the second «may» stands over the second יִסְלַח, which BHSA prints inside יִסְלַח־נא. Counted with the
    /// parts it lands on a part, so it is not put on the first יִסְלַח, the first «may»'s word.
    /// </summary>
    [Fact]
    public void AWordTheCountPutsOnAPartOfANameIsNotPutOnAnotherWordOfItsSpelling()
    {
        var (pairs, _) = Join(
            [English("may"), English("Yahweh"), English("forgive"), English("may"), English("Yahweh"), English("forgive")],
            [Hebrew("יִסְלַ֥ח"), Hebrew("יְהוָ֖ה"), Hebrew("יִסְלַח־נ֨א"), Hebrew("יְהוָ֤ה")],
            Span("יִסְלַ֥ח", "may", occurrence: 1, occurrences: 2),
            Span("יְהוָ֖ה", "Yahweh", occurrence: 1, occurrences: 2),
            Span("יִסְלַ֥ח", "forgive", occurrence: 1, occurrences: 2),
            Span("יִסְלַ֥ח", "may", occurrence: 2, occurrences: 2),
            Span("יְהוָ֤ה", "Yahweh", occurrence: 2, occurrences: 2));

        pairs.Where(pair => pair.To.SequenceEqual(new long[] { 101 })).SelectMany(pair => pair.From).Should().NotContain(4);
    }

    [Fact]
    public void ASpanRefusedForItsOriginalWordKeepsItsTranslatedWordsFromTheNextSpan()
    {
        // Matthew 14:29 in the ULT: «to go» is ἐλθεῖν, which this witness does not print; «to» of «to Jesus» is πρὸς.
        var (pairs, account) = Join(
            [English("to"), English("go"), English("to"), English("Jesus")],
            [Greek("καὶ"), Greek("ἦλθεν"), Greek("πρὸς"), Greek("τὸν"), Greek("Ἰησοῦν")],
            Span("ἐλθεῖν", "to", "go"),
            Span("πρὸς", "to"),
            Span("Ἰησοῦν", "Jesus"));

        pairs.Select(pair => (pair.From.Single(), pair.To.Single())).Should().Equal((3L, 103L), (4L, 105L));
        account.SpanFailures[InterlinearSpanFailure.OriginalWordAbsent].Should().Be(1);
    }

    [Fact]
    public void AWordTheEditionsSpellDifferentlyIsTheOneOfItsLemmaTheOthersLeave()
    {
        // 1 Corinthians 7:9 in the ULT: the source reads γαμησάτωσαν and γαμῆσαι, Nestle 1904 γαμησάτωσαν and γαμεῖν.
        var (pairs, account) = Join(
            [English("they"), English("should"), English("marry"), English("For"), English("it"), English("is"), English("better"),
                English("to"), English("marry"), English("than"), English("to"), English("burn")],
            [Greek("εἰ", "G1487"), Greek("δὲ", "G1161"), Greek("οὐκ", "G3756"), Greek("ἐγκρατεύονται", "G1467"),
                Greek("γαμησάτωσαν", "G1060"), Greek("κρεῖττον", "G2909"), Greek("γάρ", "G1063"), Greek("ἐστιν", "G1510"),
                Greek("γαμεῖν", "G1060"), Greek("ἢ", "G2228"), Greek("πυροῦσθαι", "G4448")],
            Numbered("G10600", "γαμησάτωσαν", "they", "should", "marry"),
            Numbered("G10630", "γάρ", "For"),
            Numbered("G15100", "ἐστιν", "it", "is"),
            Numbered("G29090", "κρεῖττον", "better"),
            Numbered("G10600", "γαμῆσαι", "to", "marry"),
            Numbered("G22280", "ἢ", "than"),
            Numbered("G44480", "πυροῦσθαι", "to", "burn"));

        pairs.Single(pair => pair.From.SequenceEqual([8L, 9L])).To.Should().Equal(109);
        pairs.Single(pair => pair.From.SequenceEqual([1L, 2L, 3L])).To.Should().Equal(105);
        account.SpansJoinedByTheOnePlaceLeft.Should().Be(1);
    }

    [Fact]
    public void AWordTheSourceDoesNotAlignStillCountsAgainstTheOnePlaceLeft()
    {
        // Matthew 13:4 in the UST: the source counts three καὶ and aligns the second and third; Nestle 1904 prints two,
        // having ἐλθόντα where the source has καὶ ἦλθεν. Counting only the aligned two put «and» on the first καὶ.
        var (pairs, account) = Join(
            [English("Then"), English("and")],
            [Greek("καὶ", "G2532"), Greek("ἐν", "G1722"), Greek("τῷ", "G3588"), Greek("σπείρειν", "G4687"), Greek("αὐτὸν", "G846"),
                Greek("ἃ", "G3739"), Greek("μὲν", "G3303"), Greek("ἔπεσεν", "G4098"), Greek("καὶ", "G2532"),
                Greek("ἐλθόντα", "G2064"), Greek("τὰ", "G3588"), Greek("πετεινὰ", "G4071"), Greek("κατέφαγεν", "G2719")],
            Numbered("G25320", "καὶ", "Then", occurrence: 2, occurrences: 3),
            Numbered("G25320", "καὶ", "and", occurrence: 3, occurrences: 3));

        pairs.Should().BeEmpty();
        account.SpansJoinedByTheOnePlaceLeft.Should().Be(0);
    }

    [Fact]
    public void APronounTheEditionsReadInAnotherCaseIsNotTheOnePlaceLeft()
    {
        // Mark 14:46 in the ULT: «their» is the source's αὐτῶν (their hands); Nestle 1904 has αὐτῷ (on him) there.
        var (pairs, _) = Join(
            [English("they"), English("laid"), English("their"), English("hands"), English("and"), English("seized"), English("him")],
            [Greek("οἱ", "G3588"), Greek("δὲ", "G1161"), Greek("ἐπέβαλαν", "G1911"), Greek("τὰς", "G3588"), Greek("χεῖρας", "G5495"),
                Greek("αὐτῷ", "G846"), Greek("καὶ", "G2532"), Greek("ἐκράτησαν", "G2902"), Greek("αὐτόν", "G846")],
            Numbered("G08460", "αὐτῶν", "their"),
            Numbered("G08460", "αὐτόν", "him"));

        pairs.Select(pair => (pair.From.Single(), pair.To.Single())).Should().Equal((7L, 109L));
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

    private static string French(string word) => $"fra|{word}";

    private static AlignmentSpan Numbered(string strong, string content, params string[] words) =>
        new(strong, content, words, 1, 1);

    private static AlignmentSpan Numbered(string strong, string content, string word, int occurrence, int occurrences) =>
        new(strong, content, [word], occurrence, occurrences);

    private static string Greek(string word) => $"grc|{word}";

    /// <summary>A Nestle word with the Strong number the corpus gives it.</summary>
    private static string Greek(string word, string strong) => $"grc|{word}#{strong}";

    private static string Hebrew(string word) => $"hbo|{word}";

    /// <summary>A word printed against the next one, as BHSA prints a conjunction before its word.</summary>
    private static string Attached(string tagged) => $"{tagged}+";

    private static InterlinearWord Word(long id, string tagged)
    {
        var language = tagged[..3];
        var mark = tagged.IndexOf('#');
        var strong = mark > 0 ? tagged[(mark + 1)..] : null;
        tagged = mark > 0 ? tagged[..mark] : tagged;
        var attached = tagged.EndsWith('+');
        var written = attached ? tagged[4..^1] : tagged[4..];
        return new InterlinearWord(id, WordFolding.Fold(written, language), language, written, strong, attached);
    }
}
