using System.Text.Json;

namespace Essenthos.Core.Indexes;

/// <summary>What part an original word plays, for the rules a translation's word takes its number by.</summary>
internal enum WordClass
{
    /// <summary>A noun, a name, a verb, an adjective or a Greek adverb: a word whose number a content word renders.</summary>
    Content,

    /// <summary>A preposition, conjunction, article, pronoun, particle or Hebrew adverb standing as a word of its own.</summary>
    Function,

    /// <summary>
    /// One of the prefixes BHSA writes as a word of its own and the Open Hebrew Bible numbers with a pseudo-number: ו H9000,
    /// ב H9003, כ H9004, ל H9005, the interrogative הֲ H9008 and the article ה H9009.
    /// </summary>
    Prefix,
}

/// <summary>
/// Strong numbers the dictionary splits by form where a reader sees one word: the two Greek negatives, and the case and
/// number forms of the personal and demonstrative pronouns (Strong relates most of the pronoun forms by its form-of
/// relations, not the negatives, and not G1691/G3165 to G1473). Checks and reports count them as one; a word keeps the
/// number of the word its source linked.
/// </summary>
internal static class StrongSynonyms
{
    private static readonly string[][] Groups =
    [
        ["G3756", "G3361"],
        ["G1473", "G1691", "G1698", "G1700", "G3165", "G3427", "G3450", "G2248", "G2249", "G2254", "G2257"],
        ["G4771", "G4571", "G4671", "G4675", "G5209", "G5210", "G5213", "G5216"],
        ["G3778", "G5023", "G5025", "G5026", "G5124", "G5125", "G5126", "G5127", "G5128", "G5129", "G5130"],
        ["H589", "H595"],
        ["H587", "H5168"],
    ];

    private static readonly Dictionary<string, string> First = Groups
        .SelectMany(group => group.Select(number => (number, group[0])))
        .ToDictionary(p => p.number, p => p.Item2, StringComparer.Ordinal);

    /// <summary>The number a number is counted as: the first of its group, else itself.</summary>
    public static string Canonical(string number) => First.GetValueOrDefault(number, number);

    public static bool Same(string a, string b) => string.Equals(Canonical(a), Canonical(b), StringComparison.Ordinal);
}

/// <summary>The classes of the numbers of the originals, and whether a verb of an original is finite.</summary>
internal static class OriginalWords
{
    public static readonly IReadOnlySet<string> Prefixes = new HashSet<string>(["H9000", "H9003", "H9004", "H9005", "H9008", "H9009"], StringComparer.Ordinal);

    private static readonly HashSet<string> FunctionParts = new(
        ["prep", "conj", "art", "prps", "prde", "prin", "nega", "inrg", "intj", "advb", "det", "pron", "ptcl"], StringComparer.Ordinal);

    /// <summary>The part of speech a words product's morphology names (BHSA's and Nestle's <c>pos</c>); null where it names none.</summary>
    public static string? Part(string? morphology) => Read(morphology, "pos");

    /// <summary>The class a part of speech puts a word in.</summary>
    public static WordClass Of(string number, string? part) =>
        Prefixes.Contains(number) ? WordClass.Prefix : part is not null && FunctionParts.Contains(part) ? WordClass.Function : WordClass.Content;

    /// <summary>
    /// Whether a word's morphology makes it a finite verb, whose ending holds its subject: BHSA's perfect, imperfect, wayyiqtol and
    /// imperative, a Greek indicative, subjunctive, optative or imperative (Nestle's <c>mood</c>, Robinson's fourth letter).
    /// </summary>
    public static bool Finite(string? morphology)
    {
        if (morphology is null)
        {
            return false;
        }

        if (Read(morphology, "tense") is { } tense && Read(morphology, "pos") == "verb")
        {
            return tense is "perf" or "impf" or "wayq" or "impv";
        }

        if (Read(morphology, "mood") is { } mood)
        {
            return mood is "indicative" or "subjunctive" or "optative" or "imperative";
        }

        return Read(morphology, "robinson") is { } robinson && robinson.StartsWith("V-", StringComparison.Ordinal)
            && robinson.Split('-') is [_, { Length: >= 3 } parsing, ..] && parsing[^1] is 'I' or 'S' or 'O' or 'M';
    }

    private static string? Read(string? morphology, string field)
    {
        if (morphology is null || !morphology.Contains($"\"{field}\"", StringComparison.Ordinal))
        {
            return null;
        }

        using var json = JsonDocument.Parse(morphology);
        return json.RootElement.TryGetProperty(field, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    }
}

/// <summary>
/// The owner's rules (PRB-1869, 2026-11-18) for the number a translation's word takes over a link to an original that holds
/// several original words, or several words of the translation:
/// <list type="bullet">
/// <item>A word the translation marks as supplied takes none (P1).</item>
/// <item>English «the» takes the original article where the link holds one (H9009, G3588), else none (P4).</item>
/// <item>A word takes a prefix's pseudo-number only where it renders that prefix: in English the function words of each prefix
/// (and → ו, to/for → ל, in/with/by → ב, like/as → כ), in another language a form whose links render that prefix alone (P2).</item>
/// <item>Otherwise a word takes the number of an original word of the link its form confidently renders: in the text's links
/// of one word to one original word (prefixes aside), that number is at least <see cref="ConfidentShare"/> of what the form
/// renders, at least <see cref="ConfidentAtLeast"/> times.</item>
/// <item>After those pairs, the words left are one phrase rendering what is left of the original: where exactly one content
/// word of the original is left, every word left takes its number, function words too («he said» for וַיֹּאמֶר, «who was about
/// to marry» for ἀνήρ); where two or more are left, none does; where the link holds no content word, its one function word left
/// (P3 and P5, the owner's answers of 2026-11-18, <see cref="LeftoverTakesTheOneContentWord"/>). A content word whose form
/// renders another number most of the time elsewhere, and never the phrase's, is not given the phrase's where another word of
/// the verse holds that other number (<see cref="UsualShare"/>).</item>
/// </list>
/// A form is a function word by the English list, or in another language where most of what it renders alone is function
/// words of the original.
/// </summary>
internal sealed class GroupNumbers
{
    /// <summary>
    /// P3 (owner, 2026-11-18: on): «he» in «he said» for וַיֹּאמֶר takes the verb's number, being part of one rendering of one word.
    /// It decides only where <see cref="LeftoverTakesTheOneContentWord"/> is off; on, the phrase rule already gives it.
    /// </summary>
    public const bool SubjectPronounTakesTheVerb = true;

    /// <summary>
    /// P5 (owner, 2026-11-18: on): the words of a group left after the pairs take the number of the one content word of the original
    /// left, function words too. Off: only a content word of a link of one word to one original word takes it.
    /// </summary>
    public const bool LeftoverTakesTheOneContentWord = true;

    public const int ConfidentAtLeast = 2;

    public const double ConfidentShare = 0.1;

    /// <summary>A form that renders another number at least this share of the time, and at least <see cref="UsualAtLeast"/> times, is not given a leftover's.</summary>
    public const double UsualShare = 0.5;

    public const int UsualAtLeast = 5;

    private const double FunctionShare = 0.6;

    private const int FunctionAtLeast = 5;

    private const string English = "eng";

    private static readonly Dictionary<string, string[]> EnglishPrefixes = new(StringComparer.Ordinal)
    {
        ["and"] = ["H9000"], ["but"] = ["H9000"], ["then"] = ["H9000"], ["now"] = ["H9000"], ["so"] = ["H9000"], ["yet"] = ["H9000"],
        ["or"] = ["H9000"],
        ["in"] = ["H9003"], ["with"] = ["H9003"], ["by"] = ["H9003"], ["on"] = ["H9003"], ["at"] = ["H9003"], ["upon"] = ["H9003"],
        ["among"] = ["H9003"], ["within"] = ["H9003"], ["through"] = ["H9003"], ["into"] = ["H9003", "H9005"], ["while"] = ["H9003"],
        ["when"] = ["H9003", "H9004"], ["against"] = ["H9003", "H9005"], ["over"] = ["H9003", "H9005"],
        ["like"] = ["H9004"], ["as"] = ["H9004"], ["according"] = ["H9004"], ["about"] = ["H9004"],
        ["to"] = ["H9005"], ["for"] = ["H9005"], ["unto"] = ["H9005"], ["of"] = ["H9005"], ["toward"] = ["H9005"], ["towards"] = ["H9005"],
        ["concerning"] = ["H9005"],
        ["whether"] = ["H9008"],
        ["the"] = ["H9009"],
    };

    private static readonly HashSet<string> Articles = new(["H9009", "G3588"], StringComparer.Ordinal);

    private static readonly HashSet<string> EnglishFunction = new(
    [
        "a", "an", "the", "and", "but", "or", "nor", "so", "yet", "then", "now", "if", "when", "whenever", "while", "because", "since",
        "that", "though", "although", "whether", "unless", "lest", "than", "as", "like", "also", "even", "too", "not", "no", "there",
        "of", "to", "for", "in", "on", "at", "by", "with", "from", "into", "unto", "upon", "onto", "before", "after", "about", "above",
        "below", "against", "among", "amongst", "between", "through", "throughout", "toward", "towards", "under", "over", "without",
        "within", "until", "till", "beside", "besides", "beyond", "near", "out", "off", "up", "down", "around", "across", "along",
        "behind", "beneath", "concerning", "except", "during", "amid", "amidst", "according",
        "i", "me", "my", "mine", "myself", "we", "us", "our", "ours", "ourselves", "you", "your", "yours", "yourself", "yourselves",
        "thou", "thee", "thy", "thine", "thyself", "ye", "he", "him", "his", "himself", "she", "her", "hers", "herself", "it", "its",
        "itself", "they", "them", "their", "theirs", "themselves", "who", "whom", "whose", "which", "what", "whoever", "whatever",
        "this", "these", "those",
        "be", "am", "is", "are", "was", "were", "been", "being", "have", "has", "had", "having", "do", "does", "did", "doing",
        "will", "would", "shall", "should", "may", "might", "can", "could", "must", "let", "hath", "doth", "art", "wast", "wert",
        "shalt", "wilt", "hast", "hadst", "didst", "dost", "canst",
    ], StringComparer.Ordinal);

    private static readonly HashSet<string> SubjectPronouns = new(["i", "we", "you", "thou", "ye", "he", "she", "it", "they"], StringComparer.Ordinal);

    private readonly bool _english;
    private readonly bool _pronounTakesTheVerb;
    private readonly bool _leftovers;
    private readonly Dictionary<string, Dictionary<string, int>> _renders = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _rendered = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _functionEvidence = new(StringComparer.Ordinal);

    /// <summary>An original word of a link as the rules see it: its key in the lender, its number, class and whether it is a finite verb.</summary>
    internal readonly record struct Original(string Key, string Number, WordClass Class, bool Finite)
    {
        public bool Core => Class != WordClass.Prefix;
    }

    /// <summary>A translation's word of a link: its key, its form, and its own number if it has one.</summary>
    internal readonly record struct Translated(string Key, string Form, string? Own);

    /// <param name="pronounTakesTheVerb">P3 for this run; <see cref="SubjectPronounTakesTheVerb"/> where not given.</param>
    /// <param name="leftovers">P5 for this run; <see cref="LeftoverTakesTheOneContentWord"/> where not given.</param>
    public GroupNumbers(string language, bool? pronounTakesTheVerb = null, bool? leftovers = null)
    {
        _english = language == English;
        _pronounTakesTheVerb = pronounTakesTheVerb ?? SubjectPronounTakesTheVerb;
        _leftovers = leftovers ?? LeftoverTakesTheOneContentWord;
    }

    /// <summary>Counts what each form renders in the links of one of its words to the originals (<see cref="Learn"/> per link).</summary>
    public void Learn(string form, IReadOnlyList<Original> originals)
    {
        var core = originals.Where(o => o.Core).Select(o => StrongSynonyms.Canonical(o.Number)).Distinct(StringComparer.Ordinal).ToList();
        string? rendered = null;
        var function = false;
        if (core.Count == 1)
        {
            rendered = core[0];
            function = originals.First(o => o.Core).Class == WordClass.Function;
        }
        else if (core.Count == 0 && originals.Select(o => o.Number).Distinct(StringComparer.Ordinal).ToList() is [var prefix])
        {
            rendered = prefix;
            function = true;
        }

        if (rendered is null)
        {
            return;
        }

        if (!_renders.TryGetValue(form, out var numbers))
        {
            _renders[form] = numbers = new Dictionary<string, int>(StringComparer.Ordinal);
        }

        numbers[rendered] = numbers.GetValueOrDefault(rendered) + 1;
        _rendered[form] = _rendered.GetValueOrDefault(form) + 1;
        if (function)
        {
            _functionEvidence[form] = _functionEvidence.GetValueOrDefault(form) + 1;
        }
    }

    /// <summary>How often the form renders the number (synonyms counted as one) where it renders one alone; 0 where that is not confident.</summary>
    private int Confident(string form, string number)
    {
        if (!_renders.TryGetValue(form, out var numbers))
        {
            return 0;
        }

        var count = numbers.GetValueOrDefault(Prefixes(number) ? number : StrongSynonyms.Canonical(number));
        return count >= ConfidentAtLeast && count >= ConfidentShare * _rendered[form] ? count : 0;
    }

    private static bool Prefixes(string number) => OriginalWords.Prefixes.Contains(number);

    private bool Function(string form) =>
        _english
            ? EnglishFunction.Contains(form)
            : _rendered.GetValueOrDefault(form) is var all && all >= FunctionAtLeast && _functionEvidence.GetValueOrDefault(form) >= FunctionShare * all;

    /// <summary>
    /// The one word of a link that renders what it holds, for learning what forms render: its only word, or in English its only
    /// word besides «the» and the words that take a prefix of the link («the LORD» over יְהוָה, «and Abraham» over ו + אַבְרָהָם).
    /// </summary>
    public Translated? Rendering(IReadOnlyList<Translated> words, IReadOnlyList<Original> originals)
    {
        if (words.Count == 1)
        {
            return words[0];
        }

        if (!_english)
        {
            return null;
        }

        var rest = words.Where(w => w.Form != "the" && !originals.Any(o => o.Class == WordClass.Prefix
            && EnglishPrefixes.TryGetValue(w.Form, out var renders) && renders.Contains(o.Number))).ToList();
        return rest.Count == 1 ? rest[0] : null;
    }

    /// <summary>
    /// Whether the form renders a number the link does not hold so often that a leftover's number would be a guess: another word
    /// of the verse holds that number («Joseph» over ὤν where Ἰωσήφ stands in the verse, «thus» of «thus saith» over אָמַר beside כֹּה).
    /// A form whose usual number the verse does not hold renders this link's word in it («pass» of «came to pass» over הָיָה), and
    /// so does a form that renders the given number alone elsewhere too («lord» over אֲדֹנִי beside יְהוָה).
    /// </summary>
    private bool UsuallyElse(string form, Original given, IReadOnlyList<Original> originals, IReadOnlySet<string>? verse)
    {
        if (!_renders.TryGetValue(form, out var numbers)
            || numbers.GetValueOrDefault(Prefixes(given.Number) ? given.Number : StrongSynonyms.Canonical(given.Number)) >= ConfidentAtLeast)
        {
            return false;
        }

        // Only the numbers of the link's own language: «Joseph» renders יוֹסֵף in the Old Testament and Ἰωσήφ in the New.
        var languages = originals.Select(o => o.Number[0]).ToHashSet();
        var same = numbers.Where(n => languages.Contains(n.Key[0])).ToList();
        if (same.Count == 0)
        {
            return false;
        }

        var (usual, count) = same.MaxBy(n => n.Value);
        return count >= UsualAtLeast && count >= UsualShare * same.Sum(n => n.Value)
            && (verse is null || verse.Contains(usual))
            && !originals.Any(o => string.Equals(Prefixes(o.Number) ? o.Number : StrongSynonyms.Canonical(o.Number), usual, StringComparison.Ordinal));
    }

    /// <summary>
    /// The original word each word of a link takes its number from, by the rules above; a word the rules give none is left out.
    /// </summary>
    /// <param name="verse">The numbers of the original words of the link's verses (synonyms counted as one); null where not known.</param>
    public Dictionary<string, Original> Decide(IReadOnlyList<Translated> words, IReadOnlyList<Original> originals, IReadOnlySet<string>? verse = null)
    {
        var taken = new Dictionary<string, Original>(StringComparer.Ordinal);
        if (originals.Count == 0)
        {
            return taken;
        }

        var used = new HashSet<string>(StringComparer.Ordinal);
        var left = new List<Translated>();
        foreach (var word in words)
        {
            if (word.Own is not null)
            {
                used.UnionWith(originals.Where(o => StrongSynonyms.Same(o.Number, word.Own)).Select(o => o.Key));
                continue;
            }

            if (_english && word.Form == "the")
            {
                if (originals.FirstOrDefault(o => Articles.Contains(o.Number)) is { Key: not null } article)
                {
                    taken[word.Key] = article;
                    used.Add(article.Key);
                }

                continue;
            }

            var prefix = originals.FirstOrDefault(o => o.Class == WordClass.Prefix
                && (_english ? EnglishPrefixes.TryGetValue(word.Form, out var renders) && renders.Contains(o.Number) : Confident(word.Form, o.Number) > 0));
            if (prefix.Key is not null)
            {
                taken[word.Key] = prefix;
                used.Add(prefix.Key);
                continue;
            }

            var paired = originals.Where(o => o.Core).Select(o => (Original: o, Count: Confident(word.Form, o.Number)))
                .Where(p => p.Count > 0).OrderByDescending(p => p.Count).FirstOrDefault();
            if (paired.Count > 0)
            {
                taken[word.Key] = paired.Original;
                used.Add(paired.Original.Key);
                continue;
            }

            left.Add(word);
        }

        var unused = originals.Where(o => o.Core && !used.Contains(o.Key)).ToList();
        var content = unused.Where(o => o.Class == WordClass.Content).ToList();
        var anyContent = originals.Any(o => o.Class == WordClass.Content);
        var group = words.Count > 1 || originals.Count(o => o.Core) > 1;
        Original? One(List<Original> candidates) =>
            candidates.Select(o => StrongSynonyms.Canonical(o.Number)).Distinct(StringComparer.Ordinal).Count() == 1 ? candidates[0] : null;

        // The words left render, as one phrase, what is left of the original: its one content word, or where the link holds
        // none, its one function word.
        var phrase = anyContent
            ? One(content) ?? (content.Count == 0 ? One([.. originals.Where(o => o.Class == WordClass.Content)]) : null)
            : One(unused);
        foreach (var word in left)
        {
            var function = Function(word.Form);
            Original? given;
            if (_leftovers || !group)
            {
                given = phrase;
            }
            else if (function)
            {
                given = _pronounTakesTheVerb && _english && SubjectPronouns.Contains(word.Form) && One(content) is { Finite: true } verb
                    ? verb
                    : anyContent ? null : phrase;
            }
            else
            {
                given = null;
            }

            if (given is { } phrased && !function && UsuallyElse(word.Form, phrased, originals, verse))
            {
                given = null;
            }

            if (given is { } original)
            {
                taken[word.Key] = original;
            }
        }

        return taken;
    }
}
