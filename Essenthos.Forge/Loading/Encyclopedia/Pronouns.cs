using System.Text;

namespace Essenthos.Core.Loading.Encyclopedia;

/// <summary>
/// The personal pronouns of the languages a carried name lands on, each with the sex it states where
/// it states one: <em>he</em> is a man and <em>she</em> a woman, <em>thee</em>, <em>their</em>,
/// <em>sein</em> and <em>свой</em> say neither. A pronoun is never a name and never a spelling of
/// one; whether the person it refers to may be shown on it is <see cref="PronounReferents"/>'s rule.
/// </summary>
internal static class Pronouns
{
    public const string Male = "male";

    public const string Female = "female";

    private static readonly IReadOnlyDictionary<string, IReadOnlyDictionary<string, string?>> ByLanguage =
        new Dictionary<string, IReadOnlyDictionary<string, string?>>(StringComparer.Ordinal)
        {
            ["eng"] = Words(
                ["he", "him", "his", "himself"],
                ["she", "her", "hers", "herself"],
                ["they", "them", "their", "theirs", "themselves", "thee", "thou", "thy", "thine", "thyself", "ye",
                 "you", "your", "yours", "yourself", "yourselves", "i", "me", "my", "mine", "myself", "we", "us",
                 "our", "ours", "ourselves", "it", "its", "itself", "who", "whom", "whose"]),
            ["rus"] = Words(
                ["он", "его", "ему", "нем", "него", "нему", "ним"],
                ["она", "ее", "её", "ей", "ней", "нее", "неё", "ею", "нею"],
                ["они", "их", "них", "ними", "им", "ты", "тебя", "тебе", "тобою", "тобой", "вы", "вас", "вам",
                 "вами", "я", "меня", "мне", "мною", "мной", "мы", "нас", "нам", "нами", "свой", "своего",
                 "своему", "своим", "своем", "своя", "свою", "свои", "своих", "сам", "себя", "себе", "собою",
                 "кто", "кого", "кому"]),
            ["ukr"] = Words(
                ["він", "його", "йому", "нього", "ньому", "ним"],
                ["вона", "її", "їй", "нею", "неї", "ній"],
                ["вони", "їх", "їм", "ними", "них", "ти", "тебе", "тобі", "тобою", "ви", "вас", "вам", "вами",
                 "я", "мене", "мені", "мною", "ми", "нас", "нам", "нами", "свій", "свого", "своєму", "своїм",
                 "своя", "свою", "свої", "своїх", "сам", "себе", "собі", "собою", "хто", "кого", "кому"]),
            ["deu"] = Words(
                ["er", "ihn", "ihm"],
                [],
                ["sein", "seine", "seinen", "seinem", "seiner", "seines", "sie", "ihr", "ihre", "ihren", "ihrem",
                 "ihrer", "ihres", "du", "dich", "dir", "dein", "deine", "deinen", "deinem", "deiner", "ich", "mich",
                 "mir", "mein", "meine", "meinen", "meinem", "meiner", "wir", "uns", "unser", "unsere", "euch",
                 "euer", "eure", "sich", "wer", "wen", "wem"]),
            ["spa"] = Words(
                ["él"],
                ["ella"],
                ["ellos", "ellas", "le", "les", "se", "su", "sus", "suyo", "suya", "tú", "ti", "te", "tu", "tus",
                 "yo", "me", "mi", "mis", "nosotros", "nos", "vosotros", "os", "quien", "quién"]),
        };

    private static Dictionary<string, string?> Words(string[] male, string[] female, string[] neither)
    {
        var words = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var word in male)
        {
            words[word] = Male;
        }

        foreach (var word in female)
        {
            words[word] = Female;
        }

        foreach (var word in neither)
        {
            words.TryAdd(word, null);
        }

        return words;
    }

    /// <summary>Whether a word, lower-cased and stripped of punctuation, is a pronoun of its language.</summary>
    public static bool Is(string language, string word) =>
        ByLanguage.TryGetValue(language, out var words) && words.ContainsKey(word);

    /// <summary>Every pronoun as a SQL row source of (language, word, sex).</summary>
    public static string Rows()
    {
        var rows = new StringBuilder("(VALUES ");
        var first = true;
        foreach (var (language, words) in ByLanguage)
        {
            foreach (var (word, sex) in words)
            {
                rows.Append(first ? string.Empty : ", ")
                    .Append($"('{language}', '{word}', {(sex is null ? "NULL" : $"'{sex}'")})");
                first = false;
            }
        }

        return rows.Append(") AS pronoun (language, word, sex)").ToString();
    }

    /// <summary>
    /// Whether the word <c>hw</c> of the text <c>ht</c> is a pronoun, as SQL over the same aliases
    /// <see cref="Annotating.NeverAName"/> reads.
    /// </summary>
    public static string IsWord(string word = "hw", string text = "ht") =>
        $"""
         EXISTS (SELECT 1 FROM {Rows()}
                 WHERE pronoun.language = {text}.language
                   AND pronoun.word = lower(regexp_replace({word}.text, '[[:punct:]]', '', 'g')))
         """;
}
