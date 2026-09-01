namespace Essenthos.Core.Loading.Links;

/// <summary>
/// Strips the inflection off a King James word.
///
/// English inflects lightly, so this looked like the least promising of the reductions — and it is
/// the one without which the others do harm. A model counts how often a source type and a target
/// type occur together, so reducing one side alone does not pool evidence, it splits it: once the
/// Synodal's <em>отделил</em>, <em>отделяет</em> and <em>отделить</em> became one word, that one
/// word still faced <em>divided</em>, <em>divide</em> and <em>divideth</em> as three, and its
/// evidence was divided three ways instead of being gathered. Measured, that pair fell from 0.13 to
/// 0.05. Both sides have to be reduced or neither.
///
/// It is deliberately shallow. The King James's own forms are the ones worth catching — the
/// <em>-eth</em> and <em>-est</em> of <em>divideth</em> and <em>sayest</em>, which a modern stemmer
/// does not know — and beyond those, the plural, the past and the participle. It does not attempt
/// irregulars: <em>was</em> and <em>is</em> stay apart, and should, because nothing here can tell
/// that they are one verb.
/// </summary>
internal static class EnglishStemmer
{
    /// <summary>
    /// Tried longest first, so "divideth" loses its -eth rather than a bare -th that would take the
    /// last letter of "earth" too.
    ///
    /// The agentive -er is deliberately absent. It is derivational rather than inflectional, and it
    /// turns "waters" into "wat" and leaves "gather" one pass away from "gath" — a stemmer that is
    /// not stable on its own output is one that has stopped describing the language.
    /// </summary>
    private static readonly string[] Endings =
        [.. new[] { "iously", "ously", "ingly", "edly", "iest", "eth", "est", "ies", "ied", "ing",
                    "ely", "ed", "es", "ly", "s" }.OrderByDescending(ending => ending.Length)];

    /// <summary>
    /// What must be left. Below this the ending is most of the word, and what remains is not a stem
    /// but a fragment two unrelated words could share.
    /// </summary>
    private const int Keep = 3;

    public static string Stem(string word)
    {
        var lower = word.ToLowerInvariant();
        if (lower.Length <= Keep)
        {
            return lower;
        }

        foreach (var ending in Endings)
        {
            if (!lower.EndsWith(ending, StringComparison.Ordinal) || lower.Length - ending.Length < Keep)
            {
                continue;
            }

            var stem = lower[..^ending.Length];

            // "ies" and "ied" are a "y" the spelling turned: cries is cry, and cried is too.
            if (ending is "ies" or "ied")
            {
                return stem + "i";
            }

            // A doubled consonant belongs to the ending, not the stem: "begat" keeps its t, but
            // "sitting" is "sit" and not "sitt".
            return Silent(stem.Length > Keep && stem[^1] == stem[^2] && !Vowel(stem[^1]) ? stem[..^1] : stem);
        }

        return Silent(lower);
    }

    /// <summary>
    /// The silent -e, which the inflections eat and the bare word keeps: "divide" against "divided"
    /// and "divideth" is one word written three ways, and only this makes it one.
    /// </summary>
    private static string Silent(string stem) =>
        stem.Length > Keep && stem[^1] == 'e' ? stem[..^1] : stem;

    private static bool Vowel(char letter) => letter is 'a' or 'e' or 'i' or 'o' or 'u' or 'y';
}
