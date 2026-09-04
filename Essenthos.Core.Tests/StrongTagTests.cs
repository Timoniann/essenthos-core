using Essenthos.Core.Loading.Links;
using Essenthos.Core.Strong;
using FluentAssertions;
using Xunit;

namespace Essenthos.Core.Tests;

/// <summary>
/// What a Zefania Strong tag names. Every case here is quoted from the King James file the corpus
/// loads, and the multi-number ones are the 14,446 tags whose second number the reader cut off at
/// the first space, on the belief that a morphology code followed it. No tag in that file carries
/// one: of the 412,699 tags, the only token that is not a number is <c>5555*</c>.
/// </summary>
public class StrongTagTests
{
    /// <summary>Matthew 6:25 — <em>Therefore</em>, which is διὰ τοῦτο and two Greek words.</summary>
    [Fact]
    public void ReadsEveryNumberAPhraseTagNames()
    {
        StrongTags.Read("1223 5124", StrongNumbers.Greek).Should().Equal("G1223", "G5124");
    }

    /// <summary>
    /// Galatians 1:5 — <em>ever</em>, εἰς τοὺς αἰῶνας τῶν αἰώνων. The same number twice is the
    /// source naming two Greek words, so it is read twice and left for the caller to group.
    /// </summary>
    [Fact]
    public void KeepsANumberTheTagNamesTwice()
    {
        StrongTags.Read("1519 165 165", StrongNumbers.Greek).Should().Equal("G1519", "G165", "G165");
    }

    [Fact]
    public void ReadsASingleNumber()
    {
        StrongTags.Read("2532", StrongNumbers.Greek).Should().Equal("G2532");
    }

    /// <summary>
    /// The tag carries digits alone and the file numbers its two testaments in two series, so the
    /// caller says which. Genesis 1:1's <em>beginning</em> is H7225 and never G7225, which is a
    /// perfectly good number for ἡλικία.
    /// </summary>
    [Fact]
    public void NumbersInTheSeriesTheCallerNames()
    {
        StrongTags.Read("7225", StrongNumbers.Hebrew).Should().Equal("H7225");
    }

    /// <summary>
    /// Genesis 15:5 — <em>forth</em>, H3318 יצא with the object marker beside it. The asterisk is
    /// on the number and not on the tag, so the rest of the tag stands.
    /// </summary>
    [Fact]
    public void DropsAnUnsettledNumberAndKeepsTheRestOfTheTag()
    {
        StrongTags.Read("3318 *853", StrongNumbers.Hebrew).Should().Equal("H3318");
    }

    [Theory]
    [InlineData("*853")]
    [InlineData("5555*")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void NamesNothingWhereTheSourceSettledNothing(string? tag)
    {
        StrongTags.Read(tag, StrongNumbers.Greek).Should().BeEmpty();
    }
}
