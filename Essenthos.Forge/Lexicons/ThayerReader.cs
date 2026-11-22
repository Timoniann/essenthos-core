using System.Globalization;
using System.Text.RegularExpressions;
using Essenthos.Core.Commentary;
using Essenthos.Core.Corpus;

namespace Essenthos.Core.Lexicons;

/// <summary>
/// Thayer's Greek-English Lexicon of the New Testament (1889) as the Every Promise project published it: 27 Markdown
/// files, each entry a heading (<c>### headword — transliteration</c>), the line <c>`Strong's G26`</c> and the article.
/// Each article goes under the one Strong number its entry names.
///
/// <para>
/// The edition was decoded from an e-Sword module and keeps its wrapper in places: an article may open with the label
/// <c>STRONGS NT 217:</c> and the headword twice over, both of which are dropped. Ninety-seven entries are no article of
/// Thayer's at all but a card of Strong-style fields (<c>- Original:</c>, <c>- Part(s) of speech:</c>) with a brief
/// gloss; they give no row, so the page never shows a gloss under Thayer's name. Ten numbers hold the article of
/// a neighbouring word (<see cref="Misfiled"/>) and give none either.
/// </para>
/// <para>
/// In an article: Greek and Hebrew are marked by their script; a paragraph opening with Thayer's own numbering (<c>I.</c>,
/// <c>A.</c>, <c>1.</c>, <c>a.</c>) is a sense, one level below the nearest sense of a higher kind it stands in, its marker
/// bold; the New Testament verses cited (<c>Mat 20:8</c>, <c>Mat 5:3-12</c>) are linked. Verses of the Old Testament and
/// the Apocrypha are not, as the edition does not say which numbering it follows.
/// </para>
/// </summary>
internal static partial class ThayerReader
{
    public const string Folder = "Thayer";

    public const string Book = $"{Folder}/book";

    public const string Credit = LexiconWorks.ThayerSource
        + " (1889), the Every Promise edition, github.com/imbennyhim/Every-Promise-Thayers at a31ff38, CC0";

    /// <summary>
    /// Numbers under which the module files the article of another word, read against Strong's lemma of each number
    /// (2026-11-22): the article of the word before it (G671 holds ἀποχωρέω's, G676 Ἄππιος's, G1463 ἔγκλημα's, G3190
    /// μέλας's, G3991 πελεκίζω's, G4877 συναντάω's, G5163 τροφός's), of one alphabetically near (G68 ἅπτω, G5104 τίω), or a
    /// stub on a different word (G2252, "ἤ μήν, see ἤ", for ἤμην). The number has no Thayer article rather than another
    /// word's.
    /// </summary>
    internal static readonly HashSet<string> Misfiled = ["G68", "G671", "G676", "G1463", "G2252", "G3190", "G3991", "G4877", "G5104", "G5163"];

    /// <summary>The kind of numbering a paragraph opens with, in the order Thayer nests them.</summary>
    private enum Marker
    {
        Roman,
        Capital,
        Number,
        Letter,
    }

    private static readonly Dictionary<string, int> NewTestament = new(StringComparer.Ordinal)
    {
        ["Mat"] = 40, ["Mar"] = 41, ["Luk"] = 42, ["Joh"] = 43, ["Act"] = 44, ["Rom"] = 45, ["1Co"] = 46, ["2Co"] = 47, ["Gal"] = 48,
        ["Eph"] = 49, ["Php"] = 50, ["Col"] = 51, ["1Th"] = 52, ["2Th"] = 53, ["1Ti"] = 54, ["2Ti"] = 55, ["Tit"] = 56, ["Phm"] = 57,
        ["Heb"] = 58, ["Jas"] = 59, ["1Pe"] = 60, ["2Pe"] = 61, ["1Jn"] = 62, ["2Jn"] = 63, ["3Jn"] = 64, ["Jud"] = 65, ["Rev"] = 66,
    };

    /// <summary>What the reading leaves out, by Strong number, so a run can say how much.</summary>
    internal sealed class Left
    {
        /// <summary>Entries that are a card of Strong-style fields, not an article of Thayer's.</summary>
        public List<string> Cards { get; } = [];

        /// <summary>Entries the module numbers between Strong's numbers (<c>STRONGS NT 255a:</c>), which no Strong number names.</summary>
        public List<string> Extended { get; } = [];

        /// <summary>Numbers whose entry is the article of another word (see <see cref="Misfiled"/>).</summary>
        public List<string> Misfiled { get; } = [];
    }

    /// <summary>The entries of every file under <c>Thayer/book</c>, each under its number, in Strong's order.</summary>
    public static List<LexiconArticleRow> Read(string resourcesPath, Left? left = null) =>
        Read(Directory.GetFiles(Path.Combine(resourcesPath, Book), "*.md")
            .Where(file => char.IsDigit(Path.GetFileName(file)[0]))
            .Order(StringComparer.Ordinal)
            .Select(File.ReadAllText), left);

    public static List<LexiconArticleRow> Read(IEnumerable<string> files, Left? left = null)
    {
        var rows = new List<LexiconArticleRow>();
        foreach (var file in files)
        {
            var text = file.Replace("\r\n", "\n", StringComparison.Ordinal);
            var starts = Entry().Matches(text);
            for (var i = 0; i < starts.Count; i++)
            {
                var start = starts[i];
                var end = i + 1 < starts.Count ? starts[i + 1].Index : text.Length;
                var body = text[(start.Index + start.Length)..end].Trim();
                if (body.EndsWith("---", StringComparison.Ordinal))
                {
                    body = body[..^3].TrimEnd();
                }

                if (LexiconText.Strong(start.Groups["number"].Value) is not { } number || body.Length == 0)
                {
                    continue;
                }

                if (Card().IsMatch(body))
                {
                    left?.Cards.Add(number);
                    continue;
                }

                if (Misfiled.Contains(number))
                {
                    left?.Misfiled.Add(number);
                    continue;
                }

                var heading = start.Groups["heading"].Value.Trim();
                var dash = heading.IndexOf(" — ", StringComparison.Ordinal);
                var headword = Headword(dash < 0 ? heading : heading[..dash], body);
                if (Article(Entries(body, number, left)) is { } json)
                {
                    rows.Add(new LexiconArticleRow(LexiconWorks.Thayer.Id, number, 0, number, headword, null, json, Credit));
                }
            }
        }

        return [.. LexiconText.Numbered(rows.OrderBy(r => int.Parse(r.StrongNumber.AsSpan(1))))];
    }

    /// <summary>The headword as the heading gives it, without the stray colon or bracket the module leaves on some; the article's own first word where the heading is only the module's label.</summary>
    private static string Headword(string heading, string body)
    {
        var word = heading.Trim().TrimStart('[').TrimEnd(':').Trim();
        if (!word.StartsWith("STRONGS", StringComparison.Ordinal))
        {
            return word;
        }

        var opening = Label().Replace(body, string.Empty, 1).TrimStart();
        var end = opening.IndexOfAny([' ', ',', ';', ':', '(']);
        return end > 0 ? opening[..end] : opening;
    }

    /// <summary>
    /// The entries the module put under one Strong number, each as its own text: what stands before its first label
    /// (a form that refers to another), then each entry labelled with the number itself. An entry labelled with a
    /// letter after the number (<c>255a</c>) is a word Strong's numbering lacks, put there for its place in the
    /// alphabet, and belongs to no number: it is left out. The label goes, and the headword the module writes twice.
    /// </summary>
    private static List<string> Entries(string body, string number, Left? left)
    {
        var labels = Label().Matches(body);
        var entries = new List<string>();
        var first = labels.Count > 0 ? body[..labels[0].Index] : body;
        if (first.Trim().Length > 0)
        {
            entries.Add(first.Trim());
        }

        for (var i = 0; i < labels.Count; i++)
        {
            var end = i + 1 < labels.Count ? labels[i + 1].Index : body.Length;
            var entry = body[(labels[i].Index + labels[i].Length)..end].Trim();
            if (labels[i].Groups["letter"].Success || labels[i].Groups["number"].Value != number[1..])
            {
                left?.Extended.Add(number);
                continue;
            }

            // The module writes the headword before the article's own, which opens with it: one copy of the two.
            for (var length = Math.Min(40, entry.Length / 2); length >= 1; length--)
            {
                if (string.CompareOrdinal(entry, 0, entry, length, length) == 0 && entry[..length].All(char.IsLetter))
                {
                    entry = entry[length..];
                    break;
                }
            }

            entries.Add(entry);
        }

        return entries;
    }

    private static string? Article(List<string> entries)
    {
        var writer = new ArticleWriter();
        foreach (var entry in entries)
        {
            Senses(entry, writer);
        }

        return writer.Json();
    }

    /// <summary>One entry's paragraphs, its numbering nested from the top.</summary>
    private static void Senses(string body, ArticleWriter writer)
    {
        var open = new List<Marker>();
        foreach (var raw in ParagraphBreak().Split(body))
        {
            var paragraph = raw.Replace('\n', ' ').Trim();
            if (paragraph.Length == 0)
            {
                continue;
            }

            var text = paragraph;
            if (Numbering().Match(paragraph) is { Success: true } numbering)
            {
                var kind = KindOf(numbering.Groups["mark"].Value);
                open.RemoveAll(m => m >= kind);
                open.Add(kind);
                writer.Paragraph(open.Count);
                writer.Push(CommentBuilder.Mark.Bold);
                writer.Text(numbering.Value.TrimEnd());
                writer.Pop();
                text = " " + paragraph[numbering.Length..];
            }
            else
            {
                writer.Paragraph(open.Count);
            }

            Verses(text, writer);
        }
    }

    private static Marker KindOf(string mark) =>
        char.IsDigit(mark[0]) ? Marker.Number
        : char.IsLower(mark[0]) ? Marker.Letter
        : mark.Length > 1 || mark is "I" or "V" or "X" ? Marker.Roman
        : Marker.Capital;

    /// <summary>The text with the New Testament verses it cites made links.</summary>
    private static void Verses(string text, ArticleWriter writer)
    {
        var at = 0;
        foreach (Match match in Reference().Matches(text))
        {
            if (Verse(match) is not { } verse)
            {
                continue;
            }

            writer.Text(text[at..match.Index]);
            writer.Cite(verse);
            writer.Text(match.Value);
            writer.Cite(null);
            at = match.Index + match.Length;
        }

        writer.Text(text[at..]);
    }

    /// <summary>[book, chapter, verse], or with a run [book, chapter, verse, last chapter, last verse]; null for a book that is not in the New Testament.</summary>
    internal static int[]? Verse(Match match)
    {
        if (!NewTestament.TryGetValue(match.Groups["book"].Value, out var book))
        {
            return null;
        }

        var chapter = int.Parse(match.Groups["chapter"].Value, CultureInfo.InvariantCulture);
        var verse = int.Parse(match.Groups["verse"].Value, CultureInfo.InvariantCulture);
        if (chapter < 1 || verse < 1)
        {
            return null;
        }

        if (!match.Groups["last"].Success)
        {
            return [book, chapter, verse];
        }

        var lastChapter = match.Groups["lastChapter"].Success ? int.Parse(match.Groups["lastChapter"].Value, CultureInfo.InvariantCulture) : chapter;
        var last = int.Parse(match.Groups["last"].Value, CultureInfo.InvariantCulture);
        return lastChapter > chapter || (lastChapter == chapter && last > verse) ? [book, chapter, verse, lastChapter, last] : [book, chapter, verse];
    }

    [GeneratedRegex(@"^### (?<heading>[^\n]+)\n`Strong's (?<number>G\d+)`\n", RegexOptions.Multiline)]
    private static partial Regex Entry();

    [GeneratedRegex(@"^STRONGS NT (?<number>\d+)(?<letter>[a-z])?: ", RegexOptions.Multiline)]
    private static partial Regex Label();

    [GeneratedRegex(@"^- Part\(s\) of speech:", RegexOptions.Multiline)]
    private static partial Regex Card();

    [GeneratedRegex(@"\n{2,}")]
    private static partial Regex ParagraphBreak();

    [GeneratedRegex(@"^(?<mark>[IVX]+|[A-D]|\d+|[a-z])\. ")]
    private static partial Regex Numbering();

    [GeneratedRegex(@"\b(?<book>[123]?[A-Z][a-z]{1,2}) (?<chapter>\d+):(?<verse>\d+)(?:-(?:(?<lastChapter>\d+):)?(?<last>\d+))?")]
    private static partial Regex Reference();
}
