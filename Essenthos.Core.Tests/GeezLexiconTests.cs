using System.Xml.Linq;
using Essenthos.Core.BetaMasaheft;
using Essenthos.Core.Corpus;
using FluentAssertions;
using Xunit;

namespace Essenthos.Core.Tests;

/// <summary>
/// Which of Dillmann's headwords a Ge'ez word is a form of: only where one headword is spelt like it
/// and no other is as close, or where the Greek it is aligned to settles which.
/// </summary>
public class GeezLexiconTests
{
    private static readonly GeezLexicon Lexicon = new(
    [
        new GeezHeadword("word", ["ቃል"], ["λαλιά"]),
        new GeezHeadword("son", ["ወልድ"], ["υἱός"]),
        new GeezHeadword("made", ["ገብረ"], ["ποιεῖν"]),
        new GeezHeadword("servant", ["ገብር"], ["δοῦλος"]),
        new GeezHeadword("kill", ["ቀተለ"], ["ἀποκτείνειν"]),
        new GeezHeadword("citron", ["ሎሚ"], []),
        new GeezHeadword("all", ["ኵሉ"], ["πᾶς"]),
        new GeezHeadword("in", ["ውስተ"], ["ἐν", "εἰς"]),
        new GeezHeadword("into", ["ውስተ"], []),
        new GeezHeadword("this", ["ዝ", "እሉ"], ["ταῦτα"]),
    ]);

    [Theory]
    [InlineData("ቃል", "word")]
    [InlineData("ቃለ", "word")]
    [InlineData("ቃልየ", "word")]
    [InlineData("ወቃሉ", "word")]
    [InlineData("ወልደ", "son")]
    [InlineData("ለወልዱ", "son")]
    [InlineData("ኵሎ", "all")]
    [InlineData("እሉ", "this")]
    public void AWordSpeltAsOneHeadwordOnceWhatIsWrittenOntoItIsOffIsAFormOfIt(string word, string entry) =>
        Lexicon.Match(word, []).Should().Be(new GeezLexiconMatch(entry, GeezLexicon.ByForm));

    [Fact]
    public void TwoHeadwordsAsCloseAsEachOtherAreNotChosenBetween()
    {
        Lexicon.Match("ገብሩ", []).Should().BeNull("the servant's and he made differ only in the last vowel");
        Lexicon.Match("ውስተ", []).Should().BeNull("the lexicon has two entries under the one spelling");
    }

    [Fact]
    public void TheAlignedGreekSettlesWhichOfSeveralHeadwordsItIs()
    {
        Lexicon.Match("ገብሩ", ["ποιέω", "ἐποίησαν"]).Should().Be(new GeezLexiconMatch("made", GeezLexicon.ByGreek));
        Lexicon.Match("ገብሩ", ["δοῦλος"]).Should().Be(new GeezLexiconMatch("servant", GeezLexicon.ByGreek));
        Lexicon.Match("ውስተ", ["ἐν"]).Should().Be(new GeezLexiconMatch("in", GeezLexicon.ByGreek));
    }

    [Fact]
    public void SharingOnlyTheConsonantsIsNotEnough()
    {
        Lexicon.Match("ይቀትል", []).Should().BeNull("the imperfect shares the verb's consonants and nothing more");
        Lexicon.Match("ይቀትል", ["ἀποκτείνω"]).Should().Be(new GeezLexiconMatch("kill", GeezLexicon.ByGreek));
    }

    [Fact]
    public void AShortWordEndingInAVowelIsNotReadAsAnotherVowel() =>
        Lexicon.Match("ሎሙ", []).Should().BeNull("to them is not citron");

    [Fact]
    public void AWordIsLookedUpByEveryWayOfTakingItApart() =>
        GeezLexicon.KeysOf("ወቃልየ").Should().Contain(GeezLexicon.ConsonantsOf("ቃል"));

    [Fact]
    public void DillmannsGlossesAreReadWithoutHisNotesOnTheCognatesAndItsGreekBesideThem()
    {
        var entry = DillmannLexicon.Parse(XDocument.Parse(
            """
            <TEI xmlns="http://www.tei-c.org/ns/1.0"><text><body><div>
              <entry xml:id="Lx" n="1"><form><foreign xml:lang="gez">ብህለ</foreign></form>
                <sense xml:id="Dmain" xml:lang="la" source="#dillmann">[Arabice <cit type="translation" xml:lang="la"><quote> maledixit </quote></cit>]
                  <cit type="translation" xml:lang="la"><quote> dixit </quote></cit>, <cit type="translation" xml:lang="la"><quote> ê </quote></cit>
                  <foreign xml:lang="grc">εἶπεν</foreign></sense>
                <sense xml:id="Tmain" xml:lang="en" source="#traces"><cit type="translation" xml:lang="en"><quote>to say</quote></cit></sense>
              </entry></div></body></text></TEI>
            """))!;

        entry.Headword.Should().Be("ብህለ");
        entry.Latin.Should().Equal("dixit");
        entry.Greek.Should().Equal("εἶπεν");
        entry.Refers.Should().BeNull();
    }

    [Fact]
    public void AnEntryThatOnlySendsTheReaderElsewhereBecomesASpellingOfThatEntry()
    {
        var pointer = DillmannLexicon.Parse(XDocument.Parse(
            """
            <TEI xmlns="http://www.tei-c.org/ns/1.0"><text><body><div>
              <entry xml:id="Lp" n="2"><form><foreign xml:lang="gez">እለ</foreign></form>
                <sense xml:id="Dmain" xml:lang="la" source="#dillmann"><lbl expand="videas">vid. sub</lbl> <foreign xml:lang="gez">ዘ፡</foreign></sense>
              </entry></div></body></text></TEI>
            """))!;
        var target = new DillmannEntry("Lz", "ዘ", ["qui"], [], null);
        var electrum = new DillmannEntry("Le", "እለ", ["electrum"], ["ἤλεκτρον"], null);

        var headwords = DillmannLexicon.Headwords([pointer, target, electrum]);

        headwords.Should().HaveCount(2);
        headwords.Single(headword => headword.Entry == "Lz").Forms.Should().Equal("ዘ", "እለ");
    }
}
