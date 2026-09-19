using System.Globalization;
using System.Text.RegularExpressions;
using Essenthos.Core.Database.Entities.Enums;

namespace Essenthos.Core.Berean;

/// <summary>
/// Where the Berean starts a paragraph or a line, read from its translation tables and placed on the
/// words of the published edition.
///
/// The published file is one verse a line and prints no paragraphs; the tables print them, as a
/// <c>&lt;p class=|…|&gt;</c> in the <c>Par</c> column of the row whose English opens the paragraph.
/// So the mark is found on a row of the table and has to land on a word of the edition, and the two
/// do not tokenise the same English the same way — the tables rebuild the published verse exactly
/// only nine times in ten.
///
/// <para>
/// A mark is therefore placed by walking the verse's English rows in the Berean's own order and
/// following them through the published words, a few words of slack at a time, and it lands only
/// where the row's first word is the published word the walk has reached. Measured over the whole
/// Bible that places 38,995 of 38,996 marks; the one left is Judges 16:14, where the table orders
/// the English differently from the edition. A mark that cannot be confirmed is dropped rather than
/// put on a neighbouring word — and the read refuses outright past a handful, because that many
/// would mean the tables and the edition are no longer the same text.
/// </para>
/// </summary>
internal static partial class BereanParagraphs
{
    private const int EnglishOrder = 2;
    private const int VerseIndex = 3;
    private const int Reference = 12;
    private const int Paragraph = 15;
    private const int Rendering = 18;

    /// <summary>The widest column this reads.</summary>
    private const int Columns = Rendering + 1;

    /// <summary>
    /// How far ahead of the walk a rendered word is looked for among the published ones. The
    /// published edition prints words the tables split or leave out — a possessive, a supplied
    /// article — and a few words of slack keeps the walk on the verse without letting it wander.
    /// </summary>
    private const int Slack = 4;

    /// <summary>How many marks may fail to be placed before the read refuses. Measured: one.</summary>
    private const int MostUnplaced = 10;

    /// <summary>
    /// What each class the tables print means for a reader. A plain paragraph, the first line of a
    /// stanza or a list, a psalm's heading and an inscription begin a paragraph; every other line of
    /// poetry, of a list or of a Selah begins a line inside one. Red-letter classes are the same
    /// breaks in the words of Jesus.
    /// </summary>
    private static readonly Dictionary<string, TextBreak> Classes = new(StringComparer.Ordinal)
    {
        ["reg"] = TextBreak.Paragraph,
        ["red"] = TextBreak.Paragraph,
        ["indent1stline"] = TextBreak.Paragraph,
        ["indent1stlinered"] = TextBreak.Paragraph,
        ["list1stline"] = TextBreak.Paragraph,
        ["tab1stline"] = TextBreak.Paragraph,
        ["tab1stlinered"] = TextBreak.Paragraph,
        ["pshdg"] = TextBreak.Paragraph,
        ["inscrip"] = TextBreak.Paragraph,
        ["subhdg"] = TextBreak.Paragraph,
        ["indent1"] = TextBreak.Line,
        ["indent2"] = TextBreak.Line,
        ["indentred1"] = TextBreak.Line,
        ["indentred2"] = TextBreak.Line,
        ["list1"] = TextBreak.Line,
        ["list2"] = TextBreak.Line,
        ["tab1"] = TextBreak.Line,
        ["selah"] = TextBreak.Line,
    };

    [GeneratedRegex(@"class=\|(?<name>[a-z0-9]+)\|")]
    private static partial Regex Class { get; }

    /// <param name="published">The published words of each verse, by the reference the edition writes.</param>
    /// <returns>For each verse that has any, the one-based positions of the words a break opens.</returns>
    public static Dictionary<string, Dictionary<int, TextBreak>> Read(
        string tablesPath,
        IReadOnlyDictionary<string, List<BereanWord>> published)
    {
        var placed = new Dictionary<string, Dictionary<int, TextBreak>>(StringComparer.Ordinal);
        var unplaced = new List<string>();

        foreach (var (reference, rows) in Verses(tablesPath))
        {
            if (!published.TryGetValue(reference, out var words))
            {
                continue;
            }

            var at = 0;
            TextBreak? pending = null;
            foreach (var row in rows)
            {
                pending = Stronger(pending, row.Break);
                var rendered = BereanWords.Rendering(row.English);
                if (rendered.Count == 0)
                {
                    continue;
                }

                if (pending is { } opening)
                {
                    if (at < words.Count && BereanWords.Same(words[at].Surface, rendered[0]))
                    {
                        if (!placed.TryGetValue(reference, out var marks))
                        {
                            marks = [];
                            placed[reference] = marks;
                        }

                        marks[at + 1] = opening;
                    }
                    else
                    {
                        unplaced.Add(reference);
                    }

                    pending = null;
                }

                foreach (var word in rendered)
                {
                    for (var next = at; next < Math.Min(at + Slack, words.Count); next++)
                    {
                        if (BereanWords.Same(words[next].Surface, word))
                        {
                            at = next + 1;
                            break;
                        }
                    }
                }
            }
        }

        if (unplaced.Count > MostUnplaced)
        {
            throw new InvalidOperationException(
                $"{unplaced.Count} of the Berean's paragraph marks could not be placed on the published words — "
                + $"the first in {unplaced[0]}. One was measured when this was written; this many means the "
                + "tables and bsb.txt are no longer the same edition. Fetch both from the same release.");
        }

        return placed;
    }

    private readonly record struct Row(int EnglishOrder, TextBreak? Break, string English);

    /// <summary>Each verse's rows in the order the Berean prints its English, padding rows included.</summary>
    private static IEnumerable<(string Reference, List<Row> Rows)> Verses(string path)
    {
        using var reader = new StreamReader(path);
        reader.ReadLine();

        var rows = new List<Row>(64);
        var reference = string.Empty;
        var verse = int.MinValue;

        while (reader.ReadLine() is { } line)
        {
            var cells = line.Split('\t');
            if (cells.Length < Columns
                || !int.TryParse(cells[VerseIndex].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var index))
            {
                continue;
            }

            if (index != verse && rows.Count > 0)
            {
                yield return (reference, Ordered(rows));
                rows = new List<Row>(64);
                reference = string.Empty;
            }

            verse = index;
            if (cells[Reference].Trim() is { Length: > 0 } stated)
            {
                reference = stated;
            }

            int.TryParse(cells[EnglishOrder].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var order);
            rows.Add(new Row(order, Break(cells[Paragraph]), cells[Rendering]));
        }

        if (rows.Count > 0)
        {
            yield return (reference, Ordered(rows));
        }
    }

    private static List<Row> Ordered(List<Row> rows)
    {
        rows.Sort((a, b) => a.EnglishOrder.CompareTo(b.EnglishOrder));
        return rows;
    }

    private static TextBreak? Break(string cell)
    {
        TextBreak? found = null;
        foreach (Match match in Class.Matches(cell))
        {
            var name = match.Groups["name"].Value;
            if (!Classes.TryGetValue(name, out var kind))
            {
                throw new InvalidOperationException(
                    $"The Berean tables mark a paragraph with the class \"{name}\", which this reader has not been "
                    + "told about. Decide whether it opens a paragraph or a line and add it to Classes.");
            }

            found = Stronger(found, kind);
        }

        return found;
    }

    private static TextBreak? Stronger(TextBreak? held, TextBreak? next) =>
        held == TextBreak.Paragraph || next is null ? held : next;
}
