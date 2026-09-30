using Essenthos.Core.Loading.Links.Evidentia;
using FluentAssertions;
using Xunit;

namespace Essenthos.Core.Tests;

/// <summary>
/// The answer key cut to the parts of a written Hebrew word: a grammatical word of a link on the prefix
/// of its own kind, every other word on the stem, and the measurement by state that reads it.
/// </summary>
public class EvidentiaKeySplitTests
{
    private static readonly EvidentiaAddress Verse = new(27, 10, 1);

    private static readonly LanguagePackRegistry Packs = new([new EnglishLanguagePack(), new OriginalLanguagePack()]);

    [Fact]
    public void AContentWordKeepsTheStemAndAGrammaticalWordTakesThePrefixOfItsKind()
    {
        var and = English(1, "and", "CCONJ");
        var the = English(2, "the", "DET");
        var earth = English(3, "earth", "NOUN");
        var said = English(4, "said", "VERB");
        var vav = Hebrew(11, "וְ", "H9000", "conj", joined: true);
        var article = Hebrew(12, "הָ", "H9009", "art", joined: true);
        var land = Hebrew(13, "אָרֶץ", "H776", "subs");
        var narrative = Hebrew(14, "וַ", "H9000", "conj", joined: true);
        var say = Hebrew(15, "יֹּאמֶר", "H559", "verb");

        var key = Split([and, the, earth, said], [vav, article, land, narrative, say], ([1, 2, 3], [11, 12, 13]), ([4], [14, 15]));

        key.Pairs.Should().BeEquivalentTo([(1L, 11L), (2L, 12L), (3L, 13L), (4L, 15L)]);
        key.Kinds[(1, 11)].Should().Be(EvidentiaKeyPairKind.PrefixOfItsKind);
        key.Kinds[(3, 13)].Should().Be(EvidentiaKeyPairKind.Stem);
        key.UnclaimedPrefixes.Should().Equal(14L);
        key.PairsAsLoaded.Should().Be(11);
    }

    [Fact]
    public void AnArticleTheOriginalDoesNotWriteStaysWithItsNounAndIsMarked()
    {
        var of = English(1, "of", "ADP");
        var the = English(2, "the", "DET");
        var king = English(3, "king", "NOUN");
        var lamed = Hebrew(11, "לְ", "H9005", "prep", joined: true);
        var melek = Hebrew(12, "מֶלֶךְ", "H4428", "subs");

        var key = Split([of, the, king], [lamed, melek], ([1, 2, 3], [11, 12]));

        key.Pairs.Should().BeEquivalentTo([(1L, 11L), (2L, 12L), (3L, 12L)]);
        key.Kinds[(2, 12)].Should().Be(EvidentiaKeyPairKind.UnwrittenArticle);
    }

    [Fact]
    public void NowOfAndNowIsTheAdverbAndThenBeforeAVerbIsTheConjunction()
    {
        var and = English(1, "And", "CCONJ");
        var now = English(2, "now", "ADV");
        var then = English(3, "Then", "ADV");
        var went = English(4, "went", "VERB");
        var vav = Hebrew(11, "וְ", "H9000", "conj", joined: true);
        var attah = Hebrew(12, "עַתָּה", "H6258", "advb");
        var narrative = Hebrew(13, "וַ", "H9000", "conj", joined: true);
        var go = Hebrew(14, "יֵּלֶךְ", "H1980", "verb");

        var key = Split([and, now, then, went], [vav, attah, narrative, go], ([1, 2], [11, 12]), ([3, 4], [13, 14]));

        key.Pairs.Should().BeEquivalentTo([(1L, 11L), (2L, 12L), (3L, 13L), (4L, 14L)]);

        var alone = Split([now], [vav, attah], ([2], [11, 12]));
        alone.Pairs.Should().BeEquivalentTo([(2L, 12L)]);
        alone.UnclaimedPrefixes.Should().Equal(11L);
    }

    [Fact]
    public void APrepositionKeepsAStemNoWordOfMeaningIsLeftForAndAPrepositionWrittenOnIt()
    {
        var before = English(1, "before", "ADP");
        var you = English(2, "You", "PROPN");
        var above = English(3, "above", "ADP");
        var theWord = English(4, "the", "DET");
        var earth = English(5, "earth", "NOUN");
        var lamed = Hebrew(11, "לְ", "H9005", "prep", joined: true);
        var face = Hebrew(12, "פָנֶיךָ", "H6440", "subs");
        var min = Hebrew(13, "מֵ", "H4480", "prep", joined: true);
        var al = Hebrew(14, "עַל", "H5921", "prep");
        var article = Hebrew(15, "הָ", "H9009", "art", joined: true);
        var land = Hebrew(16, "אָרֶץ", "H776", "subs");

        var key = Split([before, you, above, theWord, earth], [lamed, face, min, al, article, land],
            ([1, 2], [11, 12]), ([3, 4, 5], [13, 14, 15, 16]));

        key.Pairs.Should().BeEquivalentTo(
            [(1L, 11L), (1L, 12L), (2L, 12L), (3L, 13L), (3L, 14L), (4L, 15L), (5L, 14L), (5L, 16L)]);
    }

    [Fact]
    public void ASecondWordOfAKindSharesThePrefixOnlyWhereItStandsDirectlyAfterTheFirst()
    {
        var because = English(1, "because", "ADP");
        var of = English(2, "of", "ADP");
        var vision = English(3, "vision", "NOUN");
        var to = English(4, "to", "PART");
        var lift = English(5, "lift", "VERB");
        var up = English(6, "up", "ADP");
        var bet = Hebrew(11, "בַּ", "H9003", "prep", joined: true);
        var sight = Hebrew(12, "מַּרְאָה", "H4759", "subs");
        var lamed = Hebrew(13, "לְ", "H9005", "prep", joined: true);
        var raise = Hebrew(14, "הָרִים", "H7311", "verb");

        var key = Split([because, of, vision, to, lift, up], [bet, sight, lamed, raise], ([1, 2, 3], [11, 12]), ([4, 5, 6], [13, 14]));

        key.Pairs.Should().BeEquivalentTo([(1L, 11L), (2L, 11L), (3L, 12L), (4L, 13L), (5L, 14L), (6L, 14L)]);
        key.Doubts.Select(doubt => (doubt.Kind, doubt.SourceWordId)).Should().BeEquivalentTo(
            [(EvidentiaKeySplit.DoubtGenitive, 2L), (EvidentiaKeySplit.DoubtLaterWord, 6L)]);
    }

    [Fact]
    public void AGreekLinkIsKeptAsItIs()
    {
        var the = English(1, "the", "DET");
        var word = English(2, "word", "NOUN");
        var article = Greek(11, "ὁ", "G3588", "det");
        var logos = Greek(12, "λόγος", "G3056", "noun");

        Split([the, word], [article, logos], ([1, 2], [11, 12])).Pairs
            .Should().BeEquivalentTo([(1L, 11L), (1L, 12L), (2L, 11L), (2L, 12L)]);
    }

    [Fact]
    public void AWordIsLinkedOnItsOwnPartAttachedOnItsHeadsWordSuppliedOrUnresolved()
    {
        var and = English(1, "and", "CCONJ");
        var he = English(2, "he", "PRON", ("PronType", "Prs"), ("Person", "3"), ("Number", "Sing"), ("Case", "Nom"), ("Gender", "Masc"));
        var said = English(3, "said", "VERB");
        var the = English(4, "the", "DET");
        var king = English(5, "king", "NOUN");
        var in_ = English(6, "in", "ADP");
        var vav = Hebrew(11, "וַ", "H9000", "conj", joined: true);
        var say = Hebrew(12, "יֹּאמֶר", "H559", "verb", false, ("person", "p3"), ("number", "sg"), ("gender", "m"));
        var melek = Hebrew(13, "מֶלֶךְ", "H4428", "subs");
        EvidentiaToken[] source = [and, he, said, the, king, in_];
        EvidentiaToken[] target = [vav, say, melek];
        var gold = Gold(([1, 2, 3], [11, 12]), ([4, 5], [13]), ([6], [13]));
        var english = EvidentiaAuxiliaryWords.Mark([.. source.Select(Analysis)]);
        var original = target.Select(Analysis).ToList();
        List<EvidentiaProposal> lexical =
        [
            new(Analysis(said), Analysis(say), EvidentiaProposalKind.GlobalReviewKnownRendering, 0.8),
            new(Analysis(king), Analysis(melek), EvidentiaProposalKind.GlobalReviewKnownRendering, 0.8),
        ];
        var attached = EvidentiaAttachedWords.Resolve(english, original, lexical);
        List<EvidentiaProposal> proposals = [.. lexical, .. attached];
        var absences = EvidentiaAbsences.Resolve(english, original, proposals);
        var key = EvidentiaKeySplit.Of(gold, english, original);
        var accepted = EvidentiaWordScore.Of(english, original, proposals, absences, gold with { Pairs = key.Pairs }, out var verdicts).Accepted!;

        var measure = EvidentiaStateScore.Of(
            english, proposals, absences, verdicts, gold, key, accepted, new HashSet<(long, long)> { (3, 12) }, out var words);

        words[1].State.Should().Be(EvidentiaWordState.Linked);
        words[1].Rule.Should().Be(nameof(EvidentiaAttachment.Conjunction));
        words[2].State.Should().Be(EvidentiaWordState.Attached);
        words[2].HeadWordId.Should().Be(3);
        words[3].State.Should().Be(EvidentiaWordState.Linked);
        words[3].Rule.Should().Be(EvidentiaStateScore.LexicalRule);
        words[4].State.Should().Be(EvidentiaWordState.Supplied);
        words[4].Right.Should().BeTrue();
        words[6].State.Should().Be(EvidentiaWordState.Unresolved);
        measure.Of(EvidentiaWordState.Linked).Should().Be(new EvidentiaStateCount(3, 3, 3, 1, 1));
        measure.Of(EvidentiaWordState.Attached).Should().Be(new EvidentiaStateCount(1, 1, 1, 0, 0));
        measure.Of(EvidentiaWordState.Supplied).Should().Be(new EvidentiaStateCount(1, 1, 1, 0, 0));
        measure.Of(EvidentiaWordState.Unresolved).Words.Should().Be(1);
        measure.SplitPairs.Should().Be(6);
        measure.KeyPairsAsLoaded.Should().Be(9);
        measure.Final.Should().Be(new EvidentiaTierScore(4, 4, 4));
        measure.UnwrittenArticlesSupplied.Should().Be(1);
        (measure + measure).Of(EvidentiaWordState.Linked).Words.Should().Be(6);
        measure.Report().Should().Contain("by state, attached: 1/6").And.Contain("split key: 6 pairs of 9 as loaded");
    }

    [Fact]
    public void AnUnresolvedGrammaticalWordSaysWhetherItsHeadIsPlaced()
    {
        var in_ = English(1, "in", "ADP");
        var house = English(2, "house", "NOUN");
        var of = English(3, "of", "ADP");
        var bread = English(4, "bread", "NOUN");
        var bet = Hebrew(11, "בְּ", "H9003", "prep", joined: true);
        var bayit = Hebrew(12, "בֵית", "H1004", "subs");
        var lechem = Hebrew(13, "לֶחֶם", "H3899", "subs");
        var gold = Gold(([1, 2], [11, 12]), ([3, 4], [13]));
        var english = EvidentiaAuxiliaryWords.Mark([.. new[] { in_, house, of, bread }.Select(Analysis)]);
        var original = new[] { bet, bayit, lechem }.Select(Analysis).ToList();
        List<EvidentiaProposal> proposals = [new(Analysis(bread), Analysis(lechem), EvidentiaProposalKind.GlobalReviewKnownRendering, 0.8)];
        var key = EvidentiaKeySplit.Of(gold, english, original);

        var measure = EvidentiaStateScore.Of(
            english, proposals, [], new Dictionary<long, bool?>(), gold, key,
            new HashSet<(long, long)> { (4, 13) }, new HashSet<(long, long)>(), out var words);

        words[1].HeadState.Should().Be(EvidentiaHeadState.UnplacedWithCounterpart);
        words[3].HeadState.Should().Be(EvidentiaHeadState.Placed);
        words[2].HeadState.Should().BeNull();
        measure.Cascade.Should().BeEquivalentTo(new Dictionary<EvidentiaHeadState, int>
        {
            [EvidentiaHeadState.UnplacedWithCounterpart] = 1,
            [EvidentiaHeadState.Placed] = 1,
        });
        measure.UnresolvedGrammatical.Should().Be(2);
    }

    [Fact]
    public void AWordPlacedOnAPrefixItsLinkDoesNotReachIsLeftForAPersonToJudge()
    {
        var in_ = English(1, "In", "ADP");
        var those = English(2, "those", "DET");
        var days = English(3, "days", "NOUN");
        var bet = Hebrew(11, "בַּ", "H9003", "prep", joined: true);
        var yamim = Hebrew(12, "יָּמִים", "H3117", "subs");
        var article = Hebrew(13, "הָ", "H9009", "art", joined: true);
        var hem = Hebrew(14, "הֵם", "H1992", "prde");
        var gold = Gold(([1, 2], [13, 14]), ([3], [11, 12]));
        var english = new[] { in_, those, days }.Select(Analysis).ToList();
        var original = new[] { bet, yamim, article, hem }.Select(Analysis).ToList();
        var key = EvidentiaKeySplit.Of(gold, english, original);
        List<EvidentiaProposal> proposals =
        [
            new(Analysis(days), Analysis(yamim), EvidentiaProposalKind.GlobalReviewKnownRendering, 0.8),
            new(Analysis(in_), Analysis(bet), EvidentiaProposalKind.AttachedWord, 0.7),
        ];

        var doubts = EvidentiaKeyDoubts.Of(gold, key, english, original, proposals, new HashSet<(long, long)> { (3, 12) });

        key.Pairs.Should().BeEquivalentTo([(1L, 14L), (2L, 14L), (3L, 12L)]);
        doubts.Select(doubt => (doubt.SourceWordId, doubt.Kind[..1])).Should().Equal((1L, "B"), (1L, "C"));
        doubts[0].Note.Should().Be("counted wrong");
    }

    private static EvidentiaSplitKey Split(
        IReadOnlyList<EvidentiaToken> source,
        IReadOnlyList<EvidentiaToken> target,
        params (long[] Source, long[] Target)[] links) =>
        EvidentiaKeySplit.Of(Gold(links), [.. source.Select(Analysis)], [.. target.Select(Analysis)]);

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

    private static EvidentiaToken English(long id, string surface, string partOfSpeech, params (string Name, string Value)[] morphology) =>
        new(id, Verse, (int)id, surface, "eng", Trailer: " ", PartOfSpeech: partOfSpeech,
            Morphology: morphology.Length == 0 ? null : morphology.ToDictionary(feature => feature.Name, feature => feature.Value));

    private static EvidentiaToken Hebrew(
        long id, string surface, string strong, string partOfSpeech, bool joined = false,
        params (string Name, string Value)[] morphology) =>
        new(id, Verse, (int)id, surface, "hbo", Trailer: joined ? string.Empty : " ", StrongNumber: strong,
            PartOfSpeech: partOfSpeech,
            Morphology: morphology.Append(("pos", partOfSpeech)).ToDictionary(feature => feature.Item1, feature => feature.Item2));

    private static EvidentiaToken Greek(long id, string surface, string strong, string partOfSpeech) =>
        new(id, Verse, (int)id, surface, "grc", Trailer: " ", Lemma: surface, StrongNumber: strong, PartOfSpeech: partOfSpeech);
}
