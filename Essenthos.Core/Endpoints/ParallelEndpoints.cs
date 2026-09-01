using Essenthos.Core.Database;
using Microsoft.EntityFrameworkCore;

namespace Essenthos.Core.Endpoints;

/// <param name="Alignment">
/// How this pane's verse was paired with the row. Every pairing now goes through the shared frame,
/// so it is always <c>original-verse</c> where there is a verse at all — the contract's name for
/// "a recorded mapping said so" rather than "the numbers matched". Pairing by number is what showed
/// two different passages in two panes, and it is no longer possible here.
/// </param>
/// <param name="Reference">
/// What this text itself calls the verse, which can differ from the row: canonical Joel 3:1 is
/// Joel 4:1 in BHSA, and a reader comparing them should see both numbers.
/// </param>
internal record ParallelCellResponse(
    IList<TextWordResponse> Words,
    string Alignment,
    VerseRefResponse? Reference);

internal record ParallelVerseResponse(int Number, Dictionary<string, ParallelCellResponse?> Texts);

internal record ParallelTextResponse(
    BookRefResponse Book,
    int Chapter,
    int ChapterCount,
    string? ReferenceCorpus,
    IList<CorpusResponse> Corpora,
    IList<ParallelVerseResponse> Verses);

internal static class ParallelEndpoints
{
    private const string PairedThroughTheFrame = "original-verse";

    /// <summary>The contract's separator for the corpus list, and the cap DOC-0002 sets.</summary>
    private const char CorpusSeparator = ',';

    private const int MostCorporaAtOnce = 6;

    public static void MapParallel(this IEndpointRouteBuilder routes)
    {
        routes.MapGet("/parallel/{book}/{chapter:int}", async (
            string book,
            int chapter,
            string? corpora,
            AppDbContext db,
            ICanonIndex canon,
            CancellationToken cancellationToken) =>
        {
            var ordinal = BookReferences.ResolveOrdinal(book);
            if (ordinal is null)
            {
                return ApiResults.NotFound(BookReferences.FormatHint(book));
            }

            var requested = await Requested(canon, corpora, cancellationToken);
            if (requested.Count == 0)
            {
                return ApiResults.Malformed(
                    "None of the requested texts exist. Ask /v1/corpora for the ones this corpus holds, and name " +
                    $"them in the corpora query as a {CorpusSeparator}-separated list.");
            }

            if (requested.Count > MostCorporaAtOnce)
            {
                return ApiResults.Malformed(
                    $"At most {MostCorporaAtOnce} texts can be read side by side; {requested.Count} were asked for.");
            }

            var chapterCount = await canon.ChapterCount(ordinal.Value, cancellationToken);
            if (chapter < 1 || chapter > chapterCount)
            {
                return ApiResults.NotFound(
                    $"{BookReferences.Name(ordinal.Value)} has {chapterCount} chapters, so there is no chapter " +
                    $"{chapter}. That is the shared numbering; a text of its own may divide the book differently.");
            }

            var byText = new Dictionary<string, Dictionary<int, List<TextWordResponse>>>();
            var references = new Dictionary<string, Dictionary<int, VerseRefResponse>>();
            foreach (var entry in requested)
            {
                byText[entry.Slug] = await Texts.ReadByCanonicalVerse(
                    db, entry.Id, ordinal.Value, chapter, cancellationToken);
                references[entry.Slug] = await OwnReferences(db, entry.Id, ordinal.Value, chapter, cancellationToken);
            }

            var numbers = byText.Values
                .SelectMany(verses => verses.Keys)
                .Distinct()
                .OrderBy(number => number)
                .ToList();

            var rows = numbers
                .Select(number => new ParallelVerseResponse(
                    number,
                    requested.ToDictionary(
                        entry => entry.Slug,
                        entry => Cell(byText[entry.Slug], references[entry.Slug], number))))
                .ToList();

            var corpusRows = await CorpusRows(db, canon, requested, cancellationToken);

            return Results.Ok(new ParallelTextResponse(
                new BookRefResponse(ordinal.Value, BookReferences.Name(ordinal.Value),
                    BookReferences.Slug(ordinal.Value)),
                chapter,
                chapterCount,
                requested[0].Slug,
                corpusRows,
                rows));
        });
    }

    /// <summary>
    /// A text with no verse at this address answers null, which is a different fact from a verse
    /// with no words: the first is "this text does not go here", the second would be a defect.
    /// </summary>
    private static ParallelCellResponse? Cell(
        Dictionary<int, List<TextWordResponse>> verses,
        Dictionary<int, VerseRefResponse> references,
        int number) =>
        verses.TryGetValue(number, out var words)
            ? new ParallelCellResponse(words, PairedThroughTheFrame, references.GetValueOrDefault(number))
            : null;

    private static async Task<List<TextEntry>> Requested(
        ICanonIndex canon,
        string? corpora,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(corpora))
        {
            return (await canon.Texts(cancellationToken)).Take(2).ToList();
        }

        var found = new List<TextEntry>();
        foreach (var slug in corpora.Split(CorpusSeparator, StringSplitOptions.RemoveEmptyEntries |
                                                            StringSplitOptions.TrimEntries))
        {
            if (await canon.Text(slug, cancellationToken) is { } entry && found.All(f => f.Id != entry.Id))
            {
                found.Add(entry);
            }
        }

        return found;
    }

    /// <summary>What each text calls the verses it puts at these canonical addresses.</summary>
    private static async Task<Dictionary<int, VerseRefResponse>> OwnReferences(
        AppDbContext db,
        int textId,
        int canonicalBook,
        int canonicalChapter,
        CancellationToken cancellationToken)
    {
        var rows = await db.VerseReferences
            .Where(r => r.IsPrimary
                        && r.Verse!.TextId == textId
                        && r.CanonicalBook == canonicalBook
                        && r.CanonicalChapter == canonicalChapter)
            .Select(r => new { r.CanonicalVerse, r.Verse!.ChapterNumber, r.Verse.Number })
            .ToListAsync(cancellationToken);

        return rows
            .GroupBy(r => r.CanonicalVerse)
            .ToDictionary(
                group => group.Key,
                group => new VerseRefResponse(
                    canonicalBook,
                    BookReferences.Name(canonicalBook),
                    BookReferences.Slug(canonicalBook),
                    group.Min(r => r.ChapterNumber),
                    group.Min(r => r.Number)));
    }

    private static async Task<IList<CorpusResponse>> CorpusRows(
        AppDbContext db,
        ICanonIndex canon,
        List<TextEntry> requested,
        CancellationToken cancellationToken)
    {
        var ids = requested.Select(e => e.Id).ToList();
        var texts = await db.Texts.Where(t => ids.Contains(t.Id)).ToListAsync(cancellationToken);

        return requested
            .Select(entry => Texts.Corpus(
                texts.Single(t => t.Id == entry.Id),
                new CoverageResponse(entry.FirstBook, entry.LastBook),
                entry.HasWordMapping))
            .ToList();
    }
}
