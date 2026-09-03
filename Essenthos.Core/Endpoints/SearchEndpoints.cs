using Essenthos.Core.Database;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Essenthos.Core.Endpoints;

/// <summary>
/// Search over the text of one corpus.
///
/// A verse matches when **every** term matches some word in it. That is the granularity the
/// corpus is stored at — one row per word — and it is also the honest one: this is a word search
/// over a word-level corpus, not a phrase search pretending the verses are strings.
///
/// Every term is matched against the word's folded form, so a search for <c>bereshit</c> in
/// unpointed letters finds <c>בְּרֵאשִׁית</c> and <c>θεος</c> finds <c>Θεὸς</c>. Nobody types the
/// points and nobody types the accents, so neither is searched. <see cref="WordFolding"/> does the
/// folding for both the stored word and the typed term, which is the only way the two can be
/// guaranteed to agree.
///
/// A term matches a stored word first, then the word as it is printed, then part of one. The
/// middle step is there because a row is a morpheme: Hebrew writes the preposition and the article
/// onto the noun, so בְּרֵאשִׁית is two rows and one printed word, and a reader typing what the page
/// shows is typing something no single row contains. The response says per term which of the three
/// happened — a search that silently changes what it did is a search whose results cannot be read.
/// </summary>
internal static class SearchEndpoints
{
    /// <summary>A page bigger than this is a download, and there is an endpoint for reading text.</summary>
    private const int MostPerPage = 100;

    public static void MapSearch(this IEndpointRouteBuilder routes)
    {
        routes.MapGet("/search", async (
            [FromQuery] string? q,
            [FromQuery] string? corpus,
            [FromQuery] string? book,
            [FromQuery] int? skip,
            [FromQuery] int? take,
            AppDbContext db,
            ICanonIndex index,
            CancellationToken cancellationToken) =>
        {
            var terms = SearchTerms.Parse(q);
            if (terms.Length == 0)
            {
                return Results.BadRequest(new ProblemResponse(SearchTerms.FormatHint()));
            }

            var text = corpus is { Length: > 0 }
                ? await index.Text(corpus, cancellationToken)
                : (await index.Texts(cancellationToken)).FirstOrDefault();

            if (text is null)
            {
                return ApiResults.NotFound(
                    $"There is no text \"{corpus}\". Ask /v1/corpora for the ones this corpus holds.");
            }

            int? ordinal = null;
            if (book is { Length: > 0 })
            {
                ordinal = BookReferences.ResolveOrdinal(book);
                if (ordinal is null)
                {
                    return ApiResults.NotFound(BookReferences.FormatHint(book));
                }
            }

            var language = await db.Texts.Where(t => t.Id == text.Id).Select(t => t.Language)
                .FirstAsync(cancellationToken);

            // Every term narrows the set of verses. Each is resolved on its own so that the
            // response can say how each one was matched, and so that one term falling back to a
            // substring does not quietly change how the others were read.
            var matched = new List<SearchTerm>(terms.Length);
            IQueryable<int>? verses = null;

            foreach (var term in terms)
            {
                var folded = WordFolding.Fold(term, language);
                var words = db.Words.Where(w => w.TextId == text.Id && w.NormalisedText != null);
                if (ordinal is { } only)
                {
                    words = words.Where(w => w.Verse!.Book!.CanonicalOrdinal == only);
                }

                var whole = words.Where(w => w.NormalisedText == folded);
                var matching = TermMatching.Folded;

                if (!await whole.AnyAsync(cancellationToken))
                {
                    // No row is that word. It may still be a word of the text: a row is a morpheme,
                    // and Hebrew prints several of them together, so בראשית is two rows and one
                    // printed word. Ask that before falling back to a substring, or the commonest
                    // noun phrase in the Hebrew Bible is findable only as a fragment of a longer
                    // word somewhere else.
                    whole = words.Where(w => w.GraphicalText == folded);
                    matching = TermMatching.Printed;
                }

                if (!await whole.AnyAsync(cancellationToken))
                {
                    // Nothing in the corpus is that word, printed or stored. Try it as part of one
                    // before giving up: a reader typing "beginn" means "beginning", and answering
                    // nothing is worse than answering something and saying what was done.
                    var pattern = LikePatterns.Containing(folded);
                    whole = words.Where(w => EF.Functions.Like(w.NormalisedText!, pattern));
                    matching = TermMatching.Substring;
                }

                matched.Add(new SearchTerm(term, matching, folded));
                var forThisTerm = whole.Select(w => w.VerseId).Distinct();
                verses = verses is null ? forThisTerm : verses.Intersect(forThisTerm);
            }

            var total = await verses!.CountAsync(cancellationToken);
            var page = await db.Verses
                .Where(v => verses!.Contains(v.Id))
                .OrderBy(v => v.Book!.CanonicalOrdinal).ThenBy(v => v.ChapterNumber)
                .ThenBy(v => v.Number).ThenBy(v => v.Label)
                .Skip(Math.Max(0, skip ?? 0))
                .Take(Math.Clamp(take ?? 20, 1, MostPerPage))
                .Select(v => new
                {
                    v.Id,
                    Ordinal = v.Book!.CanonicalOrdinal,
                    v.ChapterNumber,
                    v.Number,
                    v.Label,
                    Words = v.Words.OrderBy(w => w.Position)
                        .Select(w => new { w.Surface, w.Trailer, w.NormalisedText, w.GraphicalText })
                        .ToList(),
                })
                .ToListAsync(cancellationToken);

            var wanted = matched.ToList();

            return Results.Ok(new SearchResultsResponse(
                total,
                Math.Max(0, skip ?? 0),
                page.Count,
                [
                    .. page.Select(verse => new SearchHitResponse(
                        text.Slug,
                        new BookRefResponse(
                            verse.Ordinal, BookReferences.Name(verse.Ordinal), BookReferences.Slug(verse.Ordinal)),
                        verse.ChapterNumber,
                        verse.Number,
                        Snippets.Build(verse.Words.Select(word => (
                            word.Surface,
                            word.Trailer,
                            Matched: word.NormalisedText is { } folded && wanted.Any(term =>
                                term.Matching switch
                                {
                                    // Every row of the run carries the run's form, so marking on it
                                    // marks the whole printed word rather than the morpheme that
                                    // happens to spell the term.
                                    TermMatching.Printed => word.GraphicalText == term.Match,
                                    TermMatching.Folded => folded == term.Match,
                                    _ => folded.Contains(term.Match, StringComparison.Ordinal),
                                })))),
                        verse.Label)),
                ],
                SearchTerms.Matching(matched),
                [.. matched.Select(term => new SearchTermResponse(term.Text, SearchTerms.Name(term.Matching)))]));
        });
    }
}

/// <param name="Matching">
/// How the query as a whole was matched: <c>fulltext</c>, <c>substring</c>, or <c>mixed</c> when
/// the terms were not all read the same way. <paramref name="Terms"/> has it per term.
/// </param>
internal record SearchResultsResponse(
    int Total,
    int Skip,
    int Take,
    IList<SearchHitResponse> Items,
    string Matching,
    IList<SearchTermResponse> Terms);

/// <param name="Snippet">
/// The verse, rebuilt from its words with the matched ones wrapped in <c>&lt;em&gt;</c> and
/// everything else escaped. Which words matched is decided by the query that selected the verse,
/// not by the spelling of the word — deciding it from the spelling marks "the" for a search for
/// "therefore".
/// </param>
internal record SearchHitResponse(
    string Corpus,
    BookRefResponse Book,
    int Chapter,
    int Verse,
    string Snippet,
    string Label);

internal record SearchTermResponse(string Text, string Matching);
