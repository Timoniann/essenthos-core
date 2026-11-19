using Essenthos.Core.Strong;
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

/// <summary>The classes of the numbers of the originals, and whether a verb of an original is finite.</summary>
internal static class OriginalWords
{
    public static readonly IReadOnlySet<string> Prefixes = new HashSet<string>(["H9000", "H9003", "H9004", "H9005", "H9008", "H9009"], StringComparer.Ordinal);

    private static readonly HashSet<string> FunctionParts = new(
        ["prep", "conj", "art", "prps", "prde", "prin", "nega", "inrg", "intj", "advb", "det", "pron", "ptcl"], StringComparer.Ordinal);

    /// <summary>
    /// The value of each numeral of the originals (Strong's cardinals, Hebrew, Aramaic and Greek): עָשָׂר H6240 is the ten of
    /// the teens, עַשְׁתֵּי H6249 the one of eleven.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, long> Values = new Dictionary<string, long>(StringComparer.Ordinal)
    {
        ["H259"] = 1, ["H6249"] = 1, ["H8147"] = 2, ["H7969"] = 3, ["H702"] = 4, ["H2568"] = 5, ["H8337"] = 6, ["H7651"] = 7, ["H8083"] = 8,
        ["H8672"] = 9, ["H6235"] = 10, ["H6240"] = 10, ["H6242"] = 20, ["H7970"] = 30, ["H705"] = 40, ["H2572"] = 50, ["H8346"] = 60,
        ["H7657"] = 70, ["H8084"] = 80, ["H8673"] = 90, ["H3967"] = 100, ["H505"] = 1000, ["H7233"] = 10000, ["H7239"] = 10000,
        ["H2298"] = 1, ["H8648"] = 2, ["H8532"] = 3, ["H703"] = 4, ["H8353"] = 6, ["H7655"] = 7, ["H6236"] = 10, ["H6243"] = 20,
        ["H8533"] = 30, ["H3969"] = 100, ["H506"] = 1000, ["H7240"] = 10000,
        ["G1520"] = 1, ["G1417"] = 2, ["G5140"] = 3, ["G5064"] = 4, ["G4002"] = 5, ["G1803"] = 6, ["G2033"] = 7, ["G3638"] = 8, ["G1767"] = 9,
        ["G1176"] = 10, ["G1733"] = 11, ["G1427"] = 12, ["G1180"] = 14, ["G1178"] = 15, ["G1501"] = 20, ["G5144"] = 30, ["G5062"] = 40,
        ["G4004"] = 50, ["G1835"] = 60, ["G1440"] = 70, ["G3589"] = 80, ["G1540"] = 100, ["G1250"] = 200, ["G5145"] = 300, ["G4001"] = 500,
        ["G1812"] = 600, ["G5507"] = 1000, ["G1367"] = 2000, ["G5153"] = 3000, ["G5070"] = 4000, ["G4000"] = 5000, ["G2035"] = 7000,
        ["G3463"] = 10000, ["G3461"] = 10000,
    };

    /// <summary>
    /// The number numerals written in this order stand for, each with whether a conjunction stands before it. A hundred multiplies
    /// the unit standing right before it (שְׁלֹשׁ מֵאוֹת, 300), a thousand or more everything since the last such that stands right
    /// before it (שְׁנֵים עָשָׂר אֶלֶף, 12,000); after a conjunction, and otherwise, they add up (עֶשְׂרִים וּשְׁתַּיִם, 22; שֶׁבַע וְעֶשְׂרִים וּמֵאָה, 127).
    /// </summary>
    public static long Compose(IEnumerable<(long Value, bool Added)> numerals)
    {
        long total = 0, group = 0, last = 0;
        foreach (var (value, added) in numerals)
        {
            if (value >= 1000 && !added && group > 0)
            {
                total += group * value;
                group = 0;
            }
            else if (value == 100 && !added && last is > 0 and < 10)
            {
                group += last * 99;
            }
            else
            {
                group += value;
            }

            last = value;
        }

        return total + group;
    }

    /// <summary>The part of speech a words product's morphology names (BHSA's and Nestle's <c>pos</c>); null where it names none.</summary>
    public static string? Part(string? morphology) => Read(morphology, "pos");

    /// <summary>Whether a word of an original is a proper name: BHSA's <c>nmpr</c>, or a Greek noun spelled with a capital.</summary>
    public static bool Name(string part, string surface) =>
        part == "nmpr" || (part == "noun" && surface.FirstOrDefault(char.IsLetter) is var first && char.IsUpper(first));

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
/// <item>Where one word is left over two or more content words, it takes what it renders by meaning (P7, the owner's answer of
/// 2026-11-18, <see cref="OneWordOverSeveralByMeaning"/>): all of them where it renders them as one unit, a compound numeral or
/// a compound name («twelve» over שְׁתֵּי עֶשְׂרֵה, «Lebo-hamath» over לְבוֹא חֲמָת); the name alone where it renders only the name
/// («Sepharvaim» over עִיר סְפַרְוַיִם); otherwise none.</item>
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

    /// <summary>
    /// P7 (owner, 2026-11-18: on): one word left over two or more content words of the original takes what it renders by meaning.
    /// Off: it takes none.
    /// </summary>
    public const bool OneWordOverSeveralByMeaning = true;

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
    private readonly bool _byMeaning;
    private readonly Dictionary<string, Dictionary<string, int>> _renders = new(StringComparer.Ordinal);
    private Dictionary<string, string>? _usual;
    private readonly Dictionary<string, int> _rendered = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _functionEvidence = new(StringComparer.Ordinal);

    /// <summary>
    /// An original word of a link as the rules see it: its key in the lender, its number, class, whether it is a finite verb, and
    /// whether its number is a proper name's.
    /// </summary>
    internal readonly record struct Original(string Key, string Number, WordClass Class, bool Finite, bool Name = false)
    {
        public bool Core => Class != WordClass.Prefix;

        /// <summary>Its verse and its place in it, from a key <c>verse.position</c>; null where the key is not one.</summary>
        public (string Verse, int Position)? Place =>
            Key.LastIndexOf('.') is var dot and > 0 && int.TryParse(Key.AsSpan(dot + 1), out var position) ? (Key[..dot], position) : null;
    }

    private readonly StrongNumberLists _lists;

    /// <summary>A translation's word of a link: its key, its form, and its own number if it has one.</summary>
    internal readonly record struct Translated(string Key, string Form, string? Own);

    /// <param name="pronounTakesTheVerb">P3 for this run; <see cref="SubjectPronounTakesTheVerb"/> where not given.</param>
    /// <param name="leftovers">P5 for this run; <see cref="LeftoverTakesTheOneContentWord"/> where not given.</param>
    /// <param name="byMeaning">P7 for this run; <see cref="OneWordOverSeveralByMeaning"/> where not given.</param>
    /// <param name="lists">
    /// The Strong number lists: numbers the dictionary splits by form where a reader sees one word (the two Greek negatives, the
    /// case and number forms of the personal and demonstrative pronouns) are counted as one (<see cref="StrongNumberLists.Canonical"/>).
    /// </param>
    public GroupNumbers(string language, StrongNumberLists lists, bool? pronounTakesTheVerb = null, bool? leftovers = null, bool? byMeaning = null)
    {
        _lists = lists;
        _english = language == English;
        _pronounTakesTheVerb = pronounTakesTheVerb ?? SubjectPronounTakesTheVerb;
        _leftovers = leftovers ?? LeftoverTakesTheOneContentWord;
        _byMeaning = byMeaning ?? OneWordOverSeveralByMeaning;
    }

    /// <summary>Counts what each form renders in the links of one of its words to the originals (<see cref="Learn"/> per link).</summary>
    public void Learn(string form, IReadOnlyList<Original> originals)
    {
        var core = originals.Where(o => o.Core).Select(o => _lists.Canonical(o.Number)).Distinct(StringComparer.Ordinal).ToList();
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

        _usual = null;
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

        var count = numbers.GetValueOrDefault(Prefixes(number) ? number : _lists.Canonical(number));
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
            || numbers.GetValueOrDefault(Prefixes(given.Number) ? given.Number : _lists.Canonical(given.Number)) >= ConfidentAtLeast)
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
            && !originals.Any(o => string.Equals(Prefixes(o.Number) ? o.Number : _lists.Canonical(o.Number), usual, StringComparison.Ordinal));
    }

    /// <summary>
    /// The original words each word of a link takes its numbers from, by the rules above: one, or several where one word renders
    /// several as one unit (P7), the first in the original's order; a word the rules give none is left out.
    /// </summary>
    /// <param name="verse">The numbers of the original words of the link's verses (synonyms counted as one); null where not known.</param>
    public Dictionary<string, IReadOnlyList<Original>> Decide(IReadOnlyList<Translated> words, IReadOnlyList<Original> originals, IReadOnlySet<string>? verse = null)
    {
        var taken = new Dictionary<string, Original>(StringComparer.Ordinal);
        if (originals.Count == 0)
        {
            return [];
        }

        var used = new HashSet<string>(StringComparer.Ordinal);
        var left = new List<Translated>();
        foreach (var word in words)
        {
            if (word.Own is not null)
            {
                used.UnionWith(originals.Where(o => _lists.Same(o.Number, word.Own)).Select(o => o.Key));
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
            candidates.Select(o => _lists.Canonical(o.Number)).Distinct(StringComparer.Ordinal).Count() == 1 ? candidates[0] : null;

        var decided = taken.ToDictionary(t => t.Key, t => (IReadOnlyList<Original>)[t.Value], StringComparer.Ordinal);

        // One word left over two or more content words renders, by its meaning, one of them or all of them as one unit (P7); the
        // function words left beside it take none.
        if (_byMeaning && anyContent && content.Select(o => _lists.Canonical(o.Number)).Distinct(StringComparer.Ordinal).Count() > 1
            && left.Where(w => !Function(w.Form)).ToList() is [var single])
        {
            if (Rendered(single, content, originals) is { } unit)
            {
                decided[single.Key] = unit;
            }

            return decided;
        }

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
                decided[word.Key] = [original];
            }
        }

        return decided;
    }

    /// <summary>
    /// What one word renders of two or more content words of the original (P7), by meaning; null where that is not sure (NOT-0228):
    /// <list type="bullet">
    /// <item>a number word (twelve, twenty-two, twelfth, 12) over numerals standing together whose value is its own: all of them
    /// («twelve» over שְׁתֵּי עֶשְׂרֵה);</item>
    /// <item>a hyphened name over words standing together, one of them a name, the name's usual rendering one of its parts: all of
    /// them («Lebo-hamath» over לְבוֹא חֲמָת, «hamath» rendering חֲמָת alone);</item>
    /// <item>a word that is the usual rendering of the one name among them, and renders none of the others alone anywhere: the name
    /// («Sepharvaim» over עִיר סְפַרְוַיִם; עִיר is left unrendered).</item>
    /// </list>
    /// </summary>
    private IReadOnlyList<Original>? Rendered(Translated word, List<Original> content, IReadOnlyList<Original> originals)
    {
        var ordered = content.OrderBy(o => o.Place?.Position ?? int.MaxValue).ToList();
        if (ordered.All(o => OriginalWords.Values.ContainsKey(o.Number)))
        {
            return Value(word.Form) is { } value && Together(ordered, originals, conjunctions: true) is { } added
                && OriginalWords.Compose(ordered.Select((o, at) => (OriginalWords.Values[o.Number], added[at]))) == value
                ? Distinct(ordered)
                : null;
        }

        var names = ordered.Where(o => o.Name).ToList();
        if (names.Count == 0)
        {
            return null;
        }

        var form = Bare(word.Form);
        var parts = form.Split('-');
        if (parts.Length > 1)
        {
            return parts.Length >= ordered.Count && Together(ordered, originals, conjunctions: false) is not null
                && names.Any(n => Usual(n.Number) is { } usual && Bare(usual) is var name && name != form && Within(parts, name.Split('-')))
                ? Distinct(ordered)
                : null;
        }

        return names.Select(n => _lists.Canonical(n.Number)).Distinct(StringComparer.Ordinal).Count() == 1
            && Usual(names[0].Number) == word.Form
            && ordered.Where(o => !o.Name).All(o => RendersAlone(word.Form, o.Number) == 0)
            ? [names[0]]
            : null;
    }

    /// <summary>Each number once, in the order given.</summary>
    private List<Original> Distinct(List<Original> ordered) =>
        [.. ordered.DistinctBy(o => _lists.Canonical(o.Number), StringComparer.Ordinal)];

    /// <summary>
    /// Whether the words stand together in one verse, only prefixes of the link between them (and its conjunctions, where allowed):
    /// for each, whether a conjunction stands right before it; null where they do not.
    /// </summary>
    private static bool[]? Together(List<Original> ordered, IReadOnlyList<Original> originals, bool conjunctions)
    {
        if (ordered.Any(o => o.Place is null) || ordered.Select(o => o.Place!.Value.Verse).Distinct(StringComparer.Ordinal).Count() != 1)
        {
            return null;
        }

        var between = originals.Where(o => o.Place is { } place && place.Verse == ordered[0].Place!.Value.Verse)
            .ToDictionary(o => o.Place!.Value.Position, o => o);
        var added = new bool[ordered.Count];
        for (var at = 1; at < ordered.Count; at++)
        {
            for (var position = ordered[at - 1].Place!.Value.Position + 1; position < ordered[at].Place!.Value.Position; position++)
            {
                if (!between.TryGetValue(position, out var gap))
                {
                    return null;
                }

                var conjunction = gap.Number is "H9000" or "G2532";
                if (conjunction ? !conjunctions : gap.Class != WordClass.Prefix)
                {
                    return null;
                }

                added[at] |= conjunction;
            }
        }

        return added;
    }

    /// <summary>Whether the name's parts stand in the word's parts, in order and together.</summary>
    private static bool Within(string[] parts, string[] name) =>
        Enumerable.Range(0, parts.Length - name.Length + 1).Any(at => parts.AsSpan(at, name.Length).SequenceEqual(name));

    /// <summary>The form without the punctuation around it.</summary>
    private static string Bare(string form) => form.Trim().Trim(Punctuation);

    private static readonly char[] Punctuation = ['“', '”', '‘', '’', '"', '\'', '(', ')', '[', ']', ',', '.', ';', ':', '!', '?', '«', '»', '-', '—'];

    /// <summary>The form the text most often renders a number with where a word renders it alone; null where none does.</summary>
    private string? Usual(string number)
    {
        _usual ??= _renders
            .SelectMany(f => f.Value.Select(n => (Form: f.Key, Number: n.Key, Count: n.Value)))
            .GroupBy(r => r.Number, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(r => r.Count).ThenBy(r => r.Form, StringComparer.Ordinal).First().Form, StringComparer.Ordinal);
        return _usual.GetValueOrDefault(Prefixes(number) ? number : _lists.Canonical(number));
    }

    /// <summary>How often the form renders the number alone (synonyms counted as one).</summary>
    private int RendersAlone(string form, string number) =>
        _renders.TryGetValue(form, out var numbers) ? numbers.GetValueOrDefault(_lists.Canonical(number)) : 0;

    /// <summary>
    /// The value a word stands for: digits (12, 1,000), an English number word, cardinal or ordinal, alone or as tens and unit
    /// (twenty-two, twenty-second); in any language, the value of the one numeral its form renders where it renders one alone,
    /// at least <see cref="ConfidentAtLeast"/> times and half of what it renders. Null where it stands for none.
    /// </summary>
    private long? Value(string form)
    {
        var bare = Bare(form);
        if (bare.Length > 0 && bare.All(c => char.IsAsciiDigit(c) || c == ',') && char.IsAsciiDigit(bare[0])
            && long.TryParse(bare.Replace(",", string.Empty, StringComparison.Ordinal), out var digits))
        {
            return digits;
        }

        if (_english && EnglishValue(bare) is { } english)
        {
            return english;
        }

        if (!_renders.TryGetValue(form, out var numbers))
        {
            return null;
        }

        var (number, count) = numbers.MaxBy(n => n.Value);
        return count >= ConfidentAtLeast && count * 2 >= _rendered[form] && OriginalWords.Values.TryGetValue(number, out var learned) ? learned : null;
    }

    private static readonly Dictionary<string, long> EnglishNumbers = new(StringComparer.Ordinal)
    {
        ["one"] = 1, ["two"] = 2, ["three"] = 3, ["four"] = 4, ["five"] = 5, ["six"] = 6, ["seven"] = 7, ["eight"] = 8, ["nine"] = 9,
        ["ten"] = 10, ["eleven"] = 11, ["twelve"] = 12, ["thirteen"] = 13, ["fourteen"] = 14, ["fifteen"] = 15, ["sixteen"] = 16,
        ["seventeen"] = 17, ["eighteen"] = 18, ["nineteen"] = 19,
        ["first"] = 1, ["second"] = 2, ["third"] = 3, ["fourth"] = 4, ["fifth"] = 5, ["sixth"] = 6, ["seventh"] = 7, ["eighth"] = 8,
        ["ninth"] = 9, ["tenth"] = 10, ["eleventh"] = 11, ["twelfth"] = 12, ["thirteenth"] = 13, ["fourteenth"] = 14, ["fifteenth"] = 15,
        ["sixteenth"] = 16, ["seventeenth"] = 17, ["eighteenth"] = 18, ["nineteenth"] = 19,
    };

    private static readonly Dictionary<string, long> EnglishTens = new(StringComparer.Ordinal)
    {
        ["twenty"] = 20, ["thirty"] = 30, ["forty"] = 40, ["fifty"] = 50, ["sixty"] = 60, ["seventy"] = 70, ["eighty"] = 80, ["ninety"] = 90,
        ["twentieth"] = 20, ["thirtieth"] = 30, ["fortieth"] = 40, ["fiftieth"] = 50, ["sixtieth"] = 60, ["seventieth"] = 70,
        ["eightieth"] = 80, ["ninetieth"] = 90, ["threescore"] = 60, ["fourscore"] = 80,
    };

    private static long? EnglishValue(string word)
    {
        if (EnglishNumbers.TryGetValue(word, out var value) || EnglishTens.TryGetValue(word, out value))
        {
            return value;
        }

        return word.Split('-') is [var tens, var unit] && EnglishTens.TryGetValue(tens, out var t) && !tens.EndsWith("th", StringComparison.Ordinal)
            && EnglishNumbers.TryGetValue(unit, out var u) && u < 10
            ? t + u
            : null;
    }
}
