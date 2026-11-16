using System.Text.Json;

namespace Essenthos.Core.Corpus;

/// <summary>
/// The person, number and gender a word says of whom it stands for: <c>1</c>, <c>2</c> or <c>3</c>; <c>s</c> or <c>p</c>;
/// <c>m</c>, <c>f</c> or <c>n</c>. Null where the word does not say: <em>you</em> is either number, <em>їх</em> any gender.
/// A translation's <c>m</c> is masculine or neuter (<em>him</em>, <em>його</em>), since none of its languages tells them apart in
/// a pronoun.
/// </summary>
internal readonly record struct Agreement(char? Person, char? Number, char? Gender)
{
    /// <summary>
    /// What a source's parsing of an original's word says: MACULA Greek's <c>number</c>, <c>gender</c> and the person in its
    /// code (<c>P-1GS</c>), BHSA's <c>person</c> (<c>p1</c>), <c>number</c> (<c>sg</c>, <c>du</c>) and <c>gender</c>. Of a word
    /// the source classes as anything but a pronoun or an article only the person: <c>ἰδίᾳ</c> of "his own wife" and
    /// <c>ἐμή</c> are feminine and singular by the wife, not by whose she is.
    /// </summary>
    public static Agreement Of(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            return default;
        }

        string? Read(string key) =>
            root.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

        char? person = Read("person") switch
        {
            "first" or "p1" => '1',
            "second" or "p2" => '2',
            "third" or "p3" => '3',
            _ => Read("morph") is { Length: > 2 } code && code[1] == '-' && code[2] is '1' or '2' or '3' ? code[2] : null,
        };
        char? number = Read("number") switch
        {
            "singular" or "sg" => 's',
            "plural" or "pl" or "dual" or "du" => 'p',
            _ => null,
        };
        char? gender = Read("gender") switch
        {
            "masculine" or "m" => 'm',
            "feminine" or "f" => 'f',
            "neuter" or "n" => 'n',
            _ => null,
        };
        return Read("class") is null or "pron" or "det" ? new Agreement(person, number, gender) : new Agreement(person, null, null);
    }

    /// <summary>A pronoun suffix's person, gender and number as MACULA Hebrew spells them: <c>3ms</c>, <c>1cs</c>, <c>2fp</c>.</summary>
    public static Agreement OfSuffix(string code) =>
        code.Length != 3
            ? default
            : new Agreement(
                code[0] is '1' or '2' or '3' ? code[0] : null,
                code[2] switch { 's' => 's', 'p' or 'd' => 'p', _ => null },
                code[1] is 'm' or 'f' ? code[1] : null);

    /// <summary>
    /// Whether a translation's pronoun saying this cannot render an original's word saying that: another number, another
    /// person where the original is of the first or second (a third-person <c>αὐτός</c> is <em>myself</em> beside
    /// <c>ἐγώ</c>, and a translation turns reported speech into direct), or, of one person, masculine against feminine.
    /// </summary>
    public bool Contradicts(Agreement original) =>
        (Number is { } number && original.Number is { } theirs && number != theirs)
        || (Person is { } person && original.Person is '1' or '2' && person != original.Person)
        || (original.Number == 's' && Gender is { } gender && original.Gender is 'm' or 'f' && gender != original.Gender);
}

/// <summary>
/// The personal and possessive pronouns of the languages whose translations show whom a pronoun refers to most, with what
/// each says of the one it stands for. A form two readings share is given only what both say: Russian <em>им</em> is
/// "by him" and "to them", so it says no number. Reflexives and <em>who</em> say nothing and are not here.
/// </summary>
internal static class PronounForms
{
    private static readonly Dictionary<(string Language, string Form), Agreement> Forms = Build();

    /// <summary>What the form says, where its language is here and the form, its letters lower-cased, is one of its pronouns.</summary>
    public static Agreement? Of(string language, string form)
    {
        var letters = string.Concat(form.Where(char.IsLetter)).ToLowerInvariant();
        return Forms.TryGetValue((language, letters), out var agreement) ? agreement : null;
    }

    private static Dictionary<(string, string), Agreement> Build()
    {
        var forms = new Dictionary<(string, string), Agreement>();

        void Add(string language, string spaced, char person, char? number, char? gender = null)
        {
            foreach (var form in spaced.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                forms.Add((language, form), new Agreement(person, number, gender));
            }
        }

        Add("eng", "i me my mine myself", '1', 's');
        Add("eng", "we us our ours ourselves", '1', 'p');
        Add("eng", "thou thee thy thine thyself yourself", '2', 's');
        Add("eng", "ye yourselves", '2', 'p');
        Add("eng", "you your yours", '2', null);
        Add("eng", "he him his himself", '3', 's', 'm');
        Add("eng", "she her hers herself", '3', 's', 'f');
        Add("eng", "it its itself", '3', 's');
        Add("eng", "they them their theirs themselves", '3', 'p');

        // Both spellings of the 1871 translation's ї after a consonant (менї, нїй) beside today's.
        Add("ukr", "я мене мені менї мною мій мого моєму мою моя моє моєю моєї мої моїй моїм моїми моїх", '1', 's');
        Add("ukr", "ми нас нам нами наш наша наше нашого нашому нашим нашими наших нашою нашої нашу наші нашій", '1', 'p');
        Add("ukr", "ти тебе тобі тобою твій твого твою твоя твоє твоєму твоєю твоєї твої твоїй твоїм твоїми твоїх", '2', 's');
        Add("ukr", "ви вас вам вами ваш ваша ваше вашого вашому вашим вашими ваших вашою вашої вашу ваші вашій", '2', 'p');
        Add("ukr", "він його йому него нього ньому нім нїм", '3', 's', 'm');
        Add("ukr", "ним", '3', null, 'm');
        Add("ukr", "вона її їй неї нею ній нїй", '3', 's', 'f');
        Add("ukr", "вони їх їм ними них їхній їхнього їхньому їхньою їхньої їхню їхня їхнє їхні їхнім їхніми їхніх", '3', 'p');

        Add("rus", "я меня мне мной мною мой моего моему моим моем моём моя моей мою моею мое моё мои моих моими", '1', 's');
        Add("rus", "мы нас нам нами наш нашего нашему нашим нашем наша нашей нашу нашею наше наши наших нашими", '1', 'p');
        Add("rus", "ты тебя тебе тобой тобою твой твоего твоему твоим твоем твоём твоя твоей твою твоею твое твоё твои твоих твоими", '2', 's');
        Add("rus", "вы вас вам вами ваш вашего вашему вашим вашем ваша вашей вашу вашею ваше ваши ваших вашими", '2', 'p');
        Add("rus", "он оно его него ему нему нем нём", '3', 's', 'm');
        Add("rus", "им ним", '3', null, 'm');
        Add("rus", "она ее её ей ею нее неё ней нею", '3', 's', 'f');
        Add("rus", "они их них ими ними", '3', 'p');
        return forms;
    }
}
