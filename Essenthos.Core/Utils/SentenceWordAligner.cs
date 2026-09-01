using System.Buffers;

namespace Essenthos.Core.Utils;

/// <summary>
/// Aligns two ordered word lists, producing a symmetric mapping between them.
/// 
/// Algorithm overview:
///   1. Normalize all words (lowercase, trimmed) once upfront.
///   2. Find exact-match anchors via LCS (Longest Common Subsequence) with a contiguity bonus
///      that favors longer contiguous blocks � similar to how `git diff` picks the best matching hunks.
///   3. Fill gaps between anchors with a local greedy pass that handles:
///      - 1?2 and 2?1 word splits/merges (e.g. "farewall" ? "fare"+"wall")
///      - Fuzzy similarity via Levenshtein distance (e.g. "Moseus" ? "Moses")
///      - Unmatched (missing/inserted) words
///
/// Performance notes:
///   - LCS DP table uses a rented int[] from ArrayPool to avoid large heap allocations.
///   - Normalized values are computed once and stored alongside items.
///   - No LINQ in hot paths; manual loops throughout.
///
/// To modify similarity thresholds, adjust <see cref="AreSimilar"/>.
/// To change anchor selection strategy, modify <see cref="FindExactAnchors{T1,T2}"/>.
/// </summary>
public static class SentenceWordAligner
{
    /// <summary>Convenience overload for plain string lists.</summary>
    public static WordAlignmentResult<string, string> Align(IList<string> source, IList<string> target)
    {
        return Align(source, target, static s => s, static s => s);
    }

    /// <summary>
    /// Aligns <paramref name="source"/> and <paramref name="target"/> word lists using value extractors.
    /// Returns a symmetric result: every item from both lists appears exactly once in its respective alignment.
    /// </summary>
    public static WordAlignmentResult<TSource, TTarget> Align<TSource, TTarget>(
        IList<TSource> source,
        IList<TTarget> target,
        Func<TSource, string> sourceValueGetter,
        Func<TTarget, string> targetValueGetter)
    {
        var sourceWords = BuildWordList(source, sourceValueGetter);
        var targetWords = BuildWordList(target, targetValueGetter);

        var anchors = FindExactAnchors(sourceWords, targetWords);

        var sourceAlignment = new List<WordMapping<TSource, TTarget>>(sourceWords.Length);
        var targetAlignment = new List<WordMapping<TTarget, TSource>>(targetWords.Length);

        int srcCursor = 0, tgtCursor = 0;
        for (int a = 0; a < anchors.Count; a++)
        {
            var (anchorSrc, anchorTgt) = anchors[a];

            AlignGap(sourceWords, targetWords, srcCursor, anchorSrc, tgtCursor, anchorTgt, sourceAlignment, targetAlignment);

            EmitOneToOne(sourceAlignment, targetAlignment, sourceWords[anchorSrc], targetWords[anchorTgt], WordMatchType.Exact);

            srcCursor = anchorSrc + 1;
            tgtCursor = anchorTgt + 1;
        }

        AlignGap(sourceWords, targetWords, srcCursor, sourceWords.Length, tgtCursor, targetWords.Length, sourceAlignment, targetAlignment);

        return new WordAlignmentResult<TSource, TTarget>(sourceAlignment, targetAlignment);
    }

    #region Word list preparation

    private static NormalizedWord<T>[] BuildWordList<T>(IList<T> items, Func<T, string> valueGetter)
    {
        var result = new NormalizedWord<T>[items.Count];
        for (int i = 0; i < items.Count; i++)
        {
            var raw = valueGetter(items[i]);
            result[i] = new NormalizedWord<T>(items[i], i, Normalize(raw));
        }
        return result;
    }

    #endregion

    #region LCS anchor detection

    /// <summary>
    /// Finds the optimal set of exact-match anchors using LCS with a contiguity bonus.
    /// Scoring: +100 per match, +1 bonus when the match continues a contiguous run.
    /// This ensures longer contiguous blocks are preferred over scattered matches.
    /// Uses ArrayPool to avoid allocating large arrays on the heap.
    /// </summary>
    private static List<(int srcIdx, int tgtIdx)> FindExactAnchors<TSource, TTarget>(
        NormalizedWord<TSource>[] source,
        NormalizedWord<TTarget>[] target)
    {
        int n = source.Length;
        int m = target.Length;
        int dpSize = (n + 1) * (m + 1);

        var dpArray = ArrayPool<int>.Shared.Rent(dpSize);
        try
        {
            Array.Clear(dpArray, 0, dpSize);

            // Fill DP table (row-major: dp[i,j] = dpArray[i * (m+1) + j])
            int stride = m + 1;
            for (int i = 1; i <= n; i++)
            {
                var srcNorm = source[i - 1].Normalized;
                int rowOffset = i * stride;
                int prevRowOffset = (i - 1) * stride;

                for (int j = 1; j <= m; j++)
                {
                    if (string.Equals(srcNorm, target[j - 1].Normalized, StringComparison.Ordinal))
                    {
                        int bonus = (i > 1 && j > 1 &&
                                     string.Equals(source[i - 2].Normalized, target[j - 2].Normalized, StringComparison.Ordinal))
                            ? 1 : 0;
                        dpArray[rowOffset + j] = dpArray[prevRowOffset + j - 1] + 100 + bonus;
                    }
                    else
                    {
                        int fromAbove = dpArray[prevRowOffset + j];
                        int fromLeft = dpArray[rowOffset + j - 1];
                        dpArray[rowOffset + j] = fromAbove >= fromLeft ? fromAbove : fromLeft;
                    }
                }
            }

            // Backtrack to extract anchors
            var result = new List<(int, int)>(Math.Min(n, m));
            int ci = n, cj = m;
            while (ci > 0 && cj > 0)
            {
                int idx = ci * stride + cj;
                var srcNorm = source[ci - 1].Normalized;
                var tgtNorm = target[cj - 1].Normalized;

                if (string.Equals(srcNorm, tgtNorm, StringComparison.Ordinal))
                {
                    int bonus = (ci > 1 && cj > 1 &&
                                 string.Equals(source[ci - 2].Normalized, target[cj - 2].Normalized, StringComparison.Ordinal))
                        ? 1 : 0;
                    if (dpArray[idx] == dpArray[(ci - 1) * stride + cj - 1] + 100 + bonus)
                    {
                        result.Add((ci - 1, cj - 1));
                        ci--; cj--;
                        continue;
                    }
                }

                if (dpArray[(ci - 1) * stride + cj] >= dpArray[ci * stride + cj - 1])
                    ci--;
                else
                    cj--;
            }

            result.Reverse();
            return result;
        }
        finally
        {
            ArrayPool<int>.Shared.Return(dpArray);
        }
    }

    #endregion

    #region Gap alignment (local greedy)

    /// <summary>
    /// Aligns words in the gap between two anchors using a greedy strategy:
    /// split/merge check ? similarity check ? lookahead ? mark as missing.
    /// </summary>
    private static void AlignGap<TSource, TTarget>(
        NormalizedWord<TSource>[] source, NormalizedWord<TTarget>[] target,
        int srcStart, int srcEnd, int tgtStart, int tgtEnd,
        List<WordMapping<TSource, TTarget>> sourceAlignment,
        List<WordMapping<TTarget, TSource>> targetAlignment)
    {
        int si = srcStart, ti = tgtStart;

        while (si < srcEnd && ti < tgtEnd)
        {
            var sw = source[si];
            var tw = target[ti];

            // 1?2 split: source word equals concatenation of two target words
            if (ti + 1 < tgtEnd &&
                string.Equals(sw.Normalized, string.Concat(tw.Normalized, target[ti + 1].Normalized), StringComparison.Ordinal))
            {
                EmitOneToMany(sourceAlignment, targetAlignment, sw, source, target, ti, 2, WordMatchType.Substring);
                si++; ti += 2;
                continue;
            }

            // 2?1 merge: two source words concatenate to one target word
            if (si + 1 < srcEnd &&
                string.Equals(string.Concat(sw.Normalized, source[si + 1].Normalized), tw.Normalized, StringComparison.Ordinal))
            {
                EmitManyToOne(sourceAlignment, targetAlignment, source, si, 2, tw, target, WordMatchType.Substring);
                si += 2; ti++;
                continue;
            }

            // Direct similarity
            if (AreSimilar(sw.Normalized, tw.Normalized))
            {
                EmitOneToOne(sourceAlignment, targetAlignment, sw, tw, WordMatchType.Similar);
                si++; ti++;
                continue;
            }

            // Lookahead: skip a few words on either side to find a similar match
            bool found = false;
            const int maxLookahead = 5;
            for (int d = 1; d < maxLookahead; d++)
            {
                if (ti + d < tgtEnd && AreSimilar(sw.Normalized, target[ti + d].Normalized))
                {
                    for (int k = ti; k < ti + d; k++)
                        EmitMissing(targetAlignment, target[k]);
                    EmitOneToOne(sourceAlignment, targetAlignment, sw, target[ti + d], WordMatchType.Similar);
                    si++; ti += d + 1;
                    found = true;
                    break;
                }
                if (si + d < srcEnd && AreSimilar(source[si + d].Normalized, tw.Normalized))
                {
                    for (int k = si; k < si + d; k++)
                        EmitMissing(sourceAlignment, source[k]);
                    EmitOneToOne(sourceAlignment, targetAlignment, source[si + d], tw, WordMatchType.Similar);
                    si += d + 1; ti++;
                    found = true;
                    break;
                }
            }
            if (found) continue;

            // Default: mark the word from the side with more remaining items as missing
            if ((srcEnd - si) >= (tgtEnd - ti))
            {
                EmitMissing(sourceAlignment, sw);
                si++;
            }
            else
            {
                EmitMissing(targetAlignment, tw);
                ti++;
            }
        }

        // Drain remaining unmatched words
        for (; si < srcEnd; si++)
            EmitMissing(sourceAlignment, source[si]);
        for (; ti < tgtEnd; ti++)
            EmitMissing(targetAlignment, target[ti]);
    }

    #endregion

    #region Emit helpers (allocation-conscious)

    private static void EmitOneToOne<T1, T2>(
        List<WordMapping<T1, T2>> alignment1,
        List<WordMapping<T2, T1>> alignment2,
        NormalizedWord<T1> w1, NormalizedWord<T2> w2, WordMatchType type)
    {
        alignment1.Add(new WordMapping<T1, T2>(
            new IndexedWord<T1>(w1.Item, w1.Index),
            [new IndexedWord<T2>(w2.Item, w2.Index)],
            type));
        alignment2.Add(new WordMapping<T2, T1>(
            new IndexedWord<T2>(w2.Item, w2.Index),
            [new IndexedWord<T1>(w1.Item, w1.Index)],
            type));
    }

    private static void EmitOneToMany<TSource, TTarget>(
        List<WordMapping<TSource, TTarget>> sourceAlignment,
        List<WordMapping<TTarget, TSource>> targetAlignment,
        NormalizedWord<TSource> single,
        NormalizedWord<TSource>[] sourceArr,
        NormalizedWord<TTarget>[] targetArr,
        int tgtStart, int tgtCount, WordMatchType type)
    {
        var mapped = new IndexedWord<TTarget>[tgtCount];
        for (int i = 0; i < tgtCount; i++)
            mapped[i] = new IndexedWord<TTarget>(targetArr[tgtStart + i].Item, targetArr[tgtStart + i].Index);

        sourceAlignment.Add(new WordMapping<TSource, TTarget>(
            new IndexedWord<TSource>(single.Item, single.Index), mapped, type));

        var backRef = new IndexedWord<TSource>[] { new(single.Item, single.Index) };
        for (int i = 0; i < tgtCount; i++)
        {
            var tw = targetArr[tgtStart + i];
            targetAlignment.Add(new WordMapping<TTarget, TSource>(
                new IndexedWord<TTarget>(tw.Item, tw.Index), backRef, type));
        }
    }

    private static void EmitManyToOne<TSource, TTarget>(
        List<WordMapping<TSource, TTarget>> sourceAlignment,
        List<WordMapping<TTarget, TSource>> targetAlignment,
        NormalizedWord<TSource>[] sourceArr, int srcStart, int srcCount,
        NormalizedWord<TTarget> single,
        NormalizedWord<TTarget>[] targetArr, WordMatchType type)
    {
        var backRef = new IndexedWord<TTarget>[] { new(single.Item, single.Index) };
        for (int i = 0; i < srcCount; i++)
        {
            var sw = sourceArr[srcStart + i];
            sourceAlignment.Add(new WordMapping<TSource, TTarget>(
                new IndexedWord<TSource>(sw.Item, sw.Index), backRef, type));
        }

        var mapped = new IndexedWord<TSource>[srcCount];
        for (int i = 0; i < srcCount; i++)
            mapped[i] = new IndexedWord<TSource>(sourceArr[srcStart + i].Item, sourceArr[srcStart + i].Index);

        targetAlignment.Add(new WordMapping<TTarget, TSource>(
            new IndexedWord<TTarget>(single.Item, single.Index), mapped, type));
    }

    private static void EmitMissing<T1, T2>(List<WordMapping<T1, T2>> alignment, NormalizedWord<T1> word)
    {
        alignment.Add(new WordMapping<T1, T2>(
            new IndexedWord<T1>(word.Item, word.Index),
            Array.Empty<IndexedWord<T2>>(),
            WordMatchType.Missing));
    }

    #endregion

    #region String utilities

    private static string Normalize(string value) => value.Trim().ToLowerInvariant();

    /// <summary>
    /// Determines whether two normalized strings are similar enough to be considered a match.
    /// Rules:
    ///   - Single-char tokens must be exact.
    ///   - Normalized Levenshtein distance = 50% ? similar.
    ///   - Single edit on words = 5 chars ? similar (covers minor typos).
    /// </summary>
    private static bool AreSimilar(string a, string b)
    {
        int maxLen = a.Length > b.Length ? a.Length : b.Length;
        if (maxLen == 0) return true;
        if (maxLen <= 1) return string.Equals(a, b, StringComparison.Ordinal);

        int dist = SimilarityUtils.GetLevenshteinDistance(a, b);
        if ((double)dist / maxLen <= 0.5) return true;
        return dist <= 1 && maxLen >= 5;
    }

    #endregion

    #region Internal types

    private readonly record struct NormalizedWord<T>(T Item, int Index, string Normalized);

    #endregion

    #region Public API types

    /// <summary>A word with its original index in the source list.</summary>
    public record IndexedWord<T>(T Item, int Index);

    /// <summary>
    /// A single word's alignment: the word itself, the words it maps to on the other side, and the match type.
    /// <see cref="MappedWords"/> is empty when <see cref="Type"/> is <see cref="WordMatchType.Missing"/>.
    /// </summary>
    public record WordMapping<T1, T2>(IndexedWord<T1> Word, IndexedWord<T2>[] MappedWords, WordMatchType Type);

    /// <summary>
    /// Symmetric alignment result. Every word from both input lists appears exactly once
    /// in its respective alignment list, preserving original order.
    /// </summary>
    public record WordAlignmentResult<TSource, TTarget>(
        IList<WordMapping<TSource, TTarget>> SourceAlignment,
        IList<WordMapping<TTarget, TSource>> TargetAlignment
    );

    /// <summary>How a word was matched to the other side.</summary>
    public enum WordMatchType
    {
        /// <summary>Exact case-insensitive match.</summary>
        Exact,
        /// <summary>Word split/merge (e.g. "farewall" ? "fare"+"wall").</summary>
        Substring,
        /// <summary>Fuzzy match via Levenshtein distance.</summary>
        Similar,
        /// <summary>No counterpart on the other side (inserted or deleted).</summary>
        Missing
    }

    #endregion
}
