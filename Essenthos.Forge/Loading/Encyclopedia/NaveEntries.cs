using System.Globalization;
using System.Text.RegularExpressions;
using Essenthos.Core.Loading.Frame;

namespace Essenthos.Core.Loading.Encyclopedia;

/// <summary>
/// Nave's entries as lines of verses: <c>-Makes the golden calf EXO 32; ACT 7:40; DEU 9:20,21</c>.
///
/// <para>
/// An entry is a heading per line, the verses cited on it, and lines indented under the line above
/// them. A citation carries its book forward — <c>EXO 6:23,25; 1CH 6:3-15,50-53; 24</c> is Exodus 6,
/// then Chronicles 6 and 24 — and a bare chapter is the whole chapter. <c>with</c> pairs a verse
/// with its quotation elsewhere and is read as a separator, and a verse range may cross a chapter
/// end. Whatever does not read as one of these is counted and dropped rather than guessed at.
/// </para>
/// </summary>
internal static partial class NaveEntries
{
    /// <summary>A chapter, or verses of one: no verses is the whole chapter, no last verse runs to its end.</summary>
    internal sealed record Citation(int Book, int Chapter, int? FirstVerse, int? LastVerse);

    /// <param name="Heading">The line's own words, under the line above where it is indented; null where it has none.</param>
    internal sealed record Line(string? Heading, IReadOnlyList<Citation> Citations, int Unread);

    /// <summary>
    /// Book codes the transcription writes in other ways than the frame's own: the Song of Solomon
    /// as <c>So</c>, Jude as <c>Jude</c>, and 1 John once as <c>1JHN</c>.
    /// </summary>
    private static readonly Dictionary<string, string> Spellings = new(StringComparer.Ordinal)
    {
        ["So"] = "Sng",
        ["Jude"] = "Jud",
        ["1JHN"] = "1Jn",
    };

    /// <summary>How deep the transcription indents a line nested under another.</summary>
    private const int NestedIndent = 1;

    private const string Joiner = " — ";

    /// <summary>Every line of an entry that cites something, with the heading it is filed under.</summary>
    public static IEnumerable<Line> Lines(string entry)
    {
        string? parent = null;
        foreach (var raw in entry.Split('\n'))
        {
            var text = raw.TrimEnd('\r');
            if (text.Trim().Length == 0)
            {
                continue;
            }

            var nested = text.Length - text.TrimStart().Length >= NestedIndent;
            var (heading, citations, unread) = Read(text);
            if (!nested)
            {
                parent = heading;
            }

            if (citations.Count == 0 && unread == 0)
            {
                continue;
            }

            var filed = nested && parent is not null
                ? heading is null ? parent : parent + Joiner + heading
                : heading;
            yield return new Line(filed, citations, unread);
        }
    }

    /// <summary>One line: the words before its first citation, the citations, and how many pieces did not read.</summary>
    public static (string? Heading, List<Citation> Citations, int Unread) Read(string line)
    {
        var first = FirstBook(line);
        var heading = Heading(first < 0 ? line : line[..first]);
        if (first < 0)
        {
            return (heading, [], 0);
        }

        var citations = new List<Citation>();
        var unread = 0;
        int? book = null;
        foreach (var segment in Separators().Split(line[first..]))
        {
            var words = segment.Trim();
            if (words.Length == 0)
            {
                continue;
            }

            var code = Code().Match(words);
            if (code.Success && Ordinal(code.Groups["code"].Value) is { } named)
            {
                book = named;
                words = words[code.Length..];
            }

            if (book is null || !Items(book.Value, words, citations))
            {
                unread++;
            }
        }

        return (heading, citations, unread);
    }

    /// <summary>
    /// The verses of one segment — <c>6:3-15,50-53</c>, <c>24</c>, <c>13:8-14:5</c> — added to the
    /// list; false where any of it does not read.
    /// </summary>
    private static bool Items(int book, string words, List<Citation> citations)
    {
        var read = new List<Citation>();
        int? chapter = null;
        foreach (var raw in words.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            var item = Verses().Match(raw);
            if (!item.Success)
            {
                return false;
            }

            var number = Number(item.Groups["a"]);
            if (item.Groups["b"].Success)
            {
                chapter = number;
                if (!Range(book, number, Number(item.Groups["b"]), item, read))
                {
                    return false;
                }
            }
            else if (chapter is null)
            {
                if (item.Groups["to"].Success)
                {
                    return false;
                }

                read.Add(new Citation(book, number, null, null));
            }
            else if (!Range(book, chapter.Value, number, item, read))
            {
                return false;
            }
        }

        citations.AddRange(read);
        return read.Count > 0;
    }

    /// <summary>A verse or a run of them from <paramref name="verse"/>, which may run into a later chapter.</summary>
    private static bool Range(int book, int chapter, int verse, Match item, List<Citation> read)
    {
        if (!item.Groups["to"].Success)
        {
            read.Add(new Citation(book, chapter, verse, verse));
            return true;
        }

        var to = Number(item.Groups["to"]);
        if (!item.Groups["toVerse"].Success)
        {
            if (to < verse)
            {
                return false;
            }

            read.Add(new Citation(book, chapter, verse, to));
            return true;
        }

        var last = Number(item.Groups["toVerse"]);
        if (to <= chapter)
        {
            return false;
        }

        read.Add(new Citation(book, chapter, verse, null));
        for (var between = chapter + 1; between < to; between++)
        {
            read.Add(new Citation(book, between, null, null));
        }

        read.Add(new Citation(book, to, 1, last));
        return true;
    }

    /// <summary>Where the first citation starts: the first book code followed by a chapter number.</summary>
    private static int FirstBook(string line)
    {
        foreach (Match match in Code().Matches(line))
        {
            if (Ordinal(match.Groups["code"].Value) is not null)
            {
                return match.Index;
            }
        }

        return -1;
    }

    private static string? Heading(string words)
    {
        var heading = words.Trim().TrimStart('-').Trim().TrimEnd(',', ':', ';').Trim();
        return heading.Length == 0 ? null : heading;
    }

    /// <summary>
    /// The canonical book a code names, or null. Codes are read as the transcription writes them,
    /// in capitals, so that a heading's ordinary word — <em>Job</em>, <em>Acts</em> — is not a book.
    /// </summary>
    private static int? Ordinal(string code)
    {
        var spelled = Spellings.GetValueOrDefault(code);
        if (spelled is null && !code.Equals(code.ToUpperInvariant(), StringComparison.Ordinal))
        {
            return null;
        }

        return BookCodes.TryGetOrdinal(spelled ?? code, out var ordinal) ? ordinal : null;
    }

    private static int Number(Group group) => int.Parse(group.Value, CultureInfo.InvariantCulture);

    [GeneratedRegex(@"(?<![\p{L}\d])(?<code>[1-3]?[A-Za-z]{2,4})\s+(?=\d)")]
    private static partial Regex Code();

    /// <summary>What separates citations: a semicolon, a full stop, or the word <c>with</c>.</summary>
    [GeneratedRegex(@";|\.(?=\s|$)|\bwith\b")]
    private static partial Regex Separators();

    [GeneratedRegex(@"^(?<a>\d+)(?:\s*:\s*(?<b>\d+))?(?:\s*-\s*(?<to>\d+)(?:\s*:\s*(?<toVerse>\d+))?)?$")]
    private static partial Regex Verses();
}
