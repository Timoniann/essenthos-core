using Essenthos.Core.Loading;
using Essenthos.Core.Loading.Links;
using Essenthos.Core.Mapping;
using Essenthos.Core.Sword;
using FluentAssertions;
using Xunit;

namespace Essenthos.Core.Tests.Links;

/// <summary>
/// CrossWire's King James laid onto another digitisation of it: each word takes its element's numbers whole and the
/// element's unit, the italics are the words in <c>transChange type="added"</c> that name no number, and an element that
/// prints no word keeps its positions as a Greek word the English leaves untranslated.
/// </summary>
public sealed class CrossWireKingJamesTests
{
    /// <summary>Romans 16:27 as the module writes it, with its italic <em>be</em> and two words the English leaves untranslated.</summary>
    private const string Romans1627 =
        """<w src="9" lemma="strong:G3588 lemma.TR:ο" morph="robinson:T-NSM"/><w src="3" lemma="strong:G2316 lemma.TR:θεω" morph="robinson:N-DSM">To God</w> <w src="1" lemma="strong:G3441 lemma.TR:μονω" morph="robinson:A-DSM">only</w> <w src="2" lemma="strong:G4680 lemma.TR:σοφω" morph="robinson:A-DSM">wise</w>, <transChange type="added">be</transChange> <w src="7 8" lemma="strong:G3588 strong:G1391 lemma.TR:η lemma.TR:δοξα" morph="robinson:T-NSF robinson:N-NSF">glory</w> <w src="4" lemma="strong:G1223 lemma.TR:δια" morph="robinson:PREP">through</w> <w src="5" lemma="strong:G2424 lemma.TR:ιησου" morph="robinson:N-GSM">Jesus</w> <w src="6" lemma="strong:G5547 lemma.TR:χριστου" morph="robinson:N-GSM">Christ</w> <w src="9" lemma="strong:G1519 lemma.TR:εις" morph="robinson:PREP">for</w> <w src="10 11" lemma="strong:G3588 strong:G165 lemma.TR:τους lemma.TR:αιωνας" morph="robinson:T-APM robinson:N-APM">ever</w>. <w src="12" lemma="strong:G281 lemma.TR:αμην" morph="robinson:HEB">Amen</w>.""";

    [Fact]
    public void TheReaderMarksTheWordsTheTranslatorsSupplied()
    {
        var unit = 0;
        var words = SwordTextSource.Segment(OsisVerse.Parse(Romans1627), SwordSegmentation.Spaced, ref unit, out _);

        words.Where(word => word.Added).Select(word => word.Surface).Should().Equal("be");
        words.Single(word => word.Surface == "be").Numbers.Should().BeEmpty();
        words.Single(word => word.Surface == "glory").Morphology.Should().Equal("T-NSF", "N-NSF");
    }

    [Fact]
    public void AnElementPrintingNoWordIsAPieceWithItsPositions()
    {
        var empty = OsisVerse.Parse(Romans1627).Where(piece => piece is { Kind: OsisPieceKind.Word, Text.Length: 0 }).ToList();

        empty.Should().ContainSingle();
        ReceivedTextPositions.Read(empty[0].Lemma, empty[0].Source).Should().Equal(new StatedPosition(9, false, "ο"));
    }

    /// <summary>
    /// A word the module prints whole and the corpus divides (as bible4u's <em>to day</em> against <em>today</em>; here
    /// <em>ever</em>) is laid on its letters, and both parts take the element's numbers and its unit; the number each word
    /// shows is the element's first that is not the article, and a function word shows one only of a Greek word of its class.
    /// </summary>
    [Fact]
    public void EachWordTakesItsElementsNumbersWholeAndShowsTheOneThatIsNotTheArticle()
    {
        string[] corpus = ["To", "God", "only", "wise", "be", "glory", "through", "Jesus", "Christ", "for", "e", "ver", "Amen"];

        var laid = Lay(corpus);

        laid.Verses.Should().Be(1);
        laid.Tags[0].Numbers.Should().Equal("G2316");
        laid.Tags[0].Number.Should().BeNull("'To' renders the dative of θεῷ, not the noun");
        laid.Tags[0].Closed.Should().BeTrue();
        laid.Tags[1].Numbers.Should().Equal("G2316");
        laid.Tags[1].Number.Should().Be("G2316");
        laid.Tags[1].Closed.Should().BeFalse();
        laid.Tags[6].Number.Should().Be("G1223", "'through' renders διά");
        laid.Tags[9].Number.Should().Be("G1519");
        laid.Tags[5].Numbers.Should().Equal("G3588", "G1391");
        laid.Tags[5].Number.Should().Be("G1391");
        laid.Tags[4].Supplied.Should().BeTrue();
        laid.Tags[4].Number.Should().BeNull();
        laid.Tags[10].Unit.Should().Be(laid.Tags[11].Unit);
        laid.Tags[11].Number.Should().Be("G165");
        laid.Positions[laid.Tags[5].Unit].Positions.Select(position => position.Position).Should().Equal(7, 8);
        laid.Unrendered[(45, 16, 27)].Should().ContainSingle().Which.Should().Equal(new StatedPosition(9, false, "ο"));
    }

    [Fact]
    public void AVerseTheModuleDoesNotWriteTheSameWayGetsNothing()
    {
        var laid = Lay(["Now", "unto", "him", "that", "is", "of", "power", "to", "stablish", "you"]);

        laid.Refused.Should().Be(1);
        laid.Tags.Should().BeEmpty();
        laid.Unrendered.Should().BeEmpty();
    }

    [Theory]
    [InlineData("T-NSM", true)]
    [InlineData("CONJ", true)]
    [InlineData("PRT-N", true)]
    [InlineData("COND", true)]
    [InlineData("PREP", false)]
    [InlineData("N-DSF", false)]
    [InlineData("A-NUI", false)]
    [InlineData("P-ASM", false)]
    [InlineData(null, false)]
    public void OnlyAnEmptyElementOverAnArticleOrParticleSaysTheEnglishLeavesItOut(string? robinson, bool untranslated) =>
        KingJamesGreekSource.GoesUntranslated(robinson).Should().Be(untranslated);

    /// <summary>
    /// A function word of an element shows the number of a Greek word of its own class, and none where the element names
    /// none (the owner's decision on PRB-1645): the element's link still names the Greek word for it.
    /// </summary>
    [Theory]
    [InlineData("G3588 G2889", "T-ASM N-ASM", "the", "the world", "G3588")]
    [InlineData("G3588 G2889", "T-ASM N-ASM", "world", "the world", "G2889")]
    [InlineData("G2889", "N-ASM", "The", "The world", null)]
    [InlineData("G5207", "N-GSM", "the", "the son", null)]
    [InlineData("G5207", "N-GSM", "son", "the son", "G5207")]
    [InlineData("G1325", "V-AAI-3S", "he", "he gave", null)]
    [InlineData("G1325", "V-AAI-3S", "gave", "he gave", "G1325")]
    [InlineData("G3588 G3004", "T-NSM V-2AAI-3S", "he", "he said", "G3588")]
    [InlineData("G2316", "N-GSM", "of", "of God", null)]
    [InlineData("G1537 G846", "PREP P-GSM", "of", "of him", "G1537")]
    [InlineData("G1537 G846", "PREP P-GSM", "him", "of him", "G846")]
    [InlineData("G846", "P-DPM", "unto", "unto them", null)]
    [InlineData("G1577", "N-DPF", "to", "to the churches", null)]
    [InlineData("G5315", "V-2AAN", "to", "to eat", null)]
    [InlineData("G2064", "V-FDI-3S", "shall", "shall come", null)]
    [InlineData("G1510", "V-FDI-3S", "shall", "shall be", null)]
    [InlineData("G1510", "V-FDI-3S", "be", "shall be", "G1510")]
    [InlineData("G1096", "V-2ADI-3S", "was", "it was", "G1096")]
    [InlineData("G1096", "V-2ADI-3S", "it", "it was", null)]
    [InlineData("G1096", "V-2ADI-3S", "was", "was made", null)]
    [InlineData("G622", "V-2AMS-3S", "should", "should perish", null)]
    [InlineData("G3754 G568", "CONJ V-PAI-3P", "have", "They have", "G568")]
    [InlineData("G3754 G568", "CONJ V-PAI-3P", "They", "They have", null)]
    [InlineData("G1401", "N-NSM", "a", "a servant", null)]
    [InlineData("G5100", "X-NSM", "a", "a certain", null)]
    [InlineData("G5100", "X-NSM", "certain", "a certain", "G5100")]
    [InlineData("G3756 G1410", "PRT-N V-PNI-2P", "cannot", "Ye cannot", "G1410")]
    [InlineData("G3756 G1410", "PRT-N V-PNI-2P", "Ye", "Ye cannot", null)]
    [InlineData("G3756 G3361", "PRT-N PRT-N", "not", "shall not", "G3756")]
    [InlineData("G571", "A-GPM", "not", "believe not", "G571")]
    [InlineData("G1715", "ADV", "before", "before", "G1715")]
    [InlineData("G1500", "ADV", "in", "in vain", null)]
    [InlineData("G1563", "ADV", "there", "there", "G1563")]
    [InlineData("G2521", "V-INI-3S", "there", "there sat", null)]
    [InlineData("G2532", "CONJ", "and", "and", "G2532")]
    [InlineData("G3004", "V-2AAI-3S", "and", "and said", null)]
    [InlineData("G1492", "V-2AAP-NSM", "when", "when he saw", null)]
    [InlineData("G3754", "CONJ", "that", "that", "G3754")]
    [InlineData("G3588 G191", "T-NSM V-PAP-NSM", "that", "He that heareth", "G3588")]
    [InlineData("G3956", "A-NSM", "whosoever", "whosoever", "G3956")]
    [InlineData("G1909 G3739", "PREP R-DSN", "wherein", "wherein", "G1909")]
    [InlineData("G1223", "PREP", "wherefore", "wherefore", "G1223")]
    public void AFunctionWordShowsOnlyTheNumberOfAGreekWordOfItsClass(
        string numbers, string morphology, string word, string element, string? number) =>
        CrossWireKingJames.Own(numbers.Split(' '), morphology.Split(' '), word, element.Split(' ')).Should().Be(number);

    [Theory]
    [InlineData("the", true)]
    [InlineData("unto", true)]
    [InlineData("hath", true)]
    [InlineData("He", true)]
    [InlineData("world", false)]
    [InlineData("whosoever", false)]
    public void TheFunctionWordsAreTheClosedClassesOfEnglish(string word, bool closed) =>
        CrossWireKingJames.Closed(word).Should().Be(closed);

    [Theory]
    [InlineData(new[] { "G3588", "G2316" }, "G2316")]
    [InlineData(new[] { "G3588" }, "G3588")]
    [InlineData(new[] { "G2316", "G3588" }, "G2316")]
    public void AWordShowsTheArticleOnlyWhereItsElementNamesNothingElse(string[] numbers, string number) =>
        CrossWireKingJames.Primary(numbers).Should().Be(number);

    [Theory]
    [InlineData("G3754 G568", "CONJ V-PAI-3P", "G568")]
    [InlineData("G302 G3739", "PRT R-NSM", "G3739")]
    [InlineData("G3756 G3361", "PRT-N PRT-N", "G3756")]
    public void AWordOfContentShowsTheGreekWordOfContentItsElementNames(string numbers, string morphology, string number) =>
        CrossWireKingJames.Primary(numbers.Split(' '), morphology.Split(' ')).Should().Be(number);

    /// <summary>
    /// The module against bible4u's words: <em>wife's</em> and <em>sister's</em> take the element of their own letters, not
    /// Simon's or Paul's, and <em>Caesar's</em>, <em>Sarah's</em> and <em>Epaenetus</em>, which the module spells otherwise,
    /// take theirs.
    /// </summary>
    [Fact]
    [Trait("Category", "Corpus")]
    public void APossessiveTakesItsOwnElementAndANameSpelledOtherwiseIsPaired()
    {
        var elements = SwordTextSource.Elements(TestResources.Folder(CrossWireKingJames.Folder));
        CorpusVerse Verse(int book, int chapter, int verse, int first, string words) =>
            new(book, chapter, verse, [], [.. words.Split(' ').Select((word, at) => new CorpusWord(first + at, word))]);
        var laid = CrossWireKingJames.Lay(elements,
        [
            Verse(41, 1, 30, 0, "But Simon's wife's mother lay sick of a fever and anon they tell him of her"),
            Verse(44, 23, 16, 100, "And when Paul's sister's son heard of their lying in wait he went and entered into the castle and told Paul"),
            Verse(40, 22, 21, 200, "They say unto him Caesar's Then saith he unto them Render therefore unto Caesar the things which are Caesar's and unto God the things that are God's"),
            Verse(45, 4, 19, 300, "And being not weak in faith he considered not his own body now dead when he was about an hundred years old neither yet the deadness of Sarah's womb"),
            Verse(45, 16, 5, 400, "Likewise greet the church that is in their house Salute my well-beloved Epaenetus who is the firstfruits of Achaia unto Christ"),
        ]);

        laid.Tags[1].Number.Should().Be("G4613");
        laid.Tags[2].Number.Should().Be("G3994", "'wife's' is the 'wife s' of the module, over πενθερά");
        laid.Tags[2].Unit.Should().Be(laid.Tags[3].Unit);
        laid.Tags[102].Number.Should().Be("G3972");
        laid.Tags[103].Number.Should().Be("G79", "'sister's' renders ἀδελφῆς");
        laid.Tags[204].Number.Should().Be("G2541");
        laid.Tags[218].Number.Should().Be("G2541");
        laid.Tags[327].Number.Should().Be("G4564");
        laid.Tags[411].Number.Should().Be("G27");
        laid.Tags[412].Number.Should().Be("G1866");
    }

    private static LaidKingJames Lay(string[] corpus)
    {
        var unit = 0;
        var pieces = OsisVerse.Parse(Romans1627);
        var words = SwordTextSource.Segment(pieces, SwordSegmentation.Spaced, ref unit, out _);
        var unrendered = pieces.Where(piece => piece is { Kind: OsisPieceKind.Word, Text.Length: 0 })
            .Select(piece => ReceivedTextPositions.Read(piece.Lemma, piece.Source)).ToList();
        var elements = new Dictionary<(int, int, int), (List<SwordWord>, List<IReadOnlyList<StatedPosition>>)>
        {
            [(45, 16, 27)] = (words, unrendered),
        };

        return CrossWireKingJames.Lay(elements, [new CorpusVerse(45, 16, 27, [], [.. corpus.Select((word, at) => new CorpusWord(at, word))])]);
    }
}
