using System.Diagnostics;
using Essenthos.Core.Corpus;
using Essenthos.Core.Database;
using Essenthos.Core.Loading;
using Essenthos.Core.Loading.Encyclopedia;
using Essenthos.Core.Products;
using Npgsql;
using NpgsqlTypes;

namespace Essenthos.Core.Indexes;

/// <summary>
/// The texts' indexes into the serving tables (never live): <c>strong_rendering</c>, <c>strong_reach</c>,
/// <c>strong_reach_method</c>, <c>strong_book_reach</c> and <c>strong_book</c> from the strong and occurrences indexes,
/// <c>text_form</c> from the forms, <c>entity_rendering</c> and <c>entity_search_spelling</c> from the spellings. Each
/// table's rows of the texts loaded are replaced in one transaction. What is counted is what the database steps count
/// (<see cref="StrongRenderingCounts"/>, <see cref="EntityRenderingLoader"/>): the
/// commonest phrases are ranked by the database, as before, so ties fall in its collation; the variety and the lexical
/// class are the same C#.
/// </summary>
internal static class IndexLoad
{
    public sealed record Outcome(string Table, long Rows, int Texts, TimeSpan Elapsed, string? Note = null)
    {
        public override string ToString() => $"{Table}: {Rows} rows of {Texts} texts in {Elapsed.TotalSeconds:0.0} s{(Note is null ? "" : $"; {Note}")}";
    }

    private sealed record Counted(
        int TextId,
        List<(string Number, string Phrase, int Uses)> Phrases,
        List<(string Number, int Witness, int Occurrences, int Reached, Variety Variety, bool? Lexical)> Reach,
        List<(string Number, string Method, int Links)> Methods,
        List<(string Number, int Book, int Reached)> Books);

    private static async Task<Dictionary<string, (int Id, string Language)>> Texts(NpgsqlConnection connection, CancellationToken cancellationToken)
    {
        var texts = new Dictionary<string, (int, string)>(StringComparer.Ordinal);
        await using var command = new NpgsqlCommand("SELECT slug, id, language FROM text", connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            texts[reader.GetString(0)] = (reader.GetInt32(1), reader.GetString(2));
        }

        return texts;
    }

    /// <summary>
    /// The Strong tables from the strong indexes of <paramref name="texts"/> and the occurrences of the editions they are
    /// counted over. The lexical class of a number reads the occurrences of every text of an original language.
    /// </summary>
    public static async Task<Outcome> Strong(string connectionString, ProductStore store, IReadOnlyCollection<string> texts, CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        return await Strong(connection, store, texts, cancellationToken, own: true);
    }

    /// <summary>The same on a connection whose transaction the caller holds and commits.</summary>
    public static Task<Outcome> Strong(NpgsqlConnection connection, ProductStore store, IReadOnlyCollection<string> texts, CancellationToken cancellationToken) =>
        Strong(connection, store, texts, cancellationToken, own: false);

    private static async Task<Outcome> Strong(NpgsqlConnection connection, ProductStore store, IReadOnlyCollection<string> texts, CancellationToken cancellationToken, bool own)
    {
        var clock = Stopwatch.StartNew();
        var ids = await Texts(connection, cancellationToken);

        var occurrences = new Dictionary<string, List<OccurrenceRow>>(StringComparer.Ordinal);
        foreach (var slug in ids.Keys.Where(slug => store.Exists(IndexProducts.Of(slug, IndexProducts.Occurrences))))
        {
            using var reader = store.Open(IndexProducts.Of(slug, IndexProducts.Occurrences), OccurrenceRow.Schema);
            occurrences[slug] = [.. reader.Rows()];
        }

        var lexical = StrongRenderingLoader.Lexical(occurrences
            .Where(o => Renderings.Original.Contains(ids[o.Key].Language))
            .SelectMany(o => o.Value)
            .Where(row => row.Pos is not null)
            .GroupBy(row => (row.Number, row.Pos!))
            .Select(g => (g.Key.Number, g.Key.Item2, g.Sum(row => row.Words))));

        // Each text is counted from its own index alone, so the texts are counted at once.
        var indexed = texts.Where(slug => ids.ContainsKey(slug) && store.Exists(IndexProducts.Of(slug, IndexProducts.Strong))).ToList();
        var countedBy = new Counted[indexed.Count];
        var primaries = new string[indexed.Count][];
        Parallel.For(0, indexed.Count, new ParallelOptions { CancellationToken = cancellationToken }, i =>
        {
            var text = indexed[i];
            using var reader = store.Open(IndexProducts.Of(text, IndexProducts.Strong), StrongIndexRow.Schema);
            var notes = reader.Header!.Notes;
            string? Note(string key) => notes.LastOrDefault(n => n.Key == key).Value is { } value && value != "-" ? value : null;
            primaries[i] = Note("primary") is { } listed ? listed.Split(' ', StringSplitOptions.RemoveEmptyEntries) : [];
            countedBy[i] = Count(ids[text].Id, ids[text].Language, [.. reader.Rows()], Note("hebrew"), Note("greek"), ids, occurrences, lexical);
        });

        var counted = countedBy.ToList();
        var editions = primaries.SelectMany(p => p).ToHashSet(StringComparer.Ordinal);
        var countedIn = clock.Elapsed;

        await using var transaction = own ? await connection.BeginTransactionAsync(cancellationToken) : null;
        var textIds = counted.Select(c => c.TextId).ToArray();
        var editionIds = editions.Where(ids.ContainsKey).Select(e => ids[e].Id).ToArray();
        await using (var delete = new NpgsqlCommand(
                         """
                         DELETE FROM strong_rendering WHERE text_id = ANY(@texts);
                         DELETE FROM strong_reach_method WHERE text_id = ANY(@texts);
                         DELETE FROM strong_reach WHERE text_id = ANY(@texts);
                         DELETE FROM strong_book_reach WHERE text_id = ANY(@texts);
                         DELETE FROM strong_book WHERE witness_id = ANY(@editions);
                         CREATE TEMP TABLE phrase_stage (text_id int, strong_number text, phrase text, uses int) ON COMMIT DROP;
                         """, connection, transaction))
        {
            delete.Parameters.AddWithValue("texts", textIds);
            delete.Parameters.AddWithValue("editions", editionIds);
            await delete.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var writer = await connection.BeginBinaryImportAsync("COPY phrase_stage (text_id, strong_number, phrase, uses) FROM STDIN (FORMAT BINARY)", cancellationToken))
        {
            foreach (var text in counted)
            {
                foreach (var (number, phrase, uses) in text.Phrases)
                {
                    await writer.StartRowAsync(cancellationToken);
                    await writer.WriteAsync(text.TextId, NpgsqlDbType.Integer, cancellationToken);
                    await writer.WriteAsync(number, NpgsqlDbType.Text, cancellationToken);
                    await writer.WriteAsync(phrase, NpgsqlDbType.Text, cancellationToken);
                    await writer.WriteAsync(uses, NpgsqlDbType.Integer, cancellationToken);
                }
            }

            await writer.CompleteAsync(cancellationToken);
        }

        long renderings;
        await using (var rank = new NpgsqlCommand(
                         $"""
                          INSERT INTO strong_rendering (strong_number, text_id, rank, phrase, uses)
                          SELECT strong_number, text_id, rank, phrase, uses
                          FROM (
                              SELECT strong_number, text_id, phrase, uses,
                                     row_number() OVER (PARTITION BY text_id, strong_number ORDER BY uses DESC, phrase) AS rank
                              FROM phrase_stage
                          ) ranked
                          WHERE rank <= {StrongRenderingCounts.Kept}
                          """, connection, transaction))
        {
            renderings = await rank.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var writer = await connection.BeginBinaryImportAsync(
                         "COPY strong_reach (text_id, strong_number, witness_id, occurrences, reached, phrases, renderings, rendering_links, lexical) FROM STDIN (FORMAT BINARY)",
                         cancellationToken))
        {
            foreach (var text in counted)
            {
                foreach (var row in text.Reach)
                {
                    await writer.StartRowAsync(cancellationToken);
                    await writer.WriteAsync(text.TextId, NpgsqlDbType.Integer, cancellationToken);
                    await writer.WriteAsync(row.Number, NpgsqlDbType.Text, cancellationToken);
                    await writer.WriteAsync(row.Witness, NpgsqlDbType.Integer, cancellationToken);
                    await writer.WriteAsync(row.Occurrences, NpgsqlDbType.Integer, cancellationToken);
                    await writer.WriteAsync(row.Reached, NpgsqlDbType.Integer, cancellationToken);
                    await writer.WriteAsync(row.Variety.Phrases, NpgsqlDbType.Integer, cancellationToken);
                    if (row.Variety.Renderings is { } distinct)
                    {
                        await writer.WriteAsync(distinct, NpgsqlDbType.Integer, cancellationToken);
                    }
                    else
                    {
                        await writer.WriteNullAsync(cancellationToken);
                    }

                    await writer.WriteAsync(row.Variety.RenderingLinks, NpgsqlDbType.Integer, cancellationToken);
                    if (row.Lexical is { } open)
                    {
                        await writer.WriteAsync(open, NpgsqlDbType.Boolean, cancellationToken);
                    }
                    else
                    {
                        await writer.WriteNullAsync(cancellationToken);
                    }
                }
            }

            await writer.CompleteAsync(cancellationToken);
        }

        await using (var writer = await connection.BeginBinaryImportAsync(
                         "COPY strong_reach_method (text_id, strong_number, method, links) FROM STDIN (FORMAT BINARY)", cancellationToken))
        {
            foreach (var text in counted)
            {
                foreach (var (number, method, links) in text.Methods)
                {
                    await writer.StartRowAsync(cancellationToken);
                    await writer.WriteAsync(text.TextId, NpgsqlDbType.Integer, cancellationToken);
                    await writer.WriteAsync(number, NpgsqlDbType.Text, cancellationToken);
                    await writer.WriteAsync(method, NpgsqlDbType.Text, cancellationToken);
                    await writer.WriteAsync(links, NpgsqlDbType.Integer, cancellationToken);
                }
            }

            await writer.CompleteAsync(cancellationToken);
        }

        await using (var writer = await connection.BeginBinaryImportAsync(
                         "COPY strong_book_reach (text_id, strong_number, book, reached) FROM STDIN (FORMAT BINARY)", cancellationToken))
        {
            foreach (var text in counted)
            {
                foreach (var (number, book, reached) in text.Books)
                {
                    await writer.StartRowAsync(cancellationToken);
                    await writer.WriteAsync(text.TextId, NpgsqlDbType.Integer, cancellationToken);
                    await writer.WriteAsync(number, NpgsqlDbType.Text, cancellationToken);
                    await writer.WriteAsync(book, NpgsqlDbType.Integer, cancellationToken);
                    await writer.WriteAsync(reached, NpgsqlDbType.Integer, cancellationToken);
                }
            }

            await writer.CompleteAsync(cancellationToken);
        }

        await using (var writer = await connection.BeginBinaryImportAsync(
                         "COPY strong_book (witness_id, strong_number, book, occurrences) FROM STDIN (FORMAT BINARY)", cancellationToken))
        {
            foreach (var edition in editions.Where(e => ids.ContainsKey(e) && occurrences.ContainsKey(e)).Order(StringComparer.Ordinal))
            {
                foreach (var row in occurrences[edition].GroupBy(r => (r.Number, r.Book)))
                {
                    await writer.StartRowAsync(cancellationToken);
                    await writer.WriteAsync(ids[edition].Id, NpgsqlDbType.Integer, cancellationToken);
                    await writer.WriteAsync(row.Key.Number, NpgsqlDbType.Text, cancellationToken);
                    await writer.WriteAsync(row.Key.Book, NpgsqlDbType.Integer, cancellationToken);
                    await writer.WriteAsync(row.Sum(r => r.Words), NpgsqlDbType.Integer, cancellationToken);
                }
            }

            await writer.CompleteAsync(cancellationToken);
        }

        if (transaction is not null)
        {
            await transaction.CommitAsync(cancellationToken);
        }

        return new Outcome("strong_*", renderings + counted.Sum(c => (long)c.Reach.Count + c.Methods.Count + c.Books.Count), counted.Count, clock.Elapsed,
            $"{renderings} phrases kept, {counted.Sum(c => c.Reach.Count)} reach, {counted.Sum(c => c.Methods.Count)} methods, {counted.Sum(c => c.Books.Count)} by book, " +
            $"{editions.Count} editions by book; counted from the files in {countedIn.TotalSeconds:0.0} s");
    }

    /// <summary>
    /// What <see cref="StrongRenderingCounts.CountText"/> counts for a text, from its index: every phrase over every edition
    /// it is counted over; the reach, the methods and the books only over the edition counted for the number's language.
    /// </summary>
    private static Counted Count(
        int textId,
        string language,
        List<StrongIndexRow> rows,
        string? hebrew,
        string? greek,
        Dictionary<string, (int Id, string Language)> ids,
        Dictionary<string, List<OccurrenceRow>> occurrences,
        IReadOnlyDictionary<string, bool> lexical)
    {
        var phrases = rows.Where(r => r.Phrase is not null)
            .GroupBy(r => (r.Number, r.Phrase!))
            .Select(g => (g.Key.Number, Phrase: g.Key.Item2, Uses: g.Count()))
            .ToList();
        var variety = RenderingVariety.Count(language, phrases.Select(p => new StrongRenderingCount(p.Number, 0, p.Phrase, p.Uses)));

        string? CountedIn(string number) => number.StartsWith('G') ? greek : hebrew;
        var counted = rows.Where(r => CountedIn(r.Number) == r.Witness).ToList();
        var reached = counted.GroupBy(r => r.Number).ToDictionary(g => g.Key, g => g.Select(r => r.Original).Distinct().Count(), StringComparer.Ordinal);
        var methods = counted
            .SelectMany(r => r.Links.Split(' ').Zip(r.Methods.Split(' ')).Select(l => (r.Number, Link: l.First, Method: l.Second)))
            .Distinct()
            .GroupBy(l => (l.Number, l.Method))
            .Select(g => (g.Key.Number, g.Key.Method, Links: g.Count()))
            .ToList();
        var books = counted
            .GroupBy(r => (r.Number, Book: WordKey.Parse(r.Original, r.Witness).Verse.Book))
            .Select(g => (g.Key.Number, g.Key.Book, Reached: g.Select(r => r.Original).Distinct().Count()))
            .ToList();

        var reach = new List<(string, int, int, int, Variety, bool?)>();
        foreach (var (edition, greekNumbers) in (ReadOnlySpan<(string? Edition, bool Greek)>)[(hebrew, false), (greek, true)])
        {
            if (edition is null || !occurrences.TryGetValue(edition, out var standing) || !ids.TryGetValue(edition, out var witness))
            {
                continue;
            }

            foreach (var number in standing.Where(r => r.Number.StartsWith('G') == greekNumbers).GroupBy(r => r.Number))
            {
                reach.Add((number.Key, witness.Id, number.Sum(r => r.Words), reached.GetValueOrDefault(number.Key), variety.Of(number.Key),
                    lexical.TryGetValue(number.Key, out var open) ? open : null));
            }
        }

        return new Counted(textId, phrases, reach, methods, books);
    }

    /// <summary>
    /// <c>text_form</c> from the forms indexes: each text's rows replaced, a form the index leaves unstemmed stemmed by the
    /// database's dictionary for the text's language.
    /// </summary>
    public static async Task<Outcome> Forms(string connectionString, ProductStore store, IReadOnlyCollection<string> texts, CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        return await Forms(connection, store, texts, cancellationToken, own: true);
    }

    /// <summary>The same on a connection whose transaction the caller holds and commits.</summary>
    public static Task<Outcome> Forms(NpgsqlConnection connection, ProductStore store, IReadOnlyCollection<string> texts, CancellationToken cancellationToken) =>
        Forms(connection, store, texts, cancellationToken, own: false);

    private static async Task<Outcome> Forms(NpgsqlConnection connection, ProductStore store, IReadOnlyCollection<string> texts, CancellationToken cancellationToken, bool own)
    {
        var clock = Stopwatch.StartNew();
        var ids = await Texts(connection, cancellationToken);
        var loaded = texts.Where(slug => ids.ContainsKey(slug) && store.Exists(IndexProducts.Of(slug, IndexProducts.Forms))).ToList();

        await using var transaction = own ? await connection.BeginTransactionAsync(cancellationToken) : null;
        await using (var stage = new NpgsqlCommand(
                         """
                         DELETE FROM text_form WHERE text_id = ANY(@texts);
                         CREATE TEMP TABLE form_stage (text_id int, form text, occurrences int, stem text) ON COMMIT DROP;
                         """, connection, transaction))
        {
            stage.Parameters.AddWithValue("texts", loaded.Select(slug => ids[slug].Id).ToArray());
            await stage.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var writer = await connection.BeginBinaryImportAsync("COPY form_stage (text_id, form, occurrences, stem) FROM STDIN (FORMAT BINARY)", cancellationToken))
        {
            foreach (var slug in loaded)
            {
                using var reader = store.Open(IndexProducts.Of(slug, IndexProducts.Forms), FormRow.Schema);
                foreach (var row in reader.Rows())
                {
                    await writer.StartRowAsync(cancellationToken);
                    await writer.WriteAsync(ids[slug].Id, NpgsqlDbType.Integer, cancellationToken);
                    await writer.WriteAsync(row.Form, NpgsqlDbType.Text, cancellationToken);
                    await writer.WriteAsync(row.Occurrences, NpgsqlDbType.Integer, cancellationToken);
                    if (row.Stem is { } stem)
                    {
                        await writer.WriteAsync(stem, NpgsqlDbType.Text, cancellationToken);
                    }
                    else
                    {
                        await writer.WriteNullAsync(cancellationToken);
                    }
                }
            }

            await writer.CompleteAsync(cancellationToken);
        }

        foreach (var language in loaded.Select(slug => ids[slug].Language).Distinct())
        {
            if (SearchWords.Dictionary(language) is not { } dictionary)
            {
                continue;
            }

            await using var stem = new NpgsqlCommand(
                """
                UPDATE form_stage
                SET stem = coalesce(nullif((ts_lexize(@dictionary::regdictionary, form))[1], ''), form)
                WHERE stem IS NULL AND text_id = ANY(@texts)
                """, connection, transaction);
            stem.Parameters.AddWithValue("dictionary", dictionary);
            stem.Parameters.AddWithValue("texts", loaded.Where(slug => ids[slug].Language == language).Select(slug => ids[slug].Id).ToArray());
            await stem.ExecuteNonQueryAsync(cancellationToken);
        }

        long rows;
        await using (var insert = new NpgsqlCommand(
                         "INSERT INTO text_form (text_id, form, stem, occurrences) SELECT text_id, form, coalesce(stem, form), occurrences FROM form_stage",
                         connection, transaction))
        {
            rows = await insert.ExecuteNonQueryAsync(cancellationToken);
        }

        if (transaction is not null)
        {
            await transaction.CommitAsync(cancellationToken);
        }

        return new Outcome("text_form", rows, loaded.Count, clock.Elapsed);
    }

    /// <summary>
    /// <c>entity_rendering</c> and <c>entity_search_spelling</c> from the spellings indexes of every text, counted by
    /// <see cref="EntityRenderingLoader.Count"/> as the load counts them: both tables are replaced whole, since a spelling
    /// is chosen across texts. The names the texts print put back over a pass's are counted and not written.
    /// </summary>
    public static async Task<Outcome> Spellings(string connectionString, ProductStore store, EntityRenderingLoader counter, CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        return await Spellings(connection, store, counter, cancellationToken, own: true);
    }

    /// <summary>The same on a connection whose transaction the caller holds and commits.</summary>
    public static Task<Outcome> Spellings(NpgsqlConnection connection, ProductStore store, EntityRenderingLoader counter, CancellationToken cancellationToken) =>
        Spellings(connection, store, counter, cancellationToken, own: false);

    private static async Task<Outcome> Spellings(NpgsqlConnection connection, ProductStore store, EntityRenderingLoader counter, CancellationToken cancellationToken, bool own)
    {
        var clock = Stopwatch.StartNew();
        var ids = await Texts(connection, cancellationToken);
        var records = new Dictionary<string, int>(StringComparer.Ordinal);
        await using (var command = new NpgsqlCommand("SELECT slug, id FROM entity", connection))
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                records[reader.GetString(0)] = reader.GetInt32(1);
            }
        }

        var words = new List<NamedWord>();
        var printed = new Dictionary<(int Text, string Normalised), int>();
        var verses = new Dictionary<(int Text, VerseKey Verse), int>();
        var unknown = 0;
        var texts = 0;
        foreach (var (slug, (textId, language)) in ids.OrderBy(t => t.Key, StringComparer.Ordinal))
        {
            if (!store.Exists(IndexProducts.Of(slug, IndexProducts.Spellings)))
            {
                continue;
            }

            texts++;
            using var reader = store.Open(IndexProducts.Of(slug, IndexProducts.Spellings), SpellingRow.Schema(slug));
            foreach (var row in reader.Rows())
            {
                if (!records.TryGetValue(row.Record, out var entity))
                {
                    unknown++;
                    continue;
                }

                if (!verses.TryGetValue((textId, row.Own.Verse), out var verse))
                {
                    verses[(textId, row.Own.Verse)] = verse = verses.Count + 1;
                }

                words.Add(new NamedWord(entity, textId, verse, row.Own.Position, row.Surface, row.Trailer, row.Lemma, language,
                    row.Renders, row.Guess, row.Thing, row.Normalised, row.Firm));
                if (row.Normalised is { } normalised)
                {
                    printed[(textId, normalised)] = row.Printed;
                }
            }
        }

        var (renderings, spellings, search, renamed) = await counter.Count(
            words,
            asked => Task.FromResult(asked.Distinct().ToDictionary(a => a, a => printed.GetValueOrDefault(a))),
            cancellationToken);

        await using var transaction = own ? await connection.BeginTransactionAsync(cancellationToken) : null;
        await using (var delete = new NpgsqlCommand("DELETE FROM entity_rendering; DELETE FROM entity_search_spelling;", connection, transaction))
        {
            await delete.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var writer = await connection.BeginBinaryImportAsync(
                         "COPY entity_rendering (entity_id, text_id, form, folded, occurrences, heading) FROM STDIN (FORMAT BINARY)", cancellationToken))
        {
            foreach (var rendering in renderings)
            {
                await writer.StartRowAsync(cancellationToken);
                await writer.WriteAsync(rendering.EntityId, NpgsqlDbType.Integer, cancellationToken);
                await writer.WriteAsync(rendering.TextId, NpgsqlDbType.Integer, cancellationToken);
                await writer.WriteAsync(rendering.Form, NpgsqlDbType.Text, cancellationToken);
                await writer.WriteAsync(rendering.Folded, NpgsqlDbType.Text, cancellationToken);
                await writer.WriteAsync(rendering.Occurrences, NpgsqlDbType.Integer, cancellationToken);
                await writer.WriteAsync(rendering.Heading, NpgsqlDbType.Boolean, cancellationToken);
            }

            await writer.CompleteAsync(cancellationToken);
        }

        await using (var writer = await connection.BeginBinaryImportAsync(
                         "COPY entity_search_spelling (entity_id, form, folded) FROM STDIN (FORMAT BINARY)", cancellationToken))
        {
            foreach (var spelling in spellings)
            {
                await writer.StartRowAsync(cancellationToken);
                await writer.WriteAsync(spelling.EntityId, NpgsqlDbType.Integer, cancellationToken);
                await writer.WriteAsync(spelling.Form, NpgsqlDbType.Text, cancellationToken);
                await writer.WriteAsync(spelling.Folded, NpgsqlDbType.Text, cancellationToken);
            }

            await writer.CompleteAsync(cancellationToken);
        }

        if (transaction is not null)
        {
            await transaction.CommitAsync(cancellationToken);
        }

        return new Outcome("entity_rendering, entity_search_spelling", renderings.Count + spellings.Count, texts, clock.Elapsed,
            $"{words.Count} named words, {renderings.Count} renderings, {spellings.Count} search spellings ({search}); " +
            $"{renamed.Count} names the texts print put back, not written; {unknown} words naming a record the database lacks");
    }
}
