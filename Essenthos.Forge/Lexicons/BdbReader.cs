using System.Xml.Linq;
using Essenthos.Core.Commentary;
using Essenthos.Core.Corpus;

namespace Essenthos.Core.Lexicons;

/// <summary>
/// Brown-Driver-Briggs as the Open Scriptures Hebrew Bible project marks it up (<c>BrownDriverBriggs.xml</c>), each
/// article under the Strong numbers its lexical index (<c>LexicalIndex.xml</c>) files it at.
///
/// <para>
/// The index is the only keying taken: a Strong number reaches the BDB articles its index entries name and no others,
/// in BDB's own order (the index letters them as BDB numbers them), so H1254 has I. בָּרָא "shape" before II. בָּרָא "be fat". An index entry naming no number
/// (a root, a letter) gives no row. The edition is unfinished and says so per article (<c>status</c>): the status is
/// kept, so the page can say how much of the printed article the digital one holds.
/// </para>
/// <para>
/// In an article: Hebrew words (<c>w</c>), whose <c>src</c> names another article, are linked to the Strong number of
/// that article when the index files it at exactly one; definitions (<c>def</c>) and stems are bold; parts of speech,
/// aspects, transliterations and foreign words italic; references (<c>ref r</c>) cite their verse; each sense is a
/// paragraph one level below the one it is in, opening with its number. Page breaks and the status are not text.
/// </para>
/// </summary>
internal static class BdbReader
{
    public const string Folder = "BrownDriverBriggs";

    public const string Lexicon = $"{Folder}/BrownDriverBriggs.xml";

    public const string Index = $"{Folder}/LexicalIndex.xml";

    private static readonly XNamespace Ns = "http://openscriptures.github.com/morphhb/namespace";

    public static List<LexiconArticleRow> Read(string resourcesPath, Dictionary<string, int>? unread = null) =>
        Read(XDocument.Load(Path.Combine(resourcesPath, Lexicon), LoadOptions.PreserveWhitespace),
            XDocument.Load(Path.Combine(resourcesPath, Index)), unread);

    public static List<LexiconArticleRow> Read(XDocument lexicon, XDocument index, Dictionary<string, int>? unread = null)
    {
        // Where one number stands over several articles the index letters them as BDB numbers them (aug a for I, b for II).
        var filed = index.Descendants(Ns + "xref")
            .Select(xref => (Number: LexiconText.Strong(xref.Attribute("strong")?.Value, 'H'), Article: xref.Attribute("bdb")?.Value,
                Letter: xref.Attribute("aug")?.Value ?? string.Empty))
            .Where(x => x.Number is not null && !string.IsNullOrEmpty(x.Article))
            .OrderBy(x => int.Parse(x.Number!.AsSpan(1)))
            .ThenBy(x => x.Letter, StringComparer.Ordinal)
            .Select(x => (Number: x.Number!, Article: x.Article!))
            .Distinct()
            .ToList();
        var numbersOf = filed.GroupBy(f => f.Article, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Select(f => f.Number).Distinct().ToList(), StringComparer.Ordinal);
        var articles = lexicon.Descendants(Ns + "entry")
            .Where(e => e.Attribute("id") is not null)
            .ToDictionary(e => e.Attribute("id")!.Value, StringComparer.Ordinal);

        var written = new Dictionary<string, (string Headword, string? Status, string Body)?>(StringComparer.Ordinal);
        var rows = new List<LexiconArticleRow>();
        foreach (var (number, id) in filed)
        {
            if (!written.TryGetValue(id, out var article))
            {
                article = articles.TryGetValue(id, out var entry) ? Article(entry, numbersOf, unread) : null;
                written[id] = article;
            }

            if (article is { } made)
            {
                rows.Add(new LexiconArticleRow(LexiconWorks.Bdb.Id, number, 0, id, made.Headword, made.Status, made.Body, Credit));
            }
        }

        return [.. LexiconText.Numbered(rows)];
    }

    /// <summary>The credit on every row, which the dataset declaration claims as its prefix.</summary>
    public const string Credit = LexiconWorks.BdbSource + ", github.com/openscriptures/HebrewLexicon at 21c9add, CC BY 4.0";

    private static (string Headword, string? Status, string Body)? Article(
        XElement entry, Dictionary<string, List<string>> numbersOf, Dictionary<string, int>? unread)
    {
        var writer = new ArticleWriter();
        writer.Paragraph(0);
        if (entry.Attribute("cite")?.Value == "full")
        {
            writer.Text("† ");
        }

        Children(entry, writer, numbersOf, unread);
        var headword = entry.Elements(Ns + "w").FirstOrDefault()?.Value.Trim();
        return writer.Json() is { } body && !string.IsNullOrEmpty(headword)
            ? (headword, entry.Element(Ns + "status")?.Value.Trim(), body)
            : null;
    }

    private static void Children(XElement parent, ArticleWriter writer, Dictionary<string, List<string>> numbersOf, Dictionary<string, int>? unread)
    {
        foreach (var node in parent.Nodes())
        {
            switch (node)
            {
                case XText text:
                    writer.Text(text.Value);
                    break;
                case XElement element:
                    Element(element, writer, numbersOf, unread);
                    break;
            }
        }
    }

    private static void Element(XElement element, ArticleWriter writer, Dictionary<string, List<string>> numbersOf, Dictionary<string, int>? unread)
    {
        switch (element.Name.LocalName)
        {
            case "status":
            case "page":
                return;
            case "w":
                var linked = element.Attribute("src")?.Value is { } source && numbersOf.TryGetValue(source, out var numbers) && numbers.Count == 1
                    ? numbers[0]
                    : null;
                writer.Name(linked);
                Children(element, writer, numbersOf, unread);
                writer.Name(null);
                return;
            case "def":
            case "stem":
                Marked(element, CommentBuilder.Mark.Bold, writer, numbersOf, unread);
                return;
            case "pos":
            case "asp":
            case "em":
            case "foreign":
                Marked(element, CommentBuilder.Mark.Italic, writer, numbersOf, unread);
                return;
            case "ref":
                writer.Cite(LexiconText.Verse(element.Attribute("r")?.Value));
                Children(element, writer, numbersOf, unread);
                writer.Cite(null);
                return;
            case "sense":
                var depth = writer.Depth;
                writer.Paragraph(depth + 1);
                if (element.Attribute("n")?.Value is { Length: > 0 } n)
                {
                    writer.Push(CommentBuilder.Mark.Bold);
                    writer.Text($"{n}. ");
                    writer.Pop();
                }

                Children(element, writer, numbersOf, unread);
                writer.Paragraph(depth);
                return;
            default:
                if (unread is not null)
                {
                    unread[element.Name.LocalName] = unread.GetValueOrDefault(element.Name.LocalName) + 1;
                }

                Children(element, writer, numbersOf, unread);
                return;
        }
    }

    private static void Marked(XElement element, CommentBuilder.Mark mark, ArticleWriter writer, Dictionary<string, List<string>> numbersOf,
        Dictionary<string, int>? unread)
    {
        writer.Push(mark);
        Children(element, writer, numbersOf, unread);
        writer.Pop();
    }
}
