using Essenthos.Core.Door43;
using FluentAssertions;
using Xunit;

namespace Essenthos.Core.Tests;

/// <summary>
/// The first verse of Esther as unfoldingWord aligns it. This is the only stated word-level
/// correspondence anyone publishes for a Slavic text, so reading it wrong would turn the one
/// asserted link the Ukrainian could ever have into another guess.
/// </summary>
public class Usfm3AlignmentTests
{
    private const string Esther =
        """
        \c 1
        \v 1 \zaln-s |x-strong="c:H1961" x-lemma="הָיָה" x-occurrence="1" x-content="וַ⁠יְהִ֖י"\*\w І|x-occurrence="1"\w*
        \w сталося|x-occurrence="1"\w*\zaln-e\*
        \zaln-s |x-strong="b:H3117" x-lemma="יוֹם" x-occurrence="1" x-content="בִּ⁠ימֵ֣י"\*\w за|x-occurrence="1"\w*
        \w днів|x-occurrence="1"\w*\zaln-e\*
        \zaln-s |x-strong="H0325" x-lemma="אֲחַשְׁוֵרוֹשׁ" x-occurrence="1" x-content="אֲחַשְׁוֵר֑וֹשׁ"\*\w Ахашвероша|x-occurrence="1"\w*\zaln-e\*
        """;

    [Fact]
    public void ReadsTheSpansOfAVerse()
    {
        var verses = Usfm3AlignmentReader.Read(Esther);

        verses.Should().ContainSingle();
        verses[0]!.Chapter.Should().Be(1);
        verses[0]!.Number.Should().Be(1);
        verses[0]!.Spans.Should().HaveCount(3);
    }

    [Fact]
    public void EachSpanCarriesItsStrongAndTheWordsThatRenderIt()
    {
        var spans = Usfm3AlignmentReader.Read(Esther)[0]!.Spans;

        spans[0]!.Strong.Should().Be("c:H1961");
        spans[0]!.Words.Should().Equal(["І", "сталося"]);
        spans[2]!.Strong.Should().Be("H0325");
        spans[2]!.Words.Should().Equal(["Ахашвероша"]);
    }

    [Fact]
    public void APrefixedWordSplitsWhereBhsaSplitsIt()
    {
        // וַ⁠יְהִי is two words in BHSA — the conjunction H9000 and the verb H1961 — and the source
        // marks the boundary itself. That is what makes this a join rather than an alignment.
        var spans = Usfm3AlignmentReader.Read(Esther)[0]!.Spans;

        spans[0]!.Morphemes.Should().HaveCount(2);
        spans[1]!.Morphemes.Should().HaveCount(2);
        spans[2]!.Morphemes.Should().ContainSingle();
    }

    [Fact]
    public void AWordOutsideEverySpanIsNotClaimed()
    {
        // Not every translated word renders an original one, and a reader of this file must not
        // invent a link for the ones that do not.
        const string loose =
            """
            \c 1
            \v 1 \w вільне|x-occurrence="1"\w*
            """;

        Usfm3AlignmentReader.Read(loose).Should().BeEmpty();
    }
}
