using System.Buffers;
using Essenthos.Core.TextusReceptus;

namespace Essenthos.Core.MorphGnt;

/// <summary>How the parsing was matched to the word it was put on.</summary>
internal enum MorphGntMatch
{
    /// <summary>
    /// The two editions print the same letters, once breathings, accents and case are set aside.
    /// 135,960 words of Nestle 1904, and the pairing is as certain as a pairing that nobody stated
    /// can be.
    /// </summary>
    Printed,

    /// <summary>
    /// The two editions spell the same word differently, and the difference is one of the two the
    /// hundred years between them turned over: <c>ει</c> against <c>ι</c>, or a consonant written
    /// once against twice. Ἰωάνης and Ἰωάννης, Πειλᾶτος and Πιλᾶτος, Δαυείδ and Δαυίδ.
    /// </summary>
    Spelling,
}

/// <param name="Surface">The word as its own text prints it, without whatever follows it.</param>
/// <param name="Morphology">
/// What its own text says about it, in the keys <c>word.morphology</c> uses. Read only to refuse a
/// spelling-variant pairing that the two analyses contradict; the printed-form pairings are never
/// filtered by it, because those are the pairings the disagreement is measured on and a join that
/// discarded its disagreements would report perfect agreement.
/// </param>
internal sealed record JoinWord(
    int Book,
    int Chapter,
    int Verse,
    string Surface,
    IReadOnlyDictionary<string, string> Morphology);

internal sealed record MorphGntJoinRow(int Witness, int Parsing, MorphGntMatch Match);

/// <param name="Unmatched">Indices into the witness: words this parsing does not reach.</param>
/// <param name="Unused">Indices into the parsing: words the witness does not have.</param>
internal sealed record MorphGntJoinOutcome(
    IReadOnlyList<MorphGntJoinRow> Rows,
    IReadOnlyList<int> Unmatched,
    IReadOnlyList<int> Unused);

/// <summary>
/// Puts MorphGNT's parsing of an SBLGNT word onto the Nestle 1904 word that is the same word.
///
/// <para>
/// This is an alignment and not a lookup, and it has to be. MorphGNT parses the SBLGNT, which is
/// Holmes's independent eclectic text and differs from the Nestle line in more than 540 variation
/// units; the two hold 137,554 and 137,779 words and neither is a subset of the other. There is no
/// shared identifier — MorphGNT carries no Strong number, which is what every other Greek witness
/// here joins by for nothing.
/// </para>
///
/// <para>
/// What makes it tractable is that they are two printings of the same sentences, so the alignment
/// is a diff rather than a translation: within one verse, the longest common subsequence of the
/// printed forms, and nothing outside it. **A word in a gap gets no parsing.** That is the whole
/// discipline of this file — 1,375 Nestle words are left with nothing rather than given the parse
/// of whatever stood nearest, because a parsing on the wrong word is worse than no parsing, and it
/// is worse in the way this corpus most needs to avoid: it looks like scholarship.
/// </para>
///
/// <para>
/// One narrow second pass runs inside the gaps, and only where each side has exactly one word.
/// Nestle 1904 prints Ἰωάνης, Πειλᾶτος, Δαυείδ, Ἡλείας and Ῥαββεί where the SBLGNT prints Ἰωάννης,
/// Πιλᾶτος, Δαυίδ, Ἠλίας and Ῥαββί — the same names, spelled as a 1904 editor spelled them, and
/// they are refused by a letter-for-letter comparison. Folding <c>ει</c> to <c>ι</c> and a doubled
/// consonant to a single one recovers 444 of them. It recovers proper names almost exclusively,
/// which is not a coincidence: an indeclinable transliterated name is where two editors have
/// nothing but convention to go on.
/// </para>
///
/// <para>
/// That fold is deliberately narrower than itacism. Folding <c>η</c> and <c>υ</c> to <c>ι</c> as
/// well would also pair ἡμᾶς with ὑμᾶς and ἔχωμεν with ἔχομεν — *us* with *you*, and a subjunctive
/// with an indicative — which are the two editions disagreeing about the text, not about how to
/// spell it, and would put a first-person parse on a second-person word six times over. The
/// remaining pass is the guard below: a spelling pairing whose two analyses contradict each other
/// on a feature both state is refused, which is what stops διέλειπεν being read as διέλιπεν and
/// καταλειπόντες as καταλιπόντες, the two places where the narrow fold still crosses a tense.
/// </para>
/// </summary>
internal static class MorphGntJoin
{
    /// <summary>
    /// The features compared to decide whether two differently spelled words are the same word.
    /// Nestle's own keys; MorphGNT is read into the same vocabulary by <see cref="MorphGntParsing"/>
    /// so that no translation table stands between them.
    /// </summary>
    private static readonly (string Key, Func<string, string?> Read)[] Features =
    [
        ("case", MorphGntParsing.Case),
        ("number", MorphGntParsing.Number),
        ("gender", MorphGntParsing.Gender),
        ("tense", MorphGntParsing.Tense),
        ("voice", MorphGntParsing.Voice),
        ("mood", MorphGntParsing.Mood),
        ("person", MorphGntParsing.Person),
    ];

    /// <summary>
    /// Nestle writes this where a form could be middle or passive and it declines to choose.
    /// MorphGNT always chooses, so the two are not in contradiction here and this must not be read
    /// as one — it is the single largest thing this dataset adds.
    /// </summary>
    private const string Undecided = "middlepassive";

    /// <summary>
    /// Both lists in document order. The witness is grouped by its own verse addresses and the
    /// parsing by the addresses it writes, so a verse one side does not have contributes its words
    /// to the unmatched list and nothing else.
    /// </summary>
    public static MorphGntJoinOutcome Of(
        IReadOnlyList<JoinWord> witness,
        IReadOnlyList<MorphGntWord> parsing)
    {
        var rows = new List<MorphGntJoinRow>(witness.Count);
        var unmatched = new List<int>();
        var unused = new List<int>();

        var witnessVerses = Runs(witness.Count, at => (witness[at].Book, witness[at].Chapter, witness[at].Verse));
        var parsingVerses = Runs(parsing.Count, at => (parsing[at].Book, parsing[at].Chapter, parsing[at].Verse));

        foreach (var address in witnessVerses.Keys.Union(parsingVerses.Keys))
        {
            var here = witnessVerses.GetValueOrDefault(address, []);
            var there = parsingVerses.GetValueOrDefault(address, []);

            if (here.Count == 0 || there.Count == 0)
            {
                unmatched.AddRange(here);
                unused.AddRange(there);
                continue;
            }

            Verse(witness, parsing, here, there, rows, unmatched, unused);
        }

        unmatched.Sort();
        unused.Sort();
        return new MorphGntJoinOutcome(rows, unmatched, unused);
    }

    private static void Verse(
        IReadOnlyList<JoinWord> witness,
        IReadOnlyList<MorphGntWord> parsing,
        List<int> here,
        List<int> there,
        List<MorphGntJoinRow> rows,
        List<int> unmatched,
        List<int> unused)
    {
        var left = here.Select(at => Bare(witness[at].Surface)).ToArray();
        var right = there.Select(at => Bare(parsing[at].Word)).ToArray();
        var anchors = LongestCommonSubsequence(left, right);

        var previousLeft = -1;
        var previousRight = -1;

        foreach (var (i, j) in anchors.Append((left.Length, right.Length)))
        {
            Gap(witness, parsing, here, there, left, right,
                previousLeft + 1, i, previousRight + 1, j, rows, unmatched, unused);

            if (i < left.Length)
            {
                rows.Add(new MorphGntJoinRow(here[i], there[j], MorphGntMatch.Printed));
            }

            previousLeft = i;
            previousRight = j;
        }
    }

    /// <summary>
    /// What stands between two anchors. Only the one-against-one case can be paired at all; a gap
    /// where either side has two words or none is two editions reading differently, and nothing
    /// there is joined.
    /// </summary>
    private static void Gap(
        IReadOnlyList<JoinWord> witness,
        IReadOnlyList<MorphGntWord> parsing,
        List<int> here,
        List<int> there,
        string[] left,
        string[] right,
        int leftFrom,
        int leftTo,
        int rightFrom,
        int rightTo,
        List<MorphGntJoinRow> rows,
        List<int> unmatched,
        List<int> unused)
    {
        if (leftTo - leftFrom == 1
            && rightTo - rightFrom == 1
            && MorphGntSpelling.Folded(left[leftFrom]) == MorphGntSpelling.Folded(right[rightFrom])
            && !Contradict(witness[here[leftFrom]], parsing[there[rightFrom]]))
        {
            rows.Add(new MorphGntJoinRow(here[leftFrom], there[rightFrom], MorphGntMatch.Spelling));
            return;
        }

        for (var at = leftFrom; at < leftTo; at++)
        {
            unmatched.Add(here[at]);
        }

        for (var at = rightFrom; at < rightTo; at++)
        {
            unused.Add(there[at]);
        }
    }

    /// <summary>
    /// Whether the two analyses rule out these being the same word. Only a feature both of them
    /// state can contradict — a feature one has and the other lacks is the ordinary case and the
    /// reason the second morphology is worth holding.
    /// </summary>
    private static bool Contradict(JoinWord word, MorphGntWord parsing)
    {
        foreach (var (key, read) in Features)
        {
            if (!word.Morphology.TryGetValue(key, out var stated) || stated == Undecided)
            {
                continue;
            }

            if (read(parsing.Parse) is { } other && other != stated)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// The letters of a word and nothing else.
    ///
    /// The corpus's own fold deliberately passes a character that is not Greek straight through,
    /// because none of them is improved by being guessed at — and Nestle 1904 attaches an editor's
    /// bracket to the word itself, so its Mark 16:9 begins <c>[[Ἀναστὰς</c> and its John 1:38 holds
    /// <c>(ὃ</c>. MorphGNT publishes a column with the punctuation already stripped. Comparing the
    /// two folds as they stand refused 32 words over brackets and dashes, and refused their
    /// neighbours with them by breaking the gap those neighbours sat in.
    /// </summary>
    private static string Bare(string surface)
    {
        var folded = GreekLetters.Bare(surface);
        return folded.All(char.IsLetter) ? folded : new string([.. folded.Where(char.IsLetter)]);
    }

    /// <summary>
    /// The indices of each verse's words, by address. Both sides are in document order and stay
    /// that way, so a run is the verse and the order inside it is the order on the page.
    /// </summary>
    private static Dictionary<(int, int, int), List<int>> Runs(
        int count,
        Func<int, (int, int, int)> address)
    {
        var runs = new Dictionary<(int, int, int), List<int>>(8_000);
        for (var at = 0; at < count; at++)
        {
            var key = address(at);
            if (!runs.TryGetValue(key, out var run))
            {
                runs[key] = run = new List<int>(24);
            }

            run.Add(at);
        }

        return runs;
    }

    /// <summary>
    /// The classic table, over a verse rather than a text: the longest run of words the two
    /// editions have in common, in order. A verse is thirty words at the outside, so the quadratic
    /// table costs nothing and its answer is exact — an anchor heuristic would be faster and would
    /// pair a repeated καί with the wrong one of its twins.
    /// </summary>
    private static List<(int, int)> LongestCommonSubsequence(string[] left, string[] right)
    {
        var width = right.Length + 1;
        var table = ArrayPool<int>.Shared.Rent((left.Length + 1) * width);

        try
        {
            Array.Clear(table, 0, (left.Length + 1) * width);

            for (var i = left.Length - 1; i >= 0; i--)
            {
                for (var j = right.Length - 1; j >= 0; j--)
                {
                    table[(i * width) + j] = left[i] == right[j]
                        ? table[((i + 1) * width) + j + 1] + 1
                        : Math.Max(table[((i + 1) * width) + j], table[(i * width) + j + 1]);
                }
            }

            var pairs = new List<(int, int)>(Math.Min(left.Length, right.Length));
            int at = 0, other = 0;

            while (at < left.Length && other < right.Length)
            {
                if (left[at] == right[other])
                {
                    pairs.Add((at, other));
                    at++;
                    other++;
                }
                else if (table[((at + 1) * width) + other] >= table[(at * width) + other + 1])
                {
                    at++;
                }
                else
                {
                    other++;
                }
            }

            return pairs;
        }
        finally
        {
            ArrayPool<int>.Shared.Return(table);
        }
    }
}
