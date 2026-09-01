using Essenthos.Core.Utils;
using FluentAssertions;
using Xunit;
using static Essenthos.Core.Utils.SentenceWordAligner;

namespace Essenthos.Core.Tests;

/// <summary>
/// Tests for <see cref="SentenceWordAligner"/>. Covers core alignment scenarios
/// (exact, similar, missing, split/merge) and Bible-specific edge cases where
/// verse word lists differ between translations or database sources.
/// </summary>
public class SentenceWordAlignerTests
{
    // Helper aliases for readability
    private static WordMatchType Exact => WordMatchType.Exact;
    private static WordMatchType Similar => WordMatchType.Similar;
    private static WordMatchType Missing => WordMatchType.Missing;
    private static WordMatchType Substring => WordMatchType.Substring;

    [Fact]
    public void Align_IdenticalLists_AllExact()
    {
        var words = new[] { "in", "the", "beginning" };

        var result = SentenceWordAligner.Align(words, words);

        result.SourceAlignment.Should().HaveCount(3);
        result.TargetAlignment.Should().HaveCount(3);
        result.SourceAlignment.Should().OnlyContain(m => m.Type == Exact && m.MappedWords.Length == 1);
        result.TargetAlignment.Should().OnlyContain(m => m.Type == Exact && m.MappedWords.Length == 1);
    }

    [Fact]
    public void Align_SimilarAndMissingWords_MapsCorrectly()
    {
        // "was" ? "wasn't" (similar), "running" ? "ruining" (similar), "it" and "afternoon" are extra
        var source = new[] { "I", "was", "running", "yesterday" };
        var target = new[] { "I", "wasn't", "ruining", "it", "yesterday", "afternoon" };

        var result = SentenceWordAligner.Align(source, target);

        result.SourceAlignment.Should().HaveCount(source.Length);
        result.TargetAlignment.Should().HaveCount(target.Length);

        // Source perspective
        AssertMapping(result.SourceAlignment[0], "I", Exact, "I");
        AssertMapping(result.SourceAlignment[1], "was", Similar, "wasn't");
        AssertMapping(result.SourceAlignment[2], "running", Similar, "ruining");
        AssertMapping(result.SourceAlignment[3], "yesterday", Exact, "yesterday");

        // Target perspective: "it" and "afternoon" are missing
        AssertMapping(result.TargetAlignment[0], "I", Exact, "I");
        AssertMapping(result.TargetAlignment[1], "wasn't", Similar, "was");
        AssertMapping(result.TargetAlignment[2], "ruining", Similar, "running");
        AssertMissing(result.TargetAlignment[3], "it");
        AssertMapping(result.TargetAlignment[4], "yesterday", Exact, "yesterday");
        AssertMissing(result.TargetAlignment[5], "afternoon");
    }

    [Fact]
    public void Align_WordSplitAndMerge_DetectsSubstring()
    {
        // "farewall" should split into "fare" + "wall" and vice versa
        var single = new[] { "farewall" };
        var split = new[] { "fare", "wall" };

        var forward = SentenceWordAligner.Align(single, split);
        forward.SourceAlignment.Should().HaveCount(1);
        forward.SourceAlignment[0].MappedWords.Select(w => w.Item).Should().BeEquivalentTo("fare", "wall");
        forward.SourceAlignment[0].Type.Should().Be(Substring);
        forward.TargetAlignment.Should().HaveCount(2);
        forward.TargetAlignment[0].Type.Should().Be(Substring);
        forward.TargetAlignment[1].Type.Should().Be(Substring);

        var reverse = SentenceWordAligner.Align(split, single);
        reverse.TargetAlignment.Should().HaveCount(1);
        reverse.TargetAlignment[0].MappedWords.Select(w => w.Item).Should().BeEquivalentTo("fare", "wall");
        reverse.TargetAlignment[0].Type.Should().Be(Substring);
    }

    [Fact]
    public void Align_MissingBlocks_DetectsGaps()
    {
        // Words 1-4 and 9 have no counterpart in the shorter list
        var full = new[] { "1", "2", "3", "4", "5", "6", "7", "8", "9" };
        var partial = new[] { "5", "6", "7", "8" };

        var result = SentenceWordAligner.Align(full, partial);

        result.SourceAlignment.Take(4).Should().OnlyContain(m => m.Type == Missing && m.MappedWords.Length == 0);
        result.SourceAlignment.Skip(4).Take(4).Should().OnlyContain(m => m.Type == Exact);
        result.SourceAlignment.Last().Should().Match<WordMapping<string, string>>(m => m.Word.Item == "9" && m.Type == Missing);
        result.TargetAlignment.Should().HaveCount(4).And.OnlyContain(m => m.Type == Exact);
    }

    [Fact]
    public void Align_BiggestSequence_PrefersLongerContiguousBlock()
    {
        // [1,2,3,4] should match the longer contiguous block, not the shorter [1,2,3] prefix
        var source = new[] { "1", "2", "3", "4", "5", "6", "7", "8" };
        var target = new[] { "1", "2", "3", "0", "9", "9", "1", "2", "3", "4" };

        var result = SentenceWordAligner.Align(source, target);

        for (int i = 0; i < 4; i++)
        {
            result.SourceAlignment[i].Type.Should().Be(Exact);
            result.SourceAlignment[i].Word.Item.Should().Be((i + 1).ToString());
        }
    }

    #region Bible-specific tests

    [Fact]
    public void Bible_MosesSpelling_DetectedAsSimilar()
    {
        var result = SentenceWordAligner.Align(["Moseus"], ["Moses"]);

        result.SourceAlignment[0].Type.Should().Be(Similar);
        result.TargetAlignment[0].Type.Should().Be(Similar);
    }

    [Fact]
    public void Bible_ExtraConjunction_MarkedAsMissing()
    {
        // One version has "and" between "Moses" and "Aaron", the other doesn't
        var withAnd = new[] { "Moses", "and", "Aaron", "said" };
        var withoutAnd = new[] { "Moses", "Aaron", "said" };

        var result = SentenceWordAligner.Align(withAnd, withoutAnd);

        result.SourceAlignment[1].Word.Item.Should().Be("and");
        result.SourceAlignment[1].Type.Should().Be(Missing);
        result.TargetAlignment.Should().OnlyContain(m => m.Word.Item != "and");
    }

    [Fact]
    public void Bible_Rev21_17_ArticleDifference()
    {
        // "a" vs "an" — minor article difference should be detected as similar
        List<string> csvWords =
        [
            "And", "he", "measured", "the", "wall", "thereof", "an", "hundred", "and", "forty", "and", "four", "cubits",
            "according", "to", "the", "measure", "of", "a", "man", "that", "is", "of", "the", "angel"
        ];
        List<string> dbWords =
        [
            "And", "he", "measured", "the", "wall", "thereof", "a", "hundred", "and", "forty", "and", "four", "cubits",
            "according", "to", "the", "measure", "of", "a", "man", "that", "is", "of", "the", "angel"
        ];

        var result = SentenceWordAligner.Align(dbWords, csvWords);

        AssertExactRange(result, 0, 6);
        result.SourceAlignment[6].Word.Item.Should().Be("a");
        result.SourceAlignment[6].MappedWords[0].Item.Should().Be("an");
        result.SourceAlignment[6].Type.Should().Be(Similar);
        AssertExactRange(result, 7, dbWords.Count);
    }

    [Fact]
    public void Bible_Ezek20_3_TrailingWordsMissing()
    {
        // DB version is shorter — csv has extra trailing words "of", "by", "you"
        List<string> csvWords = ["Son", "of", "man", "speak", "unto", "the", "elders", "of", "Israel", "and", "say", "unto", "them", "Thus", "saith", "the", "Lord", "GOD", "Are", "ye", "come", "to", "enquire", "of", "me", "As", "I", "live", "saith", "the", "Lord", "GOD", "I", "will", "not", "be", "enquired", "of", "by", "you"];
        List<string> dbWords = ["Son", "of", "man", "speak", "unto", "the", "elders", "of", "Israel", "and", "say", "unto", "them", "Thus", "saith", "the", "Lord", "GOD", "Are", "ye", "come", "to", "enquire", "of", "me", "As", "I", "live", "saith", "the", "Lord", "GOD", "I", "will", "not", "be", "enquired"];

        var result = SentenceWordAligner.Align(csvWords, dbWords);

        AssertExactRange(result, 0, 36);
        // "of", "by", "you" are missing from csv side
        result.SourceAlignment[37].MappedWords.Length.Should().Be(0);
        result.SourceAlignment[38].MappedWords.Length.Should().Be(0);
        result.SourceAlignment[39].MappedWords.Length.Should().Be(0);
    }

    [Fact]
    public void Bible_Hab3_16_TrailingWordsMissing()
    {
        List<string> csvWords = ["When", "I", "heard", "my", "belly", "trembled", "my", "lips", "quivered", "at", "the",
            "voice", "rottenness", "entered", "into", "my", "bones", "and", "I", "trembled", "in", "myself", "that", "I",
            "might", "rest", "in", "the", "day", "of", "trouble", "when", "he", "cometh", "up", "unto", "the", "people",
            "he", "will", "invade", "them", "with", "his", "troops"];
        List<string> dbWords = ["When", "I", "heard", "my", "belly", "trembled", "my", "lips", "quivered", "at", "the",
            "voice", "rottenness", "entered", "into", "my", "bones", "and", "I", "trembled", "in", "myself", "that", "I",
            "might", "rest", "in", "the", "day", "of", "trouble", "when", "he", "cometh", "up", "unto", "the", "people",
            "he", "will", "invade"];

        var result = SentenceWordAligner.Align(csvWords, dbWords);

        AssertExactRange(result, 0, 40);
        // "them", "with", "his", "troops" are missing
        result.SourceAlignment[41].MappedWords.Length.Should().Be(0);
        result.SourceAlignment[42].MappedWords.Length.Should().Be(0);
        result.SourceAlignment[43].MappedWords.Length.Should().Be(0);
        result.SourceAlignment[44].MappedWords.Length.Should().Be(0);
    }

    [Fact]
    public void Bible_Mal3_10_HyphenatedCompoundWords()
    {
        // "in law" (two words) vs "-in-law" (one hyphenated word)
        List<string> csvWords = [
            "For", "I", "am", "come", "to", "set", "a", "man", "at", "variance", "against", "his", "father", "and",
            "the", "daughter", "against", "her", "mother", "and", "the", "daughter", "in", "law", "against", "her",
            "mother", "in", "law"];
        List<string> dbWords = [
            "For", "I", "am", "come", "to", "set", "a", "man", "at", "variance", "against", "his", "father", "and",
            "the", "daughter", "against", "her", "mother", "and", "the", "daughter", "-in-law", "against", "her",
            "mother", "-in-law"];

        var result = SentenceWordAligner.Align(csvWords, dbWords);

        AssertExactRange(result, 0, 21);
        // "in" and "law" are unmatched; "-in-law" is unmatched
        result.SourceAlignment[22].Word.Item.Should().Be("in");
        result.SourceAlignment[22].MappedWords.Length.Should().Be(0);
        result.SourceAlignment[23].Word.Item.Should().Be("law");
        result.SourceAlignment[23].MappedWords.Length.Should().Be(0);
        result.TargetAlignment[22].Word.Item.Should().Be("-in-law");
        result.TargetAlignment[22].MappedWords.Length.Should().Be(0);

        AssertExactRange(result, 24, 27, sourceOffset: 0, targetOffset: -1);
        result.SourceAlignment[27].Word.Item.Should().Be("in");
        result.SourceAlignment[27].MappedWords.Length.Should().Be(0);
        result.SourceAlignment[28].Word.Item.Should().Be("law");
        result.SourceAlignment[28].MappedWords.Length.Should().BeOneOf(0, 1);
        result.TargetAlignment[26].Word.Item.Should().Be("-in-law");
        result.TargetAlignment[26].MappedWords.Length.Should().BeOneOf(0, 1);
    }

    [Fact]
    public void Bible_Rev9_5_SingleExtraWord()
    {
        // csv has extra "as" that db doesn't
        List<string> csvWords = ["And", "to", "them", "it", "was", "given", "that", "they", "should", "not", "kill", "them", "but", "that", "they", "should", "be", "tormented", "five", "months", "and", "their", "torment", "was", "as", "the", "torment", "of", "a", "scorpion", "when", "he", "striketh", "a", "man"];
        List<string> dbWords = ["And", "to", "them", "it", "was", "given", "that", "they", "should", "not", "kill", "them", "but", "that", "they", "should", "be", "tormented", "five", "months", "and", "their", "torment", "was", "the", "torment", "of", "a", "scorpion", "when", "he", "striketh", "a", "man"];

        var result = SentenceWordAligner.Align(csvWords, dbWords);

        AssertExactRange(result, 0, 24);
        AssertMissing(result.SourceAlignment[24], "as");
        for (int i = 25; i < csvWords.Count; i++)
        {
            result.SourceAlignment[i].Type.Should().Be(Exact);
            result.TargetAlignment[i - 1].Type.Should().Be(Exact);
        }
    }

    [Fact]
    public void Bible_Rev21_8_SingleExtraArticle()
    {
        // csv has extra "the" before "abominable"
        List<string> csvWords =
        [
            "But", "the", "fearful", "and", "unbelieving", "and", "the", "abominable", "and", "murderers", "and",
            "whoremongers", "and", "sorcerers", "and", "idolaters", "and", "all", "liars", "shall", "have", "their",
            "part", "in", "the", "lake", "which", "burneth", "with", "fire", "and", "brimstone", "which", "is", "the",
            "second", "death"
        ];
        List<string> dbWords = [
            "But", "the", "fearful", "and", "unbelieving", "and", "abominable", "and", "murderers", "and",
            "whoremongers", "and", "sorcerers", "and", "idolaters", "and", "all", "liars", "shall", "have", "their",
            "part", "in", "the", "lake", "which", "burneth", "with", "fire", "and", "brimstone", "which", "is", "the",
            "second", "death"
        ];

        var result = SentenceWordAligner.Align(csvWords, dbWords);

        AssertExactRange(result, 0, 6);
        AssertMissing(result.SourceAlignment[6], "the");
        for (int i = 8; i < csvWords.Count; i++)
        {
            result.SourceAlignment[i].Type.Should().Be(Exact);
            result.TargetAlignment[i - 1].Type.Should().Be(Exact);
        }
    }

    [Fact]
    public void Bible_Ps3_1_LargeHeaderPrefix()
    {
        // DB has a long psalm header prefix that should all be marked as missing
        List<string> csvWords =
        [
            "LORD", "how", "are", "they", "increased", "that", "trouble", "me", "many", "are", "they", "that", "rise",
            "up", "against", "me"
        ];
        List<string> dbWords =
        [
            "", "b", "A", "Psalm", "b", "", "b", "of", "David", "b", "", "b", "when", "he", "fled", "b", "", "b",
            "from", "b", "", "b", "Absalom", "b", "", "b", "his", "son", "b", "LORD", "how", "are", "they", "increased",
            "that", "trouble", "me", "many", "are", "they", "that", "rise", "up", "against"
        ];

        var result = SentenceWordAligner.Align(csvWords, dbWords);

        // First 29 db words are header metadata — all missing
        for (int i = 0; i < 29; i++)
            result.TargetAlignment[i].Type.Should().Be(Missing);

        // Remaining db words match csv words exactly
        for (int i = 29; i < result.TargetAlignment.Count; i++)
        {
            result.TargetAlignment[i].Type.Should().Be(Exact);
            result.SourceAlignment[i - 29].Type.Should().Be(Exact);
        }
        // Last csv word "me" has no counterpart (db is truncated)
        result.SourceAlignment[csvWords.Count - 1].MappedWords.Length.Should().Be(0);
    }

    [Fact]
    public void Bible_Ps122_1_SongHeaderPrefix()
    {
        // DB has "A Song of degrees of David" header before the actual verse text
        List<string> csvWords = ["I", "was", "glad", "when", "they", "said", "unto", "me", "Let", "us", "go", "into", "the", "house", "of", "the", "LORD"];
        List<string> dbWords = ["", "b", "A", "Song", "b", "", "b", "of", "degrees", "b", "", "b", "of", "David", "b", "I", "was", "glad", "when", "they", "said", "unto", "me", "Let", "us", "go", "into", "the", "house", "of", "the", "LORD"];

        var result = SentenceWordAligner.Align(csvWords, dbWords);

        // First 15 db words are header — all missing
        for (int i = 0; i < 15; i++)
            result.TargetAlignment[i].Type.Should().Be(Missing);

        // Remaining words match exactly
        for (int i = 15; i < result.TargetAlignment.Count; i++)
        {
            result.TargetAlignment[i].Type.Should().Be(Exact);
            result.SourceAlignment[i - 15].Type.Should().Be(Exact);
        }
    }

    #endregion

    #region Assertion helpers

    private static void AssertMapping<T1, T2>(WordMapping<T1, T2> mapping, string expectedWord, WordMatchType expectedType, string expectedMapped)
    {
        mapping.Word.Item!.ToString().Should().Be(expectedWord);
        mapping.Type.Should().Be(expectedType);
        mapping.MappedWords.Should().ContainSingle().Which.Item!.ToString().Should().Be(expectedMapped);
    }

    private static void AssertMissing<T1, T2>(WordMapping<T1, T2> mapping, string expectedWord)
    {
        mapping.Word.Item!.ToString().Should().Be(expectedWord);
        mapping.Type.Should().Be(Missing);
        mapping.MappedWords.Should().BeEmpty();
    }

    /// <summary>Asserts that a range of alignments are all Exact matches on both sides.</summary>
    private static void AssertExactRange(
        WordAlignmentResult<string, string> result,
        int from, int to,
        int sourceOffset = 0, int targetOffset = 0)
    {
        for (int i = from; i < to; i++)
        {
            result.SourceAlignment[i + sourceOffset].Type.Should().Be(Exact, $"SourceAlignment[{i + sourceOffset}]");
            result.TargetAlignment[i + targetOffset].Type.Should().Be(Exact, $"TargetAlignment[{i + targetOffset}]");
        }
    }

    #endregion
}
