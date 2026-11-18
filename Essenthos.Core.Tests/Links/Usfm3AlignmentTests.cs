using Essenthos.Core.Door43;
using FluentAssertions;
using Xunit;

namespace Essenthos.Core.Tests.Links;

/// <summary>
/// The first verse of Esther as unfoldingWord aligns it, and the first of Titus as the Door43
/// community aligned the Synodal. This ecosystem is the only place a stated word-level
/// correspondence for a Slavic text is published, so reading it wrong would turn the only asserted
/// links either text could ever have into more guesses.
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

        spans[0]!.Originals.Single().Morphemes.Should().HaveCount(2);
        spans[1]!.Originals.Single().Morphemes.Should().HaveCount(2);
        spans[2]!.Originals.Single().Morphemes.Should().ContainSingle();
    }

    /// <summary>
    /// Titus 1:1 as the Synodal alignment writes it. Two things differ from the Ukrainian above and
    /// both would break a reader that only ever saw one file: the milestone puts a space after its
    /// pipe, and the last span here is a nest — two Greek words over one Russian word.
    /// </summary>
    private const string Titus =
        """
        \c 1
        \v 1 \zaln-s | x-strong="G39720" x-lemma="Παῦλος" x-occurrence="1" x-content="Παῦλος"\*\w Павел|x-occurrence="1"\w*\zaln-e\*,
        \zaln-s | x-strong="G14010" x-lemma="δοῦλος" x-occurrence="1" x-content="δοῦλος"\*\w раб|x-occurrence="1"\w*\zaln-e\*
        \zaln-s | x-strong="G35880" x-lemma="ὁ" x-occurrence="1" x-content="τῆς"\*\zaln-s | x-strong="G25960" x-lemma="κατά" x-occurrence="1" x-content="κατ’"\*\w относящейся|x-occurrence="1"\w*\zaln-e\*\zaln-e\*
        """;

    [Fact]
    public void ReadsTheSynodalAlignmentDespiteTheSpaceAfterItsPipe()
    {
        var spans = Usfm3AlignmentReader.Read(Titus)[0]!.Spans;

        spans.Should().HaveCount(3);
        spans[0]!.Strong.Should().Be("G39720");
        spans[0]!.Words.Should().Equal(["Павел"]);
        spans[1]!.Words.Should().Equal(["раб"]);
    }

    [Fact]
    public void AWordInsideANestRendersEveryOriginalWordOfIt()
    {
        // τῆς and κατ' both stand over относящейся: translationCore's group of two Greek words over one
        // Russian word. The source says the word renders the two together, and nothing finer.
        var spans = Usfm3AlignmentReader.Read(Titus)[0]!.Spans;

        spans.Should().HaveCount(3);
        spans[2]!.Words.Should().Equal(["относящейся"]);
        spans[2]!.Originals.Select(original => original.Strong).Should().Equal("G35880", "G25960");
    }

    [Fact]
    public void ANestKeepsItsOuterWordWhenTheInnerOneIsRead()
    {
        // Genesis 8:10 in the unfoldingWord Literal Text: «Then he waited» renders וַיָּחֶל and עוֹד together.
        const string waited =
            """
            \c 8
            \v 10 \zaln-s |x-strong="c:H2342a" x-occurrence="1" x-occurrences="1" x-content="וַ⁠יָּ֣חֶל"\*\zaln-s |x-strong="H5750" x-occurrence="1" x-occurrences="1" x-content="ע֔וֹד"\*\w Then|x-occurrence="1" x-occurrences="1"\w*
            \w he|x-occurrence="1" x-occurrences="1"\w*
            \w waited|x-occurrence="1" x-occurrences="1"\w*\zaln-e\*\zaln-e\*
            \zaln-s |x-strong="H0312" x-occurrence="1" x-occurrences="1" x-content="אֲחֵרִ֑ים"\*\w another|x-occurrence="1" x-occurrences="1"\w*\zaln-e\*
            """;

        var spans = Usfm3AlignmentReader.Read(waited).Single().Spans;

        spans.Should().HaveCount(2);
        spans[0].Words.Should().Equal("Then", "he", "waited");
        spans[0].Originals.Select(original => original.Strong).Should().Equal("c:H2342a", "H5750");
        spans[1].Originals.Select(original => original.Strong).Should().Equal("H0312");
    }

    [Fact]
    public void WordsPrintedAsOneAreMarkedJoined()
    {
        // «the bird{s}» and «he-goats»: the source cuts each printed word in two, and the text prints it whole.
        const string pieces =
            """
            \c 1
            \v 22 \zaln-s |x-strong="c:d:H5775" x-occurrence="1" x-occurrences="1" x-content="וְ⁠הָ⁠ע֖וֹף"\*\w the|x-occurrence="3" x-occurrences="4"\w*
            \w bird|x-occurrence="1" x-occurrences="1"\w*{\w s|x-occurrence="1" x-occurrences="1"\w*\zaln-e\*}
            \zaln-s |x-strong="H7235a" x-occurrence="1" x-occurrences="1" x-content="יִ֥רֶב"\*\w multiply|x-occurrence="2" x-occurrences="2"\w*\zaln-e\*
            \zaln-s |x-strong="H7344" x-occurrence="1" x-occurrences="1" x-content="רְחֹבֹ֥ת"\*\w Rehoboth|x-occurrence="1" x-occurrences="1"\w*\zaln-e\*-\zaln-s |x-strong="H5892b" x-occurrence="1" x-occurrences="1" x-content="עִ֖יר"\*\w Ir|x-occurrence="1" x-occurrences="1"\w*\zaln-e\*
            """;

        var spans = Usfm3AlignmentReader.Read(pieces).Single().Spans;

        spans[0].Glue!.Select(glue => (glue.Joined, string.Join(' ', glue.After)))
            .Should().Equal((false, ""), (false, "s"), (true, ""));
        spans[1].Glue.Should().BeNull();
        spans[2].Glue!.Single().Should().Match<WordGlue>(glue => !glue.Joined && glue.After.Single() == "Ir");
        spans[3].Glue!.Single().Joined.Should().BeTrue();
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
