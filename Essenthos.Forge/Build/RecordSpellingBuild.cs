using System.Diagnostics;
using Essenthos.Core.Corpus;
using Essenthos.Core.Database;
using Essenthos.Core.Names;
using Essenthos.Core.Products;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;

namespace Essenthos.Core.Build;

/// <summary>One form a text prints for a record under one Strong number, as its product lists it.</summary>
internal sealed record RecordSpellingRow(string Record, string Number, string Form, long Occurrences)
{
    public static readonly ProductSchema<RecordSpellingRow> Schema = new ProductSchema<RecordSpellingRow>()
        .Text("record", r => r.Record)
        .Text("number", r => r.Number)
        .Text("form", r => r.Form)
        .Integer("occurrences", r => r.Occurrences)
        .Reads(row => new RecordSpellingRow(row.Text(0), row.Text(1), row.Text(2), row.Integer(3)));
}

/// <summary>What a spellings run did for one text: made or current, and the rows the table holds for it.</summary>
internal sealed record RecordSpellingOutcome(string Text, bool Made, int Rows, TimeSpan Elapsed);

/// <summary>
/// The spellings a record page reads (<c>record_spelling</c>, <see cref="RecordSpellings"/>): per text, how often it prints
/// each form for each record under each Strong number. A product of the serving stage (<c>build/products</c>, maker
/// <c>serve</c>): <c>build --into</c> counts every text once the database is filled, <c>build --apply</c> and
/// <c>forge record-words</c> only the texts whose inputs moved, each replaced in a transaction of its own.
/// <c>record-spellings/&lt;SLUG&gt;</c> lists what the table holds for the text, records by slug, and names the products
/// the counted tables were loaded from.
/// </summary>
internal sealed class RecordSpellingBuild(string connectionString, ProductStore store, IServiceProvider services, ILogger logger)
{
    public static string Product(string text) => $"record-spellings/{text}";

    /// <summary>
    /// The spellings of the texts named (every text when null): a text is counted when <paramref name="force"/> is set, its
    /// product is missing or its inputs moved, or the table does not hold the rows its product lists.
    /// </summary>
    public async Task<IReadOnlyList<RecordSpellingOutcome>> Run(IReadOnlyCollection<string>? only, bool force, CancellationToken cancellationToken)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.NoTracking;
        var texts = await db.Texts.OrderBy(t => t.Slug).Select(t => new { t.Id, t.Slug }).ToListAsync(cancellationToken);
        var held = await db.RecordSpellings.GroupBy(s => s.TextId).Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.Key, g => g.Count, cancellationToken);
        var slugs = await db.Entities.Select(e => new { e.Id, e.Slug }).ToDictionaryAsync(e => e.Id, e => e.Slug, cancellationToken);

        var outcomes = new List<RecordSpellingOutcome>();
        foreach (var text in texts.Where(t => only is null || only.Contains(t.Slug)))
        {
            var clock = Stopwatch.StartNew();
            var inputs = Inputs(text.Slug, texts.Select(t => t.Slug));
            var product = Product(text.Slug);
            if (!force && !store.NeedsBuild(product, ProductKind.Machine, inputs) && Listed(product) == held.GetValueOrDefault(text.Id))
            {
                outcomes.Add(new RecordSpellingOutcome(text.Slug, false, held.GetValueOrDefault(text.Id), clock.Elapsed));
                continue;
            }

            var counted = (await RecordSpellings.Count(db, text.Id, cancellationToken))
                .OrderBy(c => slugs[c.EntityId], StringComparer.Ordinal)
                .ThenBy(c => c.StrongNumber, StringComparer.Ordinal)
                .ThenBy(c => c.Form, StringComparer.Ordinal)
                .ToList();
            await Replace(text.Id, counted, cancellationToken);
            using (var writer = store.Create(product, ProductKind.Machine, RecordSpellingRow.Schema,
                       ProductHeader.DescribeProducer("forge record-words"), inputs))
            {
                foreach (var row in counted)
                {
                    writer.Write(new RecordSpellingRow(slugs[row.EntityId], row.StrongNumber, row.Form, row.Occurrences));
                }

                writer.Complete();
            }

            store.Save();
            logger.LogInformation("Record spellings of {Text}: {Rows} rows in {Seconds:0.0} s", text.Slug, counted.Count, clock.Elapsed.TotalSeconds);
            outcomes.Add(new RecordSpellingOutcome(text.Slug, true, counted.Count, clock.Elapsed));
        }

        return outcomes;
    }

    /// <summary>How many rows a text's product lists, or -1 where there is none.</summary>
    private int Listed(string product)
    {
        if (!store.Exists(product))
        {
            return -1;
        }

        using var reader = store.Open(product, RecordSpellingRow.Schema);
        return reader.Rows().Count();
    }

    /// <summary>
    /// The products the counted tables were loaded from, where this store holds them: the text's words and the names on
    /// them, the records, and every text's words, whose Strong numbers a name carried from an original is counted under.
    /// </summary>
    internal List<ProductInput> Inputs(string text, IEnumerable<string> texts)
    {
        var products = new List<string> { $"texts/{text}/words", NameProducts.Projected(text), "records/records" };
        products.AddRange(texts.Select(t => $"texts/{t}/words"));
        return [.. store.Inputs(products.Distinct(StringComparer.Ordinal).Select(store.Find).OfType<string>().Order(StringComparer.Ordinal))];
    }

    /// <summary>A text's spellings replaced in one transaction: a failure leaves the rows it had.</summary>
    private async Task Replace(int textId, List<RecordSpellingCount> rows, CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using (var delete = new NpgsqlCommand("DELETE FROM record_spelling WHERE text_id = @text", connection, transaction))
        {
            delete.Parameters.AddWithValue("text", textId);
            await delete.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var copy = await connection.BeginBinaryImportAsync(
                         "COPY record_spelling (entity_id, text_id, strong_number, form, occurrences) FROM STDIN (FORMAT BINARY)", cancellationToken))
        {
            foreach (var row in rows)
            {
                await copy.StartRowAsync(cancellationToken);
                await copy.WriteAsync(row.EntityId, NpgsqlDbType.Integer, cancellationToken);
                await copy.WriteAsync(textId, NpgsqlDbType.Integer, cancellationToken);
                await copy.WriteAsync(row.StrongNumber, NpgsqlDbType.Text, cancellationToken);
                await copy.WriteAsync(row.Form, NpgsqlDbType.Text, cancellationToken);
                await copy.WriteAsync(row.Occurrences, NpgsqlDbType.Integer, cancellationToken);
            }

            await copy.CompleteAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }
}
