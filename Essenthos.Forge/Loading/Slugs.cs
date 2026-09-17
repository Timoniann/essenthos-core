using System.Text;

namespace Essenthos.Core.Loading;

/// <summary>
/// A slug is the part of a public address a reader can type and a paper can cite —
/// <c>bhsa/gen.1.1#3</c> — so it is lower case, has no spaces, and never changes once a text is
/// published.
/// </summary>
internal static class Slugs
{
    public static string Of(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var c in value)
        {
            if (char.IsLetterOrDigit(c))
            {
                builder.Append(char.ToLowerInvariant(c));
            }
        }

        if (builder.Length == 0)
        {
            throw new ArgumentException($"\"{value}\" has no letters or digits, so it cannot be a slug.", nameof(value));
        }

        return builder.ToString();
    }
}
