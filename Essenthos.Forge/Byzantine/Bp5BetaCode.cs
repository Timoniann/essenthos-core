using System.Text;

namespace Essenthos.Core.Byzantine;

/// <summary>
/// The Byzantine Textform's beta-code, in Greek letters.
///
/// This is standard TLG beta code and <b>not</b> the alphabet the Textus Receptus composite is
/// written in, although the two files come from the same editor and the same repository family.
/// Here <c>c</c> is xi and <c>x</c> is chi; there they are the other way round, and a final sigma
/// is written <c>v</c> rather than being decided by where the sigma stands. Converting one file
/// with the other's table turns χριστου into ξριστου and every final sigma into a medial one, in
/// every verse, silently.
///
/// The table below was not transcribed from a specification. It was derived by laying all 140,149
/// words of <c>source/Strongs/*.BP5</c> against the Unicode the repository's own converter produced
/// in <c>csv-unicode/strongs/with-parsing</c>, character for character: 24 letters, one Greek
/// letter each, and <c>s</c> the only one whose answer depends on position.
/// </summary>
internal static class Bp5BetaCode
{
    private const string Latin = "abgdezhqiklmncoprstufxyw";

    private const string Greek = "αβγδεζηθικλμνξοπρστυφχψω";

    /// <summary>
    /// One word in Greek letters. A character outside the alphabet is passed through rather than
    /// guessed at, the way the composite's reader passes its own strays through.
    /// </summary>
    public static string ToGreek(string word)
    {
        var greek = new StringBuilder(word.Length);
        for (var i = 0; i < word.Length; i++)
        {
            var at = Latin.IndexOf(word[i], StringComparison.Ordinal);
            greek.Append(at < 0 ? word[i] : Greek[at]);
        }

        // A sigma closing the word is the final form. The file marks it nowhere — it is the one
        // letter whose shape follows from its place — so it is decided here and not in the table.
        if (greek.Length > 0 && greek[^1] == 'σ')
        {
            greek[^1] = 'ς';
        }

        return greek.ToString();
    }
}
