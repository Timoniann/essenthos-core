using System.Diagnostics;
using Essenthos.Core.Corpus;
using Essenthos.Core.Database;
using Essenthos.Core.Names;
using Essenthos.Core.Products;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;

namespace Essenthos.Core.Build;

/// <summary>Where a record's name first stands in one verse of a text, as its product lists it.</summary>
internal sealed record RecordPositionRow(string Record, int Book, int Chapter, int Verse, int Position)
{
    public static readonly ProductSchema<RecordPositionRow> Schema = new ProductSchema<RecordPositionRow>()
        .Text("record", r => r.Record)
        .Integer("book", r => r.Book)
        .Integer("chapter", r => r.Chapter)
        .Integer("verse", r => r.Verse)
        .Integer("position", r => r.Position)
        .Reads(row => new RecordPositionRow(row.Text(0), (int)row.Integer(1), (int)row.Integer(2), (int)row.Integer(3), (int)row.Integer(4)));
}

/// <summary>
/// The positions a record page orders a verse's names by (<c>record_position</c>, <see cref="RecordPositions"/>), for the
/// text it reads that order in. A product of the serving stage (<c>build/products</c>, maker <c>serve</c>), made like the
/// record spellings: <c>build --into</c> counts it once the database is filled, <c>build --apply</c> and
/// <c>forge record-words</c> when its inputs moved or the table does not hold the rows its product lists.
/// </summary>
internal sealed class RecordPositionBuild(string connectionString, ProductStore store, IServiceProvider services, ILogger logger)
{
    public static string Product(string text) => $"record-positions/{text}";

    public async Task<RecordSpellingOutcome?> Run(bool force, CancellationToken cancellationToken)
    {
        var clock = Stopwatch.StartNew();
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.NoTracking;
        var text = await db.Texts.Where(t => t.Slug == RecordPositions.Text).Select(t => new { t.Id, t.Slug }).SingleOrDefaultAsync(cancellationToken);
        if (text is null)
        {
            return null;
        }

        var held = await db.RecordPositions.CountAsync(p => p.TextId == text.Id, cancellationToken);
        var product = Product(text.Slug);
        var inputs = store.Inputs(
            new[] { $"texts/{text.Slug}/words", $"texts/{text.Slug}/verses", $"texts/{text.Slug}/books", NameProducts.Projected(text.Slug), "records/records" }
                .Select(store.Find).OfType<string>().Order(StringComparer.Ordinal)).ToList();
        if (!force && !store.NeedsBuild(product, ProductKind.Machine, inputs) && Listed(product) == held)
        {
            return new RecordSpellingOutcome(text.Slug, false, held, clock.Elapsed);
        }

        var slugs = await db.Entities.Select(e => new { e.Id, e.Slug }).ToDictionaryAsync(e => e.Id, e => e.Slug, cancellationToken);
        var counted = (await RecordPositions.Count(db, text.Id, cancellationToken))
            .OrderBy(p => slugs[p.EntityId], StringComparer.Ordinal)
            .ThenBy(p => p.Book).ThenBy(p => p.Chapter).ThenBy(p => p.Verse)
            .ToList();
        await Replace(text.Id, counted, cancellationToken);
        using (var writer = store.Create(product, ProductKind.Machine, RecordPositionRow.Schema,
                   ProductHeader.DescribeProducer("forge record-words"), inputs))
        {
            foreach (var row in counted)
            {
                writer.Write(new RecordPositionRow(slugs[row.EntityId], row.Book, row.Chapter, row.Verse, row.Position));
            }

            writer.Complete();
        }

        store.Save();
        logger.LogInformation("Record positions of {Text}: {Rows} rows in {Seconds:0.0} s", text.Slug, counted.Count, clock.Elapsed.TotalSeconds);
        return new RecordSpellingOutcome(text.Slug, true, counted.Count, clock.Elapsed);
    }

    private int Listed(string product)
    {
        if (!store.Exists(product))
        {
            return -1;
        }

        using var reader = store.Open(product, RecordPositionRow.Schema);
        return reader.Rows().Count();
    }

    /// <summary>The text's positions replaced in one transaction: a failure leaves the rows it had.</summary>
    private async Task Replace(int textId, List<RecordPositionCount> rows, CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using (var delete = new NpgsqlCommand("DELETE FROM record_position WHERE text_id = @text", connection, transaction))
        {
            delete.Parameters.AddWithValue("text", textId);
            await delete.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var copy = await connection.BeginBinaryImportAsync(
                         "COPY record_position (entity_id, text_id, book, chapter, verse, position) FROM STDIN (FORMAT BINARY)", cancellationToken))
        {
            foreach (var row in rows)
            {
                await copy.StartRowAsync(cancellationToken);
                await copy.WriteAsync(row.EntityId, NpgsqlDbType.Integer, cancellationToken);
                await copy.WriteAsync(textId, NpgsqlDbType.Integer, cancellationToken);
                await copy.WriteAsync(row.Book, NpgsqlDbType.Integer, cancellationToken);
                await copy.WriteAsync(row.Chapter, NpgsqlDbType.Integer, cancellationToken);
                await copy.WriteAsync(row.Verse, NpgsqlDbType.Integer, cancellationToken);
                await copy.WriteAsync(row.Position, NpgsqlDbType.Integer, cancellationToken);
            }

            await copy.CompleteAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }
}
