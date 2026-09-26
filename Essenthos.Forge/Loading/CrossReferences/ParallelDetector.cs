namespace Essenthos.Core.Loading.CrossReferences;

/// <summary>A word of an original as the detector reads it: where it stands, and its dictionary form.</summary>
internal readonly record struct LemmaToken(VerseAddress Verse, int Position, string Lemma);

/// <param name="Gram">How many dictionary forms in a row make a seed.</param>
/// <param name="Commonest">
/// A run of that many that occurs more often than this in the whole text is a formula — <em>and the
/// LORD spoke unto Moses, saying</em> — and seeds nothing.
/// </param>
/// <param name="Fewest">The fewest words two passages must share, on each side, to be parallels at all.</param>
internal sealed record ParallelSettings(int Gram, int Commonest, int Fewest)
{
    /// <summary>
    /// The Hebrew counts its prefixes as words of their own — the article, the conjunction, the
    /// prepositions — so a run of five is two or three words of the English, and fifteen shared is a
    /// sentence and more.
    /// </summary>
    public static readonly ParallelSettings Hebrew = new(5, 4, 15);

    public static readonly ParallelSettings Greek = new(4, 4, 12);
}

/// <summary>One pair of verses of a detected passage, and the words they share as positions in each.</summary>
internal sealed record ParallelVerses(VerseAddress A, VerseAddress B, IReadOnlyList<(int A, int B)> Words);

/// <summary>Two passages that tell the same thing in the same words, verse against verse.</summary>
internal sealed record ParallelPassage(IReadOnlyList<ParallelVerses> Verses)
{
    public int Shared => Verses.Sum(pair => pair.Words.Count);
}

/// <summary>
/// Finds the passages of one text that another passage of it repeats: Samuel and Kings in Chronicles,
/// the psalm David sang in 2 Samuel 22 and again as Psalm 18, Isaiah 36–39 in 2 Kings, the first
/// three gospels in one another.
///
/// <para>
/// **By dictionary form, not by letters.** Chronicles spells what Samuel spells, and the gospels
/// inflect what one another inflect, differently; the lemma is what stays. Every run of
/// <see cref="ParallelSettings.Gram"/> lemmas is indexed, and a run found in two places at most
/// <see cref="ParallelSettings.Commonest"/> times over is a seed. Seeds that follow one another on
/// both sides, near enough and in step, are one stretch of shared text; stretches that touch are one
/// passage. That is linear in the text, where aligning every passage against every other is not.
/// </para>
///
/// <para>
/// **Then word against word.** Inside each passage the two runs of lemmas are aligned exactly, as the
/// longest sequence they have in common, and that alignment is what the rows record: which word of
/// which verse matches which word of the other. A verse pair sharing fewer than
/// <see cref="FewestPerVerse"/> words is not a pair.
/// </para>
///
/// <para>
/// Two places in one chapter are never parallels of each other — Numbers 7 repeats one offering
/// twelve times, and that is a list, not a parallel.
/// </para>
/// </summary>
internal static class ParallelDetector
{
    /// <summary>How far apart, in words on either side, two seeds may be and still be one stretch.</summary>
    private const int Reach = 40;

    /// <summary>How far out of step two seeds may be — words one side adds that the other has not.</summary>
    private const int Drift = 12;

    /// <summary>The fewest words a pair of verses must share to be recorded as a pair.</summary>
    public const int FewestPerVerse = 2;

    public static List<ParallelPassage> Detect(IReadOnlyList<LemmaToken> tokens, ParallelSettings settings)
    {
        var lemmas = Intern(tokens);
        var chains = Chain(Seeds(tokens, lemmas, settings), tokens);
        var passages = new List<ParallelPassage>();
        foreach (var (a, b) in Merge(chains, tokens, settings))
        {
            var verses = Align(tokens, lemmas, a, b);
            if (verses.Count > 0)
            {
                passages.Add(new ParallelPassage(verses));
            }
        }

        return passages;
    }

    private static int[] Intern(IReadOnlyList<LemmaToken> tokens)
    {
        var ids = new Dictionary<string, int>(StringComparer.Ordinal);
        var lemmas = new int[tokens.Count];
        for (var i = 0; i < tokens.Count; i++)
        {
            if (!ids.TryGetValue(tokens[i].Lemma, out var id))
            {
                id = ids.Count;
                ids[tokens[i].Lemma] = id;
            }

            lemmas[i] = id;
        }

        return lemmas;
    }

    /// <summary>Every pair of places, in two chapters of one book or two books, that begin the same uncommon run.</summary>
    private static List<(int A, int B)> Seeds(IReadOnlyList<LemmaToken> tokens, int[] lemmas, ParallelSettings settings)
    {
        var index = new Dictionary<Gram, List<int>>();
        for (var i = 0; i + settings.Gram <= tokens.Count; i++)
        {
            if (tokens[i].Verse.Book != tokens[i + settings.Gram - 1].Verse.Book)
            {
                continue;
            }

            var gram = new Gram(lemmas, i, settings.Gram);
            if (!index.TryGetValue(gram, out var at))
            {
                at = [];
                index[gram] = at;
            }

            at.Add(i);
        }

        var seeds = new List<(int, int)>();
        foreach (var at in index.Values)
        {
            if (at.Count < 2 || at.Count > settings.Commonest)
            {
                continue;
            }

            for (var x = 0; x < at.Count; x++)
            {
                for (var y = x + 1; y < at.Count; y++)
                {
                    var (a, b) = (tokens[at[x]].Verse, tokens[at[y]].Verse);
                    if (a.Book != b.Book || a.Chapter != b.Chapter)
                    {
                        seeds.Add((at[x], at[y]));
                    }
                }
            }
        }

        seeds.Sort();
        return seeds;
    }

    /// <summary>Seeds that follow one another on both sides, in step, as stretches of shared text.</summary>
    private static List<List<(int A, int B)>> Chain(List<(int A, int B)> seeds, IReadOnlyList<LemmaToken> tokens)
    {
        var chains = new List<List<(int A, int B)>>();
        var open = new List<int>();
        foreach (var (a, b) in seeds)
        {
            open.RemoveAll(chain => a - chains[chain][^1].A > Reach);
            var joined = -1;
            for (var k = open.Count - 1; k >= 0; k--)
            {
                var (lastA, lastB) = chains[open[k]][^1];
                var (stepA, stepB) = (a - lastA, b - lastB);
                if (stepA is > 0 and <= Reach && stepB is > 0 and <= Reach && Math.Abs(stepB - stepA) <= Drift
                    && tokens[a].Verse.Book == tokens[lastA].Verse.Book
                    && tokens[b].Verse.Book == tokens[lastB].Verse.Book)
                {
                    joined = open[k];
                    break;
                }
            }

            if (joined < 0)
            {
                chains.Add([(a, b)]);
                open.Add(chains.Count - 1);
            }
            else
            {
                chains[joined].Add((a, b));
            }
        }

        return chains;
    }

    /// <summary>
    /// The stretches long enough to count, joined where they touch on both sides into passages, as
    /// the span of words each passage covers on each side.
    /// </summary>
    private static List<(Span A, Span B)> Merge(
        List<List<(int A, int B)>> chains,
        IReadOnlyList<LemmaToken> tokens,
        ParallelSettings settings)
    {
        var stretches = new List<(Span A, Span B)>();
        foreach (var chain in chains)
        {
            var a = new HashSet<int>();
            var b = new HashSet<int>();
            foreach (var (seedA, seedB) in chain)
            {
                for (var k = 0; k < settings.Gram; k++)
                {
                    a.Add(seedA + k);
                    b.Add(seedB + k);
                }
            }

            if (Math.Min(a.Count, b.Count) >= settings.Fewest)
            {
                stretches.Add((new Span(a.Min(), a.Max()), new Span(b.Min(), b.Max())));
            }
        }

        stretches.Sort((x, y) => x.A.First.CompareTo(y.A.First));
        var merged = new List<(Span A, Span B)>();
        foreach (var stretch in stretches)
        {
            var into = merged.FindIndex(m =>
                m.A.Touches(stretch.A, Reach) && m.B.Touches(stretch.B, Reach)
                && tokens[m.A.First].Verse.Book == tokens[stretch.A.First].Verse.Book
                && tokens[m.B.First].Verse.Book == tokens[stretch.B.First].Verse.Book);
            if (into < 0)
            {
                merged.Add(stretch);
            }
            else
            {
                merged[into] = (merged[into].A.With(stretch.A), merged[into].B.With(stretch.B));
            }
        }

        return [.. merged.Where(m => !m.A.Touches(m.B, 0))];
    }

    /// <summary>The longest run of lemmas the two spans have in common, grouped into the pairs of verses it joins.</summary>
    private static List<ParallelVerses> Align(IReadOnlyList<LemmaToken> tokens, int[] lemmas, Span a, Span b)
    {
        var rows = a.Last - a.First + 1;
        var columns = b.Last - b.First + 1;
        var length = new int[rows + 1, columns + 1];
        for (var i = rows - 1; i >= 0; i--)
        {
            for (var j = columns - 1; j >= 0; j--)
            {
                length[i, j] = lemmas[a.First + i] == lemmas[b.First + j]
                    ? length[i + 1, j + 1] + 1
                    : Math.Max(length[i + 1, j], length[i, j + 1]);
            }
        }

        var pairs = new Dictionary<(VerseAddress A, VerseAddress B), List<(int, int)>>();
        for (int i = 0, j = 0; i < rows && j < columns;)
        {
            if (lemmas[a.First + i] == lemmas[b.First + j])
            {
                var (left, right) = (tokens[a.First + i], tokens[b.First + j]);
                if (!pairs.TryGetValue((left.Verse, right.Verse), out var words))
                {
                    words = [];
                    pairs[(left.Verse, right.Verse)] = words;
                }

                words.Add((left.Position, right.Position));
                i++;
                j++;
            }
            else if (length[i + 1, j] >= length[i, j + 1])
            {
                i++;
            }
            else
            {
                j++;
            }
        }

        return
        [
            .. pairs
                .Where(pair => pair.Value.Count >= FewestPerVerse)
                .OrderBy(pair => pair.Key.A)
                .ThenBy(pair => pair.Key.B)
                .Select(pair => new ParallelVerses(pair.Key.A, pair.Key.B, pair.Value)),
        ];
    }

    /// <summary>A run of word indexes, both ends included.</summary>
    private readonly record struct Span(int First, int Last)
    {
        public bool Touches(Span other, int gap) => First <= other.Last + gap && other.First <= Last + gap;

        public Span With(Span other) => new(Math.Min(First, other.First), Math.Max(Last, other.Last));
    }

    /// <summary>Up to five lemmas in a row, as one key; the unused places are -1.</summary>
    private readonly record struct Gram(int A, int B, int C, int D, int E)
    {
        public const int Longest = 5;

        public Gram(int[] lemmas, int at, int length)
            : this(
                lemmas[at],
                length > 1 ? lemmas[at + 1] : -1,
                length > 2 ? lemmas[at + 2] : -1,
                length > 3 ? lemmas[at + 3] : -1,
                length > 4 ? lemmas[at + 4] : -1)
        {
            if (length > Longest)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(length), length, $"A seed is at most {Longest} lemmas; the settings ask for more.");
            }
        }
    }
}
