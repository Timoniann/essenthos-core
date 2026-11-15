using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Loading.Frame;

namespace Essenthos.Core.Loading.Links;

/// <summary>
/// A pair of texts joined verse by verse through the frame before any word of them is.
/// </summary>
internal sealed record DeclaredVersePair(string From, string To, IReadOnlySet<int> Without)
{
    /// <summary>
    /// In a book the frame has no rules for, join only the chapters where both texts print the same
    /// verses. There the shared address is all there is to go on, and a chapter one edition divides
    /// otherwise would pair every verse after the first difference with the wrong one.
    /// </summary>
    public bool AgreeingChaptersOnly { get; init; }

    /// <summary>
    /// Books of <see cref="From"/> never joined through the frame, whatever word links the pair holds:
    /// divided as no versification scheme describes, so the frame stands them at their own numbers and
    /// a shared address says nothing. <see cref="Without"/> gives way once the pair is linked word by
    /// word; these do not, and a reading of the book (the Ge'ez verse map) joins them instead.
    /// </summary>
    public IReadOnlySet<int> Never { get; init; } = new HashSet<int>();
}

/// <summary>
/// Which verse of one text is which verse of another, by the canonical frame: two verses correspond when they stand at the
/// same canonical address, and the correspondence is transitive through the addresses they share, so the shared addresses
/// are walked as a graph and each connected component is one verse link naming a set against a set. The build draws the
/// verse links of every pair from the frames with this rule (<see cref="Mapping.VerseLinks"/>); verify reads the links
/// back by <see cref="Shapes"/>. Nothing writes a verse link into a corpus database outside the build (PRB-1597).
/// </summary>
internal static class VerseLinkRule
{
    internal const string Source = "the canonical frame: the addresses the two texts share";

    /// <summary>
    /// Pairs that are joined verse by verse before any word of them is. A text that arrives with no
    /// word link to anything — the Ethiopic, until it is aligned — would otherwise stand beside
    /// nothing, although the frame already says which of its verses stands where another text's does.
    /// </summary>
    internal static IReadOnlyList<DeclaredVersePair> DeclaredPairs =>
        [.. GeezTextSource.VersePairs, .. DeuterocanonTextSource.VersePairs, .. AlexandrinusTextSource.VersePairs,
        .. CodexTextSource.VersePairs];

    /// <summary>
    /// Every verse link with what it states and the verses it states it of, as one array: the verses
    /// of the <c>from</c> side as they are and those of the <c>to</c> side negated, each side in order.
    /// Two links of one pair with the same shape are one statement written twice.
    /// </summary>
    internal const string Shapes =
        """
        SELECT l.id, l.from_text_id, l.to_text_id, l.relation, l.method, l.confidence, l.source, l.note, m.verses
        FROM verse_link l
        JOIN (SELECT verse_link_id,
                     array_agg(CASE side WHEN 'from' THEN verse_id ELSE -verse_id END ORDER BY side, verse_id) AS verses
              FROM verse_link_verse GROUP BY verse_link_id) m ON m.verse_link_id = l.id
        """;

    /// <summary>
    /// The books a declared pair leaves out. A pair the aligner or a source has linked word by word is
    /// joined wherever those links reach, as every linked pair is, and its declaration only adds the
    /// books it names: the Synodal is declared against the King James for the books it gains, and read
    /// as the declaration alone its sixty-six were left unjoined the first time the pair was built
    /// afresh, beneath half a million word links. Its <see cref="DeclaredVersePair.Never"/> books are
    /// left out either way.
    /// </summary>
    internal static IReadOnlySet<int> Outside(DeclaredVersePair declaration, bool linked) =>
        linked ? declaration.Never : declaration.Without.Union(declaration.Never).ToHashSet();

    /// <summary>
    /// The two texts' addresses without the chapters, in books the frame has no rules for, that the
    /// two do not print alike. Everything else is kept as it was.
    /// </summary>
    /// <param name="hereCovers">
    /// The further addresses the first text's verses cover, which it prints as much as the ones they
    /// stand at: the Synodal's Prayer of Manasseh, read against the King James, stands at ten of its
    /// fifteen verses and covers the other five.
    /// </param>
    /// <param name="spared">Books never left out, which the pair is joined in whole.</param>
    internal static (Dictionary<(int, int, int), List<int>> Here, Dictionary<(int, int, int), List<int>> There)
        Agreeing(
            Dictionary<(int, int, int), List<int>> here,
            Dictionary<(int, int, int), List<int>> there,
            IReadOnlySet<(int, int, int)>? hereCovers = null,
            IReadOnlySet<(int, int, int)>? thereCovers = null,
            IReadOnlySet<int>? spared = null)
    {
        var differing = Differing(here.Keys.Concat(hereCovers ?? Empty), there.Keys.Concat(thereCovers ?? Empty), spared);

        Dictionary<(int, int, int), List<int>> Without(Dictionary<(int, int, int), List<int>> addresses) =>
            addresses
                .Where(address => !differing.Contains((address.Key.Item1, address.Key.Item2)))
                .ToDictionary(address => address.Key, address => address.Value);

        return (Without(here), Without(there));
    }

    private static readonly HashSet<(int, int, int)> Empty = [];

    /// <summary>
    /// The chapters of the first text, in books the frame has no rules for, where the two do not reach
    /// the same addresses.
    /// </summary>
    internal static HashSet<(int Book, int Chapter)> Differing(
        IEnumerable<(int, int, int)> here,
        IEnumerable<(int, int, int)> there,
        IReadOnlySet<int>? spared = null)
    {
        Dictionary<(int, int), HashSet<int>> Chapters(IEnumerable<(int, int, int)> addresses) =>
            addresses
                .Where(address => !BookCodes.Places(address.Item1) && spared?.Contains(address.Item1) != true)
                .GroupBy(address => (address.Item1, address.Item2))
                .ToDictionary(chapter => chapter.Key, chapter => chapter.Select(address => address.Item3).ToHashSet());

        var mine = Chapters(here);
        var theirs = Chapters(there);
        return mine.Keys
            .Where(chapter => !theirs.TryGetValue(chapter, out var verses) || !verses.SetEquals(mine[chapter]))
            .ToHashSet();
    }

    /// <summary>
    /// The connected components of the graph whose edges are *these two verses share a canonical
    /// address*.
    ///
    /// Union-find rather than a pairwise join, because the relation is transitive and a pairwise
    /// join would cut it: if the one text's verse 12 covers the other's 12 and 13, and its verse 13
    /// covers the other's 13 as well, all four are one statement. Taking them two at a time would
    /// write three links that each contradict the others about what corresponds to what.
    /// </summary>
    internal static List<Component> Components(
        Dictionary<(int, int, int), List<int>> here,
        Dictionary<(int, int, int), List<int>> there,
        ref int alone)
    {
        var parent = new Dictionary<long, long>();
        var side = new Dictionary<long, LinkSide>();

        // A verse is only *missing a counterpart* inside a book the other text has. Without this
        // the count is dominated by pairs that cover one testament, and reads as thirty thousand
        // faults where the truth is that the King James New Testament is not in BHSA.
        var shared = here.Keys.Select(address => address.Item1)
            .Intersect(there.Keys.Select(address => address.Item1))
            .ToHashSet();
        var books = new Dictionary<long, int>();

        long Key(int verseId, LinkSide which) => which == LinkSide.From ? verseId : -(long)verseId;

        long Find(long node)
        {
            while (parent[node] != node)
            {
                parent[node] = parent[parent[node]];
                node = parent[node];
            }

            return node;
        }

        void Add(int verseId, LinkSide which)
        {
            var key = Key(verseId, which);
            if (parent.TryAdd(key, key))
            {
                side[key] = which;
            }
        }

        void Union(long left, long right)
        {
            var a = Find(left);
            var b = Find(right);
            if (a != b)
            {
                parent[a] = b;
            }
        }

        // Every verse of both texts is a node, including the ones the other text never answers, so
        // that a verse with no counterpart is counted rather than quietly absent.
        foreach (var (address, verses) in here)
        {
            foreach (var verse in verses)
            {
                Add(verse, LinkSide.From);
                books[Key(verse, LinkSide.From)] = address.Item1;
            }
        }

        foreach (var (address, verses) in there)
        {
            foreach (var verse in verses)
            {
                Add(verse, LinkSide.To);
                books[Key(verse, LinkSide.To)] = address.Item1;
            }
        }

        foreach (var (address, mine) in here)
        {
            if (!there.TryGetValue(address, out var theirs))
            {
                continue;
            }

            // Every verse standing at this address is one statement, whichever text it belongs to.
            foreach (var verse in mine.Skip(1))
            {
                Union(Key(mine[0], LinkSide.From), Key(verse, LinkSide.From));
            }

            foreach (var verse in theirs)
            {
                Union(Key(mine[0], LinkSide.From), Key(verse, LinkSide.To));
            }
        }

        var grouped = new Dictionary<long, Component>();
        foreach (var node in parent.Keys)
        {
            var root = Find(node);
            if (!grouped.TryGetValue(root, out var component))
            {
                component = new Component([], []);
                grouped[root] = component;
            }

            (side[node] == LinkSide.From ? component.From : component.To).Add((int)Math.Abs(node));
        }

        var complete = new List<Component>(grouped.Count);
        foreach (var (root, component) in grouped)
        {
            if (component.From.Count > 0 && component.To.Count > 0)
            {
                complete.Add(component);
            }
            else if (shared.Contains(books[root]))
            {
                alone++;
            }
        }

        return complete;
    }

    internal sealed record Component(List<int> From, List<int> To);
}
