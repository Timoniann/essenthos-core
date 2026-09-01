using Essenthos.Core.Loading.Links;
using FluentAssertions;
using Xunit;

namespace Essenthos.Core.Tests;

/// <summary>
/// Merging the two routes to the same Hebrew word. What matters here is that agreement is worth
/// something, that it is not worth everything, and that neither route can quietly overwrite the
/// other.
/// </summary>
public class RouteTests
{
    [Fact]
    public void APairOnlyTheDirectRouteFindsKeepsItsOwnConfidence()
    {
        var merged = Routes.Merge([(1, 10, 0.42)], []);

        merged.Should().ContainSingle().Which.Should().Be(new RoutedLink(1, 10, 0.42, Route.Direct));
    }

    /// <summary>
    /// The case that motivated this. "отделил" has no direct link at all, and reaches וַיַּבְדֵּל
    /// through "divided", which the file states.
    /// </summary>
    [Fact]
    public void APairOnlyTheComposedRouteFindsIsAdded()
    {
        var merged = Routes.Merge([], [(1, 10, 0.71)]);

        merged.Should().ContainSingle().Which.Should().Be(new RoutedLink(1, 10, 0.71, Route.Composed));
    }

    [Fact]
    public void AgreementIsWorthMoreThanEitherRouteAlone()
    {
        var merged = Routes.Merge([(1, 10, 0.26)], [(1, 10, 0.60)]).Single();

        merged.Route.Should().Be(Route.Both);
        merged.Confidence.Should().BeApproximately(0.704, 0.001);
        merged.Confidence.Should().BeGreaterThan(0.60);
    }

    /// <summary>
    /// The two routes read the same verses, so their mistakes are not independent. However often
    /// they agree, the pair is still inferred, and the schema keeps a number on it saying so.
    /// </summary>
    [Fact]
    public void AgreementNeverAmountsToCertainty()
    {
        Routes.Merge([(1, 10, 0.99)], [(1, 10, 0.99)]).Single().Confidence.Should().Be(Routes.Ceiling);
        Routes.Ceiling.Should().BeLessThan(1);
    }

    /// <summary>
    /// Two English words can lead to one Hebrew word — "it was good" is three of them against טוֹב —
    /// so the composed route reaches the same pair by several paths. That is one claim found twice
    /// over the same evidence, and combining it with itself would manufacture confidence.
    /// </summary>
    [Fact]
    public void TheSameRouteArrivingTwiceIsNotTreatedAsAgreement()
    {
        var merged = Routes.Merge([], [(1, 10, 0.50), (1, 10, 0.60)]).Single();

        merged.Confidence.Should().Be(0.60);
        merged.Route.Should().Be(Route.Composed);
    }

    [Fact]
    public void EachPairIsMergedOnItsOwn()
    {
        var merged = Routes.Merge([(1, 10, 0.3), (2, 20, 0.4)], [(2, 20, 0.5), (3, 30, 0.6)]);

        merged.Should().HaveCount(3);
        merged.Should().ContainSingle(link => link.Route == Route.Both).Which.From.Should().Be(2);
    }

    [Fact]
    public void NeitherRouteOverwritesTheOtherWithSomethingWeaker()
    {
        var merged = Routes.Merge([(1, 10, 0.90)], [(1, 10, 0.10)]).Single();

        merged.Confidence.Should().BeGreaterThan(0.90);
    }
}
