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
    ///
    /// <para>
    /// <paramref name="conjunctions"/> are the witness's words that are the conjunction ו, which BHSA writes apart from the word
    /// it is prefixed to; given them, a row of a conjunction and bracketed words is read as <see cref="Supplied"/> says.
    /// <paramref name="prepositions"/> are its words that are a preposition, by their Strong number; given them, a row of a
    /// preposition and bracketed words is read as <see cref="Prefixed"/> says.
    /// </para>
    /// </summary>
    internal static bool Pair(
        IReadOnlyList<BereanRow> rows,
        IReadOnlyList<Word> ours,
        IReadOnlyList<List<long>> witness,
        List<Draft> drafts,
        Silences silences,
        IReadOnlySet<long>? overruled = null,
        IReadOnlySet<long>? conjunctions = null,
        IReadOnlyDictionary<long, string>? prepositions = null)
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
                    if (prepositions is not null && Prefixed(rows, claimed, witness, prepositions, i) is { } prefix)
                    {
                        drafts.Add(new Draft(LinkRelation.Renders, [.. mine.Skip(1)], theirs, Bracketed: true));
                        drafts.Add(new Draft(LinkRelation.Renders, [mine[0]], [prefix]));
                        silences.PrepositionsMoved++;
                    }
                    else if (conjunctions is not null && Supplied(rows, claimed, witness, conjunctions, i) is { } supplied)
                    {
                        drafts.Add(new Draft(LinkRelation.Renders, [.. mine.Skip(1)], theirs, Bracketed: true));
                        if (supplied.Waw is { } to)
                        {
                            drafts.Add(new Draft(LinkRelation.Renders, [mine[0]], [to]));
                            silences.ConjunctionsMoved++;
                        }
                        else
                        {
                            silences.ConjunctionsUnlinked++;
                        }
                    }
                    else
                    {
                        drafts.Add(new Draft(LinkRelation.Renders, mine, theirs));
                    }
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
    /// A row whose English is a conjunction and bracketed words and nothing else, on a word with no conjunction of its own.
    /// Joshua 3:6 gives יְהוֹשֻׁעַ «And [he]» and וַיֹּאמֶר before it «said»: the bracketed words are what the translators
    /// wrote for the word, so they render it, and the conjunction renders the ו of the word beside it, which BHSA writes as a
    /// word of its own.
    ///
    /// <para>
    /// Which ו: the one of the word before, which opens the clause the row's word stands in (a verb before its subject) or
    /// joins the phrase it ends (וְאֵשֶׁת נֹחַ). Only where the word before has no ו, the one of a verb right after, which
    /// opens the next clause the bracketed words stand in (2 Kings 23:6, «and [there] ground»). A ו counts only where its
    /// word renders something and its English does not open with a conjunction of its own; otherwise the conjunction is
    /// left unlinked. «For» renders a preposition or כִּי, never a ו, and is left unlinked too where <see cref="Prefixed"/> finds no ל.
    /// </para>
    ///
    /// <para>
    /// A row whose word is or carries a conjunction, a preposition or an adverb is not read so: there the conjunction renders
    /// the word's own ו or the word itself («for» over לוֹ, «so» over כֵּן), and the bracketed words often stand for a word
    /// further on (Judges 19:25 gives לוֹ «So [the Levite]» for הָאִישׁ after it).
    /// </para>
    /// </summary>
    /// <returns>Null where the row is not of this shape; otherwise the ו its conjunction renders, if one is found.</returns>
    private static Conjunction? Supplied(
        IReadOnlyList<BereanRow> rows,
        List<long>[] claimed,
        IReadOnlyList<List<long>> witness,
        IReadOnlySet<long> conjunctions,
        int at)
    {
        if (LeadAndBracketed(rows[at].English) is not { } conjunction
            || !SuppliedConjunctions.Contains(conjunction)
            || Parts(rows[at].Parsing).ToList() is not { Count: > 0 } parts
            || !parts.All(SubjectPart))
        {
            return null;
        }

        if (!WawConjunctions.Contains(conjunction))
        {
            return new Conjunction(null);
        }

        var before = at > 0 ? witness[at - 1].Where(conjunctions.Contains).ToList() : [];
        if (before.Count > 0)
        {
            return new Conjunction(Free(at - 1) ? before.Single() : null);
        }

        return at + 1 < rows.Count
               && witness[at + 1].Where(conjunctions.Contains).ToList() is [var after]
               && Parts(rows[at + 1].Parsing).Contains("Verb")
               && Free(at + 1)
            ? new Conjunction(after)
            : new Conjunction(null);

        bool Free(int row) =>
            witness[row].Count(conjunctions.Contains) == 1
            && claimed[row] is { Count: > 0 }
            && BereanWords.Rendering(rows[row].English) is [var first, ..]
            && !OpeningConjunctions.Contains(first);
    }

    /// <summary>
    /// A row whose English is a preposition and bracketed words and nothing else, on a word with no preposition of its own,
    /// where the preposition is the prefix of the word the English goes on to. 2 Samuel 23:21 gives מִיַּד הַמִּצְרִי, and
    /// the table «hand» for מִיַּד and «from [his]» for הַמִּצְרִי: the bracketed words are what the translators wrote for
    /// the Egyptian, so they render him, and «from» renders the מִ of מִיַּד, which BHSA writes as a word of its own.
    ///
    /// <para>
    /// Only where nothing else could be meant: the word whose English comes next stands right before the row's word in the
    /// Hebrew (the word the row's word completes, «from [his] hand» for «from the hand of the Egyptian»), the table's parsing
    /// gives it the preposition the English names (ב for in, into, with, at, by; ל for to, for; מ for from; כ for like, as),
    /// exactly one of its witness words is that preposition, and its own English does not open with a preposition that would
    /// render the prefix itself. Any other row of the shape is left as the table has it.
    /// </para>
    /// </summary>
    /// <returns>The witness word the preposition renders; null where the row is not of this shape.</returns>
    private static long? Prefixed(
        IReadOnlyList<BereanRow> rows,
        List<long>[] claimed,
        IReadOnlyList<List<long>> witness,
        IReadOnlyDictionary<long, string> prepositions,
        int at)
    {
        if (at == 0
            || LeadAndBracketed(rows[at].English) is not { } preposition
            || !Prefixes.TryGetValue(preposition, out var prefix)
            || Parts(rows[at].Parsing).ToList() is not { Count: > 0 } parts
            || !parts.All(SubjectPart))
        {
            return null;
        }

        var before = at - 1;
        var order = rows[at].EnglishOrder;
        var next = rows[before].EnglishOrder;
        if (next <= order
            || rows.Any(row => row.EnglishOrder > order && row.EnglishOrder < next)
            || claimed[before] is not { Count: > 0 }
            || BereanWords.Rendering(rows[before].English) is not [var first, ..]
            || OpeningPrepositions.Contains(first)
            || !Parts(rows[before].Parsing).Contains(prefix.Part))
        {
            return null;
        }

        return witness[before].Where(id => prepositions.TryGetValue(id, out var strong) && strong == prefix.Strong).ToList() is [var word]
            ? word
            : null;
    }

    /// <summary>The first word of a phrase that is one word and one bracketed span, «And [he]»; null for any other.</summary>
    private static string? LeadAndBracketed(string english)
    {
        var phrase = english.Trim();
        var space = phrase.IndexOf(' ');
        if (space < 0)
        {
            return null;
        }

        var span = phrase[(space + 1)..].TrimStart();
        return span.Length > 2
               && span[0] == '['
               && span[^1] == ']'
               && span.AsSpan(1, span.Length - 2).IndexOfAny('[', ']') < 0
               && BereanWords.Rendering(span).Count > 0
            ? phrase[..space]
            : null;
    }

    /// <summary>The head of each part the table's parsing names: <c>Conjunctive waw, Preposition-b | Noun - …</c> is three.</summary>
    private static IEnumerable<string> Parts(string parsing) =>
        parsing.Split('|', ',').Select(part => part.Split(" - ")[0].Trim()).Where(part => part.Length > 0);

    private static bool SubjectPart(string part) => SubjectParts.Contains(part) || part.Contains(" person", StringComparison.Ordinal);

    private static readonly HashSet<string> SuppliedConjunctions =
        new(["and", "so", "then", "but", "now", "yet", "for"], StringComparer.OrdinalIgnoreCase);

    private static readonly HashSet<string> WawConjunctions =
        new(["and", "so", "then", "but", "now", "yet"], StringComparer.OrdinalIgnoreCase);

    /// <summary>The English a ו is rendered by, or a word that may stand for one: a row opening with one has its ו rendered.</summary>
    private static readonly HashSet<string> OpeningConjunctions =
        new(["and", "so", "then", "but", "now", "yet", "or", "for"], StringComparer.OrdinalIgnoreCase);

    /// <summary>The parts of a word that can stand for a person or thing the translators named with a pronoun or a name.</summary>
    private static readonly HashSet<string> SubjectParts =
        new(["Noun", "Article", "Pronoun", "Verb", "Number", "Adjective"], StringComparer.Ordinal);

    /// <summary>
    /// The prefixed preposition each English preposition names, as the table's parsing writes it and as BHSA numbers the word
    /// it writes for it.
    /// </summary>
    private static readonly Dictionary<string, Prefix> Prefixes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["in"] = new("Preposition-b", "H9003"), ["into"] = new("Preposition-b", "H9003"), ["with"] = new("Preposition-b", "H9003"),
        ["at"] = new("Preposition-b", "H9003"), ["by"] = new("Preposition-b", "H9003"),
        ["to"] = new("Preposition-l", "H9005"), ["for"] = new("Preposition-l", "H9005"),
        ["from"] = new("Preposition-m", "H4480"),
        ["like"] = new("Preposition-k", "H9004"), ["as"] = new("Preposition-k", "H9004"),
    };

    /// <summary>The English a prefixed preposition is rendered by: a row opening with one has its prefix rendered.</summary>
    private static readonly HashSet<string> OpeningPrepositions = new(
        ["in", "into", "with", "at", "by", "to", "for", "from", "like", "as", "on", "upon", "of", "among", "within", "through",
         "before", "after", "against", "over", "under", "toward", "about", "according", "when", "while"],
        StringComparer.OrdinalIgnoreCase);

    private readonly record struct Conjunction(long? Waw);

    private readonly record struct Prefix(string Part, string Strong);

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

    /// <param name="Bracketed">
    /// Whether the words the English brackets render here, which they do nowhere else (<see cref="Supplied"/>, <see cref="Prefixed"/>).
    /// </param>
    internal sealed record Draft(LinkRelation Relation, List<long> From, List<long> To, bool Bracketed = false);

    /// <summary>
    /// The rows with no English of their own, by what the table says about each, and where the conjunctions of the rows
    /// <see cref="Supplied"/> reads and the prepositions of those <see cref="Prefixed"/> reads went.
    /// </summary>
    internal sealed class Silences
    {
        public int Absent { get; set; }

        public int Moved { get; set; }

        public int Joined { get; set; }

        public int Unmarked { get; set; }

        public int Overruled { get; set; }

        /// <summary>Conjunctions of a conjunction and bracketed words moved to the ו beside the word (<see cref="Supplied"/>).</summary>
        public int ConjunctionsMoved { get; set; }

        /// <summary>Conjunctions of such a row for which no ו beside the word could be told, left unlinked.</summary>
        public int ConjunctionsUnlinked { get; set; }

        /// <summary>Prepositions of a preposition and bracketed words moved to the prefix of the next word (<see cref="Prefixed"/>).</summary>
        public int PrepositionsMoved { get; set; }
    }
}
