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
