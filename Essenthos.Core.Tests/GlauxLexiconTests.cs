using System.Text;
using Essenthos.Core.Glaux;
using FluentAssertions;
using Xunit;

namespace Essenthos.Core.Tests;

/// <summary>
/// The Septuagint is the one text in the corpus with no lemma, no Strong number and no morphology,
/// which is why its alignment to BHSA is a statistical model rather than a lookup. GLAUx is the
/// only openly licensed lemmatisation of it that does not descend from CATSS — DOC-0161 — and these
/// tests pin the two things that have to be true for it to be usable: that the format parses, and
/// that a lemma written the Attic way still finds its Koine counterpart in Nestle's lemma list.
/// </summary>
public class GlauxLexiconTests
{
    /// <summary>
    /// Ruth 1:1 as GLAUx writes it: a flat element per word, everything on attributes, and
    /// punctuation tokenised separately with no lemma of its own.
    /// </summary>
    private const string RuthOpening =
        """
        <treebank version="2" xml:lang="grc">
          <sentence struct_id="413849" id="1" document_id="0527-010" analysis="auto">
            <word id="1" form="καὶ" div_chapter="1" div_section="1.1" lemma="καί" postag="c--------"/>
            <word id="2" form="ἐγένετο" div_chapter="1" div_section="1.1" lemma="γίγνομαι" postag="v3saim---"/>
            <word id="3" form="ἐν" div_chapter="1" div_section="1.1" lemma="ἐν" postag="r--------"/>
            <word id="4" form="τῷ" div_chapter="1" div_section="1.1" lemma="ὁ" postag="l-s---md-"/>
            <word id="5" form="κρίνειν" div_chapter="1" div_section="1.1" lemma="κρίνω" postag="v--pna---"/>
            <word id="6" form="τοὺς" div_chapter="1" div_section="1.1" lemma="ὁ" postag="l-p---ma-"/>
            <word id="7" form="κριτὰς" div_chapter="1" div_section="1.1" lemma="κριτής" postag="n-p---ma-"/>
            <word id="8" form="." div_chapter="1" div_section="1.1"/>
          </sentence>
        </treebank>
        """;

    private static IReadOnlyList<GlauxWord> Read(string xml) =>
        GlauxReader.Read(new MemoryStream(Encoding.UTF8.GetBytes(xml))).ToList();

    [Fact]
    public void ReadsEveryLemmatisedWordAndSkipsPunctuation()
    {
        var words = Read(RuthOpening);

        words.Should().HaveCount(7);
        words[1].Form.Should().Be("ἐγένετο");
        words[1].Lemma.Should().Be("γίγνομαι");
        words[1].PartOfSpeech.Should().Be('v');
        words.Should().NotContain(word => word.Form == ".");
    }

    [Fact]
    public void BuildsAFoldedFormToLemmaTable()
    {
        var lexicon = GlauxLexicon.Build(Read(RuthOpening));

        lexicon["εγενετο"].Lemma.Should().Be("γιγνομαι");
        lexicon["κριτασ"].Lemma.Should().Be("κριτησ");
        lexicon["εγενετο"].Share.Should().Be(1);
    }

    /// <summary>
    /// GLAUx is Unicode NFD and Nestle is not, so a table built without folding first agrees with
    /// nothing. The article appears twice here in two cases, and both must reach one entry.
    /// </summary>
    [Fact]
    public void FoldsAcrossAccentCaseAndComposition()
    {
        var lexicon = GlauxLexicon.Build(Read(RuthOpening));

        lexicon.Should().ContainKey("τω");
        lexicon.Should().ContainKey("τουσ");
        lexicon["τω"].Lemma.Should().Be("ο");
        lexicon["τουσ"].Lemma.Should().Be("ο");
    }

    /// <summary>
    /// A form the corpus lemmatises two ways keeps both in view: the leader wins, and the share
    /// says how thin the win was, so a caller can decline to write it.
    /// </summary>
    [Fact]
    public void RecordsHowMuchOfTheEvidenceTheLeadingLemmaHolds()
    {
        var words = new[]
        {
            new GlauxWord("αὐτοῦ", "αὐτός", 'p'),
            new GlauxWord("αὐτοῦ", "αὐτός", 'p'),
            new GlauxWord("αὐτοῦ", "αὐτοῦ", 'd'),
        };

        var choice = GlauxLexicon.Build(words)["αυτου"];

        choice.Lemma.Should().Be("αυτοσ");
        choice.Occurrences.Should().Be(3);
        choice.Share.Should().BeApproximately(2d / 3d, 1e-9);
    }
}
