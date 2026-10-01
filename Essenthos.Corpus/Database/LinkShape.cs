using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Essenthos.Core.Database;

/// <summary>
/// The words a link names, as one 128-bit value: <see cref="Entities.Link.Fingerprint"/>.
///
/// <para>
/// A link is a set against a set, so the value is taken over the sorted words of each side, and
/// the sides are kept apart — a word id on the from side and the same number on the to side are
/// different shapes. It is the MD5 of <c>f12 f15 t40</c>: each from-word as <c>f</c> and its id,
/// then each to-word as <c>t</c> and its id, ascending, separated by single spaces. A link left with
/// no words has no fingerprint, rather than the one every empty link would share. MD5 is not here
/// to keep anything secret, only to be the hash Postgres has built in, so the database and the
/// loaders compute the same value and either may write it.
/// </para>
///
/// <para>
/// A trigger on <c>link_word</c> computes the same expression after any change to a link's words,
/// so the column is true whoever wrote them. A loader that writes the value with the link saves the
/// trigger a write; one that does not is corrected by it.
/// </para>
/// </summary>
public static class LinkShape
{
    public static Guid Of(IEnumerable<long> from, IEnumerable<long> to)
    {
        var written = new StringBuilder();
        Append(written, 'f', from);
        Append(written, 't', to);

        var hash = MD5.HashData(Encoding.ASCII.GetBytes(written.ToString()));
        return Guid.ParseExact(Convert.ToHexStringLower(hash), "N");
    }

    private static void Append(StringBuilder written, char side, IEnumerable<long> words)
    {
        foreach (var word in words.Distinct().Order())
        {
            if (written.Length > 0)
            {
                written.Append(' ');
            }

            written.Append(side).Append(word.ToString(CultureInfo.InvariantCulture));
        }
    }
}
