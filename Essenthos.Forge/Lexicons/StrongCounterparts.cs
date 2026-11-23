using Essenthos.Core.Strong;

namespace Essenthos.Core.Lexicons;

/// <summary>
/// One Hebrew number and one Greek number that stand for the same word: the Septuagint renders the one by the other, or
/// Strong's Greek dictionary says the Greek word comes from the Hebrew.
/// </summary>
/// <param name="Septuagint">The Hebrew words of <paramref name="Hebrew"/> whose Septuagint link holds a word of <paramref name="Greek"/>.</param>
/// <param name="GreekWords">The Greek words of <paramref name="Greek"/> whose link holds a word of <paramref name="Hebrew"/>.</param>
/// <param name="HebrewLinked">The Hebrew words of <paramref name="Hebrew"/> that are paired with any Greek number at all.</param>
/// <param name="GreekLinked">The Greek words of <paramref name="Greek"/> that are paired with any Hebrew number at all.</param>
/// <param name="Stated">Of <paramref name="Septuagint"/>, the Hebrew words whose link a source states (CATSS), not a model.</param>
/// <param name="Dictionary">Strong's relation between the two (<c>loan-from</c>, <c>corresponds-to</c>), null where he states none.</param>
/// <param name="Statement">Strong's own clause for <paramref name="Dictionary"/>.</param>
internal sealed record StrongCounterpartRow(
    string Hebrew,
    string Greek,
    int Septuagint,
    int GreekWords,
    int HebrewLinked,
    int GreekLinked,
    int Stated,
    string? Dictionary,
    string? Statement);

/// <summary>
/// The Greek words that stand for a Hebrew word and back, counted from the settled links of the Septuagint to the Hebrew and
/// from Strong's own derivations, never guessed.
///
/// <para>
/// A pair of words is counted only where a link joins exactly one content number on each side, among the words that link is
/// the narrowest link of: CATSS states phrases over phrases (ἐν ἀρχῇ over בְּ רֵאשִׁית), and the one-to-one links inside them
/// say which word renders which, so a word is read in its narrowest link only, and a phrase whose content words cannot be
/// told apart counts for nothing. Prepositions, conjunctions, articles, pronouns and particles are left out on both sides, by
/// the part of speech most of a number's words have in BHSA and Nestle 1904.
/// </para>
///
/// <para>
/// A pairing is kept where it is not a one-off and is a real share of what either word is rendered by: at least
/// <see cref="Least"/> Hebrew words, and at least <see cref="Share"/> of the Hebrew number's paired words or of the Greek
/// number's, or at least <see cref="Always"/> Hebrew words whatever the share (θεός for יְהוָה 177 times is a rendering though
/// it is 3% of each). A pairing Strong's dictionary states is kept whatever its count.
/// </para>
/// </summary>
internal static class StrongCounterparts
{
    public const int Least = 2;

    public const double Share = 0.10;

    public const int Always = 20;

    /// <summary>Strong's relations from a Greek entry to a Hebrew one that say the two are one word.</summary>
    public static readonly IReadOnlySet<string> SameWord = new HashSet<string>(
        [StrongRelationKinds.LoanFrom, StrongRelationKinds.CorrespondsTo], StringComparer.Ordinal);

    /// <summary>A settled link of the Septuagint to the Hebrew: its Greek words and its Hebrew words by own key, and whether a source states it.</summary>
    public sealed record Link(IReadOnlyList<string> Greek, IReadOnlyList<string> Hebrew, bool Stated);

    /// <summary>A relation Strong's dictionary states between two entries, as <c>strong_relation</c> holds it.</summary>
    public sealed record Relation(string From, string To, string Kind, string Statement);

    /// <summary>
    /// Every pairing kept, Hebrew number then Greek number in ordinal order.
    /// </summary>
    /// <param name="greek">The Greek number of each word of the Septuagint, by own key; a word whose lemma reaches no number, or several, is absent.</param>
    /// <param name="hebrew">The Strong number of each Hebrew word, by own key.</param>
    /// <param name="content">Whether a number is a content word's.</param>
    public static List<StrongCounterpartRow> Count(
        IReadOnlyList<Link> links,
        IReadOnlyDictionary<string, string> greek,
        IReadOnlyDictionary<string, string> hebrew,
        Func<string, bool> content,
        IEnumerable<Relation> relations)
    {
        var narrowestGreek = new Dictionary<string, int>(StringComparer.Ordinal);
        var narrowestHebrew = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var link in links)
        {
            var size = link.Greek.Count + link.Hebrew.Count;
            Narrow(narrowestGreek, link.Greek, size);
            Narrow(narrowestHebrew, link.Hebrew, size);
        }

        var hebrewWords = new Dictionary<(string Hebrew, string Greek), HashSet<string>>();
        var greekWords = new Dictionary<(string Hebrew, string Greek), HashSet<string>>();
        var stated = new Dictionary<(string Hebrew, string Greek), HashSet<string>>();
        foreach (var link in links)
        {
            var size = link.Greek.Count + link.Hebrew.Count;
            var greekSide = Read(link.Greek, greek, narrowestGreek, size, content);
            var hebrewSide = Read(link.Hebrew, hebrew, narrowestHebrew, size, content);
            if (greekSide.Count == 0 || hebrewSide.Count == 0
                || greekSide.Select(w => w.Number).Distinct().Count() != 1 || hebrewSide.Select(w => w.Number).Distinct().Count() != 1)
            {
                continue;
            }

            var pair = (hebrewSide[0].Number, greekSide[0].Number);
            Add(hebrewWords, pair, hebrewSide.Select(w => w.Key));
            Add(greekWords, pair, greekSide.Select(w => w.Key));
            if (link.Stated)
            {
                Add(stated, pair, hebrewSide.Select(w => w.Key));
            }
        }

        var hebrewLinked = Totals(hebrewWords, pair => pair.Hebrew);
        var greekLinked = Totals(greekWords, pair => pair.Greek);
        var said = new Dictionary<(string Hebrew, string Greek), Relation>();
        foreach (var relation in relations.Where(r => SameWord.Contains(r.Kind)))
        {
            var pair = relation.From.StartsWith(StrongNumbers.Greek) && relation.To.StartsWith(StrongNumbers.Hebrew)
                ? (relation.To, relation.From)
                : relation.From.StartsWith(StrongNumbers.Hebrew) && relation.To.StartsWith(StrongNumbers.Greek)
                    ? (relation.From, relation.To)
                    : default;
            if (pair != default)
            {
                said.TryAdd(pair, relation);
            }
        }

        var kept = new List<StrongCounterpartRow>();
        foreach (var pair in hebrewWords.Keys.Union(said.Keys))
        {
            var septuagint = hebrewWords.GetValueOrDefault(pair)?.Count ?? 0;
            var inGreek = greekWords.GetValueOrDefault(pair)?.Count ?? 0;
            var ofHebrew = hebrewLinked.GetValueOrDefault(pair.Hebrew);
            var ofGreek = greekLinked.GetValueOrDefault(pair.Greek);
            var relation = said.GetValueOrDefault(pair);
            if (relation is null && !Kept(septuagint, inGreek, ofHebrew, ofGreek))
            {
                continue;
            }

            kept.Add(new StrongCounterpartRow(pair.Hebrew, pair.Greek, septuagint, inGreek, ofHebrew, ofGreek,
                stated.GetValueOrDefault(pair)?.Count ?? 0, relation?.Kind, relation?.Statement));
        }

        return
        [
            .. kept.OrderBy(c => c.Hebrew, StringComparer.Ordinal).ThenBy(c => c.Greek, StringComparer.Ordinal),
        ];
    }

    /// <summary>Whether a pairing counted in the Septuagint is a rendering rather than noise.</summary>
    public static bool Kept(int septuagint, int greekWords, int hebrewLinked, int greekLinked) =>
        septuagint >= Least
        && (septuagint >= Always
            || (hebrewLinked > 0 && septuagint >= Share * hebrewLinked)
            || (greekLinked > 0 && greekWords >= Share * greekLinked));

    private static void Narrow(Dictionary<string, int> narrowest, IEnumerable<string> words, int size)
    {
        foreach (var word in words)
        {
            if (!narrowest.TryGetValue(word, out var known) || size < known)
            {
                narrowest[word] = size;
            }
        }
    }

    private static List<(string Key, string Number)> Read(
        IEnumerable<string> words, IReadOnlyDictionary<string, string> numbers, Dictionary<string, int> narrowest, int size, Func<string, bool> content) =>
    [
        .. words
            .Where(word => narrowest[word] == size)
            .Select(word => (Key: word, Number: numbers.GetValueOrDefault(word)))
            .Where(word => word.Number is not null && content(word.Number))
            .Select(word => (word.Key, word.Number!)),
    ];

    private static void Add(Dictionary<(string, string), HashSet<string>> counted, (string, string) pair, IEnumerable<string> words)
    {
        if (!counted.TryGetValue(pair, out var held))
        {
            counted[pair] = held = new HashSet<string>(StringComparer.Ordinal);
        }

        held.UnionWith(words);
    }

    private static Dictionary<string, int> Totals(
        Dictionary<(string Hebrew, string Greek), HashSet<string>> counted, Func<(string Hebrew, string Greek), string> side)
    {
        var words = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var (pair, held) in counted)
        {
            if (!words.TryGetValue(side(pair), out var all))
            {
                words[side(pair)] = all = new HashSet<string>(StringComparer.Ordinal);
            }

            all.UnionWith(held);
        }

        return words.ToDictionary(w => w.Key, w => w.Value.Count, StringComparer.Ordinal);
    }
}
