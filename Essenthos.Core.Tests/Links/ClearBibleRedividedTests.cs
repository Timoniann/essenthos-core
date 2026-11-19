using Essenthos.Core.ClearBible;
using Essenthos.Core.Loading.Links;
using FluentAssertions;
using Xunit;

namespace Essenthos.Core.Tests.Links;

/// <summary>
/// The sets whose token file matches their records except in a verse here and there that was counted over another
/// division of the text. The verses are the files' own.
/// </summary>
public class ClearBibleRedividedTests
{
    /// <summary>
    /// 1 Kings 13:23 in the Van Dyck: הֵשִׁיב names the comma after أَرْجَعَهُ, אֲשֶׁר names أَرْجَعَهُ and the prophet
    /// takes ٱلَّذِي, each one word late. The verse is refused.
    /// </summary>
    [Fact]
    public void AVanDyckVerseWhoseRecordsNameAMarkIsMisnumbered()
    {
        var tokens = Tokens(
            "11013023", "ثُمَّ", "بَعْدَمَا", "أَكَلَ", "خُبْزًا", "وَبَعْدَ", "أَنْ", "شَرِبَ", "شَدَّ", "لَهُ", "عَلَى", "ٱلْحِمَارِ",
            "،", "أَيْ", "لِلنَّبِيِّ", "ٱلَّذِي", "أَرْجَعَهُ", "،");
        ClearBibleRecord[] records =
        [
            Record(["o110130230012"], ["11013023001"]), Record(["o110130230021"], ["11013023002"]),
            Record(["o110130230031"], ["11013023003"]), Record(["o110130230041"], ["11013023004"]),
            Record(["o110130230052"], ["11013023005"]), Record(["o110130230061"], ["11013023006", "11013023007"]),
            Record(["o110130230072"], ["11013023008"]),
            Record(["o110130230092"], ["11013023010", "11013023011", "11013023013"]),
            Record(["o110130230102"], ["11013023014", "11013023015"]), Record(["o110130230111"], ["11013023016"]),
            Record(["o110130230121"], ["11013023017"]),
        ];

        ClearBibleLinks.Misnumbered(records, tokens).Should().Equal(ClearBibleAlignment.Verse(11, 13, 23));
        ClearBibleSet.VanDyckOldTestament("AVD1865", "BHSA").Numbering.Should().Be(ClearBibleNumbering.Redivided);
    }

    /// <summary>
    /// Mark 3:32 in the Open Hausa: ἰδοὺ, which the Hausa does not render, names the comma before the quotation, and
    /// μήτηρ and ζητοῦσίν still name «Mahaifiyarka» and «nemanka». A word on a mark is not a misnumbered verse in this
    /// set, so its verses are not refused for it.
    /// </summary>
    [Fact]
    public void AHausaWordOnAMarkLeavesItsVerseStanding()
    {
        var tokens = Tokens(
            "41003032", "Taron", "yana", "zaune", "kewaye", "da", "shi", ",", "sai", "suka", "ce", "masa", ",", "“",
            "Mahaifiyarka", "da", "’", "yan’uwanka", "suna", "waje", ",", "suna", "nemanka", ".", "”");
        ClearBibleRecord[] records =
        [
            Record(["n41003032002"], ["41003032003"]), Record(["n41003032005"], ["41003032001"]),
            Record(["n41003032007"], ["41003032010"]), Record(["n41003032009"], ["41003032012"]),
            Record(["n41003032011"], ["41003032014"]), Record(["n41003032018"], ["41003032022"]),
        ];

        ClearBibleLinks.Misnumbered(records, tokens).Should().Equal(ClearBibleAlignment.Verse(41, 3, 32));
        ClearBibleSet.OpenHausaNewTestament("OHCB", "NESTLE1904").Numbering.Should().Be(ClearBibleNumbering.TokenFile);
    }

    private static List<ClearBibleToken> Tokens(string verse, params string[] texts) =>
        [.. texts.Select((text, at) => new ClearBibleToken($"{verse}{at + 1:000}", text, !char.IsLetter(text[0]), null))];

    private static ClearBibleRecord Record(string[] source, string[] target) => new(source, target);
}
