namespace Essenthos.Core.Loading.Links;

/// <param name="EnglishWord">Index of the English word within its verse, counting from zero.</param>
/// <param name="HebrewPosition">The file's position for the Hebrew morpheme it renders.</param>
internal sealed record PrefixMatch(int EnglishWord, int HebrewPosition, double Confidence);

/// <summary>
/// Gives the English function words their own Hebrew morphemes.
///
/// The mapping file marks content words only. "In the beginning" carries one marker, naming
/// <em>beginning</em>, and the Hebrew בְּ that "In" actually renders is listed in the verse and
/// named by nothing. Handing the whole phrase to the one marked word is what the file's shape
/// invites and it produces a claim the file never made: that "And" renders <em>God</em>, while the
/// וַ it does render is reached by nothing at all. Across the Old Testament that is 144,963 English
/// function words attached to a word they do not translate, and 34,910 Hebrew morphemes left dark.
///
/// So the correspondence is recovered rather than asserted, from what the file itself says: every
/// morpheme carries its Strong number, its position, and the clause it belongs to. Two rules, both
/// conservative — where there is no positive evidence the word stays where it was:
///
/// <list type="number">
/// <item>
/// <b>Adjacency.</b> Hebrew prefixes attach to the front of the word they govern, so the morphemes
/// standing immediately before a marked word and claimed by no marker are that phrase's prefixes,
/// in the same order the English gives them: "and the earth" against וְ־אֵת־הָ־אָרֶץ pairs
/// <em>and</em> with וְ and <em>the</em> with הָ, and leaves <em>earth</em> where the file put it.
/// </item>
/// <item>
/// <b>Clause-initial conjunction.</b> Hebrew hangs its <em>and</em> on the verb and English puts it
/// first, so the two orders disagree exactly once per clause. Genesis 1:3 reads "And God said" over
/// וַ־יֹּאמֶר אֱלֹהִים: walking back from <em>God</em> reaches the verb, not the conjunction. When a
/// clause opens with an English conjunction and its first morpheme is an unclaimed וְ, they are each
/// other's.
/// </item>
/// </list>
///
/// These are inferences, so they are written <c>lexical</c> with a confidence and never as anything
/// the file stated. Adjacency has two independent signals agreeing, position and gloss, and is
/// scored above the clause rule, which has only the one.
/// </summary>
internal static class HebrewPrefixes
{
    /// <summary>Position and gloss both agree, and no other morpheme could be meant.</summary>
    public const double Adjacent = 0.95;

    /// <summary>The clause has an unclaimed conjunction and the English opens with one.</summary>
    public const double ClauseInitial = 0.9;

    /// <summary>
    /// The prefix morphemes, by the Strong number the file gives them, and the English words each
    /// may render. Keyed on the number rather than the gloss because a handful of rows carry the
    /// gloss of the word the prefix is attached to — <c>H9003｜walk</c> — and a number does not
    /// drift.
    /// </summary>
    private static readonly Dictionary<string, string[]> Renders = new(StringComparer.Ordinal)
    {
        ["H9000"] = ["and", "but", "now", "then", "so", "also", "yet"],
        ["H9009"] = ["the"],
        ["H9003"] = ["in", "with", "by", "at", "among", "through", "within"],
        ["H9005"] = ["to", "for", "unto"],
        ["H9004"] = ["as", "like"],
    };

    /// <summary>
    /// Opens a clause in English where Hebrew opens it with a conjunction on the verb. Narrower than
    /// everything וְ can render, because this rule reaches across the clause and the adjacency rule
    /// does not.
    /// </summary>
    private static readonly HashSet<string> Conjunctions =
        new(["and", "but", "now", "then", "so"], StringComparer.OrdinalIgnoreCase);

    /// <summary>The object marker אֵת is never rendered, so a prefix search reads straight past it.</summary>
    private const string ObjectMarker = "H853";

    public static IReadOnlyList<PrefixMatch> Match(
        IReadOnlyList<HebrewEntry> hebrew,
        IReadOnlyList<EnglishSegment> segments)
    {
        var byPosition = new Dictionary<int, HebrewEntry>(hebrew.Count);
        foreach (var entry in hebrew)
        {
            byPosition.TryAdd(entry.Position, entry);
        }

        var claimed = new HashSet<int>(segments.Count);
        foreach (var segment in segments)
        {
            if (segment.RendersHebrew is not null)
            {
                claimed.Add(segment.RendersHebrew.Position);
            }
        }

        var matches = new List<PrefixMatch>();
        var taken = new HashSet<int>();
        var first = 0;

        foreach (var segment in segments)
        {
            var start = first;
            first += segment.Words.Count;

            if (segment.RendersHebrew is null || segment.Words.Count < 2)
            {
                continue;
            }

            var prefixes = Preceding(byPosition, claimed, taken, segment.RendersHebrew.Position);
            if (prefixes.Count == 0)
            {
                continue;
            }

            var at = 0;
            for (var i = 0; i < segment.Words.Count - 1 && at < prefixes.Count; i++)
            {
                var word = segment.Words[i].Text;
                if (!Renders.Values.Any(words => words.Contains(word, StringComparer.OrdinalIgnoreCase)))
                {
                    // The run of function words the phrase opens with has ended. Anything further in
                    // is the phrase's own content, and past it the order stops being parallel.
                    break;
                }

                var found = prefixes.FindIndex(at, position => Accepts(byPosition[position], word));
                if (found < 0)
                {
                    // English supplies an article the Hebrew does not have. It renders nothing, and
                    // stays with the phrase rather than being given the next prefix along.
                    continue;
                }

                matches.Add(new PrefixMatch(start + i, prefixes[found], Adjacent));
                taken.Add(prefixes[found]);
                at = found + 1;
            }
        }

        matches.AddRange(ClauseOpenings(hebrew, segments, claimed, taken, matches));
        return matches;
    }

    /// <summary>
    /// The unclaimed prefix morphemes standing immediately before a marked word, in the order the
    /// English would give them. The walk stops at the first morpheme that is neither a prefix nor
    /// the object marker, because beyond that is another phrase's Hebrew.
    /// </summary>
    private static List<int> Preceding(
        Dictionary<int, HebrewEntry> byPosition,
        HashSet<int> claimed,
        HashSet<int> taken,
        int position)
    {
        var prefixes = new List<int>(3);

        for (var at = position - 1; byPosition.TryGetValue(at, out var entry); at--)
        {
            if (claimed.Contains(at) || taken.Contains(at))
            {
                break;
            }

            if (entry.Strong == ObjectMarker)
            {
                continue;
            }

            if (!Renders.ContainsKey(entry.Strong))
            {
                break;
            }

            prefixes.Insert(0, at);
        }

        return prefixes;
    }

    /// <summary>
    /// The conjunction the adjacency rule cannot reach, because Hebrew puts it on the verb and
    /// English puts it first. One per clause at most, and only where the clause opens with one on
    /// both sides.
    /// </summary>
    private static List<PrefixMatch> ClauseOpenings(
        IReadOnlyList<HebrewEntry> hebrew,
        IReadOnlyList<EnglishSegment> segments,
        HashSet<int> claimed,
        HashSet<int> taken,
        List<PrefixMatch> already)
    {
        var opening = new Dictionary<string, int>(8);
        foreach (var entry in hebrew)
        {
            if (entry.Strong == "H9000" && !claimed.Contains(entry.Position) && !taken.Contains(entry.Position)
                && (!opening.TryGetValue(entry.Clause, out var standing) || entry.Position < standing))
            {
                opening[entry.Clause] = entry.Position;
            }
        }

        var matched = new List<PrefixMatch>();
        var seen = new HashSet<string>(8);
        var assigned = new HashSet<int>(already.Select(match => match.EnglishWord));
        var first = 0;

        foreach (var segment in segments)
        {
            var start = first;
            first += segment.Words.Count;

            if (segment.RendersHebrew is null || segment.Words.Count < 2
                || !seen.Add(segment.RendersHebrew.Clause)
                || !opening.TryGetValue(segment.RendersHebrew.Clause, out var conjunction)
                || assigned.Contains(start)
                || !Conjunctions.Contains(segment.Words[0].Text))
            {
                continue;
            }

            matched.Add(new PrefixMatch(start, conjunction, ClauseInitial));
            taken.Add(conjunction);
        }

        return matched;
    }

    private static bool Accepts(HebrewEntry entry, string word) =>
        Renders.TryGetValue(entry.Strong, out var words)
        && words.Contains(word, StringComparer.OrdinalIgnoreCase);
}
