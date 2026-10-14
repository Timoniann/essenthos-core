using Essenthos.Core.Loading.Encyclopedia;
using FluentAssertions;
using Xunit;

namespace Essenthos.Core.Tests;

/// <summary>The slips of spelling in the datasets' lines, and what putting them right must not touch.</summary>
public sealed class LineSpellingsTests
{
    [Theory]
    [InlineData("daughter of Bethuel, grandaughter of Nahor (GEN 22:23)",
        "daughter of Bethuel, granddaughter of Nahor (GEN 22:23)")]
    [InlineData("daughter of Ahab and grandaughter of Omri (2KI 8:26, 2CH 22:2))",
        "daughter of Ahab and granddaughter of Omri (2KI 8:26, 2CH 22:2)")]
    [InlineData("scribe for King David (2SA 20:25", "scribe for King David (2SA 20:25)")]
    [InlineData("son of Huri (1CH 5:14, Gadite (1CH 5:11)", "son of Huri (1CH 5:14), Gadite (1CH 5:11)")]
    [InlineData("the priest, son of Buzi (EZK  1:3)", "the priest, son of Buzi (EZK 1:3)")]
    [InlineData("son of Zerah (1CH 2:6).  Solomon was wiser than him (1KI 4:31)",
        "son of Zerah (1CH 2:6). Solomon was wiser than him (1KI 4:31)")]
    [InlineData("a Merarite (collectively), or decendants of Merari, as Strong's Dictionary describes them",
        "a Merarite (collectively), or descendants of Merari, as Strong's Dictionary describes them")]
    [InlineData("Paul's fellow worker and fellow solder (PHP 2:25)", "Paul's fellow worker and fellow soldier (PHP 2:25)")]
    [InlineData("(huband?) of Mary (JHN 19:25)", "(husband?) of Mary (JHN 19:25)")]
    public void ASlipIsPutRight(string line, string right)
    {
        LineSpellings.Correct(line).Should().Be(right);
    }

    [Theory]
    [InlineData("the son of Jesse (1SA 16:18)")]
    [InlineData("the granddaughter of Omri (2KI 8:26)")]
    [InlineData("the one who was (and is")]
    [InlineData(null)]
    [InlineData("")]
    public void ALineSpelledRightIsLeftAlone(string? line)
    {
        LineSpellings.Correct(line).Should().Be(line);
    }

    [Theory]
    [InlineData("His Annointed", "His Anointed")]
    [InlineData("his [the LORD's] annointed", "his [the LORD's] anointed")]
    [InlineData("on of Deker [pierces]", "son of Deker [pierces]")]
    [InlineData("my] father is Ner [a lamp]", "[my] father is Ner [a lamp]")]
    [InlineData("poss. a form of] man of Baal; having a long upper lip", "[poss. a form of] man of Baal; having a long upper lip")]
    [InlineData("rom the right, good, fortune", "from the right, good, fortune")]
    [InlineData("awning, shining or flashing [red or scarlet] light", "dawning, shining or flashing [red or scarlet] light")]
    [InlineData("TRUE", "true")]
    [InlineData("Oather (?) of Shadrach, Meshash, and Abednego", "G-d of Shadrach, Meshach, and Abednego")]
    [InlineData("King Ahasureus", "King Ahasuerus")]
    [InlineData("might god [El]", "mighty G-d [El]")]
    [InlineData("G-d has filled with joy or [the object of] joy is G-d”", "G-d has filled with joy or [the object of] joy is G-d")]
    public void ASlipInANameIsPutRight(string line, string right)
    {
        LineSpellings.Name(line).Should().Be(right);
        LineSpellings.Name(right).Should().Be(right);
    }

    [Theory]
    [InlineData("Ashurbanipal, “Ashur creates a son”")]
    [InlineData("valley [of the] plain (?)")]
    [InlineData("[my] father is good")]
    [InlineData("son of Deker [piercing]")]
    [InlineData(null)]
    public void ANameSpelledRightIsLeftAlone(string? line)
    {
        LineSpellings.Name(line).Should().Be(line);
    }

    [Fact]
    public void PuttingALineRightTwiceChangesNothingTheSecondTime()
    {
        foreach (var (was, _) in LineSpellings.Slips)
        {
            var once = LineSpellings.Correct($"a line with {was} in it (GEN 1:1");
            LineSpellings.Correct(once).Should().Be(once);
        }
    }

    /// <summary>
    /// Every slip is one a dataset still makes; one it no longer makes is an entry that corrects nothing
    /// and should go.
    /// </summary>
    [Fact]
    [Trait(TestCategory.Name, TestCategory.Corpus)]
    public void EverySlipIsInTheData()
    {
        var bibleData = Loading.Encyclopedia.Csv.Read(TestResources.Path("BibleData2026", "BibleData-Person.csv"))
            .Select(row => row["unique_attribute"])
            .ToList();
        var strong = File.ReadAllText(TestResources.Path("Strong", "StrongHebrew.xml"));

        foreach (var (was, _) in LineSpellings.Slips)
        {
            (bibleData.Any(line => line.Contains(was, StringComparison.Ordinal)) ||
             strong.Contains(was, StringComparison.Ordinal))
                .Should().BeTrue($"\"{was}\" should be a slip a dataset makes");
        }
    }
}
