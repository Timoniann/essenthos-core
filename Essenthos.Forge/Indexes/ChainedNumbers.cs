namespace Essenthos.Core.Indexes;

/// <summary>
/// The one Strong number each word of a text carries for marking (TSK-1822, the owner's rule of 2026-11-18): its own number
/// where it has one; otherwise the number of a word of an edition of an original that a renders or equals link joins it to;
/// otherwise the number of a translation's word it is joined to, as that translation carries it by the same rule. A word's
/// own number is never joined by another.
/// <para>
/// The texts are taken in an order (<see cref="Order"/>), and a text reads the numbers another text was given by this rule
/// only where that text comes before it; of a text after it, only the numbers its words carry themselves. So no number goes
/// round a cycle and every text's numbers are a function of texts already numbered.
/// </para>
/// <para>
/// Among several numbers a word could take, the one of an original wins over a translation's, then the surer link (a link
/// stated or ruled counts as sure), then the shorter chain, then the number the same form of the text carries most often
/// where it carries one number alone (a source joining 神 to אֱלֹהִים מִן הָעֲבֹדָה gives it God's number, as 神 takes everywhere
/// else), then the number fewer words of the lending text carry (of an article and the noun it governs, joined in one link to
/// one word, the noun's), then the number and the word lending it in ordinal order. The relative שֶׁ is counted with אֲשֶׁר in
/// both counts (<see cref="CountedAs"/>).
/// </para>
/// <para>
/// A translation's word joined to an original takes only what the group rules (<see cref="GroupNumbers"/>, PRB-1869) give it over
/// those links: a link holding several original words or several of its words no longer hands a function word the content
/// word's number, nor a content word a prefix's. A word the translation marks as supplied takes none.
/// </para>
/// </summary>
internal static class ChainedNumbers
{
    /// <summary>The confidence from which a link is sure enough for a word's own number to be held against the number it gives.</summary>
    public const double Reliable = 0.9;

    /// <summary>
    /// Numbers counted with another's for the tie-breaks: the relative שֶׁ (H7945) is the short form of אֲשֶׁר (H834), so a form
    /// whose most usual number is אֲשֶׁר's takes it, and against a content word joined with it in one link it counts as common as
    /// אֲשֶׁר.
    /// </summary>
    private static readonly Dictionary<string, string> CountedAs = new(StringComparer.Ordinal) { ["H7945"] = "H834" };

    private static int Counted(string number, Func<string, int> count) =>
        count(number) + (CountedAs.TryGetValue(number, out var with) ? count(with) : 0);

    /// <summary>A text whose words a text's links reach, with the numbers it lends: by own key, the number and the links it took to reach it.</summary>
    /// <param name="classes">The class of each number of the originals (<see cref="OriginalWords"/>); a number it lacks is a content word's.</param>
    /// <param name="finite">The keys of the text's words that are finite verbs.</param>
    internal sealed class Lender(
        bool original,
        IReadOnlyDictionary<string, (string Number, int Hops)> numbers,
        IReadOnlyDictionary<string, WordClass>? classes = null,
        IReadOnlySet<string>? finite = null)
    {
        private readonly Lazy<Dictionary<string, int>> _occurrences = new(() =>
            numbers.Values.GroupBy(n => n.Number, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal));

        /// <summary>A word of the text as the group rules see it.</summary>
        public GroupNumbers.Original Word(string key, string number) =>
            new(key, number,
                OriginalWords.Prefixes.Contains(number) ? WordClass.Prefix : classes?.GetValueOrDefault(number, WordClass.Content) ?? WordClass.Content,
                finite?.Contains(key) == true);

        /// <summary>Whether the text is an edition of an original (any text that is not a translation).</summary>
        public bool Original => original;

        /// <summary>Whether any word of the text has a number to lend.</summary>
        public bool Lends => numbers.Count > 0;

        public bool TryGet(string key, out (string Number, int Hops) lent) => numbers.TryGetValue(key, out lent);

        public int Occurrences(string number) => Counted(number, n => _occurrences.Value.GetValueOrDefault(n));
    }

    /// <summary>A renders or equals link of the text: its words in it, the other text and the other text's words in it.</summary>
    internal sealed record Joined(IReadOnlyList<string> Mine, string Other, IReadOnlyList<string> Theirs, double? Confidence);

    /// <summary>
    /// A word's number: its own (<see cref="Hops"/> 0, no source) or one taken from <see cref="Source"/> (<c>TEXT key</c>) over a
    /// link of <see cref="Confidence"/> (null where stated or ruled). On a word with its own number, <see cref="Linked"/> is the
    /// number a sure link gives it where none of the sure links gives its own, and <see cref="LinkedSource"/> the word lending it.
    /// </summary>
    internal sealed record Numbered(string Key, string Number, int Hops, string? Source, double? Confidence, string? Linked, string? LinkedSource);

    private sealed record Candidate(string Number, int Hops, bool Original, double? Confidence, string Source, int Occurrences)
    {
        public double Sureness => Confidence ?? 1.0;

        public bool Sure => Sureness >= Reliable;
    }

    /// <summary>The numbers of a text's words, in the order the words come; a word that has none and takes none is left out.</summary>
    /// <param name="words">The text's words: own key, own number, and the form a word is told by (folded, lower-cased).</param>
    /// <param name="rules">
    /// For a translation, the group rules (<see cref="GroupNumbers"/>): a word without a number of its own that a link joins to an
    /// original takes only what they give it over those links; the choice above decides only among what they give, and for a word
    /// that reaches translations alone.
    /// </param>
    /// <param name="supplied">The keys of the words the translation marks as its translators' own, which take no number.</param>
    public static List<Numbered> Resolve(
        IEnumerable<(string Key, string? Own, string Form)> words, IEnumerable<Joined> links, IReadOnlyDictionary<string, Lender> lenders,
        GroupNumbers? rules = null, IReadOnlySet<string>? supplied = null)
    {
        var listed = words as IReadOnlyList<(string Key, string? Own, string Form)> ?? [.. words];
        var joined = links as IReadOnlyList<Joined> ?? [.. links];
        var candidates = new Dictionary<string, List<Candidate>>(StringComparer.Ordinal);
        var ruled = rules is null ? null : Ruled(listed, joined, lenders, rules, supplied);
        foreach (var link in joined)
        {
            var lender = lenders[link.Other];
            foreach (var theirs in link.Theirs)
            {
                if (!lender.TryGet(theirs, out var lent))
                {
                    continue;
                }

                var candidate = new Candidate(lent.Number, lent.Hops + 1, lender.Original, link.Confidence, $"{link.Other} {theirs}",
                    lender.Occurrences(lent.Number));
                foreach (var mine in link.Mine)
                {
                    if (!candidates.TryGetValue(mine, out var held))
                    {
                        candidates[mine] = held = [];
                    }

                    held.Add(candidate);
                }
            }
        }

        var usual = new Dictionary<(string Form, string Number), int>();
        foreach (var (key, own, form) in listed)
        {
            var reached = candidates.GetValueOrDefault(key);
            var alone = own ?? (reached is not null && reached.All(c => c.Number == reached[0].Number) ? reached[0].Number : null);
            if (alone is not null)
            {
                usual[(form, alone)] = usual.GetValueOrDefault((form, alone)) + 1;
            }
        }

        var most = usual.GroupBy(u => u.Key.Form).ToDictionary(g => g.Key, g => g.Max(u => u.Value));

        var numbered = new List<Numbered>();
        foreach (var (key, own, form) in listed)
        {
            if (supplied?.Contains(key) == true)
            {
                continue;
            }

            var reached = ruled is not null && own is null && ruled.TryGetValue(key, out var given) ? given : candidates.GetValueOrDefault(key);
            int Usual(Candidate c) =>
                CountedAs.TryGetValue(c.Number, out var with) && usual.GetValueOrDefault((form, with)) == most.GetValueOrDefault(form, -1)
                    ? Counted(c.Number, n => usual.GetValueOrDefault((form, n)))
                    : usual.GetValueOrDefault((form, c.Number));
            if (own is not null)
            {
                var disagreeing = reached is null ? null : Disagreeing(own, reached, Usual);
                numbered.Add(new Numbered(key, own, 0, null, null, disagreeing?.Number, disagreeing?.Source));
            }
            else if (reached is not null && Best(reached, Usual) is { } best)
            {
                numbered.Add(new Numbered(key, best.Number, best.Hops, best.Source, best.Confidence, null, null));
            }
        }

        return numbered;
    }

    /// <summary>The number the sure links give a word that has its own, where none of them gives its own: those of originals first, else of translations.</summary>
    private static Candidate? Disagreeing(string own, List<Candidate> reached, Func<Candidate, int> usual)
    {
        var sure = reached.Where(c => c.Sure).ToList();
        var level = sure.Any(c => c.Original) ? sure.Where(c => c.Original).ToList() : sure;
        return level.Count == 0 || level.Any(c => StrongSynonyms.Same(c.Number, own)) ? null : Best(level, usual);
    }

    /// <summary>
    /// For every word without a number of its own that a link joins to a numbered word of an original, the numbers the group
    /// rules give it over those links (none where they give none).
    /// </summary>
    private static Dictionary<string, List<Candidate>> Ruled(
        IReadOnlyList<(string Key, string? Own, string Form)> words, IReadOnlyList<Joined> links, IReadOnlyDictionary<string, Lender> lenders,
        GroupNumbers rules, IReadOnlySet<string>? supplied)
    {
        var byKey = words.ToDictionary(w => w.Key, w => w, StringComparer.Ordinal);
        var grouped = new List<(Joined Link, Lender Lender, List<GroupNumbers.Translated> Mine, List<GroupNumbers.Original> Theirs)>();
        foreach (var link in links)
        {
            var lender = lenders[link.Other];
            if (!lender.Original)
            {
                continue;
            }

            var theirs = link.Theirs.Select(key => lender.TryGet(key, out var lent) ? lender.Word(key, lent.Number) : (GroupNumbers.Original?)null)
                .OfType<GroupNumbers.Original>().ToList();
            var mine = link.Mine.Where(key => supplied?.Contains(key) != true && byKey.ContainsKey(key))
                .Select(key => new GroupNumbers.Translated(key, byKey[key].Form, byKey[key].Own)).ToList();
            if (theirs.Count == 0 || mine.Count == 0)
            {
                continue;
            }

            grouped.Add((link, lender, mine, theirs));
            if (mine.Count == 1)
            {
                rules.Learn(mine[0].Form, theirs);
            }
        }

        var ruled = new Dictionary<string, List<Candidate>>(StringComparer.Ordinal);
        foreach (var (link, lender, mine, theirs) in grouped)
        {
            var taken = rules.Decide(mine, theirs);
            foreach (var word in mine.Where(w => w.Own is null))
            {
                if (!ruled.TryGetValue(word.Key, out var held))
                {
                    ruled[word.Key] = held = [];
                }

                if (taken.TryGetValue(word.Key, out var original) && lender.TryGet(original.Key, out var lent))
                {
                    held.Add(new Candidate(lent.Number, lent.Hops + 1, true, link.Confidence, $"{link.Other} {original.Key}", lender.Occurrences(lent.Number)));
                }
            }
        }

        return ruled;
    }

    private static Candidate? Best(IEnumerable<Candidate> reached, Func<Candidate, int> usual) =>
        reached
            .OrderByDescending(c => c.Original)
            .ThenByDescending(c => c.Sureness)
            .ThenBy(c => c.Hops)
            .ThenByDescending(usual)
            .ThenBy(c => c.Occurrences)
            .ThenBy(c => c.Number, StringComparer.Ordinal)
            .ThenBy(c => c.Source, StringComparer.Ordinal)
            .FirstOrDefault();

    /// <summary>
    /// The order texts are numbered in: every edition of an original before every translation; within each, the second text of
    /// a pair before the first (a pair names the text it is aligned to second), the pairs taken in ordinal order and one that
    /// would close a cycle left out; texts no pair orders by slug.
    /// </summary>
    public static List<string> Order(IReadOnlyDictionary<string, bool> translations, IEnumerable<(string From, string To)> pairs)
    {
        var after = translations.Keys.ToDictionary(t => t, _ => new SortedSet<string>(StringComparer.Ordinal), StringComparer.Ordinal);
        foreach (var (from, to) in pairs.Where(p => translations.ContainsKey(p.From) && translations.ContainsKey(p.To))
                     .OrderBy(p => p.From, StringComparer.Ordinal).ThenBy(p => p.To, StringComparer.Ordinal))
        {
            if (translations[from] == translations[to] && from != to && !Reaches(after, from, to))
            {
                after[to].Add(from);
            }
        }

        var waiting = translations.Keys.ToDictionary(t => t, _ => 0, StringComparer.Ordinal);
        foreach (var later in after.Values.SelectMany(s => s))
        {
            waiting[later]++;
        }

        var order = new List<string>();
        foreach (var translation in new[] { false, true })
        {
            var ready = new SortedSet<string>(waiting.Keys.Where(t => translations[t] == translation && waiting[t] == 0), StringComparer.Ordinal);
            while (ready.Count > 0)
            {
                var next = ready.Min!;
                ready.Remove(next);
                order.Add(next);
                foreach (var later in after[next].Where(later => --waiting[later] == 0))
                {
                    ready.Add(later);
                }
            }
        }

        return order;
    }

    private static bool Reaches(Dictionary<string, SortedSet<string>> after, string from, string to)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var stack = new Stack<string>([from]);
        while (stack.Count > 0)
        {
            var at = stack.Pop();
            if (at == to)
            {
                return true;
            }

            foreach (var next in after[at].Where(seen.Add))
            {
                stack.Push(next);
            }
        }

        return false;
    }
}
