namespace Essenthos.Core.Utils;

/// <summary>
/// A verse is rebuilt by concatenating each word's text and its trailer, so the trailer carries
/// the separation from the next word as well as the punctuation. A word ending in punctuation
/// still needs the space that followed it in the source — without it a verse reads
/// "their sister,and her nurse,and".
/// </summary>
public static class WordSeparation
{
    /// <summary>
    /// Punctuation that opens rather than closes. A space does not belong after it: the source
    /// reads "went out (and saw", not "went out ( and saw".
    /// </summary>
    private const string OpeningPunctuation = "([{<«“‘";

    /// <summary>
    /// Returns the trailer with the separating space it needs before the following word. Applies
    /// to a word that has a successor within its verse; the last word of a verse is separated by
    /// the verse break itself and is returned unchanged.
    /// </summary>
    public static string EnsureSeparator(string trailer)
    {
        if (trailer.Length == 0)
        {
            return " ";
        }

        var last = trailer[^1];
        if (char.IsWhiteSpace(last) || OpeningPunctuation.Contains(last))
        {
            return trailer;
        }

        return trailer + " ";
    }

    /// <summary>
    /// Collapses whitespace runs to a single space, so that indentation in a pretty-printed
    /// source file does not end up inside a trailer.
    /// </summary>
    public static string NormalizeWhitespace(string trailer)
    {
        if (trailer.Length == 0)
        {
            return trailer;
        }

        var needsWork = false;
        for (var i = 0; i < trailer.Length; i++)
        {
            var c = trailer[i];
            if (c is not ' ' && char.IsWhiteSpace(c))
            {
                needsWork = true;
                break;
            }

            if (c == ' ' && i > 0 && trailer[i - 1] == ' ')
            {
                needsWork = true;
                break;
            }
        }

        if (!needsWork)
        {
            return trailer;
        }

        var builder = new System.Text.StringBuilder(trailer.Length);
        var previousWasSpace = false;
        foreach (var c in trailer)
        {
            if (char.IsWhiteSpace(c))
            {
                if (!previousWasSpace)
                {
                    builder.Append(' ');
                }

                previousWasSpace = true;
                continue;
            }

            builder.Append(c);
            previousWasSpace = false;
        }

        return builder.ToString();
    }
}
