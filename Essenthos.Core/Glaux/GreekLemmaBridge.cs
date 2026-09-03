using Essenthos.Core.TextusReceptus;

namespace Essenthos.Core.Glaux;

/// <summary>
/// The same Greek word, as a classicist lemmatises it and as a New Testament edition lemmatises it.
///
/// GLAUx follows the Ancient Greek Dependency Treebank convention, which is Attic: the citation
/// form of "become" is <em>γίγνομαι</em>. Nestle 1904 follows Koine practice and writes
/// <em>γίνομαι</em>. They are one word with one Strong number, and joining the two lemma lists as
/// written silently drops it.
///
/// Every rule below was measured against the corpus rather than guessed, and together they recover
/// 6,849 Septuagint tokens — 1.19% of Brenton — that the direct join misses. The largest single
/// case is <em>γίγνομαι</em> at 2,053. Rules that recovered nothing are not here: this is a bridge
/// between two known conventions, not a general theory of Greek spelling, and a rule that fires on
/// a word it was not meant for merges two lexemes into one, which is the failure RUL-0024 is about.
///
/// Candidates are offered in order and the caller takes the first that its own lemma list knows,
/// so the unchanged lemma always wins over any rewriting of it.
/// </summary>
internal static class GreekLemmaBridge
{
    /// <summary>Attic reduplication: γιγνώσκω for γινώσκω, γίγνομαι for γίνομαι.</summary>
    private const string AtticReduplication = "γιγν";

    private const string KoineReduplication = "γιν";

    private const string ActiveEnding = "ω";

    /// <summary>Deponents cited active by one convention and middle by the other: πορεύω, ἐντέλλω, ἀποκρίνω.</summary>
    private const string MiddleEnding = "ομαι";

    /// <summary>οὕτως beside οὕτω, and the same for other adverbs that drop the sigma.</summary>
    private const string SigmaticAdverbEnding = "ωσ";

    /// <summary>
    /// The folded lemma, then the folded forms the other convention might have written it as.
    /// Folding first is what makes the comparison possible at all — GLAUx is NFD and Nestle is not.
    /// </summary>
    public static IEnumerable<string> Candidates(string lemma)
    {
        var bare = GreekLetters.Bare(lemma);
        if (bare.Length == 0)
        {
            yield break;
        }

        yield return bare;

        if (bare.StartsWith(AtticReduplication, StringComparison.Ordinal))
        {
            yield return string.Concat(KoineReduplication, bare.AsSpan(AtticReduplication.Length));
        }

        if (bare.EndsWith(SigmaticAdverbEnding, StringComparison.Ordinal))
        {
            yield return bare[..^1];
        }

        if (bare.EndsWith(MiddleEnding, StringComparison.Ordinal))
        {
            yield return string.Concat(bare.AsSpan(0, bare.Length - MiddleEnding.Length), ActiveEnding);
        }
        else if (bare.EndsWith(ActiveEnding, StringComparison.Ordinal))
        {
            yield return string.Concat(bare.AsSpan(0, bare.Length - ActiveEnding.Length), MiddleEnding);
        }
    }
}
