using System.Text;

namespace Essenthos.Core.Loading.Links;

/// <summary>
/// Strips the inflection off a word of Luther's German.
///
/// It is the first two steps of the Snowball German stemmer and its closing fold, written out here:
/// the case and number endings (<em>Himmels</em>, <em>Kindern</em>), then the verb and comparative
/// endings (<em>sprachen</em>, <em>größest</em>), each only inside the region the algorithm allows,
/// and the umlaut folded at the end so <em>Söhne</em> meets <em>Sohn</em> and <em>Väter</em> meets
/// <em>Vater</em>. The derivational step is left out for the reason <see cref="EnglishStemmer"/>
/// leaves out -er: <em>-ung</em> and <em>-lich</em> make another word, not another form of one.
///
/// It does not reach the strong verbs: <em>sprach</em>, <em>spricht</em> and <em>gesprochen</em>
/// stay three, as <em>was</em> and <em>is</em> stay two in English.
/// </summary>
internal static class GermanStemmer
{
    private const string Vowels = "aeiouyäöü";

    /// <summary>The letters a plural or genitive -s may follow.</summary>
    private const string SEndings = "bdfghklmnrt";

    /// <summary>The letters a second-person -st may follow.</summary>
    private const string StEndings = "bdfghklmnt";

    /// <summary>How many letters stand before the region an ending may be taken from, at least.</summary>
    private const int Leading = 3;

    private static readonly string[] CaseEndings = ["ern", "em", "er", "en", "es", "e", "s"];

    private static readonly string[] VerbEndings = ["est", "en", "er", "st"];

    public static string Stem(string word)
    {
        var marked = Mark(word.ToLowerInvariant().Replace("ß", "ss", StringComparison.Ordinal));
        var region = RegionOne(marked);

        marked = StripCaseEnding(marked, region);
        marked = StripVerbEnding(marked, region);

        return Unmark(marked);
    }

    private static string StripCaseEnding(string word, int region)
    {
        foreach (var ending in CaseEndings)
        {
            if (!word.EndsWith(ending, StringComparison.Ordinal))
            {
                continue;
            }

            if (word.Length - ending.Length < region)
            {
                return word;
            }

            var stem = word[..^ending.Length];
            if (ending == "s")
            {
                return stem.Length > 0 && SEndings.Contains(stem[^1]) ? stem : word;
            }

            // Kenntnisse is Kenntnis: the doubled s belongs to the plural, not to the word.
            return ending is "e" or "en" or "es" && stem.EndsWith("niss", StringComparison.Ordinal)
                ? stem[..^1]
                : stem;
        }

        return word;
    }

    private static string StripVerbEnding(string word, int region)
    {
        foreach (var ending in VerbEndings)
        {
            if (!word.EndsWith(ending, StringComparison.Ordinal))
            {
                continue;
            }

            if (word.Length - ending.Length < region)
            {
                return word;
            }

            var stem = word[..^ending.Length];
            if (ending == "st")
            {
                return stem.Length > Leading && StEndings.Contains(stem[^1]) ? stem : word;
            }

            return stem;
        }

        return word;
    }

    /// <summary>
    /// Where the region after the first consonant that follows a vowel begins, and never before the
    /// third letter: the part of a word an ending may be taken from.
    /// </summary>
    private static int RegionOne(string word)
    {
        for (var i = 1; i < word.Length; i++)
        {
            if (!IsVowel(word[i]) && IsVowel(word[i - 1]))
            {
                return Math.Max(i + 1, Leading);
            }
        }

        return word.Length;
    }

    /// <summary>
    /// A u or y standing between two vowels is a consonant there — <em>bauen</em>, <em>Mayer</em> —
    /// and is written in capitals until the end so the region is counted without it.
    /// </summary>
    private static string Mark(string word)
    {
        var marked = new StringBuilder(word);
        for (var i = 1; i + 1 < marked.Length; i++)
        {
            if (marked[i] is 'u' or 'y' && IsVowel(marked[i - 1]) && IsVowel(marked[i + 1]))
            {
                marked[i] = char.ToUpperInvariant(marked[i]);
            }
        }

        return marked.ToString();
    }

    private static string Unmark(string word) =>
        word.ToLowerInvariant().Replace('ä', 'a').Replace('ö', 'o').Replace('ü', 'u');

    private static bool IsVowel(char letter) => Vowels.Contains(letter);
}
