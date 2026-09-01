using Essenthos.Core.Loading.Links;
using FluentAssertions;
using Xunit;

namespace Essenthos.Core.Tests;

/// <summary>
/// Merging the routes to the same Hebrew word. What matters is that agreement is worth something,
/// that it is not worth everything, and that no route can quietly overwrite another.
/// </summary>
public class RouteTests
{
    private static (Route, IEnumerable<(long, long, double)>) Written(params (long, long, double)[] pairs) =>
        (Route.Written, pairs);

    private static (Route, IEnumerable<(long, long, double)>) Reduced(params (long, long, double)[] pairs) =>
        (Route.Reduced, pairs);

    private static (Route, IEnumerable<(long, long, double)>) Composed(params (long, long, double)[] pairs) =>
        (Route.Composed, pairs);

    [Fact]
    public void APairOnlyOneRouteFindsKeepsItsOwnConfidence()
    {
        var merged = Routes.Merge(Written((1, 10, 0.42)), Reduced(), Composed());

        merged.Should().ContainSingle().Which.Should().Be(new RoutedLink(1, 10, 0.42, Route.Written));
    }

    /// <summary>
    /// The case that made a third route necessary. "безвидна" matched תֹהוּ at 0.98 on the words as
    /// written and at 0.15 once every form of "быть" had become one frequent stem competing with it.
    /// Reducing the forms is right about most words and wrong about this one, so it does not get to
    /// overrule the reading that saw it.
    /// </summary>
    [Fact]
    public void AReadingThatLostAWordDoesNotOverruleTheOneThatFoundIt()
    {
        var merged = Routes.Merge(Written((1, 10, 0.98)), Reduced((1, 10, 0.15)), Composed()).Single();

        merged.Confidence.Should().BeGreaterThanOrEqualTo(0.98);
        merged.Route.Should().Be(Route.Written | Route.Reduced);
    }

    [Fact]
    public void AgreementIsWorthMoreThanAnyRouteAlone()
    {
        var merged = Routes.Merge(Written((1, 10, 0.26)), Reduced(), Composed((1, 10, 0.60))).Single();

        merged.Route.Should().Be(Route.Written | Route.Composed);
        merged.Confidence.Should().BeApproximately(0.704, 0.001);
    }

    [Fact]
    public void AllThreeAgreeingCountsAllThree()
    {
        var merged = Routes.Merge(
            Written((1, 10, 0.4)), Reduced((1, 10, 0.4)), Composed((1, 10, 0.4))).Single();

        merged.Route.Should().Be(Route.Written | Route.Reduced | Route.Composed);
        merged.Confidence.Should().BeApproximately(0.784, 0.001);
    }

    /// <summary>
    /// The routes read the same verses, so their mistakes are not independent. However many of them
    /// agree, the pair is still inferred, and the schema keeps a number on it saying so.
    /// </summary>
    [Fact]
    public void AgreementNeverAmountsToCertainty()
    {
        Routes.Merge(Written((1, 10, 0.99)), Reduced((1, 10, 0.99)), Composed((1, 10, 0.99)))
            .Single().Confidence.Should().Be(Routes.Ceiling);

        Routes.Ceiling.Should().BeLessThan(1);
    }

    /// <summary>
    /// Two English words can lead to one Hebrew word — "it was good" is three of them against טוֹב —
    /// so one route reaches the same pair by several paths. That is one claim found twice over the
    /// same evidence, and combining it with itself would manufacture confidence.
    /// </summary>
    [Fact]
    public void TheSameRouteArrivingTwiceIsNotTreatedAsAgreement()
    {
        var merged = Routes.Merge(Composed((1, 10, 0.50), (1, 10, 0.60))).Single();

        merged.Confidence.Should().Be(0.60);
        merged.Route.Should().Be(Route.Composed);
    }

    [Fact]
    public void EachPairIsMergedOnItsOwn()
    {
        var merged = Routes.Merge(Written((1, 10, 0.3), (2, 20, 0.4)), Composed((2, 20, 0.5), (3, 30, 0.6)));

        merged.Should().HaveCount(3);
        merged.Should().ContainSingle(link => link.Route.HasFlag(Route.Composed) && link.Route.HasFlag(Route.Written))
            .Which.From.Should().Be(2);
    }

    [Fact]
    public void TheSourceNamesEveryReadingThatFoundIt()
    {
        Routes.Describe(Route.Written | Route.Composed, "kjv")
            .Should().Be("SIL.Machine, aligned as written and through kjv");
        Routes.Describe(Route.Reduced, "kjv").Should().Be("SIL.Machine, aligned as stems");
    }
}
