using System.Text.RegularExpressions;

namespace Essenthos.Core.Loading.Links;

/// <summary>
/// One form for the spellings in which two printings of an English Bible differ and nothing else does: the
/// Zefania KJV+ prints the King James in American spelling, the loaded edition in the 1769 one, so the same word
/// is <em>honorable</em> in one and <em>honourable</em> in the other. Lays both on one form before they are put
/// in step (<see cref="TaggedEdition"/>), so that the word anchors the verse and the words beside it stay in step.
/// Each rule is a spelling convention, not a word: -our/-or (honour, labour, saviour), -ell-/-el- before an ending
/// (counsellor, travelled), -ence/-ense (offence, defence), -ick/-ic after a stem of three letters or more (heretick,
/// publick), -eable/-able (unmoveable), and the archaic sted- for stead- (stedfast). A rule changes both sides alike,
/// so where it folds two different words onto one form it can only join words the editions print in the same place.
/// Words whose two spellings follow no rule are listed (plough, grey, judgement). EVIDENTIA's English pack has its own
/// word list for the same purpose (<see cref="Evidentia.EnglishSpelling"/>), which also takes possessives off; it is left
/// as it is so that its runs stay comparable.
/// </summary>
internal static partial class AmericanSpelling
{
    private static readonly (string British, string American)[] Irregular = [("plough", "plow"), ("grey", "gray"), ("judgement", "judgment")];

    public static string Fold(string word)
    {
        var folded = word.ToLowerInvariant();
        foreach (var (british, american) in Irregular)
        {
            if (folded.StartsWith(british, StringComparison.Ordinal))
            {
                folded = american + folded[british.Length..];
            }
        }

        folded = Honour().Replace(folded, "or");
        folded = Counsellor().Replace(folded, "el$1");
        folded = Offence().Replace(folded, "ens$1");
        folded = Heretick().Replace(folded, "ic$1");
        folded = Moveable().Replace(folded, "abl");
        folded = Stedfast().Replace(folded, "stead");
        return folded;
    }

    [GeneratedRegex(@"(?<=\p{L}{1,}[^aeiou\W]|i)our(?=s?$|ed$|ing$|abl|er$|ers$|ite)")]
    private static partial Regex Honour();

    [GeneratedRegex(@"(?<=\p{L})ell(or|ors|ed|ing|er|ers)$")]
    private static partial Regex Counsellor();

    [GeneratedRegex(@"(?<=\p{L})enc(e|es)$")]
    private static partial Regex Offence();

    [GeneratedRegex(@"(?<=\p{L}{3})ick(s?)$")]
    private static partial Regex Heretick();

    [GeneratedRegex(@"(?<=\p{L}{2})eabl(?=e$|y$|eness$)")]
    private static partial Regex Moveable();

    [GeneratedRegex(@"^sted(?=fast)")]
    private static partial Regex Stedfast();
}
