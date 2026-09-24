namespace Essenthos.Core.Loading.Links;

/// <summary>
/// Strips the inflection off a word of the 1909 Reina-Valera.
///
/// Spanish carries its person, tense and number on the end of the verb, so <em>dijo</em>,
/// <em>dijeron</em> and <em>decía</em> are three strings to a model that has twenty-three thousand
/// verses to learn from, and the Hebrew verb they render is one. This takes the verb endings, the
/// plural and the gender off, and the pronoun the 1909 spelling still writes onto the verb —
/// <em>respondióle</em>, <em>levantóse</em> — which is a word of its own that a model otherwise
/// meets only as part of a longer one.
///
/// It is shallow on purpose, like <see cref="EnglishStemmer"/>: no derivational suffixes, and no
/// irregular stems, so <em>fué</em> and <em>era</em> stay apart.
/// </summary>
internal static class SpanishStemmer
{
    /// <summary>What must be left, below which the ending is most of the word.</summary>
    private const int Keep = 3;

    /// <summary>
    /// The pronouns the 1909 text joins to a verb, which it marks with the accent the verb keeps
    /// when it grows a syllable: <em>díjole</em>, <em>respondióle</em>, <em>haciéndolo</em>.
    /// </summary>
    private static readonly string[] Enclitics =
        [.. new[] { "selas", "selos", "sela", "selo", "les", "los", "las", "nos", "le", "lo", "la", "me", "te", "se", "os" }
            .OrderByDescending(ending => ending.Length)];

    private static readonly string[] Endings =
        [.. new[]
            {
                "aríamos", "eríamos", "iríamos", "iéramos", "iésemos", "áramos", "ásemos",
                "ábamos", "íamos", "aremos", "eremos", "iremos", "asteis", "isteis", "ierais", "ieseis",
                "arían", "erían", "irían", "aríais", "eríais", "iríais", "aréis", "eréis", "iréis",
                "abais", "ieron", "iendo", "yendo", "ieran", "iesen", "arías", "erías", "irías",
                "aban", "aron", "eron", "ando", "aran", "asen", "arán", "erán", "irán", "aría", "ería", "iría",
                "iera", "iese", "aste", "iste", "amos", "emos", "imos", "íais", "ados", "idos", "adas", "idas",
                "aba", "ían", "ará", "erá", "irá", "aré", "eré", "iré", "ara", "ase", "ado", "ido", "ada", "ida",
                "áis", "éis", "ió", "ía", "ar", "er", "ir", "an", "en", "as", "es", "os", "ó", "é", "í", "a", "e", "o",
            }
            .OrderByDescending(ending => ending.Length)];

    public static string Stem(string word)
    {
        var lower = Enclitic(word.ToLowerInvariant());

        foreach (var ending in Endings)
        {
            if (lower.EndsWith(ending, StringComparison.Ordinal) && lower.Length - ending.Length >= Keep)
            {
                return Fold(lower[..^ending.Length]);
            }
        }

        return lower;
    }

    /// <summary>
    /// The verb without the pronoun written onto it. Only where the accent says the verb has grown:
    /// an accented last vowel (<em>respondió-le</em>), an accented gerund (<em>haciéndo-lo</em>), or
    /// a preterite the bare word would not accent at all (<em>díjo-le</em>, <em>dijéron-le</em>).
    /// Without that test every noun ending in -la or -le would lose its last syllable.
    /// </summary>
    private static string Enclitic(string word)
    {
        foreach (var pronoun in Enclitics)
        {
            if (!word.EndsWith(pronoun, StringComparison.Ordinal) || word.Length - pronoun.Length < Keep)
            {
                continue;
            }

            var verb = word[..^pronoun.Length];
            if (verb[^1] is 'ó' or 'é' or 'í' or 'á')
            {
                return verb;
            }

            // Here the accent is the pronoun's doing and the bare verb does not carry it.
            if (verb.EndsWith("ndo", StringComparison.Ordinal) && verb.AsSpan().IndexOfAny("áéí") >= 0
                || verb.AsSpan().IndexOfAny("áéíóú") >= 0
                && (verb[^1] == 'o' || verb.EndsWith("on", StringComparison.Ordinal)))
            {
                return Fold(verb);
            }
        }

        return word;
    }

    private static string Fold(string stem) =>
        stem.Replace('á', 'a').Replace('é', 'e').Replace('í', 'i').Replace('ó', 'o').Replace('ú', 'u');
}
