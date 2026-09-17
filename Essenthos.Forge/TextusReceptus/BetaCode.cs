using System.Text;

namespace Essenthos.Core.TextusReceptus;

/// <summary>
/// Robinson's beta-code, in Greek letters.
///
/// The Textus Receptus files are written in Latin letters — <c>biblov genesewv ihsou cristou</c> —
/// because that is how the Online Greek Bible's texts have always been distributed. Stored as they
/// come, they are unreadable beside the Nestle text they exist to be compared with, and a reader
/// looking at two Greek witnesses side by side sees one Greek and one that is not.
///
/// The mapping is exact and total: one Latin letter, one Greek letter, no accents to guess at
/// because these editions carry none. The one thing it is not is a transliteration in reverse —
/// <c>v</c> is not a letter, it is the position of a sigma at the end of a word, and <c>h</c> is
/// eta rather than a breathing.
/// </summary>
internal static class BetaCode
{
    /// <summary>
    /// The composite's alphabet — the one <c>ScrivenerReader.Fold</c> puts both files into, where
    /// <c>q</c> is theta and <c>y</c> is psi. The other repository trades those two letters, which
    /// is why folding happens before anything is compared or converted.
    /// </summary>
    private const string Latin = "abgdezhqiklmnxoprsvtufcyw";

    private const string Greek = "αβγδεζηθικλμνξοπρσςτυφχψω";

    /// <summary>
    /// The same pair as SQL <c>translate()</c> arguments, for the one-off repair of rows already
    /// loaded as beta-code. Kept here so the two can never drift apart quietly.
    /// </summary>
    public static (string From, string To) TranslateArguments => (Latin, Greek);

    /// <summary>
    /// One word in Greek letters. Anything not in the alphabet is passed through: the source files
    /// leak a handful of subscription lines and division markers, and mangling those into Greek
    /// would hide them rather than leave them visible to be dealt with.
    /// </summary>
    public static string ToGreek(string word)
    {
        var greek = new StringBuilder(word.Length);
        foreach (var character in word)
        {
            var at = Latin.IndexOf(character, StringComparison.Ordinal);
            greek.Append(at < 0 ? character : Greek[at]);
        }

        return greek.ToString();
    }
}
