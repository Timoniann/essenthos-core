using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using Essenthos.Core.Commentary;
using Essenthos.Core.Corpus;

namespace Essenthos.Core.Lexicons;

/// <summary>
/// Liddell-Scott-Jones as STEP Bible formats it for the words of the Bible (TFLSJ, <c>TFLSJ-0-5624.txt</c>), each
/// article under the Strong number of its row's first column.
///
/// <para>
/// STEP parts some numbers by meaning or person (<c>G0863G</c>, <c>G0863H</c>, ...) and repeats the same LSJ article on
/// each part; an article is kept once under its number, and a number keeps as many articles as differ (G1 has
/// ἄλφα and the interjection ἆ). Rows whose text says LSJ has no entry carry Abbott-Smith's article or a note of
/// STEP's own instead, and are left out: Abbott-Smith is served as itself.
/// </para>
/// <para>
/// In an article: bold, italic and underline are kept; a line break starts a paragraph and a level marker
/// (<c>&lt;Level2&gt;__II</c>) a paragraph that many levels down, opening with its number; the sources STEP hides
/// behind the earliest date (<c>&lt;a title="NT.Rom.5.8, ..."&gt;NT&lt;/a&gt;</c>) are kept as hidden text after the
/// date, with their New Testament references citing the verse (the Septuagint's are in its own numbering and left
/// as text); a Hebrew number in brackets (<c>[H0002]</c>) names it.
/// </para>
/// </summary>
internal static partial class LsjReader
{
    public const string Folder = "STEPBibleLexicons";

    public const string Lexicon = $"{Folder}/TFLSJ-0-5624.txt";

    public const string Credit = LexiconWorks.LsjSource
        + ", by STEP Bible (Tyndale House, Cambridge) from the Perseus Digital Library's LSJ, read from STEPBible/STEPBible-Data at ae39711; "
        + "CC BY-SA 4.0 (Perseus), CC BY 4.0 (STEP Bible)";

    private static readonly Dictionary<string, int> NewTestament = new(StringComparer.Ordinal)
    {
        ["Matt"] = 40, ["Mat"] = 40, ["Matthew"] = 40, ["Mark"] = 41, ["Mrk"] = 41, ["Luke"] = 42, ["Luk"] = 42, ["John"] = 43, ["Jhn"] = 43,
        ["Act"] = 44, ["Acts"] = 44, ["Rom"] = 45, ["1Cor"] = 46, ["2Cor"] = 47, ["Gal"] = 48, ["Eph"] = 49, ["Phil"] = 50, ["Philp"] = 50,
        ["Php"] = 50, ["Col"] = 51, ["1Thes"] = 52, ["1Thess"] = 52, ["2Thes"] = 53, ["2Thess"] = 53, ["1Tim"] = 54, ["2Tim"] = 55,
        ["Tit"] = 56, ["Titus"] = 56, ["Philm"] = 57, ["Phlm"] = 57, ["Heb"] = 58, ["Jam"] = 59, ["Jas"] = 59, ["1Pet"] = 60,
        ["2Pet"] = 61, ["1John"] = 62, ["1Jn"] = 62, ["2John"] = 63, ["2Jn"] = 63, ["3John"] = 64, ["3Jn"] = 64, ["Jude"] = 65,
        ["Jud"] = 65, ["Rev"] = 66,
    };

    /// <summary>The books of one chapter, cited by verse alone (<c>NT.Jude.12</c>).</summary>
    private static readonly HashSet<int> OneChapter = [57, 63, 64, 65];

    public static List<LexiconArticleRow> Read(string resourcesPath, Dictionary<string, int>? unread = null) =>
        Read(File.ReadLines(Path.Combine(resourcesPath, Lexicon)), unread);

    public static List<LexiconArticleRow> Read(IEnumerable<string> lines, Dictionary<string, int>? unread = null)
    {
        var rows = new List<LexiconArticleRow>();
        var seen = new HashSet<(string Number, string Meaning)>();
        foreach (var line in lines)
        {
            var columns = line.Split('\t');
            if (columns.Length < 8 || !EStrong().IsMatch(columns[0]) || LexiconText.Strong(columns[0]) is not { } number)
            {
                continue;
            }

            var meaning = columns[7].Trim();
            if (meaning.Length == 0 || meaning.Contains("LSJ has no entry", StringComparison.Ordinal)
                || meaning.Contains("Not in LSJ", StringComparison.Ordinal) || !seen.Add((number, meaning)))
            {
                continue;
            }

            if (Article(meaning, unread) is { } body)
            {
                var headword = columns[3].Trim();
                rows.Add(new LexiconArticleRow(LexiconWorks.Lsj.Id, number, 0, columns[1].Split(' ')[0].Trim(),
                    headword.Length > 0 ? headword : number, null, body, Credit));
            }
        }

        return [.. LexiconText.Numbered(rows.OrderBy(r => int.Parse(r.StrongNumber.AsSpan(1), CultureInfo.InvariantCulture)))];
    }

    private static string? Article(string markup, Dictionary<string, int>? unread)
    {
        var writer = new ArticleWriter();
        writer.Paragraph(0);
        var at = 0;
        string? hiddenSources = null;
        foreach (Match tag in Tag().Matches(markup))
        {
            if (tag.Index > at)
            {
                Text(markup[at..tag.Index], writer);
            }

            at = tag.Index + tag.Length;
            var closing = tag.Groups[1].Length > 0;
            var name = tag.Groups[2].Value.ToLowerInvariant();
            switch (name)
            {
                case "b":
                    Toggle(writer, CommentBuilder.Mark.Bold, closing);
                    break;
                case "i":
                    Toggle(writer, CommentBuilder.Mark.Italic, closing);
                    break;
                case "u":
                    Toggle(writer, CommentBuilder.Mark.Underlined, closing);
                    break;
                case "br":
                    writer.Paragraph(0);
                    break;
                case "level1" or "level2" or "level3" or "level4" when !closing:
                    writer.Paragraph(name[^1] - '1');
                    break;
                case "a" when !closing:
                    // The label (the earliest date) is the anchor's text; what it hides follows it, written when it closes.
                    hiddenSources = Attribute(tag.Groups[3].Value, "title");
                    break;
                case "a":
                    if (hiddenSources is { Length: > 0 } sources)
                    {
                        Sources(WebUtility.HtmlDecode(sources).Trim(), writer);
                    }

                    hiddenSources = null;
                    break;
                case "level1" or "level2" or "level3" or "level4" or "ref" or "span" or "date" or "author":
                    break;
                default:
                    if (unread is not null)
                    {
                        unread[name] = unread.GetValueOrDefault(name) + 1;
                    }

                    break;
            }
        }

        if (at < markup.Length)
        {
            Text(markup[at..], writer);
        }

        return writer.Json();
    }

    private static void Toggle(ArticleWriter writer, CommentBuilder.Mark mark, bool closing)
    {
        if (closing)
        {
            writer.Pop();
        }
        else
        {
            writer.Push(mark);
        }
    }

    /// <summary>Text between tags: entities decoded, the level markers' underscores dropped, braces and bracketed numbers read.</summary>
    private static void Text(string raw, ArticleWriter writer)
    {
        var text = WebUtility.HtmlDecode(raw).Replace("__", string.Empty, StringComparison.Ordinal);
        var at = 0;
        foreach (Match match in Named().Matches(text))
        {
            writer.Text(text[at..match.Index]);
            if (match.Groups[1].Success)
            {
                writer.Text(match.Groups[1].Value);
            }
            else
            {
                writer.Text("[");
                writer.Name(LexiconText.Strong(match.Groups[2].Value));
                writer.Text(LexiconText.Strong(match.Groups[2].Value) ?? match.Groups[2].Value);
                writer.Name(null);
                writer.Text("]");
            }

            at = match.Index + match.Length;
        }

        writer.Text(text[at..]);
    }

    /// <summary>The sources behind a date, hidden, each New Testament reference citing its verse.</summary>
    private static void Sources(string sources, ArticleWriter writer)
    {
        writer.Push(CommentBuilder.Mark.Hidden);
        writer.Text(" ");
        var at = 0;
        foreach (Match match in NewTestamentReference().Matches(sources))
        {
            if (Verse(match) is not { } verse)
            {
                continue;
            }

            writer.Text(sources[at..match.Index]);
            writer.Cite(verse);
            writer.Text(match.Value);
            writer.Cite(null);
            at = match.Index + match.Length;
        }

        writer.Text(sources[at..]);
        writer.Pop();
    }

    private static int[]? Verse(Match match)
    {
        if (!NewTestament.TryGetValue(match.Groups[1].Value, out var book))
        {
            return null;
        }

        var first = int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture);
        if (match.Groups[3].Success)
        {
            return [book, first, int.Parse(match.Groups[3].Value, CultureInfo.InvariantCulture)];
        }

        return OneChapter.Contains(book) ? [book, 1, first] : null;
    }

    private static string? Attribute(string attributes, string name)
    {
        var match = Regex.Match(attributes, $"""{name}\s*=\s*"([^"]*)" """.TrimEnd(), RegexOptions.IgnoreCase);
        return match.Success ? match.Groups[1].Value : null;
    }

    [GeneratedRegex(@"^G\d{4,5}$")]
    private static partial Regex EStrong();

    [GeneratedRegex(@"<(/?)([A-Za-z][A-Za-z0-9]*)((?:[^>""]|""[^""]*"")*)/?>")]
    private static partial Regex Tag();

    [GeneratedRegex(@"\{([^{}]+)\}|\[([HG]\d{1,5}[A-Z]?)\]")]
    private static partial Regex Named();

    [GeneratedRegex(@"NT\.([1-3]?[A-Za-z]+)\.(\d+)(?:\.(\d+))?")]
    private static partial Regex NewTestamentReference();
}
