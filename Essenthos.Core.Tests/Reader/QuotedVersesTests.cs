using Essenthos.Core.Endpoints;
using FluentAssertions;
using Xunit;

namespace Essenthos.Core.Tests.Reader;

/// <summary>
/// An event description that quotes the New American Standard Bible gives its verses and nothing of its
/// words; what the compiler wrote himself stays (PRB-1678). The strings are BibleData's own.
/// </summary>
public sealed class QuotedVersesTests
{
    private const string BibleData = "BibleData by Brady Stephenson, github.com/BradyStephenson/bible-data, CC BY 4.0";

    private static string[] Addresses(QuotedVerses.Reading reading) =>
        [.. QuotedVerses.Verses(reading.Passages).Select(v => $"{v.BookOrdinal}.{v.Chapter}:{v.Verse}")];

    [Fact]
    public void AQuotationGivesItsVersesAndNoneOfItsWords()
    {
        var reading = QuotedVerses.Read(
            "abramdepartedharan",
            BibleData,
            "So Abram went forth as the LORD had spoken to him; and Lot went with him. Now Abram was SEVENTY-FIVE YEARS old when he departed from Haran. (GEN 12:4)");

        Addresses(reading).Should().Equal("1.12:4");
        reading.Remainder.Should().BeNull();
    }

    [Fact]
    public void EveryCitationClosesTheQuotationBeforeItAndRangesGiveEachVerse()
    {
        var reading = QuotedVerses.Read(
            "nehemiahfirstreturn",
            BibleData,
            "The words of Nehemiah the son of Hacaliah. (NEH 1:1)  So I came to Jerusalem and was there three days. (Nehemiah 2:11)\nThe LORD said to him, \"Go.\" (1KI 19:15-16, 19)");

        Addresses(reading).Should().Equal("16.1:1", "16.2:11", "11.19:15", "11.19:16", "11.19:19");
        reading.Remainder.Should().BeNull();
    }

    [Fact]
    public void AnUnclosedOrBareCitationAtTheEndStillCloses()
    {
        Addresses(QuotedVerses.Read("enddavid1reign", BibleData, "Then David slept with his fathers and was buried in the city of David. (1KI 2:10"))
            .Should().Equal("11.2:10");
        Addresses(QuotedVerses.Read("enddeborahjudge", BibleData, "And the land was undisturbed for FORTY years. JDG 5:31b"))
            .Should().Equal("7.5:31");
        Addresses(QuotedVerses.Read("beginfirsttempleconstruction", BibleData, "…he began to build the house of the LORD. (1 KI 6:1)"))
            .Should().Equal("11.6:1");
    }

    [Fact]
    public void TheCompilersBracketedWorkingStaysAndTheQuotationGoes()
    {
        var reading = QuotedVerses.Read(
            "birthabram1",
            BibleData,
            "Terah lived SEVENTY YEARS, and became the father of Abram, Nahor and Haran. (GEN 11:26)\n[Terah was 205 when he died in Haran (Acts 7:4).\nThis means Terah was 130 when Abram was born.]");

        Addresses(reading).Should().Equal("1.11:26");
        reading.Remainder.Should().Be("[Terah was 205 when he died in Haran (Acts 7:4).\nThis means Terah was 130 when Abram was born.]");
    }

    [Fact]
    public void TheCompilersOwnSentenceStaysWhole()
    {
        var own = "Ahaz's sole reign began when his co-regency ended";
        QuotedVerses.Read("beginahaz1reign", BibleData, own).Should().Be(new QuotedVerses.Reading([], own));

        var cited = "Jehoram was a regent of Judah under his father, Jehoshaphat.  This reconciles 2KI 1:17 and 2KI 3:1.  (see notes)";
        QuotedVerses.Read("beginjehoram1regency", BibleData, cited).Remainder.Should().Be(cited);

        var prose = "All of Creation was formed by G-d's spoken word. (GEN 1:1-31)";
        QuotedVerses.Read("creation", BibleData, prose).Should().Be(new QuotedVerses.Reading([], prose),
            "a sentence of the compiler's that ends in a citation is no quotation");
    }

    [Fact]
    public void OnlyBibleDatasDescriptionsAreRead()
    {
        var ussher = "Adam was created. (GEN 1:27)";
        QuotedVerses.Read("ussher-1", "Ussher's Annals of the World (1658)", ussher).Remainder.Should().Be(ussher);
    }

    [Fact]
    public void AMiscitedQuotationGivesTheVerseItQuotes()
    {
        Addresses(QuotedVerses.Read(
                "azariahsprophecy",
                BibleData,
                "Now the Spirit of God came on Azariah the son of Oded… (1CH 15:1-2)"))
            .Should().Equal("14.15:1", "14.15:2");
        Addresses(QuotedVerses.Read(
                "esaumarriedjudithandbasemath",
                BibleData,
                "When Esau was FORTY YEARS OLD he married Judith the daughter of Beeri the Hittite"))
            .Should().Equal("1.26:34");
    }

    [Fact]
    public void ANoteLosesTheVerseItQuotesAndKeepsTheCitationAndTheCompilersWords()
    {
        const string note = "\"He was twenty-five years old when he became king\" (2KI 18:2)\nThis is the year that starts the count of Hezekiah's 29-year reign.";

        QuotedVerses.WithoutQuotations("beginhezekiah1reign", BibleData, note)
            .Should().Be("(2KI 18:2)\nThis is the year that starts the count of Hezekiah's 29-year reign.");
        QuotedVerses.WithoutQuotations("birthhezekiah1", BibleData, note).Should().Be(note, "only the notes read by hand are changed");
    }

    [Fact]
    public void APassageIntoTheNextChapterTakesTheEndOfTheFirst()
    {
        var passages = QuotedVerses.Passages("2KI 1:17-2:1");
        QuotedVerses.Verses(passages, (_, chapter) => chapter == 1 ? 18 : null)
            .Select(v => $"{v.Chapter}:{v.Verse}")
            .Should().Equal("1:17", "1:18", "2:1");
    }
}
