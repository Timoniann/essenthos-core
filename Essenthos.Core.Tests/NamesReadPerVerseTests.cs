using Essenthos.Core.Loading.Encyclopedia;
using FluentAssertions;
using Xunit;

namespace Essenthos.Core.Tests;

/// <summary>
/// What the shipped rulings say about the Hebrew names only a reading of the verse could settle, and
/// what they deliberately leave unsaid.
/// </summary>
public sealed class NamesReadPerVerseTests
{
    private static readonly IReadOnlyList<OwnRecordRuling> Rulings =
        [.. SenseReadingFiles.AllRulings().SelectMany(file => file.Rulings)];

    private static IEnumerable<string> Named(string reference) =>
        Rulings.Where(r => r.Text == "BHSA" && r.Reference == reference)
            .Select(r => r.Create?.Slug ?? r.Existing!);

    /// <summary>
    /// Words whose verse cannot say whom they name stay without a record: Asshur of Genesis 10:11 (the
    /// man who went out, or the land he went into), Canaan as a trader, the two Ariels of Moab, Pul
    /// among the far nations, Ephratah of Psalm 132, the one word that is Ain and Rimmon, and Ezekiel
    /// 27:19's Javan, which trades from Uzal.
    /// </summary>
    [Theory]
    [InlineData("GEN 10:11", 7)]
    [InlineData("HOS 12:8", 1)]
    [InlineData("ZEP 1:11", 9)]
    [InlineData("2SA 23:20", 16)]
    [InlineData("ISA 66:19", 13)]
    [InlineData("PSA 132:6", 4)]
    [InlineData("JOS 15:32", 6)]
    [InlineData("EZK 27:19", 3)]
    public void AWordTheVerseCannotSettleIsLeftWithoutARecord(string reference, int position) =>
        Rulings.Should().NotContain(r => r.Text == "BHSA" && r.Reference == reference && r.Position == position);

    /// <summary>
    /// The singular with the article, the Rapha the four were born to, is the giant their father; the
    /// people of giants is the plural Rephaim of 1 Chronicles 20:4.
    /// </summary>
    [Fact]
    public void TheSonsOfTheRaphaAreTheGiantsSonsNotThePeoples()
    {
        foreach (var reference in new[] { "2SA 21:16", "2SA 21:18", "2SA 21:20", "2SA 21:22", "1CH 20:6", "1CH 20:8" })
        {
            Named(reference).Should().Contain("raphah").And.NotContain("rephaim");
        }
    }

    /// <summary>The nation Amalek is never read as the man its name comes from.</summary>
    [Fact]
    public void AmalekTheNationIsTheAmalekites()
    {
        var amalek = SenseReadingFiles.AllRulings()
            .Single(file => file.Source.Contains("Hebrew names read against their verses", StringComparison.Ordinal))
            .Rulings.Where(r => r.StrongNumber == "H6002").ToList();

        amalek.Should().HaveCountGreaterThanOrEqualTo(35);
        amalek.Should().OnlyContain(r => r.Existing == "amalekites");
    }
}
