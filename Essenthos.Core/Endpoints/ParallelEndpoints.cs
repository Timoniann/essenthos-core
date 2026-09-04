using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities.Enums;
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
/// <param name="Verses">
/// Which of this text's own verses the row holds, as the text numbers them — <c>50</c>, <c>50a</c>.
///
/// Usually one, and then it says nothing new. Where a text divides a passage more finely than the
/// shared frame does it is more than one, and saying so is the difference between a row a reader can
/// trust and a silent merge: Brenton's Genesis 31:50 and 31:50a both belong at canonical 31:50, so
/// the row carried 39 Greek words against 19 Hebrew with nothing to say why. 91 addresses across the
/// Septuagint are like that. PRB-0118.
/// </param>
/// <param name="Strength">
/// How strongly this verse is linked to the same row of the reference pane, or null on the
/// reference pane itself and on a text nothing links to it.
/// </param>
internal record ParallelCellResponse(
    IList<TextWordResponse> Words,
    string Alignment,
    VerseRefResponse? Reference,
    IList<string> Verses,
    LinkStrengthResponse? Strength);

/// <param name="Links">
/// How many links join the two verses. A mean over one or two of them says nothing; the
/// verification pass reads no verse's strength under three, and a client showing this should be at
/// least as careful.
/// </param>
/// <param name="Stated">
/// Of those, how many a source or a person asserted. Those carry no number at all — that is what
/// the schema means by a stated link, and defaulting them to 1 would make testimony and a
/// confident guess indistinguishable.
/// </param>
/// <param name="Confidence">
/// The mean confidence of the links that have one, and null where none of them does. A verse pair
/// this is low on is either laid against the wrong verse or a place the two traditions genuinely
/// say different things, and the corpus cannot tell the reader which — but it can say the words on
/// the two sides answer each other faintly, which is the fact a split view exists to show. Of the
/// 154 verse pairs the verification pass calls suspect between Brenton and BHSA, 137 are paired
/// correctly and faint because the Septuagint is translating freely.
/// </param>
internal record LinkStrengthResponse(int Links, int Stated, double? Confidence);

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
            var own = new Dictionary<string, Dictionary<int, List<string>>>();
            var strength = new Dictionary<string, Dictionary<int, LinkStrengthResponse>>();
            var reference = requested[0];
            foreach (var entry in requested)
            {
                byText[entry.Slug] = await Texts.ReadByCanonicalVerse(
                    db, entry.Id, ordinal.Value, chapter, cancellationToken);
                references[entry.Slug] = await OwnReferences(db, entry.Id, ordinal.Value, chapter, cancellationToken);
                own[entry.Slug] = await OwnVerses(db, entry.Id, ordinal.Value, chapter, cancellationToken);
                strength[entry.Slug] = entry.Id == reference.Id
                    ? []
                    : await Strengths(db, entry.Id, reference.Id, ordinal.Value, chapter, cancellationToken);
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
                        entry => Cell(
                            byText[entry.Slug], references[entry.Slug], own[entry.Slug],
                            strength[entry.Slug], number))))
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
        Dictionary<int, List<string>> own,
        Dictionary<int, LinkStrengthResponse> strength,
        int number) =>
        verses.TryGetValue(number, out var words)
            ? new ParallelCellResponse(
                words,
                PairedThroughTheFrame,
                references.GetValueOrDefault(number),
                own.GetValueOrDefault(number) ?? [],
                strength.GetValueOrDefault(number))
            : null;

    /// <summary>
    /// How strongly each of this text's verses answers the reference pane's verse at the same
    /// address, which is the one thing about a verse pair that only the corpus knows.
    ///
    /// <para>
    /// The verification pass computes exactly this number and uses it to decide a verse was laid
    /// against the wrong verse. That is one of two things a faint pair can mean, and the other is
    /// the more interesting: the two traditions say different things here. The corpus cannot tell
    /// them apart, and a reader looking at both panes can — but only if the number reaches them,
    /// and until now it reached nobody outside a verification run.
    /// </para>
    ///
    /// <para>
    /// Against the reference pane alone rather than every pair of panes. Six corpora make fifteen
    /// pairs and fourteen of them are not what the reader is comparing; the response already names
    /// the reference corpus, and this is read against it.
    /// </para>
    ///
    /// <para>
    /// Links are counted once however many words of this text they name, and read in either
    /// direction, because which text a link is stored as being from is a fact about the loader
    /// rather than about the two verses.
    /// </para>
    /// </summary>
    public static async Task<Dictionary<int, LinkStrengthResponse>> Strengths(
        AppDbContext db,
        int textId,
        int againstId,
        int canonicalBook,
        int canonicalChapter,
        CancellationToken cancellationToken)
    {
        var rows = await db.LinkWords
            .Where(side => side.Word!.Verse!.References.Any(r => r.IsPrimary
                                                                 && r.CanonicalBook == canonicalBook
                                                                 && r.CanonicalChapter == canonicalChapter)
                           && ((side.Side == LinkSide.From
                                && side.Link!.FromTextId == textId
                                && side.Link.ToTextId == againstId)
                               || (side.Side == LinkSide.To
                                   && side.Link!.ToTextId == textId
                                   && side.Link.FromTextId == againstId)))
            .Select(side => new
            {
                Canonical = side.Word!.Verse!.References.First(r => r.IsPrimary).CanonicalVerse,
                side.LinkId,
                side.Link!.Confidence,
            })
            .Distinct()
            .ToListAsync(cancellationToken);

        return rows
            .GroupBy(row => row.Canonical)
            .ToDictionary(
                group => group.Key,
                group => Strength([.. group.Select(row => row.Confidence)]));
    }

    /// <summary>
    /// A stated link carries no confidence and is not averaged as though it did — <c>Average</c>
    /// over nullable doubles skips the nulls and answers null when they are all there is, which is
    /// the reading wanted here: nothing inferred this pair, so there is no inference to report.
    /// </summary>
    private static LinkStrengthResponse Strength(IReadOnlyList<double?> confidences) =>
        new(confidences.Count, confidences.Count(c => c is null), confidences.Average());

    /// <summary>
    /// Which of a text's own verses sit at each canonical address in this chapter, as the text
    /// numbers them.
    ///
    /// <see cref="OwnReferences"/> answers the same question and keeps only one per address, because
    /// a reference is a single address and cannot be two. That is right for what it is for and it is
    /// why the label went missing: Brenton's <c>31:50a</c> and <c>31:50</c> both belong at canonical
    /// 31:50, so one of the two was reported and the row silently held both.
    /// </summary>
    private static async Task<Dictionary<int, List<string>>> OwnVerses(
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
            .Select(r => new { r.CanonicalVerse, r.Verse!.ChapterNumber, r.Verse.Number, r.Verse.Label })
            .ToListAsync(cancellationToken);

        return rows
            .GroupBy(r => r.CanonicalVerse)
            .ToDictionary(
                group => group.Key,
                group => group
                    .OrderBy(r => r.Number).ThenBy(r => r.Label, StringComparer.Ordinal)
                    .Select(r => $"{r.ChapterNumber}:{r.Number}{r.Label}")
                    .ToList());
    }

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
                new CoverageResponse(entry.FirstBook, entry.LastBook, entry.Books),
                entry.HasWordMapping))
            .ToList();
    }
}
