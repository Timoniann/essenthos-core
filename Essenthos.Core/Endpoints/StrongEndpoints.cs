using System.Data;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Strong;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Essenthos.Core.Endpoints;

/// <summary>
/// Strong's concordance, and the words of the corpus that carry each number.
///
/// The occurrences endpoint is the one worth having. A dictionary entry is a page anyone can find
/// elsewhere; "every place this lexeme stands, in every witness that tags it, and how each
/// translation rendered it there" is the thing this corpus is shaped to answer and almost nowhere
/// else can.
/// </summary>
internal static class StrongEndpoints
{
    /// <summary>
    /// A search that returns everything is a search nobody paged, and it is the whole table over
    /// the wire.
    /// </summary>
    private const int MostPerPage = 200;

    public static void MapStrong(this IEndpointRouteBuilder routes)
    {
        routes.MapGet("/strong/{number}", async (
            string number,
            AppDbContext db,
            CancellationToken cancellationToken) =>
        {
            if (StrongNumbers.Normalize(number) is not { } canonical)
            {
                return Results.BadRequest(new ProblemResponse(
                    $"\"{number}\" is not a Strong number. Write a language letter and digits, as H430 or G26."));
            }

            var entry = await db.StrongEntries
                .Where(e => e.StrongNumber == canonical)
                .FirstOrDefaultAsync(cancellationToken);

            if (entry is not null)
            {
                return Results.Ok(Response(entry, await Gentilic(db, canonical, cancellationToken)));
            }

            // A prefix morpheme is not a missing entry. ETCBC numbers the conjunction, the article
            // and the inseparable prepositions in the H9000 range, and Strong never catalogued them
            // because a concordance has nothing to say about a letter. Answering 404 would report
            // 121,077 words of this corpus as broken.
            return StrongMorphemeCodes.GetDescription(canonical) is { } morpheme
                ? Results.Ok(new StrongEntryResponse(canonical, null, null, null, morpheme, null, null,
                    null, null, null, null, null, true, null))
                : Results.NotFound(new ProblemResponse(
                    $"Strong's concordance has no entry {canonical}."));
        });

        routes.MapGet("/strong", async (
            [FromQuery] string? query,
            [FromQuery] string? language,
            [FromQuery] int? skip,
            [FromQuery] int? take,
            AppDbContext db,
            CancellationToken cancellationToken) =>
        {
            var entries = db.StrongEntries.AsQueryable();

            if (language is { Length: > 0 })
            {
                var letter = language.StartsWith('g') || language.StartsWith('G') ? "G" : "H";
                entries = entries.Where(e => e.StrongNumber.StartsWith(letter));
            }

            if (query is { Length: > 0 })
            {
                var like = $"%{query}%";
                entries = entries.Where(e =>
                    EF.Functions.ILike(e.Lemma ?? string.Empty, like) ||
                    EF.Functions.ILike(e.Transliteration ?? string.Empty, like) ||
                    EF.Functions.ILike(e.Definition ?? string.Empty, like) ||
                    EF.Functions.ILike(e.KjvDefinition ?? string.Empty, like));
            }

            var total = await entries.CountAsync(cancellationToken);
            var page = await entries
                .OrderBy(e => e.StrongNumber.Substring(0, 1))
                .ThenBy(e => e.StrongNumber.Length)
                .ThenBy(e => e.StrongNumber)
                .Skip(Math.Max(0, skip ?? 0))
                .Take(Math.Clamp(take ?? 50, 1, MostPerPage))
                .Select(e => Response(e, null))
                .ToListAsync(cancellationToken);

            return Results.Ok(new StrongListResponse(total, page));
        });

        routes.MapGet("/strong/{number}/occurrences", async (
            string number,
            [FromQuery] string? corpus,
            [FromQuery] int? skip,
            [FromQuery] int? take,
            AppDbContext db,
            ICanonIndex canon,
            CancellationToken cancellationToken) =>
        {
            if (StrongNumbers.Normalize(number) is not { } canonical)
            {
                return Results.BadRequest(new ProblemResponse(
                    $"\"{number}\" is not a Strong number. Write a language letter and digits, as H430 or G26."));
            }

            var words = db.Words.Where(w => w.StrongNumber == canonical);
            if (corpus is { Length: > 0 })
            {
                // Resolved rather than compared to the column, so that a spelling the corpus does
                // not know is a 404 and not an empty page: matching the slug directly answered
                // "this number stands nowhere" to a question that was never asked of a real text.
                if (await canon.Text(corpus, cancellationToken) is not { } named)
                {
                    return Results.NotFound(new ProblemResponse(
                        $"There is no text \"{corpus}\". Ask /v1/corpora for the ones this corpus holds."));
                }

                words = words.Where(w => w.TextId == named.Id);
            }

            var total = await words.CountAsync(cancellationToken);
            var page = await words
                .OrderBy(w => w.Text!.Slug)
                .ThenBy(w => w.Verse!.Book!.CanonicalOrdinal)
                .ThenBy(w => w.Verse!.ChapterNumber)
                .ThenBy(w => w.Verse!.Number)
                .ThenBy(w => w.Position)
                .Skip(Math.Max(0, skip ?? 0))
                .Take(Math.Clamp(take ?? 50, 1, MostPerPage))
                .Select(w => new
                {
                    Corpus = w.Text!.Slug,
                    Ordinal = w.Verse!.Book!.CanonicalOrdinal,
                    w.Verse!.ChapterNumber,
                    Verse = w.Verse!.Number,
                    w.Id,
                    w.Position,
                    w.Surface,
                    w.Gloss,
                })
                .ToListAsync(cancellationToken);

            return Results.Ok(new StrongOccurrenceListResponse(
                canonical,
                total,
                [
                    .. page.Select(w => new StrongOccurrenceResponse(
                        w.Corpus,
                        w.Ordinal,
                        BookReferences.Name(w.Ordinal),
                        BookReferences.Slug(w.Ordinal),
                        w.ChapterNumber,
                        w.Verse,
                        w.Id,
                        w.Position,
                        w.Surface,
                        w.Gloss)),
                ]));
        });

        MapRenderings(routes);
    }

    /// <summary>
    /// How a translation actually renders this lexeme, counted over every place it stands.
    ///
    /// This is the question the corpus is shaped for and the reason the link table exists. A
    /// dictionary says H430 means "God"; this says the King James writes it <em>god</em> 2,284
    /// times, <em>gods</em> 210, and — because a Hebrew word carries its pronoun with it and the
    /// English has to spend a separate word on it — <em>thy</em> 342 times and <em>our</em> 184.
    /// Nothing about that is in a lexicon, and no free site answers it.
    ///
    /// It is counted from links, so it inherits their honesty and their limits both: a rendering
    /// listed here is one some link asserts, and a word this text reaches by no link is counted
    /// separately as unrendered rather than quietly dropped.
    /// </summary>
    private static void MapRenderings(IEndpointRouteBuilder routes) =>
        routes.MapGet("/strong/{number}/renderings", async (
            string number,
            [FromQuery] string? corpus,
            [FromQuery] int? take,
            AppDbContext db,
            ICanonIndex canon,
            CancellationToken cancellationToken) =>
        {
            if (StrongNumbers.Normalize(number) is not { } canonical)
            {
                return Results.BadRequest(new ProblemResponse(
                    $"\"{number}\" is not a Strong number. Write a language letter and digits, as H430 or G26."));
            }

            if (corpus is not { Length: > 0 })
            {
                return Results.BadRequest(new ProblemResponse(
                    "Name the text whose renderings you want, as ?corpus=kjv. GET /v1/corpora lists them."));
            }

            if (await canon.Text(corpus, cancellationToken) is not { } text)
            {
                return Results.NotFound(new ProblemResponse($"There is no text \"{corpus}\"."));
            }

            // Counted only over the witnesses this text is linked to at all. G26 stands 348 times
            // across the three Greek witnesses, but the King James is linked to two of them, so
            // counting all three would report a third of the lexeme as unrendered when the truth is
            // that those words belong to an edition this pair does not join.
            var neighbours = await db.Links
                .Where(l => l.FromTextId == text.Id || l.ToTextId == text.Id)
                .Select(l => l.FromTextId == text.Id ? l.ToTextId : l.FromTextId)
                .Distinct()
                .ToListAsync(cancellationToken);

            var occurrences = await db.Words.CountAsync(
                w => w.StrongNumber == canonical && neighbours.Contains(w.TextId), cancellationToken);

            var reached = await db.LinkWords
                .Where(side => side.Word!.StrongNumber == canonical
                               && (side.Link!.FromTextId == text.Id || side.Link.ToTextId == text.Id)
                               && (side.Link!.Relation == LinkRelation.Renders
                                   || side.Link.Relation == LinkRelation.Equals))
                .Select(side => side.WordId)
                .Distinct()
                .CountAsync(cancellationToken);

            var counted = await Phrases(db, canonical, text.Id, Math.Clamp(take ?? 40, 1, MostPerPage),
                cancellationToken);

            return Results.Ok(new StrongRenderingsResponse(
                canonical,
                text.Slug,
                occurrences,
                reached,
                occurrences - reached,
                [.. counted.Select(row => new StrongRenderingResponse(row.Text, row.Count))]));
        });

    /// <summary>
    /// The whole phrase each link renders, not each word of it separately.
    ///
    /// Counting words alone reports אֱלֹהֶיךָ as <em>god</em> 2,284 times and <em>thy</em> 342, which
    /// reads as noise and is not: a Hebrew word carries its pronoun and its construct relation
    /// inside itself, so <em>thy God</em> is one rendering of one word and splitting it in two
    /// destroys the very thing the link recorded. Grouped by link it reads as what it is —
    /// <em>god</em> 684, <em>thy god</em> 319, <em>of god</em> 317.
    ///
    /// Written as SQL because the aggregation is one <c>string_agg</c> over an ordered group inside
    /// a grouped outer query, which EF will not translate; doing it in memory would pull every word
    /// of every link for a number like the conjunction.
    /// </summary>
    private static async Task<List<StrongRenderingResponse>> Phrases(
        AppDbContext db,
        string canonical,
        int textId,
        int take,
        CancellationToken cancellationToken)
    {
        const string sql =
            """
            SELECT phrase, count(*) AS uses
            FROM (
                SELECT string_agg(lower(w.text), ' ' ORDER BY v.number, w.position) AS phrase
                FROM link l
                JOIN link_word o ON o.link_id = l.id
                JOIN word w ON w.id = o.word_id AND w.text_id = @text
                JOIN verse v ON v.id = w.verse_id
                WHERE (l.from_text_id = @text OR l.to_text_id = @text)
                  AND l.relation IN ('renders', 'equals')
                  AND EXISTS (
                      SELECT 1 FROM link_word s
                      JOIN word sw ON sw.id = s.word_id
                      WHERE s.link_id = l.id AND s.side <> o.side AND sw.strong_number = @number)
                GROUP BY l.id
            ) rendered
            WHERE phrase IS NOT NULL
            GROUP BY phrase
            ORDER BY count(*) DESC, phrase
            LIMIT @take
            """;

        var connection = (NpgsqlConnection)db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken);
        }

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("text", textId);
        command.Parameters.AddWithValue("number", canonical);
        command.Parameters.AddWithValue("take", take);

        var rows = new List<StrongRenderingResponse>(take);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new StrongRenderingResponse(reader.GetString(0), (int)reader.GetInt64(1)));
        }

        return rows;
    }

    /// <summary>
    /// Whom this people is named after, where the dictionary says so.
    ///
    /// It hangs on the entry rather than on a route of its own because it is a property of the
    /// lexeme and because this is the request a reader hovering a word already makes: the word
    /// carries H4125, this answers that a Moabite descends from Moab, and where the origin is one
    /// person or one place it hands over the page as well.
    ///
    /// The origin's own gloss is carried too, so the claim reads as a sentence for the 109 origins
    /// that reach no page — <em>patronymic from H2246, Chobab</em> is worth more to a reader than a
    /// number on its own.
    /// </summary>
    internal static async Task<StrongGentilicResponse?> Gentilic(
        AppDbContext db,
        string canonical,
        CancellationToken cancellationToken)
    {
        var stated = await db.StrongGentilics
            .Include(g => g.Origin)
            .FirstOrDefaultAsync(g => g.StrongNumber == canonical, cancellationToken);

        if (stated is null)
        {
            return null;
        }

        var origin = await db.StrongEntries
            .Where(e => e.StrongNumber == stated.OriginNumber)
            .Select(e => new { e.Lemma, e.Definition })
            .FirstOrDefaultAsync(cancellationToken);

        return new StrongGentilicResponse(
            stated.OriginNumber,
            stated.Kind,
            origin?.Lemma,
            origin?.Definition,
            stated.Statement,
            stated.Source,
            stated.Origin is null ? null : EnumSpelling.Of(stated.Origin.Kind),
            stated.Origin?.Slug,
            stated.Origin?.Name);
    }

    private static StrongEntryResponse Response(
        Database.Entities.StrongEntry entry,
        StrongGentilicResponse? gentilic) => new(
        entry.StrongNumber,
        entry.Lemma,
        entry.Transliteration,
        entry.Pronunciation,
        entry.Definition,
        entry.Derivation,
        entry.KjvDefinition,
        entry.Morphology,
        entry.DetailedDefinition,
        entry.SeeAlso,
        entry.SourceLanguage,
        entry.TwotReference,
        false,
        gentilic);
}

/// <param name="Kind">
/// <c>patronymic</c> where the people is named after a man, <c>patrial</c> where it is named after
/// a place, and <c>patronymic or patrial</c> where the dictionary writes both and settles neither.
/// </param>
/// <param name="Statement">
/// The dictionary's own clause. Shown rather than paraphrased: this is a nineteenth-century
/// lexicographer's claim about a people's ancestry, and a reader is entitled to weigh it in his
/// words.
/// </param>
/// <param name="EntityKind">
/// The page the origin reaches, where it reaches exactly one. Null is the common answer — 23 men
/// are called Zechariah and the derivation does not say which — and it means the claim stands
/// without a page behind it, never that the claim is weaker.
/// </param>
internal record StrongGentilicResponse(
    string Origin,
    string Kind,
    string? OriginLemma,
    string? OriginDefinition,
    string Statement,
    string Source,
    string? EntityKind,
    string? EntitySlug,
    string? EntityName);

/// <param name="Morpheme">
/// True where the number is not a concordance entry at all but a prefix morpheme ETCBC numbers in
/// the H9000 range. The definition then says which morpheme, and everything else is null — because
/// there is no entry, not because one is missing.
/// </param>
/// <param name="Gentilic">
/// Whom this people is named after, where the entry is a gentilic and the dictionary states an
/// origin plainly enough to be read. Null for everything else, which is 14,005 of the 14,197
/// entries.
/// </param>
internal record StrongEntryResponse(
    string StrongNumber,
    string? Lemma,
    string? Transliteration,
    string? Pronunciation,
    string? Definition,
    string? Derivation,
    string? KjvDefinition,
    string? Morphology,
    string? DetailedDefinition,
    string? SeeAlso,
    string? SourceLanguage,
    string? TwotReference,
    bool Morpheme,
    StrongGentilicResponse? Gentilic);

internal record StrongListResponse(int Total, IList<StrongEntryResponse> Items);

internal record StrongOccurrenceResponse(
    string Corpus,
    int BookOrdinal,
    string Book,
    string BookSlug,
    int Chapter,
    int Verse,
    long WordId,
    int Position,
    string Text,
    string? Gloss);

internal record StrongOccurrenceListResponse(
    string StrongNumber,
    int Total,
    IList<StrongOccurrenceResponse> Items);

/// <param name="Occurrences">Every word of the corpus carrying this number, in any witness.</param>
/// <param name="Reached">
/// How many of those the named text renders by some link. The gap between this and
/// <paramref name="Occurrences"/> is the honest one: places the lexeme stands and nothing in this
/// text has been linked to it.
/// </param>
/// <param name="Renderings">
/// Each distinct phrase this text puts where the lexeme stands, and how often. A phrase, not a
/// word: one Hebrew word is often two or three English ones and the link says which.
/// </param>
internal record StrongRenderingsResponse(
    string StrongNumber,
    string Corpus,
    int Occurrences,
    int Reached,
    int Unrendered,
    IList<StrongRenderingResponse> Renderings);

internal record StrongRenderingResponse(string Text, int Count);
