using System.Globalization;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Essenthos.Core.Corpus;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Utils;

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
    /// <summary>The whole chapter in the same text, which the page shows around the verse; the verse alone where empty.</summary>
    public IReadOnlyList<(int Number, string Text)> Verses { get; init; } = [];

    public int ChapterCount { get; init; }

    /// <summary>The language the text is written in, which the reader marks each verse with; the page's own where unknown.</summary>
    public string? TextLanguage { get; init; }

    /// <summary>The verse in the original, word by word; null where no original holds it.</summary>
    public OriginalVerseView? Original { get; init; }

    /// <summary>The texts the address names side by side, as the reader lays them out; empty for the one text alone.</summary>
    public IReadOnlyList<ReadingColumn> Columns { get; init; } = [];
}

/// <summary>A verse on its own page, in the text the page's language reads, with the same verse in others beside it.</summary>
internal sealed record SingleVerseView(int Book, string BookName, int Chapter, int Verse, string Text, string TextName)
{
    /// <summary>The verse word by word, each name linked to its record; empty where only its plain text is known.</summary>
    public IReadOnlyList<PaneWord> Words { get; init; } = [];

    /// <summary>The language the text is written in; the page's own where unknown.</summary>
    public string? TextLanguage { get; init; }

    /// <summary>The same verse in the texts the reader offers first beside this one.</summary>
    public IReadOnlyList<OtherVerse> Others { get; init; } = [];

    /// <summary>The verse in the original, word by word; null where no original holds it.</summary>
    public OriginalVerseView? Original { get; init; }

    /// <summary>The verses either side in the same chapter, where there are any.</summary>
    public int? Previous { get; init; }

    public int? Next { get; init; }
}

/// <summary>A verse in another text than the page's own, named.</summary>
internal sealed record OtherVerse(string TextName, string? Language, string Text);

/// <param name="Language">The original's language as a web page names it.</param>
internal sealed record OriginalVerseView(string TextName, string? Language, string Text, IReadOnlyList<OriginalWordView> Words);

/// <param name="Gloss">What the edition says the word means, on an English page; null elsewhere.</param>
internal sealed record OriginalWordView(string Surface, IReadOnlyList<string> Numbers, string? Gloss);

internal sealed record ChapterView(
    int Book,
    string BookName,
    int Chapter,
    int ChapterCount,
    IReadOnlyList<(int Number, string Text)> Verses,
    string TextName)
{
    /// <summary>The language the text is written in, which the reader marks each verse with; the page's own where unknown.</summary>
    public string? TextLanguage { get; init; }

    /// <summary>
    /// The texts the address names side by side, as the reader lays them out, in the order named; empty where
    /// the chapter is shown in its one text (<see cref="Verses"/>, <see cref="TextName"/>).
    /// </summary>
    public IReadOnlyList<ReadingColumn> Columns { get; init; } = [];
}

/// <summary>A piece of a record's description, linked where it names another record.</summary>
internal sealed record LinePart(string Text, string? Href = null);

internal sealed record VerseMention(int Book, int Chapter, int Verse, string Label)
{
    /// <summary>The verse's words in the text the page's language reads, where it holds them.</summary>
    public string? Text { get; init; }
}

/// <summary>A name of the record as the Hebrew or the Greek writes it, with its Strong numbers and what it means.</summary>
internal sealed record OriginalName(
    string? Label,
    string? Hebrew,
    string? HebrewTransliterated,
    IReadOnlyList<string> HebrewNumbers,
    string? Greek,
    string? GreekTransliterated,
    IReadOnlyList<string> GreekNumbers,
    string? Meaning);

/// <summary>Something a person is known for, in the language its words are said in.</summary>
internal sealed record KnownClause(string Text, string? Language);

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

    /// <summary>Where a place is, as its gazetteer's best answer puts it; null for anything else.</summary>
    public (double Latitude, double Longitude)? Coordinates { get; init; }

    /// <summary>The same thing elsewhere: its Wikidata item and its Wikipedia articles.</summary>
    public IReadOnlyList<string> SameAs { get; init; } = [];

    public IReadOnlyList<OriginalName> Names { get; init; } = [];

    public IReadOnlyList<KnownClause> KnownFor { get; init; } = [];
}

/// <param name="Outline">The lexicon's numbered senses, a line each, as the reader's page sets them out.</param>
internal sealed record StrongView(
    string Number,
    string? Lemma,
    string? Transliteration,
    string? Definition,
    string? Derivation,
    string? Outline = null)
{
    /// <summary>How many words carry the number in the edition it is counted in.</summary>
    public int? Uses { get; init; }

    /// <summary>The edition of the original the number is counted in.</summary>
    public string? CountedIn { get; init; }

    /// <summary>The text whose renderings are listed: the one the page's language reads.</summary>
    public string? RenderedIn { get; init; }

    public IReadOnlyList<StrongRenderingCount> Renderings { get; init; } = [];

    /// <summary>The books it stands in, in canon order, with how often.</summary>
    public IReadOnlyList<StrongBookCount> Books { get; init; } = [];
}

internal sealed record StrongRenderingCount(string Text, int Count);

internal sealed record StrongBookCount(string Book, int Count);

internal sealed record BookLink(int Ordinal, string Name);

/// <summary>A verse in a list of verses: where it is, as the page's language cites it, and what it says.</summary>
internal sealed record ListedVerse(int Book, int Chapter, int Verse, string Label, string? Text);

/// <summary>How many verses of a list stand in one book.</summary>
internal sealed record BookCount(int Ordinal, string Name, int Count);

/// <summary>One page of every verse of a record or a Strong number.</summary>
/// <param name="English">The list's own address, without its page.</param>
/// <param name="Page">The address of the record's or the number's page.</param>
/// <param name="Name">What the record or the number is called on its page.</param>
/// <param name="Section">The section the record's or the number's page is listed in.</param>
/// <param name="TextName">The text the verses are quoted from.</param>
internal sealed record VersesView(
    string English,
    string Page,
    string Name,
    string? Section,
    int Total,
    int Skip,
    int Take,
    IReadOnlyList<BookCount> Books,
    IReadOnlyList<ListedVerse> Verses,
    string? TextName)
{
    public bool Indexed { get; init; } = true;

    /// <summary>Whether the description says how many verses there are; a record's page does, a number's does not.</summary>
    public bool Counted { get; init; } = true;
}

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
        var verses = verse.Verses.Count > 0 ? verse.Verses : [(verse.Verse, verse.Text)];
        var body = Reading(language, t, new ChapterView(verse.Book, verse.BookName, verse.Chapter,
            Math.Max(verse.ChapterCount, verse.Chapter), verses, verse.TextName)
        {
            TextLanguage = verse.TextLanguage,
            Columns = verse.Columns,
        }, verse.Verse);
        if (verse.Original is { } original)
        {
            OriginalWords(body, language, t, reference, original);
        }
        var title = string.Join(Separator, reference, t.Quoted(Shortened(verse.Text, TitleQuoteLength)), verse.TextName);
        var beside = Beside(t, OriginalOf(verse.Book));
        var description = AroundQuote(t, verse.Text, quote => t.Text("verse.description",
            ("reference", reference), ("text", verse.TextName), ("quote", quote), ("beside", beside)));
        // The reader's address of a verse shows its whole chapter, as thirty others do; the verse's own page
        // is the one a search engine is told to keep (PRB-1653).
        var versePath = PageAddress.SingleVersePath(verse.Book, verse.Chapter, verse.Verse);
        return Page(StatusCodes.Status200OK, t.Titled(title), reference, description, body) with
        {
            Canonical = versePath,
            Type = "article",
            PageType = "ItemPage",
            Trail = [Start, new Crumb(chapter, PageAddress.ChapterPath(verse.Book, verse.Chapter)), new Crumb(reference, versePath)],
            About = (link, _) => StructuredData.Node("CreativeWork",
                ("name", reference),
                ("text", verse.Text),
                ("url", link(versePath)),
                ("isPartOf", ChapterNode(link, verse.Book, verse.BookName, verse.Chapter, verse.TextName))),
        };
    }

    /// <summary>
    /// A verse's own page: the verse large, in the page language's text, each name in it linked to its
    /// record as the reader marks it; the way to the whole chapter at the verse; the verses either side;
    /// the same verse in the texts the reader offers first; and the verse in the original, word by word.
    /// </summary>
    public static PageContent SingleVerse(SiteLanguage language, PageWording t, SingleVerseView verse)
    {
        var reference = t.Reference(verse.BookName, verse.Chapter, verse.Verse);
        var chapter = $"{verse.BookName} {verse.Chapter}";
        var chapterPath = PageAddress.ChapterPath(verse.Book, verse.Chapter);
        var versePath = PageAddress.SingleVersePath(verse.Book, verse.Chapter, verse.Verse);
        var textLanguage = verse.TextLanguage ?? language.Code;
        var body = Open(language)
            .Open("nav").Link(language.Path("/"), PageContent.SiteName).Text(Crumb).Link(language.Path(chapterPath), chapter).Close("nav")
            .Element("h1", reference)
            .Open("blockquote", lang: textLanguage, attributes: ("dir", Direction(textLanguage))).Open("p");
        Said(body, language, verse);
        body.Close("p").Close("blockquote")
            .Element("p", verse.TextName, "source")
            .Open("p").Link(language.Path(PageAddress.VersePath(verse.Book, verse.Chapter, verse.Verse)),
                t.Text("verse.wholeChapter", ("chapter", chapter)), className: "chapter-link").Close("p");
        if (verse.Previous is not null || verse.Next is not null)
        {
            body.Open("nav");
            if (verse.Previous is { } previous)
            {
                body.Link(language.Path(PageAddress.SingleVersePath(verse.Book, verse.Chapter, previous)),
                    $"← {t.Reference(verse.BookName, verse.Chapter, previous)}", "prev");
            }

            if (verse.Next is { } next)
            {
                body.Text(verse.Previous is null ? "" : " · ").Link(language.Path(PageAddress.SingleVersePath(verse.Book, verse.Chapter, next)),
                    $"{t.Reference(verse.BookName, verse.Chapter, next)} →", "next");
            }

            body.Close("nav");
        }

        if (verse.Others.Count > 0)
        {
            body.Open("section").Element("h2", t.Text("verse.otherTexts", ("reference", reference)));
            foreach (var other in verse.Others)
            {
                var otherLanguage = other.Language ?? language.Code;
                body.Element("h3", other.TextName)
                    .Open("p", lang: otherLanguage, attributes: ("dir", Direction(otherLanguage))).Text(other.Text).Close("p");
            }

            body.Close("section");
        }

        body.Close("article");
        if (verse.Original is { } original)
        {
            OriginalWords(body, language, t, reference, original);
        }

        var title = string.Join(Separator, reference, t.Quoted(Shortened(verse.Text, TitleQuoteLength)), verse.TextName);
        var beside = Beside(t, OriginalOf(verse.Book));
        var description = AroundQuote(t, verse.Text, quote => t.Text("verse.description",
            ("reference", reference), ("text", verse.TextName), ("quote", quote), ("beside", beside)));
        return Page(StatusCodes.Status200OK, t.Titled(title), reference, description, body) with
        {
            Canonical = versePath,
            Type = "article",
            PageType = "ItemPage",
            Trail = [Start, new Crumb(chapter, chapterPath), new Crumb(reference, versePath)],
            About = (link, _) => StructuredData.Node("CreativeWork",
                ("name", reference),
                ("text", verse.Text),
                ("inLanguage", textLanguage),
                ("url", link(versePath)),
                ("isPartOf", ChapterNode(link, verse.Book, verse.BookName, verse.Chapter, verse.TextName))),
        };
    }

    /// <summary>
    /// The verse's words as the page prints them, the white space between them collapsed as the plain text
    /// is, each name a link to its record where the site has a page for it.
    /// </summary>
    private static void Said(HtmlWriter body, SiteLanguage language, SingleVerseView verse)
    {
        if (verse.Words.Count == 0)
        {
            body.Text(verse.Text);
            return;
        }

        var pieces = new List<(string Text, string? Href)>();
        foreach (var word in verse.Words)
        {
            pieces.Add((word.Surface, word.Record is { } record ? PageAddress.TargetPath(record.Kind, record.Slug) : null));
            pieces.Add((word.Trailer, null));
        }

        // Trimmed at both ends and collapsed between, as the verse's plain text is.
        var last = pieces.FindLastIndex(piece => !string.IsNullOrWhiteSpace(piece.Text));
        var space = true;
        for (var i = 0; i <= last; i++)
        {
            var (text, href) = pieces[i];
            var flat = Spaces().Replace(text, " ");
            if (space)
            {
                flat = flat.TrimStart();
            }

            if (i == last)
            {
                flat = flat.TrimEnd();
            }

            if (flat.Length == 0)
            {
                continue;
            }

            space = flat.EndsWith(' ');
            if (href is null)
            {
                body.Text(flat);
            }
            else
            {
                body.Link(language.Path(href), flat, className: "name");
            }
        }
    }

    /// <summary>Right to left for the scripts that run so; null, the page's own way, for the rest.</summary>
    private static string? Direction(string language) => language is "he" or "hbo" or "arc" or "ar" or "arb" or "ur" ? "rtl" : null;

    /// <summary>A chapter as part of its book, and the book as part of the Bible it is read in.</summary>
    private static JsonObject ChapterNode(Func<string, string> link, int book, string bookName, int chapter, string textName) =>
        StructuredData.Node("Chapter",
            ("name", $"{bookName} {chapter}"),
            ("position", chapter),
            ("url", link(PageAddress.ChapterPath(book, chapter))),
            ("isPartOf", StructuredData.Node("Book",
                ("name", bookName),
                ("isPartOf", StructuredData.Node("Book", ("name", textName))))));

    /// <summary>
    /// A chapter as the reader's first screen draws it, for a verse page and a chapter page alike: the
    /// reading bar's room, the chapter's name, and every verse in the reader's own grid, the verse the
    /// address names marked. The reader's stylesheet sets it exactly where the reader will, so it is
    /// shown at once rather than held back for the script, and the reader takes its verses over as
    /// they are while its own copy of the chapter is still on its way (essenthos-web, src/features/reader/landing.tsx).
    /// The class names are that stylesheet's; nothing here is styled by the plain <c>.prerendered</c> rules.
    /// </summary>
    private static HtmlWriter Reading(SiteLanguage language, PageWording t, ChapterView chapter, int? landed)
    {
        var chapterPath = PageAddress.ChapterPath(chapter.Book, chapter.Chapter);
        var reference = $"{chapter.BookName} {chapter.Chapter}";
        var body = new HtmlWriter()
            .Open("article", "prerendered reading", lang: language.Code,
                attributes: [("data-chapter", chapterPath), ("data-verse", landed?.ToString())])
            .Open("div", "reading-bar").Close("div")
            .Open("div", "reading-page")
            .Open("nav", "reading-trail").Link(language.Path("/"), PageContent.SiteName);
        if (landed is not null)
        {
            body.Text(Crumb).Link(language.Path(chapterPath), reference);
        }

        body.Close("nav")
            .Open("div", "reading-head").Element("h1", reference).Close("div");
        if (chapter.Columns.Count > 1)
        {
            Columns(body, language, t, chapter, landed);
        }
        else
        {
            Verses(body, language, chapter, landed);
        }

        body.Open("nav", "reading-turns");
        if (chapter.Chapter > 1)
        {
            body.Link(language.Path(PageAddress.ChapterPath(chapter.Book, chapter.Chapter - 1)),
                $"← {chapter.BookName} {chapter.Chapter - 1}", "prev");
        }

        if (chapter.Chapter < chapter.ChapterCount)
        {
            body.Link(language.Path(PageAddress.ChapterPath(chapter.Book, chapter.Chapter + 1)),
                $"{chapter.BookName} {chapter.Chapter + 1} →", "next");
        }

        return body.Close("nav").Close("div").Close("article");
    }

    /// <summary>The chapter in its one text: a verse grid of one column, the text's name over it where the reader heads it.</summary>
    private static void Verses(HtmlWriter body, SiteLanguage language, ChapterView chapter, int? landed)
    {
        body.Open("div", "verse-grid reading-verses")
            .Element("div", chapter.TextName, "reading-pane");
        var textLanguage = chapter.TextLanguage ?? language.Code;
        foreach (var (number, text) in chapter.Verses)
        {
            body.Open("div", "verse-row", $"v{number}", attributes: ("data-landed", number == landed ? "" : null))
                .Link(language.Path(PageAddress.VersePath(chapter.Book, chapter.Chapter, number)), number.ToString(), className: "verse-number")
                .Open("div", "verse-cell").Element("p", text, "text-ltr", textLanguage).Close("div")
                .Close("div");
        }

        body.Close("div");
    }

    /// <summary>
    /// The chapter in the texts the address names, side by side as the reader lays them out (essenthos-web
    /// ParallelReader): on a phone the line of the texts' names over the chapter, the text the others are set
    /// against, and every verse's texts stacked under its number, each named; on a wide screen a column
    /// each, named at its head. A row for every verse any of them holds, as the reader's rows are, and in
    /// it a cell for each text, saying so where the text has no such verse.
    /// </summary>
    private static void Columns(HtmlWriter body, SiteLanguage language, PageWording t, ChapterView chapter, int? landed)
    {
        var columns = chapter.Columns;
        var count = columns.Count.ToString(CultureInfo.InvariantCulture);
        body.Open("div", "reading-texts", attributes: ("aria-hidden", "true"))
            .Element("span", t.Text("reading.together"), "reading-text-on");
        foreach (var column in columns)
        {
            body.Element("span", column.Name);
        }

        body.Close("div")
            .Open("div", "reading-verses reading-columns")
            .Element("p", t.Text("reading.alignedTo", ("name", columns[0].Name)), "reading-aligned")
            .Open("div", "verse-grid", attributes: [("data-panes", count), ("style", $"--panes:{count}")])
            .Open("div", "verse-number reading-corner").Close("div");
        for (var at = 0; at < columns.Count; at++)
        {
            body.Open("div", "reading-pane", attributes: ("data-pane-last", at == columns.Count - 1 ? "" : null))
                .Open("span", "reading-dot", attributes: ("data-script", Script(columns[at]))).Close("span")
                .Element("span", columns[at].Name, "reading-pane-name");
            if (at == 0)
            {
                body.Element("span", t.Text("reading.base"), "reading-base");
            }

            body.Close("div");
        }

        foreach (var number in columns.SelectMany(column => column.Cells.Keys).Distinct().Order())
        {
            var shown = number.ToString(CultureInfo.InvariantCulture);
            body.Open("div", "verse-row", $"v{number}", attributes: [("data-verse", shown), ("data-landed", number == landed ? "" : null)])
                .Link(language.Path(PageAddress.VersePath(chapter.Book, chapter.Chapter, number)), shown, className: "verse-number");
            foreach (var column in columns)
            {
                body.Open("div", "verse-cell")
                    .Open("span", "reading-label")
                    .Open("span", "reading-dot", attributes: ("data-script", Script(column))).Close("span")
                    .Element("span", column.Name)
                    .Close("span");
                if (column.Cells.GetValueOrDefault(number) is { } cell)
                {
                    if (cell.Badge is { } badge)
                    {
                        body.Element("span", badge, "reading-badge");
                    }

                    body.Open("p", column.Face, lang: column.Language, attributes: ("dir", column.RightToLeft ? "rtl" : "ltr"))
                        .Text(cell.Text).Close("p");
                }
                else
                {
                    body.Element("p", t.Text("reading.notPresentAt", ("corpus", column.Name), ("verse", shown)), "reading-absent");
                }

                body.Close("div");
            }

            body.Close("div");
        }

        body.Close("div").Close("div");
    }

    /// <summary>Which original a text belongs to, for the dot beside its name, as the reader marks it (essenthos-web scriptDotOf).</summary>
    private static string Script(ReadingColumn column) => column.Face switch
    {
        "text-hebrew" => "hebrew",
        "text-greek" => "greek",
        _ => "translation",
    };

    public static PageContent Chapter(SiteLanguage language, PageWording t, ChapterView chapter)
    {
        var reference = $"{chapter.BookName} {chapter.Chapter}";
        var body = Reading(language, t, chapter, landed: null);
        var beside = Beside(t, OriginalOf(chapter.Book));
        var opening = string.Join(' ', chapter.Verses.Take(OpeningVerses).Select(verse => verse.Text));
        var description = opening.Length > 0
            ? AroundQuote(t, opening, quote => t.Text("chapter.description",
                ("reference", reference), ("text", chapter.TextName), ("opening", quote), ("beside", beside)))
            : t.Text("chapter.plain", ("reference", reference), ("beside", beside));
        var chapterPath = PageAddress.ChapterPath(chapter.Book, chapter.Chapter);
        return Page(StatusCodes.Status200OK, t.Titled(reference), reference, description, body) with
        {
            Canonical = chapterPath,
            Type = "article",
            Trail = [Start, new Crumb(reference, chapterPath)],
            About = (link, _) => ChapterNode(link, chapter.Book, chapter.BookName, chapter.Chapter, chapter.TextName),
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

        foreach (var name in record.Names)
        {
            body.Open("p");
            if (name.Label is { } label)
            {
                body.Text(label + ": ");
            }

            OriginalWord(body, language, name.Hebrew, "he", name.HebrewTransliterated, name.HebrewNumbers);
            if (name.Hebrew is not null && name.Greek is not null)
            {
                body.Text("; ");
            }

            OriginalWord(body, language, name.Greek, "el", name.GreekTransliterated, name.GreekNumbers);
            if (name.Meaning is { Length: > 0 } meaning)
            {
                body.Text(" — " + meaning);
            }

            body.Close("p");
        }

        if (record.KnownFor.Count > 0)
        {
            body.Open("ul");
            foreach (var clause in record.KnownFor)
            {
                body.Open("li", lang: clause.Language).Text(clause.Text).Close("li");
            }

            body.Close("ul");
        }

        if (verses is not null)
        {
            body.Element("p", verses).Open("ul");
            foreach (var mention in record.FirstVerses)
            {
                body.Open("li")
                    .Link(language.Path(PageAddress.VersePath(mention.Book, mention.Chapter, mention.Verse)), mention.Label);
                if (mention.Text is { Length: > 0 } said)
                {
                    body.Text(" " + said);
                }

                body.Close("li");
            }

            body.Close("ul")
                .Open("p")
                .Link(language.Path(PageAddress.VersesPath(PageAddress.RecordPath(record.Kind, record.Slug))),
                    t.Text("verses.all", ("count", record.References.ToString(CultureInfo.InvariantCulture))))
                .Close("p");
        }

        body.Close("article");
        var description = Fitted(line, fit => Closed($"{record.Name}: {fit}") + (verses is null ? "" : " " + verses));
        var recordPath = PageAddress.RecordPath(record.Kind, record.Slug);
        return Page(
            StatusCodes.Status200OK,
            t.Titled($"{record.Name}{Separator}{Shortened(line, TitleLineLength)}"),
            record.Name,
            description,
            body) with
        {
            Canonical = recordPath,
            Image = record.Image,
            Type = "article",
            PageType = "ItemPage",
            Trail = Trail(t, PageSections.Encyclopedia, record.Name, recordPath),
            About = (link, _) => RecordNode(link, record, line),
        };
    }

    /// <summary>
    /// A verse in the original under the chapter's copy, outside it, so the reader's lifting of that
    /// copy is untouched: the verse as the edition prints it, then each word with its Strong entries.
    /// </summary>
    private static void OriginalWords(HtmlWriter body, SiteLanguage language, PageWording t, string reference, OriginalVerseView original)
    {
        var direction = original.Language is "hbo" or "arc" or "he" ? "rtl" : null;
        body.Open("section", "prerendered", lang: language.Code)
            .Element("h2", $"{reference} — {t.Text("verse.original", ("text", original.TextName))}")
            .Open("p", lang: original.Language, attributes: ("dir", direction)).Text(original.Text).Close("p")
            .Open("ol");
        foreach (var word in original.Words)
        {
            body.Open("li").Open("span", lang: original.Language, attributes: ("dir", direction)).Text(word.Surface).Close("span");
            foreach (var number in word.Numbers)
            {
                body.Text(" ").Link(language.Path($"/strong/{number}"), number);
            }

            if (word.Gloss is { } gloss)
            {
                body.Text(" — " + gloss);
            }

            body.Close("li");
        }

        body.Close("ol").Close("section");
    }

    /// <summary>A name's original letters, their transliteration and a link to each Strong entry.</summary>
    private static void OriginalWord(
        HtmlWriter body, SiteLanguage language, string? word, string lang, string? transliterated, IReadOnlyList<string> numbers)
    {
        if (word is null)
        {
            return;
        }

        body.Open("span", lang: lang).Text(word).Close("span");
        if (transliterated is { Length: > 0 })
        {
            body.Text(" " + transliterated);
        }

        if (numbers.Count > 0)
        {
            body.Text(" (");
            for (var i = 0; i < numbers.Count; i++)
            {
                body.Text(i > 0 ? ", " : "").Link(language.Path($"/strong/{numbers[i]}"), numbers[i]);
            }

            body.Text(")");
        }
    }

    /// <summary>
    /// A record as schema.org knows it: a person, a place, a term — and anything else as a thing, since
    /// the types search engines check most strictly, such as an event, ask for what a Bible record has
    /// not got: a date, an address, a ticket.
    /// </summary>
    private static JsonObject RecordNode(Func<string, string> link, RecordView record, string line)
    {
        var node = StructuredData.Node(
            record.Kind switch
            {
                EntityKind.Person => "Person",
                EntityKind.Place => "Place",
                EntityKind.Term or EntityKind.Title => "DefinedTerm",
                _ => "Thing",
            },
            ("name", record.Name),
            ("description", WithoutCitations(line)),
            ("url", link(PageAddress.RecordPath(record.Kind, record.Slug))));
        if (record.Coordinates is { } at)
        {
            node["geo"] = StructuredData.Node("GeoCoordinates", ("latitude", at.Latitude), ("longitude", at.Longitude));
        }

        if (record.SameAs.Count > 0)
        {
            node["sameAs"] = new JsonArray([.. record.SameAs.Select(address => (JsonNode)address)]);
        }

        return node;
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
            Trail = Trail(t, PageSections.FamilyTree, title, english),
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
        if (!string.IsNullOrWhiteSpace(entry.Definition))
        {
            body.Element("p", entry.Definition);
        }

        if (!string.IsNullOrWhiteSpace(entry.Outline))
        {
            body.Element("p", entry.Outline, "outline");
        }

        if (!string.IsNullOrWhiteSpace(entry.Derivation))
        {
            body.Element("p", entry.Derivation);
        }

        if (entry.Uses is { } uses)
        {
            body.Element("p", t.Counted("strong.uses", uses) +
                              (entry.CountedIn is { } edition ? " " + t.Text("strong.countedIn", ("text", edition)) : ""));
        }

        if (entry.Renderings.Count > 0 && entry.RenderedIn is { } renderedIn)
        {
            body.Element("h2", t.Text("strong.renderedBy", ("text", renderedIn))).Open("ul");
            foreach (var rendering in entry.Renderings.Take(StrongRenderingsShown))
            {
                body.Element("li", $"{rendering.Text} — {rendering.Count.ToString(CultureInfo.InvariantCulture)}");
            }

            body.Close("ul");
        }

        if (entry.Books.Count > 0)
        {
            body.Element("h2", t.Text("strong.books")).Open("p");
            for (var i = 0; i < entry.Books.Count; i++)
            {
                body.Text((i > 0 ? "; " : "") + $"{entry.Books[i].Book} {entry.Books[i].Count.ToString(CultureInfo.InvariantCulture)}");
            }

            body.Close("p");
        }

        var strongPath = $"/strong/{entry.Number}";
        if (entry.Lemma is not null)
        {
            body.Open("p").Link(language.Path(PageAddress.VersesPath(strongPath)), t.Text("verses.every")).Close("p");
        }

        body.Close("article");
        var lexicon = PageSections.Named("lexicon");
        return Page(StatusCodes.Status200OK, t.Titled(head), head, description, body) with
        {
            Canonical = strongPath,
            Type = "article",
            PageType = "ItemPage",
            Trail = Trail(t, lexicon.Key, head, strongPath),
            About = (link, _) => StructuredData.Node("DefinedTerm",
                ("name", entry.Lemma ?? entry.Number),
                ("alternateName", entry.Transliteration),
                ("termCode", entry.Number),
                ("description", entry.Definition is { Length: > 0 } definition ? PageHtml.Plain(definition) : null),
                ("url", link(strongPath)),
                ("inDefinedTermSet", StructuredData.Node("DefinedTermSet",
                    ("name", t.Text($"page.{lexicon.Key}.title")),
                    ("url", link(lexicon.Path))))),
        };
    }

    /// <summary>
    /// One page of every verse of a record or a Strong number, under the books they are in, each book
    /// with how many of the list's verses it holds, and the earlier and later pages linked.
    /// </summary>
    public static PageContent Verses(SiteLanguage language, PageWording t, VersesView list)
    {
        var title = t.Text("verses.title", ("name", list.Name));
        var body = Open(language).Open("nav").Link(language.Path("/"), PageContent.SiteName).Text(Crumb);
        if (list.Section is { } section)
        {
            body.Link(language.Path(PageSections.Named(section).Path), t.Text($"page.{section}.title")).Text(Crumb);
        }

        body.Link(language.Path(list.Page), list.Name).Close("nav")
            .Element("h1", title)
            .Element("p", t.Counted("verses.count", list.Total) +
                          (list.TextName is { } text ? " " + t.Text("verses.quotedFrom", ("text", text)) : ""));

        var counts = list.Books.ToDictionary(book => book.Ordinal);
        foreach (var inBook in list.Verses.GroupBy(verse => verse.Book))
        {
            var heading = counts.TryGetValue(inBook.Key, out var book) ? $"{book.Name} ({book.Count})" : inBook.First().Label;
            body.Element("h2", heading).Open("ul");
            foreach (var verse in inBook)
            {
                body.Open("li").Link(language.Path(PageAddress.VersePath(verse.Book, verse.Chapter, verse.Verse)), verse.Label);
                if (verse.Text is { Length: > 0 } said)
                {
                    body.Text(" " + said);
                }

                body.Close("li");
            }

            body.Close("ul");
        }

        if (list.Skip > 0 || list.Skip + list.Take < list.Total)
        {
            body.Open("nav");
            string At(int skip) => language.Path(list.English) + Paged(skip, list.Take);
            if (list.Skip > 0)
            {
                body.Link(At(Math.Max(0, list.Skip - list.Take)), t.Text("verses.earlier"), "prev");
            }

            if (list.Skip + list.Take < list.Total)
            {
                body.Link(At(list.Skip + list.Take), t.Text("verses.later"), "next");
            }

            body.Close("nav");
        }

        body.Close("article");
        return Page(StatusCodes.Status200OK, t.Titled(title), title,
            t.Text("verses.description", ("name", list.Name)) + (list.Counted ? " " + t.Counted("verses.count", list.Total) : ""), body) with
        {
            Canonical = list.Indexed ? list.English : null,
            Indexed = list.Indexed,
            PageType = "CollectionPage",
            Trail = list.Section is null
                ? [Start, new Crumb(list.Name, list.Page), new Crumb(title, list.English)]
                : [Start, new Crumb(t.Text($"page.{list.Section}.title"), PageSections.Named(list.Section).Path),
                    new Crumb(list.Name, list.Page), new Crumb(title, list.English)],
        };
    }

    private static string Paged(int skip, int take)
    {
        var query = new List<string>(2);
        if (skip > 0)
        {
            query.Add($"skip={skip}");
        }

        if (take != PageAddress.VersesPerPage)
        {
            query.Add($"take={take}");
        }

        return query.Count == 0 ? "" : "?" + string.Join('&', query);
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
        string? image = null,
        string? section = null,
        Described? about = null)
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
            PageType = about is null ? "WebPage" : "ItemPage",
            Trail = Trail(t, section, name, english),
            About = about,
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
            IsHome = true,
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
                .Element("p", t.Optional($"page.{key}.lead") ?? description)
                .Close("article")
            : null;
        return Page(StatusCodes.Status200OK, t.Titled(title), title, description, body) with
        {
            Canonical = address.Indexed ? address.English : null,
            Indexed = address.Indexed,
            PageType = SectionTypes.GetValueOrDefault(key, "WebPage"),
            Trail = [Start, new Crumb(title, address.English)],
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

    /// <summary>
    /// A record's short description without the verse codes the source writes into it, <c>(EXO 13:3)</c>
    /// or <c>(MAT 28:19; 1CO 3:16)</c>; a code standing in the words is the book's name,
    /// <em>Pelonite in 1 Chronicles 11:27</em>.
    /// </summary>
    public static string WithoutCitations(string text) =>
        Bare().Replace(
                Citations().Replace(text, ""),
                code => BibleBookAbbreviation.GetAbbreviation(code.Groups[1].Value) is { } book
                    ? $"{BookReferences.Name(book.Ordinal)} {code.Groups[2].Value}"
                    : code.Value)
            .Trim();

    /// <summary>The first step of every trail.</summary>
    private static readonly Crumb Start = new(PageContent.SiteName, "/");

    /// <summary>The site's pages that are lists of what it holds, or say who it is or how to reach it.</summary>
    private static readonly IReadOnlyDictionary<string, string> SectionTypes = new Dictionary<string, string>
    {
        ["texts"] = "CollectionPage",
        ["lexicon"] = "CollectionPage",
        [PageSections.Encyclopedia] = "CollectionPage",
        ["occupations"] = "CollectionPage",
        ["events"] = "CollectionPage",
        ["downloads"] = "CollectionPage",
        ["about"] = "AboutPage",
        ["contact"] = "ContactPage",
    };

    /// <summary>The home page, the section a page is listed in where it has one, and the page.</summary>
    private static IReadOnlyList<Crumb> Trail(PageWording t, string? section, string name, string english) =>
        section is null
            ? [Start, new Crumb(name, english)]
            : [Start, new Crumb(t.Text($"page.{section}.title"), PageSections.Named(section).Path), new Crumb(name, english)];

    private static readonly string[] HomeSections =
    [
        "texts", "lexicon", "syntax", PageSections.Encyclopedia, PageSections.FamilyTree, "events", "timeline", "map",
    ];

    private static string Beside(PageWording t, Original original) => t.Text($"beside.{Spelled(original)}");

    private static string Spelled(Original original) => original == Original.Greek ? "greek" : "hebrew";

    private static PageContent Page(int status, string title, string heading, string description, HtmlWriter? body) =>
        new(status, title, heading, Shortened(description, DescriptionLength), body?.ToString() ?? "");

    /// <summary>How many of a number's renderings the server's copy lists.</summary>
    private const int StrongRenderingsShown = 12;

    private static HtmlWriter Open(SiteLanguage language) => new HtmlWriter().Open("article", "prerendered", lang: language.Code);

    [GeneratedRegex(@"[\s,;:.!?—–-]+$")]
    private static partial Regex Trailing();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();

    [GeneratedRegex(@"[.!?…]$")]
    private static partial Regex Sentence();

    [GeneratedRegex(@"\s*\([1-4]?[A-Z]{2,4} \d+:\d+(?:[-–]\d+)?(?:[,;]\s*(?:(?:[1-4]?[A-Z]{2,4} )?\d+(?::\d+)?(?:[-–]\d+)?|etc\.))*\)")]
    private static partial Regex Citations();

    [GeneratedRegex(@"\b([1-4]?[A-Z]{2,4}) (\d+:\d+(?:[-–]\d+)?)")]
    private static partial Regex Bare();
}
