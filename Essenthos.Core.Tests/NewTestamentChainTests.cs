using Essenthos.Core.Loading.Encyclopedia;
using FluentAssertions;
using Xunit;

namespace Essenthos.Core.Tests;

/// <summary>
/// Dating what Theographic leaves undated.
///
/// The fallback, for a row that arrives with a predecessor and no date of its own. Nothing in the
/// file as it stands needs it — every row states a date — so these are the rules stated on rows
/// built for the purpose, and the reason the answer is arithmetic rather than a guess.
/// </summary>
public class NewTestamentChainTests
{
    private static Dictionary<string, string> An(
        string title, string start = "", string duration = "1D", string predecessor = "") =>
        new(StringComparer.Ordinal)
        {
            ["title"] = title,
            ["startDate"] = start,
            ["duration"] = duration,
            ["predecessor"] = predecessor,
            ["partOf"] = string.Empty,
        };

    [Fact]
    public void FollowsAChainOfDaysBackToADatedEvent()
    {
        var rows = new[]
        {
            An("Zaccheus Converted", start: "30"),
            An("Mary Anoints Jesus", predecessor: "Zaccheus Converted"),
            An("Triumphal Entry", predecessor: "Mary Anoints Jesus"),
            An("Crucifixion and Burial", predecessor: "Triumphal Entry"),
        };

        // Days, not years. Summing four steps as whole years would put the crucifixion in 34.
        TheographicEventLoader.YearOf(rows[3], Titled(rows)).Should().Be(30);
    }

    [Fact]
    public void CountsAYearOnlyWhenTheDurationsAddUpToOne()
    {
        var rows = new[]
        {
            An("Anchor", start: "40"),
            An("Two years later", predecessor: "Anchor"),
        };

        rows[0]["duration"] = "2Y";
        TheographicEventLoader.YearOf(rows[1], Titled(rows)).Should().Be(42);

        rows[0]["duration"] = "11M";
        TheographicEventLoader.YearOf(rows[1], Titled(rows)).Should().Be(40);
    }

    [Fact]
    public void WorksBackwardsAcrossTheEraBoundary()
    {
        // Astronomical numbering, so -3 is 4 BCE and there is no year zero to fall into.
        var rows = new[]
        {
            An("Birth of Jesus", start: "-3"),
            An("Jesus Circumsized", predecessor: "Birth of Jesus", duration: "8D"),
        };

        TheographicEventLoader.YearOf(rows[1], Titled(rows)).Should().Be(-3);
    }

    [Fact]
    public void GivesUpOnAChainThatEatsItself()
    {
        var rows = new[]
        {
            An("First", predecessor: "Second"),
            An("Second", predecessor: "First"),
        };

        TheographicEventLoader.YearOf(rows[0], Titled(rows)).Should().BeNull();
    }

    [Fact]
    public void JoinsActsToTheGospelsWhereTheSourceDoesNot()
    {
        // The one edge added by hand: Acts 1:4 names no predecessor, so Pentecost and the ascension
        // have nothing to hang on. The gospel chain ends at the verse before it.
        var rows = new[]
        {
            An("Resurrection and Ascension", start: "30"),
            An("The Holy Spirit is promised"),
            An("The Holy Spirit comes", predecessor: "The Holy Spirit is promised"),
        };

        TheographicEventLoader.YearOf(rows[2], Titled(rows)).Should().Be(30);
    }

    [Fact]
    public void ReadsAPredecessorTheSourceHadToQuote()
    {
        var rows = new[]
        {
            An("Mission to Phrygia, Galatia and Asia", start: "50"),
            An("Call to Macedonia", predecessor: "\"Mission to Phrygia, Galatia and Asia\""),
        };

        TheographicEventLoader.YearOf(rows[1], Titled(rows)).Should().Be(50);
    }

    private static Dictionary<string, Dictionary<string, string>> Titled(
        IEnumerable<Dictionary<string, string>> rows)
    {
        var titled = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in rows)
        {
            titled.TryAdd(row["title"], row);
        }

        return titled;
    }
}

/// <summary>
/// Reading Wikidata's timestamps, which are not written the way its own documentation describes.
///
/// The RDF these queries return is XSD <c>dateTime</c>, and XSD has a year zero — so <c>-0489</c>
/// is 490 BCE. Wikidata's internal model has no year zero and writes the same battle
/// <c>-0490</c>. Believing the documentation over the file put every world event a year late,
/// which is invisible on a six-thousand-year axis and wrong in every citation.
/// </summary>
public class WorldYearTests
{
    [Theory]
    [InlineData("-0489-08-07T00:00:00Z", -489)]   // Marathon, 490 BCE
    [InlineData("-2559-01-01T00:00:00Z", -2559)]  // The Great Pyramid, 2560 BCE
    [InlineData("-0030-09-02T00:00:00Z", -30)]    // Actium, 31 BCE
    [InlineData("0079-08-24T00:00:00Z", 79)]      // Vesuvius, AD 79
    [InlineData("0000-01-01T00:00:00Z", 0)]       // The year the axis calls 1 BCE
    public void ReadsTheYearOutOfATimestamp(string timestamp, int expected) =>
        WorldHistoryLoader.Year(timestamp).Should().Be(expected);

    [Theory]
    [InlineData("")]
    [InlineData("not a date")]
    [InlineData("-04")]
    public void AnswersNothingForWhatIsNotADate(string timestamp) =>
        WorldHistoryLoader.Year(timestamp).Should().BeNull();

    [Fact]
    public void PutsAYearOnTheAxisWhereEveryReckoningCanFindIt()
    {
        // 490 BCE is the axis year `zero + -489` on any reckoning: 3472 on this corpus's own,
        // 3514 on Ussher's. The historical year is the same; only the count from creation moves.
        var marathon = WorldHistoryLoader.Year("-0489-01-01T00:00:00Z")!.Value;
        (3961 + marathon).Should().Be(3472);
        (4003 + marathon).Should().Be(3514);
    }
}
