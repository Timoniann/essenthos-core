using Essenthos.Core.Loading.Links;
using FluentAssertions;
using Xunit;

namespace Essenthos.Core.Tests.Links;

/// <summary>
/// A pair nothing states, composed from floors measured on texts in its language that do state the
/// target: an English translation through the King James and the Berean, measured on the Berean
/// through the King James and on the King James through the Berean. Each of those reads only some
/// of the pair's routes, and what it measured is a floor only for answers the routes it could not
/// read also found.
/// </summary>
public class BorrowedAdmissionTests
{
    private const Route Direct = Route.Written | Route.Reduced;
    private const Route ThroughKjv = Route.Composed;
    private const Route ThroughBsb = Route.ComposedAgain;
    private const Route ReadByBerean = Direct | ThroughKjv;
    private const Route ReadByKingJames = Direct | ThroughBsb;
    private static readonly string[] Vias = ["KJV", "BSB"];

    /// <summary>
    /// The Berean measured the direct model and the King James agreeing. Where the route through
    /// the Berean found the same word, that is the easier part of what it measured, and its floor
    /// holds.
    /// </summary>
    [Fact]
    public void AnAnswerEveryRouteFoundIsWrittenFromTheFloorMeasuredForThePartReadThere()
    {
        var admission = Borrow(
            Berean((Direct | ThroughKjv, new RouteFloor(0.40, 5_000, 0.94))),
            KingJames((Direct | ThroughBsb, new RouteFloor(0.60, 5_000, 0.93))));

        var all = Direct | ThroughKjv | ThroughBsb;
        admission.Floors[all].Confidence.Should().Be(0.40);
        admission.Floors[all].Basis.Should().Be("as W+R+KJV on BSB");
        admission.Admits(new RoutedLink(1, 1, 0.39, all)).Should().BeFalse();
        admission.Admits(new RoutedLink(1, 1, 0.40, all)).Should().BeTrue();
    }

    /// <summary>
    /// Where the Berean's route did not find the word, the answer is the harder part of what the
    /// Berean measured, so that measure flatters it. The King James reads the route through the
    /// Berean, and its measure of the direct model alone is a floor here, since the King James route
    /// it could not read did find the word — so it decides.
    /// </summary>
    [Fact]
    public void AnAnswerARouteATextCouldNotReadDidNotFindIsNotJudgedByThatText()
    {
        var admission = Borrow(
            Berean((Direct | ThroughKjv, new RouteFloor(0.25, 5_000, 0.94))),
            KingJames((Direct, new RouteFloor(1.01, 5_000, 0.79))));

        admission.Admits(new RoutedLink(1, 1, 0.98, Direct | ThroughKjv)).Should().BeFalse();
        admission.Floors[Direct | ThroughKjv].Basis.Should().Be("as W+R on KJV");
    }

    /// <summary>The direct model alone is the harder part of what either text measured.</summary>
    [Fact]
    public void AnAnswerNeitherMiddleTextFoundIsMeasuredOnNeither()
    {
        var admission = Borrow(
            Berean((Direct, new RouteFloor(0.25, 5_000, 0.95))),
            KingJames((Direct, new RouteFloor(0.25, 5_000, 0.92))));

        admission.Admits(new RoutedLink(1, 1, 0.98, Direct)).Should().BeFalse();
        admission.Floors[Direct].Scored.Should().Be(0);
    }

    [Fact]
    public void TwoMiddleTextsRefusedAloneStayRefusedTogether()
    {
        var admission = Borrow(
            Berean((ThroughKjv, new RouteFloor(1.01, 9_000, 0.79))),
            KingJames((ThroughBsb, new RouteFloor(1.01, 9_000, 0.56))));

        admission.Admits(new RoutedLink(1, 1, 0.98, ThroughKjv | ThroughBsb)).Should().BeFalse();
    }

    [Fact]
    public void EitherTextsFloorIsEnoughSinceEachIsAFloor()
    {
        var admission = Borrow(
            Berean((Route.Reduced | ThroughKjv, new RouteFloor(0.25, 900, 0.91))),
            KingJames((Route.Reduced | ThroughBsb, new RouteFloor(1.01, 900, 0.87))));

        admission.Admits(new RoutedLink(1, 1, 0.30, Route.Reduced | ThroughKjv | ThroughBsb)).Should().BeTrue();
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
            Berean((Direct | ThroughKjv, new RouteFloor(0.50, 900, 0.92))),
            KingJames((Direct, new RouteFloor(0.70, 800, 0.90))));

        admission.Describe(Vias).Should().Contain("on BSB and KJV")
            .And.Contain("W+R+KJV+BSB from 0.50")
            .And.Contain("refused as measured on neither");
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

    /// <summary>
    /// The Elberfelder through the King James, the Berean and Luther (PRB-0863), measured on Luther through the first two and on
    /// the Schlachter through all three. Luther cannot measure what the others find without him, so that is refused unless the
    /// Schlachter measured it; the composition through the first two alone measured it on Luther, and its floor stands for those
    /// answers, which keep the confidence they had without him. What Luther's route found keeps the floor measured for it.
    /// </summary>
    [Fact]
    public void AnAddedMiddleTextLeavesWhatTheOthersFindWithoutItAtTheFloorsTheyHadWithoutIt()
    {
        string[] three = ["KJV", "BSB", "LUTH1912"];
        const Route throughLuther = Route.ComposedThird;
        var everyButLuther = Direct | ThroughKjv | ThroughBsb;
        var luther = ("LUTH1912", everyButLuther, (IReadOnlyDictionary<Route, RouteFloor>)new Dictionary<Route, RouteFloor>
        {
            [Direct | ThroughKjv] = new(0.57, 8_702, 0.90),
            [ThroughKjv] = new(0.82, 1_000, 0.92),
        });
        var schlachter = ("SCH1951", everyButLuther | throughLuther, (IReadOnlyDictionary<Route, RouteFloor>)new Dictionary<Route, RouteFloor>
        {
            [Direct | ThroughKjv] = new(0.70, 900, 0.91),
            [throughLuther] = new(1.01, 400, 0.70),
        });
        var withLuther = Admission.Borrow([luther, schlachter], three, 0.25);
        var before = Admission.Borrow(
            [("LUTH1912", everyButLuther, new Dictionary<Route, RouteFloor> { [Direct | ThroughKjv] = new(0.57, 8_702, 0.90) })], Vias, 0.25);

        var admission = withLuther.Keeping(before, throughLuther, "LUTH1912", new HashSet<(long, long)>());

        admission.Floors[Direct | ThroughKjv].Confidence.Should().Be(0.57);
        admission.Floors[Direct | ThroughKjv].Basis.Should().Be("as before LUTH1912 was a middle text, on LUTH1912");
        admission.Floors[Direct | ThroughKjv | throughLuther].Confidence.Should().Be(0.57);
        admission.Floors[Direct | ThroughKjv | throughLuther].Basis.Should().Be("as W+R+KJV on LUTH1912");
        admission.Floors[ThroughKjv | throughLuther].Confidence.Should().Be(0.82);
        admission.Admits(new RoutedLink(1, 1, 0.98, throughLuther)).Should().BeFalse();
        admission.Admits(new RoutedLink(1, 1, 0.98, Direct)).Should().BeFalse();
        withLuther.Floors[Direct | ThroughKjv].Confidence.Should().Be(0.70, "without the floors from before, only the Schlachter measured it");
    }

    /// <summary>
    /// What Luther's route finds with one other family of readings is written only where the composition without him writes
    /// the same answer; with two others or more it is written from its floor.
    /// </summary>
    [Fact]
    public void AnAddedMiddleTextSecondsWhatOneOtherFamilyFoundButBringsNothingInWithIt()
    {
        string[] three = ["KJV", "BSB", "LUTH1912"];
        const Route throughLuther = Route.ComposedThird;
        var luther = ("LUTH1912", Direct | ThroughKjv | ThroughBsb, (IReadOnlyDictionary<Route, RouteFloor>)new Dictionary<Route, RouteFloor>
        {
            [Direct] = new(0.50, 3_893, 0.90),
            [ThroughKjv | ThroughBsb] = new(0.40, 800, 0.91),
        });
        var admission = Admission.Borrow([luther], three, 0.25)
            .Keeping(Admission.Borrow([], Vias, 0.25), throughLuther, "LUTH1912", new HashSet<(long, long)> { (1, 10) });

        admission.Admits(new RoutedLink(1, 10, 0.60, Direct | throughLuther)).Should().BeTrue("the composition without Luther writes it");
        admission.Admits(new RoutedLink(2, 20, 0.90, Direct | throughLuther)).Should().BeFalse("only Luther's route brings it in");
        admission.Admits(new RoutedLink(3, 30, 0.45, ThroughKjv | ThroughBsb | throughLuther)).Should().BeTrue();
        admission.Describe(three).Should().Contain("what LUTH1912 found with one other family of readings only where");
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
