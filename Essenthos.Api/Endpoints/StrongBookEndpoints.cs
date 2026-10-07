using Essenthos.Core.Corpus;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Strong;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Essenthos.Core.Endpoints;

/// <summary>
/// Where a Strong number stands, book by book, and how one or two texts render it there.
///
/// <para>
/// The map is read from what the load counted (<c>strong_book</c>, <c>strong_book_reach</c>), a few
/// hundred rows off one index per text. The phrases of one book are counted as they are asked,
/// because they are bounded by the number and the book: the words of one lexeme in one book and the
/// links of two texts on them, never the corpus.
/// </para>
/// </summary>
internal static class StrongBookEndpoints
{
    /// <summary>Two texts side by side is the comparison; a third makes every cell unreadable.</summary>
    private const int MostTexts = 2;

    private const int PhrasesPerBook = 20;

    /// <summary>The ETCBC prefix morphemes, which a rate per numbered word leaves out of its denominator.</summary>
    private const string Morpheme = "H9";

    public static void MapStrongBooks(this IEndpointRouteBuilder routes)
    {
        routes.MapGet("/strong/{number}/books", async (
            string number,
            [FromQuery] string? corpus,
            AppDbContext db,
            ICanonIndex canon,
            CancellationToken cancellationToken) =>
        {
            if (StrongNumbers.Normalize(number) is not { } canonical)
            {
                return Results.BadRequest(new ProblemResponse(
                    $"\"{number}\" is not a Strong number. Write a language letter and digits, as H430 or G26."));
            }

            var texts = new List<TextEntry>();
            foreach (var slug in Named(corpus))
            {
                if (await canon.Text(slug, cancellationToken) is not { } text)
                {
                    return Results.NotFound(new ProblemResponse($"There is no text \"{slug}\"."));
                }

                texts.Add(text);
            }

            return Results.Ok(await Map(db, canonical, texts, cancellationToken));
        });

        routes.MapGet("/strong/{number}/books/{book:int}", async (
            string number,
            int book,
            [FromQuery] string? corpus,
            AppDbContext db,
            ICanonIndex canon,
            CancellationToken cancellationToken) =>
        {
            if (StrongNumbers.Normalize(number) is not { } canonical)
            {
                return Results.BadRequest(new ProblemResponse(
                    $"\"{number}\" is not a Strong number. Write a language letter and digits, as H430 or G26."));
            }

            var renderings = new List<StrongBookRenderingsResponse>();
            foreach (var slug in Named(corpus))
            {
                if (await canon.Text(slug, cancellationToken) is not { } text)
                {
                    return Results.NotFound(new ProblemResponse($"There is no text \"{slug}\"."));
                }

                renderings.Add(await InBook(db, canonical, book, text.Id, text.Slug, cancellationToken));
            }

            return Results.Ok(new StrongBookPhrasesResponse(
                canonical, book, BookReferences.Name(book), BookReferences.Slug(book), renderings));
        }).RequireRateLimiting(RateLimits.Expensive);
    }

    /// <summary>The texts asked for, as <c>KJV,UBIO</c>; the King James where none is named.</summary>
    private static IEnumerable<string> Named(string? corpus) =>
        (corpus?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) is { Length: > 0 } named
            ? named
            : [StrongRenderingCounts.CardTranslation])
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .Take(MostTexts);

    /// <summary>
    /// The number's books in the edition the first text is counted over, each text's reach in each,
    /// and whether the text holds the book at all. A text counted over another edition says which.
    /// </summary>
    internal static async Task<StrongBookMapResponse> Map(
        AppDbContext db,
        string canonical,
        IReadOnlyList<TextEntry> texts,
        CancellationToken cancellationToken)
    {
        var witnesses = new List<int?>();
        foreach (var text in texts)
        {
            witnesses.Add(await db.StrongReaches
                              .Where(r => r.TextId == text.Id && r.StrongNumber == canonical)
                              .Select(r => (int?)r.WitnessId)
                              .FirstOrDefaultAsync(cancellationToken)
                          ?? LinkedOriginals.WitnessFor(
                              LinkedOriginals.Primary(await LinkedOriginals.Of(db, text.Id, cancellationToken)), canonical)?.Id);
        }

        var edition = witnesses.FirstOrDefault(w => w is not null);
        var named = await db.Texts.Where(t => witnesses.Contains(t.Id)).ToDictionaryAsync(t => t.Id, t => t.Slug, cancellationToken);
        var rows = edition is { } counted
            ? await db.StrongBooks.Where(b => b.WitnessId == counted && b.StrongNumber == canonical)
                .OrderBy(b => b.Book)
                .ToDictionaryAsync(b => b.Book, b => b.Occurrences, cancellationToken)
            : [];
        var books = rows.Keys.ToList();
        var numbered = edition is { } over
            ? await db.StrongBooks
                .Where(b => b.WitnessId == over && books.Contains(b.Book) && !b.StrongNumber.StartsWith(Morpheme))
                .GroupBy(b => b.Book)
                .Select(g => new { Book = g.Key, Words = g.Sum(b => b.Occurrences) })
                .ToDictionaryAsync(g => g.Book, g => g.Words, cancellationToken)
            : [];

        var columns = new List<StrongBookTextResponse>();
        for (var at = 0; at < texts.Count; at++)
        {
            var text = texts[at];
            var held = (await db.Books.Where(b => b.TextId == text.Id).Select(b => b.CanonicalOrdinal)
                .ToListAsync(cancellationToken)).ToHashSet();
            var linked = (await db.StrongBookReaches.Where(b => b.TextId == text.Id && books.Contains(b.Book))
                .Select(b => b.Book).Distinct().ToListAsync(cancellationToken)).ToHashSet();
            var reached = await db.StrongBookReaches
                .Where(b => b.TextId == text.Id && b.StrongNumber == canonical)
                .ToDictionaryAsync(b => b.Book, b => b.Reached, cancellationToken);

            columns.Add(new StrongBookTextResponse(
                text.Slug,
                witnesses[at] is { } witness ? named.GetValueOrDefault(witness) : null,
                [
                    .. rows.Keys.Order().Select(book =>
                        !held.Contains(book) ? new StrongBookCellResponse(book, StrongBookState.NotHeld, null)
                        : !linked.Contains(book) ? new StrongBookCellResponse(book, StrongBookState.NotLinked, null)
                        : new StrongBookCellResponse(book, StrongBookState.Counted, reached.GetValueOrDefault(book))),
                ]));
        }

        return new StrongBookMapResponse(
            canonical,
            edition is { } slug ? named.GetValueOrDefault(slug) : null,
            rows.Values.Sum(),
            [
                .. rows.OrderBy(row => row.Key).Select(row => new StrongBookRowResponse(
                    row.Key,
                    BookReferences.Name(row.Key),
                    BookReferences.Slug(row.Key),
                    row.Value,
                    numbered.GetValueOrDefault(row.Key))),
            ],
            columns);
    }

    /// <summary>
    /// The phrases one text writes for the number in one book, counted from the links on the number's
    /// words there — the same grouping by link as the entry page's renderings, bounded by the book.
    /// </summary>
    private const string PhrasesInBook =
        """
        WITH pairs AS (
            SELECT DISTINCT s.link_id, s.side
            FROM word sw
            JOIN verse sv ON sv.id = sw.verse_id
            JOIN book sb ON sb.id = sv.book_id
            JOIN link_word s ON s.word_id = sw.id
            JOIN link l ON l.id = s.link_id
            WHERE sw.strong_number = @number
              AND sw.text_id = @witness
              AND sb.canonical_ordinal = @book
              AND (l.from_text_id = @text OR l.to_text_id = @text)
              AND l.relation IN ('renders', 'equals')
        ),
        rendered AS (
            SELECT p.link_id, string_agg(lower(w.text), ' ' ORDER BY v.number, w.position) AS phrase
            FROM pairs p
            JOIN link_word o ON o.link_id = p.link_id AND o.side <> p.side
            JOIN word w ON w.id = o.word_id AND w.text_id = @text
            JOIN verse v ON v.id = w.verse_id
            GROUP BY p.link_id
        )
        SELECT phrase, count(*) AS uses
        FROM rendered
        WHERE phrase IS NOT NULL
        GROUP BY phrase
        ORDER BY uses DESC, phrase
        LIMIT @take
        """;

    internal static async Task<StrongBookRenderingsResponse> InBook(
        AppDbContext db,
        string canonical,
        int book,
        int textId,
        string slug,
        CancellationToken cancellationToken)
    {
        var witness = LinkedOriginals.WitnessFor(
            LinkedOriginals.Primary(await LinkedOriginals.Of(db, textId, cancellationToken)), canonical);
        if (witness is null)
        {
            return new StrongBookRenderingsResponse(slug, null, 0, []);
        }

        var reached = await db.StrongBookReaches
            .Where(b => b.TextId == textId && b.StrongNumber == canonical && b.Book == book)
            .Select(b => b.Reached)
            .FirstOrDefaultAsync(cancellationToken);

        var connection = (NpgsqlConnection)db.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken);
        }

        await using var command = new NpgsqlCommand(PhrasesInBook, connection);
        command.Parameters.AddWithValue("number", canonical);
        command.Parameters.AddWithValue("witness", witness.Id);
        command.Parameters.AddWithValue("book", book);
        command.Parameters.AddWithValue("text", textId);
        command.Parameters.AddWithValue("take", PhrasesPerBook);

        var phrases = new List<StrongRenderingResponse>();
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                phrases.Add(new StrongRenderingResponse(reader.GetString(0), (int)reader.GetInt64(1)));
            }
        }

        return new StrongBookRenderingsResponse(slug, witness.Slug, reached, phrases);
    }
}

/// <summary>How a text stands toward one book of the map.</summary>
internal static class StrongBookState
{
    /// <summary>The text has links into the book, and <c>reached</c> says how many of the number's words they render.</summary>
    public const string Counted = "counted";

    /// <summary>The text holds the book and no link of it reaches any word of the edition there: unaligned, not unused.</summary>
    public const string NotLinked = "not-linked";

    /// <summary>The text does not hold the book at all.</summary>
    public const string NotHeld = "not-held";
}

/// <param name="Edition">The edition of the original the occurrences are counted in, as its slug.</param>
/// <param name="Occurrences">Every word of the edition carrying the number, the sum of the rows.</param>
internal record StrongBookMapResponse(
    string StrongNumber,
    string? Edition,
    int Occurrences,
    IList<StrongBookRowResponse> Books,
    IList<StrongBookTextResponse> Texts);

/// <param name="Occurrences">The number's words in this book of the edition.</param>
/// <param name="NumberedWords">
/// The words of the book that carry any Strong number, prefix morphemes left out: the denominator a
/// rate per ten thousand words is taken over.
/// </param>
internal record StrongBookRowResponse(int BookOrdinal, string Book, string BookSlug, int Occurrences, int NumberedWords);

/// <param name="Witness">The edition the text's reach is counted in; null where it is linked to none for this language.</param>
internal record StrongBookTextResponse(string Corpus, string? Witness, IList<StrongBookCellResponse> Cells);

/// <param name="State"><c>counted</c>, <c>not-linked</c> or <c>not-held</c>.</param>
/// <param name="Reached">The number's words in the book the text renders; null unless <paramref name="State"/> is <c>counted</c>.</param>
internal record StrongBookCellResponse(int Book, string State, int? Reached);

internal record StrongBookPhrasesResponse(
    string StrongNumber,
    int BookOrdinal,
    string Book,
    string BookSlug,
    IList<StrongBookRenderingsResponse> Texts);

/// <param name="Reached">The number's words in the book this text's links render.</param>
internal record StrongBookRenderingsResponse(
    string Corpus,
    string? Witness,
    int Reached,
    IList<StrongRenderingResponse> Renderings);
