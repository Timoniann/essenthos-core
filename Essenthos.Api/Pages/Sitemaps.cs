using System.Text;
using System.Xml;
using Essenthos.Core.Corpus;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities.Enums;
using Microsoft.EntityFrameworkCore;

namespace Essenthos.Core.Pages;

/// <summary>
/// The site's sitemaps: an index, and beneath it one list per kind of page — the site's own pages,
/// the chapters, each book's verses, each kind of record, the events, the periods and the lexicon.
///
/// Every address is listed once per language with all four languages as its alternates, which is
/// how a search engine learns that <c>/uk/read/mark/3</c> is the Ukrainian of <c>/read/mark/3</c>.
/// No list comes near the protocol's 50,000 addresses: the largest, Psalms' verses or the Hebrew
/// lexicon, is about 35,000 once multiplied by four.
///
/// The addresses are what is kept, not the XML: written out, the verses alone are tens of megabytes,
/// and writing them again for a crawler that asks once a day costs less than holding them.
/// </summary>
internal sealed class Sitemaps(AppDbContext db, ICanonIndex canon, PageCache cache)
{
    private const string Namespace = "http://www.sitemaps.org/schemas/sitemap/0.9";
    private const string Alternates = "http://www.w3.org/1999/xhtml";
    private const string Pages = "pages";
    private const string Chapters = "chapters";
    private const string Verses = "verses-";
    private const string Records = "records-";
    private const string Events = "events";
    private const string Periods = "periods";
    private const string Lexicon = "lexicon-";
    private const string Extension = ".xml";

    private static readonly IReadOnlyDictionary<string, EntityKind> RecordKinds = new Dictionary<string, EntityKind>
    {
        ["people"] = EntityKind.Person,
        ["places"] = EntityKind.Place,
        ["peoples"] = EntityKind.People,
        ["terms"] = EntityKind.Term,
        ["titles"] = EntityKind.Title,
        ["objects"] = EntityKind.Object,
        ["observances"] = EntityKind.Observance,
    };

    private static readonly IReadOnlyDictionary<string, char> Lexicons = new Dictionary<string, char>
    {
        ["hebrew"] = 'H',
        ["greek"] = 'G',
    };

    private static readonly XmlWriterSettings Writing = new()
    {
        Async = true,
        Encoding = new UTF8Encoding(false),
        Indent = false,
    };

    /// <summary>The names of the lists the index points to, each without its extension.</summary>
    public async Task<IReadOnlyList<string>> Names(CancellationToken cancellationToken)
    {
        var names = new List<string> { Pages, Chapters };
        foreach (var book in await BooksWithText(cancellationToken))
        {
            names.Add(Verses + BookReferences.Slug(book));
        }

        names.AddRange(RecordKinds.Keys.Select(kind => Records + kind));
        names.AddRange([Events, Periods]);
        names.AddRange(Lexicons.Keys.Select(lexicon => Lexicon + lexicon));
        return names;
    }

    public async Task WriteIndex(Stream output, string origin, CancellationToken cancellationToken)
    {
        await using var xml = XmlWriter.Create(output, Writing);
        await xml.WriteStartDocumentAsync();
        await xml.WriteStartElementAsync(null, "sitemapindex", Namespace);
        foreach (var name in await Names(cancellationToken))
        {
            await xml.WriteStartElementAsync(null, "sitemap", Namespace);
            await xml.WriteElementStringAsync(null, "loc", Namespace, $"{origin}/sitemaps/{name}{Extension}");
            await xml.WriteEndElementAsync();
        }

        await xml.WriteEndElementAsync();
        await xml.WriteEndDocumentAsync();
    }

    /// <summary>The English addresses one list holds, or null where there is no list by that name.</summary>
    public async Task<IReadOnlyList<string>?> Addresses(string file, CancellationToken cancellationToken)
    {
        if (!file.EndsWith(Extension, StringComparison.Ordinal))
        {
            return null;
        }

        var name = file[..^Extension.Length];
        if (!(await Names(cancellationToken)).Contains(name))
        {
            return null;
        }

        return await cache.Remember($"sitemap:{name}", PageCache.Corpus, () => Listed(name, cancellationToken),
            addresses => addresses.Sum(address => (long)address.Length) * sizeof(char));
    }

    public static async Task Write(Stream output, string origin, IReadOnlyList<string> addresses)
    {
        await using var xml = XmlWriter.Create(output, Writing);
        await xml.WriteStartDocumentAsync();
        await xml.WriteStartElementAsync(null, "urlset", Namespace);
        await xml.WriteAttributeStringAsync("xmlns", "xhtml", null, Alternates);
        foreach (var address in addresses)
        {
            foreach (var language in SiteLanguage.All)
            {
                await xml.WriteStartElementAsync(null, "url", Namespace);
                await xml.WriteElementStringAsync(null, "loc", Namespace, origin + language.Path(address));
                foreach (var (code, href) in PageHtml.Alternates(origin, address))
                {
                    await xml.WriteStartElementAsync("xhtml", "link", Alternates);
                    await xml.WriteAttributeStringAsync(null, "rel", null, "alternate");
                    await xml.WriteAttributeStringAsync(null, "hreflang", null, code);
                    await xml.WriteAttributeStringAsync(null, "href", null, href);
                    await xml.WriteEndElementAsync();
                }

                await xml.WriteEndElementAsync();
            }
        }

        await xml.WriteEndElementAsync();
        await xml.WriteEndDocumentAsync();
    }

    private async Task<IReadOnlyList<string>> Listed(string name, CancellationToken cancellationToken)
    {
        if (name == Pages)
        {
            var texts = await db.Texts.OrderBy(t => t.Slug).Select(t => t.Slug).ToListAsync(cancellationToken);
            return
            [
                "/",
                .. PageSections.All.Where(section => section.Listed).Select(section => section.Path),
                .. texts.Select(slug => $"/texts/{slug}"),
            ];
        }

        if (name == Chapters)
        {
            var chapters = new List<string>();
            foreach (var book in await BooksWithText(cancellationToken))
            {
                var count = await canon.ChapterCount(book, cancellationToken);
                chapters.AddRange(Enumerable.Range(1, count).Select(chapter => PageAddress.ChapterPath(book, chapter)));
            }

            return chapters;
        }

        if (name.StartsWith(Verses, StringComparison.Ordinal))
        {
            var book = BookReferences.ResolveOrdinal(name[Verses.Length..])!.Value;
            var verses = await db.VerseReferences
                .Where(r => r.CanonicalBook == book)
                .Select(r => new { r.CanonicalChapter, r.CanonicalVerse })
                .Distinct()
                .ToListAsync(cancellationToken);
            return
            [
                .. verses
                    .OrderBy(v => v.CanonicalChapter).ThenBy(v => v.CanonicalVerse)
                    .Select(v => PageAddress.VersePath(book, v.CanonicalChapter, v.CanonicalVerse)),
            ];
        }

        if (name.StartsWith(Records, StringComparison.Ordinal))
        {
            var kind = RecordKinds[name[Records.Length..]];
            var slugs = await db.Entities.Where(e => e.Kind == kind).OrderBy(e => e.Slug).Select(e => e.Slug)
                .ToListAsync(cancellationToken);
            return [.. slugs.Select(slug => PageAddress.RecordPath(kind, slug))];
        }

        if (name == Events)
        {
            var slugs = await db.Events.OrderBy(e => e.Slug).Select(e => e.Slug).ToListAsync(cancellationToken);
            return [.. slugs.Select(slug => $"/events/{slug}")];
        }

        if (name == Periods)
        {
            var slugs = await db.Periods.OrderBy(p => p.Slug).Select(p => p.Slug).ToListAsync(cancellationToken);
            return [.. slugs.Select(slug => $"/periods/{slug}")];
        }

        var letter = Lexicons[name[Lexicon.Length..]].ToString();
        var numbers = await db.StrongEntries
            .Where(e => e.StrongNumber.StartsWith(letter))
            .Select(e => e.StrongNumber)
            .ToListAsync(cancellationToken);
        return
        [
            .. numbers
                .OrderBy(number => int.TryParse(number.AsSpan(1).TrimEnd("abcdefghijklmnopqrstuvwxyz"), out var n) ? n : int.MaxValue)
                .ThenBy(number => number, StringComparer.Ordinal)
                .Select(number => $"/strong/{number}"),
        ];
    }

    private async Task<IReadOnlyList<int>> BooksWithText(CancellationToken cancellationToken)
    {
        var books = new List<int>();
        for (var ordinal = 1; ordinal <= BookReferences.LastOrdinal; ordinal++)
        {
            if (await canon.ChapterCount(ordinal, cancellationToken) > 0)
            {
                books.Add(ordinal);
            }
        }

        return books;
    }
}
