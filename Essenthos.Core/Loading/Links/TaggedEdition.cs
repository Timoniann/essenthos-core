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
/// Lays the Strong-tagged King James against the loaded one, which are two printings of one
/// translation and divide the same letters differently.
///
/// The tagged edition writes the possessive as a word of its own — <em>child</em> then <em>'s</em>
/// — where the loaded one writes <em>child's</em>; it writes <em>forever</em>, <em>today</em> and
/// <em>tomorrow</em> where the loaded one writes <em>for ever</em>, <em>to day</em> and <em>to
/// morrow</em>. Neither is a different text. Measured over the New Testament, 436 verses differ in
/// word count for reasons of this kind and no other, and demanding the counts agree threw all 436
/// away whole — 10,605 English words, a fifth of everything the King James New Testament failed to
/// reach, including the doxology of the Lord's Prayer.
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
    public static List<EditionSpan> Align(IReadOnlyList<string> tagged, IReadOnlyList<string> corpus)
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
            Divided(spans, tagged, corpus, at, i, to, j);
            spans.Add(new EditionSpan(i, i + 1, j, j + 1, EditionMatch.Identical));
            at = i + 1;
            to = j + 1;
        }

        Divided(spans, tagged, corpus, at, tagged.Count, to, corpus.Count);
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
        int corpusTo)
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
        }
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
