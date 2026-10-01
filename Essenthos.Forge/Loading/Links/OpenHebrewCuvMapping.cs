using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Essenthos.Core.Loading.Links;

/// <param name="Text">What the run prints, punctuation and all.</param>
/// <param name="Positions">
/// The Open Hebrew Bible's running numbers of the BHS words it renders, in the order written; empty
/// for text the file maps to nothing.
/// </param>
internal sealed record OhbRun(string Text, IReadOnlyList<int> Positions);

/// <param name="Chapter">The chapter in the King James's numbering, which the Union Version is printed in.</param>
internal sealed record OhbVerse(int Book, int Chapter, int Verse, IReadOnlyList<OhbRun> Runs);

/// <summary>
/// The Open Hebrew Bible's mapping of the Chinese Union Version's Old Testament to BHS:
/// <c>009-BHS-mapping-CUV/CUV-OT-mapped-to-BHS.csv</c>, one verse per line, the Chinese text with a
/// marker on every span naming the BHS word it renders.
///
/// <para>
/// **The spans are FHL's.** The file is FHL's tagged text, in which each span carries the Strong
/// numbers FHL gave it, and Eliran Wong put after each number the running number of the BHS word it
/// stands for — the same numbers his King James mapping writes, so they reach BHSA the same way
/// (<see cref="OldTestamentLinkLoader.WordsByRunningNumber"/>). A span opens with <c>％〈…〉</c>; a
/// further number of the same span follows it as <c>〈…〉&lt;sup&gt;S&lt;/sup&gt;</c>, and belongs
/// to the text printed just before it, whether or not that text opened a span — <em>面</em> in
/// Genesis 1:2 is printed bare and then given H5921 and H6440. A marker with no running number —
/// a parsing code, or a prefix FHL numbered and BHS does not divide the same way — names no word.
/// </para>
/// </summary>
internal static partial class OpenHebrewCuvMapping
{
    private const int Columns = 5;
    private const char NumberSeparator = '＝';
    private const char FieldSeparator = '｜';

    public static IReadOnlyList<OhbVerse> Read(string path)
    {
        var verses = new List<OhbVerse>(23_200);
        foreach (var line in File.ReadLines(path, Encoding.UTF8))
        {
            var columns = line.Split('\t');
            if (columns.Length != Columns
                || !int.TryParse(columns[1], CultureInfo.InvariantCulture, out var book)
                || !int.TryParse(columns[2], CultureInfo.InvariantCulture, out var chapter)
                || !int.TryParse(columns[3], CultureInfo.InvariantCulture, out var verse))
            {
                continue;
            }

            verses.Add(new OhbVerse(book, chapter, verse, Runs(columns[4])));
        }

        return verses;
    }

    internal static List<OhbRun> Runs(string body)
    {
        var runs = new List<(StringBuilder Text, List<int> Positions)>();
        foreach (Match piece in Pieces().Matches(body))
        {
            if (piece.Groups["span"].Success)
            {
                runs.Add((new StringBuilder(piece.Groups["text"].Value), [.. Position(piece.Groups["span"].Value)]));
            }
            else if (piece.Groups["more"].Success)
            {
                if (runs.Count == 0)
                {
                    runs.Add((new StringBuilder(), []));
                }

                runs[^1].Positions.AddRange(Position(piece.Groups["more"].Value));
            }
            else
            {
                runs.Add((new StringBuilder(Tags().Replace(piece.Value, string.Empty)), []));
            }
        }

        return [.. runs.Select(run => new OhbRun(run.Text.ToString(), run.Positions))];
    }

    /// <summary>The running number a marker names: <c>H8064＝c1｜7｜E70007</c> names 7.</summary>
    private static IEnumerable<int> Position(string marker)
    {
        var at = marker.IndexOf(NumberSeparator);
        if (at < 0)
        {
            yield break;
        }

        var fields = marker[(at + 1)..].Split(FieldSeparator);
        if (fields.Length >= 2 && int.TryParse(fields[1], CultureInfo.InvariantCulture, out var position))
        {
            yield return position;
        }
    }

    [GeneratedRegex(@"％〈(?<span>[^〉]*)〉(?<text>.*?)</a>|〈(?<more>[^〉]*)〉<sup>[^<]*</sup></a>|[^％〈]+")]
    private static partial Regex Pieces();

    [GeneratedRegex("<[^>]*>")]
    private static partial Regex Tags();
}
