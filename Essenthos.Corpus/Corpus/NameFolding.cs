using System.Text;
using Essenthos.Core.Utils;

namespace Essenthos.Core.Corpus;

/// <summary>
/// A name the way a search compares it, whatever script and whatever language it is in.
///
/// <para>
/// A reader types <em>aaron</em> for <em>Aarón</em>, <em>ахав</em> for <em>Ахав</em>,
/// <em>beth shemesh</em> for <em>Beth-shemesh</em> and <em>אהרן</em> for
/// <em>אַהֲרֹן</em>. So only letters and digits are kept, in lower case, with the Hebrew points, the
/// Greek accents and breathings and the Latin diacritics taken off, and the two Cyrillic letters a
/// keyboard routinely spells another way — <em>ё</em> and <em>ґ</em> — folded onto the ones it does.
/// </para>
///
/// <para>
/// Written out rather than left to <see cref="string.Normalize()"/>, which the build's invariant
/// globalisation turns into a no-op, so a decomposition would silently fold nothing. The stored
/// spelling and the typed one both go through here, which is the only way they are guaranteed to
/// agree.
/// </para>
/// </summary>
internal static class NameFolding
{
    private const string Accented = "àáâãäåāăąçćčèéêëēėęěìíîïīįñńňòóôõöøōőùúûüūůűýÿšśžźżłđďťřľĺёґ";

    private const string Plain = "aaaaaaaaaccceeeeeeeeiiiiiinnnoooooooouuuuuuuyysszzzlddtrllег";

    public static string Fold(string name)
    {
        var bare = GreekLetters.Bare(WordFolding.Fold(name, "hbo")).ToLowerInvariant();
        var folded = new StringBuilder(bare.Length);

        foreach (var c in bare)
        {
            if (c == 'ß')
            {
                folded.Append("ss");
                continue;
            }

            if (!char.IsLetterOrDigit(c))
            {
                continue;
            }

            var at = Accented.IndexOf(c, StringComparison.Ordinal);
            folded.Append(at >= 0 ? Plain[at] : c);
        }

        return folded.ToString();
    }
}
