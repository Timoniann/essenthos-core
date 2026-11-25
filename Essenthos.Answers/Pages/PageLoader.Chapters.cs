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

internal sealed partial class PageLoader
{
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

    private async Task<PageContent?> Chapter(PageAddress asked, PageWording wording, CancellationToken cancellationToken)
    {
        var numbered = await InFrame(asked, cancellationToken);
        var address = numbered.Address;
        var chapterCount = await canon.ChapterCount(address.Book, cancellationToken);
        if (address.Chapter < 1 || address.Chapter > chapterCount)
        {
            return null;
        }

        var language = address.Language;
        var named = await NamedTexts(address, cancellationToken);
        if (await SideBySide(address, named, wording, cancellationToken) is { } sides)
        {
            return PageViews.Chapter(language, wording, await Numbered(new ChapterView(
                address.Book,
                await BookName(language, wording, address.Book, cancellationToken),
                address.Chapter,
                chapterCount,
                sides[0].Verses,
                sides[0].Column.Name)
            {
                TextLanguage = sides[0].Column.Language,
                Columns = [.. sides.Select(side => side.Column)],
            }, numbered, wording, cancellationToken)) with
            {
                Data = PageData.Of(CarriedAnswers(address, sides).Concat(numbered.Carried)),
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
                IEnumerable<(string, string)> carried = pane is null
                    ? []
                    : [(PageData.PaneAddress(text.Slug, BookReferences.Slug(address.Book), address.Chapter), PageData.Written(pane))];
                return PageViews.Chapter(language, wording, await Numbered(new ChapterView(
                    address.Book,
                    await BookName(language, wording, address.Book, cancellationToken),
                    address.Chapter,
                    chapterCount,
                    verses,
                    TextName(wording, text)) { TextLanguage = StructuredData.LanguageTag(text.Language) },
                    numbered, wording, cancellationToken)) with
                {
                    Data = PageData.Of(carried.Concat(numbered.Carried)),
                };
            }
        }

        return null;
    }

    private async Task<PageContent?> Verse(PageAddress asked, PageWording wording, CancellationToken cancellationToken)
    {
        var numbered = await InFrame(asked, cancellationToken);
        var address = numbered.Address;
        var chapterCount = await canon.ChapterCount(address.Book, cancellationToken);
        if (address.Chapter < 1 || address.Chapter > chapterCount)
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
                AddressPath = numbered.AddressPath,
                Heading = numbered.Heading(address.Chapter),
                Asked = numbered.InTextNumbering && (asked.Chapter, asked.Verse) != (address.Chapter, address.Verse)
                    ? (asked.Chapter, asked.Verse)
                    : null,
            }) with
            {
                Data = PageData.Of(CarriedAnswers(address, sides).Concat(numbered.Carried)),
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
                AddressPath = numbered.AddressPath,
                Heading = numbered.Heading(address.Chapter),
                Asked = numbered.InTextNumbering && (asked.Chapter, asked.Verse) != (address.Chapter, address.Verse)
                    ? (asked.Chapter, asked.Verse)
                    : null,
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
            books = [.. list.Books ?? []];
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
                // Where the build keeps the run's reads, a text's places are read whole once, by the first page to quote it.
                var placed = SharedReads.Corpus.Value is not null
                    ? [.. (await SharedReads.Kept($"placed:{text.Id}", async () =>
                            (await PlacedIn(db.VerseReferences.Where(r => r.Verse!.TextId == text.Id)).ToListAsync(cancellationToken))
                            .ToLookup(p => p.CanonicalBook)))[inBook.Key]
                        .Where(p => chapters.Contains(p.CanonicalChapter))]
                    : await PlacedIn(db.VerseReferences
                        .Where(r => r.CanonicalBook == inBook.Key && chapters.Contains(r.CanonicalChapter) && r.Verse!.TextId == text.Id))
                        .ToListAsync(cancellationToken);

                // Where two verses of the text share an address, the one whose own address it is, then the first printed.
                var verses = placed
                    .Select(p => (At: Address(inBook.Key, p.CanonicalChapter, p.CanonicalVerse), p.VerseId,
                        Own: p.ChapterNumber == p.CanonicalChapter && p.Number == p.CanonicalVerse && p.Label.Length == 0,
                        p.ChapterNumber, p.Sequence, p.Number, p.Label))
                    .Where(p => missing.Contains(p.At))
                    .OrderBy(p => p.At)
                    .ThenByDescending(p => p.Own)
                    .ThenBy(p => p.ChapterNumber)
                    .ThenBy(p => p.Sequence)
                    .ThenBy(p => p.Number)
                    .ThenBy(p => p.Label, StringComparer.Ordinal)
                    .ThenBy(p => p.VerseId)
                    .ToList();
                var words = await Said([.. verses.Select(v => v.VerseId)], cancellationToken);
                foreach (var (at, verseId, _, _, _, _, _) in verses)
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
    private static IEnumerable<(string, string)> CarriedAnswers(PageAddress address, IReadOnlyList<Side> sides) =>
        sides.Select(side =>
            (PageData.PaneAddress(side.Text.Slug, BookReferences.Slug(address.Book), address.Chapter), PageData.Written(side.Pane)));

    /// <summary>
    /// An address in the frame, and how the first text it names numbers the book: an address names its chapter and
    /// verse in that text's numbering, and one that names no text in the frame's.
    /// </summary>
    /// <param name="Address">The address in the frame.</param>
    /// <param name="Addresses">The first text's numbering of the book; empty where the address names no text the corpus holds.</param>
    /// <param name="AddressPath">The chapter's address as asked, where it names the chapter in the first text's numbering.</param>
    /// <param name="Carried">The numbering as the reader's script asks for it, which the page carries.</param>
    private sealed record NumberedAddress(
        PageAddress Address,
        IReadOnlyList<NumberedAddressResponse> Addresses,
        bool InTextNumbering,
        string? AddressPath,
        IReadOnlyList<(string, string)> Carried)
    {
        /// <summary>The chapter as the first text numbers it, where that is not the frame's number.</summary>
        public string? Heading(int frameChapter)
        {
            if (!InTextNumbering)
            {
                return null;
            }

            var label = ChapterNumbering.Label(ChapterNumbering.OwnChapters(Addresses, frameChapter));
            return label == frameChapter.ToString(System.Globalization.CultureInfo.InvariantCulture) ? null : label;
        }
    }

    private async Task<NumberedAddress> InFrame(PageAddress address, CancellationToken cancellationToken)
    {
        if (address.Texts.Count == 0 || await canon.Text(address.Texts[0], cancellationToken) is not { } first)
        {
            return new NumberedAddress(address, [], false, null, []);
        }

        var addresses = await TextNumbering.Of(db, first.Id, address.Book, cancellationToken);
        var (chapter, verse) = ChapterNumbering.InFrame(addresses, address.Chapter, address.Verse > 0 ? address.Verse : null);
        var framed = address with { Chapter = chapter, Verse = address.Verse > 0 ? verse ?? address.Verse : address.Verse };
        var book = BookReferences.Slug(address.Book);
        var answer = new TextNumberingResponse(first.Slug, ReadEndpoints.BookReference(address.Book), addresses);
        return new NumberedAddress(
            framed,
            addresses,
            true,
            chapter == address.Chapter ? null : PageAddress.ChapterPath(address.Book, address.Chapter),
            [(PageData.NumberingAddress(address.Texts[0], book), PageData.Json(answer))]);
    }

    /// <summary>
    /// The chapter headed as its first text numbers it, and named in the other numbering tradition where that numbers
    /// it otherwise: a psalm's title carries both its numbers, and the line under its heading leads to the chapter the
    /// other tradition gives this number, as the reader's own line does.
    /// </summary>
    private async Task<ChapterView> Numbered(
        ChapterView view, NumberedAddress numbered, PageWording t, CancellationToken cancellationToken)
    {
        var slug = BookReferences.Slug(view.Book);
        var lead = numbered.InTextNumbering
            ? ChapterNumbering.OwnChapters(numbered.Addresses, view.Chapter)
            : [view.Chapter];
        var heading = ChapterNumbering.Label(lead);
        var renumbered = heading != view.Chapter.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var reference = renumbered ? null : await TraditionNumbering(slug, view.Book, cancellationToken);
        var other = ChapterNumbering.OtherOf(slug, view.Chapter, view.ChapterCount, lead, reference);
        var psalms = slug == "psalms";
        string Named(string label) => psalms
            ? t.Text(label.Contains('–') ? "reading.otherNumbering.psalms" : "reading.otherNumbering.psalm", ("number", label))
            : $"{view.BookName} {label}";

        OtherNumberingLine? line = null;
        if (other is not null)
        {
            var sentence = other.Label is { } said
                ? t.Text($"reading.otherNumbering.this_{other.Tradition}", ("reference", Named(said)))
                : null;
            (string, string, string)? sought = null;
            if (other.Sought is { } wanted)
            {
                var (chapter, verse) = ChapterNumbering.Own(numbered.Addresses, wanted.FrameChapter, wanted.FrameVerse);
                var number = verse is { } v
                    ? t.Text("chapterVerse", ("chapter", chapter.ToString()), ("verse", v.ToString()))
                    : chapter.ToString(System.Globalization.CultureInfo.InvariantCulture);
                var path = PageAddress.ChapterPath(view.Book, chapter) + (verse is { } at ? $"/{at}" : "")
                           + (numbered.InTextNumbering ? $"?c={string.Join(',', numbered.Address.Texts.Select(Uri.EscapeDataString))}" : "");
                sought = (
                    t.Text($"reading.otherNumbering.sought_{other.Tradition}", ("reference", Named(wanted.Chapter.ToString()))),
                    psalms ? t.Text("reading.otherNumbering.psalmTitle", ("number", number)) : $"{view.BookName} {number}",
                    path);
            }

            line = new OtherNumberingLine(sentence, sought);
        }

        return view with
        {
            AddressPath = numbered.AddressPath,
            Heading = renumbered ? heading : null,
            OtherNumber = psalms ? other?.Label : null,
            Other = line,
        };
    }

    /// <summary>How the text the book's other numbering tradition is read from numbers it; null where the corpus holds none.</summary>
    private async Task<IReadOnlyList<NumberedAddressResponse>?> TraditionNumbering(
        string slug, int book, CancellationToken cancellationToken)
    {
        foreach (var name in NumberingTraditions.Texts(slug))
        {
            if (await canon.Text(name, cancellationToken) is { } entry)
            {
                return await TextNumbering.Of(db, entry.Id, book, cancellationToken);
            }
        }

        return null;
    }

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

    /// <summary>The primary places of these references, as a quoted verse is chosen among them.</summary>
    private static IQueryable<PlacedVerse> PlacedIn(IQueryable<Database.Entities.VerseReference> references) =>
        references
            .Where(r => r.IsPrimary)
            .Select(r => new PlacedVerse(
                r.CanonicalBook,
                r.CanonicalChapter,
                r.CanonicalVerse,
                r.VerseId,
                r.Verse!.ChapterNumber,
                r.Verse.Number,
                r.Verse.Label,
                r.Verse.Sequence));

    private async Task<Dictionary<int, string>> Said(List<int> verseIds, CancellationToken cancellationToken)
    {
        if (SharedReads.Corpus.Value is not { } kept)
        {
            return await Read(verseIds);
        }

        // Each verse's text is its own words alone, so a verse read for one page is the same for the next.
        var unread = verseIds.Where(id => !kept.Quoted.ContainsKey(id)).ToList();
        if (unread.Count > 0)
        {
            var read = await Read(unread);
            foreach (var id in unread)
            {
                kept.Quoted.TryAdd(id, read.GetValueOrDefault(id));
            }
        }

        return verseIds.Distinct()
            .Where(id => kept.Quoted[id] is not null)
            .ToDictionary(id => id, id => kept.Quoted[id]!);

        async Task<Dictionary<int, string>> Read(List<int> ids)
        {
            var words = await db.Words
                .Where(w => ids.Contains(w.VerseId) && !w.Elided)
                .OrderBy(w => w.VerseId).ThenBy(w => w.Position)
                .Select(w => new { w.VerseId, w.Surface, w.Trailer })
                .ToListAsync(cancellationToken);
            return words
                .GroupBy(w => w.VerseId)
                .Select(verse => (verse.Key, Text: PageHtml.Plain(string.Concat(verse.Select(w => w.Surface + w.Trailer)))))
                .Where(verse => verse.Text.Length > 0)
                .ToDictionary(verse => verse.Key, verse => verse.Text);
        }
    }
}
