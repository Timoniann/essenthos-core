using Essenthos.Core.TextusReceptus;

namespace Essenthos.Core.Loading.Links;

/// <summary>
/// Ancient Greek, reduced to something a model can count.
///
/// Brenton carries no lemmas, so the only handle on a Greek word is the word. Greek declines a
/// noun eight ways and conjugates a verb across hundreds of forms, so <em>θεός θεοῦ θεῷ θεόν θεοί
/// θεῶν θεοῖς θεούς</em> are eight strings for one word — and a lexicon built over twenty-three
/// thousand verses never sees any one of them often enough to learn it. This is the same problem
/// BHSA's vowel pointing posed and it takes the same answer.
///
/// It is deliberately shallow. The endings below are inflections that carry no meaning of their
/// own; the aorist and perfect stems Greek forms by changing the word itself — <em>λέγω</em>
/// against <em>εἶπον</em>, <em>φῶς</em> against <em>φωτός</em> — are left alone, because guessing
/// at those is how a stemmer starts merging words that are not the same word. A model that sees
/// two forms of one lexeme as two words loses a little; a model that sees two lexemes as one word
/// is wrong, and cannot tell that it has been lied to.
///
/// Accents go first, through <see cref="GreekLetters"/>. That alone does much of the work: Brenton
/// is accented, and the accent moves with the ending, so two forms of one word differ in more
/// places than their endings.
/// </summary>
internal static class GreekStemmer
{
    /// <summary>A word this short has no ending to lose.</summary>
    private const int LeaveAlone = 2;

    /// <summary>What must survive. A stem shorter than this stops identifying a word.</summary>
    private const int Keep = 2;

    /// <summary>
    /// The article, the commonest prepositions and the particles, left exactly as they are.
    ///
    /// They carry no lexical content, so nothing is gained by reducing them — and something is
    /// lost: <em>εἰς</em> stripped of its sigma is <em>ει</em>, which is also <em>εἰ</em>, "if".
    /// Merging a preposition into a conjunction teaches the model a word that does not exist.
    /// </summary>
    private static readonly HashSet<string> Uninflected = Bare(
    [
        "ο", "η", "το", "οι", "αι", "τα", "του", "της", "των", "τω", "τη", "τον", "την", "τοις",
        "ταις", "τους", "τας", "και", "δε", "εν", "εις", "εκ", "εξ", "επι", "προς", "απο", "δια",
        "κατα", "μετα", "περι", "υπο", "υπερ", "παρα", "συν", "ανα", "αντι", "ου", "ουκ", "ουχ",
        "μη", "ως", "αν", "γαρ", "ουν", "τε", "ει", "αλλα", "ινα", "οτι", "εαν", "ιδου", "ναι",
    ]);

    /// <summary>
    /// Inflectional endings, in the bare alphabet <see cref="GreekLetters"/> writes — so a final
    /// sigma is σ here, because that is what a bare word ends in.
    /// </summary>
    private static readonly string[] Endings = Longest(
    [
        // The thematic verb: present, future and aorist, active and middle-passive.
        "θησονται", "θησομαι", "θησεται", "σθωσαν", "σομεθα", "ομεθα", "εσθαι", "οντων",
        "θησαν", "θημεν", "ουσιν", "ουσαν", "οντες", "οντος", "σαμεν", "σασιν", "καμεν",
        "κασιν", "σουσι", "σομαι", "σεται", "θηναι", "ονται", "ουντο", "ομην", "θητε",
        "εσθε", "ηται", "οντο", "εται", "ηναι", "ομεν", "ουσι", "ασιν", "ησαν", "θεις",
        "σατε", "σασι", "κατε", "κασι", "σεις", "σετε", "οντα", "οντι", "ετε", "ασι", "θεν",
        "ην",
        // Nouns and adjectives, across the three declensions.
        "οισιν", "αισιν", "ησιν", "εων", "εσι", "οισ", "αισ", "ουσ", "ων", "οσ", "ου",
        "ον", "οι", "ησ", "αν", "αι", "εσ", "ει", "ασ",
        // What is left when everything else has been tried.
        "η", "α", "ε", "ο", "ω", "σ", "ν",
    ]);

    /// <summary>
    /// Both lists written through the same normalisation the stemmer works in, so that a final
    /// sigma typed here can never fail to match a final sigma in the text. Typing σ where ς was
    /// meant is invisible on the page and total in effect.
    /// </summary>
    private static HashSet<string> Bare(string[] words) =>
        [.. words.Select(GreekLetters.Bare)];

    /// <summary>
    /// Sorted longest first, in code rather than by hand. A list written in the wrong order takes
    /// <em>σ</em> off <em>λογουσ</em> and calls the stem <em>λογου</em>, and nothing about reading
    /// the list would show it.
    /// </summary>
    private static string[] Longest(string[] endings) =>
        [.. endings.Select(GreekLetters.Bare).Distinct()
            .OrderByDescending(ending => ending.Length).ThenBy(ending => ending)];

    /// <summary>The word with its inflection set aside, or the word, when taking it off leaves nothing.</summary>
    public static string Stem(string word)
    {
        var bare = GreekLetters.Bare(word);
        if (bare.Length <= LeaveAlone || Uninflected.Contains(bare))
        {
            return bare;
        }

        foreach (var ending in Endings)
        {
            if (bare.Length - ending.Length >= Keep && bare.EndsWith(ending, StringComparison.Ordinal))
            {
                return bare[..^ending.Length];
            }
        }

        return bare;
    }
}
