using System.Text;

namespace Essenthos.Core.Loading.Links;

/// <summary>
/// Strips the inflection off a Russian or Ukrainian word, so that the model sees one word where the
/// text writes a dozen.
///
/// This is the largest thing that can be done for the alignment, and the size of it is measured
/// rather than hoped for. Greek carries its own lemmas, so the same text can be aligned against the
/// King James twice — once as written and once reduced — and scored against the correspondences the
/// Strong numbers state:
///
///     nestle1904 as written   62.1 % precision, 46.8 % recall
///     nestle1904 as lemmas    75.7 % precision, 51.2 % recall
///
/// Thirteen points. The reason is arithmetic rather than linguistic: a model learns which words
/// correspond by seeing them co-occur, and 47% of the Synodal's forms and 49% of the Ukrainian's
/// appear exactly once in the whole Bible. A word seen once cannot be learned. Reduced to a stem,
/// <em>отделил</em>, <em>отделяет</em> and <em>отделить</em> stop being three words seen once each
/// and become one seen three times — which is the difference between evidence and none.
///
/// Neither Slavic text carries a lemma, so this computes one. It is the Snowball Russian stemmer,
/// which is published and stable, with the Ukrainian vowels and endings added: the two languages
/// inflect alike enough that one set of regions serves both, and where they differ it is in which
/// endings exist rather than in how they attach.
///
/// It is deliberately not a lemmatiser. <em>вода</em> and <em>воду</em> must land together; whether
/// they land on a word a dictionary would recognise does not matter, because nothing reads this —
/// the link is between the words themselves, and the stem is only what the model counts with.
/// </summary>
internal static class SlavicStemmer
{
    private const string Vowels = "аеиоуыэюяіїє";

    /// <summary>Perfective gerunds, in two groups: the first only after а or я.</summary>
    private static readonly string[] GerundAfterVowel = ["вшись", "вши", "в"];

    private static readonly string[] Gerund = ["ывшись", "ившись", "ывши", "ивши", "ыв", "ив"];

    private static readonly string[] Reflexive = ["ся", "сь"];

    private static readonly string[] AdjectiveEndings =
    [
        "ими", "ыми", "его", "ого", "ему", "ому", "ее", "ие", "ые", "ое", "ей", "ий", "ый", "ой",
        "ем", "им", "ым", "ом", "их", "ых", "ую", "юю", "ая", "яя", "ою", "ею",
        // Ukrainian
        "ими", "їми", "ього", "ого", "ому", "єму", "і", "ї", "є",
    ];

    private static readonly string[] ParticipleAfterVowel = ["ющ", "ем", "нн", "вш", "щ"];

    private static readonly string[] Participle = ["ывш", "ивш", "ующ"];

    private static readonly string[] VerbAfterVowel =
    [
        "ешь", "нно", "ете", "йте", "ли", "ла", "на", "ем", "ло", "но", "ет", "ют", "ны", "ть",
        "й", "л", "н",
    ];

    private static readonly string[] Verb =
    [
        "ейте", "уйте", "ила", "ыла", "ена", "ите", "или", "ыли", "ило", "ыло", "ено", "ует",
        "уют", "ены", "ить", "ыть", "ишь", "ей", "уй", "ил", "ыл", "им", "ым", "ен", "ят", "ит",
        "ыт", "ую", "ю",
        // Ukrainian. Its past tense is -ла/-ло/-ли like the Russian, and like the Russian it only
        // counts after а or я — unconditionally it eats the genitive of every noun, and "земли"
        // becomes "зем".
        "ємо", "имо", "ємось", "ешся", "ються", "ти",
    ];

    private static readonly string[] Noun =
    [
        "иями", "ями", "ами", "иях", "ией", "ием", "иям", "ях", "ов", "ев", "ие", "ье",
        "еи", "ии", "ей", "ой", "ий", "ям", "ем", "ам", "ом", "ах", "ию", "ью", "ия", "ья",
        "а", "е", "и", "й", "о", "у", "ы", "ь", "ю", "я",
        // Ukrainian
        "ями", "ами", "ові", "ів", "ах", "ях", "ою", "єю", "ею", "і", "ї", "є",
    ];

    private static readonly string[] Derivational = ["ость", "ост", "ість", "іст"];

    private static readonly string[] Superlative = ["ейше", "ейш", "іше", "іш"];

    /// <summary>
    /// A word shorter than this is left alone. Slavic function words are two and three letters and
    /// are entirely ending; stripping them leaves nothing to count with, and they are the words a
    /// model has least trouble with anyway.
    /// </summary>
    private const int LeaveAlone = 4;

    /// <param name="isName">
    /// Whether the word is a proper name. A name inflects as a noun and never as a verb, and the
    /// difference matters: the gerund ending is a bare -в after а or я, which is right for
    /// <em>сказав</em> and takes the last letter off <em>Аминадав</em>, <em>Иоав</em>,
    /// <em>Ахав</em> and <em>Моав</em>. The name then has two stems — <em>Аминадава</em> keeps its
    /// в and <em>Аминадав</em> loses it — and in a genealogy where the same name stands twice in a
    /// verse, one of them matches the Greek and the other matches nothing and is placed by
    /// position alone.
    /// </param>
    /// <param name="suppletion">
    /// Whether the closed-class table is consulted. It is not, by default, because it was measured
    /// and did not earn its place — <see cref="SlavicSuppletion"/> carries the numbers. The switch
    /// stays so the measurement can be repeated rather than believed.
    /// </param>
    public static string Stem(string word, bool isName = false, bool suppletion = false)
    {
        var lower = word.ToLowerInvariant().Replace('ё', 'е');

        // The closed classes first, where they are consulted at all: stripping endings cannot help a
        // word whose forms share no stem, and "я" and "меня" are one word and two strings that no
        // rule turns into each other. SlavicSuppletion says what unifying them was measured to be
        // worth, and why it is off.
        if (suppletion && SlavicSuppletion.Of(lower) is { } closed)
        {
            return closed;
        }

        if (lower.Length < LeaveAlone)
        {
            return lower;
        }

        var rv = RegionAfterFirstVowel(lower);
        var r2 = SecondRegion(lower);
        var stem = new StringBuilder(lower);

        Step1(stem, rv, isName);
        // "и" is the plural of everything and the conjunction of everything else; on a stem it says
        // nothing the singular does not.
        Trim(stem, rv, ["и"]);
        Trim(stem, r2, Derivational);
        Step4(stem, rv);
        Thematic(stem);

        return stem.Length >= 2 ? stem.ToString() : lower;
    }

    /// <summary>
    /// The vowel a verb carries between its root and its ending, which the published algorithm
    /// leaves behind: <em>отделил</em> reduces to отдел and <em>отделяет</em> to отделя, and a
    /// dictionary is right to call those two verbs — one perfective, one not.
    ///
    /// A dictionary's question is not this one. Both render the same Hebrew word, and keeping them
    /// apart means the model counts each half as often. So the vowel goes, and отделять joins
    /// отделить at отдел. It also settles the algorithm: without this, stemming a stem could strip
    /// it again.
    /// </summary>
    private static void Thematic(StringBuilder stem)
    {
        if (stem.Length > 3 && Vowels.Contains(stem[^1]))
        {
            stem.Length--;
        }
    }

    /// <summary>
    /// A gerund, or else a reflexive followed by whichever of adjective, verb or noun matches
    /// first. The order is the published one and matters: an adjectival ending that is also a noun
    /// ending has to be read as the adjective it is.
    /// </summary>
    private static void Step1(StringBuilder stem, int rv, bool isName)
    {
        if (!isName && (TrimAfterVowel(stem, rv, GerundAfterVowel) || Trim(stem, rv, Gerund)))
        {
            return;
        }

        Trim(stem, rv, Reflexive);

        if (Adjectival(stem, rv) || (!isName && (TrimAfterVowel(stem, rv, VerbAfterVowel) || Trim(stem, rv, Verb))))
        {
            return;
        }

        Trim(stem, rv, Noun);
    }

    private static bool Adjectival(StringBuilder stem, int rv)
    {
        if (!Trim(stem, rv, AdjectiveEndings))
        {
            return false;
        }

        // A participle only ever stands in front of an adjective ending, so it is looked for here
        // and nowhere else.
        if (!TrimAfterVowel(stem, rv, ParticipleAfterVowel))
        {
            Trim(stem, rv, Participle);
        }

        return true;
    }

    private static void Step4(StringBuilder stem, int rv)
    {
        if (EndsWithin(stem, rv, "нн"))
        {
            stem.Length--;
            return;
        }

        if (Trim(stem, rv, Superlative) && EndsWithin(stem, rv, "нн"))
        {
            stem.Length--;
            return;
        }

        Trim(stem, rv, ["ь"]);
    }

    private static bool Trim(StringBuilder stem, int region, string[] endings)
    {
        foreach (var ending in endings.OrderByDescending(e => e.Length))
        {
            if (EndsWithin(stem, region, ending))
            {
                stem.Length -= ending.Length;
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// The endings that only count when а or я stands in front of them — the vowel belongs to the
    /// stem and the ending is what follows it, so removing the ending without the vowel present
    /// would be removing something else that happens to be spelt the same.
    /// </summary>
    private static bool TrimAfterVowel(StringBuilder stem, int region, string[] endings)
    {
        foreach (var ending in endings.OrderByDescending(e => e.Length))
        {
            if (!EndsWithin(stem, region, ending))
            {
                continue;
            }

            var before = stem.Length - ending.Length - 1;
            if (before >= region && (stem[before] == 'а' || stem[before] == 'я'))
            {
                stem.Length -= ending.Length;
                return true;
            }
        }

        return false;
    }

    private static bool EndsWithin(StringBuilder stem, int region, string ending)
    {
        if (stem.Length - ending.Length < region)
        {
            return false;
        }

        for (var i = 0; i < ending.Length; i++)
        {
            if (stem[stem.Length - ending.Length + i] != ending[i])
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Everything after the word's first vowel. Endings are only stripped inside it.</summary>
    private static int RegionAfterFirstVowel(string word)
    {
        for (var i = 0; i < word.Length; i++)
        {
            if (Vowels.Contains(word[i]))
            {
                return i + 1;
            }
        }

        return word.Length;
    }

    /// <summary>
    /// The region after the second vowel-then-consonant boundary. Derivational suffixes are only
    /// stripped this deep in, which is what keeps the rule off short words that merely end the same
    /// way.
    /// </summary>
    private static int SecondRegion(string word) => FirstRegion(word, FirstRegion(word, 0));

    private static int FirstRegion(string word, int from)
    {
        for (var i = from; i + 1 < word.Length; i++)
        {
            if (Vowels.Contains(word[i]) && !Vowels.Contains(word[i + 1]))
            {
                return i + 2;
            }
        }

        return word.Length;
    }
}
