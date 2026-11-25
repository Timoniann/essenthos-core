using Essenthos.Core.ClearBible;
using Essenthos.Core.Loading.Links;
using FluentAssertions;
using Xunit;

namespace Essenthos.Core.Tests.Links;

/// <summary>
/// The Reina-Valera's verses whose records are numbered off as a block, a noun or verb named on function words the set
/// seldom renders it with, are refused whole (PRB-2013).
/// </summary>
public class ClearBibleScrambledTests
{
    private static readonly HashSet<string> Function = new(["que", "se", "de", "hacia", "por", "y"], StringComparer.Ordinal);

    /// <summary>1 Sam 24:8: «que» on קוּם, «se» on קָם and «de» on the cave, «levantasen», «saliendo» and «cueva» named by nothing.</summary>
    [Fact]
    public void AVerseWithTwoUnusualRenderingsOnFunctionWordsIsOffAsABlock()
    {
        var set = new Set();
        set.Add("09024008013", "que", "6965", "verb");
        set.Add("09024008014", "se", "6965", "verb");
        set.Add("09024008021", "de", "4631", "noun");
        set.Elsewhere(1, "levantar", "6965", "verb");
        set.Elsewhere(2, "cueva", "4631", "noun");

        set.Scrambled().Should().Equal(ClearBibleAlignment.Verse(9, 24, 8));
    }

    /// <summary>Gen 19:28: «hacia» for both פְּנֵי is the translator's one rendering, not two ids off.</summary>
    [Fact]
    public void OneRenderingTwiceIsNoSign()
    {
        var set = new Set();
        set.Add("01019028003", "hacia", "6440", "noun");
        set.Add("01019028008", "hacia", "6440", "noun");
        set.Elsewhere(3, "rostro", "6440", "noun");
        set.Elsewhere(4, "faz", "6440", "noun");

        set.Scrambled().Should().BeEmpty();
    }

    /// <summary>2 Sam 10:12: «por» is how the set renders בְּעַד, so only «y» on God is unusual; «de» on בְּעַד would be the second.</summary>
    [Fact]
    public void ARenderingTheSetGivesTheNumberOftenIsNoSign()
    {
        var set = new Set();
        set.Add("10010012004", "por", "1157", "noun");
        set.Add("10010012012", "y", "0430", "noun");
        set.Elsewhere(1, "Dios", "0430", "noun");
        set.Elsewhere(2, "por", "1157", "noun");
        set.Scrambled().Should().BeEmpty();

        var other = new Set();
        other.Add("10010012004", "de", "1157", "noun");
        other.Add("10010012012", "y", "0430", "noun");
        other.Elsewhere(1, "Dios", "0430", "noun");
        other.Elsewhere(2, "por", "1157", "noun");
        other.Scrambled().Should().Equal([ClearBibleAlignment.Verse(10, 10, 12)], "«de» renders it once in eleven");
    }

    private sealed class Set
    {
        private readonly List<ClearBibleRecord> _records = [];
        private readonly List<ClearBibleToken> _targets = [];
        private readonly List<ClearBibleToken> _sources = [];

        public void Add(string target, string text, string strong, string part)
        {
            _records.Add(new ClearBibleRecord([$"o{target}1"], [target]));
            _targets.Add(new ClearBibleToken(target, text, false, null));
            _sources.Add(new ClearBibleToken($"o{target}1", "א", false, strong, null, part));
        }

        /// <summary>Ten records of Genesis' chapter <paramref name="chapter"/> rendering the number with the word.</summary>
        public void Elsewhere(int chapter, string text, string strong, string part)
        {
            for (var verse = 1; verse <= 10; verse++)
            {
                Add($"01{chapter:000}{verse:000}001", text, strong, part);
            }
        }

        public HashSet<int> Scrambled() => ClearBibleLinks.Scrambled(_records, _targets, _sources, Function);
    }
}
