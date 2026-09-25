namespace Essenthos.Core.Loading.Links.Evidentia;

/// <summary>
/// Whether an English verb form says a time or a kind of verb the Greek word it would be placed on
/// cannot be. Greek states tense and mood on every verb, English states them on its own words, and two
/// forms of one lexeme in a verse (<em>εἰσίν … ἔσονται</em>) are otherwise told apart only by where they
/// stand. Hebrew is left alone: its perfect and imperfect are aspects a translation renders in any tense.
///
/// Only the forms that cannot correspond are refused: <em>are</em>, <em>is</em> or <em>was</em>, a
/// finite <em>be</em> English writes as <em>will be</em> where it means the future, is not the Greek
/// future; and <em>being</em>, a participle, is not a finite verb. Other verbs are left alone, since the
/// King James renders a Greek future in its present (<em>the new wine doth burst the bottles</em>,
/// ῥήξει), and so is a past participle, since <em>be glorified</em> is the Greek future passive.
/// </summary>
internal static class EvidentiaTenseAgreement
{
    private const string EnglishLanguage = "eng";

    private const string GreekLanguage = "grc";

    private const string Future = "future";

    private const string Finite = "Fin";

    private static readonly HashSet<string> FiniteTenses = new(StringComparer.OrdinalIgnoreCase) { "Pres", "Past" };

    private static readonly HashSet<string> NonFiniteForms = new(StringComparer.OrdinalIgnoreCase) { "Ger", "Part" };

    private static readonly HashSet<string> FiniteMoods = new(StringComparer.OrdinalIgnoreCase)
    {
        "indicative", "subjunctive", "imperative", "optative",
    };

    private const string Being = "being";

    private const string Be = "be";

    public static bool Disagrees(EvidentiaAnalysis source, EvidentiaAnalysis target)
    {
        if (!source.Token.Language.Equals(EnglishLanguage, StringComparison.OrdinalIgnoreCase)
            || !target.Token.Language.Equals(GreekLanguage, StringComparison.OrdinalIgnoreCase)
            || EvidentiaAttachedWords.Class(target) != "verb")
        {
            return false;
        }

        var form = Feature(source.Token, "VerbForm");
        if (source.Lemma == Be && string.Equals(form, Finite, StringComparison.OrdinalIgnoreCase)
            && Feature(source.Token, "Tense") is { } tense && FiniteTenses.Contains(tense))
        {
            return string.Equals(Feature(target.Token, "tense"), Future, StringComparison.OrdinalIgnoreCase);
        }

        return source.Token.Surface.Equals(Being, StringComparison.OrdinalIgnoreCase)
            && form is not null && NonFiniteForms.Contains(form)
            && Feature(target.Token, "mood") is { } mood && FiniteMoods.Contains(mood);
    }

    private static string? Feature(EvidentiaToken token, string name) =>
        token.Morphology?.FirstOrDefault(pair => pair.Key.Equals(name, StringComparison.OrdinalIgnoreCase)).Value;
}
