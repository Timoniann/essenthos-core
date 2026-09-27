using Essenthos.Core.Database;
using Microsoft.EntityFrameworkCore;

namespace Essenthos.Core.Corpus;

/// <param name="Books">
/// The canonical books this text actually holds, in order.
///
/// Not a range, because a range cannot describe a set with a hole in it and one of these texts has
/// one: Brenton's Septuagint holds books 1-39 and 67-81 and nothing between, so *first 1, last 81*
/// reads as covering the whole New Testament. The reader believed it, opened John against the
/// Septuagint, and printed "not present in lxx-brenton" against every verse of the chapter.
/// </param>
internal sealed record TextEntry(int Id, string Slug, IReadOnlyList<int> Books, bool HasWordMapping)
{
    public int FirstBook => Books.Count > 0 ? Books[0] : 0;

    public int LastBook => Books.Count > 0 ? Books[^1] : 0;

    public bool Covers(int canonicalBook) => Books.Contains(canonicalBook);
}

/// <summary>
/// The canon and the texts, read once and kept.
///
/// Every request needs to turn a slug into a text id and a canonical book into a chapter count, and
/// neither changes while the process runs. Answering them from a query per request is a join per
/// request for an answer that is the same every time.
/// </summary>
internal interface ICanonIndex
{
    /// <summary>
    /// The text this identifier names, whether it is the text's own slug or one of the other
    /// spellings <see cref="TextAliases"/> declares. The entry answers with the canonical slug
    /// either way, which is what every response is built from.
    /// </summary>
    Task<TextEntry?> Text(string slug, CancellationToken cancellationToken);

    Task<IReadOnlyList<TextEntry>> Texts(CancellationToken cancellationToken);

    /// <summary>
    /// How many chapters a book has in the shared frame — the English scheme, so Joel is three and
    /// Malachi four, whichever text is being read. A text's own count can differ and belongs to the
    /// text, not to the canon.
    /// </summary>
    Task<int> ChapterCount(int canonicalBook, CancellationToken cancellationToken);

    /// <summary>
    /// How far into the shared frame the widest of these texts reaches in a book: the chapters a
    /// reader with exactly these texts open can step through. The Septuagint's Psalm 151 is there
    /// when the Septuagint is open and not when only the King James is.
    /// </summary>
    Task<int> ChapterCountAcross(IEnumerable<int> textIds, int canonicalBook, CancellationToken cancellationToken);

    Task<int> ChapterCountIn(int textId, int canonicalBook, CancellationToken cancellationToken);

    void Forget();
}

internal sealed class CanonIndex(IServiceScopeFactory scopes) : ICanonIndex
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private IReadOnlyList<TextEntry>? _texts;
    private IReadOnlyDictionary<(int Text, int Book), int>? _frameChapterCounts;
    private IReadOnlyDictionary<(int Text, int Book), int>? _chapterCountsByText;

    public async Task<TextEntry?> Text(string slug, CancellationToken cancellationToken) =>
        Resolve(await Texts(cancellationToken), slug);

    /// <summary>
    /// The text an identifier names, its own slug or an alias of it.
    ///
    /// A loaded text's own slug is tried first, so an alias can never shadow one: were some later
    /// text to take an identifier already declared as another's alias, the request would still
    /// reach the text that owns it. Serving the wrong text is the one failure this mechanism must
    /// not have, and ordering the two lookups is what rules it out rather than checking for it.
    /// </summary>
    public static TextEntry? Resolve(IReadOnlyList<TextEntry> texts, string identifier) =>
        Named(texts, identifier)
        ?? (TextAliases.Canonical(identifier) is { } canonical ? Named(texts, canonical) : null);

    private static TextEntry? Named(IReadOnlyList<TextEntry> texts, string slug) =>
        texts.FirstOrDefault(t => string.Equals(t.Slug, slug, StringComparison.OrdinalIgnoreCase));

    public async Task<IReadOnlyList<TextEntry>> Texts(CancellationToken cancellationToken)
    {
        await Ensure(cancellationToken);
        return _texts!;
    }

    public async Task<int> ChapterCount(int canonicalBook, CancellationToken cancellationToken)
    {
        await Ensure(cancellationToken);
        return _frameChapterCounts!
            .Where(pair => pair.Key.Book == canonicalBook)
            .Select(pair => pair.Value)
            .DefaultIfEmpty()
            .Max();
    }

    public async Task<int> ChapterCountAcross(
        IEnumerable<int> textIds, int canonicalBook, CancellationToken cancellationToken)
    {
        await Ensure(cancellationToken);
        return WidestReach(_frameChapterCounts!, textIds, canonicalBook);
    }

    public static int WidestReach(
        IReadOnlyDictionary<(int Text, int Book), int> frameChapterCounts,
        IEnumerable<int> textIds,
        int canonicalBook) =>
        textIds
            .Select(id => frameChapterCounts.GetValueOrDefault((id, canonicalBook)))
            .DefaultIfEmpty()
            .Max();

    /// <summary>The last canonical chapter each text reaches in each book, in the shared frame.</summary>
    public static async Task<Dictionary<(int Text, int Book), int>> FrameChapterCounts(
        AppDbContext db, CancellationToken cancellationToken) =>
        (await db.VerseReferences
            .GroupBy(r => new { r.Verse!.TextId, r.CanonicalBook })
            .Select(g => new { g.Key.TextId, g.Key.CanonicalBook, Chapters = g.Max(r => r.CanonicalChapter) })
            .ToListAsync(cancellationToken))
        .ToDictionary(row => (row.TextId, row.CanonicalBook), row => row.Chapters);

    /// <summary>
    /// The books a text holds, and the books the frame places some of its verses in under another
    /// name: the King James's Baruch 6 is the Letter of Jeremiah too, so a reader of the letter finds it.
    /// </summary>
    public static IReadOnlyList<int> Covered(
        int textId,
        IEnumerable<int> books,
        IReadOnlyDictionary<(int Text, int Book), int> frameChapterCounts) =>
        [.. books.Union(frameChapterCounts.Keys.Where(key => key.Text == textId).Select(key => key.Book)).Order()];

    public async Task<int> ChapterCountIn(int textId, int canonicalBook, CancellationToken cancellationToken)
    {
        await Ensure(cancellationToken);
        return _chapterCountsByText!.GetValueOrDefault((textId, canonicalBook));
    }

    /// <summary>Called when the dataset load finishes, because until then the answers are wrong.</summary>
    public void Forget()
    {
        _texts = null;
        _frameChapterCounts = null;
        _chapterCountsByText = null;
    }

    private async Task Ensure(CancellationToken cancellationToken)
    {
        if (_texts is not null)
        {
            return;
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_texts is not null)
            {
                return;
            }

            using var scope = scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            // Whether a text has any links at all is asked one text at a time, on purpose. Written
            // as a correlated subquery inside the projection below, it is evaluated once per row
            // of the join — 586 times, once for every book of every text — and each evaluation
            // scans the 4.9-million-row link table, because a filter that ORs two columns can use
            // neither index. That was 46 seconds against a 30-second command timeout: /v1/corpora
            // stopped answering the moment Swete's 51 books pushed it over. Asked here it is
            // twenty-six index probes over thirteen texts, and each one is a millisecond.
            var linked = new HashSet<int>();
            foreach (var id in await db.Texts.Select(t => t.Id).ToListAsync(cancellationToken))
            {
                if (await db.Links.AnyAsync(l => l.FromTextId == id, cancellationToken)
                    || await db.Links.AnyAsync(l => l.ToTextId == id, cancellationToken))
                {
                    linked.Add(id);
                }
            }

            var texts = (await db.Texts
                    .OrderBy(t => t.Slug)
                    .Select(t => new
                    {
                        t.Id,
                        t.Slug,
                        Books = t.Books.Select(b => b.CanonicalOrdinal).OrderBy(ordinal => ordinal).ToList(),
                    })
                    .ToListAsync(cancellationToken))
                .Select(t => new { t.Id, t.Slug, t.Books, Linked = linked.Contains(t.Id) })
                .ToList();

            _frameChapterCounts = await FrameChapterCounts(db, cancellationToken);

            _chapterCountsByText = (await db.Chapters
                    .GroupBy(c => new { c.TextId, c.Book!.CanonicalOrdinal })
                    .Select(g => new { g.Key.TextId, g.Key.CanonicalOrdinal, Chapters = g.Max(c => c.Number) })
                    .ToListAsync(cancellationToken))
                .ToDictionary(row => (row.TextId, row.CanonicalOrdinal), row => row.Chapters);

            _texts = texts
                .Select(t => new TextEntry(t.Id, t.Slug, Covered(t.Id, t.Books, _frameChapterCounts), t.Linked))
                .ToList();
        }
        finally
        {
            _gate.Release();
        }
    }
}
