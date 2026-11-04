using System.Collections.Concurrent;
using System.Diagnostics;
using Essenthos.Core.Loading;
using Essenthos.Core.Loading.Frame;
using Essenthos.Core.Products;
using Npgsql;
using NpgsqlTypes;

namespace Essenthos.Core.Mapping;

/// <summary>
/// Writes each pair's verse links (<see cref="VerseLinks"/>) as <c>mapping/&lt;pair&gt;/verses</c> and loads them into a
/// serving database: <c>verse_link</c> and <c>verse_link_verse</c>, each verse found by its text and own address.
/// </summary>
internal static class VerseLinkFiles
{
    public const string Producer = "forge verse-pairs";

    public const string Name = "verses";

    public sealed record Outcome(string Pair, int Links, bool Written);

    public sealed record Loaded(int Pairs, long Links, long Verses, long LeftOut, TimeSpan Elapsed);

    public static string Product(string pair) => MappingProducts.Of(pair, Name);

    /// <summary>Every pair with a final, and every declared pair whose two texts the store holds, in ordinal order.</summary>
    public static List<string> Pairs(ProductStore store) =>
    [
        .. MappingProducts.Pairs(store).Where(pair => store.Exists(MappingProducts.Of(pair, MappingProducts.Final)))
            .Concat(Loading.Links.VerseLinkLoader.DeclaredPairs
                .Where(d => store.Exists(VerseManifestRow.ProductOf(d.From)) && store.Exists(VerseManifestRow.ProductOf(d.To)))
                .Select(d => MappingProducts.Pair(d.From, d.To)))
            .Distinct().Order(StringComparer.Ordinal),
    ];

    /// <summary>The pair's frames, each text read once however many pairs ask for it.</summary>
    public static PairFrames Frames(ProductStore store, string pair, ConcurrentDictionary<string, Lazy<TextFrame>>? cache = null)
    {
        var (from, to) = MappingProducts.Split(pair);
        cache ??= new ConcurrentDictionary<string, Lazy<TextFrame>>(StringComparer.Ordinal);
        TextFrame Of(string slug) => cache.GetOrAdd(slug, s => new Lazy<TextFrame>(() => TextFrame.Read(store, s))).Value;
        return new PairFrames(Of(from), Of(to), MappingProducts.Methods(store, MappingProducts.Pair(to, from)).Count > 0);
    }

    /// <summary>What a pair's verse links are made from.</summary>
    public static IReadOnlyList<ProductInput> Inputs(ProductStore store, string pair)
    {
        var (from, to) = MappingProducts.Split(pair);
        string[] products =
        [
            VerseManifestRow.ProductOf(from), VerseManifestRow.ProductOf(to), FrameFile.Product(from), FrameFile.Product(to),
            MappingProducts.Of(pair, MappingProducts.Final),
        ];
        return store.Inputs([
            .. products.Select(store.Find).OfType<string>(),
            .. from == GeezTextSource.Slug || to == GeezTextSource.Slug ? ["Essenthos/versification/geez.tsv"] : Array.Empty<string>(),
        ]);
    }

    /// <summary>Writes the verse links of each pair named, or of every pair, whose inputs moved (all of them with <paramref name="force"/>).</summary>
    public static List<Outcome> Write(ProductStore store, IReadOnlySet<string>? only, bool force, int parallel)
    {
        var cache = new ConcurrentDictionary<string, Lazy<TextFrame>>(StringComparer.Ordinal);
        var outcomes = new ConcurrentBag<Outcome>();
        Parallel.ForEach(Pairs(store).Where(pair => only is null || only.Contains(pair)),
            new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, parallel) }, pair =>
            {
                var inputs = Inputs(store, pair);
                if (!force && !store.NeedsBuild(Product(pair), ProductKind.Machine, inputs))
                {
                    outcomes.Add(new Outcome(pair, 0, false));
                    return;
                }

                var final = new List<FinalRow>();
                if (store.Exists(MappingProducts.Of(pair, MappingProducts.Final)))
                {
                    using var reader = store.Open(MappingProducts.Of(pair, MappingProducts.Final), FinalRow.Schema);
                    final = reader.ReadAll();
                }

                var rows = VerseLinks.Derive(Frames(store, pair, cache), final);
                using var writer = store.Create(Product(pair), ProductKind.Machine, VerseLinkRow.Schema, ProductHeader.DescribeProducer(Producer), inputs);
                writer.WriteAll(rows);
                outcomes.Add(new Outcome(pair, rows.Count, true));
            });

        store.Save();
        return [.. outcomes.OrderBy(o => o.Pair, StringComparer.Ordinal)];
    }

    /// <summary>
    /// Loads the verse links of the pairs named, or of every pair with a file, replacing what the database holds of those
    /// pairs, in the caller's transaction if there is one. A verse the database does not hold leaves its link out, counted.
    /// </summary>
    public static async Task<Loaded> Load(NpgsqlConnection connection, ProductStore store, IReadOnlyCollection<string>? pairs, CancellationToken cancellationToken)
    {
        var clock = Stopwatch.StartNew();
        var chosen = (pairs ?? Pairs(store)).Where(pair => store.Exists(Product(pair))).ToList();
        var texts = new Dictionary<string, int>(StringComparer.Ordinal);
        await using (var command = new NpgsqlCommand("SELECT slug, id FROM text", connection))
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                texts[reader.GetString(0)] = reader.GetInt32(1);
            }
        }

        var verses = new Dictionary<(int Text, VerseKey Own), int>();
        await using (var command = new NpgsqlCommand(
                         "SELECT v.text_id, b.canonical_ordinal, v.chapter_number, v.number, v.label, v.id FROM verse v JOIN book b ON b.id = v.book_id",
                         connection) { CommandTimeout = 0 })
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                verses[(reader.GetInt32(0), new VerseKey(reader.GetInt32(1), reader.GetInt32(2), reader.GetInt32(3), reader.GetString(4)))] = reader.GetInt32(5);
            }
        }

        var links = new List<(int From, int To, VerseLinkRow Row, int[] FromVerses, int[] ToVerses)>();
        long leftOut = 0;
        foreach (var pair in chosen)
        {
            var (from, to) = MappingProducts.Split(pair);
            if (!texts.TryGetValue(from, out var fromId) || !texts.TryGetValue(to, out var toId))
            {
                continue;
            }

            using var reader = store.Open(Product(pair), VerseLinkRow.Schema);
            foreach (var row in reader.Rows())
            {
                int[]? Resolve(int text, string keys)
                {
                    var ids = new List<int>();
                    foreach (var key in MappingText.Split(keys))
                    {
                        if (!verses.TryGetValue((text, VerseKey.Parse(key)), out var id))
                        {
                            return null;
                        }

                        ids.Add(id);
                    }

                    return [.. ids];
                }

                if (Resolve(fromId, row.From) is { } fromVerses && Resolve(toId, row.To) is { } toVerses)
                {
                    links.Add((fromId, toId, row, fromVerses, toVerses));
                }
                else
                {
                    leftOut++;
                }
            }
        }

        if (pairs is not null)
        {
            await using var delete = new NpgsqlCommand(
                "DELETE FROM verse_link l USING text f, text t WHERE f.id = l.from_text_id AND t.id = l.to_text_id AND f.slug || '-' || t.slug = ANY(@pairs)",
                connection) { CommandTimeout = 0 };
            delete.Parameters.AddWithValue("pairs", pairs.ToArray());
            await delete.ExecuteNonQueryAsync(cancellationToken);
        }

        int first;
        await using (var command = new NpgsqlCommand("SELECT coalesce(max(id), 0) + 1 FROM verse_link", connection))
        {
            first = Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken));
        }

        await using (var writer = await connection.BeginBinaryImportAsync(
                         "COPY verse_link (id, from_text_id, to_text_id, relation, method, confidence, source, note) FROM STDIN (FORMAT BINARY)", cancellationToken))
        {
            for (var i = 0; i < links.Count; i++)
            {
                var (fromId, toId, row, _, _) = links[i];
                await writer.StartRowAsync(cancellationToken);
                await writer.WriteAsync(first + i, NpgsqlDbType.Integer, cancellationToken);
                await writer.WriteAsync(fromId, NpgsqlDbType.Integer, cancellationToken);
                await writer.WriteAsync(toId, NpgsqlDbType.Integer, cancellationToken);
                await writer.WriteAsync(row.Relation, NpgsqlDbType.Text, cancellationToken);
                await writer.WriteAsync(row.Method, NpgsqlDbType.Text, cancellationToken);
                await (row.Confidence is { } confidence ? writer.WriteAsync(confidence, NpgsqlDbType.Double, cancellationToken) : writer.WriteNullAsync(cancellationToken));
                await writer.WriteAsync(row.Source, NpgsqlDbType.Text, cancellationToken);
                await (row.Note is { } note ? writer.WriteAsync(note, NpgsqlDbType.Text, cancellationToken) : writer.WriteNullAsync(cancellationToken));
            }

            await writer.CompleteAsync(cancellationToken);
        }

        long members = 0;
        await using (var writer = await connection.BeginBinaryImportAsync(
                         "COPY verse_link_verse (verse_link_id, verse_id, side) FROM STDIN (FORMAT BINARY)", cancellationToken))
        {
            for (var i = 0; i < links.Count; i++)
            {
                foreach (var (ids, side) in new[] { (links[i].FromVerses, "from"), (links[i].ToVerses, "to") })
                {
                    foreach (var id in ids)
                    {
                        await writer.StartRowAsync(cancellationToken);
                        await writer.WriteAsync(first + i, NpgsqlDbType.Integer, cancellationToken);
                        await writer.WriteAsync(id, NpgsqlDbType.Integer, cancellationToken);
                        await writer.WriteAsync(side, NpgsqlDbType.Text, cancellationToken);
                        members++;
                    }
                }
            }

            await writer.CompleteAsync(cancellationToken);
        }

        await using (var command = new NpgsqlCommand(
                         "SELECT setval(pg_get_serial_sequence('verse_link', 'id'), greatest(coalesce((SELECT max(id) FROM verse_link), 0), 1))", connection))
        {
            await command.ExecuteScalarAsync(cancellationToken);
        }

        return new Loaded(chosen.Count, links.Count, members, leftOut, clock.Elapsed);
    }
}
