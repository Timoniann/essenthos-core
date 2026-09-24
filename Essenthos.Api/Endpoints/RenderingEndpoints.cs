using System.Data;
using Essenthos.Core.Corpus;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities.Enums;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;

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
/// </summary>
internal static class RenderingEndpoints
{
    /// <summary>
    /// The originals a witness lists by name. Past this the rest are counted together: a word like
    /// <em>the</em> stands on hundreds of numbers and a list of all of them is noise.
    /// </summary>
    internal const int MostOriginals = 40;

    /// <summary>Places quoted under each original — enough to read, not a concordance of its own.</summary>
    internal const int Samples = 3;

    public static void MapRenderings(this IEndpointRouteBuilder routes)
    {
        routes.MapGet("/renderings", async (
            [FromQuery] string? corpus,
            [FromQuery] string? word,
            [FromQuery] bool? exact,
            AppDbContext db,
            ICanonIndex canon,
            WordForms forms,
            CancellationToken cancellationToken) =>
        {
            if (corpus is not { Length: > 0 })
            {
                return Results.BadRequest(new ProblemResponse(
                    "Name the translation the word is read in, as ?corpus=KJV. GET /v1/corpora lists them."));
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
            var witnesses = matched.Count == 0
                ? []
                : await Behind(db, text.Id, [.. matched.Select(form => form.Text)], cancellationToken);

            return Results.Ok(new WordRenderingsResponse(
                text.Slug,
                terms[0],
                exact ?? false,
                index.Stems,
                [.. matched.Select(form => new WordFormResponse(form.Text, form.Count))],
                matched.Sum(form => form.Count),
                witnesses));
        });
    }

    /// <summary>
    /// What stands behind these spellings of one text, in every original it is linked to.
    ///
    /// The links are read once, as rows — one per occurrence and word of an original it is linked
    /// to — and counted here, because the same rows answer four questions (per original, per
    /// method, per witness, and which places to quote) and asking Postgres four times would walk
    /// the link table four times. The rows are few: <em>love</em> in the King James is some two
    /// thousand, and even <em>the</em> is a few hundred thousand narrow ones.
    /// </summary>
    internal static async Task<IList<WordWitnessResponse>> Behind(
        AppDbContext db,
        int textId,
        IReadOnlyCollection<string> spellings,
        CancellationToken cancellationToken)
    {
        var originals = await Originals(db, textId, cancellationToken);
        if (originals.Count == 0)
        {
            return [];
        }

        var witnessIds = originals.Select(t => t.Id).ToArray();
        var rows = await Rows(db, textId, spellings, witnessIds, cancellationToken);

        var perBook = await db.Words
            .Where(w => w.TextId == textId && spellings.Contains(w.NormalisedText!) && !w.Elided)
            .GroupBy(w => w.Verse!.Book!.CanonicalOrdinal)
            .Select(g => new { Ordinal = g.Key, Count = g.Count() })
            .ToDictionaryAsync(row => row.Ordinal, row => row.Count, cancellationToken);

        var held = (await db.Books
                .Where(b => witnessIds.Contains(b.TextId))
                .Select(b => new { b.TextId, b.CanonicalOrdinal })
                .ToListAsync(cancellationToken))
            .ToLookup(b => b.TextId, b => b.CanonicalOrdinal);

        var credits = await Credits(db, textId, rows, cancellationToken);

        var numbers = rows.Select(row => row.Number).OfType<string>().Distinct().ToList();
        var entries = await db.StrongEntries
            .Where(e => numbers.Contains(e.StrongNumber))
            .Select(e => new { e.StrongNumber, e.Lemma, e.Transliteration, e.Definition })
            .ToDictionaryAsync(e => e.StrongNumber, cancellationToken);

        var byWitness = rows.ToLookup(row => row.Witness);

        return [.. originals
            .Select(witness =>
            {
                var mine = byWitness[witness.Id].ToList();
                var occurrences = held[witness.Id].Distinct().Sum(ordinal => perBook.GetValueOrDefault(ordinal));

                var listed = mine
                    .GroupBy(row => (row.Number, Lemma: row.Number is null ? row.Lemma : null))
                    .Select(group =>
                    {
                        var entry = group.Key.Number is { } number ? entries.GetValueOrDefault(number) : null;
                        return new WordOriginalResponse(
                            group.Key.Number,
                            entry?.Lemma ?? group.Key.Lemma ?? group.Select(row => row.Lemma).FirstOrDefault(),
                            entry?.Transliteration,
                            entry?.Definition,
                            group.Select(row => row.Hit).Distinct().Count(),
                            group.Select(row => row.Link).Distinct().Count(),
                            Methods(group),
                            [.. group
                                .DistinctBy(row => (row.Ordinal, row.Chapter, row.Verse))
                                .OrderBy(row => row.Ordinal).ThenBy(row => row.Chapter).ThenBy(row => row.Verse)
                                .ThenBy(row => row.Hit)
                                .Take(Samples)
                                .Select(row => new WordPlaceResponse(
                                    new BookRefResponse(
                                        row.Ordinal, BookReferences.Name(row.Ordinal), BookReferences.Slug(row.Ordinal)),
                                    row.Chapter,
                                    row.Verse,
                                    row.Surface))]);
                    })
                    .OrderByDescending(original => original.Occurrences)
                    .ThenBy(original => original.StrongNumber ?? original.Lemma, StringComparer.Ordinal)
                    .ToList();

                // A word the edition gives neither a number nor a dictionary form is counted, but
                // there is nothing to name it by.
                var named = listed.Where(original => original.StrongNumber is not null || original.Lemma is { Length: > 0 });
                var rest = listed.Except(named.Take(MostOriginals)).ToList();

                return new WordWitnessResponse(
                    witness.Slug,
                    witness.Name,
                    witness.Language,
                    occurrences,
                    mine.Select(row => row.Hit).Distinct().Count(),
                    mine.Select(row => row.Link).Distinct().Count(),
                    Methods(mine),
                    credits[witness.Id].ToList(),
                    [.. named.Take(MostOriginals)],
                    rest.Count,
                    rest.Sum(original => original.Occurrences))
                {
                    Kind = EnumSpelling.Of(witness.Kind),
                };
            })
            .Where(witness => witness.Reached > 0)];
    }

    /// <summary>
    /// The originals a text is linked to, in the order a reader is likeliest to mean them: the
    /// critical editions first, then whichever the text is most fully joined to. The first of each
    /// language is the one a single count is made against, so that the Strong page's renderings and
    /// this page's first answer are counted over the same edition.
    /// </summary>
    internal static async Task<List<LinkedOriginal>> Originals(
        AppDbContext db,
        int textId,
        CancellationToken cancellationToken)
    {
        var neighbours = await StrongEndpoints.Neighbours(db, textId, cancellationToken);
        var originals = await db.Texts
            .Where(t => neighbours.Contains(t.Id) && t.Kind != TextKind.Translation)
            .Select(t => new LinkedOriginal(
                t.Id,
                t.Slug,
                t.Name,
                t.Language,
                t.Kind,
                db.Links.Count(l => l.FromTextId == textId && l.ToTextId == t.Id)
                + db.Links.Count(l => l.FromTextId == t.Id && l.ToTextId == textId)))
            .ToListAsync(cancellationToken);

        return
        [
            .. originals
                .OrderBy(original => original.Kind == TextKind.CriticalEdition ? 0 : 1)
                .ThenByDescending(original => original.Links)
                .ThenBy(original => original.Slug, StringComparer.Ordinal),
        ];
    }

    /// <summary>The first of each language in <see cref="Originals"/>.</summary>
    internal static List<LinkedOriginal> Primary(IEnumerable<LinkedOriginal> originals) =>
        [.. originals.GroupBy(original => original.Language).Select(language => language.First())];

    private static IList<TextLinkMethodResponse> Methods(IEnumerable<LinkedRow> rows) =>
    [
        .. rows
            .DistinctBy(row => row.Link)
            .GroupBy(row => row.Method)
            .Select(group => new TextLinkMethodResponse(group.Key, group.Count()))
            .OrderByDescending(method => method.Links)
            .ThenBy(method => method.Method, StringComparer.Ordinal),
    ];

    /// <summary>
    /// Whose links these are, per witness, by the datasets the source strings belong to — the same
    /// credit a text's own page gives the links it is joined by.
    /// </summary>
    private static async Task<ILookup<int, TextCreditResponse>> Credits(
        AppDbContext db,
        int textId,
        IReadOnlyCollection<LinkedRow> rows,
        CancellationToken cancellationToken)
    {
        var links = rows.Select(row => row.Link).Distinct().ToArray();
        var sources = await db.Links
            .Where(l => links.Contains(l.Id))
            .GroupBy(l => new { Other = l.FromTextId == textId ? l.ToTextId : l.FromTextId, l.Source })
            .Select(g => new { g.Key.Other, g.Key.Source })
            .ToListAsync(cancellationToken);

        return sources
            .Select(row => new { row.Other, Dataset = Datasets.Match(row.Source) })
            .Where(row => row.Dataset is not null)
            .DistinctBy(row => (row.Other, row.Dataset!.Id))
            .ToLookup(row => row.Other, row => new TextCreditResponse(row.Dataset!.Name, row.Dataset.Author));
    }

    /// <summary>
    /// One row per occurrence of the spellings and word of an original a renders or equals link
    /// puts opposite it. The number is the word's own, or the one carried beside it where the
    /// witness states none on the word — the Septuagint's are all of that kind — and the lemma
    /// stands in where there is neither.
    /// </summary>
    private static async Task<List<LinkedRow>> Rows(
        AppDbContext db,
        int textId,
        IReadOnlyCollection<string> spellings,
        int[] witnesses,
        CancellationToken cancellationToken)
    {
        const string sql =
            """
            SELECT h.id, h.text, b.canonical_ordinal, v.chapter_number, v.number,
                   l.id, l.method, ow.text_id,
                   coalesce(ow.strong_number,
                            (SELECT min(ws.number) FROM word_strong ws WHERE ws.word_id = ow.id)),
                   ow.lemma
            FROM word h
            JOIN verse v ON v.id = h.verse_id
            JOIN book b ON b.id = v.book_id
            JOIN link_word s ON s.word_id = h.id
            JOIN link l ON l.id = s.link_id AND l.relation IN ('renders', 'equals')
            JOIN link_word o ON o.link_id = l.id AND o.side <> s.side
            JOIN word ow ON ow.id = o.word_id AND ow.text_id = ANY(@witnesses)
            WHERE h.text_id = @text
              AND h.normalised_text = ANY(@spellings)
              AND NOT h.elided
            """;

        var connection = (NpgsqlConnection)db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken);
        }

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("text", textId);
        command.Parameters.AddWithValue("spellings", spellings.ToArray());
        command.Parameters.AddWithValue("witnesses", witnesses);

        var rows = new List<LinkedRow>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new LinkedRow(
                reader.GetInt64(0),
                reader.GetString(1),
                reader.GetInt32(2),
                reader.GetInt32(3),
                reader.GetInt32(4),
                reader.GetInt64(5),
                reader.GetString(6),
                reader.GetInt32(7),
                reader.IsDBNull(8) ? null : reader.GetString(8),
                reader.IsDBNull(9) ? null : reader.GetString(9)));
        }

        return rows;
    }

    internal sealed record LinkedOriginal(int Id, string Slug, string Name, string Language, TextKind Kind, int Links);

    private sealed record LinkedRow(
        long Hit,
        string Surface,
        int Ordinal,
        int Chapter,
        int Verse,
        long Link,
        string Method,
        int Witness,
        string? Number,
        string? Lemma);
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
    IList<WordWitnessResponse> Witnesses);

internal record WordFormResponse(string Text, int Count);

/// <param name="Occurrences">
/// The translation's occurrences in the books this witness holds: the denominator. A Greek New
/// Testament is not asked about Genesis.
/// </param>
/// <param name="Reached">How many of those are linked to some word of this witness.</param>
/// <param name="Links">The links those are counted from.</param>
/// <param name="Methods">What made the links, and how many each made.</param>
/// <param name="Credits">Whose links they are.</param>
/// <param name="OtherOriginals">Originals past the ones listed, and words the edition names by nothing, counted together.</param>
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
    IList<WordPlaceResponse> Samples);

/// <param name="Text">The translation's word as it is printed there.</param>
internal record WordPlaceResponse(BookRefResponse Book, int Chapter, int Verse, string Text);
