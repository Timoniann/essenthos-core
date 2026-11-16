namespace Essenthos.Core.Loading.Links.Evidentia;

/// <summary>
/// Whether an English personal pronoun and a word of an original name the same person. BHSA states the
/// person, number and gender of a verb's ending and, apart from them, of a pronominal suffix; MACULA
/// states a Greek verb's person and number. Nothing is guessed where a side is silent: a participle
/// names no person and so agrees with no pronoun, and an English <em>you</em> says nothing of number.
/// </summary>
internal static class EvidentiaPersonAgreement
{
    private static readonly Dictionary<string, string> GenderOfForm = new(StringComparer.OrdinalIgnoreCase)
    {
        ["he"] = "m", ["him"] = "m", ["his"] = "m",
        ["she"] = "f", ["her"] = "f", ["hers"] = "f",
    };

    public static string? Person(EvidentiaAnalysis pronoun) =>
        EvidentiaMorphologyLabels.Feature(Feature(pronoun.Token, "Person"), pronoun.Token.Language);

    /// <summary>
    /// Whether the word names the pronoun's person, and neither its number nor its gender says
    /// otherwise; <paramref name="suffix"/> reads the pronominal suffix rather than the word's own
    /// ending. An English plural agrees with a Hebrew dual.
    /// </summary>
    public static bool Agrees(EvidentiaAnalysis pronoun, EvidentiaAnalysis word, bool suffix)
    {
        var language = word.Token.Language;
        var person = Person(pronoun);
        if (person is null
            || EvidentiaMorphologyLabels.Feature(Feature(word.Token, suffix ? "suffixPerson" : "person"), language) != person)
        {
            return false;
        }

        var number = person == "2" ? null : EvidentiaMorphologyLabels.Feature(Feature(pronoun.Token, "Number"), pronoun.Token.Language);
        var wordNumber = EvidentiaMorphologyLabels.Feature(Feature(word.Token, suffix ? "suffixNumber" : "number"), language);
        if (number is not null && wordNumber is not null && number != wordNumber && !(number == "pl" && wordNumber == "du"))
        {
            return false;
        }

        var gender = EvidentiaMorphologyLabels.Feature(Feature(pronoun.Token, "Gender"), pronoun.Token.Language)
            ?? GenderOfForm.GetValueOrDefault(pronoun.Token.Surface);
        var wordGender = EvidentiaMorphologyLabels.Feature(Feature(word.Token, suffix ? "suffixGender" : "gender"), language);
        return gender is not ("m" or "f") || wordGender is not ("m" or "f") || gender == wordGender;
    }

    /// <summary>
    /// The owner a German or Spanish possessive names: its person, and its number where the form says it
    /// (<em>mi</em>, <em>nuestro</em>, <em>sein</em>, <em>unser</em>; not <em>su</em> or <em>ihr</em>, which serve one owner
    /// or many). The parse's own number and gender are the noun's, not the owner's: <em>sus hijos</em> are one man's
    /// sons. Read from the form, since the parse calls <em>vayas</em> and <em>Noemi</em> possessives and misses
    /// <em>euer</em>. Null for any other word.
    /// </summary>
    public static (string Person, string? Number)? Owner(EvidentiaAnalysis word)
    {
        var owners = word.Token.Language.ToLowerInvariant() switch
        {
            "spa" => SpanishOwners,
            "deu" => GermanOwners,
            _ => null,
        };
        return owners is not null && owners.TryGetValue(word.Token.Surface, out var owner) ? owner : null;
    }

    /// <summary>
    /// Whether the word names the owner a German or Spanish possessive names: the same person, and no other number
    /// where the possessive's form gives one (an owner of many agrees with a Hebrew dual). The second person is
    /// held to its number as well: Spanish and German keep <em>tu</em> and <em>vuestro</em> apart as the original does.
    /// </summary>
    public static bool OwnerAgrees(EvidentiaAnalysis possessive, EvidentiaAnalysis word, bool suffix)
    {
        if (Owner(possessive) is not var (person, number))
        {
            return false;
        }

        var language = word.Token.Language;
        var wordPerson = EvidentiaMorphologyLabels.Feature(Feature(word.Token, suffix ? "suffixPerson" : "person"), language)
            ?? (!suffix && word.Token.StrongNumber is "G846" or "G1438" ? "3" : null);
        if (wordPerson != person)
        {
            return false;
        }

        var wordNumber = EvidentiaMorphologyLabels.Feature(Feature(word.Token, suffix ? "suffixNumber" : "number"), language);
        return number is null || wordNumber is null || number == wordNumber || number == "pl" && wordNumber == "du";
    }

    private static readonly Dictionary<string, (string Person, string? Number)> SpanishOwners = Owners(
        ("1", "sg", ["mi", "mis", "mío", "mía", "míos", "mías"]),
        ("2", "sg", ["tu", "tus", "tuyo", "tuya", "tuyos", "tuyas"]),
        ("3", null, ["su", "sus", "suyo", "suya", "suyos", "suyas"]),
        ("1", "pl", ["nuestro", "nuestra", "nuestros", "nuestras"]),
        ("2", "pl", ["vuestro", "vuestra", "vuestros", "vuestras"]));

    private static readonly Dictionary<string, (string Person, string? Number)> GermanOwners = Owners(
        ("1", "sg", Declined("mein")),
        ("2", "sg", Declined("dein")),
        ("3", "sg", Declined("sein")),
        ("3", null, Declined("ihr")),
        ("1", "pl", [.. Declined("unser"), "unsre", "unsren", "unsrem", "unsrer", "unsres", "unsern", "unserm"]),
        ("2", "pl", [.. Declined("euer"), "eure", "euren", "eurem", "eurer", "eures", "euern", "euerm"]));

    private static string[] Declined(string stem) =>
        [stem, stem + "e", stem + "en", stem + "em", stem + "er", stem + "es"];

    private static Dictionary<string, (string Person, string? Number)> Owners(
        params (string Person, string? Number, string[] Forms)[] owners)
    {
        var table = new Dictionary<string, (string Person, string? Number)>(StringComparer.OrdinalIgnoreCase);
        foreach (var (person, number, forms) in owners)
        {
            foreach (var form in forms)
            {
                table[form] = (person, number);
            }
        }

        return table;
    }

    /// <summary>A Hebrew infinitive, whose subject is written as a suffix: <em>אֲכָלְךָ</em>, <em>your eating</em>.</summary>
    public static bool IsInfinitive(EvidentiaAnalysis word) =>
        string.Equals(Feature(word.Token, "tense"), "infc", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Whether a pronoun of the original could be the subject the English pronoun names. A Hebrew
    /// personal pronoun states its person; a Greek one only in the nominative, and <em>αὐτός</em>
    /// and the relatives state no person at all, so there the number has to do.
    /// </summary>
    public static bool CouldBeTheSubject(EvidentiaAnalysis english, EvidentiaAnalysis pronoun)
    {
        if (EvidentiaMorphologyLabels.Feature(Feature(pronoun.Token, "case"), pronoun.Token.Language) is not (null or "nom"))
        {
            return false;
        }

        if (Feature(pronoun.Token, "person") is not null)
        {
            return Agrees(english, pronoun, suffix: false);
        }

        var number = EvidentiaMorphologyLabels.Feature(Feature(english.Token, "Number"), english.Token.Language);
        var pronounNumber = EvidentiaMorphologyLabels.Feature(Feature(pronoun.Token, "number"), pronoun.Token.Language);
        return Person(english) == "3" && (number is null || pronounNumber is null || number == pronounNumber);
    }

    private static string? Feature(EvidentiaToken token, string name) =>
        token.Morphology?.FirstOrDefault(pair => pair.Key.Equals(name, StringComparison.OrdinalIgnoreCase)).Value;
}
