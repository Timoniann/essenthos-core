namespace Essenthos.Core.Endpoints;

/// <summary>
/// Which spellings of a Russian or Ukrainian text are forms of one word, read off their endings.
///
/// <para>
/// The aligner's stemmer is the wrong tool for this question. It is built to make the model count
/// more, so it folds a verb's thematic vowel into its stem and lands <em>завітати</em> and
/// <em>завітаю</em> (to visit) on <em>завіт</em> (covenant), <em>світити</em> (to shine) on
/// <em>світ</em> (world); and it reads the genitive of a noun in -т as an infinitive, so
/// <em>заповіти</em> leaves <em>заповіт</em>, and <em>землю</em> and <em>землі</em> leave
/// <em>земля</em>. A concordance asked about a noun has to answer about the noun.
/// </para>
///
/// <para>
/// So a spelling is read as a stem and an ending in each of three paradigms — a noun's, an
/// adjective's, a verb's — and two spellings are forms of one word when they share a stem in the
/// same paradigm. A noun and the verb made from it share letters but never a paradigm. A verb is
/// only read where the ending is one no noun or adjective has (an infinitive, a person ending, a
/// past in -в or -ла after a vowel, a gerund, the reflexive), so a noun typed in its dictionary
/// form never reaches a verb. A stem shorter than three letters says nothing, so a word of two
/// letters is its own only form.
/// </para>
/// </summary>
internal static class SlavicParadigm
{
    internal enum Kind
    {
        Noun,
        Adjective,
        Verb,
    }

    /// <summary>A stem this short is no evidence of anything shared.</summary>
    private const int ShortestStem = 3;

    /// <summary>
    /// A verb's stem before its thematic vowel may be two letters — <em>бу-ти</em>, <em>бу-в</em>,
    /// <em>да-ти</em> —, because the ending it is read by is one no noun has.
    /// </summary>
    private const int ShortestVerbRoot = 2;

    private const string Vowels = "аеиоуыэюяіїєё";

    private const string PastVowels = "аяеєиіїуюы";

    private static readonly string[] NounEndings =
    [
        "", "а", "я", "о", "е", "є", "у", "ю", "і", "ї", "и", "ь", "й",
        "ом", "ем", "єм", "ою", "ею", "єю", "ей", "ам", "ям", "ами", "ями", "ах", "ях",
        "ів", "їв", "ові", "еві", "єві", "ью", "ьми",
    ];

    /// <summary>
    /// The Russian noun's endings Ukrainian does not have. Read in Ukrainian they would take
    /// <em>любов</em> for a plural of <em>люб-</em>.
    /// </summary>
    private static readonly string[] RussianNounEndings = ["ы", "ов", "ев", "ой", "ём"];

    private static readonly string[] AdjectiveEndings =
    [
        "ий", "ій", "їй", "ого", "его", "ього", "ому", "ему", "ьому", "им", "ім", "їм", "их", "іх", "їх",
        "ими", "іми", "їми", "ую", "юю", "ою", "ьою", "ої", "ьої", "ей",
        "а", "я", "е", "є", "і", "ї", "у", "ю", "о",
    ];

    private static readonly string[] RussianAdjectiveEndings =
        ["ый", "ой", "ая", "яя", "ое", "ее", "ые", "ие", "ым", "ых", "ыми", "ы"];

    /// <summary>The vowel a verb puts between its root and its ending.</summary>
    private static readonly string[] Thematic =
        ["", "а", "я", "и", "і", "ї", "е", "є", "у", "ю", "о"];

    /// <summary>Endings only a verb has. A bare -ю, -е or -и is a noun's as often, and is not here.</summary>
    private static readonly string[] VerbEndings =
    [
        // infinitive
        "ти", "ть", "чь",
        // present and future
        "єш", "еш", "иш", "їш", "ешь", "ишь", "ємо", "емо", "имо", "їмо", "єте", "ете", "ите", "їте",
        "ють", "уть", "ять", "ать", "ют", "ут", "ят", "ат", "ет", "ит", "ем", "им",
        // past
        "в", "ла", "ло", "ли", "л",
        // imperative
        "йте", "іть", "ім", "імо", "ймо",
        // gerunds
        "ючи", "учи", "ачи", "ячи", "вши", "вшись",
    ];

    /// <summary>Endings that are a verb's after a vowel, and a noun's or a pronoun's after anything else.</summary>
    private static readonly string[] VerbEndingsAfterAVowel = ["ю", "є", "е", "й"];

    private static readonly string[] Reflexive = ["ся", "сь"];

    /// <summary>Every way this spelling (folded, without apostrophes) divides into a stem and an ending.</summary>
    public static IEnumerable<(Kind Kind, string Stem)> Stems(string word, bool russian)
    {
        var found = new HashSet<(Kind, string)>();

        foreach (var ending in russian ? NounEndings.Concat(RussianNounEndings) : NounEndings)
        {
            if (Stem(word, ending) is { } stem)
            {
                found.Add((Kind.Noun, stem));
            }
        }

        foreach (var ending in russian ? AdjectiveEndings.Concat(RussianAdjectiveEndings) : AdjectiveEndings)
        {
            if (Stem(word, ending) is { } stem)
            {
                found.Add((Kind.Adjective, stem));
            }
        }

        foreach (var verb in Reflexive.Select(r => word.EndsWith(r, StringComparison.Ordinal) ? word[..^r.Length] : null)
                     .Append(word)
                     .OfType<string>())
        {
            foreach (var ending in VerbEndings)
            {
                AddVerb(found, verb, ending);
            }

            foreach (var ending in VerbEndingsAfterAVowel)
            {
                if (verb.Length > ending.Length && verb.EndsWith(ending, StringComparison.Ordinal)
                    && Vowels.Contains(verb[^(ending.Length + 1)]))
                {
                    AddVerb(found, verb, ending);
                }
            }
        }

        return found;
    }

    private static void AddVerb(HashSet<(Kind, string)> found, string verb, string ending)
    {
        if (!verb.EndsWith(ending, StringComparison.Ordinal))
        {
            return;
        }

        var beforeEnding = verb[..^ending.Length];

        // A past in -в or -л is a verb's only after its thematic vowel, and never after о: «сила»,
        // «стіл», «любов», «стол» are not verbs.
        if (ending is "в" or "л" or "ла" or "ло" or "ли"
            && (beforeEnding.Length == 0 || !PastVowels.Contains(beforeEnding[^1])))
        {
            return;
        }

        foreach (var thematic in Thematic)
        {
            if (beforeEnding.EndsWith(thematic, StringComparison.Ordinal)
                && beforeEnding.Length - thematic.Length >= (thematic.Length == 0 ? ShortestVerbRoot : ShortestStem))
            {
                found.Add((Kind.Verb, beforeEnding[..^thematic.Length]));
            }
        }
    }

    private static string? Stem(string word, string ending) =>
        word.EndsWith(ending, StringComparison.Ordinal) && word.Length - ending.Length >= ShortestStem
            ? word[..^ending.Length]
            : null;
}
