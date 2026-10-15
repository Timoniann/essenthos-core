using Essenthos.Core.Database.Entities.Enums;

namespace Essenthos.Core.Loading.Links;

/// <summary>What says a translation's word names the same person, place or people as a word of the original.</summary>
[Flags]
internal enum NameSignal
{
    None = 0,

    /// <summary>The text's verses say the word names that entity (<see cref="Encyclopedia.NameConsensus"/>).</summary>
    Entity = 1,

    /// <summary>Its consonants are the original word's (<see cref="NameLetters"/>).</summary>
    Letters = 2,

    /// <summary>
    /// The text spells the entity this way in other verses, where nothing else could be meant: the
    /// verse names it alone on both sides, or the letters are all but the same.
    /// </summary>
    Spelling = 4,
}

/// <summary>A word of the translation, as the names are placed.</summary>
/// <param name="Letters">Its consonants, one reading or several (<see cref="NameLetters.Of"/>); none in a script we cannot read.</param>
/// <param name="Form">Its form made one across inflections (<see cref="NameLetters.Form"/>).</param>
/// <param name="Families">The entities the text's verses say it names, each by its family (<see cref="NameFamilies"/>).</param>
/// <param name="Marked">Whether the text writes it with a capital inside its verse.</param>
/// <param name="Named">
/// Whether the text marks it as a name and nothing else: a capital it writes nowhere in lower case.
/// <em>Son</em> and <em>Senhor</em> are capitalised and also written in lower case.
/// </param>
internal sealed record NameSourceWord(
    long Id,
    string Surface,
    IReadOnlyList<string> Letters,
    string Form,
    IReadOnlySet<int> Families,
    bool Marked,
    bool Named = false);

/// <summary>A word of the original annotated as naming a person, a place or a people.</summary>
/// <param name="Index">Where it stands among the words of its verse, from 0.</param>
/// <param name="Letters">Its consonants, as <see cref="NameLists.Skeleton(string?, string?)"/> reads them.</param>
/// <param name="Families">The entities its annotations name, each by its family.</param>
/// <param name="Spelling">
/// How the original writes the name here, made one across its inflections: a text's spelling is learned
/// for the entity as the original writes it, since one entity can be written with words the
/// translation renders differently — the LORD for יְהוָה, God for אֱלֹהִים, both the one God.
/// </param>
/// <param name="Proper">
/// Whether the word is a name: a proper noun or a gentilic. A common noun annotated with whom it means —
/// the בְּנֵי of the sons of Israel, who are the people — is weighed with the names, so that it keeps the
/// word that renders it from being learned as the father's spelling, and is never placed: it is
/// translated, not written by its letters, and belongs to the other steps.
/// </param>
internal sealed record NameTargetWord(
    long Id, int Index, string Letters, IReadOnlySet<int> Families, EntityKind Kind, string Spelling = "", bool Proper = true);

/// <param name="Source">Every word of the translation's verse, in order.</param>
/// <param name="Targets">The original's words that name someone, in order.</param>
/// <param name="TargetLength">How many words the original's verse has.</param>
internal sealed record NameVerse(
    (int Book, int Chapter, int Verse) Address,
    IReadOnlyList<NameSourceWord> Source,
    IReadOnlyList<NameTargetWord> Targets,
    int TargetLength);

/// <param name="Signals">Which signals name the pair.</param>
/// <param name="Likeness">How alike the two words' consonants are, 0 where either script has none we can read.</param>
/// <param name="Uneven">
/// The entity is named a different number of times on the two sides of the verse, so its order alone
/// cannot say which mention is which.
/// </param>
/// <param name="Clear">
/// The letters are all but the same (<see cref="NamePlacement.ClearLikeness"/>), and neither word is
/// like any other name of the verse: a name read once in the whole text, Bera king of Sodom, has no
/// other mention to be learned from, and this is what its letters alone can say.
/// </param>
/// <param name="Beside">
/// Placed as the word beside a placed pair naming the same entity: one name written as two words on
/// one side, בֵּית לֶחֶם against Bethlehem.
/// </param>
internal sealed record NamePlacing(
    (int Book, int Chapter, int Verse) Address,
    long Source,
    long Target,
    NameSignal Signals,
    double Likeness,
    EntityKind Kind,
    bool Uneven,
    bool Beside,
    bool Clear = false,
    bool Repeated = false)
{
    public int Count => ((int)Signals & 1) + (((int)Signals >> 1) & 1) + (((int)Signals >> 2) & 1);
}

/// <summary>
/// Each name of the original placed on the word of the translation that writes it, by three signals
/// that do not depend on each other's mistakes: whom the text's verses say the word names, the
/// word's consonants, and how the text spells that entity where nothing else in the verse could be
/// meant.
///
/// <para>
/// The spelling is learned from the text itself. Where a verse of the original names one entity
/// and the translation's verse has one word that can be a name, that word is the text's spelling
/// of it; so is a word whose consonants are all but the original's and no other's. Those forms are
/// then looked for in the verses where several names stand, which is where the letters alone go
/// wrong — the Synodal's Иисус against Joshua, a Korean name with a particle on it, a Chinese name
/// with no letters at all.
/// </para>
///
/// <para>
/// Within a verse every pair the signals name is weighed at once and each word placed once: the
/// most signals first, then the closest letters, then the mention in the same order when an entity
/// is named more than once, then the nearest place in the verse.
/// </para>
/// </summary>
internal static class NamePlacement
{
    /// <summary>The fewest consonants a name needs for its letters to count where the verses do not say whom the word names.</summary>
    public const int LongName = 3;

    /// <summary>Letters this alike are the original's name, read on their own, for learning a spelling.</summary>
    public const double ClearLikeness = 0.8;

    /// <summary>
    /// The share of a form's learned verses that must be one entity's before the form is that
    /// entity's spelling: <em>Israel</em> is the man in Genesis and the people after it.
    /// </summary>
    public const double SpellingShare = 2.0 / 3;

    public static IReadOnlyList<NamePlacing> Place(IReadOnlyList<NameVerse> verses)
    {
        var learned = Learn(verses);
        var placings = new List<NamePlacing>();
        for (var at = 0; at < verses.Count; at++)
        {
            placings.AddRange(Settle(verses[at], at, learned));
        }

        return placings;
    }

    /// <summary>
    /// The text's own spelling of each entity: by form, the entities it was the one candidate for and
    /// in which verses.
    /// </summary>
    internal static Dictionary<string, Dictionary<(int Family, string Spelling), HashSet<int>>> Learn(IReadOnlyList<NameVerse> verses)
    {
        var learned = new Dictionary<string, Dictionary<(int Family, string Spelling), HashSet<int>>>(StringComparer.Ordinal);
        void Add(string form, IEnumerable<int> families, string spelling, int verse)
        {
            if (form.Length == 0)
            {
                return;
            }

            if (!learned.TryGetValue(form, out var byFamily))
            {
                learned[form] = byFamily = [];
            }

            foreach (var family in families)
            {
                if (!byFamily.TryGetValue((family, spelling), out var where))
                {
                    byFamily[(family, spelling)] = where = [];
                }

                where.Add(verse);
            }
        }

        for (var at = 0; at < verses.Count; at++)
        {
            var verse = verses[at];
            if (verse.Targets.Count == 0)
            {
                continue;
            }

            var families = verse.Targets.SelectMany(target => target.Families).ToHashSet();
            var one = verse.Targets.All(target => target.Families.Overlaps(verse.Targets[0].Families))
                ? verse.Targets[0].Families.Where(family => verse.Targets.All(target => target.Families.Contains(family))).ToList()
                : [];
            if (one.Count > 0)
            {
                var candidates = verse.Source
                    .Where(word => word.Marked
                                   || word.Families.Overlaps(families)
                                   || verse.Targets.Any(target => Likeness(word, target) >= NameLists.LeastLikeness))
                    .ToList();
                if (candidates.Count == 1)
                {
                    foreach (var spelling in verse.Targets.Select(target => target.Spelling).Distinct())
                    {
                        Add(candidates[0].Form, one, spelling, at);
                    }

                    continue;
                }
            }

            foreach (var target in verse.Targets)
            {
                var alike = verse.Source.Where(word => Likeness(word, target) >= NameLists.LeastLikeness).ToList();
                if (alike.Count == 1 && Likeness(alike[0], target) >= ClearLikeness
                    && verse.Targets.Count(other => Likeness(alike[0], other) >= NameLists.LeastLikeness) == 1)
                {
                    Add(alike[0].Form, target.Families, target.Spelling, at);
                }
            }
        }

        return learned;
    }

    /// <summary>
    /// How alike the two words' consonants are, where the translation's word may be a name. In a script
    /// without capitals nothing says a word is a name, and a common word shares two thirds of a name's
    /// consonants often enough — the Bengali সেখানে <em>there</em> is SKN beside Ἰησοῦν — so there the
    /// letters of a word its verses do not name count only when they are all but the same and three
    /// consonants or more: তাঁর <em>his</em> is Tyre's TR. In a script with capitals a name of fewer than
    /// three consonants counts by its letters only where the verses say whom the word names or the text
    /// writes it as nothing but a name: Ἰησοῦν is SN, and so are <em>Son</em>, the Portuguese
    /// <em>Senhor</em> and the Vietnamese <em>Sáng</em>, each also written in lower case.
    /// </summary>
    private static double Likeness(NameSourceWord word, NameTargetWord target)
    {
        if (!MayBeName(word) || word.Letters.Count == 0 || target.Letters.Length == 0)
        {
            return 0;
        }

        var likeness = NameLetters.Likeness(word.Letters, target.Letters, word.Surface);
        if (word.Families.Count > 0)
        {
            return likeness;
        }

        var longName = target.Letters.Length >= LongName && word.Letters.All(letters => letters.Length >= LongName);
        return Cased(word) ? longName || word.Named ? likeness : 0
            : longName && likeness >= ClearLikeness ? likeness : 0;
    }

    /// <summary>Whether the text spells the entity, as the original writes it here, so in some verse other than this one.</summary>
    private static bool Spelt(
        Dictionary<string, Dictionary<(int Family, string Spelling), HashSet<int>>> learned, string form, NameTargetWord target, int verse)
    {
        if (form.Length == 0 || !learned.TryGetValue(form, out var byFamily))
        {
            return false;
        }

        var elsewhere = byFamily.ToDictionary(pair => pair.Key, pair => pair.Value.Count(at => at != verse));
        var total = elsewhere.Values.Sum();
        return total > 0 && target.Families.Any(family =>
            elsewhere.TryGetValue((family, target.Spelling), out var count) && count > 0 && count >= SpellingShare * total);
    }

    private sealed record Candidate(
        int Source, int Target, NameSignal Signals, double Likeness, int Count, bool SameRank, double Distance, bool Clear);

    internal static IReadOnlyList<NamePlacing> Settle(
        NameVerse verse,
        int at,
        Dictionary<string, Dictionary<(int Family, string Spelling), HashSet<int>>> learned)
    {
        if (verse.Targets.Count == 0 || verse.Source.Count == 0)
        {
            return [];
        }

        var candidates = new List<Candidate>();
        for (var t = 0; t < verse.Targets.Count; t++)
        {
            var target = verse.Targets[t];
            var namingTargets = Enumerable.Range(0, verse.Targets.Count)
                .Where(other => verse.Targets[other].Families.Overlaps(target.Families))
                .ToList();
            var namingSources = Enumerable.Range(0, verse.Source.Count)
                .Where(word => verse.Source[word].Families.Overlaps(target.Families))
                .ToList();
            for (var s = 0; s < verse.Source.Count; s++)
            {
                var word = verse.Source[s];

                // The verses say this word names someone else: no likeness of letters outweighs that.
                // The Israelites are the people, and the יִשְׂרָאֵל of בְּנֵי יִשְׂרָאֵל is their father.
                if (word.Families.Count > 0 && !word.Families.Overlaps(target.Families))
                {
                    continue;
                }

                var signals = NameSignal.None;
                if (word.Families.Overlaps(target.Families))
                {
                    signals |= NameSignal.Entity;
                }

                var likeness = Likeness(word, target);
                if (likeness >= NameLists.LeastLikeness)
                {
                    signals |= NameSignal.Letters;
                }

                if (Spelt(learned, word.Form, target, at))
                {
                    signals |= NameSignal.Spelling;
                }

                if (signals == NameSignal.None)
                {
                    continue;
                }

                var sameRank = namingSources.IndexOf(s) is >= 0 and var sourceRank && sourceRank == namingTargets.IndexOf(t);
                var distance = Math.Abs((s + 0.5) / verse.Source.Count - (target.Index + 0.5) / Math.Max(1, verse.TargetLength));
                var clear = likeness >= ClearLikeness && Cased(word)
                            && verse.Targets.Count(other => other.Proper && Likeness(word, other) >= NameLists.LeastLikeness) == 1
                            && verse.Source.Count(other => Likeness(other, target) >= NameLists.LeastLikeness) == 1;
                candidates.Add(new Candidate(s, t, signals, likeness, Count(signals), sameRank, distance, clear));
            }
        }

        var usedSource = new HashSet<int>();
        var usedTarget = new HashSet<int>();
        var placed = new List<(int Source, int Target, Candidate Candidate)>();
        foreach (var candidate in candidates
                     .OrderByDescending(candidate => candidate.Count)
                     .ThenByDescending(candidate => candidate.Likeness)
                     .ThenByDescending(candidate => candidate.SameRank)
                     .ThenBy(candidate => candidate.Distance))
        {
            if (usedSource.Contains(candidate.Source) || usedTarget.Contains(candidate.Target))
            {
                continue;
            }

            usedSource.Add(candidate.Source);
            usedTarget.Add(candidate.Target);
            placed.Add((candidate.Source, candidate.Target, candidate));
        }

        var placings = new List<NamePlacing>(placed.Count);
        foreach (var (s, t, candidate) in placed.Where(pair => verse.Targets[pair.Target].Proper))
        {
            var target = verse.Targets[t];
            placings.Add(new NamePlacing(verse.Address, verse.Source[s].Id, target.Id, candidate.Signals, candidate.Likeness,
                target.Kind, Uneven(verse, target), Beside: false, candidate.Clear, Repeated(verse, target)));
        }

        foreach (var (s, t, candidate) in placed.Where(pair => verse.Targets[pair.Target].Proper).ToList())
        {
            var target = verse.Targets[t];
            foreach (var other in new[] { t - 1, t + 1 })
            {
                if (other < 0 || other >= verse.Targets.Count || usedTarget.Contains(other) || !verse.Targets[other].Proper
                    || Math.Abs(verse.Targets[other].Index - target.Index) != 1
                    || !verse.Targets[other].Families.Overlaps(target.Families)
                    || !Spans(verse.Source[s], other < t ? verse.Targets[other] : target, other < t ? target : verse.Targets[other], candidate.Likeness))
                {
                    continue;
                }

                usedTarget.Add(other);
                placings.Add(new NamePlacing(verse.Address, verse.Source[s].Id, verse.Targets[other].Id, candidate.Signals,
                    candidate.Likeness, verse.Targets[other].Kind, Uneven(verse, target), Beside: true));
            }

            foreach (var other in new[] { s - 1, s + 1 })
            {
                if (other < 0 || other >= verse.Source.Count || usedSource.Contains(other)
                    || !verse.Source[other].Families.Overlaps(target.Families)
                    || verse.Source.Count(word => word.Families.Overlaps(target.Families)) > verse.Targets.Count(name => name.Families.Overlaps(target.Families)) + 1)
                {
                    continue;
                }

                usedSource.Add(other);
                placings.Add(new NamePlacing(verse.Address, verse.Source[other].Id, target.Id, candidate.Signals,
                    candidate.Likeness, target.Kind, Uneven(verse, target), Beside: true));
            }
        }

        return placings;
    }

    /// <summary>
    /// Whether one word of the translation writes both of two words of the original: its letters are
    /// more like the two together than like the one it was placed on. Bethlehem is בֵּית and לֶחֶם; the
    /// <em>Israel</em> of <em>the God of Israel</em> is not אֱלֹהֵי as well.
    /// </summary>
    private static bool Spans(NameSourceWord word, NameTargetWord first, NameTargetWord second, double likeness) =>
        word.Letters.Count > 0 && first.Letters.Length > 0 && second.Letters.Length > 0
        && NameLetters.Likeness(word.Letters, first.Letters + second.Letters, word.Surface) > likeness;

    /// <summary>
    /// Whether a word may be a name at all, for its letters to count: in a script with capitals, one the
    /// text marks so or its verses say names someone; in a script without, any word. A lower-case
    /// <em>worn</em> is Aaron's RN by its letters and nothing else.
    /// </summary>
    private static bool MayBeName(NameSourceWord word) => word.Marked || word.Families.Count > 0 || !Cased(word);

    /// <summary>Whether a word is written in a script with capitals, where a name says so by one.</summary>
    internal static bool Cased(NameSourceWord word) => word.Surface.Any(letter => char.ToUpperInvariant(letter) != char.ToLowerInvariant(letter));

    private static int Count(NameSignal signals) =>
        ((int)signals & 1) + (((int)signals >> 1) & 1) + (((int)signals >> 2) & 1);

    /// <summary>
    /// Whether the entity is named more than once on either side and not as often on both, so which
    /// mention is which is not given by their order.
    /// </summary>
    /// <summary>Whether the entity is named more than once in the verse on either side.</summary>
    private static bool Repeated(NameVerse verse, NameTargetWord target) =>
        verse.Targets.Count(other => other.Families.Overlaps(target.Families)) > 1
        || verse.Source.Count(word => word.Families.Overlaps(target.Families)) > 1;

    private static bool Uneven(NameVerse verse, NameTargetWord target)
    {
        var source = verse.Source.Count(word => word.Families.Overlaps(target.Families));
        var original = verse.Targets.Count(other => other.Families.Overlaps(target.Families));
        return (source > 1 || original > 1) && source != original;
    }
}

/// <summary>
/// An entity and the entities recorded as another name for it, as one: Senir is Mount Hermon, Put is
/// Libya. A word annotated to one and a word annotated to the other name the same thing.
/// </summary>
internal sealed class NameFamilies
{
    private readonly Dictionary<int, int> _parent = [];

    public NameFamilies(IEnumerable<(int Entity, int AnotherNameFor)> aliases)
    {
        foreach (var (entity, other) in aliases)
        {
            var one = Of(entity);
            var two = Of(other);
            if (one != two)
            {
                _parent[Math.Max(one, two)] = Math.Min(one, two);
            }
        }
    }

    public int Of(int entity)
    {
        var root = entity;
        while (_parent.TryGetValue(root, out var parent))
        {
            root = parent;
        }

        return root;
    }

    public IReadOnlySet<int> Of(IEnumerable<int> entities) => entities.Select(Of).ToHashSet();
}
