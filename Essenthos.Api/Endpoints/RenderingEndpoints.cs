using Essenthos.Core.Corpus;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Database.Entities.Enums;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Essenthos.Core.Endpoints;

/// <summary>
/// A concordance read backwards: for one word of a translation, which words of the originals stand
/// behind it, and how often each.
///
/// "The Greek has four words for love" is the sentence every study of the word starts from, and the
/// links answer it exactly — in the King James's New Testament <em>love</em> stands for ἀγαπάω and
/// ἀγάπη most of the time, φιλέω some of it, and φιλαδελφία a few times, and each of those is a
/// count with the places behind it. <c>/strong/{number}/renderings</c> is the same question asked
/// from the other end.
///
/// <para>
/// Every count carries its denominator and its provenance. A witness counts the translation's
/// occurrences in the books that witness holds, says how many of them are linked to it at all, and
/// says by what the links were made and whose they are — because a count made from a publisher's
/// tagging and one made by a statistical aligner are different claims, and a reader citing one has
/// to be able to tell which he is citing.
/// </para>
///
/// <para>
/// The unit is the translation's word. One English word linked to two Hebrew words — <em>love</em>
/// on אָהַב and on the object marker beside it, because the source tagged the phrase — counts for
/// both, so the shares of a witness can sum past the whole, and the response says how many
/// occurrences each original accounts for rather than pretending they partition.
/// </para>
///
/// <para>
/// The counts are made at build, per spelling (<see cref="WordRendering"/>, <see cref="WordRenderingWitness"/>), and a
/// request adds up the rows of the spellings its word gathers. A word prints one spelling, so occurrences and places add
/// up; a link holding words of two spellings that one request can gather is named on both rows and counted once.
/// </para>
/// </summary>
internal static class RenderingEndpoints
{
    /// <summary>
    /// The originals a witness lists by name. Past this the rest are counted together: a word like
    /// <em>the</em> stands on hundreds of numbers and a list of all of them is noise.
    /// </summary>
    internal const int MostOriginals = 40;

    /// <summary>Places quoted under each original — enough to read, not a concordance of its own.</summary>
    internal const int Samples = RenderingPlaces.Samples;

    /// <summary>The most originals, or places, one page answers.</summary>
    private const int MostPerPage = 200;

    /// <summary>The places one page of an original's places answers unless asked otherwise.</summary>
    private const int PlacesPerPage = 20;

    public static void MapRenderings(this IEndpointRouteBuilder routes)
    {
        routes.MapGet("/renderings", async (
            [FromQuery] string? corpus,
            [FromQuery] string? word,
            [FromQuery] bool? exact,
            [FromQuery] string? prose,
            [FromQuery] string? witness,
            [FromQuery] int? skip,
            [FromQuery] int? take,
            AppDbContext db,
            ICanonIndex canon,
            WordForms forms,
            CancellationToken cancellationToken) =>
        {
            if (await Asked(corpus, word, canon, cancellationToken) is not ({ } text, { } typed))
            {
                return Refused(corpus, word);
            }

            int? only = null;
            if (witness is { Length: > 0 })
            {
                if (await canon.Text(witness, cancellationToken) is not { } edition)
                {
                    return Results.NotFound(new ProblemResponse(
                        $"There is no text \"{witness}\". Ask /v1/corpora for the ones this corpus holds."));
                }

                only = edition.Id;
            }

            var index = await forms.Of(text.Id, cancellationToken);
            var matched = index.Matching(typed, exact ?? false);
            var witnesses = matched.Count == 0
                ? []
                : await Behind(db, text.Id, [.. matched.Select(form => form.Text)], cancellationToken, prose, only,
                    Math.Max(skip ?? 0, 0), Math.Clamp(take ?? MostOriginals, 1, MostPerPage));

            return Results.Ok(new WordRenderingsResponse(
                text.Slug,
                typed,
                exact ?? false,
                index.Stems,
                [.. matched.Select(Form)],
                matched.Sum(form => form.Count),
                witnesses)
            {
                Family = Family(index, typed, exact ?? false),
            });
        }).RequireRateLimiting(RateLimits.Expensive);

        routes.MapGet("/renderings/places", async (
            [FromQuery] string? corpus,
            [FromQuery] string? word,
            [FromQuery] bool? exact,
            [FromQuery] string? witness,
            [FromQuery] string? strong,
            [FromQuery] string? lemma,
            [FromQuery] int? skip,
            [FromQuery] int? take,
            AppDbContext db,
            ICanonIndex canon,
            WordForms forms,
            CancellationToken cancellationToken) =>
        {
            if (await Asked(corpus, word, canon, cancellationToken) is not ({ } text, { } typed))
            {
                return Refused(corpus, word);
            }

            if (witness is not { Length: > 0 } || (strong is not { Length: > 0 } && lemma is not { Length: > 0 }))
            {
                return Results.BadRequest(new ProblemResponse(
                    "Name the edition and the original, as ?witness=BHSA&strong=H1 (or &lemma= for a word with no number)."));
            }

            if (await canon.Text(witness, cancellationToken) is not { } edition)
            {
                return Results.NotFound(new ProblemResponse(
                    $"There is no text \"{witness}\". Ask /v1/corpora for the ones this corpus holds."));
            }

            var number = strong is { Length: > 0 } ? strong.ToUpperInvariant() : null;
            var index = await forms.Of(text.Id, cancellationToken);
            var matched = index.Matching(typed, exact ?? false);
            var (total, items) = await Places(db, text.Id, [.. matched.Select(form => form.Text)], edition.Id, number, number is null ? lemma : null,
                Math.Max(skip ?? 0, 0), Math.Clamp(take ?? PlacesPerPage, 1, MostPerPage), cancellationToken);

            return Results.Ok(new WordPlacesResponse(text.Slug, typed, exact ?? false, edition.Slug, number, number is null ? lemma : null, total, items));
        }).RequireRateLimiting(RateLimits.Expensive);

        routes.MapGet("/renderings/occurrences", async (
            [FromQuery] string? corpus,
            [FromQuery] string? word,
            [FromQuery] bool? exact,
            [FromQuery] int? skip,
            [FromQuery] int? take,
            AppDbContext db,
            ICanonIndex canon,
            WordForms forms,
            CancellationToken cancellationToken) =>
        {
            if (corpus is not { Length: > 0 })
            {
                return Results.BadRequest(new ProblemResponse(
                    "Name the text the word is read in, as ?corpus=KJV. GET /v1/corpora lists them."));
            }

            var terms = SearchTerms.Parse(word);
            if (terms.Length != 1)
            {
                return Results.BadRequest(new ProblemResponse(
                    "Ask about one word, as ?word=love. A phrase is not one word of the text."));
            }

            if (await canon.Text(corpus, cancellationToken) is not { } text)
            {
                return Results.NotFound(new ProblemResponse(
                    $"There is no text \"{corpus}\". Ask /v1/corpora for the ones this corpus holds."));
            }

            var index = await forms.Of(text.Id, cancellationToken);
            var matched = index.Matching(terms[0], exact ?? false);
            var page = await Occurrences(
                db, text.Id, terms[0], [.. matched.Select(form => form.Text)], skip, take, cancellationToken);

            return Results.Ok(new WordOccurrencesResponse(
                text.Slug,
                terms[0],
                exact ?? false,
                index.Stems,
                [.. matched.Select(Form)],
                page.Total,
                page.Items)
            {
                Family = Family(index, terms[0], exact ?? false),
            });
        }).RequireRateLimiting(RateLimits.Expensive);
    }

    /// <summary>
    /// Every form sharing the word's stem, when an exact ask counts only one of them, so that a reader can move to another;
    /// null when the forms counted are already all of them.
    /// </summary>
    internal static IList<WordFormResponse>? Family(FormIndex index, string typed, bool exact) =>
        exact ? [.. index.Matching(typed, exact: false).Select(Form)] : null;

    private static WordFormResponse Form(WordForm form) => new(form.Text, form.Count);

    /// <summary>The text and the one word a request asks about; null for either it does not name well.</summary>
    private static async Task<(TextEntry? Text, string? Word)> Asked(string? corpus, string? word, ICanonIndex canon, CancellationToken cancellationToken)
    {
        var terms = SearchTerms.Parse(word);
        if (corpus is not { Length: > 0 } || terms.Length != 1)
        {
            return (null, null);
        }

        return (await canon.Text(corpus, cancellationToken), terms[0]);
    }

    private static IResult Refused(string? corpus, string? word) =>
        corpus is not { Length: > 0 }
            ? Results.BadRequest(new ProblemResponse(
                "Name the translation the word is read in, as ?corpus=KJV. GET /v1/corpora lists them."))
            : SearchTerms.Parse(word).Length != 1
                ? Results.BadRequest(new ProblemResponse(
                    "Ask about one word, as ?word=love. A phrase is not one word of the text."))
                : Results.NotFound(new ProblemResponse(
                    $"There is no text \"{corpus}\". Ask /v1/corpora for the ones this corpus holds."));

    /// <summary>
    /// One page of the places a text prints these spellings, in canonical order, by the frame's address:
    /// the same shape as a Strong number's places, because a list of where a word stands is the same
    /// list whichever end it was asked from. The total is every occurrence, so it adds up to the forms.
    /// </summary>
    internal static Task<StrongOccurrenceListResponse> Occurrences(
        AppDbContext db,
        int textId,
        string word,
        IReadOnlyCollection<string> spellings,
        int? skip,
        int? take,
        CancellationToken cancellationToken) =>
        StrongEndpoints.OccurrencePage(
            db,
            word,
            db.Words.Where(w => w.TextId == textId && spellings.Contains(w.NormalisedText!) && !w.Elided),
            skip,
            take,
            cancellationToken);

    /// <summary>
    /// What stands behind these spellings of one text, in every original it is linked to (or the one
    /// <paramref name="witness"/> names), each edition's originals from <paramref name="skip"/> on, at most
    /// <paramref name="take"/> of them: the rows counted at build for each spelling, added up.
    /// </summary>
    internal static async Task<IList<WordWitnessResponse>> Behind(
        AppDbContext db,
        int textId,
        IReadOnlyCollection<string> spellings,
        CancellationToken cancellationToken,
        string? prose = null,
        int? witness = null,
        int skip = 0,
        int take = MostOriginals)
    {
        var editions = (await db.WordRenderingWitnesses.AsNoTracking()
                .Where(r => r.TextId == textId && spellings.Contains(r.Form) && (witness == null || r.WitnessId == witness))
                .ToListAsync(cancellationToken))
            .GroupBy(r => r.WitnessId)
            .Where(g => g.Sum(r => r.Reached) > 0)
            .OrderBy(g => g.Min(r => r.Rank))
            .ToList();
        if (editions.Count == 0)
        {
            return [];
        }

        var witnessIds = editions.Select(g => g.Key).ToArray();
        var texts = await db.Texts.AsNoTracking()
            .Where(t => witnessIds.Contains(t.Id))
            .Select(t => new { t.Id, t.Slug, t.Name, t.Language, t.Kind })
            .ToDictionaryAsync(t => t.Id, cancellationToken);
        var numbered = editions.SelectMany(g => g).SelectMany(r => r.Sources).Distinct().ToArray();
        var sources = await db.WordRenderingSources.AsNoTracking()
            .Where(s => s.TextId == textId && numbered.Contains(s.Number))
            .ToDictionaryAsync(s => s.Number, s => s.Source, cancellationToken);

        var rows = (await db.WordRenderings.AsNoTracking()
                .Where(r => r.TextId == textId && spellings.Contains(r.Form) && witnessIds.Contains(r.WitnessId))
                .ToListAsync(cancellationToken))
            .ToLookup(r => r.WitnessId);

        var pages = editions.Select(edition =>
        {
            var listed = rows[edition.Key]
                .GroupBy(r => (r.StrongNumber, Lemma: r.StrongNumber is null ? r.Lemma : null))
                .Select(group => new Original(group.Key.StrongNumber, group.Key.Lemma, [.. group]))
                .OrderByDescending(original => original.Occurrences)
                .ThenBy(original => original.StrongNumber ?? original.Lemma, StringComparer.Ordinal)
                .ToList();

            // A word the edition gives neither a number nor a dictionary form is counted, but
            // there is nothing to name it by.
            var named = listed.Where(original => original.StrongNumber is not null || original.Lemma is { Length: > 0 }).ToList();
            var shown = named.Take(skip + take).ToHashSet();
            var rest = listed.Where(original => !shown.Contains(original)).ToList();
            return (Edition: edition, Named: named, Page: named.Skip(skip).Take(take).ToList(), Others: rest);
        }).ToList();

        var onPages = pages.SelectMany(p => p.Page).ToList();
        var numbers = onPages.Select(original => original.StrongNumber).OfType<string>().Distinct().ToList();
        var entries = await db.StrongEntries
            .Where(e => numbers.Contains(e.StrongNumber))
            .Select(e => new { e.StrongNumber, e.Lemma, e.Transliteration, e.Definition })
            .ToDictionaryAsync(e => e.StrongNumber, cancellationToken);
        var glosses = await StrongEndpoints.Glosses(db, numbers, prose, cancellationToken);
        var lists = await Lists(db, onPages.Where(original => original.Rows.Count > 1).SelectMany(original => original.Rows), cancellationToken);

        return [.. pages.Select(page =>
        {
            var info = texts[page.Edition.Key];
            var counted = Count(page.Edition.Select(r => (r.Links, r.Methods, r.Shared)));
            var credits = page.Edition.SelectMany(r => r.Sources).Distinct().Order()
                .Select(number => Datasets.Credited(sources.GetValueOrDefault(number)))
                .OfType<Datasets.Dataset>()
                .DistinctBy(dataset => dataset.Id)
                .Select(dataset => new TextCreditResponse(dataset.Name, dataset.Author))
                .ToList();

            return new WordWitnessResponse(
                info.Slug,
                info.Name,
                info.Language,
                page.Edition.Sum(r => r.Occurrences),
                page.Edition.Sum(r => r.Reached),
                counted.Links,
                counted.Methods,
                credits,
                [.. page.Page.Select(original =>
                {
                    var entry = original.StrongNumber is { } number ? entries.GetValueOrDefault(number) : null;
                    var originalCount = Count(original.Rows.Select(r => (r.Links, r.Methods, r.Shared)));
                    var places = Merged(original.Rows, lists);
                    return new WordOriginalResponse(
                        original.StrongNumber,
                        entry?.Lemma ?? original.Lemma,
                        entry?.Transliteration,
                        entry?.Definition,
                        original.Occurrences,
                        originalCount.Links,
                        originalCount.Methods,
                        [.. places.Take(Samples).Select(Place)])
                    {
                        Translation = original.StrongNumber is { } glossed ? glosses.GetValueOrDefault(glossed) : null,
                        Places = original.Rows.Count == 1 ? original.Rows[0].Places : places.Count,
                    };
                })],
                page.Others.Count,
                page.Others.Sum(original => original.Occurrences))
            {
                Kind = EnumSpelling.Of(info.Kind),
                TotalOriginals = page.Named.Count,
            };
        })];
    }

    /// <summary>
    /// One page of the places where <paramref name="number"/> (or, with none, <paramref name="lemma"/>) of one edition stands
    /// behind these spellings, in canonical order, each place once, and how many there are.
    /// </summary>
    internal static async Task<(int Total, IList<WordPlaceResponse> Items)> Places(
        AppDbContext db,
        int textId,
        IReadOnlyCollection<string> spellings,
        int witness,
        string? number,
        string? lemma,
        int skip,
        int take,
        CancellationToken cancellationToken)
    {
        var rows = await db.WordRenderings.AsNoTracking()
            .Where(r => r.TextId == textId && spellings.Contains(r.Form) && r.WitnessId == witness
                        && (number == null ? r.StrongNumber == null && r.Lemma == lemma : r.StrongNumber == number))
            .ToListAsync(cancellationToken);
        var places = Merged(rows, await Lists(db, rows, cancellationToken));
        return (places.Count, [.. places.Skip(skip).Take(take).Select(Place)]);
    }

    /// <summary>One original of one edition, as the rows of the spellings asked about count it.</summary>
    private sealed class Original(string? number, string? lemma, List<WordRendering> rows)
    {
        public string? StrongNumber { get; } = number;

        /// <summary>The key's lemma where there is no number; beside a number, the lemma at the first place of the spellings asked about.</summary>
        public string? Lemma { get; } = number is null
            ? lemma
            : rows.OrderBy(r => RenderingPlaces.Read(r.Samples).Select(p => (p.Book, p.Chapter, p.Verse)).FirstOrDefault())
                .ThenBy(r => r.Form, StringComparer.Ordinal)
                .First().Lemma;

        public List<WordRendering> Rows { get; } = rows;

        public int Occurrences { get; } = rows.Sum(r => r.Occurrences);
    }

    /// <summary>
    /// Links and their methods over several spellings' rows: a link a row names as shared is counted once, however many of
    /// the rows count it.
    /// </summary>
    private static (int Links, IList<TextLinkMethodResponse> Methods) Count(IEnumerable<(int Links, string Methods, string? Shared)> rows)
    {
        var links = 0;
        var methods = new Dictionary<string, int>(StringComparer.Ordinal);
        var seen = new HashSet<long>();
        foreach (var (count, written, shared) in rows)
        {
            links += count;
            foreach (var (method, made) in RenderingPlaces.ReadMethods(written))
            {
                methods[method] = methods.GetValueOrDefault(method) + made;
            }

            foreach (var (id, method) in RenderingPlaces.ReadLinks(shared))
            {
                if (!seen.Add(id))
                {
                    links--;
                    methods[method]--;
                }
            }
        }

        return (links, [
            .. methods.Where(m => m.Value > 0)
                .Select(m => new TextLinkMethodResponse(m.Key, m.Value))
                .OrderByDescending(method => method.Links)
                .ThenBy(method => method.Method, StringComparer.Ordinal),
        ]);
    }

    /// <summary>Every place of the rows that have more than their samples, by row.</summary>
    private static async Task<Dictionary<long, string>> Lists(AppDbContext db, IEnumerable<WordRendering> rows, CancellationToken cancellationToken)
    {
        var ids = rows.Where(r => r.Places > Samples).Select(r => r.Id).ToArray();
        return ids.Length == 0
            ? []
            : await db.WordRenderingPlaces.AsNoTracking()
                .Where(p => ids.Contains(p.RenderingId))
                .ToDictionaryAsync(p => p.RenderingId, p => p.Places, cancellationToken);
    }

    /// <summary>
    /// The places of several spellings' rows of one original in canonical order, each once: where two spellings stand at
    /// one place, the word of the spelling that sorts first, as an index over the spellings meets them.
    /// </summary>
    private static List<RenderingPlace> Merged(IReadOnlyCollection<WordRendering> rows, IReadOnlyDictionary<long, string> lists)
    {
        if (rows.Count == 1 && rows.First() is var only && only.Places <= Samples)
        {
            return RenderingPlaces.Read(only.Samples);
        }

        return
        [
            .. rows.OrderBy(r => r.Form, StringComparer.Ordinal)
                .SelectMany(r => RenderingPlaces.Read(lists.GetValueOrDefault(r.Id) ?? r.Samples))
                .GroupBy(p => p.At)
                .Select(g => g.First())
                .OrderBy(p => p.At),
        ];
    }

    private static WordPlaceResponse Place(RenderingPlace place) =>
        new(new BookRefResponse(place.Book, BookReferences.Name(place.Book), BookReferences.Slug(place.Book)), place.Chapter, place.Verse, place.Surface);
}

/// <param name="Word">The word as it was asked about.</param>
/// <param name="Stemmed">
/// Whether the text's language has a stemmer, so that <paramref name="Forms"/> gathers every
/// spelling sharing the word's stem. False means the spelling alone was matched.
/// </param>
/// <param name="Forms">The spellings counted, commonest first.</param>
/// <param name="Occurrences">Every occurrence of those spellings in the text.</param>
/// <param name="Witnesses">
/// Each original the text is linked to that stands behind at least one of them, the critical
/// editions first.
/// </param>
internal record WordRenderingsResponse(
    string Corpus,
    string Word,
    bool Exact,
    bool Stemmed,
    IList<WordFormResponse> Forms,
    int Occurrences,
    IList<WordWitnessResponse> Witnesses)
{
    /// <summary>With an exact ask, every spelling sharing the word's stem, commonest first; null otherwise.</summary>
    public IList<WordFormResponse>? Family { get; init; }
}

internal record WordFormResponse(string Text, int Count);

/// <param name="Word">The word as it was asked about.</param>
/// <param name="Stemmed">Whether the forms were gathered by a stem, or the spelling alone was matched.</param>
/// <param name="Forms">The spellings listed, commonest first.</param>
/// <param name="Total">Every occurrence of those spellings in the text.</param>
/// <param name="Items">One page of them, in canonical order.</param>
internal record WordOccurrencesResponse(
    string Corpus,
    string Word,
    bool Exact,
    bool Stemmed,
    IList<WordFormResponse> Forms,
    int Total,
    IList<StrongOccurrenceResponse> Items)
{
    /// <summary>With an exact ask, every spelling sharing the word's stem, commonest first; null otherwise.</summary>
    public IList<WordFormResponse>? Family { get; init; }
}

/// <param name="Occurrences">
/// The translation's occurrences in the books this witness holds: the denominator. A Greek New
/// Testament is not asked about Genesis.
/// </param>
/// <param name="Reached">How many of those are linked to some word of this witness.</param>
/// <param name="Links">The links those are counted from.</param>
/// <param name="Methods">What made the links, and how many each made.</param>
/// <param name="Credits">Whose links they are.</param>
/// <param name="Originals">One page of the originals named by a number or a lemma, the commonest first.</param>
/// <param name="OtherOriginals">Originals past this page, and words the edition names by nothing, counted together.</param>
/// <param name="OtherOccurrences">The occurrences those account for.</param>
internal record WordWitnessResponse(
    string Corpus,
    string Name,
    string Language,
    int Occurrences,
    int Reached,
    int Links,
    IList<TextLinkMethodResponse> Methods,
    IList<TextCreditResponse> Credits,
    IList<WordOriginalResponse> Originals,
    int OtherOriginals,
    int OtherOccurrences)
{
    /// <summary>What kind of text the witness is, spelled as the corpora list spells it.</summary>
    public string? Kind { get; init; }

    /// <summary>Every original named by a number or a lemma, of which <see cref="Originals"/> is one page.</summary>
    public int TotalOriginals { get; init; }
}

/// <param name="StrongNumber">Null where the witness's word carries none; the lemma then names it.</param>
/// <param name="Occurrences">The translation's occurrences this original stands behind.</param>
/// <param name="Links">The links those are counted from.</param>
/// <param name="Samples">The first few of those places, in canonical order.</param>
internal record WordOriginalResponse(
    string? StrongNumber,
    string? Lemma,
    string? Transliteration,
    string? Definition,
    int Occurrences,
    int Links,
    IList<TextLinkMethodResponse> Methods,
    IList<WordPlaceResponse> Samples)
{
    /// <summary>The entry's definition in the language <c>prose</c> asked for, where it has been translated.</summary>
    public StrongGlossTranslationResponse? Translation { get; init; }

    /// <summary>Every place it stands at, each once, of which <see cref="Samples"/> are the first.</summary>
    public int Places { get; init; }
}

/// <param name="Text">The translation's word as it is printed there.</param>
internal record WordPlaceResponse(BookRefResponse Book, int Chapter, int Verse, string Text);

/// <param name="Witness">The edition the original stands in.</param>
/// <param name="StrongNumber">The original, by its number; null where it is named by <paramref name="Lemma"/>.</param>
/// <param name="Total">Every place it stands behind the spellings, each once.</param>
/// <param name="Items">One page of them, in canonical order.</param>
internal record WordPlacesResponse(
    string Corpus,
    string Word,
    bool Exact,
    string Witness,
    string? StrongNumber,
    string? Lemma,
    int Total,
    IList<WordPlaceResponse> Items);
