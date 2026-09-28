namespace Essenthos.Core.Loading.Links;

/// <summary>
/// Strips the inflection off a word of the 1910 Louis Segond.
///
/// French writes the article, the negation and the object pronoun onto the next word when it opens
/// with a vowel, so <em>l’homme</em>, <em>d’homme</em> and <em>hommes</em> are three strings to a
/// model and the Hebrew noun they render is one. This takes the elided word off the front, then the
/// verb endings, the plural and the gender off the end.
///
/// It is shallow on purpose, like <see cref="SpanishStemmer"/>: no derivational suffixes, and no
/// irregular stems, so <em>fut</em> and <em>était</em> stay apart.
/// </summary>
internal static class FrenchStemmer
{
    /// <summary>What must be left, below which the ending is most of the word.</summary>
    private const int Keep = 3;

    /// <summary>
    /// The short words French elides onto the next one. <em>jusqu’à</em> and <em>lorsqu’il</em> keep
    /// theirs: there the elided word is the one that means something.
    /// </summary>
    private static readonly HashSet<string> Elided = new(StringComparer.Ordinal)
        { "qu", "l", "d", "j", "m", "t", "s", "n", "c" };

    private static readonly string[] Endings =
        [.. new[]
            {
                "eraient", "iraient", "assions", "issions", "assent", "issent", "èrent", "irent", "erions", "irions",
                "eriez", "iriez", "erons", "irons", "eront", "iront", "aient", "erait", "irait", "erais", "irais",
                "issons", "issez", "assiez", "ions", "iez", "ons", "ez", "ent", "ait", "ais", "ant",
                "era", "ira", "erai", "irai", "âmes", "îmes", "âtes", "îtes", "ée", "er", "ir",
                "ât", "ît", "é", "a", "e",
            }
            .OrderByDescending(ending => ending.Length)];

    public static string Stem(string word)
    {
        var lower = Unelided(word.ToLowerInvariant().Replace('’', '\''));

        // A verb ending that ends in -s is the verb's: read as a plural first, donnais would stop at
        // donnai while donnait comes to donn.
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
    /// The singular, taken before the other endings so that the plural and the singular lose the
    /// same one after it: <em>enfants</em> and <em>enfant</em> both come to <em>enf</em>.
    /// </summary>
    private static string Plural(string word) =>
        word.Length - 1 >= Keep && word[^1] is 's' or 'x' ? word[..^1] : word;

    /// <summary>The word without the one elided onto its front: <em>l’homme</em> is <em>homme</em>.</summary>
    private static string Unelided(string word)
    {
        var apostrophe = word.IndexOf('\'');
        if (apostrophe <= 0 || apostrophe == word.Length - 1)
        {
            return word;
        }

        return Elided.Contains(word[..apostrophe]) ? word[(apostrophe + 1)..] : word;
    }

    private static string Fold(string stem) =>
        stem.Replace('é', 'e').Replace('è', 'e').Replace('ê', 'e').Replace('à', 'a').Replace('â', 'a')
            .Replace('î', 'i').Replace('ï', 'i').Replace('ô', 'o').Replace('û', 'u').Replace('ù', 'u')
            .Replace('ç', 'c');
}
