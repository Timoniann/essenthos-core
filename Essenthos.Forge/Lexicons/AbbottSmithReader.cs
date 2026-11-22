using System.Xml.Linq;
using Essenthos.Core.Commentary;
using Essenthos.Core.Corpus;

namespace Essenthos.Core.Lexicons;

/// <summary>
/// Abbott-Smith's Manual Greek Lexicon of the New Testament as the translatable-exegetical-tools project marks it up in
/// TEI (<c>abbott-smith.tei.xml</c>), each article under the Strong numbers its own <c>n</c> names
/// (<c>&lt;entry n="ἅγιος|G40|G39"&gt;</c>); an article naming none gives no row.
///
/// <para>
/// In an article: the headword (<c>orth</c>) and glosses are bold, emphasis italic; references (<c>ref osisRef</c>)
/// cite their verse; the Hebrew a Septuagint word renders (<c>foreign xml:lang="heb" n="H160"</c>) is linked to the
/// Strong number the edition gives it when it gives one; a Greek word cross-referred to (<c>ref</c> holding Greek) is
/// linked to the number of the article of that headword when exactly one article has it; each sense is a paragraph one
/// level below the one it is in, opening with its number, and the synonyms note (<c>re</c>) is a paragraph of its own.
/// The count of New Testament occurrences the edition adds is not the book's text and is left out; a note of the
/// edition's own (<c>note</c>, "Abbott-Smith does not include the article here") is marked as a note, never the headword.
/// </para>
/// </summary>
internal static class AbbottSmithReader
{
    public const string Folder = "AbbottSmith";

    public const string Lexicon = $"{Folder}/abbott-smith.tei.xml";

    private static readonly XNamespace Ns = "http://www.crosswire.org/2013/TEIOSIS/namespace";

    private static readonly XNamespace Xml = XNamespace.Xml;

    public const string Credit = LexiconWorks.AbbottSmithSource
        + " (1922), github.com/translatable-exegetical-tools/Abbott-Smith at 8c00cb2, public domain";

    public static List<LexiconArticleRow> Read(string resourcesPath, Dictionary<string, int>? unread = null) =>
        Read(XDocument.Load(Path.Combine(resourcesPath, Lexicon), LoadOptions.PreserveWhitespace), unread);

    public static List<LexiconArticleRow> Read(XDocument tei, Dictionary<string, int>? unread = null)
    {
        var entries = tei.Descendants(Ns + "entry")
            .Select(e => (Element: e, Parts: (e.Attribute("n")?.Value ?? string.Empty).Split('|')))
            .Where(e => e.Parts[0].Length > 0)
            .ToList();
        var headwords = entries
            .Select(e => (Headword: e.Parts[0], Numbers: e.Parts.Skip(1).Select(n => LexiconText.Strong(n)).OfType<string>().ToList()))
            .Where(e => e.Numbers.Count == 1)
            .GroupBy(e => e.Headword, StringComparer.Ordinal)
            .Where(g => g.Select(e => e.Numbers[0]).Distinct().Count() == 1)
            .ToDictionary(g => g.Key, g => g.First().Numbers[0], StringComparer.Ordinal);

        var rows = new List<LexiconArticleRow>();
        foreach (var (element, parts) in entries)
        {
            var numbers = parts.Skip(1).Select(n => LexiconText.Strong(n)).OfType<string>().Distinct().ToList();
            if (numbers.Count == 0)
            {
                continue;
            }

            var writer = new ArticleWriter();
            writer.Paragraph(0);
            Children(element, writer, headwords, unread);
            if (writer.Json() is not { } body)
            {
                continue;
            }

            var headword = element.Descendants(Ns + "orth").FirstOrDefault() is { } orth
                           && string.Concat(orth.Nodes().OfType<XText>().Select(t => t.Value)).Trim().TrimEnd(',').Trim() is { Length: > 0 } printed
                ? printed
                : parts[0];
            rows.AddRange(numbers.Select(number =>
                new LexiconArticleRow(LexiconWorks.AbbottSmith.Id, number, 0, string.Join('|', parts), headword, null, body, Credit)));
        }

        return [.. LexiconText.Numbered(rows.OrderBy(r => int.Parse(r.StrongNumber.AsSpan(1))))];
    }

    private static void Children(XElement parent, ArticleWriter writer, Dictionary<string, string> headwords, Dictionary<string, int>? unread)
    {
        foreach (var node in parent.Nodes())
        {
            switch (node)
            {
                case XText text:
                    writer.Text(text.Value);
                    break;
                case XElement element:
                    Element(element, writer, headwords, unread);
                    break;
            }
        }
    }

    private static void Element(XElement element, ArticleWriter writer, Dictionary<string, string> headwords, Dictionary<string, int>? unread)
    {
        switch (element.Name.LocalName)
        {
            case "note" when element.Attribute("type")?.Value == "occurrencesNT":
            case "pb":
                return;
            case "note":
                Marked(element, CommentBuilder.Mark.Note, writer, headwords, unread);
                return;
            case "lb":
                writer.Text(" ");
                return;
            case "orth":
            case "gloss":
                Marked(element, CommentBuilder.Mark.Bold, writer, headwords, unread);
                return;
            case "emph":
                Marked(element, CommentBuilder.Mark.Italic, writer, headwords, unread);
                return;
            case "hi":
                Marked(element, element.Attribute("rend")?.Value == "superscript" ? CommentBuilder.Mark.Raised : CommentBuilder.Mark.None, writer,
                    headwords, unread);
                return;
            case "foreign":
                var language = element.Attribute(Xml + "lang")?.Value;
                var outer = writer.Named;
                if (language is "heb" or "arc")
                {
                    writer.Name(LexiconText.Strong(element.Attribute("n")?.Value));
                }

                Marked(element, language == "lat" ? CommentBuilder.Mark.Italic : CommentBuilder.Mark.None, writer, headwords, unread);
                writer.Name(outer);
                return;
            case "ref":
                if (element.Attribute("osisRef")?.Value is { } osis)
                {
                    writer.Cite(LexiconText.Verse(osis));
                    Children(element, writer, headwords, unread);
                    writer.Cite(null);
                }
                else
                {
                    writer.Name(headwords.GetValueOrDefault(element.Value.Trim()));
                    Children(element, writer, headwords, unread);
                    writer.Name(null);
                }

                return;
            case "sense":
                var depth = writer.Depth;
                writer.Paragraph(depth + 1);
                if (element.Attribute("n")?.Value is { Length: > 0 } n)
                {
                    writer.Push(CommentBuilder.Mark.Bold);
                    writer.Text($"{n} ");
                    writer.Pop();
                }

                Children(element, writer, headwords, unread);
                writer.Paragraph(depth);
                return;
            case "re":
            case "row":
                writer.Paragraph(0);
                Children(element, writer, headwords, unread);
                writer.Paragraph(0);
                return;
            case "cell":
                Children(element, writer, headwords, unread);
                writer.Text(" ");
                return;
            case "form":
            case "etym":
            case "seg":
            case "gramGrp":
            case "gram":
            case "pos":
            case "tns":
            case "mood":
            case "per":
            case "number":
            case "gen":
            case "usg":
            case "subc":
            case "name":
            case "table":
            case "p":
                Children(element, writer, headwords, unread);
                return;
            default:
                if (unread is not null)
                {
                    unread[element.Name.LocalName] = unread.GetValueOrDefault(element.Name.LocalName) + 1;
                }

                Children(element, writer, headwords, unread);
                return;
        }
    }

    private static void Marked(XElement element, CommentBuilder.Mark mark, ArticleWriter writer, Dictionary<string, string> headwords,
        Dictionary<string, int>? unread)
    {
        if (mark != CommentBuilder.Mark.None)
        {
            writer.Push(mark);
        }

        Children(element, writer, headwords, unread);
        if (mark != CommentBuilder.Mark.None)
        {
            writer.Pop();
        }
    }
}
