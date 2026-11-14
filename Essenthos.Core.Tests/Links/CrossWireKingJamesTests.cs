using Essenthos.Core.Loading;
using Essenthos.Core.Loading.Links;
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
        var words = SwordTextSource.Segment(OsisVerse.Parse(Romans1627), SwordSegmentation.Spaced, ref unit, out _, fhlPrefixes: false);

        words.Where(word => word.Added).Select(word => word.Surface).Should().Equal("be");
        words.Single(word => word.Surface == "be").Numbers.Should().BeEmpty();
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
    /// shows is the element's first that is not the article.
    /// </summary>
    [Fact]
    public void EachWordTakesItsElementsNumbersWholeAndShowsTheOneThatIsNotTheArticle()
    {
        string[] corpus = ["To", "God", "only", "wise", "be", "glory", "through", "Jesus", "Christ", "for", "e", "ver", "Amen"];

        var laid = Lay(corpus);

        laid.Verses.Should().Be(1);
        laid.Tags[1].Numbers.Should().Equal("G2316");
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
    [InlineData(new[] { "G3588", "G2889" }, "the", "G3588")]
    [InlineData(new[] { "G3588", "G2889" }, "world", "G2889")]
    [InlineData(new[] { "G2889" }, "The", "G2889")]
    public void TheEnglishArticleOfAPhraseShowsTheArticle(string[] numbers, string word, string number) =>
        CrossWireKingJames.Own(numbers, word).Should().Be(number);

    [Theory]
    [InlineData(new[] { "G3588", "G2316" }, "G2316")]
    [InlineData(new[] { "G3588" }, "G3588")]
    [InlineData(new[] { "G2316", "G3588" }, "G2316")]
    public void AWordShowsTheArticleOnlyWhereItsElementNamesNothingElse(string[] numbers, string number) =>
        CrossWireKingJames.Primary(numbers).Should().Be(number);

    private static LaidKingJames Lay(string[] corpus)
    {
        var unit = 0;
        var pieces = OsisVerse.Parse(Romans1627);
        var words = SwordTextSource.Segment(pieces, SwordSegmentation.Spaced, ref unit, out _, fhlPrefixes: false);
        var unrendered = pieces.Where(piece => piece is { Kind: OsisPieceKind.Word, Text.Length: 0 })
            .Select(piece => ReceivedTextPositions.Read(piece.Lemma, piece.Source)).ToList();
        var elements = new Dictionary<(int, int, int), (List<SwordWord>, List<IReadOnlyList<StatedPosition>>)>
        {
            [(45, 16, 27)] = (words, unrendered),
        };

        return CrossWireKingJames.Lay(elements, [new CorpusVerse(45, 16, 27, [], [.. corpus.Select((word, at) => new CorpusWord(at, word))])]);
    }
}
