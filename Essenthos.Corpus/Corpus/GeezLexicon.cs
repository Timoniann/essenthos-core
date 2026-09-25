using Essenthos.Core.Loading.Links;
using Essenthos.Core.Utils;

namespace Essenthos.Core.Corpus;

/// <summary>A headword of the Ge'ez lexicon, with every spelling it is filed under and the Greek it gives.</summary>
/// <param name="Forms">The headword first, then the spellings the lexicon sends to it from entries of their own.</param>
internal sealed record GeezHeadword(string Entry, IReadOnlyList<string> Forms, IReadOnlyList<string> Greek);

/// <param name="Via">
/// <c>form</c> where the word, with what is written onto it taken off, is spelt as this headword and
/// as no other; <c>greek</c> where it could be several and this is the one whose Greek the word's
/// aligned Greek agrees with.
/// </param>
internal sealed record GeezLexiconMatch(string Entry, string Via);

/// <summary>
/// Which of Dillmann's headwords a word of the Ethiopic Bible is a form of, where that can be said
/// without choosing.
///
/// <para>
/// A dictionary lists a verb once, as <em>ቀተለ</em> "he killed", and a noun once, as <em>ቃል</em>
/// "word"; the text writes <em>ወይቀትልዎ</em> "and they kill him" and <em>ቃልየ</em> "my word". So a
/// word is taken apart every way the language writes things onto it — the particles in front
/// (<em>ወ-</em>, <em>ለ-</em>, <em>በ-</em>, <em>ዘ-</em>, <em>እም-</em>, …), a pronoun or a person
/// ending behind, the person prefix of a verb's imperfect — and each remainder is looked for among
/// the headwords that share its consonants.
/// </para>
///
/// <para>
/// A remainder spelt exactly as a headword, or differing only in the vowel of its last letter,
/// which is where Ge'ez writes case and the endings fused onto it (<em>ቃለ</em>, <em>ቃሉ</em>
/// against <em>ቃል</em>), is close; one that only shares the consonants is not, because consonants
/// alone are shared by the verb, its noun, its participle and its broken plurals. A word is matched
/// only where one headword is close and no other is as close, or where several could be meant and
/// the Greek word the aligner put beside it is among the Greek the lexicon gives exactly one of them.
/// Everything else is left without an entry rather than given one of several.
/// </para>
/// </summary>
internal sealed class GeezLexicon
{
    public const string ByForm = "form";

    public const string ByGreek = "greek";

    /// <summary>What must be left of a word once something is taken off it.</summary>
    private const int Keep = 2;

    /// <summary>The orders, counted from zero: the u of the second, the consonant alone of the sixth, the o of the seventh.</summary>
    private const int Second = 1;

    private const int Sixth = 5;

    private const int Seventh = 6;

    /// <summary>Taking off a particle or an ending costs one, a verb's person prefix two: it is the likelier misreading.</summary>
    private const int Particle = 1;

    private const int PersonPrefix = 2;

    /// <summary>The words Ge'ez writes onto the front of the next, including the negation and <em>ከመ</em> "as".</summary>
    private static readonly string[] Proclitics = ["እስከ", "እም", "እን", "ከመ", "ወ", "ለ", "በ", "ዘ", "ኢ"];

    /// <summary>The imperfect's person prefixes: he, she or you, I, we.</summary>
    private static readonly string[] PersonPrefixes = ["ይ", "ት", "እ", "ን"];

    /// <summary>
    /// The pronouns of the object and the owner, and the perfect's person endings, longest first.
    /// </summary>
    private static readonly string[] Suffixes =
        ["ሆሙ", "ሆን", "ክሙ", "ክን", "ኩ", "ሁ", "ሃ", "ሙ", "ከ", "ኪ", "ነ", "ኒ", "ዮ", "ት", "ቶ"];

    /// <summary><em>-ya</em> "my", taken only after a consonant with no vowel, as the stemmer does.</summary>
    private const string My = "የ";

    /// <summary>
    /// The Greek that tells nothing apart when it is one word of a longer equivalent: the article and
    /// the commonest particles and prepositions, bare. Said alone, as <em>ἐν</em> is for <em>ውስተ</em>,
    /// it still counts.
    /// </summary>
    private static readonly HashSet<string> GreekFunctionWords =
    [
        "ο", "η", "το", "οι", "αι", "τα", "του", "τησ", "των", "τω", "τη", "τοισ", "ταισ", "τον", "την",
        "τουσ", "τασ", "και", "εν", "εισ", "εκ", "εξ", "απο", "προσ", "επι", "δια", "κατα", "μετα", "περι",
        "υπο", "υπερ", "παρα", "ωσ", "ου", "μη",
    ];

    /// <summary>Two Greek words are the same lexeme when they share this much of their beginning, and all but one letter of the shorter.</summary>
    private const int GreekStem = 4;

    private const int ShortestGreekToken = 3;

    private readonly ILookup<string, (string Form, GeezHeadword Headword)> _byConsonants;

    private readonly Dictionary<string, HashSet<string>> _greek;

    public GeezLexicon(IEnumerable<GeezHeadword> headwords)
    {
        var all = headwords.DistinctBy(headword => headword.Entry).ToList();
        _byConsonants = all
            .SelectMany(headword => headword.Forms.Select(form => (Form: GeezStemmer.Bare(form), Headword: headword)))
            .Where(pair => pair.Form.Length > 0)
            .ToLookup(pair => GeezStemmer.Consonants(pair.Form), StringComparer.Ordinal);
        _greek = all.ToDictionary(headword => headword.Entry, headword => GreekKeys(headword.Greek), StringComparer.Ordinal);
    }

    /// <summary>The consonants a headword is filed under.</summary>
    public static string ConsonantsOf(string form) => GeezStemmer.Consonants(GeezStemmer.Bare(form));

    /// <summary>Every set of consonants a word could be looked up by, one per way of taking it apart.</summary>
    public static IEnumerable<string> KeysOf(string surface) =>
        Readings(GeezStemmer.Bare(surface)).Select(reading => GeezStemmer.Consonants(reading.Stem)).Distinct();

    /// <param name="greek">The dictionary forms and spellings of the Greek words the word is aligned to.</param>
    public GeezLexiconMatch? Match(string surface, IReadOnlyCollection<string> greek)
    {
        var bare = GeezStemmer.Bare(surface);
        if (bare.Length == 0)
        {
            return null;
        }

        var best = new Dictionary<string, (int Closeness, int Cost)>(StringComparer.Ordinal);
        foreach (var (stem, cost, verbal) in Readings(bare))
        {
            foreach (var (form, headword) in _byConsonants[GeezStemmer.Consonants(stem)])
            {
                var closeness = verbal ? Closeness.Consonants : Close(stem, form);
                var found = ((int)closeness, cost);
                if (!best.TryGetValue(headword.Entry, out var known) || found.CompareTo(known) < 0)
                {
                    best[headword.Entry] = found;
                }
            }
        }

        if (best.Count == 0)
        {
            return null;
        }

        var close = best.Where(pair => pair.Value.Closeness < (int)Closeness.Consonants).ToList();
        var closest = close.Count == 0
            ? []
            : close.Where(pair => pair.Value.Cost == close.Min(other => other.Value.Cost)).Select(pair => pair.Key).ToList();

        var bareGreek = greek.Where(word => !string.IsNullOrWhiteSpace(word)).Select(GreekLetters.Bare).Distinct().ToList();
        var agreeing = bareGreek.Count == 0
            ? []
            : (closest.Count > 0 ? closest : [.. best.Keys]).Where(entry => Agrees(_greek[entry], bareGreek)).ToList();

        return agreeing.Count == 1
            ? new GeezLexiconMatch(agreeing[0], ByGreek)
            : closest.Count == 1
                ? new GeezLexiconMatch(closest[0], ByForm)
                : null;
    }

    private enum Closeness
    {
        Spelt = 1,
        LastVowel = 2,
        Consonants = 3,
    }

    /// <summary>
    /// How near a remainder is to a headword with the same consonants. The last letter's vowel may
    /// differ, since case and the fused endings live there — but a two-letter headword ending in a
    /// vowel is only met by the one change that is a case ending, u to o, because otherwise
    /// <em>ሎሙ</em> "to them" would be <em>ሎሚ</em> "citron".
    /// </summary>
    private static Closeness Close(string stem, string form)
    {
        var folded = GeezStemmer.Fold(stem);
        var headword = GeezStemmer.Fold(form);
        if (string.Equals(folded, headword, StringComparison.Ordinal))
        {
            return Closeness.Spelt;
        }

        if (folded.Length != headword.Length || !folded.AsSpan(0, folded.Length - 1).SequenceEqual(headword.AsSpan(0, headword.Length - 1)))
        {
            return Closeness.Consonants;
        }

        var last = GeezStemmer.Order(stem[^1]);
        var its = GeezStemmer.Order(form[^1]);
        return form.Length > Keep || its == Sixth || (last, its) is (Second, Seventh) or (Seventh, Second)
            ? Closeness.LastVowel
            : Closeness.Consonants;
    }

    /// <summary>Each way of taking a word apart, with what it cost and whether a verb's prefix came off.</summary>
    private static IEnumerable<(string Stem, int Cost, bool Verbal)> Readings(string word)
    {
        var fronts = new Dictionary<string, int>(StringComparer.Ordinal);
        var waiting = new Queue<(string Letters, int Cost)>([(word, 0)]);
        while (waiting.Count > 0)
        {
            var (letters, cost) = waiting.Dequeue();
            if (fronts.TryGetValue(letters, out var known) && known <= cost)
            {
                continue;
            }

            fronts[letters] = cost;
            foreach (var proclitic in Proclitics)
            {
                if (letters.Length - proclitic.Length >= Keep && letters.StartsWith(proclitic, StringComparison.Ordinal))
                {
                    waiting.Enqueue((letters[proclitic.Length..], cost + Particle));
                }
            }
        }

        foreach (var (front, cost) in fronts)
        {
            foreach (var (stem, stemCost, verbal) in Verbal(front, cost))
            {
                yield return (stem, stemCost, verbal);
                foreach (var suffix in Suffixes)
                {
                    if (stem.Length - suffix.Length >= Keep && stem.EndsWith(suffix, StringComparison.Ordinal))
                    {
                        yield return (stem[..^suffix.Length], stemCost + Particle, verbal);
                    }
                }

                if (stem.Length - 1 >= Keep && stem.EndsWith(My, StringComparison.Ordinal) && GeezStemmer.Order(stem[^2]) == Sixth)
                {
                    yield return (stem[..^1], stemCost + Particle, verbal);
                }
            }
        }
    }

    private static IEnumerable<(string Stem, int Cost, bool Verbal)> Verbal(string front, int cost)
    {
        yield return (front, cost, false);
        foreach (var prefix in PersonPrefixes)
        {
            if (front.Length - prefix.Length >= Keep && front.StartsWith(prefix, StringComparison.Ordinal))
            {
                yield return (front[prefix.Length..], cost + PersonPrefix, true);
            }
        }
    }

    /// <summary>
    /// The Greek of an entry as bare words to compare: an equivalent given as one word counts
    /// whatever it is, a longer one by its words that are not the article or a particle.
    /// </summary>
    private static HashSet<string> GreekKeys(IEnumerable<string> equivalents)
    {
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var equivalent in equivalents)
        {
            var words = equivalent
                .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(GreekLetters.Bare)
                .Select(word => new string([.. word.Where(char.IsLetter)]))
                .Where(word => word.Length > 0)
                .ToList();
            keys.UnionWith(words.Count == 1
                ? words
                : words.Where(word => word.Length >= ShortestGreekToken && !GreekFunctionWords.Contains(word)));
        }

        return keys;
    }

    private static bool Agrees(HashSet<string> keys, IReadOnlyList<string> greek)
    {
        foreach (var word in greek)
        {
            if (keys.Contains(word))
            {
                return true;
            }

            foreach (var key in keys)
            {
                var shorter = Math.Min(word.Length, key.Length);
                var shared = 0;
                while (shared < shorter && word[shared] == key[shared])
                {
                    shared++;
                }

                if (shared >= GreekStem && shared >= shorter - 1)
                {
                    return true;
                }
            }
        }

        return false;
    }
}
