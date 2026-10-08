using System.Text;

namespace Essenthos.Core.Loading.Encyclopedia;

/// <summary>
/// The personal pronouns of the languages a carried name lands on, each with the sex it states where
/// it states one: <em>he</em> is a man and <em>she</em> a woman, <em>thee</em>, <em>their</em>,
/// <em>sein</em> and <em>свой</em> say neither. The Greek is the personal and demonstrative
/// pronouns and the reflexives, in the forms Brenton, Swete and the New Testament print them: a form
/// that is masculine or neuter (<em>αὐτοῦ</em>) is taken for the masculine, since a person is what it
/// is carried onto, and one that is only neuter or says no gender (<em>αὐτό</em>, <em>με</em>,
/// <em>αὐτῶν</em>) says no sex. A pronoun is never a name and never a spelling of
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
            ["grc"] = Words(
                WithGraves(
                    ["αὐτός", "αὐτοῦ", "αὐτῷ", "αὐτόν", "οὗτος", "τούτου", "τούτῳ", "τοῦτον", "ἐκεῖνος", "ἐκείνου",
                     "ἐκείνῳ", "ἐκεῖνον", "ἑαυτοῦ", "ἑαυτῷ", "ἑαυτόν", "ἐμαυτοῦ", "ἐμαυτῷ", "ἐμαυτόν", "σεαυτοῦ",
                     "σεαυτῷ", "σεαυτόν"]),
                WithGraves(
                    ["αὐτή", "αὐτῆς", "αὐτῇ", "αὐτήν", "αὕτη", "ταύτης", "ταύτῃ", "ταύτην", "ἐκείνη", "ἐκείνης",
                     "ἐκείνῃ", "ἐκείνην", "ἑαυτῆς", "ἑαυτῇ", "ἑαυτήν"]),
                WithGraves(
                    ["αὐτό", "αὐτά", "αὐτοί", "αὐταί", "αὐτῶν", "αὐτοῖς", "αὐταῖς", "αὐτούς", "αὐτάς", "τοῦτο",
                     "ταῦτα", "οὗτοι", "αὗται", "τούτων", "τούτοις", "τούτους", "ταύταις", "ταύτας", "ἐκεῖνο",
                     "ἐκεῖνα", "ἐκεῖνοι", "ἐκεῖναι", "ἐκείνων", "ἐκείνοις", "ἐκείναις", "ἐκείνους", "ἐκείνας",
                     "ἑαυτῶν", "ἑαυτοῖς", "ἑαυταῖς", "ἑαυτούς", "ἑαυτάς", "ἐγώ", "ἐμοῦ", "μου", "ἐμοί", "μοι", "ἐμέ",
                     "με", "ἡμεῖς", "ἡμῶν", "ἡμῖν", "ἡμᾶς", "σύ", "σοῦ", "σου", "σοί", "σοι", "σέ", "σε", "ὑμεῖς",
                     "ὑμῶν", "ὑμῖν", "ὑμᾶς", "ἐμός", "ἐμή", "ἐμόν", "σός", "σή", "σόν"])),
        };

    /// <summary>
    /// The forms as the texts print them mid-sentence too: a word with its accent on the last syllable
    /// takes the grave there (<em>αὐτὸν</em>, <em>ἐγὼ</em>), and both are the same pronoun.
    /// </summary>
    private static string[] WithGraves(string[] acute) =>
        [.. acute.SelectMany(form => new[] { form, ToGrave(form) }).Distinct(StringComparer.Ordinal)];

    private static string ToGrave(string form)
    {
        for (var at = form.Length - 1; at >= 0; at--)
        {
            if (form[at] is not ('ά' or 'έ' or 'ή' or 'ί' or 'ό' or 'ύ' or 'ώ'))
            {
                continue;
            }

            var grave = form[at] switch
            {
                'ά' => 'ὰ', 'έ' => 'ὲ', 'ή' => 'ὴ', 'ί' => 'ὶ', 'ό' => 'ὸ', 'ύ' => 'ὺ', _ => 'ὼ',
            };
            var tail = form[(at + 1)..];

            // Only an accent on the last syllable becomes a grave before another word.
            return tail.All(letter => "βγδζθκλμνξπρσςτφχψ".Contains(letter)) ? form[..at] + grave + tail : form;
        }

        return form;
    }

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
