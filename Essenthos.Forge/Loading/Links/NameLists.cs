using System.Globalization;
using System.Text;

namespace Essenthos.Core.Loading.Links;

/// <summary>
/// The proper names of a verse paired by how they are spelt and the order they stand in.
///
/// <para>
/// A statistical aligner has nothing to go on in a list of names. Gad's seven sons in Genesis 46:16
/// are seven words the model has never seen translated, all co-occurring in one verse, and the
/// position it falls back on is spoiled wherever a witness drops or adds a name: Brenton's
/// Θασοβάν was put against Arodi, two names late, by two readings at 0.98. Yet a transliterated name
/// is the one word whose rendering can be read off its letters — Θασοβάν is אֶצְבֹּן consonant for
/// consonant, and nothing else in that verse is.
/// </para>
///
/// <para>
/// So every name is reduced to the consonants every script here writes alike, and the names on both
/// sides are paired to keep their order and the most letters in common. What the pairing settles it
/// says: a name the model put against a different name of the list is refused, and the pair the
/// letters and the order agree on is proposed in its place. A name nobody can read off its letters
/// — Egypt for מִצְרַיִם — is matched to nothing, and the model's answer for it stands.
/// </para>
/// </summary>
internal static class NameLists
{
    /// <summary>
    /// How much of two names' consonants must agree before they are taken for one name: twice the
    /// longest run in common over both lengths. Θασοβάν against אֶצְבֹּן is 0.86, Σαφών against
    /// צִפְיוֹן 1.0, and Ἀηδείς against עֵרִי nothing at all — the Septuagint read a dalet there.
    /// </summary>
    public const double LeastLikeness = 0.6;

    /// <summary>
    /// What a pair settled by spelling and order is worth. Measured against the eleven pairs of
    /// texts where another method already links the names — the King James against BHSA and Nestle
    /// as its sources state them; the Synodal, Luther and the Reina-Valera against BHSA and Nestle,
    /// and the Synodal against Scrivener, by their printed Strong numbers; the Ukrainian against BHSA
    /// and Nestle by its interlinear — 80,418 of the 81,685 pairs it settles are the pairs that method
    /// links: 98.4%, and no pair of texts below the Reina-Valera's 94.4% against Nestle. Greek against
    /// Hebrew has nothing to be measured against and is the hardest of them, so the figure is set
    /// below the lowest measured.
    /// </summary>
    public const double Settled = 0.9;

    /// <summary>
    /// What the model's own proposal adds to a pair of names, enough to decide between two targets
    /// spelt alike and too little to outweigh a letter.
    /// </summary>
    private const double Proposed = 0.01;

    /// <summary>
    /// What a pair has to share beyond chance to be worth pairing at all, taken off every pair before
    /// the pairs are summed. Without it two weak pairs outweigh one exact one: in 1 Chronicles 6:78
    /// the Ukrainian's Єрихоні is two thirds of the second יַרְדֵּן, and taking it pushed the second
    /// Йордану onto Reuben.
    /// </summary>
    private const double Margin = 0.5;

    /// <summary>
    /// The consonants of a name as a string every script here spells alike. Vowels, the gutturals
    /// that Greek and English drop and Hebrew writes, and the letters that stand for vowels as often
    /// as not (י ו, j y w) are dropped; the sibilants are one letter, as are ph and f, th and t, ch
    /// and k, and b and v, which in a name is nearly always a ב — Рувим, Levi's ו notwithstanding. A
    /// letter doubled is one.
    /// </summary>
    public static string Skeleton(string? written) => Skeleton(written, null);

    /// <summary>
    /// <see cref="Skeleton(string?)"/> of a word in its language. A Russian or Ukrainian name is
    /// reduced to its stem first, because it declines and its endings are consonants: the Ukrainian
    /// Зузів is Zuzim in the genitive, and its -ів is not a letter of the name.
    /// </summary>
    public static string Skeleton(string? written, string? language)
    {
        if (language is "rus" or "ukr" && !string.IsNullOrEmpty(written))
        {
            written = SlavicStemmer.Stem(written, isName: true);
        }

        // Ethiopic letters are syllables, so a name is compared by the consonants it writes in Latin.
        if (language is "gez" && !string.IsNullOrEmpty(written))
        {
            written = GeezStemmer.Transliterated(written);
        }

        if (string.IsNullOrWhiteSpace(written))
        {
            return string.Empty;
        }

        var letters = Plain(written);
        var skeleton = new StringBuilder(letters.Length);

        for (var at = 0; at < letters.Length; at++)
        {
            var (sound, used) = Sound(letters, at);
            at += used - 1;
            foreach (var letter in sound)
            {
                if (skeleton.Length == 0 || skeleton[^1] != letter)
                {
                    skeleton.Append(letter);
                }
            }
        }

        return skeleton.ToString();
    }

    /// <summary>
    /// <see cref="Likeness"/>, except that a name of one consonant is alike to nothing. The
    /// Septuagint's Ἰεούλ is L and so shares two thirds of רְעוּאֵל, and it is Jeush; the Ukrainian's
    /// Емів is M, and so is Ham in the same verse, which it is not.
    /// </summary>
    public static double Alike(string one, string other) =>
        Math.Min(one.Length, other.Length) < 2 ? 0 : Likeness(one, other);

    /// <summary>Twice the longest common subsequence over the two lengths; nothing for an empty side.</summary>
    public static double Likeness(string one, string other)
    {
        if (one.Length == 0 || other.Length == 0)
        {
            return 0;
        }

        var previous = new int[other.Length + 1];
        var current = new int[other.Length + 1];
        for (var i = 1; i <= one.Length; i++)
        {
            for (var j = 1; j <= other.Length; j++)
            {
                current[j] = one[i - 1] == other[j - 1]
                    ? previous[j - 1] + 1
                    : Math.Max(previous[j], current[j - 1]);
            }

            (previous, current) = (current, previous);
        }

        return 2.0 * previous[other.Length] / (one.Length + other.Length);
    }

    /// <summary>
    /// The names of both sides paired so that they keep their order and share the most letters:
    /// source position to target position. A name with no counterpart alike enough is left out.
    /// </summary>
    /// <param name="source">Each name on the source side: where it stands and its skeleton.</param>
    /// <param name="target">The same for the target side.</param>
    /// <param name="proposed">What the model paired, which decides between names spelt alike.</param>
    public static Dictionary<int, int> Match(
        IReadOnlyList<(int At, string Skeleton)> source,
        IReadOnlyList<(int At, string Skeleton)> target,
        IReadOnlySet<(int Source, int Target)>? proposed = null)
    {
        var matched = new Dictionary<int, int>();
        if (source.Count == 0 || target.Count == 0)
        {
            return matched;
        }

        var like = new double[source.Count, target.Count];
        for (var i = 0; i < source.Count; i++)
        {
            for (var j = 0; j < target.Count; j++)
            {
                var likeness = Alike(source[i].Skeleton, target[j].Skeleton);
                like[i, j] = likeness < LeastLikeness ? 0
                    : likeness - Margin + (proposed?.Contains((source[i].At, target[j].At)) == true ? Proposed : 0);
            }
        }

        var best = new double[source.Count + 1, target.Count + 1];
        for (var i = 1; i <= source.Count; i++)
        {
            for (var j = 1; j <= target.Count; j++)
            {
                var paired = like[i - 1, j - 1] > 0 ? best[i - 1, j - 1] + like[i - 1, j - 1] : double.MinValue;
                best[i, j] = Math.Max(paired, Math.Max(best[i - 1, j], best[i, j - 1]));
            }
        }

        for (int i = source.Count, j = target.Count; i > 0 && j > 0;)
        {
            if (like[i - 1, j - 1] > 0 && best[i, j] == best[i - 1, j - 1] + like[i - 1, j - 1])
            {
                matched[source[i - 1].At] = target[j - 1].At;
                i--;
                j--;
            }
            else if (best[i, j] == best[i - 1, j])
            {
                i--;
            }
            else
            {
                j--;
            }
        }

        return matched;
    }

    /// <summary>
    /// Whether a proposed pair of names contradicts what the names settled: a source name matched to
    /// another target name may not also render this one, nor a target name matched to another source
    /// name be rendered by this one. A name may still reach the name beside its match when that one is
    /// matched to nothing, because one name is sometimes two words on one side: בֵּית לֶחֶם is
    /// Bethlehem, and only one of its halves can be its match.
    ///
    /// <para>
    /// A pair with a word that is not a name at one end is left to the model. The <em>of</em> of
    /// <em>the days of Adam</em> is linked to אָדָם by the King James's own mapping, because Hebrew says
    /// it with the construct and not a word, and the aligners learn that from it.
    /// </para>
    /// </summary>
    public static bool Contradicts(
        int source,
        int target,
        IReadOnlyDictionary<int, int> matched,
        IReadOnlyDictionary<int, int> matchedBack,
        IReadOnlySet<int> sourceNames,
        IReadOnlySet<int> targetNames)
    {
        if (matched.TryGetValue(source, out var named) && named != target && targetNames.Contains(target)
            && !Beside(target, named, matchedBack))
        {
            return true;
        }

        return matchedBack.TryGetValue(target, out var namer) && namer != source && sourceNames.Contains(source)
            && !Beside(source, namer, matched);
    }

    private static bool Beside(int word, int match, IReadOnlyDictionary<int, int> matchedOnItsSide) =>
        Math.Abs(word - match) == 1 && !matchedOnItsSide.ContainsKey(word);

    /// <summary>
    /// One verse's proposals with the names settled: contradicting pairs removed, and every pair the
    /// names settled either kept at no less than <see cref="Settled"/> or added at it. An added pair has
    /// no position score, because the model never proposed it.
    /// </summary>
    /// <param name="sourceNames">Skeletons by source position; null where the word is not a name.</param>
    /// <param name="targetLetters">The skeleton of every target word, name or not.</param>
    /// <param name="targetNames">The same for the target.</param>
    public static List<(int Source, int Target, double Confidence, double Position)> Settle(
        IReadOnlyList<(int Source, int Target, double Confidence, double Position)> verse,
        IReadOnlyList<string?> sourceNames,
        IReadOnlyList<string?> targetNames,
        IReadOnlyList<string> targetLetters)
    {
        var proposals = verse.Select(pair => (pair.Source, pair.Target)).ToHashSet();
        targetNames = Recognised(sourceNames, targetNames, targetLetters, proposals);
        var matched = Match(Names(sourceNames), Names(targetNames), proposals);
        if (matched.Count == 0)
        {
            return [.. verse];
        }

        var matchedBack = matched.ToDictionary(pair => pair.Value, pair => pair.Key);
        var sources = Positions(sourceNames);
        var targets = Positions(targetNames);
        var settled = new List<(int, int, double, double)>(verse.Count + matched.Count);
        var proposed = new HashSet<(int, int)>();

        foreach (var pair in verse)
        {
            if (Contradicts(pair.Source, pair.Target, matched, matchedBack, sources, targets))
            {
                continue;
            }

            var agreed = matched.TryGetValue(pair.Source, out var named) && named == pair.Target;
            settled.Add((pair.Source, pair.Target, agreed ? Math.Max(pair.Confidence, Settled) : pair.Confidence,
                pair.Position));
            proposed.Add((pair.Source, pair.Target));
        }

        foreach (var (source, target) in matched)
        {
            if (!proposed.Contains((source, target)))
            {
                settled.Add((source, target, Settled, double.NaN));
            }
        }

        return settled;
    }

    /// <summary>
    /// The target's names, and any word the model put against a source name whose letters it shares.
    /// Such a word is a name here whatever the target's marking says: BHSA marks as the name the
    /// אָדָם of Genesis 5:1 that the Septuagint renders ἀνθρώπων, and as a noun the one it renders
    /// Ἀδάμ, and pairing by the marking alone would move Adam onto the men. A word of one consonant is
    /// never taken for a name this way: the Synodal capitalises the pronouns of God, and its Моему is
    /// as much M as the μου it renders.
    /// </summary>
    public static IReadOnlyList<string?> Recognised(
        IReadOnlyList<string?> sourceNames,
        IReadOnlyList<string?> targetNames,
        IReadOnlyList<string> targetLetters,
        IEnumerable<(int Source, int Target)> proposed)
    {
        string?[]? recognised = null;
        foreach (var (source, target) in proposed)
        {
            if (sourceNames[source] is { Length: > 1 } name && targetNames[target] is null
                && targetLetters[target].Length > 1
                && Alike(name, targetLetters[target]) >= LeastLikeness)
            {
                recognised ??= [.. targetNames];
                recognised[target] = targetLetters[target];
            }
        }

        return recognised ?? targetNames;
    }

    /// <summary>
    /// The names of a verse in a script that marks none. Ethiopic has no capitals, so no Ge'ez word
    /// says it is a name; one is taken for a name only where it spells, consonant for consonant, a
    /// name the Greek of the same verse marks. That is stricter than <see cref="LeastLikeness"/> on
    /// purpose, because nothing else vouches for the word: three consonants must all agree, and a
    /// word of fewer is never a name here.
    /// </summary>
    public static IReadOnlyList<string?> Unmarked(IReadOnlyList<string> sourceLetters, IReadOnlyList<string?> targetNames)
    {
        var named = targetNames.Where(name => name is { Length: >= UnmarkedLength }).ToList();
        return
        [
            .. sourceLetters.Select(letters => letters.Length >= UnmarkedLength
                                               && named.Any(name => Alike(letters, name!) >= UnmarkedLikeness)
                ? letters
                : null),
        ];
    }

    private const int UnmarkedLength = 3;

    private const double UnmarkedLikeness = 0.8;

    private static HashSet<int> Positions(IReadOnlyList<string?> skeletons) =>
        [.. Enumerable.Range(0, skeletons.Count).Where(at => !string.IsNullOrEmpty(skeletons[at]))];

    private static List<(int At, string Skeleton)> Names(IReadOnlyList<string?> skeletons)
    {
        var names = new List<(int, string)>();
        for (var at = 0; at < skeletons.Count; at++)
        {
            if (!string.IsNullOrEmpty(skeletons[at]))
            {
                names.Add((at, skeletons[at]!));
            }
        }

        return names;
    }

    private static string Plain(string written)
    {
        var decomposed = written.ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var plain = new StringBuilder(decomposed.Length);
        foreach (var letter in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(letter) is UnicodeCategory.LowercaseLetter
                or UnicodeCategory.OtherLetter)
            {
                plain.Append(letter);
            }
        }

        return plain.ToString();
    }

    /// <summary>The consonant a letter or a two-letter spelling stands for, and how many letters it took.</summary>
    private static (string Sound, int Used) Sound(string letters, int at)
    {
        var two = at + 1 < letters.Length ? letters.Substring(at, 2) : string.Empty;
        if (at + 2 < letters.Length && letters.Substring(at, 3) == "sch")
        {
            return ("S", 3);
        }

        switch (two)
        {
            case "sh" or "ts" or "tz" or "zh":
                return ("S", 2);
            case "th":
                return ("T", 2);
            case "ph":
                return ("P", 2);
            case "ch" or "kh" or "ck" or "qu":
                return ("K", 2);
        }

        return (letters[at] switch
        {
            'b' or 'v' or 'β' or 'б' or 'в' or 'ב' => "B",
            'g' or 'γ' or 'г' or 'ґ' or 'ג' => "G",
            'd' or 'δ' or 'д' or 'ד' => "D",
            'k' or 'c' or 'q' or 'κ' or 'χ' or 'к' or 'ч' or 'כ' or 'ך' or 'ק' => "K",
            'l' or 'λ' or 'л' or 'ל' => "L",
            'm' or 'μ' or 'м' or 'מ' or 'ם' => "M",
            'n' or 'ν' or 'н' or 'נ' or 'ן' => "N",
            'p' or 'f' or 'π' or 'φ' or 'п' or 'ф' or 'פ' or 'ף' => "P",
            'r' or 'ρ' or 'р' or 'ר' => "R",
            's' or 'z' or 'σ' or 'ς' or 'ζ' or 'с' or 'з' or 'ж' or 'ц' or 'ш' or 'щ'
                or 'ס' or 'צ' or 'ץ' or 'ש' or 'ז' => "S",
            't' or 'θ' or 'τ' or 'т' or 'ט' or 'ת' => "T",
            'x' or 'ξ' => "KS",
            'ψ' => "PS",
            _ => string.Empty,
        }, 1);
    }
}
