using Essenthos.Core.Database;
using Microsoft.EntityFrameworkCore;

namespace Essenthos.Core.Endpoints;

internal sealed record TextEntry(int Id, string Slug, int FirstBook, int LastBook, bool HasWordMapping);

/// <summary>
/// The canon and the texts, read once and kept.
///
/// Every request needs to turn a slug into a text id and a canonical book into a chapter count, and
/// neither changes while the process runs. Answering them from a query per request is a join per
/// request for an answer that is the same every time.
/// </summary>
internal interface ICanonIndex
{
    Task<TextEntry?> Text(string slug, CancellationToken cancellationToken);

    Task<IReadOnlyList<TextEntry>> Texts(CancellationToken cancellationToken);

    /// <summary>
    /// How many chapters a book has in the shared frame — the English scheme, so Joel is three and
    /// Malachi four, whichever text is being read. A text's own count can differ and belongs to the
    /// text, not to the canon.
    /// </summary>
    Task<int> ChapterCount(int canonicalBook, CancellationToken cancellationToken);

    Task<int> ChapterCountIn(int textId, int canonicalBook, CancellationToken cancellationToken);

    void Forget();
}

internal sealed class CanonIndex(IServiceScopeFactory scopes) : ICanonIndex
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private IReadOnlyList<TextEntry>? _texts;
    private IReadOnlyDictionary<int, int>? _chapterCounts;
    private IReadOnlyDictionary<(int Text, int Book), int>? _chapterCountsByText;

    public async Task<TextEntry?> Text(string slug, CancellationToken cancellationToken)
    {
        var texts = await Texts(cancellationToken);
        return texts.FirstOrDefault(t => string.Equals(t.Slug, slug, StringComparison.OrdinalIgnoreCase));
    }

    public async Task<IReadOnlyList<TextEntry>> Texts(CancellationToken cancellationToken)
    {
        await Ensure(cancellationToken);
        return _texts!;
    }

    public async Task<int> ChapterCount(int canonicalBook, CancellationToken cancellationToken)
    {
        await Ensure(cancellationToken);
        return _chapterCounts!.GetValueOrDefault(canonicalBook);
    }

    public async Task<int> ChapterCountIn(int textId, int canonicalBook, CancellationToken cancellationToken)
    {
        await Ensure(cancellationToken);
        return _chapterCountsByText!.GetValueOrDefault((textId, canonicalBook));
    }

    /// <summary>Called when the dataset load finishes, because until then the answers are wrong.</summary>
    public void Forget()
    {
        _texts = null;
        _chapterCounts = null;
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

            var texts = await db.Texts
                .OrderBy(t => t.Slug)
                .Select(t => new
                {
                    t.Id,
                    t.Slug,
                    First = t.Books.Min(b => (int?)b.CanonicalOrdinal) ?? 0,
                    Last = t.Books.Max(b => (int?)b.CanonicalOrdinal) ?? 0,
                    Linked = db.Links.Any(l => l.FromTextId == t.Id || l.ToTextId == t.Id),
                })
                .ToListAsync(cancellationToken);

            _chapterCounts = (await db.VerseReferences
                    .GroupBy(r => r.CanonicalBook)
                    .Select(g => new { Book = g.Key, Chapters = g.Max(r => r.CanonicalChapter) })
                    .ToListAsync(cancellationToken))
                .ToDictionary(row => row.Book, row => row.Chapters);

            _chapterCountsByText = (await db.Chapters
                    .GroupBy(c => new { c.TextId, c.Book!.CanonicalOrdinal })
                    .Select(g => new { g.Key.TextId, g.Key.CanonicalOrdinal, Chapters = g.Max(c => c.Number) })
                    .ToListAsync(cancellationToken))
                .ToDictionary(row => (row.TextId, row.CanonicalOrdinal), row => row.Chapters);

            _texts = texts
                .Select(t => new TextEntry(t.Id, t.Slug, t.First, t.Last, t.Linked))
                .ToList();
        }
        finally
        {
            _gate.Release();
        }
    }
}
