using Essenthos.Core.Loading.Links;

namespace Essenthos.Core.Corpus;

/// <summary>
/// What a verse search makes of a word in each language: the stem two forms of it share, and which
/// words of the language are only its joints.
///
/// <para>
/// The stem comes from Postgres's Snowball dictionary where the language has one, read in SQL by
/// <see cref="Dictionary"/>, both when the forms of a text are counted and when a reader's term is
/// read, so the two cannot disagree. Ukrainian has none there and is stemmed by
/// <see cref="SlavicStemmer"/>, the aligner's Snowball Russian with the Ukrainian endings, on both
/// sides too. A language with neither — Hebrew, Greek, Latin, Syriac, Ge'ez and the rest — is searched
/// by the word as folded, as it always was.
/// </para>
/// </summary>
internal static class SearchWords
{
    /// <summary>The Snowball dictionaries Postgres ships, by the corpus's language codes.</summary>
    private static readonly IReadOnlyDictionary<string, string> Dictionaries = new Dictionary<string, string>
    {
        ["eng"] = "english_stem",
        ["rus"] = "russian_stem",
        ["deu"] = "german_stem",
        ["fra"] = "french_stem",
        ["spa"] = "spanish_stem",
        ["por"] = "portuguese_stem",
        ["arb"] = "arabic_stem",
        ["hin"] = "hindi_stem",
        ["tam"] = "tamil_stem",
    };

    private const string Ukrainian = "ukr";

    /// <summary>The Postgres dictionary that stems this language, or null where none does.</summary>
    public static string? Dictionary(string? language) =>
        language is not null ? Dictionaries.GetValueOrDefault(language) : null;

    /// <summary>Whether this language's words are stemmed here rather than by Postgres.</summary>
    public static bool StemmedHere(string? language) => language == Ukrainian;

    /// <summary>Whether a search of a text in this language matches the forms of a word, not only the one typed.</summary>
    public static bool Stems(string? language) => StemmedHere(language) || Dictionary(language) is not null;

    /// <summary>The stem of a folded word, where it is made here; null where Postgres makes it or nobody does.</summary>
    public static string? StemHere(string folded, string? language) =>
        StemmedHere(language) ? UkrainianVerb(SlavicStemmer.Stem(folded)) : null;

    /// <summary>
    /// What the Snowball Russian stem leaves of a Ukrainian verb's own suffixes: <em>працювати</em> stems
    /// to <em>працюв</em>, <em>працюють</em> to <em>працюют</em> and <em>працює</em> to <em>прац</em>,
    /// three words where a reader typed one. The aligner counts with the stem as it is; a search, which
    /// matches a reader's form to the text's, takes the suffix off too.
    /// </summary>
    private static string UkrainianVerb(string stem)
    {
        foreach (var suffix in VerbSuffixes)
        {
            if (stem.EndsWith(suffix, StringComparison.Ordinal) && stem.Length - suffix.Length >= ShortestVerbStem)
            {
                return stem[..^suffix.Length];
            }
        }

        return stem;
    }

    /// <summary>Longest first, so <em>-ююч</em> comes off before <em>-юч</em>.</summary>
    private static readonly string[] VerbSuffixes = ["ююч", "уюч", "юют", "уют", "юйт", "уйт", "ают", "яют", "юв", "ув", "юч", "ют"];

    private const int ShortestVerbStem = 3;

    /// <summary>
    /// The joints of each language: conjunctions and prepositions a reader quoting from memory puts in
    /// or leaves out, and a translation words either way. A verse need not hold them to be found; a
    /// verse that does is ranked above one that does not. <em>Не</em> and its kind are never among them:
    /// leaving out a negation reverses the verse.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string[][]> Joints = new Dictionary<string, string[][]>
    {
        // Each group is one joint in its spellings: Ukrainian writes «і» or «й», «у» or «в», by the sound
        // around it, so a reader's «і» is the text's «й».
        [Ukrainian] = [["і", "й"], ["у", "в"], ["та"]],
        ["rus"] = [["и"], ["в", "во"]],
        ["eng"] = [["and"], ["the"], ["a"], ["an"]],
        ["deu"] = [["und"]],
        ["fra"] = [["et"]],
        ["spa"] = [["y", "e"]],
        ["por"] = [["e"]],
    };

    /// <summary>
    /// The spellings a joint is written in, the one typed among them, or null where the folded word
    /// is not a joint of the language.
    /// </summary>
    public static IReadOnlyList<string>? Joint(string folded, string? language) =>
        language is not null && Joints.TryGetValue(language, out var joints)
            ? joints.FirstOrDefault(group => group.Contains(folded))
            : null;
}
