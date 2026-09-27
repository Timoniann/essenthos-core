using Essenthos.Core.Corpus;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Strong;

namespace Essenthos.Core.Pages;

internal enum PageKind
{
    Home,
    Chapter,
    Verse,
    Record,
    FamilyTree,
    Event,
    Period,
    Strong,
    Text,
    Search,
    Section,
    NotFound,
}

/// <summary>A page of the site that is not about one record: an index, a tool, a page every site owes its readers.</summary>
/// <param name="Key">Its name in the reader's wording, <c>page.&lt;key&gt;</c>.</param>
/// <param name="Private">A reader's own page, which is nobody else's to find.</param>
/// <param name="Listed">Whether the sitemap offers it.</param>
/// <param name="Queries">The parameters that make it a list somebody asked for, which no search engine should keep.</param>
internal sealed record PageSection(string Key, string Path, bool Private = false, bool Listed = true, IReadOnlyList<string>? Queries = null);

internal static class PageSections
{
    public const string Encyclopedia = "encyclopedia";
    public const string Search = "search";
    public const string Syntax = "syntax";
    public const string FamilyTree = "familyTree";
    public const string NotFound = "notFound";

    /// <summary>Every such page the reader has, by its English address, as the reader's own route table has them.</summary>
    public static readonly IReadOnlyList<PageSection> All =
    [
        new("texts", "/texts"),
        new("lexicon", "/lexicon", Queries: ["q"]),
        new(Syntax, "/syntax", Queries: ["on", "feature", "word", "book"]),
        new(Encyclopedia, "/encyclopedia", Queries: ["q"]),
        new(FamilyTree, "/family-tree"),
        new("events", "/events"),
        new("timeline", "/timeline"),
        new("kings", "/timeline/kings"),
        new("map", "/map"),
        new("concordance", "/concordance", Queries: ["word"]),
        new("parallels", "/parallels", Listed: false, Queries: ["a", "b"]),
        new(Search, "/search", Listed: false, Queries: ["q"]),
        new("about", "/about"),
        new("sources", "/sources"),
        new("licence", "/licence"),
        new("privacy", "/privacy"),
        new("terms", "/terms"),
        new("contact", "/contact"),
        new("support", "/support"),
        new("downloads", "/downloads"),
        new("api", "/api"),
        new("suggestions", "/suggestions", Listed: false),
        new("newSuggestion", "/suggestions/new", Private: true, Listed: false),
        new("account", "/account", Private: true, Listed: false),
        new("bookmarks", "/bookmarks", Private: true, Listed: false),
        new("admin", "/admin", Private: true, Listed: false),
    ];

    public static PageSection Named(string key) => All.First(section => section.Key == key);
}

/// <summary>
/// A page address taken apart: which language, which kind of page, and what it names. Only the shape
/// is checked here — whether the record or the verse exists is the loader's question.
/// </summary>
/// <param name="English">The address without its language prefix, as the English page has it, in its one canonical spelling.</param>
internal sealed record PageAddress(SiteLanguage Language, PageKind Kind, string English)
{
    public int Book { get; init; }

    public int Chapter { get; init; }

    public int Verse { get; init; }

    public string? Slug { get; init; }

    public EntityKind? RecordKind { get; init; }

    public PageSection? Section { get; init; }

    /// <summary>What was searched for, on a search page.</summary>
    public string? Query { get; init; }

    /// <summary>A reader's own page, or a list a query picked: a page to open and never one to index.</summary>
    public bool Private { get; init; }

    /// <summary>The English-language address the same page has without a prefix, where the prefix was <c>/en</c>.</summary>
    public string? RedirectTo { get; init; }

    public bool Indexed => Kind != PageKind.NotFound && !Private;

    private static readonly IReadOnlyDictionary<string, EntityKind> RecordRoutes = new Dictionary<string, EntityKind>(StringComparer.Ordinal)
    {
        ["people"] = EntityKind.Person,
        ["places"] = EntityKind.Place,
        ["peoples"] = EntityKind.People,
        ["terms"] = EntityKind.Term,
        ["titles"] = EntityKind.Title,
        ["objects"] = EntityKind.Object,
        ["observances"] = EntityKind.Observance,
    };

    public static string RecordPath(EntityKind kind, string slug) =>
        $"/{RecordRoutes.First(route => route.Value == kind).Key}/{slug}";

    /// <param name="kind">The kind as the API spells it, <c>person</c>.</param>
    public static string RecordPath(string kind, string slug) =>
        $"/{RecordRoutes.First(route => EnumSpelling.Of(route.Value) == kind).Key}/{slug}";

    public static string ChapterPath(int book, int chapter) => $"/read/{BookReferences.Slug(book)}/{chapter}";

    public static string VersePath(int book, int chapter, int verse) => $"{ChapterPath(book, chapter)}/{verse}";

    /// <param name="path">The path as the reader asked for it, without the query.</param>
    /// <param name="query">The address's query parameters, by name.</param>
    public static PageAddress Parse(string path, IReadOnlyDictionary<string, string>? query = null)
    {
        query ??= new Dictionary<string, string>();
        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var language = SiteLanguage.English;
        if (segments.Length > 0 && SiteLanguage.Prefixed(segments[0]) is { } prefixed)
        {
            language = prefixed;
            segments = segments[1..];
        }
        else if (segments.Length > 0 && segments[0] == SiteLanguage.English.Code)
        {
            var english = "/" + string.Join('/', segments[1..]);
            return new PageAddress(language, PageKind.NotFound, english) { RedirectTo = english };
        }

        var rest = "/" + string.Join('/', segments);
        var notFound = new PageAddress(language, PageKind.NotFound, rest);
        var verseParameter = query.TryGetValue("v", out var v) ? Number(v) : null;

        switch (segments)
        {
            case []:
                return new PageAddress(language, PageKind.Home, "/");

            case ["read", var book, var chapter] when verseParameter is { } atVerse:
                return AtVerse(language, book, chapter, atVerse.ToString()) ?? notFound;

            case ["read", var book, var chapter]:
                return BookOf(book) is { } ordinal && Number(chapter) is { } inChapter
                    ? new PageAddress(language, PageKind.Chapter, ChapterPath(ordinal, inChapter))
                    {
                        Book = ordinal, Chapter = inChapter,
                    }
                    : notFound;

            case ["read", var book, var chapter, var verse]:
                return AtVerse(language, book, chapter, verse) ?? notFound;

            case ["strong", var number]:
                return StrongNumbers.Normalize(number) is { } strong
                    ? new PageAddress(language, PageKind.Strong, $"/strong/{strong}") { Slug = strong }
                    : notFound;

            case ["events", var slug]:
                return new PageAddress(language, PageKind.Event, $"/events/{slug}") { Slug = slug };

            case ["periods", var slug]:
                return new PageAddress(language, PageKind.Period, $"/periods/{slug}") { Slug = slug };

            case ["texts", var id]:
                return new PageAddress(language, PageKind.Text, $"/texts/{id}") { Slug = id };

            case [var route, var slug] when RecordRoutes.TryGetValue(route, out var kind):
                return new PageAddress(language, PageKind.Record, rest) { Slug = slug, RecordKind = kind };

            case ["family-tree"] when query.TryGetValue("person", out var person) && person.Length > 0:
                return new PageAddress(language, PageKind.FamilyTree, $"/family-tree?person={Uri.EscapeDataString(person)}")
                {
                    Slug = person,
                };

            case ["search"] when query.TryGetValue("q", out var searched) && searched.Length > 0:
                return new PageAddress(language, PageKind.Search, rest) { Query = searched, Private = true };

            // A syntax group is addressed by a row id, which a rebuild renumbers, so it is a page to
            // open and never one to index.
            case ["syntax", var id] when long.TryParse(id, out _):
                return InSection(language, rest, PageSections.Named(PageSections.Syntax)) with { Private = true };

            case ["suggestions", var id] when id != "new":
                return InSection(language, rest, PageSections.Named("suggestions"));

            case ["admin", ..]:
                return InSection(language, rest, PageSections.Named("admin"));
        }

        if (PageSections.All.FirstOrDefault(section => section.Path == rest) is not { } found)
        {
            return notFound;
        }

        var asked = (found.Queries ?? []).Any(parameter => query.TryGetValue(parameter, out var value) && value.Length > 0);
        return InSection(language, rest, found) with { Private = found.Private || asked };
    }

    private static PageAddress? AtVerse(SiteLanguage language, string book, string chapter, string verse) =>
        BookOf(book) is { } ordinal && Number(chapter) is { } c && Number(verse) is { } v
            ? new PageAddress(language, PageKind.Verse, VersePath(ordinal, c, v)) { Book = ordinal, Chapter = c, Verse = v }
            : null;

    private static PageAddress InSection(SiteLanguage language, string rest, PageSection section) =>
        new(language, PageKind.Section, rest) { Section = section, Private = section.Private };

    private static int? BookOf(string book) =>
        BookReferences.ResolveOrdinal(book) is { } ordinal && !int.TryParse(book, out _) ? ordinal : null;

    private static int? Number(string value) =>
        value.Length is > 0 and <= 4 && value.All(char.IsAsciiDigit) && int.Parse(value) is > 0 and var number ? number : null;
}
