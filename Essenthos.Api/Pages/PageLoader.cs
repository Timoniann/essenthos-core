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
internal sealed class PageLoader(
    AppDbContext db,
    ICanonIndex canon,
    SiteSettingsFile settings,
    PageCache cache,
    DownloadsCatalogue downloads,
    PlacePictures? places = null)
{
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
            PageKind.Section when address.Section?.Key == "downloads" =>
                PageViews.Downloads(language, wording, address, downloads.Current()),
            PageKind.Section => PageViews.Section(language, wording, address),
            _ => null,
        } ?? PageViews.NotFound(language, wording);
    }

    private async Task<PageContent> OccupationCatalogue(PageAddress address, PageWording wording, CancellationToken cancellationToken)
    {
        var catalogue = await OccupationEndpoints.Catalogue(db, address.Language.Corpus, address.Language.Corpus,
            address.Query is { Length: > 120 } query ? query[..120] : address.Query, address.Category, address.Skip, 100, cancellationToken);
        return PageViews.OccupationCatalogue(address.Language, wording, catalogue, address.Indexed);
    }

    private async Task<PageContent?> Occupation(PageAddress address, PageWording wording, CancellationToken cancellationToken)
    {
        var occupation = await OccupationEndpoints.Detail(db, address.Slug!, address.Language.Corpus,
            address.Language.Corpus, take: 100, cancellationToken: cancellationToken);
        if (occupation is null) return null;
        var references = occupation.People.SelectMany(p => p.Verses).Distinct().ToList();
        var labels = new Dictionary<VerseRefResponse, string>();
        foreach (var reference in references)
            labels[reference] = wording.Reference(await BookName(address.Language, wording, reference.BookOrdinal, cancellationToken),
                reference.Chapter, reference.Verse);
        // The reader's page asks for the first people a page at a time; they are the first of these,
        // so that answer is carried as it would be given rather than asked of the database again.
        var first = occupation with
        {
            Take = PageData.OccupationPeople,
            People = [.. occupation.People.Take(PageData.OccupationPeople)],
        };
        return PageViews.Occupation(address.Language, wording, occupation, labels) with
        {
            Data = PageData.Of([(PageData.OccupationAddress(occupation.Occupation.Slug, address.Language), PageData.Json(first))]),
        };
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
        var named = await NamedTexts(address, cancellationToken);
        if (await SideBySide(address, named, wording, cancellationToken) is { } sides)
        {
            return PageViews.Chapter(language, wording, new ChapterView(
                address.Book,
                await BookName(language, wording, address.Book, cancellationToken),
                address.Chapter,
                chapterCount,
                sides[0].Verses,
                sides[0].Column.Name)
            {
                TextLanguage = sides[0].Column.Language,
                Columns = [.. sides.Select(side => side.Column)],
            }) with
            {
                Data = Carried(address, sides),
            };
        }

        var chosen = named is [var one] ? one : await Chosen(language, cancellationToken);
        foreach (var text in ReaderTexts.ForBook(await Texts(cancellationToken), chosen, address.Book))
        {
            // The page carries the pane as the reader's script asks for it, and takes its verses from the
            // same row: one read of the chapter, not three.
            var pane = await ReadPane(text.Id, address.Book, address.Chapter, cancellationToken);
            var taken = pane is null ? null : Taken(address.Book, address.Chapter, pane);
            var verses = taken?.Plain ?? await WordTableVerses(text.Id, address.Book, address.Chapter, cancellationToken);
            if (verses.Count > 0)
            {
                return PageViews.Chapter(language, wording, new ChapterView(
                    address.Book,
                    await BookName(language, wording, address.Book, cancellationToken),
                    address.Chapter,
                    chapterCount,
                    verses,
                    TextName(wording, text)) { TextLanguage = StructuredData.LanguageTag(text.Language) }) with
                {
                    Data = pane is null
                        ? null
                        : PageData.Of([(PageData.PaneAddress(text.Slug, BookReferences.Slug(address.Book), address.Chapter), PageData.Written(pane))]),
                };
            }
        }

        return null;
    }

    private async Task<PageContent?> Verse(PageAddress address, PageWording wording, CancellationToken cancellationToken)
    {
        var chapterCount = await canon.ChapterCount(address.Book, cancellationToken);
        if (address.Chapter > chapterCount)
        {
            return null;
        }

        var language = address.Language;
        var named = await NamedTexts(address, cancellationToken);
        if (await SideBySide(address, named, wording, cancellationToken) is { } sides
            && sides[0].Verses.Where(verse => verse.Number == address.Verse).Select(verse => verse.Text).FirstOrDefault() is { } first)
        {
            return PageViews.Verse(language, wording, new VerseView(
                address.Book,
                await BookName(language, wording, address.Book, cancellationToken),
                address.Chapter,
                address.Verse,
                first,
                sides[0].Column.Name)
            {
                Verses = sides[0].Verses,
                ChapterCount = chapterCount,
                TextLanguage = sides[0].Column.Language,
                Original = await OriginalVerse(address, wording, cancellationToken),
                Columns = [.. sides.Select(side => side.Column)],
            }) with
            {
                Data = Carried(address, sides),
            };
        }

        var chapterHeld = false;
        var chosen = named is [var one] ? one : await Chosen(language, cancellationToken);
        foreach (var text in ReaderTexts.ForBook(await Texts(cancellationToken), chosen, address.Book))
        {
            var verses = await ChapterVerses(text.Id, address.Book, address.Chapter, cancellationToken);
            chapterHeld |= verses.Count > 0;
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
                Verses = verses,
                ChapterCount = chapterCount,
                TextLanguage = StructuredData.LanguageTag(text.Language),
                Original = await OriginalVerse(address, wording, cancellationToken),
            });
        }

        // A number no text places in the chapter — one edition's own numbering, linked or listed as if it
        // were the shared frame's — is sent to the chapter it was asked in, rather than answered as nothing.
        return chapterHeld
            ? PageViews.NotFound(language, wording) with
            {
                Status = StatusCodes.Status301MovedPermanently,
                MovedTo = PageAddress.ChapterPath(address.Book, address.Chapter),
            }
            : null;
    }

    /// <summary>
    /// A verse's own page: the verse in the text the page's language reads, its names linked to their
    /// records, then the same verse in the other texts the reader offers first, and in the original.
    /// </summary>
    private async Task<PageContent?> SingleVerse(PageAddress address, PageWording wording, CancellationToken cancellationToken)
    {
        var chapterCount = await canon.ChapterCount(address.Book, cancellationToken);
        if (address.Chapter > chapterCount)
        {
            return null;
        }

        var language = address.Language;
        var texts = await Texts(cancellationToken);
        var chapterHeld = false;
        foreach (var text in ReaderTexts.ForBook(texts, await Chosen(language, cancellationToken), address.Book))
        {
            var pane = await ChapterPane(text.Id, address.Book, address.Chapter, cancellationToken);
            var verses = pane?.Plain ?? await WordTableVerses(text.Id, address.Book, address.Chapter, cancellationToken);
            chapterHeld |= verses.Count > 0;
            var said = verses.Where(verse => verse.Number == address.Verse).Select(verse => verse.Text).FirstOrDefault()
                       ?? await SpannedVerse(text.Id, address, cancellationToken);
            if (said is null)
            {
                continue;
            }

            var others = new List<OtherVerse>();
            foreach (var other in ReaderTexts.Others(texts, text, address.Book, language))
            {
                var otherVerses = await ChapterVerses(other.Id, address.Book, address.Chapter, cancellationToken);
                if (otherVerses.Where(verse => verse.Number == address.Verse).Select(verse => verse.Text).FirstOrDefault() is { } otherSaid)
                {
                    others.Add(new OtherVerse(TextName(wording, other), StructuredData.LanguageTag(other.Language), otherSaid));
                }
            }

            var numbers = verses.Select(verse => verse.Number).Where(number => number > 0).ToList();
            return PageViews.SingleVerse(language, wording, new SingleVerseView(
                address.Book,
                await BookName(language, wording, address.Book, cancellationToken),
                address.Chapter,
                address.Verse,
                said,
                TextName(wording, text))
            {
                Words = pane?.At(address.Verse) is { } verse && verse.Text == said ? verse.Words : [],
                TextLanguage = StructuredData.LanguageTag(text.Language),
                Others = others,
                Original = await OriginalVerse(address, wording, cancellationToken),
                Previous = numbers.Where(number => number < address.Verse).Select(number => (int?)number).Max(),
                Next = numbers.Where(number => number > address.Verse).Select(number => (int?)number).Min(),
            });
        }

        return chapterHeld
            ? PageViews.NotFound(language, wording) with
            {
                Status = StatusCodes.Status301MovedPermanently,
                MovedTo = PageAddress.ChapterPath(address.Book, address.Chapter),
            }
            : null;
    }

    private async Task<PageContent?> Record(PageAddress address, PageWording wording, CancellationToken cancellationToken)
    {
        if (await Entity(address.Slug!, address.Language, cancellationToken, address.RecordKind) is not { } entity)
        {
            // A record withdrawn with nothing in its place is gone, not missing, so a search engine
            // forgets the address rather than asking again.
            return WithdrawnAddresses.IsWithdrawn(address.Slug!)
                ? PageViews.NotFound(address.Language, wording) with { Status = StatusCodes.Status410Gone }
                : null;
        }

        // A record folded into another is found by its old address too, and sent to the one that stays,
        // so a search engine moves what it knew of the old page to the new rather than keeping both.
        if (entity.Slug != address.Slug)
        {
            return PageViews.NotFound(address.Language, wording) with
            {
                Status = StatusCodes.Status301MovedPermanently,
                MovedTo = PageAddress.RecordPath(entity.Kind, entity.Slug),
            };
        }

        var language = address.Language;
        // The record as the reader's script asks for it in this language: the page carries it, and its
        // names and what the person is known for are written from it.
        var detail = await EncyclopediaEndpoints.Detail(
            db, settings, places, entity.Slug, language.Corpus, language.Corpus, cancellationToken);
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
        var english = language.IsEnglish ? entity.Distinguisher : null;
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
        var quoted = await TextsAt(
            language, [.. first.Select(v => Address(v.CanonicalBook, v.CanonicalChapter, v.CanonicalVerse))], cancellationToken);
        var mentions = new List<VerseMention>(first.Count);
        foreach (var verse in first)
        {
            mentions.Add(new VerseMention(
                verse.CanonicalBook, verse.CanonicalChapter, verse.CanonicalVerse,
                wording.Reference(
                    await BookName(language, wording, verse.CanonicalBook, cancellationToken),
                    verse.CanonicalChapter,
                    verse.CanonicalVerse))
            {
                Text = quoted.GetValueOrDefault(Address(verse.CanonicalBook, verse.CanonicalChapter, verse.CanonicalVerse)),
            });
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
                    part.Entity is { } target ? PageAddress.TargetPath(target.Kind, target.Slug) : null))],
            tally.References,
            mentions)
        {
            Image = await Picture(entity.Slug, language.Corpus, cancellationToken),
            Coordinates = entity.Kind == EntityKind.Place ? await Coordinates(entity.Id, cancellationToken) : null,
            SameAs = await SameAs(entity.Id, cancellationToken),
            Names = detail is null ? [] : OriginalNames(detail, language),
            KnownFor = detail?.KnownFor is { } known
                ? [.. known.Select(clause => new KnownClause(clause.Text, StructuredData.LanguageTag(clause.Language)))]
                : [],
        }) with
        {
            Data = detail is null ? null : PageData.Of([(PageData.EntityAddress(entity.Slug, language), PageData.Json(detail))]),
        };
    }

    /// <summary>
    /// The record's names as the Hebrew or the Greek writes them, each once, with what it means in the
    /// page's language where that is said; a name other than the page's heading is named first.
    /// </summary>
    private static IReadOnlyList<OriginalName> OriginalNames(EntityResponse detail, SiteLanguage language)
    {
        var heading = detail.LocalName ?? detail.Name;
        return
        [
            .. detail.Names
                .Where(name => name.Hebrew is not null || name.Greek is not null)
                .Select(name =>
                {
                    var label = name.Printed ?? name.LocalLabel ?? (language.IsEnglish ? name.Label : null);
                    return new OriginalName(
                        label is null || string.Equals(label, heading, StringComparison.OrdinalIgnoreCase) ? null : label,
                        name.Hebrew, name.HebrewTransliterated, [.. name.HebrewStrongNumbers],
                        name.Greek, name.GreekTransliterated, [.. name.GreekStrongNumbers],
                        name.LocalMeaning ?? (language.IsEnglish ? name.Meaning : null));
                })
                .DistinctBy(name => (name.Label, name.Hebrew, name.Greek)),
        ];
    }

    private async Task<(double Latitude, double Longitude)?> Coordinates(int entityId, CancellationToken cancellationToken) =>
        await db.PlaceLocations
            .Where(l => l.EntityId == entityId)
            .Select(l => new { l.Latitude, l.Longitude })
            .FirstOrDefaultAsync(cancellationToken) is { } at
            ? (at.Latitude, at.Longitude)
            : null;

    /// <summary>The record's Wikidata item and its Wikipedia articles in the site's languages.</summary>
    private async Task<IReadOnlyList<string>> SameAs(int entityId, CancellationToken cancellationToken)
    {
        var articles = await db.EntityWikipedia
            .Where(w => w.EntityId == entityId)
            .OrderBy(w => w.Language)
            .Select(w => new { w.Language, w.Title, w.Qid })
            .ToListAsync(cancellationToken);
        return
        [
            .. articles.Select(a => a.Qid).Where(qid => !string.IsNullOrEmpty(qid)).Distinct()
                .Select(qid => $"https://www.wikidata.org/wiki/{qid}"),
            .. articles.Where(a => SiteLanguage.All.Any(language => language.Code == a.Language))
                .Select(a => WikipediaLinks.Address(a.Language, a.Title)),
        ];
    }

    private async Task<PageContent?> FamilyTree(PageAddress address, PageWording wording, CancellationToken cancellationToken) =>
        await Entity(address.Slug!, address.Language, cancellationToken) is { } entity
            ? PageViews.FamilyTree(
                address.Language, wording, $"/family-tree?person={Uri.EscapeDataString(entity.Slug)}", entity.Shown,
                await Picture(entity.Slug, address.Language.Corpus, cancellationToken))
            : null;

    private async Task<PageContent?> Event(PageAddress address, PageWording wording, CancellationToken cancellationToken)
    {
        var language = address.Language;
        // The event as the reader's script asks for it in this language: the page carries it and is written from it.
        var detail = await EncyclopediaEndpoints.EventDetail(
            db, address.Slug!, language.Corpus, language.Corpus, cancellationToken);
        if (detail is null)
        {
            return null;
        }

        var reckonings = await Reckonings(cancellationToken);
        var shownFirst = detail.Dates.FirstOrDefault(d => reckonings.Any(r => r.IsDefault && r.Slug == d.Chronology))
                         ?? detail.Dates.FirstOrDefault();
        var english = language.IsEnglish;

        PageLink? place = null;
        if (detail.LocationSlug is { } placeSlug)
        {
            place = await Entity(placeSlug, language, cancellationToken) is { } found
                ? new PageLink(PageAddress.RecordPath(found.Kind, found.Slug), found.Shown)
                : null;
        }

        // The verses the source's description quotes, or the verse the event is read from, in the page
        // language's own text: never the source's quotation, which is the NASB's (PRB-1678).
        IReadOnlyList<VerseRefResponse> written = detail.Quoted.Count > 0
            ? [.. detail.Quoted]
            : detail.Reference is { } reference ? [reference] : [];
        var texts = await TextsAt(
            language, [.. written.Select(v => Address(v.BookOrdinal, v.Chapter, v.Verse))], cancellationToken);
        var verses = new List<(PageLink, string?)>(written.Count);
        foreach (var verse in written)
        {
            verses.Add((
                new PageLink(
                    PageAddress.VersePath(verse.BookOrdinal, verse.Chapter, verse.Verse),
                    wording.Reference(await BookName(language, wording, verse.BookOrdinal, cancellationToken), verse.Chapter, verse.Verse)),
                texts.GetValueOrDefault(Address(verse.BookOrdinal, verse.Chapter, verse.Verse))));
        }

        PageLink? person = null;
        string? image = null;
        if (detail.EntitySlug is { } slug && await Entity(slug, language, cancellationToken) is { } entity)
        {
            person = new PageLink(PageAddress.RecordPath(entity.Kind, entity.Slug), entity.Shown);
            image = await Picture(entity.Slug, language.Corpus, cancellationToken);
        }

        var view = new EventView(detail.Slug, detail.LocalName ?? detail.Name, shownFirst is null ? null : DateYear(wording, shownFirst))
        {
            Kind = english ? detail.Kind : null,
            Description = detail.LocalDescription ?? detail.Description,
            DescriptionInEnglish = !english && detail.LocalDescription is null,
            Mark = detail.Mark switch
            {
                EventMarks.Hypothesis => wording.Text("event.hypothesis"),
                EventMarks.OutsideScripture => wording.Text("event.outsideScripture"),
                _ => null,
            },
            Place = place,
            Region = string.Join(", ", new[]
            {
                place is null ? detail.LocalLocation ?? (english ? detail.Location : null) : null,
                detail.LocalRegion ?? (english ? detail.Region : null),
            }.Where(part => !string.IsNullOrWhiteSpace(part))) is { Length: > 0 } where ? where : null,
            Dates =
            [
                .. detail.Dates
                    .Where(date => date.BceYear is not null)
                    .Select(date => new ReckonedDate(
                        date.Name, DateYear(wording, date), english ? date.Calculation : null)),
            ],
            Verses = verses,
            Person = person,
            Source = detail.Source,
            Image = image,
            Wikipedia = detail.Wikipedia is { } article ? new PageArticle(article.Url, article.Title, article.Language) : null,
        };
        return PageViews.Event(language, wording, view) with
        {
            Data = PageData.Of([(PageData.EventAddress(detail.Slug, language), PageData.Json(detail))]),
        };
    }

    /// <summary>A date's year as the event's own reckoning puts it, in the page language's words.</summary>
    private static string DateYear(PageWording wording, EventDateResponse date) =>
        PageViews.Year(wording, date.BceYear ?? 0, date.Era == "CE");

    private async Task<PageContent?> Period(PageAddress address, PageWording wording, CancellationToken cancellationToken)
    {
        var detail = await EncyclopediaEndpoints.PeriodDetail(db, address.Slug!, cancellationToken);
        if (detail is null)
        {
            return null;
        }

        var language = address.Language;
        var english = language.IsEnglish;
        var reckonings = await Reckonings(cancellationToken);
        var own = detail.Years.TryGetValue("", out var carried) ? carried : null;
        var spans = own is not null
            ? [new ReckonedSpan(null, PageViews.AxisSpan(wording, own[0], own[1], EncyclopediaEndpoints.LastYearBeforeChrist))]
            : reckonings
                .Where(r => detail.Years.ContainsKey(r.Slug))
                .Select(r => new ReckonedSpan(
                    r.Name, PageViews.AxisSpan(wording, detail.Years[r.Slug][0], detail.Years[r.Slug][1], r.LastAnnoMundiBeforeTheCommonEra)))
                .ToList();
        var shown = own is not null
            ? spans[0].Years
            : reckonings.FirstOrDefault(r => r.IsDefault && detail.Years.ContainsKey(r.Slug)) is { } first
                ? spans.First(span => span.Reckoning == first.Name).Years
                : spans.FirstOrDefault()?.Years;

        // The events inside are found on the axis every event's own year is counted on, as the reader does.
        var frame = reckonings.FirstOrDefault(r => r.LastAnnoMundiBeforeTheCommonEra == EncyclopediaEndpoints.LastYearBeforeChrist)
                    ?? reckonings.FirstOrDefault(r => r.IsDefault);
        var span = own ?? (frame is not null && detail.Years.TryGetValue(frame.Slug, out var framed) ? framed : null);
        var inside = new List<(PageLink, string)>();
        var total = 0;
        if (span is not null)
        {
            var within = db.Events.Where(e => e.YearFromCreation >= span[0] && e.YearFromCreation <= span[1]);
            var order = await EncyclopediaEndpoints.InOrder(within, cancellationToken);
            total = order.Count;
            var listed = order.Take(PeriodEventsShown).ToList();
            var titled = await db.Events
                .Where(e => listed.Contains(e.Slug))
                .Select(e => new { e.Slug, e.Name, e.YearFromCreation })
                .ToListAsync(cancellationToken);
            foreach (var one in EncyclopediaEndpoints.Ordered(titled, listed, e => e.Slug))
            {
                inside.Add((new PageLink($"/events/{one.Slug}", EventMarks.Unstarred(one.Name)),
                    one.YearFromCreation is { } year
                        ? PageViews.AxisYear(wording, year, EncyclopediaEndpoints.LastYearBeforeChrist)
                        : ""));
            }
        }

        PageLink? person = null;
        if (detail.Entity is { } named && await Entity(named.Slug, language, cancellationToken) is { } entity)
        {
            person = new PageLink(PageAddress.RecordPath(entity.Kind, entity.Slug), entity.Shown);
        }

        var view = new PeriodView(detail.Slug, detail.Name, shown)
        {
            Kind = english ? detail.Kind : null,
            Notes = detail.Notes,
            Region = english ? detail.Region : null,
            Parent = detail.Parent is { } parent ? new PageLink($"/periods/{parent.Slug}", parent.Name) : null,
            Person = person,
            Spans = spans,
            Opens = detail.Opens is { } opens ? new PageLink($"/events/{opens.Slug}", opens.Name) : null,
            Closes = detail.Closes is { } closes ? new PageLink($"/events/{closes.Slug}", closes.Name) : null,
            Inside = inside,
            InsideTotal = total,
            Nested = [.. detail.Inside.Select(child => new PageLink($"/periods/{child.Slug}", child.Name))],
            Source = detail.Source,
        };
        return PageViews.Period(language, wording, view) with
        {
            Data = PageData.Of([(PageData.PeriodAddress(detail.Slug), PageData.Json(detail))]),
        };
    }

    /// <summary>The reckonings, in their order, which change only with the corpus.</summary>
    private Task<IList<ChronologyResponse>> Reckonings(CancellationToken cancellationToken) =>
        cache.Remember("reckonings", PageCache.Corpus, () => EncyclopediaEndpoints.Chronologies(db, cancellationToken));

    private async Task<PageContent?> Strong(PageAddress address, PageWording wording, CancellationToken cancellationToken)
    {
        var number = address.Slug!;
        var language = address.Language;
        if (await StrongEndpoints.Detail(db, number, language.Corpus, cancellationToken) is not { } entry)
        {
            return null;
        }

        var answers = new List<(string, string)> { (PageData.StrongAddress(number, language), PageData.Json(entry)) };
        if (entry.Morpheme)
        {
            return PageViews.Strong(language, wording, new StrongView(number, null, null, entry.Definition, null)) with
            {
                Data = PageData.Of(answers),
            };
        }

        // How the page's own text renders the number and where it stands, as the reader's page asks
        // for them in that text: both are carried, and both are written out for whoever runs no script.
        var view = new StrongView(
            entry.StrongNumber,
            entry.Lemma,
            entry.Transliteration,
            entry.Translation?.Definition ?? entry.Definition,
            entry.Translation?.Derivation ?? entry.Derivation,
            entry.Translation?.DetailedDefinition ?? entry.DetailedDefinition);
        if (await Chosen(language, cancellationToken) is { } chosen && await canon.Text(chosen.Slug, cancellationToken) is { } text)
        {
            var renderings = await StrongEndpoints.RenderingsOf(
                db, number, text.Id, text.Slug, PageData.StrongRenderings, cancellationToken);
            answers.Add((PageData.StrongRenderingsAddress(number, chosen.Slug), PageData.Json(renderings)));
            var map = await StrongBookEndpoints.Map(db, number, [text], cancellationToken);
            answers.Add((PageData.StrongBooksAddress(number, chosen.Slug), PageData.Json(map)));
            var books = new List<StrongBookCount>();
            foreach (var row in map.Books.Where(row => row.Occurrences > 0).OrderBy(row => row.BookOrdinal))
            {
                books.Add(new StrongBookCount(await BookName(language, wording, row.BookOrdinal, cancellationToken), row.Occurrences));
            }

            var witness = map.Edition is { } edition
                ? (await Texts(cancellationToken)).FirstOrDefault(t => t.Slug == edition)
                : null;
            view = view with
            {
                Uses = map.Occurrences > 0 ? map.Occurrences : null,
                CountedIn = witness is null ? map.Edition : TextName(wording, witness),
                RenderedIn = TextName(wording, chosen),
                Renderings = [.. renderings.Renderings.Select(r => new StrongRenderingCount(r.Text, r.Count))],
                Books = books,
            };
        }

        return PageViews.Strong(language, wording, view) with { Data = PageData.Of(answers) };
    }

    /// <summary>
    /// One page of every verse of a record or a Strong number, as the books run, each verse quoted from
    /// the text the page's language reads, under its book and the book's count.
    /// </summary>
    private async Task<PageContent?> Verses(PageAddress address, PageWording wording, CancellationToken cancellationToken)
    {
        var language = address.Language;
        string name;
        string page;
        string? section;
        int total;
        List<int> wanted;
        IReadOnlyList<EntityReferenceBookResponse> books;
        if (address.RecordKind is not null)
        {
            if (await Entity(address.Slug!, language, cancellationToken, address.RecordKind) is not { } entity)
            {
                return null;
            }

            page = PageAddress.RecordPath(entity.Kind, entity.Slug);
            if (entity.Slug != address.Slug || PageAddress.VersesPath(page) != address.English)
            {
                return PageViews.NotFound(language, wording) with
                {
                    Status = StatusCodes.Status301MovedPermanently,
                    MovedTo = PageAddress.VersesPath(page),
                };
            }

            var references = db.EntityVerses.Shown().Where(v => v.EntityId == entity.Id);
            total = await EncyclopediaEndpoints.Addresses(references).CountAsync(cancellationToken);
            wanted = await EncyclopediaEndpoints.Addresses(references).OrderBy(a => a)
                .Skip(address.Skip).Take(address.Take).ToListAsync(cancellationToken);
            books = await EncyclopediaEndpoints.ReferenceBooks(references, cancellationToken);
            name = entity.Shown;
            section = PageSections.Encyclopedia;
        }
        else
        {
            var number = address.Slug!;
            var entry = await db.StrongEntries
                .Where(e => e.StrongNumber == number)
                .Select(e => new { e.Lemma, e.Transliteration })
                .FirstOrDefaultAsync(cancellationToken);
            if (entry is null)
            {
                return null;
            }

            var list = await StrongVerseEndpoints.Page(db, canon, number, null, address.Skip, address.Take, cancellationToken);
            books = [.. list.Books ?? (await StrongVerseEndpoints.Page(db, canon, number, list.Corpus, 0, 1, cancellationToken)).Books ?? []];
            total = list.Total;
            wanted = [.. list.Items.Select(verse => Address(verse.Book.Ordinal, verse.Chapter, verse.Verse))];
            var word = string.Join(' ', new[] { entry.Lemma, entry.Transliteration }.Where(part => !string.IsNullOrEmpty(part)));
            name = word.Length > 0 ? $"{word} ({number})" : number;
            page = $"/strong/{number}";
            section = "lexicon";
        }

        if (address.Skip > 0 && wanted.Count == 0)
        {
            return null;
        }

        var said = await TextsAt(language, wanted, cancellationToken);
        var rows = new List<ListedVerse>(wanted.Count);
        foreach (var at in wanted)
        {
            var (book, chapter, verse) = (at / EncyclopediaEndpoints.BookStride,
                at % EncyclopediaEndpoints.BookStride / EncyclopediaEndpoints.ChapterStride, at % EncyclopediaEndpoints.ChapterStride);
            rows.Add(new ListedVerse(book, chapter, verse,
                wording.Reference(await BookName(language, wording, book, cancellationToken), chapter, verse),
                said.GetValueOrDefault(at)));
        }

        var counted = new List<BookCount>(books.Count);
        foreach (var book in books.OrderBy(book => book.Book.Ordinal))
        {
            counted.Add(new BookCount(book.Book.Ordinal, await BookName(language, wording, book.Book.Ordinal, cancellationToken), book.Count));
        }

        var chosen = await Chosen(language, cancellationToken);
        return PageViews.Verses(language, wording, new VersesView(
            address.English, page, name, section, total, address.Skip, address.Take, counted, rows,
            chosen is null ? null : TextName(wording, chosen))
        {
            Indexed = address.Indexed,
            Counted = address.RecordKind is not null,
        });
    }

    private static int Address(int book, int chapter, int verse) =>
        (book * EncyclopediaEndpoints.BookStride) + (chapter * EncyclopediaEndpoints.ChapterStride) + verse;

    /// <summary>
    /// Verses by their frame addresses as plain text, each from the first text of the page's language
    /// that holds its book.
    /// </summary>
    private async Task<Dictionary<int, string>> TextsAt(
        SiteLanguage language, IReadOnlyList<int> addresses, CancellationToken cancellationToken)
    {
        var said = new Dictionary<int, string>();
        var texts = await Texts(cancellationToken);
        var chosen = await Chosen(language, cancellationToken);
        foreach (var inBook in addresses.GroupBy(at => at / EncyclopediaEndpoints.BookStride))
        {
            foreach (var text in ReaderTexts.ForBook(texts, chosen, inBook.Key))
            {
                var missing = inBook.Where(at => !said.ContainsKey(at)).ToHashSet();
                if (missing.Count == 0)
                {
                    break;
                }

                var chapters = missing.Select(at => at % EncyclopediaEndpoints.BookStride / EncyclopediaEndpoints.ChapterStride)
                    .Distinct().ToList();
                var placed = await db.VerseReferences
                    .Where(r => r.IsPrimary && r.CanonicalBook == inBook.Key && chapters.Contains(r.CanonicalChapter)
                                && r.Verse!.TextId == text.Id)
                    .Select(r => new { r.CanonicalChapter, r.CanonicalVerse, r.VerseId })
                    .ToListAsync(cancellationToken);
                var verses = placed
                    .Select(p => (At: Address(inBook.Key, p.CanonicalChapter, p.CanonicalVerse), p.VerseId))
                    .Where(p => missing.Contains(p.At))
                    .ToList();
                var words = await Said([.. verses.Select(v => v.VerseId)], cancellationToken);
                foreach (var (at, verseId) in verses)
                {
                    if (words.TryGetValue(verseId, out var plain))
                    {
                        said.TryAdd(at, plain);
                    }
                }
            }
        }

        return said;
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
            opening,
            section: "texts",
            about: (link, _) => StructuredData.Node("Book",
                ("name", name),
                ("inLanguage", StructuredData.LanguageTag(text.Language)),
                ("url", link($"/texts/{text.Slug}"))));
    }

    /// <summary>
    /// A record by any address it has had, with the name a page in this language gives it; where a
    /// kind is given, also by another of the names a record of that kind holds.
    /// </summary>
    private async Task<ShownEntity?> Entity(
        string slug, SiteLanguage language, CancellationToken cancellationToken, EntityKind? kind = null)
    {
        var current = await MergedAddresses.Current(db, slug, cancellationToken);
        var entity = await Shown(current);
        if (entity is null && kind is { } named && await RecordAliases.Find(db, slug, named, cancellationToken) is { } alias)
        {
            entity = await Shown(alias);
        }

        if (entity is null)
        {
            return null;
        }

        var renamed = (await RenamedHeadings.Of(db, [entity.Id], language.Corpus, cancellationToken)).GetValueOrDefault(entity.Id);
        var local = renamed is null
            ? (await EntityNames.Of(db, [entity.Id], language.Corpus, cancellationToken)).GetValueOrDefault(entity.Id)
            : renamed.LocalName;
        var name = renamed?.Name ?? entity.Name;
        return new ShownEntity(entity.Id, entity.Slug, entity.Kind, string.IsNullOrEmpty(local) ? name : local, entity.Distinguisher);

        Task<StoredEntity?> Shown(string address) => db.Entities
            .Where(e => e.Slug == address)
            .Select(e => new StoredEntity(e.Id, e.Slug, e.Kind, e.Name, e.Distinguisher))
            .FirstOrDefaultAsync(cancellationToken);
    }

    private sealed record StoredEntity(int Id, string Slug, EntityKind Kind, string Name, string? Distinguisher);

    private async Task<string?> Picture(string slug, string language, CancellationToken cancellationToken) =>
        (await ImageEndpoints.Leading(db, [slug], cancellationToken, settings.Is(SiteSettings.GeneratedImages), places, language))
        .GetValueOrDefault(slug)?.Url;

    /// <summary>
    /// The texts a chapter's address names, in its order, as the reader takes them: each that the corpus
    /// holds under that name or another it answers to, once; the rest left out, as the reader leaves them.
    /// </summary>
    private async Task<IReadOnlyList<ReaderText>> NamedTexts(PageAddress address, CancellationToken cancellationToken)
    {
        if (address.Texts.Count == 0)
        {
            return [];
        }

        var texts = await Texts(cancellationToken);
        var named = new List<ReaderText>();
        foreach (var slug in address.Texts)
        {
            if (await canon.Text(slug, cancellationToken) is { } entry
                && texts.FirstOrDefault(text => text.Id == entry.Id) is { } text && !named.Contains(text))
            {
                named.Add(text);
            }
        }

        return named;
    }

    /// <summary>
    /// The chapter in each of several named texts, from their stored panes, read in one statement; null where
    /// fewer than two are named, more than the reader reads side by side at once, or any of them has no pane
    /// for the chapter, and the page shows one text as it would without them.
    /// </summary>
    private async Task<IReadOnlyList<Side>?> SideBySide(
        PageAddress address, IReadOnlyList<ReaderText> named, PageWording wording, CancellationToken cancellationToken)
    {
        if (named.Count < 2 || named.Count > ParallelEndpoints.MostCorporaAtOnce)
        {
            return null;
        }

        var ids = named.Select(text => text.Id).ToList();
        var rows = await db.Panes.AsNoTracking()
            .Where(p => ids.Contains(p.TextId) && p.CanonicalBook == address.Book && p.CanonicalChapter == address.Chapter)
            .Select(p => new { p.TextId, p.Body })
            .ToListAsync(cancellationToken);
        if (rows.Count != named.Count)
        {
            return null;
        }

        var sides = new List<Side>(named.Count);
        foreach (var text in named)
        {
            var pane = Panes.Unpack(rows.First(row => row.TextId == text.Id).Body);
            var cells = ReadingColumn.CellsOf(pane, address.Chapter);
            var face = text.Kind == TextKind.Translation ? "text-ltr" : text.RightToLeft ? "text-hebrew" : "text-greek";
            sides.Add(new Side(
                text,
                pane,
                new ReadingColumn(TextName(wording, text), StructuredData.LanguageTag(text.Language), text.RightToLeft, face, cells),
                [.. cells.Where(cell => cell.Value.Text.Length > 0).OrderBy(cell => cell.Key).Select(cell => (cell.Key, cell.Value.Text))]));
        }

        return sides;
    }

    /// <summary>Every named text's pane, as the reader's script asks for each, so none is downloaded again.</summary>
    private static byte[]? Carried(PageAddress address, IReadOnlyList<Side> sides) =>
        PageData.Of(sides.Select(side =>
            (PageData.PaneAddress(side.Text.Slug, BookReferences.Slug(address.Book), address.Chapter), PageData.Written(side.Pane))));

    /// <summary>One named text's side of a chapter: its pane as stored, the column it draws, and its verses as plain text.</summary>
    private sealed record Side(ReaderText Text, byte[] Pane, ReadingColumn Column, IReadOnlyList<(int Number, string Text)> Verses);

    private static string TextName(PageWording wording, ReaderText text) =>
        wording.Map("textName").GetValueOrDefault(text.Slug) ?? text.Name;

    /// <summary>
    /// A chapter's verses in one text, numbered as the shared frame numbers them — the numbers the
    /// addresses use — each as plain text. A verse the edition prints no words for is left out. Read
    /// from the chapter's stored pane where it says exactly what the word table says (<see cref="PaneText"/>),
    /// otherwise from the word table. Kept a while, because a crawler asks for a chapter's verses one
    /// after another.
    /// </summary>
    private async Task<IReadOnlyList<(int Number, string Text)>> ChapterVerses(
        int textId, int book, int chapter, CancellationToken cancellationToken) =>
        (await ChapterPane(textId, book, chapter, cancellationToken))?.Plain
        ?? await WordTableVerses(textId, book, chapter, cancellationToken);

    /// <summary>The same from the word table, where the pane cannot say it.</summary>
    private Task<IReadOnlyList<(int Number, string Text)>> WordTableVerses(
        int textId, int book, int chapter, CancellationToken cancellationToken) =>
        cache.Remember($"chapter:{textId}:{book}:{chapter}", PageCache.Page,
            () => ReadChapter(textId, book, chapter, cancellationToken),
            verses => verses.Sum(verse => (long)verse.Text.Length) * sizeof(char));

    /// <summary>
    /// A chapter of one text from its stored pane, word by word; null where the corpus keeps no pane
    /// for it, or where the pane cannot say exactly what the word table says.
    /// </summary>
    private async Task<PaneText?> ChapterPane(int textId, int book, int chapter, CancellationToken cancellationToken) =>
        (await cache.Remember($"pane:{textId}:{book}:{chapter}", PageCache.Page,
            async () => new KeptPane(await ReadPane(textId, book, chapter, cancellationToken) is { } json ? Taken(book, chapter, json) : null),
            kept => kept.Text?.Bytes ?? 0)).Text;

    /// <summary>A chapter's pane in one text as <c>/v1/panes</c> sends it uncompressed; null where the corpus keeps none.</summary>
    private async Task<byte[]?> ReadPane(int textId, int book, int chapter, CancellationToken cancellationToken)
    {
        var body = await db.Panes.AsNoTracking()
            .Where(p => p.TextId == textId && p.CanonicalBook == book && p.CanonicalChapter == chapter)
            .Select(p => p.Body)
            .FirstOrDefaultAsync(cancellationToken);
        return body is null ? null : Panes.Unpack(body);
    }

    /// <summary>The pane's text where it is the word table's to the letter: one verse of the text at each address, and no passage read under another name.</summary>
    private static PaneText? Taken(int book, int chapter, byte[] json)
    {
        if (TwinPassages.NamedElsewhere(book, chapter).Any())
        {
            return null;
        }

        var text = PaneText.Parse(json);
        return text.Single ? text : null;
    }

    /// <summary>A pane as kept: its text, or that it has none a page can take.</summary>
    private sealed record KeptPane(PaneText? Text);

    private async Task<IReadOnlyList<(int Number, string Text)>> ReadChapter(
        int textId, int book, int chapter, CancellationToken cancellationToken)
    {
        var placed = await db.VerseReferences
            .Where(r => r.IsPrimary && r.CanonicalBook == book && r.CanonicalChapter == chapter && r.Verse!.TextId == textId)
            .Select(r => new
            {
                r.CanonicalVerse,
                r.VerseId,
                Place = new ChapterReading.EditionPlace(
                    r.Verse!.Book!.Position, r.Verse.ChapterNumber, r.Verse.Sequence, r.Verse.Number, r.Verse.Label),
            })
            .ToListAsync(cancellationToken);
        if (placed.Count == 0)
        {
            return [];
        }

        var said = await Said(placed.Select(p => p.VerseId).ToList(), cancellationToken);
        return
        [
            .. placed
                .OrderBy(p => p.CanonicalVerse).ThenBy(p => p.Place)
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

    /// <summary>The originals a verse's own words are quoted from, the first that holds the book, as the reader orders them.</summary>
    private static readonly string[] Originals = ["BHSA", "NESTLE1904", "SWETE", "GRCBRENT"];

    private static readonly char[] StrongSeparators = [' ', ',', ';', '/', '+', '|', '&'];

    /// <summary>
    /// The verse in the original, word by word, each with its Strong numbers and, on an English page,
    /// the gloss the edition gives it; null where no original holds the verse.
    /// </summary>
    private async Task<OriginalVerseView?> OriginalVerse(PageAddress address, PageWording wording, CancellationToken cancellationToken)
    {
        var texts = await Texts(cancellationToken);
        var original = Originals
            .Select(slug => texts.FirstOrDefault(t => t.Slug == slug))
            .FirstOrDefault(t => t is not null && t.Covers(address.Book));
        if (original is null)
        {
            return null;
        }

        IReadOnlyList<PaneWord> words;
        if (await ChapterPane(original.Id, address.Book, address.Chapter, cancellationToken) is { } pane)
        {
            words = pane.At(address.Verse)?.Words ?? [];
        }
        else
        {
            var verseIds = await db.VerseReferences
                .Where(r => r.IsPrimary && r.CanonicalBook == address.Book && r.CanonicalChapter == address.Chapter
                            && r.CanonicalVerse == address.Verse && r.Verse!.TextId == original.Id)
                .Select(r => r.VerseId)
                .ToListAsync(cancellationToken);
            words = verseIds.Count == 0
                ? []
                : (await db.Words
                    .Where(w => verseIds.Contains(w.VerseId) && !w.Elided)
                    .OrderBy(w => w.VerseId).ThenBy(w => w.Position)
                    .Select(w => new { w.Surface, w.Trailer, w.StrongNumber, w.Gloss })
                    .ToListAsync(cancellationToken))
                .Select(w => new PaneWord(w.Surface, w.Trailer, w.StrongNumber, w.Gloss, null))
                .ToList();
        }

        if (words.Count == 0)
        {
            return null;
        }

        // A Hebrew word is stored as its parts (the conjunction, the article, a preposition, the word),
        // the parts joined with no space between; they are shown as the one word the verse prints.
        var english = address.Language.IsEnglish;
        var shown = new List<OriginalWordView>();
        var surface = new System.Text.StringBuilder();
        var numbers = new List<string>();
        var glosses = new List<string>();
        for (var i = 0; i < words.Count; i++)
        {
            var word = words[i];
            surface.Append(word.Surface);
            numbers.AddRange((word.Strong ?? "").Split(StrongSeparators, StringSplitOptions.RemoveEmptyEntries)
                .Select(StrongNumbers.Normalize).OfType<string>()
                .Where(n => StrongMorphemeCodes.GetDescription(n) is null));
            if (english && word.Gloss?.Trim() is { Length: > 0 } gloss && gloss.Any(char.IsLetterOrDigit))
            {
                glosses.Add(gloss);
            }

            if (word.Trailer.Length == 0 && i < words.Count - 1)
            {
                continue;
            }

            if (PageHtml.Plain(surface.ToString()) is { Length: > 0 } printed)
            {
                shown.Add(new OriginalWordView(printed, [.. numbers.Distinct()], glosses.Count > 0 ? string.Join(" ", glosses) : null));
            }

            surface.Clear();
            numbers.Clear();
            glosses.Clear();
        }

        return new OriginalVerseView(
            TextName(wording, original),
            StructuredData.LanguageTag(original.Language),
            PageHtml.Plain(string.Concat(words.Select(w => w.Surface + w.Trailer))),
            shown);
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

    private sealed record ShownEntity(int Id, string Slug, EntityKind Kind, string Shown, string? Distinguisher);
}
