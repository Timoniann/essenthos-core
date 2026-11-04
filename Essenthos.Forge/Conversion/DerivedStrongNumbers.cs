using Essenthos.Core.Loading;
using Essenthos.Core.Products;

namespace Essenthos.Core.Conversion;

/// <summary>
/// Strong numbers for an edition that prints none, taken from editions of the same family that carry them, word by word
/// within each verse (DOC-0224 §9, TSK-1459). The SBL Greek New Testament carries no numbers; Nestle 1904 and Westcott
/// and Hort do, and the three differ in a few hundred places in all, so a verse of the SBLGNT is joined to the same verse
/// of each in turn:
/// <list type="number">
/// <item>in order, by the longest common run of folded forms (<c>normalised</c>): a word takes the number of the word it is
/// paired with, first from Nestle 1904, then, for the words left, from Westcott and Hort;</item>
/// <item>a word still left whose folded form stands exactly once among the words of the same verse of Nestle 1904 that
/// nothing paired, carrying one number, takes it: the two editions print the same words in another order.</item>
/// </list>
/// A word neither pairs keeps no number. The header says where the numbers came from (<c>strong_source</c>) and how many
/// words each step numbered (<c>strong</c>). The numbers are Nestle's and Westcott and Hort's own, which are served, so the
/// SBLGNT's are served as well.
/// </summary>
internal sealed class DerivedStrongNumbers : ITextStage
{
    /// <summary>The edition each derived text takes its numbers from, then the one that fills what the first leaves.</summary>
    private static readonly Dictionary<string, string[]> From = new(StringComparer.Ordinal)
    {
        [SblgntTextSource.Slug] = [NestleTextSource.Slug, WestcottHortTextSource.Slug],
    };

    public bool AppliesTo(string slug) => From.ContainsKey(slug);

    public IReadOnlyList<string> Sources(string slug) => [];

    public IReadOnlyList<string> Products(string slug) => [.. From[slug].Select(TextWord.ProductOf)];

    /// <summary>What the stage numbered: by the in-order join with each edition, and by a form standing once in the verse.</summary>
    internal sealed record Outcome(int Words, IReadOnlyList<(string Edition, int Numbered)> InOrder, int Reordered, int Unnumbered)
    {
        public override string ToString() =>
            $"{Words} words: " + string.Join(", ", InOrder.Select(e => $"{e.Numbered} in order with {e.Edition}")) +
            $", {Reordered} by a form standing once in the verse of {InOrder[0].Edition}, {Unnumbered} without a number";
    }

    public void Apply(ConvertedText text, string resources)
    {
        var store = new ProductStore(resources);
        var editions = From[text.Slug].Select(slug => (Slug: slug, Verses: Verses(store, slug))).ToList();
        var outcome = Number(text.Verses, editions);
        text.Provenance.Add(new("strong_source", $"derived-from {editions[0].Slug}"));
        text.Provenance.Add(new("strong", $"joined word by word within each verse to {string.Join(", then ", editions.Select(e => e.Slug))}: {outcome}"));
    }

    /// <summary>Numbers the words of <paramref name="verses"/> from <paramref name="editions"/>, in their order of preference.</summary>
    internal static Outcome Number(
        IReadOnlyList<ConvertedVerse> verses,
        IReadOnlyList<(string Slug, IReadOnlyDictionary<VerseKey, List<(string? Form, string? Strong)>> Verses)> editions)
    {
        var inOrder = new int[editions.Count];
        int words = 0, reordered = 0, unnumbered = 0;
        foreach (var verse in verses)
        {
            words += verse.Words.Count;
            var taken = new Dictionary<int, bool[]>();
            for (var e = 0; e < editions.Count; e++)
            {
                if (!editions[e].Verses.TryGetValue(verse.Key, out var theirs))
                {
                    continue;
                }

                var open = Enumerable.Range(0, verse.Words.Count).Where(i => verse.Words[i].Strong is null).ToList();
                var used = taken[e] = new bool[theirs.Count];
                foreach (var (mine, their) in Common(open.Select(i => Folded(verse.Words[i])).ToList(), theirs.Select(t => t.Form).ToList()))
                {
                    used[their] = true;
                    if (theirs[their].Strong is { } strong)
                    {
                        verse.Words[open[mine]].Strong = strong;
                        inOrder[e]++;
                    }
                }
            }

            if (editions[0].Verses.TryGetValue(verse.Key, out var first) && taken.TryGetValue(0, out var paired))
            {
                var left = Enumerable.Range(0, first.Count).Where(i => !paired[i] && first[i].Strong is not null && first[i].Form is not null)
                    .GroupBy(i => first[i].Form!, StringComparer.Ordinal)
                    .Where(g => g.Count() == 1)
                    .ToDictionary(g => g.Key, g => first[g.Single()].Strong!, StringComparer.Ordinal);
                var mine = verse.Words.Where(w => w.Strong is null && Folded(w) is not null)
                    .GroupBy(w => Folded(w)!, StringComparer.Ordinal)
                    .Where(g => g.Count() == 1 && left.ContainsKey(g.Key));
                foreach (var word in mine)
                {
                    word.Single().Strong = left[word.Key];
                    reordered++;
                }
            }

            unnumbered += verse.Words.Count(w => w.Strong is null);
        }

        return new Outcome(words, [.. editions.Select((e, i) => (e.Slug, inOrder[i]))], reordered, unnumbered);
    }

    private static string? Folded(ConvertedWord word) => word.Normalised is { Length: > 0 } folded ? folded : null;

    /// <summary>The pairs of the longest common subsequence of two runs of forms, in order; a null form pairs nothing.</summary>
    internal static List<(int Mine, int Theirs)> Common(IReadOnlyList<string?> mine, IReadOnlyList<string?> theirs)
    {
        var lengths = new int[mine.Count + 1, theirs.Count + 1];
        for (var i = mine.Count - 1; i >= 0; i--)
        {
            for (var j = theirs.Count - 1; j >= 0; j--)
            {
                lengths[i, j] = mine[i] is not null && string.Equals(mine[i], theirs[j], StringComparison.Ordinal)
                    ? lengths[i + 1, j + 1] + 1
                    : Math.Max(lengths[i + 1, j], lengths[i, j + 1]);
            }
        }

        var pairs = new List<(int, int)>();
        for (int i = 0, j = 0; i < mine.Count && j < theirs.Count;)
        {
            if (mine[i] is not null && string.Equals(mine[i], theirs[j], StringComparison.Ordinal))
            {
                pairs.Add((i, j));
                i++;
                j++;
            }
            else if (lengths[i + 1, j] >= lengths[i, j + 1])
            {
                i++;
            }
            else
            {
                j++;
            }
        }

        return pairs;
    }

    private static IReadOnlyDictionary<VerseKey, List<(string? Form, string? Strong)>> Verses(ProductStore store, string slug)
    {
        var verses = new Dictionary<VerseKey, List<(string?, string?)>>();
        using var reader = store.Open(TextWord.ProductOf(slug), TextWord.Schema(slug));
        foreach (var word in reader.Rows())
        {
            if (!verses.TryGetValue(word.Key.Verse, out var list))
            {
                verses[word.Key.Verse] = list = [];
            }

            list.Add((word.Normalised is { Length: > 0 } folded ? folded : null, word.Strong));
        }

        return verses;
    }
}
