namespace Essenthos.Core.Endpoints;

/// <summary>
/// A caller's text reaches ILIKE as a pattern, so its own <c>%</c> and <c>_</c> would be wildcards
/// rather than the characters that were typed — <c>?q=%</c> matched every verse in the corpus
///. Every pattern built from user input goes through here.
/// </summary>
internal static class LikePatterns
{
    /// <summary>
    /// Postgres reads ILIKE patterns with a backslash escape unless an ESCAPE clause says
    /// otherwise, and EF emits none, so a literal backslash has to be doubled first.
    /// </summary>
    private const char EscapeCharacter = '\\';

    private const string Wildcards = "%_";

    /// <summary>Matches the value anywhere in the column.</summary>
    public static string Containing(string value)
    {
        return $"%{Escape(value)}%";
    }

    /// <summary>Matches the column when it equals the value, ignoring case.</summary>
    public static string Exactly(string value)
    {
        return Escape(value);
    }

    private static string Escape(string value)
    {
        if (!NeedsEscaping(value))
        {
            return value;
        }

        var escaped = new System.Text.StringBuilder(value.Length + 4);
        foreach (var c in value)
        {
            if (c == EscapeCharacter || Wildcards.Contains(c))
            {
                escaped.Append(EscapeCharacter);
            }

            escaped.Append(c);
        }

        return escaped.ToString();
    }

    private static bool NeedsEscaping(string value)
    {
        foreach (var c in value)
        {
            if (c == EscapeCharacter || Wildcards.Contains(c))
            {
                return true;
            }
        }

        return false;
    }
}
