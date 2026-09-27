using System.Text.RegularExpressions;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities.Enums;

namespace Essenthos.Core.Pages;

/// <summary>Which original a passage or a word is read beside: the Hebrew up to Malachi, the Greek after it.</summary>
internal enum Original
{
    Hebrew,
    Greek,
}

/// <summary>A verse as its page shows it, in the words of the text the page's language reads.</summary>
internal sealed record VerseView(int Book, string BookName, int Chapter, int Verse, string Text, string TextName)
{
    public int? Previous { get; init; }

    public int? Next { get; init; }
}

internal sealed record ChapterView(
    int Book,
    string BookName,
    int Chapter,
    int ChapterCount,
    IReadOnlyList<(int Number, string Text)> Verses,
    string TextName);

/// <summary>A piece of a record's description, linked where it names another record.</summary>
internal sealed record LinePart(string Text, string? Href = null);

internal sealed record VerseMention(int Book, int Chapter, int Verse, string Label);

/// <param name="Line">What the record is known by: its own short description in the page's language, else the structured one, else the source's.</param>
/// <param name="Descriptor">The structured description, with its links, where there is one.</param>
internal sealed record RecordView(
    EntityKind Kind,
    string Slug,
    string Name,
    string? Line,
    IReadOnlyList<LinePart> Descriptor,
    int References,
    IReadOnlyList<VerseMention> FirstVerses)
{
    public string? Image { get; init; }
}

internal sealed record StrongView(string Number, string? Lemma, string? Transliteration, string? Definition, string? Derivation);

internal sealed record BookLink(int Ordinal, string Name);

/// <summary>
/// Each kind of page's title, description and essentials, from what the loader found and the reader's
/// own wording. The titles and descriptions follow the reader's rules to the letter — the same cut,
/// the same order, the same words — so what a search engine indexes is what the tab says. Nothing
/// here reads the database, so what a page says can be tested without one.
/// </summary>
internal static partial class PageViews
{
    /// <summary>How much of a verse the title quotes; the rest is in the description.</summary>
    public const int TitleQuoteLength = 60;

    /// <summary>How much of a line about a person or a place the title carries.</summary>
    public const int TitleLineLength = 70;

    /// <summary>About what a search engine shows of a description before it cuts it itself.</summary>
    public const int DescriptionLength = 160;

    /// <summary>A line or a definition inside a description, which leaves room for what is said around it.</summary>
    public const int DescriptionQuoteLength = 130;

    /// <summary>The least of a verse or a line a description quotes, however long the rest of it is.</summary>
    public const int LeastQuoteLength = 40;

    /// <summary>How many of a chapter's first verses its description quotes.</summary>
    public const int OpeningVerses = 2;

    /// <summary>The last book of the Hebrew canon; every book after it is read beside the Greek.</summary>
    private const int LastHebrewBook = 39;

    private const string Separator = " — ";
    private const string Ellipsis = "…";
    private const string Crumb = " › ";

    public static Original OriginalOf(int book) => book > LastHebrewBook ? Original.Greek : Original.Hebrew;

    public static Original OriginalOf(string strongNumber) =>
        strongNumber.StartsWith('G') ? Original.Greek : Original.Hebrew;

    /// <summary>
    /// A text cut to a length at a word, with an ellipsis where anything was cut, its white space
    /// collapsed first — the reader's own rule, character for character.
    /// </summary>
    public static string Shortened(string text, int limit)
    {
        var flat = PageHtml.Plain(text);
        if (flat.Length <= limit)
        {
            return flat;
        }

        var cut = flat[..(limit - Ellipsis.Length + 1)];
        var space = cut.LastIndexOf(' ');
        var words = space * 2 > limit ? cut[..space] : cut[..(limit - Ellipsis.Length)];
        return Trailing().Replace(words, "") + Ellipsis;
    }

    /// <summary>A line closed as a sentence, unless it already ends as one.</summary>
    public static string Closed(string text) => Sentence().IsMatch(text) ? text : text + ".";

    /// <summary>A description around a line, with the line cut so that the whole of it fits.</summary>
    public static string Fitted(string text, Func<string, string> around)
    {
        var room = DescriptionLength - around("").Length;
        return around(Shortened(text, Math.Max(LeastQuoteLength, room)));
    }

    /// <summary>The same around a quotation, whose marks are part of what has to fit.</summary>
    public static string AroundQuote(PageWording t, string text, Func<string, string> around)
    {
        var room = DescriptionLength - around(t.Quoted("")).Length;
        return around(t.Quoted(Shortened(text, Math.Max(LeastQuoteLength, room))));
    }

    public static PageContent Verse(SiteLanguage language, PageWording t, VerseView verse)
    {
        var reference = t.Reference(verse.BookName, verse.Chapter, verse.Verse);
        var chapter = $"{verse.BookName} {verse.Chapter}";
        var body = Open(language)
            .Open("nav").Link(language.Path("/"), PageContent.SiteName).Text(Crumb)
            .Link(language.Path(PageAddress.ChapterPath(verse.Book, verse.Chapter)), chapter).Close("nav")
            .Element("h1", reference)
            .Open("blockquote").Element("p", verse.Text).Close("blockquote")
            .Element("p", verse.TextName, "source")
            .Open("nav");
        if (verse.Previous is { } previous)
        {
            body.Link(language.Path(PageAddress.VersePath(verse.Book, verse.Chapter, previous)),
                $"← {verse.Chapter}:{previous}", "prev").Text(" ");
        }

        body.Link(language.Path(PageAddress.ChapterPath(verse.Book, verse.Chapter)), chapter);
        if (verse.Next is { } next)
        {
            body.Text(" ").Link(language.Path(PageAddress.VersePath(verse.Book, verse.Chapter, next)),
                $"{verse.Chapter}:{next} →", "next");
        }

        body.Close("nav").Close("article");
        var title = string.Join(Separator, reference, t.Quoted(Shortened(verse.Text, TitleQuoteLength)), verse.TextName);
        var beside = Beside(t, OriginalOf(verse.Book));
        var description = AroundQuote(t, verse.Text, quote => t.Text("verse.description",
            ("reference", reference), ("text", verse.TextName), ("quote", quote), ("beside", beside)));
        return Page(StatusCodes.Status200OK, t.Titled(title), reference, description, body) with
        {
            Canonical = PageAddress.VersePath(verse.Book, verse.Chapter, verse.Verse),
            Type = "article",
        };
    }

    public static PageContent Chapter(SiteLanguage language, PageWording t, ChapterView chapter)
    {
        var reference = $"{chapter.BookName} {chapter.Chapter}";
        var body = Open(language)
            .Open("nav").Link(language.Path("/"), PageContent.SiteName).Close("nav")
            .Element("h1", reference)
            .Element("p", chapter.TextName, "source");
        foreach (var (number, text) in chapter.Verses)
        {
            body.Open("p", id: $"v{number}")
                .Open("sup").Link(language.Path(PageAddress.VersePath(chapter.Book, chapter.Chapter, number)), number.ToString())
                .Close("sup").Text(" ").Text(text).Close("p");
        }

        body.Open("nav");
        if (chapter.Chapter > 1)
        {
            body.Link(language.Path(PageAddress.ChapterPath(chapter.Book, chapter.Chapter - 1)),
                $"← {chapter.BookName} {chapter.Chapter - 1}", "prev").Text(" ");
        }

        if (chapter.Chapter < chapter.ChapterCount)
        {
            body.Link(language.Path(PageAddress.ChapterPath(chapter.Book, chapter.Chapter + 1)),
                $"{chapter.BookName} {chapter.Chapter + 1} →", "next");
        }

        body.Close("nav").Close("article");
        var beside = Beside(t, OriginalOf(chapter.Book));
        var opening = string.Join(' ', chapter.Verses.Take(OpeningVerses).Select(verse => verse.Text));
        var description = opening.Length > 0
            ? AroundQuote(t, opening, quote => t.Text("chapter.description",
                ("reference", reference), ("text", chapter.TextName), ("opening", quote), ("beside", beside)))
            : t.Text("chapter.plain", ("reference", reference), ("beside", beside));
        return Page(StatusCodes.Status200OK, t.Titled(reference), reference, description, body) with
        {
            Canonical = PageAddress.ChapterPath(chapter.Book, chapter.Chapter),
            Type = "article",
        };
    }

    public static PageContent Record(SiteLanguage language, PageWording t, RecordView record)
    {
        var line = record.Line ?? t.Text($"entity.kind.{EnumSpelling.Of(record.Kind)}");
        var verses = record.References > 0 ? t.Counted("entity.verses", record.References) : null;

        var body = Open(language)
            .Open("nav").Link(language.Path("/"), PageContent.SiteName).Text(Crumb)
            .Link(language.Path(PageSections.Named(PageSections.Encyclopedia).Path),
                t.Text($"page.{PageSections.Encyclopedia}.title")).Close("nav")
            .Element("h1", record.Name);
        if (record.Descriptor.Count > 0)
        {
            body.Open("p");
            foreach (var part in record.Descriptor)
            {
                if (part.Href is { } href)
                {
                    body.Link(language.Path(href), part.Text);
                }
                else
                {
                    body.Text(part.Text);
                }
            }

            body.Close("p");
        }

        var plain = WithoutCitations(line);
        if (record.Descriptor.Count == 0 || plain != string.Concat(record.Descriptor.Select(part => part.Text)).Trim())
        {
            body.Element("p", plain);
        }

        if (verses is not null)
        {
            body.Open("p").Text(verses);
            for (var i = 0; i < record.FirstVerses.Count; i++)
            {
                var mention = record.FirstVerses[i];
                body.Text(" ")
                    .Link(language.Path(PageAddress.VersePath(mention.Book, mention.Chapter, mention.Verse)), mention.Label)
                    .Text(i < record.FirstVerses.Count - 1 ? ";" : record.References > record.FirstVerses.Count ? "; …" : "");
            }

            body.Close("p");
        }

        body.Close("article");
        var description = Fitted(line, fit => Closed($"{record.Name}: {fit}") + (verses is null ? "" : " " + verses));
        return Page(
            StatusCodes.Status200OK,
            t.Titled($"{record.Name}{Separator}{Shortened(line, TitleLineLength)}"),
            record.Name,
            description,
            body) with
        {
            Canonical = PageAddress.RecordPath(record.Kind, record.Slug),
            Image = record.Image,
            Type = "article",
        };
    }

    public static PageContent FamilyTree(SiteLanguage language, PageWording t, string english, string name, string? image)
    {
        var title = t.Text("familyTree.title", ("name", name));
        var description = t.Text("familyTree.description", ("name", name));
        var body = Open(language)
            .Open("nav").Link(language.Path("/"), PageContent.SiteName).Close("nav")
            .Element("h1", title)
            .Element("p", description)
            .Close("article");
        return Page(StatusCodes.Status200OK, t.Titled(title), title, description, body) with
        {
            Canonical = english,
            Image = image,
        };
    }

    public static PageContent Strong(SiteLanguage language, PageWording t, StrongView entry)
    {
        var word = string.Join(' ', new[] { entry.Lemma, entry.Transliteration }.Where(part => !string.IsNullOrEmpty(part)));
        var head = word.Length > 0 ? $"{word} ({entry.Number})" : entry.Number;
        var original = t.Text($"strong.{Spelled(OriginalOf(entry.Number))}");
        var description = entry.Definition is { Length: > 0 } meaning
            ? $"{original} {head}: {Closed(Shortened(meaning, DescriptionQuoteLength))}"
            : $"{original} {head}.";
        var body = Open(language)
            .Open("nav").Link(language.Path("/"), PageContent.SiteName).Close("nav")
            .Element("h1", head);
        foreach (var said in new[] { entry.Definition, entry.Derivation }.Where(said => !string.IsNullOrWhiteSpace(said)))
        {
            body.Element("p", said!);
        }

        body.Close("article");
        return Page(StatusCodes.Status200OK, t.Titled(head), head, description, body) with
        {
            Canonical = $"/strong/{entry.Number}",
            Type = "article",
        };
    }

    /// <summary>An event, a period, a text: a name, what the source says of it, and a few links.</summary>
    public static PageContent Named(
        SiteLanguage language,
        PageWording t,
        string english,
        string name,
        string description,
        string? said,
        IEnumerable<(string Href, string Text)> links,
        string? image = null)
    {
        var body = Open(language)
            .Open("nav").Link(language.Path("/"), PageContent.SiteName).Close("nav")
            .Element("h1", name);
        if (!string.IsNullOrWhiteSpace(said))
        {
            body.Element("p", said);
        }

        var linked = links.ToList();
        if (linked.Count > 0)
        {
            body.Open("ul");
            foreach (var (href, text) in linked)
            {
                body.Open("li").Link(language.Path(href), text).Close("li");
            }

            body.Close("ul");
        }

        body.Close("article");
        return Page(StatusCodes.Status200OK, t.Titled(name), name, description, body) with
        {
            Canonical = english,
            Image = image,
            Type = "article",
        };
    }

    public static PageContent Home(SiteLanguage language, PageWording t, IReadOnlyList<BookLink> books)
    {
        var description = t.Text("home.description");
        var body = Open(language)
            .Element("h1", PageContent.SiteName)
            .Element("p", description)
            .Open("nav").Open("ul");
        foreach (var key in HomeSections)
        {
            body.Open("li").Link(language.Path(PageSections.Named(key).Path), t.Text($"page.{key}.title")).Close("li");
        }

        body.Close("ul").Close("nav");
        if (books.Count > 0)
        {
            body.Open("nav").Open("ul");
            foreach (var book in books)
            {
                body.Open("li").Link(language.Path(PageAddress.ChapterPath(book.Ordinal, 1)), book.Name).Close("li");
            }

            body.Close("ul").Close("nav");
        }

        body.Close("article");
        return Page(StatusCodes.Status200OK, t.Text("home.title"), PageContent.SiteName, description, body) with
        {
            Canonical = "/",
        };
    }

    public static PageContent Section(SiteLanguage language, PageWording t, PageAddress address)
    {
        var key = address.Section!.Key;
        var title = t.Text($"page.{key}.title");
        var description = t.Text($"page.{key}.description");
        var body = address.Indexed
            ? Open(language)
                .Open("nav").Link(language.Path("/"), PageContent.SiteName).Close("nav")
                .Element("h1", title)
                .Element("p", description)
                .Close("article")
            : null;
        return Page(StatusCodes.Status200OK, t.Titled(title), title, description, body) with
        {
            Canonical = address.Indexed ? address.English : null,
            Indexed = address.Indexed,
        };
    }

    public static PageContent Search(PageWording t, string query)
    {
        var title = t.Text("search.query", ("query", query));
        return Page(StatusCodes.Status200OK, t.Titled(title), title, t.Text("page.search.description"), null) with
        {
            Indexed = false,
        };
    }

    public static PageContent NotFound(SiteLanguage language, PageWording t)
    {
        var title = t.Text($"page.{PageSections.NotFound}.title");
        var description = t.Text($"page.{PageSections.NotFound}.description");
        var body = Open(language)
            .Element("h1", title)
            .Element("p", description)
            .Open("p").Link(language.Path("/"), PageContent.SiteName).Close("p")
            .Close("article");
        return Page(StatusCodes.Status404NotFound, t.Titled(title), title, description, body) with { Indexed = false };
    }

    /// <summary>A record's short description without the verse codes the source writes into it, <c>(EXO 13:3)</c>.</summary>
    public static string WithoutCitations(string text) => Citations().Replace(text, "").Trim();

    private static readonly string[] HomeSections =
    [
        "texts", "lexicon", "syntax", PageSections.Encyclopedia, PageSections.FamilyTree, "events", "timeline", "map",
    ];

    private static string Beside(PageWording t, Original original) => t.Text($"beside.{Spelled(original)}");

    private static string Spelled(Original original) => original == Original.Greek ? "greek" : "hebrew";

    private static PageContent Page(int status, string title, string heading, string description, HtmlWriter? body) =>
        new(status, title, heading, Shortened(description, DescriptionLength), body?.ToString() ?? "");

    private static HtmlWriter Open(SiteLanguage language) => new HtmlWriter().Open("article", "prerendered", lang: language.Code);

    [GeneratedRegex(@"[\s,;:.!?—–-]+$")]
    private static partial Regex Trailing();

    [GeneratedRegex(@"[.!?…]$")]
    private static partial Regex Sentence();

    [GeneratedRegex(@"\s*\((?:[1-4]?[A-Z]{2,3} \d+:\d+(?:[-–]\d+)?(?:,\s*)?)+\)")]
    private static partial Regex Citations();
}
