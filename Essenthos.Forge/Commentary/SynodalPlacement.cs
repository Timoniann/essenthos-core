using System.Text.RegularExpressions;
using Essenthos.Core.Mapping;
using Essenthos.Core.Products;

namespace Essenthos.Core.Commentary;

/// <summary>
/// Where the verses a commentary prints in the Synodal's numbering stand in the shared frame: each is found by its words
/// among the corpus's Synodal verses and stands where that verse stands. The frame places the corpus's Synodal already
/// (its Psalms, its titles, its further books), so nothing here is placed by a number or a rule of its own.
///
/// <para>
/// A printed verse is the corpus's verse at the same address when their words agree; otherwise the verse nearby, after the
/// last one found, whose words agree most (the Synodal's Psalm 50:3 is the corpus's 51:1, which runs the title and the
/// first line together). Where the book joins in one verse what the corpus prints as two, the second goes with the first.
/// A verse found nowhere is named in the tally and places nothing.
/// </para>
/// </summary>
internal sealed partial class SynodalPlacement
{
    /// <summary>How far the words of two verses must agree, as the share of the printed verse's words the corpus's holds.</summary>
    private const double Nearby = 0.5;

    /// <summary>How far they must agree for a verse found anywhere in the book, away from the last one found.</summary>
    private const double Far = 0.75;

    /// <summary>How much of a verse of the corpus a printed verse must hold for the corpus's verse to go with it.</summary>
    private const double Joined = 0.8;

    private const int Before = 40;

    private const int After = 60;

    /// <summary>
    /// Books the corpus holds apart that a book prints within it: the Synodal's Daniel 3:24-90 is the Song of the Three, its
    /// chapters 13 and 14 Susanna and Bel, which the corpus's Synodal holds as books of their own.
    /// </summary>
    private static readonly Dictionary<int, int[]> Further = new() { [27] = [93, 77, 78] };

    private readonly Dictionary<(int Book, int Chapter, int Verse), List<VerseKey>> placed = [];

    private SynodalPlacement()
    {
    }

    public int Placed => placed.Count;

    public static SynodalPlacement Place(IEnumerable<(int Book, Fb2Verse Verse)> printed, ProductText synodal, LopukhinReader.Tally tally)
    {
        var placement = new SynodalPlacement();
        var verses = synodal.Verses
            .Select(key => (Key: key, Addresses: synodal.Addresses(key), Words: Tokens(string.Join(' ', synodal.InVerse(key).Select(id => synodal.Words[id].Surface)))))
            .ToList();
        foreach (var book in printed.GroupBy(p => p.Book))
        {
            var ordinal = book.Key;
            var further = Further.GetValueOrDefault(ordinal, []);
            var candidates = verses
                .Where(v => v.Key.Book == ordinal || v.Addresses[0].Book == ordinal || further.Contains(v.Key.Book))
                .OrderBy(v => v.Addresses[0].Book == ordinal ? 0 : 1)
                .ThenBy(v => v.Addresses[0])
                .ThenBy(v => v.Key)
                .ToList();
            var byOwn = new Dictionary<(int, int), List<int>>();
            for (var at = 0; at < candidates.Count; at++)
            {
                var key = candidates[at].Key;
                if (key.Book == ordinal)
                {
                    if (!byOwn.TryGetValue((key.Chapter, key.Verse), out var list))
                    {
                        byOwn[(key.Chapter, key.Verse)] = list = [];
                    }

                    list.Add(at);
                }
            }

            var found = new List<(Fb2Verse Verse, HashSet<string> Words, List<int> Matches)>();
            var owner = new Dictionary<int, int>();
            var cursor = 0;
            foreach (var (_, verse) in book)
            {
                // The Synodal prints what it takes from the Greek in brackets; the corpus's text leaves it out.
                var text = LeadingNumber().Replace(verse.Text, string.Empty);
                var words = Tokens(Added().Replace(text, " "));
                if (words.Count < 2)
                {
                    words = Tokens(text);
                }

                // The best agreement among the verses about the place the last one found leads to and the verse at the same
                // address; the nearer to that place where two agree as much (a title the next psalm's title repeats).
                var best = -1;
                var score = 0.0;
                var near = Enumerable.Range(Math.Max(0, cursor - Before), Math.Max(0, Math.Min(candidates.Count, cursor + After) - Math.Max(0, cursor - Before)));
                foreach (var at in near.Concat(byOwn.GetValueOrDefault((verse.Chapter, verse.Verse), [])))
                {
                    var here = Rank(words, candidates[at].Words);
                    if (here > score + 1e-9 || (Math.Abs(here - score) <= 1e-9 && best >= 0 && Math.Abs(at - cursor) < Math.Abs(best - cursor)))
                    {
                        (score, best) = (here, at);
                    }
                }

                if (best >= 0 && Score(words, candidates[best].Words) < Nearby)
                {
                    best = -1;
                }

                if (best < 0)
                {
                    // Far from the last one found: the passage the corpus holds apart (Daniel's Song of the Three), or back from it.
                    score = 0.0;
                    for (var at = 0; at < candidates.Count; at++)
                    {
                        var here = Rank(words, candidates[at].Words);
                        if (here > score + 1e-9)
                        {
                            (score, best) = (here, at);
                        }
                    }

                    if (best >= 0 && (Score(words, candidates[best].Words) < Far || words.Count < 4))
                    {
                        best = -1;
                    }
                }

                if (best < 0)
                {
                    tally.Unplaced.Add($"{ordinal}.{verse.Chapter}:{verse.Verse} \"{verse.Text[..Math.Min(50, verse.Text.Length)]}\"");
                    continue;
                }

                found.Add((verse, words, [best]));
                owner.TryAdd(best, found.Count - 1);
                cursor = best + 1;
            }

            // A verse of the corpus nobody found, held by the printed verse found beside it: the book joins the two.
            for (var at = 0; at < candidates.Count; at++)
            {
                if (owner.ContainsKey(at) || candidates[at].Words.Count == 0)
                {
                    continue;
                }

                foreach (var neighbour in new[] { at - 1, at + 1 })
                {
                    if (owner.TryGetValue(neighbour, out var holder)
                        && Contained(candidates[at].Words, found[holder].Words) >= Joined)
                    {
                        found[holder].Matches.Add(at);
                        break;
                    }
                }
            }

            foreach (var (verse, _, matches) in found)
            {
                // A verse of the corpus stands at its first address, the one it is read at; the others name the same verse under
                // the passage's other name (the Song of the Three as the Greek's Daniel 3:24-90) or the further verses of an
                // edition it is set against (Esther's additions as the Latin's chapters 11-16), where the reader does not read it.
                var addresses = matches
                    .Select(at => candidates[at].Addresses[0])
                    .Select(a => new VerseKey(a.Book, a.Chapter, a.Verse))
                    .Distinct()
                    .OrderBy(a => a)
                    .ToList();
                var verses2 = addresses.Where(a => a.Verse > 0).ToList();
                placement.placed[(ordinal, verse.Chapter, verse.Verse)] = verses2.Count > 0 ? verses2 : [.. addresses.Select(a => a with { Verse = 1 })];
            }
        }

        return placement;
    }

    /// <summary>Where a printed verse stands, the frame addresses in order; null where it was not printed or not found.</summary>
    public IReadOnlyList<VerseKey>? Of(int book, int chapter, int verse) => placed.GetValueOrDefault((book, chapter, verse));

    /// <summary>
    /// The frame's book and chapter a chapter of the book stands at: its first verse's (the Synodal's Daniel 13 is Susanna's
    /// chapter 1, its Psalm 50 the frame's 51); its own where none was found.
    /// </summary>
    public (int Book, int Chapter) Chapter(int book, int chapter)
    {
        var first = placed.Where(p => p.Key.Book == book && p.Key.Chapter == chapter).OrderBy(p => p.Key.Verse).Select(p => p.Value[0]).FirstOrDefault();
        return first == default ? (book, chapter) : (first.Book, first.Chapter);
    }

    /// <summary>
    /// The frame's passage verses <paramref name="first"/> to <paramref name="last"/> of a chapter stand at: from the first
    /// address of any of them to the last. Where none was printed, the nearest printed verse of the chapter says where they
    /// stand, shifted as far as they are from it (<paramref name="inferred"/>). Null where the chapter has none.
    /// </summary>
    public (VerseKey First, VerseKey Last)? Span(int book, int chapter, int first, int last, out bool inferred)
    {
        inferred = false;
        var addresses = new List<VerseKey>();
        for (var verse = first; verse <= last; verse++)
        {
            if (Of(book, chapter, verse) is { } at)
            {
                addresses.AddRange(at);
            }
        }

        if (addresses.Count == 0)
        {
            inferred = true;
            foreach (var verse in new[] { first, last })
            {
                if (Inferred(book, chapter, verse) is { } guess)
                {
                    addresses.Add(guess);
                }
            }
        }

        if (addresses.Count == 0)
        {
            return null;
        }

        var start = addresses.Min();
        var end = addresses.Where(a => a.Book == start.Book).Max();
        return (start, end);
    }

    /// <summary>The verse a link cites, as [book, chapter, verse] in the frame.</summary>
    public int[] Reference((int Book, int Chapter, int Verse) cited)
    {
        var at = Of(cited.Book, cited.Chapter, cited.Verse)?[0] ?? Inferred(cited.Book, cited.Chapter, cited.Verse);
        return at is { } address ? [address.Book, address.Chapter, address.Verse] : [cited.Book, cited.Chapter, cited.Verse];
    }

    private VerseKey? Inferred(int book, int chapter, int verse)
    {
        var nearest = placed
            .Where(p => p.Key.Book == book && p.Key.Chapter == chapter)
            .OrderBy(p => Math.Abs(p.Key.Verse - verse))
            .Select(p => (Own: p.Key.Verse, At: p.Value[0]))
            .FirstOrDefault();
        if (nearest.At == default)
        {
            return null;
        }

        var shifted = nearest.At.Verse + verse - nearest.Own;
        return shifted >= 1 ? nearest.At with { Verse = shifted } : nearest.At;
    }

    /// <summary>
    /// How a candidate ranks: by <see cref="Score"/> alone; of two that hold the printed verse's words alike, the nearer to
    /// where the last one found leads is taken (a psalm's title holds the words of many titles; the next psalm's is the one).
    /// </summary>
    private static double Rank(HashSet<string> printed, HashSet<string> corpus) => Score(printed, corpus);

    /// <summary>
    /// The share of the printed verse's words the corpus's verse holds. Not of the shorter's: a short verse of the corpus
    /// that a printed verse quotes in full is not that verse (the Synodal's Psalm 133:1 adds "во дворах дома Бога нашего"
    /// from the Greek, which is the whole of the corpus's 135:2).
    /// </summary>
    internal static double Score(HashSet<string> printed, HashSet<string> corpus) =>
        printed.Count == 0 || corpus.Count == 0 ? 0 : (double)printed.Count(corpus.Contains) / printed.Count;

    /// <summary>The share of <paramref name="part"/>'s words <paramref name="whole"/> holds.</summary>
    private static double Contained(HashSet<string> part, HashSet<string> whole) =>
        part.Count == 0 ? 0 : (double)part.Count(whole.Contains) / part.Count;

    /// <summary>A verse's words, lower case, ё as е.</summary>
    internal static HashSet<string> Tokens(string text) =>
        [.. Word().Matches(text.ToLowerInvariant().Replace('ё', 'е')).Select(m => Cyrillic(m.Value))];

    /// <summary>A word a scan wrote with a Latin letter for a Cyrillic one that looks the same ("Xанания"), in Cyrillic.</summary>
    private static string Cyrillic(string word)
    {
        if (!word.Any(c => c is >= 'а' and <= 'я') || !word.Any(c => c is >= 'a' and <= 'z'))
        {
            return word;
        }

        const string latin = "aceopxyk";
        const string cyrillic = "асеорхук";
        return string.Concat(word.Select(c => latin.IndexOf(c) is var at and >= 0 ? cyrillic[at] : c));
    }

    [GeneratedRegex(@"\p{L}+")]
    private static partial Regex Word();

    [GeneratedRegex(@"^\s*\d+\.\s*")]
    private static partial Regex LeadingNumber();

    [GeneratedRegex(@"\([^()]*\)|\[[^\[\]]*\]")]
    private static partial Regex Added();
}
