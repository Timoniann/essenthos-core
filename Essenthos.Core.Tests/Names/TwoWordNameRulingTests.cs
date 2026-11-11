using Essenthos.Core.Loading.Encyclopedia;
using FluentAssertions;
using Xunit;

namespace Essenthos.Core.Tests.Names;

/// <summary>
/// The names a translation prints apart from the word its link reaches: the Kulish Bible's two-word
/// names, of which the statistical link reaches one word and leaves the other blank or puts the next
/// name on it (<em>Астарот Карнаїмі</em> of Genesis 14:5, the second word named Kiriathaim), and the
/// Synodal's <em>Доик</em> in the title of Psalm 52, whose links to the Hebrew were drawn while the
/// Hebrew title carried its neighbours' numbers. The rulings are read as the corpus ships them; the
/// words they name are held by <c>names/&lt;REF&gt;</c>.
/// </summary>
public sealed class TwoWordNameRulingTests
{
    private static OwnRecordRulings File(string source) =>
        SenseReadingFiles.AllRulings().Single(file => file.Source.Contains(source, StringComparison.Ordinal));

    /// <summary>
    /// Every word of a name the Kulish Bible prints in two words where the link reaches the other one:
    /// the half of Ashteroth-karnaim, Almon-diblathaim, Cushan-rishathaim, Baal-shalishah,
    /// Berodach-baladan and Pochereth-hazzebaim left blank, and the plain of Kiriathaim, whose word
    /// the link put on the Hebrew Shaveh. The 1 Chronicles 13:11 <em>Убиттє Ози</em> is left out: it
    /// translates Perez-uzzah as a phrase, and its <em>Ози</em> is Uzzah.
    /// </summary>
    [Fact]
    public void TheKulishBiblesTwoWordNamesAreRuledWordByWordAndStayOnTheirWords()
    {
        var file = File("the names the Kulish Bible prints in two words");

        file.Carry.Should().BeFalse("the link is what went wrong, so the ruling must not cross it");
        file.Method.Should().Be("manual");
        file.Rulings.Should().OnlyContain(r => r.Text == "UKR1871" && r.Corrects == null);
        file.Rulings.Select(r => (r.Reference, r.Position, r.Existing)).Should().Equal(
            ("GEN 14:5", 19, "ashterothkarnaim"),
            ("GEN 14:5", 27, "kiriathaim"),
            ("NUM 33:46", 9, "almondiblathaim"),
            ("JDG 3:8", 14, "cushanrishathaim"),
            ("JDG 3:8", 23, "cushanrishathaim"),
            ("JDG 3:10", 24, "cushanrishathaim"),
            ("JDG 3:10", 33, "cushanrishathaim"),
            ("2KI 4:42", 4, "baalshalishah"),
            ("2KI 20:12", 5, "berodachbaladan"),
            ("ISA 39:1", 5, "berodachbaladan"),
            ("EZR 2:57", 8, "pocherethhazzebaim"));
    }

    /// <summary>
    /// The Synodal's <em>Доик</em> in the title of Psalm 52 is Doeg, as the King James's <em>Doeg</em>
    /// beside it is, ruled on the Synodal's word: its link to the Hebrew title reaches a preposition.
    /// </summary>
    [Fact]
    public void TheSynodalsDoegInTheTitleOfPsalm52IsDoeg()
    {
        var file = File("the Synodal's names in the titles of the Psalms");
        file.Carry.Should().BeFalse();
        file.Rulings.Should().ContainSingle().Which.Should().Match<OwnRecordRuling>(r =>
            r.Text == "RUSV" && r.Reference == "PSA 52:1" && r.Position == 9 && r.Surface == "Доик" && r.Existing == "doeg");
    }
}
