namespace Essenthos.Core.Loading.Links.Evidentia;

/// <summary>
/// Reads each witness's own grammatical vocabulary into one shared one.
///
/// <para>
/// There is no universal table here to be had. BHSA writes <c>subs</c>, <c>nmpr</c>, <c>prep</c>
/// and <c>nega</c> and marks a plural <c>pl</c>; MACULA, which is where Nestle 1904's classes come
/// from, writes <c>noun</c>, <c>ptcl</c> and <c>plural</c>; UDPipe writes the Universal
/// Dependencies tags and <c>Plur</c>. A single switch over labels can only ever speak one of the
/// three, which is why the earlier one scored a Hebrew verb and nothing else: 290,158 of BHSA's
/// 426,590 words, every noun and every proper noun among them, carried a label it had never heard
/// of and so could never agree with anything.
/// </para>
///
/// <para>
/// A label this does not recognise stays unknown. Guessing a class from a spelling is how a
/// witness's own annotation gets quietly overwritten by another's.
/// </para>
/// </summary>
internal static class EvidentiaMorphologyLabels
{
    public static string? PartOfSpeech(string? label, string language) =>
        Normalise(label) is not { } value
            ? null
            : Witness(language) switch
            {
                LabelVocabulary.Hebrew => Hebrew.GetValueOrDefault(value),
                LabelVocabulary.Greek => Greek.GetValueOrDefault(value),
                _ => Universal.GetValueOrDefault(value),
            };

    /// <summary>
    /// The value of one comparable feature, in the shared vocabulary. Feature names are already
    /// close enough to compare case-insensitively; the values are not — a singular is <c>sg</c>,
    /// <c>singular</c> or <c>Sing</c> depending on who annotated it.
    /// </summary>
    public static string? Feature(string? value, string language) =>
        Normalise(value) is not { } normalised
            ? null
            : Witness(language) switch
            {
                LabelVocabulary.Hebrew => HebrewFeatures.GetValueOrDefault(normalised),
                LabelVocabulary.Greek => GreekFeatures.GetValueOrDefault(normalised),
                _ => UniversalFeatures.GetValueOrDefault(normalised),
            };

    /// <summary>
    /// Whether a witness's own label marks a word whose correspondence is structural rather than
    /// lexical: the article, the preposition, the conjunction and the auxiliary, which a
    /// translation routinely writes as a case ending, a word order or nothing at all.
    ///
    /// Negation, interjections, pronouns and numerals are deliberately not here. A translation
    /// renders those with a word of its own, and the guard this answers exists to stop two
    /// grammatical words being paired for want of anything else to pair - not to exclude every
    /// word somebody once called a particle.
    ///
    /// Null where the witness states no class, which is not the same as stating a content word.
    /// </summary>
    public static bool? IsFunctionWord(string? label, string language) =>
        PartOfSpeech(label, language) is not { } universal
            ? null
            : universal is "det" or "adp" or "conj" or "aux";

    private static string? Normalise(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToLowerInvariant();

    private static LabelVocabulary Witness(string language) => language.ToLowerInvariant() switch
    {
        "hbo" or "arc" => LabelVocabulary.Hebrew,
        "grc" => LabelVocabulary.Greek,
        _ => LabelVocabulary.Universal,
    };

    private enum LabelVocabulary
    {
        Universal,
        Hebrew,
        Greek,
    }

    /// <summary>Universal Dependencies, which is what the local UDPipe models return.</summary>
    private static readonly Dictionary<string, string> Universal = new(StringComparer.Ordinal)
    {
        ["adj"] = "adj", ["adjective"] = "adj",
        ["adp"] = "adp", ["preposition"] = "adp",
        ["adv"] = "adv", ["adverb"] = "adv",
        ["aux"] = "aux", ["auxiliary"] = "aux",
        ["cconj"] = "conj", ["sconj"] = "conj", ["conj"] = "conj", ["conjunction"] = "conj",
        ["det"] = "det", ["determiner"] = "det",
        ["intj"] = "intj", ["interjection"] = "intj",
        ["noun"] = "noun", ["n"] = "noun",
        ["num"] = "num", ["numeral"] = "num",
        ["part"] = "part", ["particle"] = "part",
        ["pron"] = "pron", ["pronoun"] = "pron",
        ["propn"] = "propn", ["propernoun"] = "propn",
        ["verb"] = "verb", ["v"] = "verb",
    };

    /// <summary>
    /// BHSA's own vocabulary, which the Samaritan Pentateuch shares because it is the same
    /// Text-Fabric annotation. Its three pronoun classes and its two particles are distinctions
    /// Universal Dependencies does not draw, so they collapse rather than disappear.
    /// </summary>
    private static readonly Dictionary<string, string> Hebrew = new(StringComparer.Ordinal)
    {
        ["adjv"] = "adj",
        ["advb"] = "adv",
        ["art"] = "det",
        ["conj"] = "conj",
        ["inrg"] = "part",
        ["intj"] = "intj",
        ["nega"] = "part",
        ["nmpr"] = "propn",
        ["prde"] = "pron",
        ["prep"] = "adp",
        ["prin"] = "pron",
        ["prps"] = "pron",
        ["subs"] = "noun",
        ["verb"] = "verb",
    };

    /// <summary>
    /// The classes MACULA states, which is where Nestle 1904's stored part of speech comes from.
    /// <c>hebrew</c> and <c>aramaic</c> mark a transliterated foreign word and say nothing about
    /// its Greek class, so they stay unknown.
    /// </summary>
    private static readonly Dictionary<string, string> Greek = new(StringComparer.Ordinal)
    {
        ["adj"] = "adj",
        ["adv"] = "adv",
        ["conj"] = "conj",
        ["det"] = "det",
        ["intj"] = "intj",
        ["noun"] = "noun",
        ["num"] = "num",
        ["prep"] = "adp",
        ["pron"] = "pron",
        ["ptcl"] = "part",
        ["verb"] = "verb",
    };

    private static readonly Dictionary<string, string> UniversalFeatures = new(StringComparer.Ordinal)
    {
        ["sing"] = "sg", ["plur"] = "pl", ["dual"] = "du",
        ["masc"] = "m", ["fem"] = "f", ["neut"] = "n",
        ["1"] = "1", ["2"] = "2", ["3"] = "3",
        ["nom"] = "nom", ["acc"] = "acc", ["dat"] = "dat", ["gen"] = "gen", ["voc"] = "voc",
    };

    private static readonly Dictionary<string, string> HebrewFeatures = new(StringComparer.Ordinal)
    {
        ["sg"] = "sg", ["pl"] = "pl", ["du"] = "du",
        ["m"] = "m", ["f"] = "f",
        ["p1"] = "1", ["p2"] = "2", ["p3"] = "3",
    };

    private static readonly Dictionary<string, string> GreekFeatures = new(StringComparer.Ordinal)
    {
        ["singular"] = "sg", ["plural"] = "pl",
        ["masculine"] = "m", ["feminine"] = "f", ["neuter"] = "n",
        ["first"] = "1", ["second"] = "2", ["third"] = "3",
        ["nominative"] = "nom", ["accusative"] = "acc", ["dative"] = "dat", ["genitive"] = "gen", ["vocative"] = "voc",
    };
}
