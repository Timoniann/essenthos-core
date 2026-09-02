using Essenthos.Core.Loading.Encyclopedia;
using FluentAssertions;
using Xunit;

namespace Essenthos.Core.Tests;

/// <summary>
/// Reading the date Theographic states, in both of the shapes it states it.
///
/// The source writes most years as a bare astronomical integer and fifty of them as a full ISO
/// date. A reader that took only the integer treated those fifty as undated and re-derived them
/// from the predecessor chain, which runs early — the chain sums durations and knows nothing of
/// the gaps a stated date carries, so Galatians moved from 49 to 45.
/// </summary>
public class TheographicDateTests
{
    [Theory]
    [InlineData("30", 30, 0, 0)]
    [InlineData("-4003", -4003, 0, 0)]
    [InlineData("0", 0, 0, 0)]
    [InlineData("0030-04-04", 30, 4, 4)]
    [InlineData("0029-10-9", 29, 10, 9)]
    [InlineData("  0045-07-01  ", 45, 7, 1)]
    [InlineData("-0490-09-12", -490, 9, 12)]
    public void ReadsBothShapesTheSourceWrites(string value, int year, int month, int day) =>
        TheographicEventLoader.Stated(value).Should().Be((year, month, day));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("about 30")]
    [InlineData("0030-04")]
    [InlineData("0030-13-01")]
    [InlineData("0030-04-32")]
    public void AnswersNothingForWhatIsNotADate(string value) =>
        TheographicEventLoader.Stated(value).Should().BeNull();

    [Fact]
    public void PrefersWhatTheSourceStatesOverItsOwnChain()
    {
        // The chain would make this 45: four steps of stated duration from a dated ancestor, with
        // none of the waiting the source's own date accounts for.
        var rows = new[]
        {
            Row("Return to Antioch in Syria", start: "0045-04-01"),
            Row("Paul Writes Galatians", start: "0049-10-01", predecessor: "Return to Antioch in Syria"),
        };

        TheographicEventLoader.YearOf(rows[1], Titled(rows)).Should().Be(49);
    }

    [Fact]
    public void CountsDaysFromTheStatedDateWhenTheChainDoesRun()
    {
        // 5 December plus two months is the following February, not the December it started in.
        var rows = new[]
        {
            Row("Anchor", start: "0030-12-05", duration: "2M"),
            Row("After", predecessor: "Anchor"),
        };

        TheographicEventLoader.YearOf(rows[1], Titled(rows)).Should().Be(31);
    }

    [Fact]
    public void ReadsAYearOutOfEveryDateTheSourceStates()
    {
        var rows = Events();
        var undated = rows.Count(row => TheographicEventLoader.Stated(row["startDate"]) is null);

        undated.Should().Be(0,
            "every row in events.csv carries a startDate, and a reader that cannot parse one " +
            "silently swaps a stated date for a derived one");
        rows.Should().HaveCountGreaterThan(400);
    }

    [Fact]
    public void GivesTheSourcesOwnYearToTheEventsItWritesAsIsoDates()
    {
        var rows = Events();
        var byTitle = Titled(rows);

        var disagreeing = rows
            .Where(row => TheographicEventLoader.Stated(row["startDate"]) is { Month: > 0 } stated
                          && TheographicEventLoader.YearOf(row, byTitle) != stated.Year)
            .Select(row => row["title"])
            .ToList();

        disagreeing.Should().BeEmpty();
    }

    [Theory]
    [InlineData("Paul Writes Galatians", 49)]
    [InlineData("Mission to Ephesus/1 Cor Written", 53)]
    [InlineData("Mission to Macedonia and Greece", 54)]
    [InlineData("Third Missionary Journey Begins", 50)]
    [InlineData("Riot in Ephesus", 53)]
    [InlineData("Crucifixion and Burial", 30)]
    [InlineData("Resurrection and Ascension", 30)]
    public void DatesTheEventsTheChainUsedToGetWrong(string title, int year)
    {
        var byTitle = Titled(Events());
        TheographicEventLoader.YearOf(byTitle[title], byTitle).Should().Be(year);
    }

    private static List<Dictionary<string, string>> Events() =>
        Essenthos.Core.Loading.Encyclopedia.Csv.Read(TestResources.Path("TheographicBibleData", "events.csv")).ToList();

    private static Dictionary<string, string> Row(
        string title, string start = "", string duration = "1D", string predecessor = "") =>
        new(StringComparer.Ordinal)
        {
            ["title"] = title,
            ["startDate"] = start,
            ["duration"] = duration,
            ["predecessor"] = predecessor,
        };

    private static Dictionary<string, Dictionary<string, string>> Titled(
        IEnumerable<Dictionary<string, string>> rows)
    {
        var titled = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in rows)
        {
            titled.TryAdd(row["title"].Trim(), row);
        }

        return titled;
    }
}
