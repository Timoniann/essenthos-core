using Essenthos.Core.Loading.Links;
using FluentAssertions;
using Xunit;

namespace Essenthos.Core.Tests;

/// <summary>
/// Which composed answers are written: each combination of readings from the confidence at which its
/// answers on stated words were right often enough, measured on the pair being written.
/// </summary>
public class AdmissionTests
{
    private const Route Agreed = Route.Reduced | Route.Composed;
    private const Route Lone = Route.Written;

    [Fact]
    public void ACombinationThatIsRightOftenEnoughIsWrittenFromTheThreshold()
    {
        var (answers, statements) = Answers(Agreed, right: 95, wrong: 5, count: 400);

        var admission = Admission.Measure(answers, statements, 0.25);

        var lowest = answers.Min(answer => answer.Confidence);
        admission.Floors[Agreed].Confidence.Should().Be(lowest);
        admission.Admits(new RoutedLink(9_999, 1, lowest, Agreed)).Should().BeTrue();
    }

    /// <summary>
    /// A lone reading whose confidence does not rank its answers is refused outright: no confidence
    /// on it is worth writing, and pretending one is would only move the line through the noise.
    /// </summary>
    [Fact]
    public void ACombinationThatIsWrongTooOftenAtEveryConfidenceIsRefused()
    {
        var (answers, statements) = Answers(Lone, right: 65, wrong: 35, count: 400);

        var admission = Admission.Measure(answers, statements, 0.25);

        admission.Floors[Lone].Refused.Should().BeTrue();
        admission.Admits(new RoutedLink(9_999, 1, 0.99, Lone)).Should().BeFalse();
    }

    /// <summary>Where the confidence does rank the answers, the floor is drawn where they turn good.</summary>
    [Fact]
    public void TheFloorIsWhereTheAnswersAboveItAreGoodEnough()
    {
        var answers = new List<RoutedLink>();
        var statements = new Dictionary<long, HashSet<long>>();
        for (var i = 0; i < 1_000; i++)
        {
            var confidence = 0.25 + 0.7 * i / 1_000.0;
            var right = confidence >= 0.6 || i % 2 == 0;
            answers.Add(new RoutedLink(i, right ? 1 : 2, confidence, Lone));
            statements[i] = [1];
        }

        var floor = Admission.Measure(answers, statements, 0.25).Floors[Lone];

        floor.Refused.Should().BeFalse();
        floor.Confidence.Should().BeInRange(0.45, 0.6);
        floor.Precision.Should().BeGreaterThanOrEqualTo(Admission.DefaultPrecision);
    }

    [Fact]
    public void TooFewStatedAnswersKeepTheThreshold()
    {
        var (answers, statements) = Answers(Lone, right: 10, wrong: 90, count: Admission.FewestToMeasure - 1);

        var admission = Admission.Measure(answers, statements, 0.25);

        admission.Floors.Should().BeEmpty();
        admission.Admits(new RoutedLink(9_999, 1, 0.25, Lone)).Should().BeTrue();
        admission.Describe("KJV").Should().Contain("nothing stated");
    }

    /// <summary>Only answers on words a source states can be scored; the others are what a run adds.</summary>
    [Fact]
    public void AnswersOnWordsNothingStatesAreNotScored()
    {
        var (answers, statements) = Answers(Lone, right: 10, wrong: 90, count: 400);
        answers.AddRange(Enumerable.Range(0, 400).Select(i => new RoutedLink(50_000 + i, 1, 0.9, Lone)));

        Admission.Measure(answers, statements, 0.25).Floors[Lone].Scored.Should().Be(400);
    }

    private static (List<RoutedLink> Answers, Dictionary<long, HashSet<long>> Statements) Answers(
        Route route, int right, int wrong, int count)
    {
        var answers = new List<RoutedLink>(count);
        var statements = new Dictionary<long, HashSet<long>>(count);
        for (var i = 0; i < count; i++)
        {
            var isRight = i % (right + wrong) < right;
            answers.Add(new RoutedLink(i, isRight ? 1 : 2, 0.3 + 0.6 * (i % 97) / 97.0, route));
            statements[i] = [1];
        }

        return (answers, statements);
    }
}
