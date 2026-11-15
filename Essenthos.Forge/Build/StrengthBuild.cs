using System.Collections.Concurrent;
using System.Diagnostics;
using Essenthos.Core.Corpus;
using Essenthos.Core.Database;
using Essenthos.Core.Mapping;
using Essenthos.Core.Products;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;

namespace Essenthos.Core.Build;

/// <summary>One chapter of a text against one reference text, as its product lists it.</summary>
internal sealed record StrengthRow(string Reference, long Book, long Chapter, bool ByReading, string Verses)
{
    public static readonly ProductSchema<StrengthRow> Schema = new ProductSchema<StrengthRow>()
        .Text("reference", r => r.Reference)
        .Integer("book", r => r.Book)
        .Integer("chapter", r => r.Chapter)
        .Boolean("reading", r => r.ByReading)
        .Text("verses", r => r.Verses)
        .Reads(row => new StrengthRow(row.Text(0), row.Integer(1), row.Integer(2), row.Boolean(3), row.Text(4)));
}

/// <summary>What a strength run did for one text: counted or current, and the rows the table holds for it.</summary>
internal sealed record StrengthOutcome(string Text, bool Made, int Rows, TimeSpan Elapsed);

/// <summary>
/// The verse strength the reader lays over two panes (<c>verse_strength</c>, <see cref="VerseStrengths"/>): per text, each
/// canonical chapter against every text linked to it. A product of the serving stage (<c>build/products</c>, maker
/// <c>serve</c>): a text is counted from the filled tables when its product is missing or one of its inputs moved (the text's
/// words, verses and frame, the final and verse links of every pair it stands in); otherwise the table is matched to the
/// product, its rows copied where the table does not hold them. Each text is replaced in a transaction of its own.
/// <c>strength/&lt;SLUG&gt;</c> names the reference texts by slug, so any database built from the same store takes it.
/// </summary>
internal sealed class StrengthBuild(string connectionString, ProductStore store, IServiceProvider services, ILogger logger)
{
    /// <summary>Raised with any change to what a chapter's strength counts, so every product is counted again.</summary>
    public const string Version = "1";

    private const string VersionNote = "strength-version";

    public static string Product(string text) => $"strength/{text}";

    /// <summary>
    /// The strength of the texts named (every text when null): counted where <paramref name="force"/> is set, the product is
    /// missing or of another version, or an input moved; copied from the product where the table holds other rows for the text
    /// than it lists, or where <paramref name="copy"/> names the text.
    /// </summary>
    public async Task<IReadOnlyList<StrengthOutcome>> Run(
        IReadOnlyCollection<string>? only, bool force, int parallel, CancellationToken cancellationToken, IReadOnlyCollection<string>? copy = null)
    {
        Dictionary<int, string> slugs;
        Dictionary<int, int> held;
        using (var scope = services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            slugs = await db.Texts.AsNoTracking().ToDictionaryAsync(t => t.Id, t => t.Slug, cancellationToken);
            held = await db.VerseStrengths.GroupBy(s => s.TextId).Select(g => new { g.Key, Count = g.Count() })
                .ToDictionaryAsync(g => g.Key, g => g.Count, cancellationToken);
        }

        var ids = slugs.ToDictionary(s => s.Value, s => s.Key, StringComparer.Ordinal);
        var pairs = MappingProducts.Pairs(store);
        var outcomes = new ConcurrentBag<StrengthOutcome>();
        var texts = slugs.Where(t => only is null || only.Contains(t.Value)).OrderBy(t => t.Value, StringComparer.Ordinal).ToList();
        await Parallel.ForEachAsync(texts, new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, parallel), CancellationToken = cancellationToken },
            async (text, token) =>
            {
                var clock = Stopwatch.StartNew();
                var inputs = Inputs(text.Value, pairs);
                var product = Product(text.Value);
                if (!force && Current(product, inputs))
                {
                    var listed = Stored(product, ids);
                    if (listed is not null && copy?.Contains(text.Value) != true && listed.Count == held.GetValueOrDefault(text.Key))
                    {
                        outcomes.Add(new StrengthOutcome(text.Value, false, listed.Count, clock.Elapsed));
                        return;
                    }

                    // The product is current and the table does not hold it (a fresh database, or one served from another
                    // copy of the store): its rows are copied, each reference text found by slug, rather than counted again.
                    if (listed is not null)
                    {
                        await Replace(text.Key, listed, token);
                        logger.LogInformation("Strength of {Text}: {Rows} rows copied in {Seconds:0.0} s", text.Value, listed.Count, clock.Elapsed.TotalSeconds);
                        outcomes.Add(new StrengthOutcome(text.Value, true, listed.Count, clock.Elapsed));
                        return;
                    }
                }

                List<ChapterStrengthCount> counted;
                await using (var connection = new NpgsqlConnection(connectionString))
                {
                    await connection.OpenAsync(token);
                    counted = await VerseStrengths.Count(connection, text.Key, token);
                }

                var rows = counted.Select(c => new StrengthRow(slugs[c.Reference], c.Book, c.Chapter, c.ByReading, VerseStrengths.Pack(c.Verses)))
                    .OrderBy(r => r.Reference, StringComparer.Ordinal).ThenBy(r => r.Book).ThenBy(r => r.Chapter)
                    .ToList();
                await Replace(text.Key, [.. rows.Select(r => (ids[r.Reference], r))], token);
                lock (store)
                {
                    using var writer = store.Create(product, ProductKind.Machine, StrengthRow.Schema,
                        ProductHeader.DescribeProducer("forge strength"), inputs, header => header.Notes.Add(new(VersionNote, Version)));
                    foreach (var row in rows)
                    {
                        writer.Write(row);
                    }

                    writer.Complete();
                }

                logger.LogInformation("Strength of {Text}: {Rows} chapters against {References} texts in {Seconds:0.0} s",
                    text.Value, rows.Count, rows.Select(r => r.Reference).Distinct().Count(), clock.Elapsed.TotalSeconds);
                outcomes.Add(new StrengthOutcome(text.Value, true, rows.Count, clock.Elapsed));
            });

        // Once, after every text: the store's hash memory is not written from two threads at once.
        if (!outcomes.IsEmpty && outcomes.Any(o => o.Made))
        {
            store.Save();
        }

                return [.. outcomes.OrderBy(o => o.Text, StringComparer.Ordinal)];
    }

    /// <summary>
    /// What a text's strength is counted from, where this store holds it: its words (the links name them), verses and frame
    /// (each verse's canonical place), and the final and verse links of every pair it stands in, either way round.
    /// </summary>
    internal List<ProductInput> Inputs(string text, IEnumerable<string> pairs)
    {
        var products = new List<string> { $"texts/{text}/words", VerseManifestRow.ProductOf(text), Loading.Frame.FrameFile.Product(text) };
        foreach (var pair in pairs)
        {
            var (from, to) = MappingProducts.Split(pair);
            if (from == text || to == text)
            {
                products.Add(MappingProducts.Of(pair, MappingProducts.Final));
                products.Add(VerseLinkFiles.Product(pair));
            }
        }

        lock (store)
        {
            return [.. store.Inputs(products.Distinct(StringComparer.Ordinal).Select(store.Find).OfType<string>().Order(StringComparer.Ordinal))];
        }
    }

    private bool Current(string product, IReadOnlyList<ProductInput> inputs)
    {
        lock (store)
        {
            if (store.NeedsBuild(product, ProductKind.Machine, inputs))
            {
                return false;
            }

            using var reader = store.Open(product, StrengthRow.Schema);
            return reader.Header?.Notes.LastOrDefault(note => note.Key == VersionNote).Value == Version;
        }
    }

    /// <summary>A text's product's rows with each reference text's id, or null where a text it names is not in the database.</summary>
    private List<(int Reference, StrengthRow Row)>? Stored(string product, IReadOnlyDictionary<string, int> ids)
    {
        lock (store)
        {
            using var reader = store.Open(product, StrengthRow.Schema);
            var rows = new List<(int, StrengthRow)>();
            foreach (var row in reader.Rows())
            {
                if (!ids.TryGetValue(row.Reference, out var id))
                {
                    return null;
                }

                rows.Add((id, row));
            }

            return rows;
        }
    }

    /// <summary>A text's strength replaced in one transaction: a failure leaves the rows it had.</summary>
    private async Task Replace(int textId, List<(int Reference, StrengthRow Row)> rows, CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using (var delete = new NpgsqlCommand("DELETE FROM verse_strength WHERE text_id = @text", connection, transaction))
        {
            delete.Parameters.AddWithValue("text", textId);
            await delete.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var copy = await connection.BeginBinaryImportAsync(
                         "COPY verse_strength (text_id, reference_id, canonical_book, canonical_chapter, by_reading, verses) FROM STDIN (FORMAT BINARY)",
                         cancellationToken))
        {
            foreach (var (reference, row) in rows)
            {
                await copy.StartRowAsync(cancellationToken);
                await copy.WriteAsync(textId, NpgsqlDbType.Integer, cancellationToken);
                await copy.WriteAsync(reference, NpgsqlDbType.Integer, cancellationToken);
                await copy.WriteAsync(checked((int)row.Book), NpgsqlDbType.Integer, cancellationToken);
                await copy.WriteAsync(checked((int)row.Chapter), NpgsqlDbType.Integer, cancellationToken);
                await copy.WriteAsync(row.ByReading, NpgsqlDbType.Boolean, cancellationToken);
                await copy.WriteAsync(row.Verses, NpgsqlDbType.Text, cancellationToken);
            }

            await copy.CompleteAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }
}
