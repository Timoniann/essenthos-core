using System.Collections.Concurrent;
using System.Diagnostics;
using Essenthos.Core.Conversion;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Loading.Frame;
using Essenthos.Core.Mapping;
using Essenthos.Core.Products;
using Essenthos.Core.Products.Essx;
using Npgsql;
using NpgsqlTypes;

namespace Essenthos.Core.Indexes;

/// <summary>
/// One row of <c>texts/&lt;SLUG&gt;/strong-occurrences</c>: a word whose Strong number the database serves, at the frame address
/// of its verse's primary place.
/// </summary>
internal sealed record StrongOccurrenceRow(WordKey Key, string Number, int Book, int Chapter, int Verse)
{
    public static ProductSchema<StrongOccurrenceRow> Schema(string text) => new ProductSchema<StrongOccurrenceRow>()
        .OwnWordKey("own", r => r.Key)
        .Text("number", r => r.Number)
        .Integer("book", r => r.Book)
        .Integer("chapter", r => r.Chapter)
        .Integer("verse", r => r.Verse)
        .Reads(row => new StrongOccurrenceRow(WordKey.Parse(row.Text(0), text), row.Text(1), row.Int32(2), row.Int32(3), row.Int32(4)));
}

/// <summary>
/// One row of <c>texts/&lt;SLUG&gt;/pair-links</c>: an edition that is not a translation which some link joins to the text, the
/// links counted both ways, and whether any of the edition's words carries a served Strong number.
/// </summary>
internal sealed record PairLinksRow(string Edition, long Links, bool Numbered)
{
    public static readonly ProductSchema<PairLinksRow> Schema = new ProductSchema<PairLinksRow>()
        .Text("edition", r => r.Edition)
        .Integer("links", r => r.Links)
        .Boolean("numbered", r => r.Numbered)
        .Reads(row => new PairLinksRow(row.Text(0), row.Integer(1), row.Boolean(2)));
}

/// <summary>
/// What the Strong pages read that only the words, the frame and the links decide, as files per text, so a build loads them
/// instead of counting them in the database: <c>strong_occurrence</c> from <c>texts/&lt;SLUG&gt;/strong-occurrences</c> (the
/// text's words and frame) and <c>text_pair_links</c> from <c>texts/&lt;SLUG&gt;/pair-links</c> (the finals of its pairs with
/// editions that are not translations, those editions' words, and every other text's declaration, which says what it is).
/// A file is made only when one of its inputs moved, so one text's change makes only its own files and the files of the texts
/// its pairs join it to.
/// </summary>
internal static class StoredStrongFiles
{
    /// <summary>Raised with any change to what a file holds; a file made by another version is made again whatever its inputs say.</summary>
    public const string Version = "1";

    public const string VersionNote = "stored-strong-version";

    private const string Producer = "forge stored-strong";

    public static string Occurrences(string text) => $"texts/{text}/strong-occurrences";

    public static string PairLinks(string text) => $"texts/{text}/pair-links";

    /// <summary>Every text's two files where an input moved, the texts in parallel.</summary>
    public static List<IndexOutcome> Make(IndexContext context, bool force, int parallel)
    {
        var settled = new ConcurrentDictionary<string, Lazy<long>>(StringComparer.Ordinal);
        var numbered = new ConcurrentDictionary<string, Lazy<bool>>(StringComparer.Ordinal);
        var outcomes = new ConcurrentBag<IndexOutcome>();
        Parallel.ForEach(context.Texts.Keys.Order(StringComparer.Ordinal), new ParallelOptions { MaxDegreeOfParallelism = parallel }, text =>
        {
            if (MakeOccurrences(context.Store, text, force) is { } occurrences)
            {
                outcomes.Add(occurrences);
            }

            outcomes.Add(MakePairLinks(context, text, settled, numbered, force));
        });

        context.Store.Save();
        return [.. outcomes.OrderBy(o => o.Text, StringComparer.Ordinal).ThenBy(o => o.Index, StringComparer.Ordinal)];
    }

    /// <summary>The text's numbered words at their verses' primary places; none for a text the frame does not place.</summary>
    private static IndexOutcome? MakeOccurrences(ProductStore store, string text, bool force)
    {
        var clock = Stopwatch.StartNew();
        var frame = FrameFile.Product(text);
        if (!store.Exists(frame))
        {
            return null;
        }

        var product = Occurrences(text);
        List<ProductInput> inputs = [store.ProductInput(TextWord.ProductOf(text)), store.ProductInput(frame)];
        if (!force && !NeedsBuild(store, product, inputs))
        {
            return new IndexOutcome(text, "occurrences", false, 0, clock.Elapsed);
        }

        var primary = new Dictionary<VerseKey, VerseKey>();
        using (var reader = store.Open(frame, FrameRow.Schema))
        {
            foreach (var (own, canonical) in reader.Rows().SelectMany(row => row.Verses()))
            {
                primary[own] = canonical[0];
            }
        }

        using var writer = store.Create(product, ProductKind.Machine, StrongOccurrenceRow.Schema(text), ProductHeader.DescribeProducer(Producer), inputs,
            header => header.Notes.Add(new(VersionNote, Version)));
        foreach (var (key, number) in Numbered(store, text))
        {
            var at = primary.TryGetValue(key.Verse, out var moved) ? moved : FrameFile.AtOwn(key.Verse);
            writer.Write(new StrongOccurrenceRow(key, number, at.Book, at.Chapter, at.Verse));
        }

        writer.Complete();
        return new IndexOutcome(text, "occurrences", true, writer.Rows, clock.Elapsed);
    }

    private static IndexOutcome MakePairLinks(
        IndexContext context, string text, ConcurrentDictionary<string, Lazy<long>> settled, ConcurrentDictionary<string, Lazy<bool>> numbered, bool force)
    {
        var clock = Stopwatch.StartNew();
        var store = context.Store;
        var product = PairLinks(text);
        var pairs = context.PairsOf(text).OrderBy(p => p.Pair, StringComparer.Ordinal).ToList();
        var others = pairs.Select(p => p.Other).Distinct().Order(StringComparer.Ordinal).ToList();
        var editions = others.Where(other => context.Texts[other].Kind != TextKind.Translation).ToList();
        var counted = pairs.Where(p => editions.Contains(p.Other)).ToList();
        List<ProductInput> inputs =
        [
            .. others.Select(other => store.ProductInput(TextField.ProductOf(other))),
            .. editions.Select(edition => store.ProductInput(TextWord.ProductOf(edition))),
            .. counted.Select(p => store.ProductInput(MappingProducts.Of(p.Pair, MappingProducts.Final))),
        ];
        if (!force && !NeedsBuild(store, product, inputs))
        {
            return new IndexOutcome(text, "pair-links", false, 0, clock.Elapsed);
        }

        using var writer = store.Create(product, ProductKind.Machine, PairLinksRow.Schema, ProductHeader.DescribeProducer(Producer), inputs,
            header => header.Notes.Add(new(VersionNote, Version)));
        foreach (var edition in editions)
        {
            var links = counted.Where(p => p.Other == edition)
                .Sum(p => settled.GetOrAdd(p.Pair, pair => new Lazy<long>(() => Settled(store, pair))).Value);
            if (links > 0)
            {
                writer.Write(new PairLinksRow(edition, links,
                    numbered.GetOrAdd(edition, slug => new Lazy<bool>(() => Numbered(store, slug).Any())).Value));
            }
        }

        writer.Complete();
        return new IndexOutcome(text, "pair-links", true, writer.Rows, clock.Elapsed);
    }

    /// <summary>The links of a pair's final: one settled claim opens each.</summary>
    private static long Settled(ProductStore store, string pair)
    {
        var product = MappingProducts.Of(pair, MappingProducts.Final);
        var relative = store.Find(product) ?? throw new FileNotFoundException($"{product} is not in the store.");
        if (relative.EndsWith(EssxLayout.Extension, StringComparison.Ordinal))
        {
            using var file = EssxReader.Open(store.FullPath(relative));
            var table = file.Table(EssxFormat.TableName);
            long links = 0;
            for (var g = 0; g < table.RowGroups; g++)
            {
                var group = table.ReadGroup(g, ["settled"]);
                var column = group.Column("settled");
                for (var r = 0; r < group.Rows; r++)
                {
                    if (column.Boolean(r) == true)
                    {
                        links++;
                    }
                }
            }

            return links;
        }

        using var reader = store.Open(product, FinalRow.Schema);
        return reader.Rows().LongCount(row => row.Settled);
    }

    /// <summary>The words of a text carrying a Strong number the database serves (<see cref="TextWordLoad.StrongServed"/>), in the product's order.</summary>
    private static IEnumerable<(WordKey Key, string Number)> Numbered(ProductStore store, string text)
    {
        if (!TextWordLoad.StrongServed(store, text))
        {
            yield break;
        }

        var relative = store.Find(TextWord.ProductOf(text)) ?? throw new FileNotFoundException($"The store holds no words of {text}.");
        if (relative.EndsWith(EssxLayout.Extension, StringComparison.Ordinal))
        {
            using var file = EssxReader.Open(store.FullPath(relative));
            var table = file.Table(EssxFormat.TableName);
            for (var g = 0; g < table.RowGroups; g++)
            {
                var group = table.ReadGroup(g, ["own", "strong"]);
                var (own, strong) = (group.Column("own"), group.Column("strong"));
                for (var r = 0; r < group.Rows; r++)
                {
                    if (strong.Text(r) is { } number)
                    {
                        yield return (own.WordKey(r, text)!.Value, number);
                    }
                }
            }

            yield break;
        }

        using var reader = store.Open(TextWord.ProductOf(text), TextWord.Schema(text));
        foreach (var word in reader.Rows())
        {
            if (word.Strong is { } number)
            {
                yield return (word.Key, number);
            }
        }
    }

    private static bool NeedsBuild(ProductStore store, string product, IReadOnlyList<ProductInput> inputs)
    {
        if (store.NeedsBuild(product, ProductKind.Machine, inputs))
        {
            return true;
        }

        using var reader = store.Open(product);
        return reader.Header?.Notes.LastOrDefault(note => note.Key == VersionNote).Value != Version;
    }

    /// <summary>
    /// Puts every text's files into <c>strong_occurrence</c> and <c>text_pair_links</c> of a database whose tables are empty, the
    /// texts' occurrences on <paramref name="parallel"/> connections at once; the rows written.
    /// </summary>
    public static async Task<long> Load(string connectionString, ProductStore store, int parallel, CancellationToken cancellationToken)
    {
        Dictionary<string, int> ids;
        long rows;
        await using (var connection = new NpgsqlConnection(connectionString))
        {
            await connection.OpenAsync(cancellationToken);
            ids = await TextIds(connection, cancellationToken);
            rows = await CopyPairLinks(connection, store, ids, ids.Keys, cancellationToken);
        }

        var words = WordIdTable.For(store);
        await Parallel.ForEachAsync(ids, new ParallelOptions { MaxDegreeOfParallelism = parallel, CancellationToken = cancellationToken }, async (text, token) =>
        {
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync(token);
            Interlocked.Add(ref rows, await CopyOccurrences(connection, store, words, text.Key, text.Value, token));
        });
        return rows;
    }

    /// <summary>
    /// Replaces the occurrences of the texts <paramref name="occurrences"/> names and the pair links of those
    /// <paramref name="pairLinks"/> names with their files' rows, on the caller's connection and transaction; the rows written.
    /// </summary>
    public static async Task<long> Load(NpgsqlConnection connection, NpgsqlTransaction transaction, ProductStore store,
        IReadOnlyCollection<string> occurrences, IReadOnlyCollection<string> pairLinks, CancellationToken cancellationToken)
    {
        var ids = await TextIds(connection, cancellationToken);
        long rows = 0;
        if (occurrences.Count > 0)
        {
            await Delete(connection, transaction, "DELETE FROM strong_occurrence WHERE text_id = ANY(@texts)", ids, occurrences, cancellationToken);
            var words = WordIdTable.For(store);
            foreach (var text in occurrences.Where(ids.ContainsKey))
            {
                rows += await CopyOccurrences(connection, store, words, text, ids[text], cancellationToken);
            }
        }

        if (pairLinks.Count > 0)
        {
            await Delete(connection, transaction, "DELETE FROM text_pair_links WHERE text_id = ANY(@texts)", ids, pairLinks, cancellationToken);
            rows += await CopyPairLinks(connection, store, ids, pairLinks, cancellationToken);
        }

        return rows;
    }

    private static async Task Delete(NpgsqlConnection connection, NpgsqlTransaction transaction, string sql, Dictionary<string, int> ids,
        IReadOnlyCollection<string> texts, CancellationToken cancellationToken)
    {
        await using var delete = new NpgsqlCommand(sql, connection, transaction);
        delete.Parameters.AddWithValue("texts", texts.Where(ids.ContainsKey).Select(text => ids[text]).ToArray());
        await delete.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>A text's occurrences; none where the frame places none of its verses (no frame file) or its file is not made.</summary>
    private static async Task<long> CopyOccurrences(NpgsqlConnection connection, ProductStore store, WordIdTable words, string text, int id,
        CancellationToken cancellationToken)
    {
        if (!store.Exists(FrameFile.Product(text)) || !store.Exists(Occurrences(text)))
        {
            return 0;
        }

        long rows = 0;
        await using var writer = await connection.BeginBinaryImportAsync(
            "COPY strong_occurrence (strong_number, corpus, book, chapter, verse, position, word_id, text_id) FROM STDIN (FORMAT BINARY)", cancellationToken);
        using var reader = store.Open(Occurrences(text), StrongOccurrenceRow.Schema(text));
        foreach (var row in reader.Rows())
        {
            await writer.StartRowAsync(cancellationToken);
            await writer.WriteAsync(row.Number, NpgsqlDbType.Text, cancellationToken);
            await writer.WriteAsync(text, NpgsqlDbType.Text, cancellationToken);
            await writer.WriteAsync(row.Book, NpgsqlDbType.Integer, cancellationToken);
            await writer.WriteAsync(row.Chapter, NpgsqlDbType.Integer, cancellationToken);
            await writer.WriteAsync(row.Verse, NpgsqlDbType.Integer, cancellationToken);
            await writer.WriteAsync(row.Key.Position, NpgsqlDbType.Integer, cancellationToken);
            await writer.WriteAsync(words.Of(row.Key), NpgsqlDbType.Bigint, cancellationToken);
            await writer.WriteAsync(id, NpgsqlDbType.Integer, cancellationToken);
            rows++;
        }

        await writer.CompleteAsync(cancellationToken);
        return rows;
    }

    private static async Task<long> CopyPairLinks(NpgsqlConnection connection, ProductStore store, Dictionary<string, int> ids, IEnumerable<string> texts,
        CancellationToken cancellationToken)
    {
        long rows = 0;
        await using var writer = await connection.BeginBinaryImportAsync(
            "COPY text_pair_links (text_id, edition_id, links, numbered) FROM STDIN (FORMAT BINARY)", cancellationToken);
        foreach (var text in texts.Where(text => ids.ContainsKey(text) && store.Exists(PairLinks(text))).Order(StringComparer.Ordinal))
        {
            using var reader = store.Open(PairLinks(text), PairLinksRow.Schema);
            foreach (var row in reader.Rows())
            {
                var edition = ids.TryGetValue(row.Edition, out var known)
                    ? known
                    : throw new InvalidDataException($"{PairLinks(text)} counts links to {row.Edition}, which the database does not hold.");
                await writer.StartRowAsync(cancellationToken);
                await writer.WriteAsync(ids[text], NpgsqlDbType.Integer, cancellationToken);
                await writer.WriteAsync(edition, NpgsqlDbType.Integer, cancellationToken);
                await writer.WriteAsync(checked((int)row.Links), NpgsqlDbType.Integer, cancellationToken);
                await writer.WriteAsync(row.Numbered, NpgsqlDbType.Boolean, cancellationToken);
                rows++;
            }
        }

        await writer.CompleteAsync(cancellationToken);
        return rows;
    }

    private static async Task<Dictionary<string, int>> TextIds(NpgsqlConnection connection, CancellationToken cancellationToken)
    {
        var ids = new Dictionary<string, int>(StringComparer.Ordinal);
        await using var command = new NpgsqlCommand("SELECT slug, id FROM text", connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            ids[reader.GetString(0)] = reader.GetInt32(1);
        }

        return ids;
    }
}
