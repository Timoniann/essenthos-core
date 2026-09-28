namespace Essenthos.Core.Loading.Links;

/// <summary>
/// Strips the inflection off a word of the 1911 Almeida.
///
/// Portuguese carries its person, tense and number on the end of the verb, as Spanish does, and the
/// 1911 spelling hyphenates the object pronoun onto it — <em>disse-lhe</em>, <em>levantou-se</em> —
/// so one Hebrew verb is a dozen strings to a model. This takes the pronoun off, then the plural,
/// then the verb endings and the gender.
///
/// It is shallow on purpose, like <see cref="SpanishStemmer"/>: no derivational suffixes, and no
/// irregular stems, so <em>foi</em> and <em>era</em> stay apart.
/// </summary>
internal static class PortugueseStemmer
{
    /// <summary>What must be left, below which the ending is most of the word.</summary>
    private const int Keep = 3;

    private static readonly string[] Endings =
        [.. new[]
            {
                "aríamos", "eríamos", "iríamos", "ássemos", "êssemos", "íssemos", "áramos", "êramos", "íramos",
                "ávamos", "íamos", "aremos", "eremos", "iremos", "assem", "essem", "issem", "ariam", "eriam", "iriam",
                "aram", "eram", "iram", "avam", "arão", "erão", "irão", "aria", "eria", "iria", "ando", "endo", "indo",
                "amos", "emos", "imos", "ava", "iam", "ará", "erá", "irá", "arei", "erei", "irei", "asse", "esse", "isse",
                "ado", "ido", "ada", "ida", "ais", "eis", "ou", "eu", "iu", "am", "em", "ar", "er", "ir",
                "ia", "a", "e", "o",
            }
            .OrderByDescending(ending => ending.Length)];

    public static string Stem(string word)
    {
        var lower = Unhyphenated(word.ToLowerInvariant());

        // A verb ending that ends in -s is the verb's: read as a plural first, comemos would come to
        // comem while comer comes to com.
        return Stripped(lower, ending => ending[^1] == 's')
               ?? Stripped(Plural(lower), _ => true)
               ?? Plural(lower);
    }

    private static string? Stripped(string word, Func<string, bool> considered)
    {
        foreach (var ending in Endings)
        {
            if (considered(ending)
                && word.EndsWith(ending, StringComparison.Ordinal)
                && word.Length - ending.Length >= Keep)
            {
                return Fold(word[..^ending.Length]);
            }
        }

        return null;
    }

    /// <summary>
    /// The pronouns the spelling hyphenates onto a verb, and the endings of the future and the
    /// conditional it splits around one: <em>dar-te-hei</em>.
    /// </summary>
    private static readonly HashSet<string> Clitics = new(StringComparer.Ordinal)
    {
        "me", "te", "se", "nos", "vos", "lhe", "lhes", "o", "a", "os", "as", "lo", "la", "los", "las",
        "no", "na", "nas", "hei", "has", "ha", "há", "hemos", "heis", "hão", "ia", "ias", "iamos", "ieis", "iam",
    };

    /// <summary>
    /// The verb without the pronoun the spelling hyphenates onto it: <em>disse-lhe</em> is
    /// <em>disse</em>. Only where everything after the verb is such a pronoun or ending, so that
    /// <em>Beth-lehem</em> and <em>Todo-poderoso</em> stay whole.
    /// </summary>
    private static string Unhyphenated(string word)
    {
        var parts = word.Split('-');
        return parts.Length > 1 && parts[0].Length > 0 && parts.Skip(1).All(Clitics.Contains) ? parts[0] : word;
    }

    /// <summary>
    /// The singular, taken first so that the plural and the singular lose the same ending after it:
    /// <em>filhos</em> and <em>filho</em> both come to <em>filh</em>.
    /// </summary>
    private static string Plural(string word) =>
        word.Length - 1 >= Keep && word[^1] == 's' ? word[..^1] : word;

    private static string Fold(string stem) =>
        stem.Replace('á', 'a').Replace('à', 'a').Replace('â', 'a').Replace('ã', 'a').Replace('é', 'e')
            .Replace('ê', 'e').Replace('í', 'i').Replace('ó', 'o').Replace('ô', 'o').Replace('õ', 'o')
            .Replace('ú', 'u').Replace('ç', 'c');
}
