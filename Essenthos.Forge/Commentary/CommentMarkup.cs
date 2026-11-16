using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using Essenthos.Core.Loading.CrossReferences;

namespace Essenthos.Core.Commentary;

/// <summary>
/// A commentary module's markup read into a <see cref="CommentBuilder"/>: OSIS, as CrossWire's newer modules store it
/// (Matthew Henry), and the plain HTML of the older ones that call it ThML (Rieger). Every word is kept; what is
/// dropped is presentation — fonts and colours — and the markers a conversion left in the text, named below.
/// </summary>
internal static partial class CommentMarkup
{
    /// <summary>
    /// OSIS as SWORD stores it: paragraphs, lines of verse, book and chapter divisions as milestones (an empty element
    /// opening a paragraph, <c>sID</c>, and another closing it, <c>eID</c>), so one stored entry may close a paragraph a
    /// previous entry opened; headings as <c>title</c>; marks as <c>hi</c>; cited verses as <c>reference osisRef</c>.
    /// </summary>
    /// <param name="unread">Each element this reader does not know, counted, so a module with markup it misses says so.</param>
    public static void Osis(string markup, CommentBuilder comment, Dictionary<string, int>? unread = null)
    {
        foreach (var (tag, text) in Pieces(markup))
        {
            if (tag is null)
            {
                var decoded = Decoded(text!);

                // The CCEL conversion left "<<<" at the close of some books; it is no word of the commentary.
                comment.Text(decoded.Replace("<<<", string.Empty, StringComparison.Ordinal));
                continue;
            }

            var (closing, name, attributes) = tag.Value;
            switch (name)
            {
                case "div":
                case "chapter":
                case "milestone":
                case "lg":
                    if (!closing && attributes.Contains("sID=") && Attribute(attributes, "type") == "x-p")
                    {
                        comment.Begin("p");
                    }
                    else
                    {
                        comment.End();
                    }

                    break;
                case "l":
                    if (!closing && attributes.Contains("sID="))
                    {
                        comment.Begin("l");
                    }
                    else
                    {
                        comment.End();
                    }

                    break;
                case "title":
                    if (closing)
                    {
                        // A heading the conversion added after a book's last chapter, naming the edition.
                        if (comment.LastBlockText("h") == "Unabridged")
                        {
                            comment.DropLastBlock();
                        }
                    }
                    else
                    {
                        comment.Begin("h");
                    }

                    break;
                case "hi":
                    if (closing)
                    {
                        comment.Pop();
                    }
                    else
                    {
                        comment.Push(Attribute(attributes, "type") switch
                        {
                            "italic" => CommentBuilder.Mark.Italic,
                            "bold" => CommentBuilder.Mark.Bold,
                            "small-caps" => CommentBuilder.Mark.SmallCaps,
                            "super" => CommentBuilder.Mark.Raised,
                            "underline" => CommentBuilder.Mark.Underlined,
                            _ => CommentBuilder.Mark.None,
                        });
                    }

                    break;
                case "note":
                    if (closing)
                    {
                        comment.Pop();
                    }
                    else
                    {
                        comment.Push(CommentBuilder.Mark.Note);
                    }

                    break;
                case "reference":
                    comment.Cite(closing ? null : Verse(Attribute(attributes, "osisRef")));
                    break;
                default:
                    if (unread is not null)
                    {
                        unread[name] = unread.GetValueOrDefault(name) + 1;
                    }

                    break;
            }
        }
    }

    /// <summary>
    /// The HTML of an older module: headings, paragraphs (often left open), line breaks, numbered and bulleted lists,
    /// quotations, bold and italic; <c>font</c> only colours text and is dropped. A numbered list's items are numbered
    /// as a browser numbers them, since the number is what the page printed.
    /// </summary>
    public static void Html(string markup, CommentBuilder comment, Dictionary<string, int>? unread = null)
    {
        var lists = new Stack<int?>();
        foreach (var (tag, text) in Pieces(markup))
        {
            if (tag is null)
            {
                comment.Text(Decoded(text!));
                continue;
            }

            var (closing, name, _) = tag.Value;
            switch (name)
            {
                case "h1" or "h2" or "h3" or "h4" or "h5" or "h6":
                    if (closing)
                    {
                        comment.End();
                    }
                    else
                    {
                        comment.Begin("h");
                    }

                    break;
                case "p" or "div":
                    if (closing)
                    {
                        comment.End();
                    }
                    else
                    {
                        comment.Begin("p");
                    }

                    break;
                case "br":
                    comment.End();
                    break;
                case "blockquote":
                    if (closing)
                    {
                        comment.End();
                    }
                    else
                    {
                        comment.Begin("q");
                    }

                    break;
                case "ol" or "ul":
                    comment.End();
                    if (closing)
                    {
                        lists.TryPop(out _);
                    }
                    else
                    {
                        lists.Push(name == "ol" ? 0 : null);
                    }

                    break;
                case "li":
                    if (closing)
                    {
                        comment.End();
                        break;
                    }

                    comment.Begin("p");
                    if (lists.TryPop(out var counter))
                    {
                        lists.Push(counter + 1);
                        comment.Text(counter is { } number ? $"{number + 1}. " : "• ");
                    }

                    break;
                case "b" or "strong":
                    Mark(closing, CommentBuilder.Mark.Bold);
                    break;
                case "i" or "em":
                    Mark(closing, CommentBuilder.Mark.Italic);
                    break;
                case "u":
                    Mark(closing, CommentBuilder.Mark.Underlined);
                    break;
                case "font":
                    break;
                default:
                    if (unread is not null)
                    {
                        unread[name] = unread.GetValueOrDefault(name) + 1;
                    }

                    break;
            }
        }

        void Mark(bool closing, CommentBuilder.Mark mark)
        {
            if (closing)
            {
                comment.Pop();
            }
            else
            {
                comment.Push(mark);
            }
        }
    }

    /// <summary>
    /// A verse an OSIS reference names, as [book, chapter], [book, chapter, verse] or, for a run inside one book,
    /// [book, chapter, verse, last chapter, last verse]. Of several references in one attribute the first is taken, the
    /// one the printed citation opens with. Null for a book this corpus does not number or a reference that does not read.
    /// </summary>
    internal static int[]? Verse(string? osisRef)
    {
        var first = (osisRef ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        if (first is null)
        {
            return null;
        }

        var ends = first.Split('-');
        if (Point(ends[0]) is not { } start)
        {
            return null;
        }

        if (ends.Length == 2 && Point(ends[1]) is { Length: 3 } end && start.Length == 3 && end[0] == start[0])
        {
            return [.. start, end[1], end[2]];
        }

        return start;
    }

    private static int[]? Point(string osis)
    {
        var parts = osis.Split('.');
        if (parts.Length is < 2 or > 3 || OsisBook(parts[0]) is not { } book)
        {
            return null;
        }

        var numbers = new int[parts.Length];
        numbers[0] = book;
        for (var i = 1; i < parts.Length; i++)
        {
            if (!int.TryParse(parts[i], NumberStyles.None, CultureInfo.InvariantCulture, out numbers[i]) || numbers[i] < 1)
            {
                return null;
            }
        }

        return numbers;
    }

    /// <summary>
    /// The deuterocanonical books' OSIS names, at the ordinals the corpus holds them by, and the few shorter names
    /// Matthew Henry's module writes for a book now and then, each of which names one book only.
    /// </summary>
    private static readonly Dictionary<string, int> MoreBooks = new(StringComparer.Ordinal)
    {
        ["Bar"] = 67, ["1Esd"] = 68, ["2Esd"] = 69, ["Tob"] = 70, ["Jdt"] = 71, ["Sir"] = 72, ["1Macc"] = 73, ["2Macc"] = 74,
        ["Wis"] = 75, ["Sus"] = 77, ["Bel"] = 78, ["PrMan"] = 79, ["PrAzar"] = 93,
        ["Ge"] = 1, ["De"] = 5, ["Ne"] = 16, ["Psa"] = 19, ["Pr"] = 20, ["Eze"] = 26, ["Lu"] = 42, ["Ac"] = 44, ["Php"] = 50,
        ["1Ti"] = 54, ["1Jo"] = 62, ["1Mac"] = 73,
    };

    private static int? OsisBook(string name) => OpenBibleCrossReferences.OsisOrdinal(name) ?? (MoreBooks.TryGetValue(name, out var book) ? book : null);

    /// <summary>
    /// Entities decoded. A handful of Henry's footnotes escape a section sign twice (<c>&amp;amp;#167;</c>), which one
    /// decoding leaves as <c>&amp;#167;</c>; a numeric reference still standing after it is the character it names.
    /// </summary>
    private static string Decoded(string text)
    {
        var decoded = WebUtility.HtmlDecode(text);
        return decoded.Contains("&#", StringComparison.Ordinal)
            ? NumericReference().Replace(decoded, m => char.ConvertFromUtf32(int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture)))
            : decoded;
    }

    private static string? Attribute(string attributes, string name)
    {
        foreach (Match match in AttributePattern().Matches(attributes))
        {
            if (string.Equals(match.Groups[1].Value, name, StringComparison.OrdinalIgnoreCase))
            {
                return match.Groups[2].Success ? match.Groups[2].Value : match.Groups[3].Value;
            }
        }

        return null;
    }

    /// <summary>The markup as tags and the text between them, in order; a tag's name in lower case.</summary>
    private static IEnumerable<((bool Closing, string Name, string Attributes)? Tag, string? Text)> Pieces(string markup)
    {
        var at = 0;
        foreach (Match match in TagPattern().Matches(markup))
        {
            if (match.Index > at)
            {
                yield return (null, markup[at..match.Index]);
            }

            yield return ((match.Groups[1].Length > 0, match.Groups[2].Value.ToLowerInvariant(), match.Groups[3].Value), null);
            at = match.Index + match.Length;
        }

        if (at < markup.Length)
        {
            yield return (null, markup[at..]);
        }
    }

    [GeneratedRegex(@"<(/?)([A-Za-z][A-Za-z0-9]*)([^>]*)>")]
    private static partial Regex TagPattern();

    [GeneratedRegex("""([A-Za-z:]+)\s*=\s*(?:"([^"]*)"|([^\s"'>/]+))""")]
    private static partial Regex AttributePattern();

    [GeneratedRegex(@"&#(\d{2,6});")]
    private static partial Regex NumericReference();
}
