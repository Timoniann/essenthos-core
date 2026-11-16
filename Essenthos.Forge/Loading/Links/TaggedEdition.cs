using System.Text;

namespace Essenthos.Core.Loading.Links;

/// <param name="TaggedFrom">First word of the span in the tagged edition, counting from zero.</param>
/// <param name="TaggedTo">One past its last word.</param>
/// <param name="CorpusFrom">First word of the span in the loaded edition.</param>
/// <param name="CorpusTo">One past its last word.</param>
/// <param name="Match">What joined the two sides, which is also how strongly they are joined.</param>
internal readonly record struct EditionSpan(
    int TaggedFrom,
    int TaggedTo,
    int CorpusFrom,
    int CorpusTo,
    EditionMatch Match);

/// <summary>The three ways one edition's words can be put against another's, in order of strength.</summary>
internal enum EditionMatch
{
    /// <summary>One word each, written the same way.</summary>
    Identical,

    /// <summary>The same letters, divided into a different number of words.</summary>
    Divided,

    /// <summary>One word each, written differently — Booz for Boaz, leathern for leather.</summary>
    Spelling,
}

/// <summary>
/// Lays one digitisation of a text against another, where the two divide the same letters
/// differently and neither is a different text.
///
/// The Strong-tagged King James is the case it was written for. That edition writes the possessive
/// as a word of its own — <em>child</em> then <em>'s</em> — where the loaded one writes
/// <em>child's</em>; it writes <em>forever</em>, <em>today</em> and <em>tomorrow</em> where the
/// loaded one writes <em>for ever</em>, <em>to day</em> and <em>to morrow</em>. Measured over the
/// New Testament, 436 verses differ in word count for reasons of this kind and no other, and
/// demanding the counts agree threw all 436 away whole — 10,605 English words, a fifth of everything
/// the King James New Testament failed to reach, including the doxology of the Lord's Prayer.
///
/// The second case is a third party's tokenisation of a text this corpus holds:
/// <see cref="ClearBibleLinks"/> places their word ids by what each token is written with
/// rather than by where it stands, because their tokeniser makes a word of punctuation and the
/// Westminster morphology divides the Hebrew into 469,476 morphemes where BHSA divides it into
/// 426,590 words. The question is the same one — <em>which of our words is this word of theirs</em>
/// — so it is the same answer.
///
/// A span is admitted on evidence, in one of three shapes. Two words spelled the same are one span
/// and the strongest kind. A run where the two sides write the same letters and divide them
/// differently is one span over the whole run, because the letters agreeing is checkable and the
/// division is then only typography. One word against one word, spelled differently, is a span
/// because that is a spelling — <em>Booz</em> for <em>Boaz</em>, <em>leathern</em> for
/// <em>leather</em> — and it is the weakest of the three, so it does not count towards a verse
/// being the same verse. Anything else is left out: where
/// several words on one side stand against several different words on the other, nothing here can
/// say which renders which, and saying nothing is the answer.
/// </summary>
internal static class TaggedEdition
{
    /// <summary>
    /// The words of the two editions in step, as far as they can be put in step. Words neither side
    /// can be joined to appear in no span, and the caller sees them as untagged.
    /// </summary>
    /// <param name="spellings">
    /// A run that fits none of the three shapes is put in step word by word where it divides into pieces whose letters are
    /// the same or differ by one letter of a name's spelling: <em>Cesar s</em> against <em>Caesar's</em>, <em>wellbeloved
    /// Epenetus</em> against <em>well-beloved Epaenetus</em> (<see cref="Spelled"/>).
    /// </param>
    public static List<EditionSpan> Align(IReadOnlyList<string> tagged, IReadOnlyList<string> corpus, bool spellings = false)
    {
        if (SameWordForWord(tagged, corpus))
        {
            var identical = new List<EditionSpan>(corpus.Count);
            for (var i = 0; i < corpus.Count; i++)
            {
                identical.Add(new EditionSpan(i, i + 1, i, i + 1, EditionMatch.Identical));
            }

            return identical;
        }

        var spans = new List<EditionSpan>(corpus.Count);
        var common = Common(tagged, corpus);
        var at = 0;
        var to = 0;

        foreach (var (i, j) in common)
        {
            Divided(spans, tagged, corpus, at, i, to, j, spellings);
            spans.Add(new EditionSpan(i, i + 1, j, j + 1, EditionMatch.Identical));
            at = i + 1;
            to = j + 1;
        }

        Divided(spans, tagged, corpus, at, tagged.Count, to, corpus.Count, spellings);
        return spans;
    }

    /// <summary>
    /// How much of the loaded verse the two editions demonstrably share — the words they write the
    /// same way, and the words whose letters they write the same way and divide differently.
    ///
    /// A spelling is deliberately not counted. Two editions of the King James agreeing on the
    /// letters of <em>for ever</em> is as good as agreeing on the word; one writing <em>Booz</em>
    /// where the other writes <em>Boaz</em> is a guess that they are the same word, and a verse
    /// held together by guesses is the verse this measure exists to refuse.
    /// </summary>
    public static double Agreement(IReadOnlyList<EditionSpan> spans, int corpusWords) =>
        corpusWords == 0
            ? 1
            : (double)spans
                .Where(span => span.Match != EditionMatch.Spelling)
                .Sum(span => span.CorpusTo - span.CorpusFrom) / corpusWords;

    /// <summary>
    /// A run between two words the editions agree on. It is one span where the two sides write the
    /// same letters, or where each side is a single word — and nothing at all otherwise. The
    /// letters are tried first, so that a run which is both is recorded as the stronger of the two.
    /// </summary>
    private static void Divided(
        List<EditionSpan> spans,
        IReadOnlyList<string> tagged,
        IReadOnlyList<string> corpus,
        int taggedFrom,
        int taggedTo,
        int corpusFrom,
        int corpusTo,
        bool spellings)
    {
        if (taggedFrom >= taggedTo || corpusFrom >= corpusTo)
        {
            return;
        }

        if (Letters(tagged, taggedFrom, taggedTo) == Letters(corpus, corpusFrom, corpusTo))
        {
            spans.Add(new EditionSpan(taggedFrom, taggedTo, corpusFrom, corpusTo, EditionMatch.Divided));
            return;
        }

        if (taggedTo - taggedFrom == 1 && corpusTo - corpusFrom == 1)
        {
            spans.Add(new EditionSpan(taggedFrom, taggedTo, corpusFrom, corpusTo, EditionMatch.Spelling));
            return;
        }

        if (spellings && Spelled(tagged, taggedFrom, taggedTo, corpus, corpusFrom, corpusTo) is { } pieces)
        {
            spans.AddRange(pieces);
        }
    }

    /// <summary>
    /// A run divided into pieces of one word against one or two, in order, each piece's two sides writing the same letters
    /// (<see cref="EditionMatch.Divided"/>) or, from four letters on, letters one edit apart (<see cref="EditionMatch.Spelling"/>):
    /// <em>Cesar s</em> against <em>Caesar's</em> is one piece. Null where the run divides no such way, or more than one way.
    /// </summary>
    private static List<EditionSpan>? Spelled(
        IReadOnlyList<string> tagged, int taggedFrom, int taggedTo, IReadOnlyList<string> corpus, int corpusFrom, int corpusTo)
    {
        const int Widest = 2;
        var ways = new int[taggedTo - taggedFrom + 1, corpusTo - corpusFrom + 1];
        var next = new (int Tagged, int Corpus, EditionMatch Match)[taggedTo - taggedFrom + 1, corpusTo - corpusFrom + 1];
        ways[taggedTo - taggedFrom, corpusTo - corpusFrom] = 1;
        for (var i = taggedTo - taggedFrom; i >= 0; i--)
        {
            for (var j = corpusTo - corpusFrom; j >= 0; j--)
            {
                if (i == taggedTo - taggedFrom && j == corpusTo - corpusFrom)
                {
                    continue;
                }

                for (var a = 1; a <= Widest && i + a <= taggedTo - taggedFrom; a++)
                {
                    for (var b = 1; b <= Widest && j + b <= corpusTo - corpusFrom; b++)
                    {
                        if ((a > 1 && b > 1) || ways[i + a, j + b] == 0)
                        {
                            continue;
                        }

                        var left = Letters(tagged, taggedFrom + i, taggedFrom + i + a);
                        var right = Letters(corpus, corpusFrom + j, corpusFrom + j + b);
                        EditionMatch? match = left == right && left.Length > 0
                            ? EditionMatch.Divided
                            : Math.Min(left.Length, right.Length) >= 4 && OneEditApart(left, right) ? EditionMatch.Spelling : null;
                        if (match is { } found)
                        {
                            ways[i, j] = Math.Min(2, ways[i, j] + ways[i + a, j + b]);
                            next[i, j] = (i + a, j + b, found);
                        }
                    }
                }
            }
        }

        if (ways[0, 0] != 1)
        {
            return null;
        }

        var pieces = new List<EditionSpan>();
        for (var (x, y) = (0, 0); x < taggedTo - taggedFrom || y < corpusTo - corpusFrom;)
        {
            var (nx, ny, match) = next[x, y];
            pieces.Add(new EditionSpan(taggedFrom + x, taggedFrom + nx, corpusFrom + y, corpusFrom + ny, match));
            (x, y) = (nx, ny);
        }

        return pieces;
    }

    /// <summary>Whether one letter put in, taken out or changed makes one string the other.</summary>
    private static bool OneEditApart(string left, string right)
    {
        if (Math.Abs(left.Length - right.Length) > 1 || left == right)
        {
            return false;
        }

        var (shorter, longer) = left.Length <= right.Length ? (left, right) : (right, left);
        var at = 0;
        while (at < shorter.Length && shorter[at] == longer[at])
        {
            at++;
        }

        return shorter.Length == longer.Length
            ? shorter.AsSpan(at + 1).SequenceEqual(longer.AsSpan(at + 1))
            : shorter.AsSpan(at).SequenceEqual(longer.AsSpan(at + 1));
    }

    /// <summary>
    /// For each corpus word of a span, the tagged words whose letters it shares: in a run the two editions divide
    /// differently (<em>Simon s wife s</em> against <em>Simon's wife's</em>), <em>wife's</em> is <em>wife</em> and the
    /// <em>s</em> after it, not <em>Simon</em>. A word with no letters, and every word of another kind of span, has the whole span.
    /// </summary>
    public static (int From, int To)[] Covered(EditionSpan span, IReadOnlyList<string> tagged, IReadOnlyList<string> corpus)
    {
        var covered = new (int From, int To)[span.CorpusTo - span.CorpusFrom];
        Array.Fill(covered, (span.TaggedFrom, span.TaggedTo));
        if (span.Match != EditionMatch.Divided)
        {
            return covered;
        }

        var ends = new int[span.TaggedTo - span.TaggedFrom];
        for (int i = span.TaggedFrom, end = 0; i < span.TaggedTo; i++)
        {
            end += Letters(tagged, i, i + 1).Length;
            ends[i - span.TaggedFrom] = end;
        }

        for (int j = span.CorpusFrom, start = 0; j < span.CorpusTo; j++)
        {
            var end = start + Letters(corpus, j, j + 1).Length;
            if (end > start)
            {
                int from = -1, to = -1;
                for (var i = 0; i < ends.Length; i++)
                {
                    var tagStart = i == 0 ? 0 : ends[i - 1];
                    if (tagStart < end && ends[i] > start)
                    {
                        from = from < 0 ? i : from;
                        to = i + 1;
                    }
                }

                if (from >= 0)
                {
                    covered[j - span.CorpusFrom] = (span.TaggedFrom + from, span.TaggedFrom + to);
                }
            }

            start = end;
        }

        return covered;
    }

    private static string Letters(IReadOnlyList<string> words, int from, int to)
    {
        var letters = new StringBuilder(16);
        for (var i = from; i < to; i++)
        {
            foreach (var c in words[i])
            {
                if (char.IsLetterOrDigit(c))
                {
                    letters.Append(char.ToLowerInvariant(c));
                }
            }
        }

        return letters.ToString();
    }

    private static bool SameWordForWord(IReadOnlyList<string> tagged, IReadOnlyList<string> corpus)
    {
        if (tagged.Count != corpus.Count)
        {
            return false;
        }

        for (var i = 0; i < tagged.Count; i++)
        {
            if (!string.Equals(tagged[i], corpus[i], StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// The longest run of words, in order, that both editions write. A verse is a few dozen words
    /// and only the ones that already failed the word-for-word check reach here, so the table costs
    /// nothing worth avoiding.
    /// </summary>
    private static List<(int Tagged, int Corpus)> Common(IReadOnlyList<string> tagged, IReadOnlyList<string> corpus)
    {
        var lengths = new int[tagged.Count + 1, corpus.Count + 1];
        for (var i = tagged.Count - 1; i >= 0; i--)
        {
            for (var j = corpus.Count - 1; j >= 0; j--)
            {
                lengths[i, j] = string.Equals(tagged[i], corpus[j], StringComparison.OrdinalIgnoreCase)
                    ? lengths[i + 1, j + 1] + 1
                    : Math.Max(lengths[i + 1, j], lengths[i, j + 1]);
            }
        }

        var common = new List<(int, int)>(Math.Min(tagged.Count, corpus.Count));
        for (int i = 0, j = 0; i < tagged.Count && j < corpus.Count;)
        {
            if (string.Equals(tagged[i], corpus[j], StringComparison.OrdinalIgnoreCase))
            {
                common.Add((i, j));
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

        return common;
    }
}
