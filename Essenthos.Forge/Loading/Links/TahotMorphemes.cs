using System.Text.RegularExpressions;
using Essenthos.Core.Loading.Frame;

namespace Essenthos.Core.Loading.Links;

/// <param name="Gloss">
/// The English TAHOT prints for this morpheme where it stands, angle and square brackets and all:
/// <c>[were] from</c>, <c>&lt;to&gt; the</c>. Kept whole because it is what the source says; the
/// words inside it are pulled out separately for matching.
/// </param>
/// <param name="GlossWords">
/// The bare words of <see cref="Gloss"/>, lowered, without the brackets that mark what the
/// translator supplied. Precomputed: the matcher asks this of every morpheme standing before every
/// marked word, half a million times a load.
/// </param>
/// <param name="IsPrefix">
/// Whether the source calls this a prefix — the conjunction, the article, the prefixed prepositions
/// and the interrogative and relative particles. It is the one thing the corpus could not establish
/// for itself: the mapping file numbers a prefixed מ H4480, which is also the number of the
/// free-standing preposition מִן, so there a prefix and a word look the same.
/// </param>
/// <param name="IsConjunction">
/// The waw, in either of the two numbers TAHOT gives it — plain and consecutive. English opens a
/// clause with its conjunction and Hebrew hangs it on the verb, so this one is matched across the
/// clause rather than by adjacency.
/// </param>
internal sealed record TahotMorpheme(
    string Strong,
    string Gloss,
    IReadOnlyList<string> GlossWords,
    bool IsPrefix,
    bool IsConjunction)
{
    public bool Renders(string word)
    {
        for (var i = 0; i < GlossWords.Count; i++)
        {
            if (string.Equals(GlossWords[i], word, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}

/// <summary>
/// TAHOT, read as a morpheme layer over the Hebrew the corpus already holds.
///
/// STEPBible splits every word of the Leningrad codex into its morphemes and gives each one a
/// disambiguated Strong number and an English gloss, prefixes and suffixes included. That is the
/// thing the Open Hebrew Bible mapping cannot say: it numbers a prefixed מ H4480, the number of the
/// free-standing preposition, so <see cref="HebrewPrefixes"/> had to decide from a list of its own
/// which numbers were prefixes and which English words each could render. This states both.
///
/// <para>
/// The two are joined by position within the verse and the join is checked rather than assumed: the
/// sequences are matched on their Strong numbers and only the morphemes that line up are used, so a
/// verse the two divide differently contributes nothing instead of contributing a claim about the
/// wrong word.
/// </para>
/// </summary>
internal sealed partial class TahotSegmentation
{
    /// <summary>
    /// The tab-separated columns of a word row. The file also carries an interlinear block per
    /// verse, whose lines begin with a hash, and a long prose preamble; neither is data.
    /// </summary>
    private const int TranslationColumn = 3;

    private const int StrongColumn = 4;

    private const int MinimumColumns = 5;

    /// <summary>The first of the numbers STEPBible added for what Strong never numbered.</summary>
    private const int FirstOwnCode = 9000;

    /// <summary>
    /// Above this, a code in the source's own range is a suffix or a punctuation mark rather than a
    /// prefix: H9011 is the directional he, H9014 the maqqef, H9020 and up the pronominal suffixes.
    /// BHSA has no separate word for any of them, so they are dropped before the join and the two
    /// sequences are then the same length.
    /// </summary>
    private const int LastPrefixCode = 9010;

    /// <summary>One past the last of them, so an ordinary Strong number is never mistaken for one.</summary>
    private const int LastOwnCode = 9999;

    /// <summary>
    /// Where the two sources number the same morpheme differently. TAHOT separates the consecutive
    /// waw from the plain one and gives the prefixed preposition, article and relative their own
    /// codes; the mapping file uses one number for the waw and the free-standing word's number for
    /// the rest. Without this the join reports a difference at every conjunction in the Bible.
    /// </summary>
    private static readonly Dictionary<int, int> SameMorpheme = new()
    {
        [9001] = 9000,
        [9002] = 9000,
        [9010] = 9009,
        [9006] = 4480,
        [9007] = 834,
    };

    private static readonly int[] Conjunctions = [9001, 9002];

    private readonly Dictionary<(int Book, int Chapter, int Verse), List<TahotMorpheme>> verses;

    private TahotSegmentation(Dictionary<(int, int, int), List<TahotMorpheme>> verses) => this.verses = verses;

    public int Verses => verses.Count;

    public int Morphemes => verses.Sum(verse => verse.Value.Count);

    /// <summary>
    /// Reads every volume given. STEPBible splits the Old Testament across four files only because
    /// one would be too large for GitHub; each row addresses its own verse, so the order they are
    /// read in does not matter.
    /// </summary>
    public static TahotSegmentation Read(IEnumerable<string> paths)
    {
        var verses = new Dictionary<(int, int, int), List<TahotMorpheme>>(23_500);

        foreach (var path in paths)
        {
            foreach (var line in File.ReadLines(path))
            {
                if (line.Length == 0 || line[0] == '#')
                {
                    continue;
                }

                var columns = line.Split('\t');
                if (columns.Length < MinimumColumns)
                {
                    continue;
                }

                var reference = Reference().Match(columns[0]);
                if (!reference.Success || !BookCodes.TryGetOrdinal(reference.Groups[1].Value, out var book))
                {
                    continue;
                }

                var address = (
                    book,
                    int.Parse(reference.Groups[2].Value),
                    int.Parse(reference.Groups[3].Value));

                if (!verses.TryGetValue(address, out var morphemes))
                {
                    morphemes = new List<TahotMorpheme>(24);
                    verses[address] = morphemes;
                }

                Split(columns[StrongColumn], columns[TranslationColumn], morphemes);
            }
        }

        return new TahotSegmentation(verses);
    }

    /// <summary>
    /// The morphemes of one verse lined up with the mapping file's, keyed by the position the
    /// mapping file gives each. Positions the two do not agree on are absent rather than guessed at,
    /// and a verse TAHOT does not carry answers null.
    /// </summary>
    public IReadOnlyDictionary<int, TahotMorpheme>? Align(
        int book,
        int chapter,
        int verse,
        IReadOnlyList<HebrewEntry> hebrew)
    {
        if (!verses.TryGetValue((book, chapter, verse), out var morphemes))
        {
            return null;
        }

        var theirs = new int[morphemes.Count];
        for (var i = 0; i < morphemes.Count; i++)
        {
            theirs[i] = Comparable(morphemes[i].Strong);
        }

        var ours = new int[hebrew.Count];
        for (var i = 0; i < hebrew.Count; i++)
        {
            ours[i] = Comparable(hebrew[i].Strong);
        }

        var aligned = new Dictionary<int, TahotMorpheme>(hebrew.Count);
        if (ours.AsSpan().SequenceEqual(theirs))
        {
            for (var i = 0; i < ours.Length; i++)
            {
                aligned[hebrew[i].Position] = morphemes[i];
            }

            return aligned;
        }

        foreach (var (ourIndex, theirIndex) in Common(ours, theirs))
        {
            aligned[hebrew[ourIndex].Position] = morphemes[theirIndex];
        }

        return aligned;
    }

    /// <summary>
    /// The longest sequence of morphemes the two sources agree on, as index pairs. Two thirds of
    /// verses match outright and are answered before this is reached; the rest differ over a handful
    /// of morphemes — a divine name numbered H3068 by one and H3069 by the other, a construct one
    /// reads as two words — and everything on either side of the difference still lines up. Taking
    /// the common subsequence keeps those verses and drops the words in dispute.
    /// </summary>
    private static List<(int Ours, int Theirs)> Common(int[] ours, int[] theirs)
    {
        var lengths = new int[ours.Length + 1, theirs.Length + 1];
        for (var i = ours.Length - 1; i >= 0; i--)
        {
            for (var j = theirs.Length - 1; j >= 0; j--)
            {
                lengths[i, j] = ours[i] == theirs[j]
                    ? lengths[i + 1, j + 1] + 1
                    : Math.Max(lengths[i + 1, j], lengths[i, j + 1]);
            }
        }

        var pairs = new List<(int, int)>(Math.Min(ours.Length, theirs.Length));
        for (int i = 0, j = 0; i < ours.Length && j < theirs.Length;)
        {
            if (ours[i] == theirs[j])
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

    /// <summary>
    /// Splits one word row into its morphemes. The Strong column separates them with a slash and the
    /// gloss column does the same, so the two are read in step; where they disagree on how many there
    /// are the row is dropped, because a gloss handed to the wrong morpheme is the claim this is
    /// here to avoid.
    /// </summary>
    private static void Split(string strongs, string translation, List<TahotMorpheme> into)
    {
        // Punctuation is appended after a backslash — the maqqef, the verse end — and is not a
        // morpheme of the word.
        var codes = Before('\\', strongs).Split('/');
        var glosses = Before('\\', translation).Split('/');
        if (codes.Length != glosses.Length)
        {
            return;
        }

        for (var i = 0; i < codes.Length; i++)
        {
            var strong = codes[i].Trim().Trim('{', '}');
            if (strong.Length == 0)
            {
                continue;
            }

            var number = Number(strong);
            if (number > LastPrefixCode && number <= LastOwnCode)
            {
                // A suffix or a punctuation mark. BHSA has no word of its own for either, so a
                // morpheme list that kept them could not be lined up with the corpus at all.
                continue;
            }

            var gloss = glosses[i].Trim();
            into.Add(new TahotMorpheme(
                strong,
                gloss,
                GlossWords(gloss),
                number is > FirstOwnCode and <= LastPrefixCode,
                Conjunctions.Contains(number)));
        }
    }

    private static string Before(char separator, string value)
    {
        var at = value.IndexOf(separator);
        return at < 0 ? value : value[..at];
    }

    private static IReadOnlyList<string> GlossWords(string gloss)
    {
        if (gloss.Length == 0)
        {
            return [];
        }

        var words = new List<string>(3);
        foreach (Match word in GlossWord().Matches(gloss))
        {
            words.Add(word.Value.ToLowerInvariant());
        }

        return words;
    }

    /// <summary>
    /// The number a source wrote, without the letter STEPBible appends to separate two senses of one
    /// entry. What a morpheme <em>is</em> is read from this and never from
    /// <see cref="Comparable"/>: folding the consecutive waw onto the plain one is a statement about
    /// where two sources agree, not about what either of them said.
    /// </summary>
    private static int Number(string strong)
    {
        var digits = 0;
        var seen = false;

        foreach (var character in strong.AsSpan())
        {
            if (char.IsAsciiDigit(character))
            {
                digits = (digits * 10) + (character - '0');
                seen = true;
            }
            else if (seen)
            {
                break;
            }
        }

        return digits;
    }

    /// <summary>
    /// The number two sources can be compared on: what each wrote, with the codes they number
    /// differently folded together.
    /// </summary>
    private static int Comparable(string strong) =>
        SameMorpheme.TryGetValue(Number(strong), out var same) ? same : Number(strong);

    /// <summary>
    /// <c>Gen.1.1#01=L</c>, and its variants: a Hebrew reference in brackets where the two numberings
    /// differ, a range where one English word covers two Hebrew, and a letter saying which text the
    /// word follows.
    /// </summary>
    [GeneratedRegex(@"^([1-3]?[A-Za-z]{2,3})\.(\d+)\.(\d+)(?:\([^)]*\))?#\d+")]
    private static partial Regex Reference();

    /// <summary>
    /// A word of a gloss. Angle and square brackets mark what the translator supplied and are not
    /// part of any word: <c>[were] from</c> is <em>were</em> and <em>from</em>.
    /// </summary>
    [GeneratedRegex(@"[A-Za-z0-9]+(?:['’-][A-Za-z0-9]+)*")]
    private static partial Regex GlossWord();
}
