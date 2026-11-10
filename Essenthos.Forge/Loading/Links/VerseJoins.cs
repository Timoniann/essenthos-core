using Essenthos.Core.Mapping;
using Essenthos.Core.Products;

namespace Essenthos.Core.Loading.Links;

/// <summary>
/// Which verses of one text a word link may join to which verses of another: those standing at the
/// same canonical address, and those a verse link joins.
///
/// <para>
/// A composed answer is two hops, and the second is somebody's statement about the middle text,
/// which may cross a verse boundary where that text divides the verses its own way. Carried onto a
/// third text that divides them like the target, the crossing names the wrong verse. Clear Bible's
/// Berean joins the <em>The</em> of Philippians 1:16 to the οἱ of 1:17, the two verses standing in
/// the other order in the critical text, and every English text composed through it claimed the
/// same, across a pair of verses nothing in the frame joins.
/// </para>
/// </summary>
internal sealed class VerseJoins
{
    private readonly int[] _fromVerse;
    private readonly int[] _toVerse;
    private readonly (int, int, int)[] _fromAddress;
    private readonly (int, int, int)[] _toAddress;
    private readonly HashSet<(int From, int To)> _joined;

    private VerseJoins(ProductText from, ProductText to, HashSet<(int, int)> joined)
    {
        (_fromVerse, _fromAddress) = Placed(from);
        (_toVerse, _toAddress) = Placed(to);
        _joined = joined;
    }

    /// <summary>Whether a word of the first text (by its place in its text) may be joined to a word of the second.</summary>
    public bool Joins(long fromWord, long toWord)
    {
        if (fromWord < 0 || fromWord >= _fromVerse.Length || toWord < 0 || toWord >= _toVerse.Length)
        {
            return false;
        }

        var (from, to) = (_fromVerse[fromWord], _toVerse[toWord]);
        return _joined.Contains((from, to)) || _fromAddress[from] == _toAddress[to];
    }

    /// <summary>The verse links of the pair a run reads, either way round, relative to Resources: the ones the store holds.</summary>
    public static IEnumerable<string> Inputs(ProductStore store, string fromSlug, string toSlug) =>
        new[] { MappingProducts.Pair(fromSlug, toSlug), MappingProducts.Pair(toSlug, fromSlug) }
            .Select(pair => store.Find(MappingProducts.Of(pair, VerseLinkFiles.Name))).OfType<string>();

    /// <summary>The two texts' verses joined by their primary addresses and by the pair's verse links, either way round.</summary>
    public static VerseJoins Of(ProductStore store, ProductText from, ProductText to)
    {
        var fromIndex = from.Verses.Select((verse, index) => (verse, index)).ToDictionary(v => v.verse, v => v.index);
        var toIndex = to.Verses.Select((verse, index) => (verse, index)).ToDictionary(v => v.verse, v => v.index);
        var joined = new HashSet<(int, int)>(40_000);
        foreach (var (pair, reversed) in new[] { (MappingProducts.Pair(from.Slug, to.Slug), false), (MappingProducts.Pair(to.Slug, from.Slug), true) })
        {
            var product = MappingProducts.Of(pair, VerseLinkFiles.Name);
            if (!store.Exists(product))
            {
                continue;
            }

            using var reader = store.Open(product, VerseLinkRow.Schema);
            foreach (var row in reader.Rows())
            {
                var (here, there) = reversed ? (row.To, row.From) : (row.From, row.To);
                foreach (var a in Verses(here, fromIndex))
                {
                    foreach (var b in Verses(there, toIndex))
                    {
                        joined.Add((a, b));
                    }
                }
            }
        }

        return new VerseJoins(from, to, joined);
    }

    private static IEnumerable<int> Verses(string keys, Dictionary<VerseKey, int> index) =>
        MappingText.Split(keys).Select(key => index.TryGetValue(VerseKey.Parse(key), out var at) ? at : -1).Where(at => at >= 0);

    private static (int[] Verse, (int, int, int)[] Address) Placed(ProductText text)
    {
        var verse = new int[text.Words.Count];
        var address = new (int, int, int)[text.Verses.Count];
        for (var index = 0; index < text.Verses.Count; index++)
        {
            address[index] = text.Primary(text.Verses[index]);
            foreach (var word in text.InVerse(text.Verses[index]))
            {
                verse[word] = index;
            }
        }

        return (verse, address);
    }
}
