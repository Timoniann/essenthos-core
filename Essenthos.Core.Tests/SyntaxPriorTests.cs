using Essenthos.Core.Loading.Links;
using FluentAssertions;
using Xunit;

namespace Essenthos.Core.Tests;

/// <summary>
/// Reading BHSA's own syntax over what the aligner proposed.
///
/// The verse behind these is Genesis 1:1 — <em>בְּרֵאשִׁית בָּרָא אֱלֹהִים אֵת הַשָּׁמַיִם וְאֵת הָאָרֶץ</em>, three
/// clauses' worth of structure in one, with <em>the heaven</em> and <em>the earth</em> each a phrase
/// of their own. Words 10 to 16 stand for its Hebrew, and the English positions for the King
/// James's rendering of it.
/// </summary>
public class SyntaxPriorTests
{
    private const long SubjectPhrase = 100;
    private const long ObjectPhrase = 101;
    private const long SecondObjectPhrase = 102;
    private const long FirstClause = 200;
    private const long SecondClause = 201;
    private const long OneSentence = 300;
    private const long AnotherSentence = 301;

    /// <summary>
    /// Six Hebrew words: two in the subject phrase of the first clause, two more in an object
    /// phrase of the same clause, one in a second phrase of that clause, and one in a clause of a
    /// different sentence altogether.
    /// </summary>
    private static readonly SyntaxPrior Verse = SyntaxPrior.Of(
        (10, SubjectPhrase, FirstClause, OneSentence),
        (11, SubjectPhrase, FirstClause, OneSentence),
        (12, ObjectPhrase, FirstClause, OneSentence),
        (13, ObjectPhrase, FirstClause, OneSentence),
        (14, SecondObjectPhrase, FirstClause, OneSentence),
        (15, 0, SecondClause, AnotherSentence));

    private static readonly IReadOnlyList<long> Words = [10, 11, 12, 13, 14, 15];

    private static (int, int, double, double) Pair(int source, int target, double confidence) =>
        (source, target, confidence, 0.5);

    [Fact]
    public void AWordBesideOneAnsweredInsideTheSamePhraseSharesIt()
    {
        var judged = Verse.Judge([Pair(0, 0, 0.90), Pair(1, 1, 0.30)], Words);

        judged[1].Should().Be(Cohesion.Phrase);
    }

    [Fact]
    public void AWordBesideOneAnsweredElsewhereInTheClauseSharesTheClause()
    {
        var judged = Verse.Judge([Pair(0, 0, 0.90), Pair(1, 2, 0.30)], Words);

        judged[1].Should().Be(Cohesion.Clause);
    }

    /// <summary>
    /// The mistake this exists to catch: a lexically plausible word taken from a different sentence
    /// of a long verse while every neighbour landed in one place.
    /// </summary>
    [Fact]
    public void AWordReachingOutOfEveryNeighboursSentenceStandsApart()
    {
        var judged = Verse.Judge([Pair(0, 0, 0.90), Pair(1, 5, 0.30)], Words);

        judged[1].Should().Be(Cohesion.Apart);
        SyntaxPrior.Shift(0.30, Cohesion.Apart).Should().BeLessThan(0.30);
    }

    [Fact]
    public void AWordInAnotherClauseOfTheSameSentenceIsNeitherApartNorTogether()
    {
        var sentence = SyntaxPrior.Of(
            (10, SubjectPhrase, FirstClause, OneSentence),
            (11, 0, SecondClause, OneSentence));

        sentence.Judge([Pair(0, 0, 0.90), Pair(1, 1, 0.30)], [10, 11])[1]
            .Should().Be(Cohesion.Sentence);
    }

    /// <summary>
    /// A doubtful answer is not evidence about its neighbours, because using one as an anchor
    /// spreads whatever it got wrong to the words on either side of it.
    /// </summary>
    [Fact]
    public void AFaintAnswerIsNotAnAnchor()
    {
        var judged = Verse.Judge([Pair(0, 0, 0.30), Pair(1, 5, 0.30)], Words);

        judged[1].Should().Be(Cohesion.Alone);
    }

    /// <summary>
    /// Two source words at the same score on different targets is the model reporting that it
    /// cannot choose. Neither answer is a claim, so neither is worth measuring the others against.
    /// </summary>
    [Fact]
    public void AWordTheModelCannotChooseForIsNotAnAnchor()
    {
        var judged = Verse.Judge([Pair(0, 0, 0.90), Pair(0, 4, 0.90), Pair(1, 5, 0.30)], Words);

        judged[2].Should().Be(Cohesion.Alone);
    }

    [Fact]
    public void AWordFourAwayIsTooFarToBeEvidence()
    {
        var judged = Verse.Judge([Pair(0, 0, 0.90), Pair(4, 5, 0.30)], Words);

        judged[1].Should().Be(Cohesion.Alone);
    }

    /// <summary>
    /// The whole window is asked and the closest relation any of it offers is the answer, which is
    /// what makes standing apart a statement about every neighbour rather than about one.
    /// </summary>
    [Fact]
    public void SharingAPhraseWithAnyNeighbourOutweighsSharingOnlyAClauseWithAnother()
    {
        var judged = Verse.Judge(
            [Pair(0, 4, 0.90), Pair(1, 0, 0.30), Pair(2, 1, 0.90)], Words);

        judged[1].Should().Be(Cohesion.Phrase);
    }

    /// <summary>
    /// Every Greek witness and every translation here is loaded without a syntactic analysis, so
    /// the ordinary case for most pairs is that there is nothing to read and nothing may change.
    /// </summary>
    [Fact]
    public void ATextWithNoSyntaxLeavesEveryConfidenceExactlyAsItWas()
    {
        var none = SyntaxPrior.Of();

        none.Known.Should().BeFalse();
        none.Rescore([Pair(0, 0, 0.42)], [10]).Single().Confidence.Should().Be(0.42);
    }

    [Fact]
    public void RescoringMovesAPairInTheDirectionItsReadingDeserves()
    {
        var rescored = Verse.Rescore([Pair(0, 0, 0.90), Pair(1, 1, 0.30), Pair(2, 5, 0.30)], Words);

        rescored[1].Confidence.Should().BeGreaterThan(0.30);
        rescored[2].Confidence.Should().BeLessThan(0.30);
        rescored[0].Source.Should().Be(0);
        rescored.Should().HaveCount(3);
    }

    /// <summary>
    /// The syntax is a check on whether an answer coheres, not a second witness to what the words
    /// mean, so it may sharpen a guess and never turn one into a citation.
    /// </summary>
    [Fact]
    public void NoReadingCarriesAPairToCertainty()
    {
        SyntaxPrior.Shift(1.0, Cohesion.Phrase).Should().Be(Routes.Ceiling);
        SyntaxPrior.Shift(0.0, Cohesion.Apart).Should().BeGreaterThan(0);
        SyntaxPrior.Shift(0.98, Cohesion.Phrase).Should().BeLessThanOrEqualTo(Routes.Ceiling);
    }
}
