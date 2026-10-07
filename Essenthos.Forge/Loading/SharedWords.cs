namespace Essenthos.Core.Loading;

/// <summary>
/// Which words of a verse as a corpus holds it are the same words as in the verse as the edition is
/// now read, so that a pass rewriting the verse keeps their rows and everything standing on them.
/// </summary>
internal static class SharedWords
{
    /// <summary>
    /// For each word of <paramref name="after"/>, the index of the word of <paramref name="before"/> it
    /// is, or -1 for a word the corpus does not hold: the longest run of surfaces the two share in order,
    /// taken from the end, so that of a word the old reading holds twice — once in a stretch that goes —
    /// the one kept is the one standing where the new reading has it.
    /// </summary>
    public static int[] Of(IReadOnlyList<string> before, IReadOnlyList<string> after)
    {
        var lengths = new int[before.Count + 1, after.Count + 1];
        for (var i = 1; i <= before.Count; i++)
        {
            for (var j = 1; j <= after.Count; j++)
            {
                lengths[i, j] = before[i - 1] == after[j - 1]
                    ? lengths[i - 1, j - 1] + 1
                    : Math.Max(lengths[i - 1, j], lengths[i, j - 1]);
            }
        }

        var kept = Enumerable.Repeat(-1, after.Count).ToArray();
        for (int i = before.Count, j = after.Count; i > 0 && j > 0;)
        {
            if (before[i - 1] == after[j - 1])
            {
                kept[j - 1] = i - 1;
                i--;
                j--;
            }
            else if (lengths[i - 1, j] >= lengths[i, j - 1])
            {
                i--;
            }
            else
            {
                j--;
            }
        }

        return kept;
    }
}
