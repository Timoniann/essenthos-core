namespace Essenthos.Core.Loading.Links;

/// <summary>
/// The Hebrew words of a verse the mapping file divides differently from BHSA, lined up by the
/// running word number the file gives every entry rather than by the verse.
///
/// <para>
/// The file's third field is BHSA's own running word number: over all 23,021 verses whose counts
/// agree it runs without a gap, one number per BHSA word. So the words between two words a joined
/// verse placed are the words between their two numbers, whichever verse either side counts them
/// in. Where the stretch holds as many words as numbers, they pair in order, provided the glosses
/// agree as a joined verse's must; where a revision split or merged a word, only the words whose
/// glosses pair up in order are taken, and the split word is left without a number.
/// </para>
///
/// <para>
/// Held out over the joined verses, a twentieth of them at a time with their words treated as
/// unplaced, five times: 105,720 words numbered, 105,710 as the join numbers them (99.99%), and
/// 8,151 of 8,153 proper names.
/// </para>
/// </summary>
internal static class RunningWordNumbers
{
    /// <summary>A stretch longer than this is not a verse divided differently, and is left alone.</summary>
    private const int LongestStretch = 400;

    /// <param name="inTextOrder">Every word of the Hebrew text, in the order the text runs.</param>
    /// <param name="placed">Words a joined verse placed, with the running number the file gives each.</param>
    /// <param name="stated">The file's first entry at each running number: its Strong number and gloss.</param>
    public static List<(long WordId, string Strong)> Between(
        IReadOnlyList<(long Id, string? Gloss)> inTextOrder,
        IReadOnlyDictionary<long, int> placed,
        IReadOnlyDictionary<int, (string Strong, string Gloss)> stated,
        double sameVerse)
    {
        var numbered = new List<(long, string)>();
        var start = -1;
        for (var i = 0; i < inTextOrder.Count; i++)
        {
            if (!placed.ContainsKey(inTextOrder[i].Id))
            {
                start = start < 0 ? i : start;
                continue;
            }

            if (start > 0)
            {
                numbered.AddRange(Stretch(inTextOrder, start, i, placed, stated, sameVerse));
            }

            start = -1;
        }

        return numbered;
    }

    /// <summary>The unplaced words from <paramref name="from"/> up to the placed word at <paramref name="to"/>.</summary>
    private static IEnumerable<(long, string)> Stretch(
        IReadOnlyList<(long Id, string? Gloss)> inTextOrder,
        int from,
        int to,
        IReadOnlyDictionary<long, int> placed,
        IReadOnlyDictionary<int, (string Strong, string Gloss)> stated,
        double sameVerse)
    {
        var first = placed[inTextOrder[from - 1].Id] + 1;
        var last = placed[inTextOrder[to].Id] - 1;
        var words = inTextOrder.Skip(from).Take(to - from).ToList();
        var count = last - first + 1;
        if (count <= 0 || count > LongestStretch || words.Count > LongestStretch)
        {
            return [];
        }

        var entries = new List<(string Strong, string Gloss)>(count);
        for (var number = first; number <= last; number++)
        {
            if (!stated.TryGetValue(number, out var entry))
            {
                return [];
            }

            entries.Add(entry);
        }

        if (entries.Count == words.Count)
        {
            var glosses = Glosses.Agreement(
                [.. entries.Select(entry => (string?)entry.Gloss)],
                [.. words.Select(word => word.Gloss)]);
            if (glosses.Share >= sameVerse)
            {
                return words.Zip(entries, (word, entry) => (word.Id, entry.Strong));
            }
        }

        return Paired(words, entries);
    }

    /// <summary>
    /// The longest run of words and entries glossed the same way in the same order, and only the pairs
    /// on it whose glosses agree.
    /// </summary>
    private static IEnumerable<(long, string)> Paired(
        IReadOnlyList<(long Id, string? Gloss)> words,
        IReadOnlyList<(string Strong, string Gloss)> entries)
    {
        var longest = new int[words.Count + 1, entries.Count + 1];
        for (var w = words.Count - 1; w >= 0; w--)
        {
            for (var e = entries.Count - 1; e >= 0; e--)
            {
                longest[w, e] = Same(words[w], entries[e])
                    ? longest[w + 1, e + 1] + 1
                    : Math.Max(longest[w + 1, e], longest[w, e + 1]);
            }
        }

        var pairs = new List<(long, string)>();
        for (int w = 0, e = 0; w < words.Count && e < entries.Count;)
        {
            if (Same(words[w], entries[e]))
            {
                pairs.Add((words[w].Id, entries[e].Strong));
                w++;
                e++;
            }
            else if (longest[w + 1, e] >= longest[w, e + 1])
            {
                w++;
            }
            else
            {
                e++;
            }
        }

        return pairs;
    }

    private static bool Same((long Id, string? Gloss) word, (string Strong, string Gloss) entry) =>
        !string.IsNullOrWhiteSpace(word.Gloss) && !string.IsNullOrWhiteSpace(entry.Gloss)
        && Glosses.Same(entry.Gloss, word.Gloss);
}
