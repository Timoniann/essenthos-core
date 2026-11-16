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

    /// <summary>Mark 1:30: in a run divided differently, each word covers the tagged words of its own letters.</summary>
    [Fact]
    public void EachWordOfARunDividedDifferentlyCoversTheTaggedWordsOfItsOwnLetters()
    {
        string[] tagged = ["But", "Simon", "s", "wife", "s", "mother", "lay"];
        string[] corpus = ["But", "Simon's", "wife's", "mother", "lay"];
        var spans = TaggedEdition.Align(tagged, corpus);
        var run = spans.Single(span => span.CorpusFrom == 1);

        (run.TaggedFrom, run.TaggedTo, run.CorpusTo, run.Match).Should().Be((1, 5, 3, EditionMatch.Divided));
        TaggedEdition.Covered(run, tagged, corpus).Should().Equal((1, 3), (3, 5));
    }

    /// <summary>
    /// Matthew 22:21 and Romans 16:5: a run that differs by a name's spelling as well as its division is paired piece by
    /// piece when asked, and only then.
    /// </summary>
    [Fact]
    public void ARunThatDiffersByANamesSpellingIsPairedPieceByPieceWhenAsked()
    {
        string[] tagged = ["are", "Cesar", "s", "and"];
        string[] corpus = ["are", "Caesar's", "and"];

        TaggedEdition.Align(tagged, corpus).Should().NotContain(span => span.CorpusFrom == 1);
        TaggedEdition.Align(tagged, corpus, spellings: true).Single(span => span.CorpusFrom == 1).Should()
            .Be(new EditionSpan(1, 3, 1, 2, EditionMatch.Spelling));

        var salute = TaggedEdition.Align(["Salute", "my", "wellbeloved", "Epenetus", "who"], ["Salute", "my", "well-beloved", "Epaenetus", "who"], spellings: true);
        salute.Should().Contain(new EditionSpan(2, 3, 2, 3, EditionMatch.Divided));
        salute.Should().Contain(new EditionSpan(3, 4, 3, 4, EditionMatch.Spelling));
    }

    /// <summary>Different words, however near in letters, stay apart: a run is paired only where every piece is a spelling.</summary>
    [Fact]
    public void ARunOfDifferentWordsStaysUnpairedEvenWhenAsked()
    {
        var spans = TaggedEdition.Align(["he", "said", "unto", "them", "go"], ["he", "spake", "to", "him", "go"], spellings: true);

        spans.Should().NotContain(span => span.CorpusFrom >= 1 && span.CorpusTo <= 4);
    }

    private static (int, int, int, int) Covering(List<EditionSpan> spans, int taggedWord)
    {
        var span = spans.Single(s => s.TaggedFrom <= taggedWord && taggedWord < s.TaggedTo);
        return (span.TaggedFrom, span.TaggedTo, span.CorpusFrom, span.CorpusTo);
    }
}
