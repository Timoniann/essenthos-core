using Essenthos.Core.Loading.Links.Evidentia;
using FluentAssertions;
using Xunit;

namespace Essenthos.Core.Tests;

/// <summary>
/// Each English word goes on its own part of a written Hebrew word, and a word with no counterpart is
/// said to have none: <em>the</em> the original does not write is supplied, a ו no <em>and</em> of the
/// verse renders is unrendered. The measurement by word counts either as done, and judges an absence
/// by the answer key's links.
/// </summary>
public class EvidentiaAbsenceTests
{
    private static readonly EvidentiaAddress Genesis13 = new(1, 1, 3);

    private static readonly LanguagePackRegistry Packs = new([new EnglishLanguagePack(), new OriginalLanguagePack()]);

    [Fact]
    public void TheGoesOnTheArticleOfItsNounAndIsSuppliedWhereTheOriginalWritesNone()
    {
        var the = English(1, 1, "the", "DET");
        var waters = English(2, 2, "waters", "NOUN");
        var theOther = English(3, 3, "the", "DET");
        var surface = English(4, 4, "surface", "NOUN");
        var article = Hebrew(11, 1, "הַ", "H9009", "art", joined: true);
        var water = Hebrew(12, 2, "מַּיִם", "H4325", "subs");
        var face = Hebrew(13, 3, "פְּנֵי", "H6440", "subs");
        var (attached, absences) = Resolve([the, waters, theOther, surface], [article, water, face], (waters, water), (surface, face));

        attached.Should().Equal((1L, 11L));
        absences.Should().Equal((3L, EvidentiaAbsenceKind.Supplied));
    }

    [Fact]
    public void AndOfAndGodSaidGoesOnTheVavOfTheVerbAndAVavNoWordRendersIsUnrendered()
    {
        var and = English(1, 1, "And", "CCONJ", head: 3);
        var god = English(2, 2, "God", "PROPN", head: 3);
        var said = English(3, 3, "said", "VERB");
        var vav = Hebrew(11, 1, "וַ", "H9000", "conj", joined: true);
        var say = Hebrew(12, 2, "יֹּאמֶר", "H559", "verb");
        var elohim = Hebrew(13, 3, "אֱלֹהִים", "H430", "subs");

        Resolve([and, god, said], [vav, say, elohim], (god, elohim), (said, say)).Attached.Should().Equal((1L, 11L));
        Resolve([god, said], [vav, say, elohim], (god, elohim), (said, say)).Absences
            .Should().Equal((11L, EvidentiaAbsenceKind.NotRendered));
    }

    [Fact]
    public void ACoordinatorPlacedFromAcrossOtherWordsLeavesTheOtherVavsOfTheVerseAsTheyWere()
    {
        var and = English(1, 1, "and", "CCONJ");
        var his = English(2, 2, "his", "PRON");
        var voice = English(3, 3, "voice", "NOUN");
        var came = English(4, 4, "came", "VERB");
        var vav = Hebrew(11, 1, "וְ", "H9000", "conj", joined: true);
        var sound = Hebrew(12, 2, "קוֹל", "H6963", "subs");
        var narrative = Hebrew(13, 3, "וַ", "H9000", "conj", joined: true);
        var come = Hebrew(14, 4, "יָּבֹא", "H935", "verb");

        var across = Resolve([and, his, voice, came], [vav, sound, narrative, come], (voice, sound), (came, come));
        across.Attached.Should().Equal((1L, 11L));
        across.Absences.Should().BeEmpty();

        // Directly before its word it is placed by its own rule, and the other ו is then said unrendered.
        var beside = Resolve([and, voice, came], [vav, sound, narrative, come], (voice, sound), (came, come));
        beside.Attached.Should().Equal((1L, 11L));
        beside.Absences.Should().Equal((13L, EvidentiaAbsenceKind.NotRendered));
    }

    [Fact]
    public void AVavIsNotSaidUnrenderedWhileAnAndOfTheVerseIsUnplacedNorAnObjectMarkerWithASuffix()
    {
        var and = English(1, 1, "and", "CCONJ");
        var god = English(2, 2, "God", "PROPN");
        var vav = Hebrew(11, 1, "וַ", "H9000", "conj", joined: true);
        var elohim = Hebrew(12, 2, "אֱלֹהִים", "H430", "subs");
        var him = Hebrew(13, 3, "אֹתוֹ", "H853", "prep", morphology: ("suffixPerson", "p3"));
        var marker = Hebrew(14, 4, "אֶת", "H853", "prep");
        var light = Hebrew(15, 5, "אוֹר", "H216", "subs");
        var lightWord = English(3, 3, "light", "NOUN");

        Resolve([and, god, lightWord], [vav, elohim, him, marker, light], (god, elohim), (lightWord, light)).Absences
            .Should().Equal((14L, EvidentiaAbsenceKind.NotRendered));
    }

    [Fact]
    public void AnAndTheKeyFoldsIntoTheSubjectCountsRightOnTheVavItFoldsIntoTheVerb()
    {
        var and = English(1, 1, "And", "CCONJ");
        var god = English(2, 2, "God", "PROPN");
        var said = English(3, 3, "said", "VERB");
        var vav = Hebrew(11, 1, "וַ", "H9000", "conj", joined: true);
        var say = Hebrew(12, 2, "יֹּאמֶר", "H559", "verb");
        var elohim = Hebrew(13, 3, "אֱלֹהִים", "H430", "subs");
        var gold = Gold([([1, 2], [13]), ([3], [11, 12])]);

        var measure = Score([and, god, said], [vav, say, elohim], gold, [], (and, vav), (god, elohim), (said, say));

        measure.LinksRightByPair.Should().Be(2);
        measure.LinksRight.Should().Be(3);
        measure.SourceWordsRight.Should().Be(3);
        measure.TargetWordsRight.Should().Be(3);
    }

    [Fact]
    public void AnAbsenceIsWrongWhereTheKeyLinksAWordOfItsOwnKindOrFoldsAConjunctionOnTheOtherSide()
    {
        var the = English(1, 1, "the", "DET");
        var surface = English(2, 2, "surface", "NOUN");
        var theOther = English(3, 3, "the", "DET");
        var waters = English(4, 4, "waters", "NOUN");
        var and = English(5, 5, "And", "CCONJ");
        var god = English(6, 6, "God", "PROPN");
        var said = English(7, 7, "said", "VERB");
        var face = Hebrew(11, 1, "פְּנֵי", "H6440", "subs");
        var article = Hebrew(12, 2, "הַ", "H9009", "art", joined: true);
        var water = Hebrew(13, 3, "מַּיִם", "H4325", "subs");
        var vav = Hebrew(14, 4, "וַ", "H9000", "conj", joined: true);
        var say = Hebrew(15, 5, "יֹּאמֶר", "H559", "verb");
        var elohim = Hebrew(16, 6, "אֱלֹהִים", "H430", "subs");
        EvidentiaToken[] source = [the, surface, theOther, waters, and, god, said];
        EvidentiaToken[] target = [face, article, water, vav, say, elohim];
        var gold = Gold([([1, 2], [11]), ([3, 4], [12, 13]), ([5, 6], [16]), ([7], [14, 15])]);

        var measure = Score(source, target, gold,
            [(the, EvidentiaAbsenceKind.Supplied), (theOther, EvidentiaAbsenceKind.Supplied), (vav, EvidentiaAbsenceKind.NotRendered)],
            (surface, face), (waters, water), (god, elohim), (said, say));

        measure.SuppliedJudged.Should().Be(2);
        measure.SuppliedRight.Should().Be(1);
        measure.UnrenderedJudged.Should().Be(1);
        measure.UnrenderedRight.Should().Be(0);
    }

    private static ((long, long)[] Attached, (long, EvidentiaAbsenceKind)[] Absences) Resolve(
        IReadOnlyList<EvidentiaToken> source,
        IReadOnlyList<EvidentiaToken> target,
        params (EvidentiaToken Source, EvidentiaToken Target)[] placed)
    {
        var english = EvidentiaAuxiliaryWords.Mark([.. source.Select(Analysis)]);
        var original = target.Select(Analysis).ToList();
        var proposals = Proposals(placed);
        var attached = EvidentiaAttachedWords.Resolve(english, original, proposals);
        var absences = EvidentiaAbsences.Resolve(english, original, [.. proposals, .. attached]);
        return (
            [.. attached.Select(proposal => (proposal.Source.Token.Id, proposal.Target.Token.Id))],
            [.. absences.Select(absence => (absence.Word.Token.Id, absence.Kind))]);
    }

    private static EvidentiaWordMeasure Score(
        IReadOnlyList<EvidentiaToken> source,
        IReadOnlyList<EvidentiaToken> target,
        EvidentiaGold gold,
        IReadOnlyList<(EvidentiaToken Word, EvidentiaAbsenceKind Kind)> absences,
        params (EvidentiaToken Source, EvidentiaToken Target)[] placed) =>
        EvidentiaWordScore.Of(
            [.. source.Select(Analysis)],
            [.. target.Select(Analysis)],
            Proposals(placed),
            [.. absences.Select(absence => new EvidentiaAbsence(
                Analysis(absence.Word),
                absence.Kind == EvidentiaAbsenceKind.Supplied ? EvidentiaAbsenceRule.UnwrittenArticle : EvidentiaAbsenceRule.Conjunction,
                null))],
            gold,
            out _);

    /// <summary>A key of links by word id; a link with no English word is an <c>omits</c>.</summary>
    private static EvidentiaGold Gold(IReadOnlyList<(long[] Source, long[] Target)> links)
    {
        var stated = links.Where(link => link.Source.Length > 0 && link.Target.Length > 0)
            .Select((link, index) => new EvidentiaGoldLink(index, default, "test", link.Source, link.Target))
            .ToList();
        var pairs = stated.SelectMany(link => link.SourceWords.SelectMany(one => link.TargetWords.Select(two => (one, two)))).ToHashSet();
        return new EvidentiaGold(
            pairs,
            pairs.Select(pair => pair.one).ToHashSet(),
            new Dictionary<long, List<EvidentiaGoldLink>>(),
            stated,
            new HashSet<long>(),
            links.Where(link => link.Source.Length == 0 && link.Target.Length == 1).Select(link => link.Target[0]).ToHashSet());
    }

    private static List<EvidentiaProposal> Proposals((EvidentiaToken Source, EvidentiaToken Target)[] placed) =>
    [
        .. placed.Select(pair => new EvidentiaProposal(
            Analysis(pair.Source), Analysis(pair.Target), EvidentiaProposalKind.GlobalReviewKnownRendering, 0.8)),
    ];

    private static EvidentiaAnalysis Analysis(EvidentiaToken token)
    {
        Packs.TryAnalyse(token, out var analysis).Should().BeTrue();
        return analysis;
    }

    private static EvidentiaToken English(long id, int position, string surface, string partOfSpeech, long? head = null) =>
        new(id, Genesis13, position, surface, "eng", Trailer: " ", PartOfSpeech: partOfSpeech, SyntacticHead: head);

    private static EvidentiaToken Hebrew(
        long id, int position, string surface, string strong, string partOfSpeech, bool joined = false,
        params (string Name, string Value)[] morphology) =>
        new(id, Genesis13, position, surface, "hbo", Trailer: joined ? string.Empty : " ", StrongNumber: strong,
            PartOfSpeech: partOfSpeech,
            Morphology: morphology.Append(("pos", partOfSpeech)).ToDictionary(feature => feature.Item1, feature => feature.Item2));
}
