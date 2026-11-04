using Essenthos.Core.Loading.Links;
using FluentAssertions;
using Xunit;

namespace Essenthos.Core.Tests.Links;

/// <summary>
/// The two printings of the King James, put in step. Every case here is quoted from the pair the
/// corpus actually holds — the Zefania KJV+ against bible4u — and each one refused a whole verse
/// before this existed.
/// </summary>
public class TaggedEditionTests
{
    /// <summary>Matthew 2:20: the tagged edition writes the possessive as a word of its own.</summary>
    [Fact]
    public void JoinsAPossessiveTheOtherEditionWritesAsOneWord()
    {
        var spans = TaggedEdition.Align(
            ["sought", "the", "young", "child", "'s", "life"],
            ["sought", "the", "young", "child's", "life"]);

        Covering(spans, 3).Should().Be((3, 5, 3, 4));
        TaggedEdition.Agreement(spans, 5).Should().Be(1);
    }

    /// <summary>Matthew 6:13: the doxology of the Lord's Prayer, refused over one word.</summary>
    [Fact]
    public void SplitsAWordTheOtherEditionWritesAsTwo()
    {
        var spans = TaggedEdition.Align(
            ["and", "the", "glory", "forever", "Amen"],
            ["and", "the", "glory", "for", "ever", "Amen"]);

        Covering(spans, 3).Should().Be((3, 4, 3, 5));
        spans.Should().HaveCount(5);
    }

    /// <summary>Matthew 6:30, which needs both at once.</summary>
    [Fact]
    public void PutsSeveralRedivisionsInStepInOneVerse()
    {
        var spans = TaggedEdition.Align(
            ["which", "today", "is", "and", "tomorrow", "is", "cast"],
            ["which", "to", "day", "is", "and", "to", "morrow", "is", "cast"]);

        Covering(spans, 1).Should().Be((1, 2, 1, 3));
        Covering(spans, 4).Should().Be((4, 5, 5, 7));
        spans.Sum(span => span.CorpusTo - span.CorpusFrom).Should().Be(9);
    }

    /// <summary>
    /// Matthew 3:4 — <em>leather</em> against <em>leathern</em>. One word for one word is a
    /// spelling, which is what the two editions differ by in 882 verses, and it is aligned but not
    /// counted as agreement.
    /// </summary>
    [Fact]
    public void TakesOneWordForOneWordAsASpellingAndNotAsAgreement()
    {
        var spans = TaggedEdition.Align(
            ["a", "leather", "girdle"],
            ["a", "leathern", "girdle"]);

        Covering(spans, 1).Should().Be((1, 2, 1, 2));
        TaggedEdition.Agreement(spans, 3).Should().BeApproximately(2 / 3d, 0.001);
        spans.Single(span => span.Match == EditionMatch.Spelling).CorpusFrom.Should().Be(1);
    }

    /// <summary>
    /// Matthew 4:2 — <em>hungry</em> against <em>an hungred</em>. Two words against one, and not
    /// the same letters, so nothing here can say which renders which and nothing is said. The rest
    /// of the verse is still aligned, which is the whole point: one word the editions differ over
    /// used to cost the other thirteen.
    /// </summary>
    [Fact]
    public void SaysNothingWhereTheLettersDoNotAgreeAndKeepsTheRestOfTheVerse()
    {
        var spans = TaggedEdition.Align(
            ["he", "was", "afterward", "hungry"],
            ["he", "was", "afterward", "an", "hungred"]);

        spans.Should().HaveCount(3);
        spans.Should().OnlyContain(span => span.CorpusTo <= 3);
    }

    /// <summary>Two verses that merely number the same are still two verses.</summary>
    [Fact]
    public void RefusesAVerseTheEditionsDoNotMostlyWriteTheSameWay()
    {
        var spans = TaggedEdition.Align(
            ["in", "the", "beginning", "was", "the", "Word"],
            ["and", "God", "saw", "that", "it", "was"]);

        TaggedEdition.Agreement(spans, 6).Should().BeLessThan(0.8);
    }

    private static (int, int, int, int) Covering(List<EditionSpan> spans, int taggedWord)
    {
        var span = spans.Single(s => s.TaggedFrom <= taggedWord && taggedWord < s.TaggedTo);
        return (span.TaggedFrom, span.TaggedTo, span.CorpusFrom, span.CorpusTo);
    }
}

/// <summary>
/// The spellings the Zefania KJV+ prints the American way, each from a verse whose numbers the conversion lost when
/// two such words stood together and nothing anchored the pair.
/// </summary>
public class AmericanSpellingTests
{
    [Theory]
    [InlineData("honourable", "honorable")]
    [InlineData("counsellor", "counselor")]
    [InlineData("offence", "offense")]
    [InlineData("heretick", "heretic")]
    [InlineData("stedfast", "steadfast")]
    [InlineData("unmoveable", "unmovable")]
    [InlineData("labour", "labor")]
    [InlineData("Saviour", "Savior")]
    [InlineData("travelled", "traveled")]
    public void TheTwoSpellingsOfAWordFoldToOne(string british, string american) =>
        AmericanSpelling.Fold(british).Should().Be(AmericanSpelling.Fold(american));

    [Theory]
    [InlineData("four", "for")]
    [InlineData("your", "yor")]
    [InlineData("hour", "hor")]
    [InlineData("quick", "quic")]
    [InlineData("sick", "sic")]
    public void ShortWordsThatOnlyLookLikeASpellingAreLeftAlone(string word, string other) =>
        AmericanSpelling.Fold(word).Should().NotBe(AmericanSpelling.Fold(other));

    /// <summary>Mark 15:43: two words spelled differently side by side used to stand in no span at all.</summary>
    [Fact]
    public void TwoRespelledWordsSideBySideAreEachPutInStep()
    {
        string[] tagged = ["of", "Arimathaea", "an", "honorable", "counselor", "which"];
        string[] corpus = ["of", "Arimathaea", "an", "honourable", "counsellor", "which"];

        TaggedEdition.Align(tagged, corpus).Where(s => s.CorpusFrom is 3 or 4).Should().BeEmpty("nothing anchors the pair as printed");
        var spans = TaggedEdition.Align(tagged, corpus, AmericanSpelling.Fold);
        spans.Should().HaveCount(6).And.OnlyContain(s => s.Match == EditionMatch.Identical);
    }

    /// <summary>1 Corinthians 4:11: the fold of <em>dwelling</em> must not part the letters of <em>dwelling place</em> from <em>dwellingplace</em>.</summary>
    [Fact]
    public void ARunDividedDifferentlyIsStillComparedOnItsPrintedLetters()
    {
        var spans = TaggedEdition.Align(["have", "no", "certain", "dwelling", "place"], ["have", "no", "certain", "dwellingplace"], AmericanSpelling.Fold);

        spans.Should().Contain(new EditionSpan(3, 5, 3, 4, EditionMatch.Divided));
        TaggedEdition.Agreement(spans, 4).Should().Be(1);
    }
}
