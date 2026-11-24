using Essenthos.Core.Configuration;
using Essenthos.Core.Corpus;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Endpoints;
using Essenthos.Core.Strong;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using System.Text.Json.Serialization.Metadata;

namespace Essenthos.Core.Pages;

/// <summary>A text a page can quote, with the books it holds.</summary>
internal sealed record ReaderText(
    int Id,
    string Slug,
    string Name,
    string? NameNative,
    string Language,
    TextKind Kind,
    IReadOnlyList<int> Books)
{
    public bool Covers(int book) => Books.Contains(book);

    public bool RightToLeft { get; init; }
}

/// <summary>
/// Which text a page in each language quotes — the same answer the reader's own script gives a
/// reader who has chosen nothing, so the text a search engine indexes is the text a visitor sees.
/// </summary>
internal static class ReaderTexts
{
    /// <summary>
    /// The owner's choice where it is in the page's language, otherwise the text named for the
    /// language, then the owner's choice whatever its language, then any translation in the
    /// language, then any translation at all.
    /// </summary>
    public static ReaderText? Default(IReadOnlyList<ReaderText> texts, SiteLanguage language, string? siteChoice)
    {
        var translations = texts.Where(text => text.Kind == TextKind.Translation).ToList();
        var owners = siteChoice is null ? null : Find(translations, siteChoice);
        if (owners is not null && owners.Language == language.Corpus)
        {
            return owners;
        }

        return Find(translations, language.Text)
               ?? owners
               ?? translations.FirstOrDefault(text => text.Language == language.Corpus)
               ?? translations.FirstOrDefault()
               ?? texts.FirstOrDefault();
    }

    /// <summary>
    /// The texts a book is quoted from, in the order they are tried: the page's own where it holds
    /// the book, then a translation in the same language, then any translation, then anything that
    /// holds it — the deuterocanon is in the Septuagint and nowhere else.
    /// </summary>
    public static IEnumerable<ReaderText> ForBook(IReadOnlyList<ReaderText> texts, ReaderText? chosen, int book)
    {
        var covering = texts.Where(text => text.Covers(book)).ToList();
        var own = chosen is not null && chosen.Covers(book) ? new[] { chosen } : [];
        return own
            .Concat(covering.Where(text => text.Kind == TextKind.Translation && text.Language == chosen?.Language))
            .Concat(covering.Where(text => text.Kind == TextKind.Translation))
            .Concat(covering)
            .Distinct();
    }

    /// <summary>
    /// The texts a verse is first shown in beside the one it is read in, as the reader's own list starts
    /// for a reader who has chosen none (essenthos-web, defaultOtherTexts): up to three that hold the book,
    /// those in the page's language first, then the originals, then the rest, each group by name.
    /// </summary>
    public static IReadOnlyList<ReaderText> Others(IReadOnlyList<ReaderText> texts, ReaderText shown, int book, SiteLanguage language, int count = 3) =>
    [
        .. texts
            .Where(text => text.Id != shown.Id && text.Covers(book))
            .OrderBy(text => text.Language == language.Corpus ? 0 : text.Kind != TextKind.Translation ? 1 : 2)
            .ThenBy(text => text.Name, StringComparer.InvariantCultureIgnoreCase)
            .Take(count),
    ];

    private static ReaderText? Find(IEnumerable<ReaderText> texts, string slug) =>
        texts.FirstOrDefault(text => string.Equals(text.Slug, slug, StringComparison.OrdinalIgnoreCase));
}

/// <summary>What the database says about one page, turned into what the server writes for it.</summary>
internal sealed partial class PageLoader
{
    private readonly AppDbContext db;
    private readonly ICanonIndex canon;
    private readonly SiteSettingsFile settings;
    private readonly PageCache cache;
    private readonly DownloadsCatalogue downloads;
    private readonly PlacePictures? places;

    public PageLoader(AppDbContext db, ICanonIndex canon, SiteSettingsFile settings, PageCache cache, DownloadsCatalogue downloads, PlacePictures? places = null)
    {
        this.db = db;
        this.canon = canon;
        this.settings = settings;
        this.cache = cache;
        this.downloads = downloads;
        this.places = places;
    }

    /// <summary>How many of a record's verses its page links to; the page itself lists them all.</summary>
    private const int VersesNamed = 8;

    /// <summary>How many of the events inside a period its page lists, as the reader's page does.</summary>
    private const int PeriodEventsShown = 30;

    /// <summary>The language whose texts print the frame's own book names, so their names are not asked for.</summary>
    private const string HeadwordLanguage = "eng";

    /// <summary>The languages other than English a record's structured line is phrased in for a page.</summary>
    private static readonly HashSet<string> PhrasedLanguages = ["ukr", "deu", "spa"];

    public async Task<PageContent> Load(PageAddress address, PageWording wording, CancellationToken cancellationToken)
    {
        var language = address.Language;
        return address.Kind switch
        {
            PageKind.Home => PageViews.Home(language, wording, await Books(language, wording, cancellationToken)),
            PageKind.Chapter => await Chapter(address, wording, cancellationToken),
            PageKind.Verse => await Verse(address, wording, cancellationToken),
            PageKind.SingleVerse => await SingleVerse(address, wording, cancellationToken),
            PageKind.Record => await Record(address, wording, cancellationToken),
            PageKind.OccupationCatalogue => await OccupationCatalogue(address, wording, cancellationToken),
            PageKind.Occupation => await Occupation(address, wording, cancellationToken),
            PageKind.FamilyTree => await FamilyTree(address, wording, cancellationToken),
            PageKind.Event => await Event(address, wording, cancellationToken),
            PageKind.Period => await Period(address, wording, cancellationToken),
            PageKind.Strong => await Strong(address, wording, cancellationToken),
            PageKind.Verses => await Verses(address, wording, cancellationToken),
            PageKind.Text => await Text(address, wording, cancellationToken),
            PageKind.Search => PageViews.Search(wording, address.Query!),
            PageKind.Help => PageViews.Help(language, wording, address),
            PageKind.Section when address.Section?.Key == "downloads" =>
                PageViews.Downloads(language, wording, address, downloads.Current()),
            PageKind.Section => PageViews.Section(language, wording, address),
            _ => null,
        } ?? PageViews.NotFound(language, wording);
    }

    /// <summary>
    /// What a page in this language is composed under that the corpus does not fix: the text it quotes, whether
    /// generated pictures are shown, and, for a record, its place's map picture.
    /// </summary>
    private async Task<PageConditions> Conditions(string? slug, SiteLanguage language, CancellationToken cancellationToken) =>
        new((await Chosen(language, cancellationToken))?.Slug,
            settings.Is(SiteSettings.GeneratedImages),
            slug is not null && places?.For(slug, language.Corpus) is { } picture ? picture.Tag : null);

    private async Task<bool> Holds(PageConditions conditions, string? slug, SiteLanguage language, CancellationToken cancellationToken) =>
        conditions == await Conditions(slug, language, cancellationToken);

    /// <summary>
    /// The stored document of a page, read in one statement; null where the build made none, made it with another version or
    /// by other code than this (<see cref="PageDocuments.Build"/>), or the database predates the table.
    /// </summary>
    private async Task<T?> Stored<T>(string kind, SiteLanguage language, string address, JsonTypeInfo<T> info, CancellationToken cancellationToken)
        where T : class
    {
        byte[]? body;
        try
        {
            body = await db.PageDocuments
                .Where(d => d.Kind == kind && d.Language == language.Code && d.Address == address)
                .Select(d => d.Body)
                .FirstOrDefaultAsync(cancellationToken);
        }
        catch (PostgresException missing) when (missing.SqlState == PostgresErrorCodes.UndefinedTable)
        {
            return null;
        }

        if (body is null)
        {
            return null;
        }

        var (document, data) = PageDocuments.Unpack(body, info);
        return document switch
        {
            RecordDocument record when PageDocuments.Readable(record.Version, record.Build) => (T)(object)(record with { Data = data }),
            StrongDocument strong when PageDocuments.Readable(strong.Version, strong.Build) => (T)(object)(strong with { Data = data }),
            OccupationDocument occupation when PageDocuments.Readable(occupation.Version, occupation.Build) =>
                (T)(object)(occupation with { Data = data }),
            _ => null,
        };
    }

    private async Task<ReaderText?> Chosen(SiteLanguage language, CancellationToken cancellationToken) =>
        ReaderTexts.Default(
            await Texts(cancellationToken),
            language,
            settings.Choices.GetValueOrDefault(SiteSettings.ReaderTranslation));

    private Task<IReadOnlyList<ReaderText>> Texts(CancellationToken cancellationToken) =>
        cache.Remember("texts", PageCache.Corpus, async () =>
        {
            var entries = await canon.Texts(cancellationToken);
            var rows = await db.Texts
                .OrderBy(t => t.Slug)
                .Select(t => new { t.Id, t.Slug, t.Name, t.NameNative, t.Language, t.Kind, t.Direction })
                .ToListAsync(cancellationToken);
            return (IReadOnlyList<ReaderText>)
            [
                .. rows.Join(entries, row => row.Id, entry => entry.Id, (row, entry) =>
                    new ReaderText(row.Id, row.Slug, row.Name, row.NameNative, row.Language, row.Kind, entry.Books)
                    {
                        RightToLeft = row.Direction == TextDirection.RightToLeft,
                    }),
            ];
        });

    private Task<IReadOnlyDictionary<int, string>> BookNames(ReaderText text, CancellationToken cancellationToken) =>
        cache.Remember($"books:{text.Id}", PageCache.Corpus, async () =>
            (IReadOnlyDictionary<int, string>)await db.Books
                .Where(b => b.TextId == text.Id && b.NameNative != null)
                .ToDictionaryAsync(b => b.CanonicalOrdinal, b => b.NameNative!, cancellationToken));

}

/// <summary>A verse's primary place in the frame, with what decides which of two verses sharing it a page quotes.</summary>
internal sealed record PlacedVerse(
    int CanonicalBook, int CanonicalChapter, int CanonicalVerse, int VerseId, int ChapterNumber, int Number, string Label, int Sequence);
