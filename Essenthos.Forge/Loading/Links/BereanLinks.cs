using Essenthos.Core.Berean;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Loading.Frame;
using Essenthos.Core.Utils;

namespace Essenthos.Core.Loading.Links;

/// <summary>
/// Which Berean word renders which Greek word, stated by the Berean's own translation tables.
///
/// The corpus has had exactly one whole-Bible stated word mapping — the King James against the
/// Hebrew — and nothing at all of the kind for the New Testament, where the King James reaches
/// 77.2% of the Greek by inference from Strong numbers. This is a second, independent, human-made
/// answer, and it is the only thing the New Testament's methods can be calibrated against.
///
/// <para>
/// **The join is order, and the check is the number.** Their Greek is the Berean Greek Bible, which
/// is Nestle 1904 with modernised spelling, so the surface forms do not match — Nestle writes
/// Δαυείδ where they write Δαυίδ — and matching on the form would throw away most of the file. What
/// does hold is the word order, and both sides state a Strong number independently of each other and
/// of the spelling. Measured over the New Testament before this was written: 7,488 of 7,939 verses
/// have the same number of Greek words, and inside those, 128,029 of 129,491 Strong numbers agree —
/// **98.87%**. The 1.13% are two dictionaries disposing of the same suppletive verbs differently,
/// λέγω against εἶπον 992 times and ὁράω against ἰδού 212, which is a disagreement about lexicography
/// and not about which word is which.
/// </para>
///
/// <para>
/// So a verse is refused whole where the counts differ, and refused again where the numbers agree on
/// fewer than half its words — the second is the net that catches a verse whose counts match by
/// accident. Everything that survives is written as <c>stated-by-source</c> with no confidence,
/// because it is what the file says and not what we worked out.
/// </para>
/// </summary>
internal static class BereanLinks
{
    private const char OpenParagraph = 'פ';

    private const char ClosedParagraph = 'ס';

    /// <summary>The table's mark for a word rendered together with the word whose English follows.</summary>
    private const string Together = "vvv";

    /// <summary>
    /// How many of the verse's Strong numbers the two sides state the same way, and how many either
    /// of them states at all.
    /// </summary>
    internal static (int Agreed, int Of) Agreement(IReadOnlyList<BereanRow> rows, IReadOnlyList<Word> witness)
    {
        int agreed = 0, of = 0;
        for (var i = 0; i < rows.Count; i++)
        {
            if (rows[i].StrongNumber.Length == 0 || witness[i].Strong is not { Length: > 0 } theirs)
            {
                continue;
            }

            of++;
            if (string.Equals(rows[i].StrongNumber, theirs, StringComparison.Ordinal))
            {
                agreed++;
            }
        }

        return (agreed, of);
    }

    /// <summary>
    /// The Hebrew, which does not join by order and cannot join by number.
    ///
    /// BHSA writes the preposition, the article and the noun of לָאוֹר as three words and the
    /// Westminster edition the Berean follows writes one, so the counts never agree — 21.4% of
    /// verses, measured. Their Strong numbers do not agree either, because the numbering is
    /// OpenScriptures' against ETCBC's: 43.8%. What both editions preserve exactly is the letters.
    ///
    /// <para>
    /// So the verse is joined on its letters and each Berean word renders the BHSA words its letters
    /// fall in. The division runs both ways: BHSA also writes a name as one word where the Westminster
    /// edition writes two — בֵּית לֶחֶם, בֵּית־אֵל — and then both Berean words render the one BHSA
    /// word, which is what the two editions' letters say and all they say. A word that prints no
    /// letters, an article assimilated into its preposition, goes with the Berean word whose letters
    /// follow it; trailing ones go with nothing.
    /// </para>
    ///
    /// <para>
    /// Returns null unless the whole verse writes the same letters. A verse that differs anywhere — a
    /// qere against a ketiv, a word one edition prints and the other does not — has no place where
    /// the letters can be trusted to line the rest up, and a partial run would pair every remaining
    /// word with the wrong Hebrew and look exactly like a correct verse.
    /// </para>
    /// </summary>
    /// <returns>For each Berean word, the indices of the BHSA words it renders.</returns>
    internal static List<List<int>>? Letters(IReadOnlyList<string> berean, IReadOnlyList<string> bhsa)
    {
        var whole = string.Concat(bhsa);
        if (!string.Equals(string.Concat(berean), whole, StringComparison.Ordinal))
        {
            berean = WithoutSectionMarks(berean, whole) ?? berean;
            if (!string.Equals(string.Concat(berean), whole, StringComparison.Ordinal))
            {
                return null;
            }
        }

        var starts = new int[bhsa.Count + 1];
        for (var at = 0; at < bhsa.Count; at++)
        {
            starts[at + 1] = starts[at] + bhsa[at].Length;
        }

        var runs = new List<List<int>>(berean.Count);
        var start = 0;

        foreach (var letters in berean)
        {
            var end = start + letters.Length;
            var run = new List<int>();

            for (var at = 0; at < bhsa.Count && starts[at] <= end; at++)
            {
                var (from, to) = (starts[at], starts[at + 1]);
                if ((from < end && start < to) || (from == to && start <= from && from < end))
                {
                    run.Add(at);
                }
            }

            runs.Add(run);
            start = end;
        }

        return runs;
    }

    /// <summary>
    /// The Westminster edition's paragraph marks, taken off the word they are printed against.
    ///
    /// It prints <c>פ</c> for an open paragraph and <c>ס</c> for a closed one, and where a section
    /// ends inside a verse the mark is glued to the last word of the section — יִשְׂרָאֵל פ, הוּאס —
    /// with nothing but its place to tell it from a letter. So a final פ or ס is dropped only where
    /// the word does not match BHSA with it and does without it, and the caller still demands the
    /// whole verse agree.
    /// </summary>
    private static List<string>? WithoutSectionMarks(IReadOnlyList<string> berean, string bhsa)
    {
        var unmarked = new List<string>(berean.Count);
        var at = 0;

        foreach (var letters in berean)
        {
            if (bhsa.AsSpan(at).StartsWith(letters, StringComparison.Ordinal))
            {
                unmarked.Add(letters);
            }
            else if (letters.Length > 1
                     && letters[^1] is OpenParagraph or ClosedParagraph
                     && bhsa.AsSpan(at).StartsWith(letters.AsSpan(0, letters.Length - 1), StringComparison.Ordinal))
            {
                unmarked.Add(letters[..^1]);
            }
            else
            {
                return null;
            }

            at += unmarked[^1].Length;
        }

        return unmarked;
    }

    /// <summary>
    /// The English phrases against the verse's own words, in the file's English order.
    ///
    /// The phrases account for every word of a verse and nothing else — measured on 3,000 random
    /// verses, all 3,000 — so this walks the two in step and stops the whole verse the moment they
    /// part. Returning false rather than writing what it has is the point: a partial alignment
    /// produces links about the wrong words for the rest of the verse and looks exactly right.
    ///
    /// <para>
    /// A witness word in <paramref name="overruled"/> is one a ruling on the English says the table
    /// is wrong about, so the table's word on it is left out: an absence is not written, and a
    /// rendering names the row's other words or is not written either.
    /// </para>
    /// </summary>
    internal static bool Pair(
        IReadOnlyList<BereanRow> rows,
        IReadOnlyList<Word> ours,
        IReadOnlyList<List<long>> witness,
        List<Draft> drafts,
        Silences silences,
        IReadOnlySet<long>? overruled = null)
    {
        if (Claims(rows, ours) is not { } claimed)
        {
            return false;
        }

        if (overruled is { Count: > 0 } && witness.Any(words => words.Any(overruled.Contains)))
        {
            silences.Overruled += witness.SelectMany(words => words).Where(overruled.Contains).Distinct().Count();
            witness = [.. witness.Select(words => words.Where(word => !overruled.Contains(word)).ToList())];
        }

        var into = Joins(rows, claimed);

        // Two Berean words can share one witness word where the editions divide a name differently,
        // so a word some row renders is not also unrendered, and a word two silent rows share is
        // unrendered once.
        var rendered = new HashSet<long>();
        for (var i = 0; i < rows.Count; i++)
        {
            if (claimed[i] is { Count: > 0 } || into[i] is not null)
            {
                rendered.UnionWith(witness[i]);
            }
        }

        var omitted = new HashSet<long>();
        for (var i = 0; i < rows.Count; i++)
        {
            var mine = claimed[i] ?? [];
            if (mine.Count > 0)
            {
                List<long> theirs = [.. witness[i]];
                for (var j = 0; j < rows.Count; j++)
                {
                    if (into[j] == i)
                    {
                        theirs.AddRange(witness[j].Where(word => !theirs.Contains(word)));
                    }
                }

                if (theirs.Count > 0)
                {
                    drafts.Add(new Draft(LinkRelation.Renders, mine, theirs));
                }

                continue;
            }

            if (into[i] is not null)
            {
                silences.Joined++;
                continue;
            }

            // The file distinguishes its silences, and so does this. A dash is a word the English
            // does not render. An ellipsis is one it renders somewhere else without saying where, and
            // so is a vvv with no rendering after it to join; calling either unrendered would be
            // false. A row with no English at all states nothing, so nothing is written for it.
            var english = rows[i].English;
            if (english.Contains('.', StringComparison.Ordinal) || english.Contains(Together, StringComparison.Ordinal))
            {
                silences.Moved++;
                continue;
            }

            if (!english.Contains('-', StringComparison.Ordinal))
            {
                silences.Unmarked++;
                continue;
            }

            var unrendered = witness[i].Where(word => !rendered.Contains(word) && omitted.Add(word)).ToList();
            if (unrendered.Count > 0)
            {
                silences.Absent++;
                drafts.Add(new Draft(LinkRelation.Omits, [], unrendered));
            }
        }

        return true;
    }

    /// <summary>
    /// For each row the table marks <c>vvv</c>, the row whose English renders it together with its
    /// own word: the next row in the English order that has English, past any further <c>vvv</c>.
    /// <em>μὴ</em> is <c>vvv</c> and <em>ἀγαπῶν</em> is <em>does not love</em> in 1 John 4:8, and
    /// the <em>not</em> is the <em>μὴ</em>. Measured over the whole table before this was written:
    /// of 4,849 such rows, 4,841 are followed by a rendering, and the other eight by a dash or an
    /// ellipsis, which leave them with nothing stated to join.
    /// </summary>
    internal static int?[] Joins(IReadOnlyList<BereanRow> rows, List<long>[] claimed)
    {
        var into = new int?[rows.Count];
        var waiting = new List<int>();
        foreach (var index in Enumerable.Range(0, rows.Count).OrderBy(index => rows[index].EnglishOrder))
        {
            if (claimed[index] is { Count: > 0 })
            {
                foreach (var joined in waiting)
                {
                    into[joined] = index;
                }

                waiting.Clear();
            }
            else if (rows[index].English.Contains(Together, StringComparison.Ordinal))
            {
                waiting.Add(index);
            }
            else
            {
                waiting.Clear();
            }
        }

        return into;
    }

    /// <summary>
    /// For each of the verse's rows, the words of our verse its English phrase is; null where the
    /// phrases and the words part.
    /// </summary>
    internal static List<long>[]? Claims(IReadOnlyList<BereanRow> rows, IReadOnlyList<Word> ours)
    {
        var claimed = new List<long>[rows.Count];
        var at = 0;

        // The file's English order, kept as indices into the verse's own rows, so a row's rendering
        // and the Greek word it renders never have to be looked up by value.
        var byEnglish = Enumerable.Range(0, rows.Count)
            .OrderBy(index => rows[index].EnglishOrder)
            .ToList();

        foreach (var index in byEnglish)
        {
            var rendering = BereanWords.Rendering(rows[index].English);
            var mine = new List<long>(rendering.Count);

            foreach (var word in rendering)
            {
                if (at >= ours.Count || !BereanWords.Same(ours[at].Surface, word))
                {
                    return null;
                }

                mine.Add(ours[at].Id);
                at++;
            }

            claimed[index] = mine;
        }

        if (at != ours.Count)
        {
            return null;
        }

        return claimed;
    }

    internal sealed record Word(long Id, int VerseId, string Surface, string? Strong);

    internal sealed record Draft(LinkRelation Relation, List<long> From, List<long> To);

    /// <summary>The rows with no English of their own, by what the table says about each.</summary>
    internal sealed class Silences
    {
        public int Absent { get; set; }

        public int Moved { get; set; }

        public int Joined { get; set; }

        public int Unmarked { get; set; }

        public int Overruled { get; set; }
    }
}
