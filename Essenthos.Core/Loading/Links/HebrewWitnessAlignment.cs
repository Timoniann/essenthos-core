using Essenthos.Core.Database.Entities.Enums;

namespace Essenthos.Core.Loading.Links;

/// <param name="Consonants">The word with the pointing and the punctuation set aside.</param>
/// <param name="Lexeme">Its dictionary form, folded the same way, or empty where it has none.</param>
internal readonly record struct HebrewForm(string Consonants, string Lexeme);

/// <param name="From">
/// Which words of the first verse this is, counting from zero. Empty where the first witness has
/// none, which is what an <see cref="LinkRelation.Omits"/> says.
/// </param>
/// <param name="To">The same for the second verse, and empty for <see cref="LinkRelation.Expands"/>.</param>
internal readonly record struct HebrewPairing(
    IReadOnlyList<int> From,
    IReadOnlyList<int> To,
    LinkRelation Relation,
    double Confidence);

/// <summary>
/// One verse of one Hebrew witness laid against the same verse of another, word for word.
///
/// It is separate from the loader that writes the links because it is the part with an argument in
/// it: which words the alignment is willing to call the same word, and how sure it is. That
/// argument is checkable against the texts themselves and needs no database to check.
///
/// <para>
/// **Nothing is paired without evidence.** Two words pair when they are the same consonants, when
/// they are the same lexeme, or when they differ by a letter or two — which is one tradition
/// writing the vowel letter the other leaves out, and is most of the difference between the
/// Samaritan and the Masoretic. Anything else scores below the cost of leaving both words
/// unpaired, so the alignment says <em>each has a word the other has not</em> rather than
/// inventing a correspondence to close the gap. That is the whole reason the plus and minus counts
/// mean anything.
/// </para>
/// </summary>
internal static class HebrewWitnessAlignment
{
    /// <summary>Both witnesses write the same consonants. There is nothing left to establish.</summary>
    public const double Identical = 0.95;

    /// <summary>
    /// Different consonants, the same lexeme. The two datasets lemmatise independently, so
    /// agreement here is a second analysis reaching the same word rather than the same string twice.
    /// </summary>
    public const double LexemeAgrees = 0.85;

    /// <summary>
    /// Different consonants and no lemma agreement, but within a letter or two. The spelling is the
    /// whole of the evidence.
    /// </summary>
    public const double Spelling = 0.75;

    /// <summary>
    /// A word one witness has and the other has not, in a verse the two otherwise write the same
    /// way. The words around it are what makes the absence certain.
    /// </summary>
    public const double AbsenceWhereTheyAgree = 0.85;

    /// <summary>The same, in a verse where the two do not run together.</summary>
    public const double AbsenceWhereTheyDoNot = 0.65;

    /// <summary>
    /// How much of the shorter verse must match consonant for consonant before an absence in it is
    /// read as the strong kind. Below this it is the alignment itself that is in doubt.
    /// </summary>
    private const double Agreement = 0.75;

    private const int IdenticalScore = 4;
    private const int LexemeScore = 2;
    private const int SpellingScore = 2;

    /// <summary>
    /// Two words that are neither the same word nor the same spelling. Below twice
    /// <see cref="GapScore"/> on purpose: an alignment that pairs them scores worse than one that
    /// leaves both unpaired.
    /// </summary>
    private const int UnrelatedScore = -9;

    private const int GapScore = -2;

    /// <summary>
    /// How many letters two spellings of one word may differ by. One covers the plene and defective
    /// writing that is most of the difference between these traditions; two is allowed only on a
    /// word long enough that two letters are still a small part of it.
    /// </summary>
    private const int SpellingDistance = 2;

    private const int LongEnoughForTwo = 5;

    /// <summary>
    /// The correspondences between two verses, in the order the alignment walks them. The relation
    /// is written from the first verse's point of view: <see cref="LinkRelation.Expands"/> where it
    /// has a word the second has not, <see cref="LinkRelation.Omits"/> where the second has one it
    /// has not.
    /// </summary>
    public static List<HebrewPairing> Pair(IReadOnlyList<HebrewForm> left, IReadOnlyList<HebrewForm> right)
    {
        var here = Written(left);
        var there = Written(right);
        var steps = Align(Forms(left, here), Forms(right, there));

        var matched = steps.Count(step => step.From >= 0 && step.To >= 0
                                          && left[here[step.From]].Consonants == right[there[step.To]].Consonants);
        var shorter = Math.Min(here.Count, there.Count);
        var absence = shorter > 0 && (double)matched / shorter >= Agreement
            ? AbsenceWhereTheyAgree
            : AbsenceWhereTheyDoNot;

        var pairings = new List<Correspondence>(steps.Count);
        foreach (var (from, to) in steps)
        {
            pairings.Add(from < 0
                ? new Correspondence([], [there[to]], LinkRelation.Omits, absence)
                : to < 0
                    ? new Correspondence([here[from]], [], LinkRelation.Expands, absence)
                    : left[here[from]].Consonants == right[there[to]].Consonants
                        ? new Correspondence([here[from]], [there[to]], LinkRelation.Equals, Identical)
                        : new Correspondence(
                            [here[from]],
                            [there[to]],
                            LinkRelation.Renders,
                            ShareALexeme(left[here[from]], right[there[to]])
                                ? LexemeAgrees
                                : Spelling));
        }

        Attach(left.Count, here, pairings, correspondence => correspondence.From);
        Attach(right.Count, there, pairings, correspondence => correspondence.To);

        return [.. pairings.Select(c => new HebrewPairing(c.From, c.To, c.Relation, c.Confidence))];
    }

    /// <summary>
    /// Which words of a verse print letters. BHSA records the definite article that has assimilated
    /// into the preposition before it as a word of its own with no letters at all — 1,681 of them
    /// in the Pentateuch — and the Samaritan dataset records no such thing.
    ///
    /// Left in the alignment they would each become a word the Samaritan lacks, and the corpus
    /// would say the Samaritan omits an article 1,681 times where nothing is omitted and the two
    /// traditions write the identical letters. So they are held back from the alignment and put
    /// into it afterwards, beside the word they were pronounced with.
    /// </summary>
    private static List<int> Written(IReadOnlyList<HebrewForm> verse)
    {
        var written = new List<int>(verse.Count);
        for (var i = 0; i < verse.Count; i++)
        {
            if (verse[i].Consonants.Length > 0)
            {
                written.Add(i);
            }
        }

        return written;
    }

    private static List<HebrewForm> Forms(IReadOnlyList<HebrewForm> verse, List<int> written) =>
        [.. written.Select(at => verse[at])];

    /// <summary>
    /// Puts the words that print no letters back, each into the correspondence its own side's
    /// nearest written word stands in — the one it was pronounced with. A verse that is nothing but
    /// letterless words has no correspondence to put them in and gets none, which cannot happen in
    /// either of these texts and is not worth a branch that would never run.
    /// </summary>
    private static void Attach(
        int words,
        List<int> written,
        List<Correspondence> pairings,
        Func<Correspondence, List<int>> side)
    {
        if (written.Count == words || written.Count == 0)
        {
            return;
        }

        var holder = new Dictionary<int, Correspondence>(written.Count);
        foreach (var pairing in pairings)
        {
            foreach (var at in side(pairing))
            {
                holder[at] = pairing;
            }
        }

        var next = 0;
        for (var at = 0; at < words; at++)
        {
            if (next < written.Count && written[next] == at)
            {
                next++;
                continue;
            }

            // The article assimilates into the word before it, so that is the one it belongs with;
            // at the head of a verse there is nothing before it and the word after is the only
            // candidate there is.
            var neighbour = next > 0 ? written[next - 1] : written[0];
            var into = side(holder[neighbour]);
            into.Insert(into.FindIndex(held => held > at) is var index && index < 0 ? into.Count : index, at);
        }
    }

    private sealed record Correspondence(
        List<int> From,
        List<int> To,
        LinkRelation Relation,
        double Confidence);

    /// <summary>
    /// Needleman-Wunsch over two verses of morphemes. The verses run together almost everywhere, so
    /// the matrix is a few hundred cells and the whole Pentateuch is a second's work; nothing here
    /// is worth a heuristic that would be harder to explain.
    /// </summary>
    private static List<(int From, int To)> Align(IReadOnlyList<HebrewForm> left, IReadOnlyList<HebrewForm> right)
    {
        var rows = left.Count + 1;
        var columns = right.Count + 1;
        var score = new int[rows, columns];
        var similarity = new int[rows, columns];

        for (var i = 1; i < rows; i++)
        {
            score[i, 0] = score[i - 1, 0] + GapScore;
        }

        for (var j = 1; j < columns; j++)
        {
            score[0, j] = score[0, j - 1] + GapScore;
        }

        for (var i = 1; i < rows; i++)
        {
            for (var j = 1; j < columns; j++)
            {
                similarity[i, j] = Similarity(left[i - 1], right[j - 1]);
                score[i, j] = Math.Max(
                    score[i - 1, j - 1] + similarity[i, j],
                    Math.Max(score[i - 1, j] + GapScore, score[i, j - 1] + GapScore));
            }
        }

        var steps = new List<(int, int)>(Math.Max(left.Count, right.Count) + 8);
        var row = left.Count;
        var column = right.Count;

        while (row > 0 || column > 0)
        {
            if (row > 0 && column > 0 && score[row, column] == score[row - 1, column - 1] + similarity[row, column])
            {
                steps.Add((row - 1, column - 1));
                row--;
                column--;
            }
            else if (row > 0 && score[row, column] == score[row - 1, column] + GapScore)
            {
                steps.Add((row - 1, -1));
                row--;
            }
            else
            {
                steps.Add((-1, column - 1));
                column--;
            }
        }

        steps.Reverse();
        return steps;
    }

    private static int Similarity(HebrewForm left, HebrewForm right)
    {
        if (left.Consonants == right.Consonants)
        {
            return IdenticalScore;
        }

        if (ShareALexeme(left, right))
        {
            return LexemeScore;
        }

        var distance = Distance(left.Consonants, right.Consonants);
        return distance == 1
               || (distance == SpellingDistance
                   && Math.Max(left.Consonants.Length, right.Consonants.Length) >= LongEnoughForTwo)
            ? SpellingScore
            : UnrelatedScore;
    }

    private static bool ShareALexeme(HebrewForm left, HebrewForm right) =>
        left.Lexeme.Length > 0 && left.Lexeme == right.Lexeme;

    /// <summary>
    /// How many letters apart two spellings are, given up on past <see cref="SpellingDistance"/> —
    /// the answer is only ever read as a threshold, and stopping early is what keeps this off the
    /// profile at a hundred thousand comparisons.
    /// </summary>
    private static int Distance(string left, string right)
    {
        var beyond = SpellingDistance + 1;
        if (Math.Abs(left.Length - right.Length) > SpellingDistance)
        {
            return beyond;
        }

        Span<int> previous = stackalloc int[right.Length + 1];
        Span<int> current = stackalloc int[right.Length + 1];
        for (var j = 0; j <= right.Length; j++)
        {
            previous[j] = j;
        }

        for (var i = 1; i <= left.Length; i++)
        {
            current[0] = i;
            var best = i;
            for (var j = 1; j <= right.Length; j++)
            {
                current[j] = Math.Min(
                    Math.Min(previous[j] + 1, current[j - 1] + 1),
                    previous[j - 1] + (left[i - 1] == right[j - 1] ? 0 : 1));
                best = Math.Min(best, current[j]);
            }

            if (best > SpellingDistance)
            {
                return beyond;
            }

            var finished = previous;
            previous = current;
            current = finished;
        }

        return previous[right.Length];
    }
}
