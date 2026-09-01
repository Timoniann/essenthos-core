using System.Text;

namespace Essenthos.Core.Endpoints;

/// <summary>
/// Verse text is stored one word at a time, so a search result's snippet is rebuilt from the
/// words of the verse it matched. The matched words are wrapped in &lt;em&gt; and everything else
/// is HTML-escaped, so the caller can render the snippet without re-scanning it.
/// </summary>
internal static class Snippets
{
    /// <summary>
    /// Which words to mark is decided by the caller, per word, because only the query that
    /// selected the verse knows what matched it — deciding it here from the word's spelling
    /// marked "the" for a search for "therefore" and left "city" unmarked for "cities"
    ///.
    /// </summary>
    public static string Build(IEnumerable<(string Text, string Trailer, bool Matched)> words)
    {
        var snippet = new StringBuilder();
        foreach (var (text, trailer, matched) in words)
        {
            if (matched)
            {
                snippet.Append("<em>").Append(Escape(text)).Append("</em>");
            }
            else
            {
                snippet.Append(Escape(text));
            }

            snippet.Append(Escape(trailer));
        }

        return snippet.ToString().Trim();
    }

    public static string Build(IEnumerable<(string Text, string Trailer)> words, Func<string, bool> isMatch)
    {
        return Build(words.Select(w => (w.Text, w.Trailer, isMatch(w.Text))));
    }

    /// <summary>
    /// Only the three characters that HTML text content cannot carry literally. WebUtility's
    /// encoder also escapes the apostrophe, which is not required outside an attribute and left
    /// every possessive in the corpus reading "Joseph&amp;#39;s".
    /// </summary>
    private static string Escape(string text)
    {
        if (text.AsSpan().IndexOfAny('&', '<', '>') < 0)
        {
            return text;
        }

        var escaped = new StringBuilder(text.Length + 8);
        foreach (var c in text)
        {
            switch (c)
            {
                case '&':
                    escaped.Append("&amp;");
                    break;
                case '<':
                    escaped.Append("&lt;");
                    break;
                case '>':
                    escaped.Append("&gt;");
                    break;
                default:
                    escaped.Append(c);
                    break;
            }
        }

        return escaped.ToString();
    }
}
