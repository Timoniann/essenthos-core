using Essenthos.Core.Loading.Encyclopedia;
using FluentAssertions;
using Xunit;

namespace Essenthos.Core.Tests;

/// <summary>
/// Dating what Theographic leaves undated.
///
/// Fifty of its events have no year and every one of them names a predecessor — the crucifixion,
/// the resurrection, Pentecost and the whole first missionary journey among them. They are not
/// undatable; the source's own dependency solver simply never ran on them. These are the rules for
/// running it, and the reason it is arithmetic rather than a guess.
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
