using Essenthos.Core.Configuration;
using Essenthos.Core.Corpus;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Endpoints;
using Essenthos.Core.Strong;
using Microsoft.EntityFrameworkCore;

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

    private static ReaderText? Find(IEnumerable<ReaderText> texts, string slug) =>
        texts.FirstOrDefault(text => string.Equals(text.Slug, slug, StringComparison.OrdinalIgnoreCase));
}

/// <summary>What the database says about one page, turned into what the server writes for it.</summary>
internal sealed class PageLoader(
    AppDbContext db,
    ICanonIndex canon,
    SiteSettingsFile settings,
    PageCache cache)
{
    /// <summary>How many of a record's verses its page links to; the page itself lists them all.</summary>
    private const int VersesNamed = 8;

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
            PageKind.Record => await Record(address, wording, cancellationToken),
            PageKind.FamilyTree => await FamilyTree(address, wording, cancellationToken),
            PageKind.Event => await Event(address, wording, cancellationToken),
            PageKind.Period => await Period(address, wording, cancellationToken),
            PageKind.Strong => await Strong(address, wording, cancellationToken),
            PageKind.Text => await Text(address, wording, cancellationToken),
            PageKind.Search => PageViews.Search(wording, address.Query!),
            PageKind.Section => PageViews.Section(language, wording, address),
            _ => null,
        } ?? PageViews.NotFound(language, wording);
    }

    /// <summary>
    /// What a page in this language calls a book: the name a reader of the language cites it by, where
    /// the wording has one — <c>Markus</c> where the Luther Bible prints <c>Das Evangelium nach Markus</c>
    /// — then the name the text it quotes the book from prints, where that is a translation in a
    /// language other than English, and the frame's name otherwise.
    /// </summary>
    public async Task<string> BookName(
        SiteLanguage language, PageWording wording, int book, CancellationToken cancellationToken)
    {
        if (wording.CitedBook(book) is { Length: > 0 } cited)
        {
            return cited;
        }

        var text = ReaderTexts.ForBook(await Texts(cancellationToken), await Chosen(language, cancellationToken), book)
            .FirstOrDefault();
        if (text is null || text.Kind != TextKind.Translation || text.Language == HeadwordLanguage)
        {
            return BookReferences.Name(book);
        }

        return (await BookNames(text, cancellationToken)).GetValueOrDefault(book) ?? BookReferences.Name(book);
    }

    private async Task<IReadOnlyList<BookLink>> Books(
        SiteLanguage language, PageWording wording, CancellationToken cancellationToken)
    {
        var books = new List<BookLink>(BookReferences.CanonBookCount);
        foreach (var ordinal in BookReferences.Ordinals)
        {
            books.Add(new BookLink(ordinal, await BookName(language, wording, ordinal, cancellationToken)));
        }

        return books;
    }

    private async Task<PageContent?> Chapter(PageAddress address, PageWording wording, CancellationToken cancellationToken)
    {
        var chapterCount = await canon.ChapterCount(address.Book, cancellationToken);
        if (address.Chapter > chapterCount)
        {
            return null;
        }

        var language = address.Language;
        foreach (var text in ReaderTexts.ForBook(await Texts(cancellationToken), await Chosen(language, cancellationToken), address.Book))
        {
            var verses = await ChapterVerses(text.Id, address.Book, address.Chapter, cancellationToken);
            if (verses.Count > 0)
            {
                return PageViews.Chapter(language, wording, new ChapterView(
                    address.Book,
                    await BookName(language, wording, address.Book, cancellationToken),
                    address.Chapter,
                    chapterCount,
                    verses,
                    TextName(wording, text)));
            }
        }

        return null;
    }

    private async Task<PageContent?> Verse(PageAddress address, PageWording wording, CancellationToken cancellationToken)
    {
        if (address.Chapter > await canon.ChapterCount(address.Book, cancellationToken))
        {
            return null;
        }

        var language = address.Language;
        foreach (var text in ReaderTexts.ForBook(await Texts(cancellationToken), await Chosen(language, cancellationToken), address.Book))
        {
            var verses = await ChapterVerses(text.Id, address.Book, address.Chapter, cancellationToken);
            var said = verses.Where(verse => verse.Number == address.Verse).Select(verse => verse.Text).FirstOrDefault()
                       ?? await SpannedVerse(text.Id, address, cancellationToken);
            if (said is null)
            {
                continue;
            }

            return PageViews.Verse(language, wording, new VerseView(
                address.Book,
                await BookName(language, wording, address.Book, cancellationToken),
                address.Chapter,
                address.Verse,
                said,
                TextName(wording, text))
            {
                Previous = verses.Where(verse => verse.Number < address.Verse).Select(verse => (int?)verse.Number).LastOrDefault(),
                Next = verses.Where(verse => verse.Number > address.Verse).Select(verse => (int?)verse.Number).FirstOrDefault(),
            });
        }

        return null;
    }

    private async Task<PageContent?> Record(PageAddress address, PageWording wording, CancellationToken cancellationToken)
    {
        if (await Entity(address.Slug!, address.Language, cancellationToken) is not { } entity)
        {
            return null;
        }

        var language = address.Language;
        var localDistinguisher = (await EntityDistinguishers.Of(db, [entity.Id], language.Corpus, cancellationToken))
            .GetValueOrDefault(entity.Id);
        // The structured line where it is phrased in the page's language — every language on an English
        // page, Ukrainian, German and Spanish on theirs — and the source's English line on English pages only.
        var descriptor = await Descriptors.Of(db, entity.Slug, language.Corpus, cancellationToken);
        if (descriptor is not null && !language.IsEnglish && !PhrasedLanguages.Contains(descriptor.Language))
        {
            descriptor = null;
        }

        var described = descriptor is null ? "" : string.Concat(descriptor.Parts.Select(part => part.Text)).Trim();
        var ours = await OursOnlyRecords.Among(db, [entity.Slug], cancellationToken);
        var english = language.IsEnglish ? OursOnlyRecords.Line(ours, entity.Slug, entity.Distinguisher) : null;
        var line = new[] { localDistinguisher, described, english }
            .FirstOrDefault(said => !string.IsNullOrWhiteSpace(said));
        var tally = await db.Entities
            .Where(e => e.Id == entity.Id)
            .Select(EncyclopediaEndpoints.Tally)
            .SingleAsync(cancellationToken);
        var first = await db.EntityVerses.Shown()
            .Where(v => v.EntityId == entity.Id)
            .Select(v => new { v.CanonicalBook, v.CanonicalChapter, v.CanonicalVerse })
            .Distinct()
            .OrderBy(v => v.CanonicalBook).ThenBy(v => v.CanonicalChapter).ThenBy(v => v.CanonicalVerse)
            .Take(VersesNamed)
            .ToListAsync(cancellationToken);
        var mentions = new List<VerseMention>(first.Count);
        foreach (var verse in first)
        {
            mentions.Add(new VerseMention(
                verse.CanonicalBook, verse.CanonicalChapter, verse.CanonicalVerse,
                wording.Reference(
                    await BookName(language, wording, verse.CanonicalBook, cancellationToken),
                    verse.CanonicalChapter,
                    verse.CanonicalVerse)));
        }

        return PageViews.Record(language, wording, new RecordView(
            entity.Kind,
            entity.Slug,
            entity.Shown,
            line is null ? null : PageViews.WithoutCitations(line),
            descriptor is null
                ? []
                : [.. descriptor.Parts.Select(part => new LinePart(
                    part.Text,
                    part.Entity is { } target ? PageAddress.RecordPath(target.Kind, target.Slug) : null))],
            tally.References,
            mentions)
        {
            Image = await Picture(entity.Slug, cancellationToken),
        });
    }

    private async Task<PageContent?> FamilyTree(PageAddress address, PageWording wording, CancellationToken cancellationToken) =>
        await Entity(address.Slug!, address.Language, cancellationToken) is { } entity
            ? PageViews.FamilyTree(
                address.Language, wording, $"/family-tree?person={Uri.EscapeDataString(entity.Slug)}", entity.Shown,
                await Picture(entity.Slug, cancellationToken))
            : null;

    private async Task<PageContent?> Event(PageAddress address, PageWording wording, CancellationToken cancellationToken)
    {
        var found = await db.Events
            .Where(e => e.Slug == address.Slug)
            .Select(e => new
            {
                e.Slug,
                e.Name,
                e.Description,
                Entity = e.Entity == null ? null : e.Entity.Slug,
                e.CanonicalBook,
                e.CanonicalChapter,
                e.CanonicalVerse,
            })
            .FirstOrDefaultAsync(cancellationToken);
        if (found is null)
        {
            return null;
        }

        var language = address.Language;
        var links = new List<(string, string)>();
        string? image = null;
        if (found.Entity is { } slug && await Entity(slug, language, cancellationToken) is { } entity)
        {
            links.Add((PageAddress.RecordPath(entity.Kind, entity.Slug), entity.Shown));
            image = await Picture(entity.Slug, cancellationToken);
        }

        if (found is { CanonicalBook: { } book, CanonicalChapter: { } chapter, CanonicalVerse: { } verse })
        {
            links.Add((PageAddress.VersePath(book, chapter, verse),
                wording.Reference(await BookName(language, wording, book, cancellationToken), chapter, verse)));
        }

        // What the dataset says, in its own English, describes an English page; any other is described in its language.
        var said = language.IsEnglish && !string.IsNullOrWhiteSpace(found.Description) ? found.Description : null;
        return PageViews.Named(
            language, wording, $"/events/{found.Slug}", found.Name,
            said ?? wording.Text("event.description", ("name", found.Name)), said, links, image);
    }

    private async Task<PageContent?> Period(PageAddress address, PageWording wording, CancellationToken cancellationToken)
    {
        var found = await db.Periods
            .Where(p => p.Slug == address.Slug)
            .Select(p => new
            {
                p.Slug,
                p.Name,
                p.Notes,
                Entity = p.Entity == null ? null : p.Entity.Slug,
                Parent = p.Parent == null ? null : new { p.Parent.Slug, p.Parent.Name },
            })
            .FirstOrDefaultAsync(cancellationToken);
        if (found is null)
        {
            return null;
        }

        var language = address.Language;
        var links = new List<(string, string)>();
        if (found.Parent is { } parent)
        {
            links.Add(($"/periods/{parent.Slug}", parent.Name));
        }

        if (found.Entity is { } slug && await Entity(slug, language, cancellationToken) is { } entity)
        {
            links.Add((PageAddress.RecordPath(entity.Kind, entity.Slug), entity.Shown));
        }

        var said = language.IsEnglish && !string.IsNullOrWhiteSpace(found.Notes) ? found.Notes : null;
        return PageViews.Named(
            language, wording, $"/periods/{found.Slug}", found.Name,
            said ?? wording.Text("period.description", ("name", found.Name)), said, links);
    }

    private async Task<PageContent?> Strong(PageAddress address, PageWording wording, CancellationToken cancellationToken)
    {
        var number = address.Slug!;
        var language = address.Language;
        var entry = await db.StrongEntries
            .Where(e => e.StrongNumber == number)
            .Select(e => new { e.StrongNumber, e.Lemma, e.Transliteration, e.Definition, e.Derivation })
            .FirstOrDefaultAsync(cancellationToken);
        if (entry is null)
        {
            return StrongMorphemeCodes.GetDescription(number) is { } morpheme
                ? PageViews.Strong(language, wording, new StrongView(number, null, null, morpheme, null))
                : null;
        }

        // The definition in the page's language where the corpus has it; the lexicon's English otherwise.
        var translated = language.IsEnglish
            ? null
            : await db.StrongEntryTranslations
                .Where(t => t.StrongNumber == number && t.Language == language.Corpus)
                .Select(t => new { t.Definition, t.Derivation })
                .FirstOrDefaultAsync(cancellationToken);
        return PageViews.Strong(language, wording, new StrongView(
            entry.StrongNumber,
            entry.Lemma,
            entry.Transliteration,
            translated?.Definition ?? entry.Definition,
            translated?.Derivation ?? entry.Derivation));
    }

    private async Task<PageContent?> Text(PageAddress address, PageWording wording, CancellationToken cancellationToken)
    {
        var text = (await Texts(cancellationToken))
            .FirstOrDefault(t => string.Equals(t.Slug, address.Slug, StringComparison.OrdinalIgnoreCase));
        if (text is null)
        {
            return null;
        }

        var language = address.Language;
        var name = TextName(wording, text);
        var original = text.Kind != TextKind.Translation;
        IEnumerable<(string, string)> opening = text.Books.Count > 0
            ? [(PageAddress.ChapterPath(text.Books[0], 1), await BookName(language, wording, text.Books[0], cancellationToken))]
            : [];
        return PageViews.Named(
            language,
            wording,
            $"/texts/{text.Slug}",
            name,
            wording.Text(original ? "text.original" : "text.translation", ("name", name)),
            TextSummaries.For(text.Slug)?.GetValueOrDefault(language.Code),
            opening);
    }

    /// <summary>A record by any address it has had, with the name a page in this language gives it.</summary>
    private async Task<ShownEntity?> Entity(string slug, SiteLanguage language, CancellationToken cancellationToken)
    {
        var current = await MergedAddresses.Current(db, slug, cancellationToken);
        var entity = await db.Entities
            .Where(e => e.Slug == current)
            .Select(e => new { e.Id, e.Slug, e.Kind, e.Name, e.Distinguisher })
            .FirstOrDefaultAsync(cancellationToken);
        if (entity is null)
        {
            return null;
        }

        var local = (await EntityNames.Of(db, [entity.Id], language.Corpus, cancellationToken)).GetValueOrDefault(entity.Id);
        return new ShownEntity(entity.Id, entity.Slug, entity.Kind, string.IsNullOrEmpty(local) ? entity.Name : local, entity.Distinguisher);
    }

    private async Task<string?> Picture(string slug, CancellationToken cancellationToken) =>
        (await ImageEndpoints.Leading(db, [slug], cancellationToken, settings.Is(SiteSettings.GeneratedImages)))
        .GetValueOrDefault(slug)?.Url;

    private static string TextName(PageWording wording, ReaderText text) =>
        wording.Map("textName").GetValueOrDefault(text.Slug) ?? text.Name;

    /// <summary>
    /// A chapter's verses in one text, numbered as the shared frame numbers them — the numbers the
    /// addresses use — each as plain text. A verse the edition prints no words for is left out. Kept
    /// a while, because a crawler asks for a chapter's verses one after another.
    /// </summary>
    private Task<IReadOnlyList<(int Number, string Text)>> ChapterVerses(
        int textId, int book, int chapter, CancellationToken cancellationToken) =>
        cache.Remember($"chapter:{textId}:{book}:{chapter}", PageCache.Page,
            () => ReadChapter(textId, book, chapter, cancellationToken),
            verses => verses.Sum(verse => (long)verse.Text.Length) * sizeof(char));

    private async Task<IReadOnlyList<(int Number, string Text)>> ReadChapter(
        int textId, int book, int chapter, CancellationToken cancellationToken)
    {
        var placed = await db.VerseReferences
            .Where(r => r.IsPrimary && r.CanonicalBook == book && r.CanonicalChapter == chapter && r.Verse!.TextId == textId)
            .Select(r => new { r.CanonicalVerse, r.VerseId })
            .ToListAsync(cancellationToken);
        if (placed.Count == 0)
        {
            return [];
        }

        var said = await Said(placed.Select(p => p.VerseId).ToList(), cancellationToken);
        return
        [
            .. placed
                .OrderBy(p => p.CanonicalVerse)
                .Where(p => said.ContainsKey(p.VerseId))
                .Select(p => (p.CanonicalVerse, said[p.VerseId])),
        ];
    }

    /// <summary>The text of a verse that reaches this address without being placed at it: one verse that spans two.</summary>
    private async Task<string?> SpannedVerse(int textId, PageAddress address, CancellationToken cancellationToken)
    {
        var verseId = await db.VerseReferences
            .Where(r => r.CanonicalBook == address.Book && r.CanonicalChapter == address.Chapter
                        && r.CanonicalVerse == address.Verse && r.Verse!.TextId == textId)
            .Select(r => (int?)r.VerseId)
            .FirstOrDefaultAsync(cancellationToken);
        return verseId is { } id ? (await Said([id], cancellationToken)).GetValueOrDefault(id) : null;
    }

    private async Task<Dictionary<int, string>> Said(List<int> verseIds, CancellationToken cancellationToken)
    {
        var words = await db.Words
            .Where(w => verseIds.Contains(w.VerseId) && !w.Elided)
            .OrderBy(w => w.VerseId).ThenBy(w => w.Position)
            .Select(w => new { w.VerseId, w.Surface, w.Trailer })
            .ToListAsync(cancellationToken);
        return words
            .GroupBy(w => w.VerseId)
            .Select(verse => (verse.Key, Text: PageHtml.Plain(string.Concat(verse.Select(w => w.Surface + w.Trailer)))))
            .Where(verse => verse.Text.Length > 0)
            .ToDictionary(verse => verse.Key, verse => verse.Text);
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
                .Select(t => new { t.Id, t.Slug, t.Name, t.NameNative, t.Language, t.Kind })
                .ToListAsync(cancellationToken);
            return (IReadOnlyList<ReaderText>)
            [
                .. rows.Join(entries, row => row.Id, entry => entry.Id, (row, entry) =>
                    new ReaderText(row.Id, row.Slug, row.Name, row.NameNative, row.Language, row.Kind, entry.Books)),
            ];
        });

    private Task<IReadOnlyDictionary<int, string>> BookNames(ReaderText text, CancellationToken cancellationToken) =>
        cache.Remember($"books:{text.Id}", PageCache.Corpus, async () =>
            (IReadOnlyDictionary<int, string>)await db.Books
                .Where(b => b.TextId == text.Id && b.NameNative != null)
                .ToDictionaryAsync(b => b.CanonicalOrdinal, b => b.NameNative!, cancellationToken));

    private sealed record ShownEntity(int Id, string Slug, EntityKind Kind, string Shown, string? Distinguisher);
}
