using Essenthos.Core.Corpus;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Strong;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

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
            [FromQuery] string? corpus,
            [FromQuery] int? skip,
            [FromQuery] int? take,
            AppDbContext db,
            ICanonIndex canon,
            CancellationToken cancellationToken) =>
        {
            var rendering = corpus is { Length: > 0 } ? corpus : StrongRenderingCounts.CardTranslation;
            if (await canon.Text(rendering, cancellationToken) is not { } text)
            {
                return Results.NotFound(new ProblemResponse(
                    $"There is no text \"{rendering}\". Ask /v1/corpora for the ones this corpus holds."));
            }

            var entries = db.StrongEntries.AsQueryable();

            if (language is { Length: > 0 })
            {
                var letter = language.StartsWith('g') || language.StartsWith('G') ? "G" : "H";
                entries = entries.Where(e => e.StrongNumber.StartsWith(letter));
            }

            if (query is { Length: > 0 })
            {
                var like = LikePatterns.Containing(query);
                entries = entries.Where(e =>
                    EF.Functions.ILike(e.Lemma ?? string.Empty, like) ||
                    EF.Functions.ILike(e.Transliteration ?? string.Empty, like) ||
                    EF.Functions.ILike(e.Definition ?? string.Empty, like) ||
                    EF.Functions.ILike(e.KjvDefinition ?? string.Empty, like));
            }

            var total = await entries.CountAsync(cancellationToken);
            var page = await entries
                // Hebrew before Greek, as the Old Testament comes before the New — not G before H
                // because that is where the letters fall.
                .OrderBy(e => e.StrongNumber.StartsWith("H") ? 0 : 1)
                .ThenBy(e => e.StrongNumber.Length)
                .ThenBy(e => e.StrongNumber)
                .Skip(Math.Max(0, skip ?? 0))
                .Take(Math.Clamp(take ?? 50, 1, MostPerPage))
                .Select(e => Response(e, null))
                .ToListAsync(cancellationToken);

            var numbers = page.Select(e => e.StrongNumber).ToList();
            var occurrences = await Occurrences(db, numbers, cancellationToken);
            var renderings = await Renderings(db, numbers, text.Id, StrongRenderingCounts.CardRenderings, cancellationToken);

            return Results.Ok(new StrongListResponse(
                total,
                [
                    .. page.Select(e => e with
                    {
                        Occurrences = occurrences.GetValueOrDefault(e.StrongNumber),
                        Renderings = renderings.GetValueOrDefault(e.StrongNumber, []),
                    }),
                ])
            {
                Corpus = text.Slug,
            });
        }).RequireRateLimiting(RateLimits.Expensive);

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

            return Results.Ok(await OccurrencePage(db, canonical, words, skip, take, cancellationToken));
        });

        MapRenderings(routes);
    }

    /// <summary>One page of the words carrying a number, with how many stand in each text.</summary>
    internal static async Task<StrongOccurrenceListResponse> OccurrencePage(
        AppDbContext db,
        string canonical,
        IQueryable<Word> words,
        int? skip,
        int? take,
        CancellationToken cancellationToken)
    {
        // Per text as well as in all, because the total counts every edition of the original
        // and every translation that tags its own words with the number, and a reader citing
        // it has to be able to say which of those it is.
        var byText = await words
            .GroupBy(w => w.Text!.Slug)
            .Select(g => new StrongTextCountResponse(g.Key, g.Count()))
            .ToListAsync(cancellationToken);
        var total = byText.Sum(text => text.Count);

        // Each occurrence is answered at the place its verse stands in the shared frame, which
        // is what a link opens and what a quote in another text is looked up by. BHSA numbers
        // 2,036 verses otherwise, the Septuagint more: its Malachi 3:19 is the frame's 4:1.
        var page = await (
                from w in words
                join placed in db.VerseReferences on w.VerseId equals placed.VerseId
                where placed.IsPrimary
                orderby w.Text!.Slug, placed.CanonicalBook, placed.CanonicalChapter, placed.CanonicalVerse,
                    w.Position, w.Id
                select new
                {
                    Corpus = w.Text!.Slug,
                    placed.CanonicalBook,
                    placed.CanonicalChapter,
                    placed.CanonicalVerse,
                    w.VerseId,
                    OwnBook = w.Verse!.Book!.CanonicalOrdinal,
                    OwnChapter = w.Verse.ChapterNumber,
                    OwnVerse = w.Verse.Number,
                    w.Verse.Label,
                    w.Id,
                    w.Position,
                    w.Surface,
                    w.Gloss,
                })
            .Skip(Math.Max(0, skip ?? 0))
            .Take(Math.Clamp(take ?? 50, 1, MostPerPage))
            .ToListAsync(cancellationToken);
        var stated = await VerseEndpoints.StatedNumbers(
            db, [.. page.Select(w => w.VerseId).Distinct()], cancellationToken);

        return new StrongOccurrenceListResponse(
            canonical,
            total,
            [.. byText.OrderByDescending(text => text.Count).ThenBy(text => text.Corpus, StringComparer.Ordinal)],
            [
                .. page.Select(w => new StrongOccurrenceResponse(
                    w.Corpus,
                    w.CanonicalBook,
                    BookReferences.Name(w.CanonicalBook),
                    BookReferences.Slug(w.CanonicalBook),
                    w.CanonicalChapter,
                    w.CanonicalVerse,
                    w.Id,
                    w.Position,
                    w.Surface,
                    w.Gloss)
                {
                    Printed = VerseEndpoints.Printed(
                        stated.GetValueOrDefault(w.VerseId),
                        w.OwnBook, w.OwnChapter, w.OwnVerse, w.Label,
                        w.CanonicalBook, w.CanonicalChapter, w.CanonicalVerse),
                }),
            ]);
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
                    "Name the text whose renderings you want, as ?corpus=KJV. GET /v1/corpora lists them."));
            }

            if (await canon.Text(corpus, cancellationToken) is not { } text)
            {
                return Results.NotFound(new ProblemResponse($"There is no text \"{corpus}\"."));
            }

            // Counted over one edition of the original, the one the text is most fully joined to.
            // G26 stands 116 times in each of the Greek editions the King James is linked to, and
            // counting across them reported every rendering three times over; counting the
            // translation's own tagged words as well reported more places reached than exist.
            var witness = LinkedOriginals.Primary(await LinkedOriginals.Of(db, text.Id, cancellationToken))
                .FirstOrDefault(original => WritesNumber(original.Language, canonical));
            var witnessId = witness?.Id ?? 0;

            var occurrences = await db.Words.CountAsync(
                w => w.StrongNumber == canonical && w.TextId == witnessId, cancellationToken);

            var sides = db.LinkWords
                .Where(side => side.Word!.StrongNumber == canonical
                               && side.Word.TextId == witnessId
                               && (side.Link!.FromTextId == text.Id || side.Link.ToTextId == text.Id)
                               && (side.Link!.Relation == LinkRelation.Renders
                                   || side.Link.Relation == LinkRelation.Equals));

            var reached = await sides
                .Select(side => side.WordId)
                .Distinct()
                .CountAsync(cancellationToken);

            // What the count rests on: a publisher's tagging and a statistical aligner are different
            // witnesses to the same number, and a reader citing it has to be able to say which.
            var methods = await sides
                .Select(side => new { side.LinkId, side.Link!.Method })
                .Distinct()
                .GroupBy(link => link.Method)
                .Select(g => new { Method = g.Key, Links = g.Count() })
                .ToListAsync(cancellationToken);

            var counted = await Renderings(
                db, [canonical], text.Id, Math.Clamp(take ?? 40, 1, MostPerPage), cancellationToken);

            return Results.Ok(new StrongRenderingsResponse(
                canonical,
                text.Slug,
                occurrences,
                reached,
                occurrences - reached,
                counted.GetValueOrDefault(canonical, []))
            {
                Witness = witness?.Slug,
                Methods =
                [
                    .. methods
                        .OrderByDescending(row => row.Links)
                        .Select(row => new TextLinkMethodResponse(EnumSpelling.Of(row.Method), row.Links)),
                ],
            });
        }).RequireRateLimiting(RateLimits.Expensive);

    /// <summary>
    /// Whether an edition in this language can carry the number at all: a Hebrew number stands only
    /// in a Hebrew or Aramaic text and a Greek one only in a Greek text.
    /// </summary>
    private static bool WritesNumber(string language, string number) =>
        number.StartsWith('G') ? language == "grc" : language is "hbo" or "arc";

    /// <summary>
    /// How many words of the corpus carry each of these numbers, in every witness that tags it —
    /// the count the entry page's own list of occurrences totals to. A number no word carries is
    /// absent, and reads as zero.
    /// </summary>
    internal static Task<Dictionary<string, int>> Occurrences(
        AppDbContext db,
        IReadOnlyCollection<string> numbers,
        CancellationToken cancellationToken) =>
        db.Words
            .Where(w => w.StrongNumber != null && numbers.Contains(w.StrongNumber))
            .GroupBy(w => w.StrongNumber!)
            .Select(g => new { Number = g.Key, Count = g.Count() })
            .ToDictionaryAsync(row => row.Number, row => row.Count, cancellationToken);

    /// <summary>
    /// The commonest <paramref name="take"/> phrases for each number (<see cref="StrongRenderingCounts"/>).
    /// A text the load has counted ahead is read from what it counted, which is forty rows off one
    /// index where counting them reads every link of every occurrence; any other text, or a deeper
    /// list than the load keeps, is counted as it is asked.
    /// </summary>
    internal static async Task<Dictionary<string, IList<StrongRenderingResponse>>> Renderings(
        AppDbContext db,
        IReadOnlyCollection<string> numbers,
        int textId,
        int take,
        CancellationToken cancellationToken)
    {
        if (numbers.Count == 0)
        {
            return [];
        }

        var counted = take <= StrongRenderingCounts.CardRenderings
                      && await db.StrongRenderings.AnyAsync(r => r.TextId == textId, cancellationToken)
            ? await db.StrongRenderings
                .Where(r => r.TextId == textId && numbers.Contains(r.StrongNumber) && r.Rank <= take)
                .OrderBy(r => r.StrongNumber).ThenBy(r => r.Rank)
                .Select(r => new StrongRenderingCount(r.StrongNumber, r.Rank, r.Phrase, r.Uses))
                .ToListAsync(cancellationToken)
            : await StrongRenderingCounts.Count(db, textId, numbers, take, cancellationToken);

        return counted
            .GroupBy(row => row.Number)
            .ToDictionary(
                group => group.Key,
                IList<StrongRenderingResponse> (group) =>
                    [.. group.OrderBy(row => row.Rank).Select(row => new StrongRenderingResponse(row.Phrase, row.Uses))]);
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
            .Include(g => g.People)
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
            stated.Origin?.Name,
            stated.People?.Slug,
            stated.People?.Name);
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
/// <param name="PeopleSlug">
/// The people this lexeme names, as a page. This is the near end the table was written without,
/// because until a people was a kind there was nothing for <em>the Moabites</em> to be — and it is
/// the end a reader hovering <em>моавітяни</em> actually wants.
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
    string? EntityName,
    string? PeopleSlug,
    string? PeopleName);

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
    StrongGentilicResponse? Gentilic)
{
    /// <summary>
    /// How many words of the corpus carry this number, in every witness that tags it — what the
    /// entry page's list of occurrences totals to. Stated on the lexicon's list, for its cards;
    /// null on a single entry, whose page counts its occurrences itself.
    /// </summary>
    public int? Occurrences { get; init; }

    /// <summary>
    /// The commonest few phrases <see cref="StrongListResponse.Corpus"/> puts where this number
    /// stands, commonest first, counted as the entry page's renderings are. Empty where that text
    /// renders it nowhere; null on a single entry.
    /// </summary>
    public IList<StrongRenderingResponse>? Renderings { get; init; }
}

internal record StrongListResponse(int Total, IList<StrongEntryResponse> Items)
{
    /// <summary>The text whose renderings the cards quote, as its canonical slug.</summary>
    public string? Corpus { get; init; }
}

/// <param name="Chapter">The chapter of the shared frame the word's verse stands in.</param>
/// <param name="Verse">The verse of the shared frame, which is where a link opens and a quote is read.</param>
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
    string? Gloss)
{
    /// <summary>
    /// The address the word's own text gives its verse, where that is not the frame's: BHSA's
    /// <c>3:19</c> for Malachi 4:1, <c>3:1</c> for a psalm's title. Null where the two agree.
    /// </summary>
    public IList<string>? Printed { get; init; }
}

/// <param name="ByText">How many of <paramref name="Total"/> stand in each text, most first.</param>
internal record StrongOccurrenceListResponse(
    string StrongNumber,
    int Total,
    IList<StrongTextCountResponse> ByText,
    IList<StrongOccurrenceResponse> Items);

internal record StrongTextCountResponse(string Corpus, int Count);

/// <param name="Occurrences">Every word carrying this number in the edition the counts are made over.</param>
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
    IList<StrongRenderingResponse> Renderings)
{
    /// <summary>
    /// The edition of the original the counts are made over, as its slug. Null where the text is
    /// linked to no edition that can carry the number.
    /// </summary>
    public string? Witness { get; init; }

    /// <summary>What made the links the renderings are counted from, and how many each made.</summary>
    public IList<TextLinkMethodResponse> Methods { get; init; } = [];
}

internal record StrongRenderingResponse(string Text, int Count);
