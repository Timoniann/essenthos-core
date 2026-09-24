using Essenthos.Core.Loading.Links;
using FluentAssertions;
using Xunit;

namespace Essenthos.Core.Tests;

/// <summary>
/// A pair nothing states, composed from floors measured on texts in its language that do state the
/// target: an English translation through the King James and the Berean, measured on the Berean
/// through the King James and on the King James through the Berean. Each of those reads only some
/// of the pair's routes, and an answer is written where the composition through that text's middle
/// texts alone would have written it.
/// </summary>
public class BorrowedAdmissionTests
{
    private const Route Direct = Route.Written | Route.Reduced;
    private const Route ThroughKjv = Route.Composed;
    private const Route ThroughBsb = Route.ComposedAgain;
    private const Route ReadByBerean = Direct | ThroughKjv;
    private const Route ReadByKingJames = Direct | ThroughBsb;
    private static readonly string[] Vias = ["KJV", "BSB"];

    [Fact]
    public void AnAnswerTheBereanMeasuredIsWrittenFromTheFloorItMeasured()
    {
        var admission = Borrow(
            Berean((Direct | ThroughKjv, new RouteFloor(0.40, 5_000, 0.94))),
            KingJames((Direct, new RouteFloor(1.01, 800, 0.79))));

        admission.Admits(new RoutedLink(1, 1, 0.39, Direct | ThroughKjv)).Should().BeFalse();
        admission.Admits(new RoutedLink(1, 1, 0.40, Direct | ThroughKjv)).Should().BeTrue();
    }

    /// <summary>
    /// Both middle texts agreeing is a combination neither text can measure, since each is one of
    /// the two. Either one-middle composition would write it, so it takes the lower of their floors.
    /// </summary>
    [Fact]
    public void BothMiddleTextsAgreeingAreWrittenWhereEitherCompositionWouldWriteThem()
    {
        var admission = Borrow(
            Berean((Direct | ThroughKjv, new RouteFloor(0.30, 5_000, 0.95)), (Route.Reduced | ThroughKjv, new RouteFloor(0.25, 900, 0.91))),
            KingJames((Direct | ThroughBsb, new RouteFloor(0.45, 5_000, 0.93)), (Route.Reduced | ThroughBsb, new RouteFloor(1.01, 900, 0.87))));

        admission.Floors[Direct | ThroughKjv | ThroughBsb].Confidence.Should().Be(0.30);
        admission.Floors[Direct | ThroughKjv | ThroughBsb].Basis.Should().Be("as W+R+KJV on BSB");
        admission.Floors[Route.Reduced | ThroughKjv | ThroughBsb].Refused.Should().BeFalse();
    }

    [Fact]
    public void TwoMiddleTextsRefusedAloneStayRefusedTogether()
    {
        var admission = Borrow(
            Berean((ThroughKjv, new RouteFloor(1.01, 9_000, 0.79))),
            KingJames((ThroughBsb, new RouteFloor(1.01, 9_000, 0.56))));

        admission.Admits(new RoutedLink(1, 1, 0.98, ThroughKjv | ThroughBsb)).Should().BeFalse();
    }

    /// <summary>
    /// The King James reads no route through itself, so what it measured for the direct model alone
    /// says nothing about an answer the route through it also reached.
    /// </summary>
    [Fact]
    public void ATextSaysNothingAboutARouteItDidNotRead()
    {
        var admission = Borrow(
            Berean((ThroughKjv, new RouteFloor(1.01, 9_000, 0.79))),
            KingJames((ThroughBsb, new RouteFloor(0.50, 9_000, 0.92))));

        admission.Floors[ThroughKjv].Refused.Should().BeTrue();
        admission.Floors[ThroughBsb].Confidence.Should().Be(0.50);
    }

    [Fact]
    public void NothingMeasuredAnywhereLeavesNoFloorAndSaysSo()
    {
        var admission = Admission.Borrow([], Vias, 0.25);

        admission.Floors.Should().BeEmpty();
        admission.Describe(Vias).Should().Contain("nothing stated");
    }

    [Fact]
    public void TheDescriptionNamesTheTextsItWasMeasuredOn()
    {
        var admission = Borrow(
            Berean((ThroughKjv, new RouteFloor(0.50, 900, 0.92))),
            KingJames((Direct, new RouteFloor(0.70, 800, 0.90))));

        admission.Describe(Vias).Should().Contain("on BSB and KJV").And.Contain("KJV from 0.50");
    }

    /// <summary>
    /// The Berean composed through the King James alone numbers the King James as its first middle
    /// text; so does the pair being written. The King James composed through the Berean numbers the
    /// Berean first, and here it is the second.
    /// </summary>
    [Fact]
    public void AMeasuringTextsRoutesAreRenumberedForThePairBeingWritten()
    {
        CompositionPipeline.Translated(Direct | Route.Composed, [ThroughKjv]).Should().Be(Direct | ThroughKjv);
        CompositionPipeline.Translated(Route.Reduced | Route.Composed, [ThroughBsb]).Should().Be(Route.Reduced | ThroughBsb);
        CompositionPipeline.Translated(Route.Written, [ThroughBsb]).Should().Be(Route.Written);
    }

    private static Admission Borrow(
        (string, Route, IReadOnlyDictionary<Route, RouteFloor>) berean,
        (string, Route, IReadOnlyDictionary<Route, RouteFloor>) kingJames) =>
        Admission.Borrow([berean, kingJames], Vias, 0.25);

    private static (string, Route, IReadOnlyDictionary<Route, RouteFloor>) Berean(
        params (Route Route, RouteFloor Floor)[] floors) =>
        ("BSB", ReadByBerean, floors.ToDictionary(floor => floor.Route, floor => floor.Floor));

    private static (string, Route, IReadOnlyDictionary<Route, RouteFloor>) KingJames(
        params (Route Route, RouteFloor Floor)[] floors) =>
        ("KJV", ReadByKingJames, floors.ToDictionary(floor => floor.Route, floor => floor.Floor));
}
