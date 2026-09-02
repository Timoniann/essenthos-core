using Essenthos.Core.Database.Entities;
using Essenthos.Core.Loading.Encyclopedia;
using FluentAssertions;
using Xunit;

namespace Essenthos.Core.Tests;

/// <summary>
/// Joining the dataset's openings to its closings.
///
/// The source records no periods at all — it records an event called <em>Beginning of the 400
/// years of oppression</em> and, four hundred years later, one called <em>The 400 years of
/// oppression ended</em>, and leaves the reader to notice. These are the joins, and the two ways
/// the source writes the marker are why the key is stripped from both ends.
/// </summary>
public class PeriodTests
{
    private static Event An(int id, string slug, string name, string kind, int year) => new()
    {
        Id = id,
        Slug = slug,
        Name = name,
        Kind = kind,
        YearFromCreation = year,
        Source = "test",
    };

    [Fact]
    public void PairsAnOpeningWithItsClose()
    {
        var (periods, unpaired) = Periods.From(
            [
                An(1, "beginasa1reign", "Asa's reign as king over Judah begins", "Begin", 3052),
                An(2, "endasa1reign", "Asa's reign as king over Judah ends", "End", 3093),
            ],
            "test");

        unpaired.Should().Be(0);
        var reign = periods.Should().ContainSingle().Subject;
        reign.Kind.Should().Be("reign");
        reign.Level.Should().Be(1);
        reign.StartYear.Should().Be(3052);
        reign.EndYear.Should().Be(3093);

        // A band labelled "…begins" reads as a moment rather than a stretch of time.
        reign.Name.Should().Be("Asa's reign as king over Judah");
    }

    [Fact]
    public void FindsTheMarkerAtEitherEndOfTheIdentifier()
    {
        var (periods, unpaired) = Periods.From(
            [
                An(1, "absalomdenieddavidscountenancebegin",
                    "Absalom denied King David's countenance began*", "Begin", 2973),
                An(2, "absalomdenieddavidscountenanceend",
                    "Absalom denied King David's countenance ended*", "End", 2975),
            ],
            "test");

        unpaired.Should().Be(0);
        var span = periods.Should().ContainSingle().Subject;
        span.Name.Should().Be("Absalom denied King David's countenance");
        span.Notes.Should().NotBeNull("the source's asterisk means the year is inferred, and hiding that would overstate it");
    }

    [Fact]
    public void CountsAnOpeningThatNeverCloses()
    {
        var (periods, unpaired) = Periods.From(
            [An(1, "beginsomething", "Beginning of something", "Begin", 100)],
            "test");

        periods.Should().BeEmpty();
        unpaired.Should().Be(1);
    }

    [Fact]
    public void MakesALifeOutOfABirthAndADeath()
    {
        var (periods, _) = Periods.From(
            [
                An(1, "birthadam1", "Birth of Adam", "Birth", 1),
                An(2, "deathadam1", "Death of Adam", "Death", 931),
            ],
            "test");

        var life = periods.Should().ContainSingle().Subject;
        life.Slug.Should().Be("life-adam1");
        life.Name.Should().Be("Adam");
        life.Kind.Should().Be("life");
        life.EndYear!.Value.Should().Be(931);
    }

    [Fact]
    public void NestsWhatFallsInsideAnEra()
    {
        var (periods, _) = Periods.From(
            [
                An(1, "creation", "The Creation", "Unique", 1),
                An(2, "beginflood", "The Flood began", "Begin", 1657),
                An(3, "endflood", "The Flood ended", "End", 1658),
                An(4, "birthadam1", "Birth of Adam", "Birth", 1),
                An(5, "deathadam1", "Death of Adam", "Death", 931),
            ],
            "test");

        var era = periods.Should().Contain(p => p.Slug == "era-antediluvian").Which;
        era.Level.Should().Be(0);
        era.EndYear.Should().Be(1657);

        periods.Should().Contain(p => p.Slug == "life-adam1")
            .Which.Parent.Should().Be(era, "a life that opens inside an era belongs to it");
    }
}
