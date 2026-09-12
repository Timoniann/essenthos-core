using Essenthos.Core.Loading.Links;
using Essenthos.Core.XmlBible;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Essenthos.Core.Tests;

/// <summary>
/// The Strong-tagged Synodal as swmail/RST publishes it, read as the mapping's input: which words it
/// writes, which numbers each carries, and which of them are one rendering.
///
/// The snippets are the file's own markup, cut down. The last two tests read the whole file and lay
/// it onto bible4u's Synodal, and are skipped where the fetch script has not been run.
/// </summary>
public sealed class SynodalStrongEditionTests(ITestOutputHelper output)
{
    private const int Psalms = 19;

    private const int Romans = 45;

    /// <summary>What the edition holds, counted on the file at swmail/RST 78d6bbf.</summary>
    private const int EditionVerses = 31_163;

    private static Dictionary<(int Book, int Chapter, int Verse), List<EditionWord>> Read(string verses) =>
        SynodalStrongEdition.Read(new StringReader(
            $"""
             <?xml version="1.0" encoding="UTF-8" ?>
             <osis xmlns="http://www.bibletechnologies.net/2003/OSIS/namespace">
             <osisText osisIDWork="RSTE"><div type="book" osisID="Gen"><chapter osisID="Gen.1">
             {verses}
             </chapter></div></osisText></osis>
             """));

    [Fact]
    public void AVerseIsReadAtTheAddressTheEditionGivesIt()
    {
        var verses = Read("""<verse osisID="Ps.22.1">Псалом <w lemma="strong:H1732">Давида</w>.</verse>""");

        verses.Keys.Should().Equal((Psalms, 22, 1));
        verses[(Psalms, 22, 1)].Select(word => word.Text).Should().Equal("Псалом", "Давида");
        verses[(Psalms, 22, 1)][1].Numbers.Should().Equal("H1732");
        verses[(Psalms, 22, 1)][0].Numbers.Should().BeEmpty();
    }

    /// <summary>
    /// A section heading and a cross-reference are the module's apparatus. Read as words they would
    /// be words no corpus verse has, and every verse carrying one would fail to agree.
    /// </summary>
    [Fact]
    public void HeadingsAndCrossReferencesAreNotWords()
    {
        var verses = Read(
            """
            <verse osisID="Gen.1.1"><title subType="x-preverse" type="section">Сотворение неба и земли</title>В <w lemma="strong:H7225">начале</w><note type="crossReference"><reference osisRef="Ps.32.6"></reference></note>.</verse>
            """);

        verses[(1, 1, 1)].Select(word => word.Text).Should().Equal("В", "начале");
    }

    /// <summary>
    /// The edition writes a zero beside the number on each word of a rendering several words make —
    /// <em>начало быть</em> for ἐγένετο. Read as two occurrences, John 1:3's three renderings are six
    /// claimants for three Greek words and nothing pairs.
    /// </summary>
    [Fact]
    public void WordsTheZeroMarksAsOneRenderingAreOneUnit()
    {
        var words = Read(
            """
            <verse osisID="John.1.3"><w lemma="strong:G1096 strong:G0" morph="strongMorph:TG5633">начало</w> <w lemma="strong:G1096 strong:G0" morph="strongMorph:TG5633">быть</w>, <w lemma="strong:G1096 strong:G0">начало</w> <w lemma="strong:G1096 strong:G0">быть</w>.</verse>
            """)[(43, 1, 3)];

        words.Select(word => word.Numbers).Should().AllSatisfy(numbers => numbers.Should().Equal("G1096"));
        words[0].Unit.Should().Be(words[1].Unit);
        words[2].Unit.Should().Be(words[3].Unit);
        words[1].Unit.Should().NotBe(words[2].Unit, "a comma stands between the two renderings");
    }

    /// <summary>
    /// Without the zero, two neighbours carrying one number are two words of the original —
    /// <em>смертью умрешь</em> is מוֹת תָּמוּת — and joining them would lose one.
    /// </summary>
    [Fact]
    public void NeighboursWithoutTheZeroAreTwoRenderings()
    {
        var words = Read(
            """<verse osisID="Gen.2.17"><w lemma="strong:H4191">смертью</w> <w lemma="strong:H4191">умрешь</w>.</verse>""")[(1, 2, 17)];

        words[0].Unit.Should().NotBe(words[1].Unit);
    }

    /// <summary>A phrase the edition tags as one element is one rendering of its number.</summary>
    [Fact]
    public void APhraseTaggedOnceIsOneUnit()
    {
        var words = Read("""<verse osisID="Gen.1.3">И <w lemma="strong:H216">стал свет</w>.</verse>""")[(1, 1, 3)];

        words.Select(word => word.Text).Should().Equal("И", "стал", "свет");
        words[1].Unit.Should().Be(words[2].Unit);
        words[1].Numbers.Should().Equal("H216");
        words[0].Unit.Should().NotBe(words[1].Unit);
    }

    /// <summary>
    /// The whole file is in the Synodal's numbering, whatever its header says: Psalm 22 is the
    /// shepherd psalm and the Romans doxology stands at the end of chapter 14.
    /// </summary>
    [Fact]
    public void TheEditionIsNumberedTheSynodalWay()
    {
        if (Absent())
        {
            return;
        }

        var edition = SynodalStrongEdition.Read(TestResources.SynodalStrong);

        edition.Should().HaveCount(EditionVerses);
        edition.Keys.Select(address => address.Book).Distinct().Should().HaveCount(66);
        string.Join(' ', edition[(Psalms, 22, 1)].Select(word => word.Text)).Should().Contain("Господь Пастырь мой");
        string.Join(' ', edition[(Romans, 14, 24)].Select(word => word.Text)).Should().StartWith("Могущему же утвердить");
    }

    /// <summary>
    /// Laid onto bible4u's Synodal through the addresses bible4u prints, every tagged verse finds a
    /// verse and every verse agrees — measured, not assumed, since this is the claim the whole
    /// mapping rests on.
    /// </summary>
    [Fact]
    public void TheWholeEditionLaysOntoTheLoadedSynodal()
    {
        if (Absent())
        {
            return;
        }

        var edition = SynodalStrongEdition.Read(TestResources.SynodalStrong);
        var bible = new XmlBibleParser().Parse(File.ReadAllText(TestResources.Bible4u("RUSV")));

        var id = 0L;
        var corpus = bible.Books
            .SelectMany(book => book.Chapters.SelectMany(chapter => chapter.Verses.Select(verse => new CorpusVerse(
                book.BNumber,
                chapter.CNumber,
                verse.VNumber,
                VerseWords.StatedAddresses(verse.Text),
                [.. VerseWords.Parse(verse.Text).Select(token => new CorpusWord(++id, token.Word))]))))
            .OrderBy(verse => (verse.Book, verse.Chapter, verse.Verse))
            .ToList();

        var laid = SynodalStrongLayer.Lay(edition, corpus);
        output.WriteLine(laid.ToString());

        laid.Unused.Should().Be(0);
        laid.Refused.Should().Be(0);
        laid.Verses.Should().Be(corpus.Count);
        laid.TaggedWords.Should().BeGreaterThan(340_000);
    }

    private bool Absent()
    {
        if (File.Exists(TestResources.SynodalStrong))
        {
            return false;
        }

        output.WriteLine("The tagged Synodal is not on disk; run scripts/fetch-synodal-strong.ps1");
        return true;
    }
}
