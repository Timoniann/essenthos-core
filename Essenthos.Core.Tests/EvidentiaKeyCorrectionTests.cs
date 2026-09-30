using Essenthos.Core.Loading.Links.Evidentia;
using FluentAssertions;
using Xunit;

namespace Essenthos.Core.Tests;

/// <summary>
/// The answer key as its readers left it: a placement they found right where the key names another word
/// counts right against the judged key, one they found wrong stays wrong, and what nobody read is scored
/// as the key has it and counted as unread.
/// </summary>
public class EvidentiaKeyCorrectionTests
{
    private const string From = "BSB";

    private const string To = "BHSA";

    private static readonly EvidentiaAddress Verse = new(23, 34, 4);

    private static readonly LanguagePackRegistry Packs = new([new EnglishLanguagePack(), new OriginalLanguagePack()]);

    private static readonly EvidentiaToken[] Source =
    [
        English(1, "and", "CCONJ"), English(2, "all", "DET"), English(3, "their", "PRON"), English(4, "stars", "NOUN"),
    ];

    private static readonly EvidentiaToken[] Target =
    [
        Hebrew(11, "וְ", "H9000", "conj", joined: true), Hebrew(12, "כָל", "H3605", "subs"), Hebrew(13, "צְבָאָם", "H6635", "subs"),
    ];

    /// <summary>and all their stars: the key has the whole chunk on צְבָאָם and nothing on וְכָל.</summary>
    private static readonly (long[], long[])[] Links = [([1, 2, 3, 4], [13])];

    [Fact]
    public void APlacementTheReadersFoundRightCountsRightAgainstTheJudgedKey()
    {
        var measure = Measure([Correction("34:4:1 and", ["34:4:13 צְבָאָם"], ["34:4:11 וְ"], "corrected")], out var words, (1, 11), (4, 13));

        words[1].Right.Should().BeFalse();
        words[1].RightByTheJudgedKey.Should().BeTrue();
        words[1].Reading.Should().Be(EvidentiaKeyVerdict.Corrected);
        words[4].RightByTheJudgedKey.Should().BeTrue();
        words[4].Reading.Should().BeNull();
        measure.Final.Correct.Should().Be(1);
        measure.JudgedFinal.Should().Be(2);
        measure.JudgedLoaded.Should().Be(2);
        measure.Of(EvidentiaWordState.Linked).RightByTheJudgedKey.Should().Be(2);
        measure.Readings.Should().Be(new EvidentiaJudgedCount(1, 1, 0, 0, 0, 0, 0, 1, 0));
        measure.Report().Should()
            .Contain("split key, pairs, three ways: 1/2 (50.00 %) by the key")
            .And.Contain("against the judged key 2/2 (100.00 %)")
            .And.Contain("judged key: corrected 1/1 (100.00 %)")
            .And.Contain("wrong by the key and read by nobody 0/1");
    }

    [Fact]
    public void ACorrectedKeyNoLongerCreditsTheWordItNamed()
    {
        var measure = Measure([Correction("34:4:1 and", ["34:4:13 צְבָאָם"], ["34:4:11 וְ"], "corrected")], out var words, (1, 13));

        words[1].Right.Should().BeTrue();
        words[1].RightByTheJudgedKey.Should().BeFalse();
        measure.Final.Correct.Should().Be(1);
        measure.JudgedFinal.Should().Be(0);
    }

    [Fact]
    public void ADefensibleReadingIsRightAsWellAsTheKeys()
    {
        var corrections = new[] { Correction("34:4:2 all", ["34:4:13 צְבָאָם"], ["34:4:12 כָל"], "defensible") };

        Measure(corrections, out var onTheReading, (2, 12));
        Measure(corrections, out var onTheKey, (2, 13));

        onTheReading[2].Right.Should().BeFalse();
        onTheReading[2].RightByTheJudgedKey.Should().BeTrue();
        onTheKey[2].RightByTheJudgedKey.Should().BeTrue();
    }

    [Fact]
    public void AConfirmedKeyLeavesThePlacementWrongAndCountsItRead()
    {
        var measure = Measure([Correction("34:4:4 stars", ["34:4:13 צְבָאָם"], ["34:4:12 כָל"], "confirmed")], out var words, (4, 12), (1, 12));

        words[4].RightByTheJudgedKey.Should().BeFalse();
        words[4].Reading.Should().Be(EvidentiaKeyVerdict.Confirmed);
        words[1].RightByTheJudgedKey.Should().BeFalse();
        measure.JudgedFinal.Should().Be(0);
        measure.Readings.Confirmed.Should().Be(1);
        measure.Readings.WrongByTheKey.Should().Be(2);
        measure.Readings.Unread.Should().Be(1);
    }

    [Fact]
    public void WhereTheWordBelongsOnNeitherBothStayWrong()
    {
        var corrections = new[] { Correction("34:4:2 all", ["34:4:13 צְבָאָם"], ["34:4:11 וְ"], "neither") };

        Measure(corrections, out var onTheReading, (2, 11));
        Measure(corrections, out var onTheKey, (2, 13));

        onTheReading[2].RightByTheJudgedKey.Should().BeFalse();
        onTheKey[2].Right.Should().BeTrue();
        onTheKey[2].RightByTheJudgedKey.Should().BeFalse();
    }

    [Fact]
    public void AnUnsettledReadingChangesNothingAndIsNotUnread()
    {
        var measure = Measure([Correction("34:4:1 and", ["34:4:13 צְבָאָם"], ["34:4:11 וְ"], "unsettled")], out var words, (1, 11));

        words[1].RightByTheJudgedKey.Should().BeFalse();
        measure.Readings.Unsettled.Should().Be(1);
        measure.Readings.Unread.Should().Be(0);
    }

    [Fact]
    public void AReadingTheTextNoLongerHasIsNotAppliedAndIsCounted()
    {
        var measure = Measure(
            [
                Correction("34:4:1 but", ["34:4:13 צְבָאָם"], ["34:4:11 וְ"], "corrected"),
                Correction("34:4:1 and", ["34:4:13 צְבָאָם"], ["34:4:12 וְ"], "corrected"),
                Correction("35:1:1 and", ["35:1:13 צְבָאָם"], ["35:1:11 וְ"], "corrected"),
                Correction("34:4:1 and", ["34:4:13 צְבָאָם"], ["34:4:11 וְ"], "corrected") with { From = "KJV" },
            ],
            out var words, (1, 11));

        words[1].RightByTheJudgedKey.Should().BeFalse();
        measure.Readings.Readings.Should().Be(0);
        measure.Readings.Stale.Should().Be(2);
        measure.Readings.Unread.Should().Be(1);
    }

    [Fact]
    public void AWordTheReadersFoundSuppliedIsRightlySaidSupplied()
    {
        var english = Source.Select(Analysis).ToList();
        var original = Target.Select(Analysis).ToList();
        var gold = Gold(Links);
        var key = EvidentiaKeySplit.Of(gold, english, original);
        List<EvidentiaAbsence> absences = [new(english[1], EvidentiaAbsenceRule.UnwrittenArticle, null)];
        var judged = EvidentiaJudgedKey.Of(
            [Correction("34:4:2 all", ["34:4:13 צְבָאָם"], [], "corrected") with { Supplied = true }], From, To, english, original);

        var measure = EvidentiaStateScore.Of(
            english, [], absences, new Dictionary<long, bool?> { [2] = false }, gold, key, new HashSet<(long, long)>(),
            new HashSet<(long, long)>(), new Dictionary<(long, long), EvidentiaBoundaryCase>(), out var words, judged);

        words[2].State.Should().Be(EvidentiaWordState.Supplied);
        words[2].Right.Should().BeFalse();
        words[2].RightByTheJudgedKey.Should().BeTrue();
        measure.Of(EvidentiaWordState.Supplied).Should().Be(new EvidentiaStateCount(1, 1, 0, 0, 0, RightByTheJudgedKey: 1));
        measure.Readings.Unread.Should().Be(0);
        measure.Report().Should().Contain("by state, supplied: 1/4 (25.00 %) of words; right 0/1 (0.00 %); against the judged key 1/1 (100.00 %)");
    }

    [Fact]
    public void WithNoReadingTheJudgedKeyIsTheKey()
    {
        var measure = Measure([], out var words, (1, 11), (4, 13));

        words[1].RightByTheJudgedKey.Should().BeFalse();
        words[4].RightByTheJudgedKey.Should().BeTrue();
        measure.JudgedFinal.Should().Be(measure.Final.Correct);
        measure.JudgedLoaded.Should().Be(measure.Loaded.Correct);
        (measure + measure).JudgedFinal.Should().Be(2);
        (measure + measure).Readings.Unread.Should().Be(2);
    }

    [Fact]
    public void AWordIsMatchedAsTheTextPrintsItWithItsQuotationMarks()
    {
        var english = new[] { English(1, "“And", "CCONJ") with { Surface = "“And" } }.Select(Analysis).ToList();
        var original = Target.Select(Analysis).ToList();

        var judged = EvidentiaJudgedKey.Of(
            [Correction("34:4:1 “And", ["34:4:13 צְבָאָם"], ["34:4:11 וְ"], "corrected")], From, To, english, original);

        english[0].Token.Surface.Should().Be("And");
        judged.Stale.Should().Be(0);
        judged.Verdict(1, 11, false).Should().BeTrue();
    }

    [Fact]
    public void AVerdictThatIsNoneOfTheFiveStopsTheRead()
    {
        using var stream = new MemoryStream(
            """{"entries":[{"from":"BSB","to":"BHSA","book":23,"word":"34:4:1 and","key":["34:4:13 x"],"read":["34:4:11 y"],"verdict":"maybe"}]}"""u8.ToArray());

        var read = () => EvidentiaKeyCorrections.Read(stream);

        read.Should().Throw<InvalidOperationException>().WithMessage("*\"maybe\" is not a verdict*corrected*");
    }

    [Fact]
    public void TheShippedReadingsAreWellFormed()
    {
        var shipped = EvidentiaKeyCorrections.All;

        shipped.Should().OnlyContain(entry => entry.From.Length > 0 && entry.To.Length > 0 && entry.Book > 0);
        shipped.Should().OnlyContain(entry => entry.Supplied ? entry.Read.Count == 0 : entry.Read.Count > 0);
        shipped.Should().OnlyContain(entry => entry.Key.Count > 0);
        shipped.Select(entry => (entry.From, entry.To, entry.Book, entry.Word, string.Join("|", entry.Read), string.Join("|", entry.Key)))
            .Should().OnlyHaveUniqueItems();
        foreach (var book in shipped.GroupBy(entry => (entry.From, entry.To, entry.Book)))
        {
            // Every place parses: a reading of another chapter than the words given is skipped, not refused.
            var parse = () => EvidentiaJudgedKey.Of([.. book], book.Key.From, book.Key.To, [Analysis(English(1, "and", "CCONJ"))], []);
            parse.Should().NotThrow();
        }
    }

    private static EvidentiaStateMeasure Measure(
        IReadOnlyList<EvidentiaKeyCorrection> corrections,
        out IReadOnlyDictionary<long, EvidentiaWordStateRecord> words,
        params (long From, long To)[] placed)
    {
        var english = Source.Select(Analysis).ToList();
        var original = Target.Select(Analysis).ToList();
        var gold = Gold(Links);
        var key = EvidentiaKeySplit.Of(gold, english, original);
        List<EvidentiaProposal> proposals =
        [
            .. placed.Select(pair => new EvidentiaProposal(
                english.Single(word => word.Token.Id == pair.From), original.Single(word => word.Token.Id == pair.To),
                EvidentiaProposalKind.GlobalReviewKnownRendering, 0.8)),
        ];
        var accepted = placed.Where(key.Pairs.Contains).ToHashSet();
        var judged = EvidentiaJudgedKey.Of(corrections, From, To, english, original);
        return EvidentiaStateScore.Of(
            english, proposals, [], new Dictionary<long, bool?>(), gold, key, accepted, new HashSet<(long, long)>(),
            new Dictionary<(long, long), EvidentiaBoundaryCase>(), out words, judged);
    }

    private static EvidentiaKeyCorrection Correction(string word, string[] key, string[] read, string verdict) =>
        new(From, To, Verse.Book, word, key, read, verdict);

    private static EvidentiaGold Gold(params (long[] Source, long[] Target)[] links)
    {
        var stated = links.Select((link, index) => new EvidentiaGoldLink(index, default, "test", link.Source, link.Target)).ToList();
        var pairs = stated.SelectMany(link => link.SourceWords.SelectMany(one => link.TargetWords.Select(two => (one, two)))).ToHashSet();
        return new EvidentiaGold(
            pairs,
            pairs.Select(pair => pair.one).ToHashSet(),
            new Dictionary<long, List<EvidentiaGoldLink>>(),
            stated,
            new HashSet<long>(),
            new HashSet<long>());
    }

    private static EvidentiaAnalysis Analysis(EvidentiaToken token)
    {
        Packs.TryAnalyse(token, out var analysis).Should().BeTrue();
        return analysis;
    }

    private static EvidentiaToken English(long id, string surface, string partOfSpeech) =>
        new(id, Verse, (int)id, surface, "eng", Trailer: " ", PartOfSpeech: partOfSpeech);

    private static EvidentiaToken Hebrew(long id, string surface, string strong, string partOfSpeech, bool joined = false) =>
        new(id, Verse, (int)id, surface, "hbo", Trailer: joined ? string.Empty : " ", StrongNumber: strong,
            PartOfSpeech: partOfSpeech, Morphology: new Dictionary<string, string> { ["pos"] = partOfSpeech });
}
