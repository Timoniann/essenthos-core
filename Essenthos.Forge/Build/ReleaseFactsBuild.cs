using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Essenthos.Core.Endpoints;
using Essenthos.Core.Products;
using Npgsql;
using NpgsqlTypes;

namespace Essenthos.Core.Build;

/// <summary>One constant of the release (<see cref="ReleaseFacts"/>), as its product lists it.</summary>
internal sealed record ReleaseFactRow(string Key, string Stamp, string Body)
{
    public static readonly ProductSchema<ReleaseFactRow> Schema = new ProductSchema<ReleaseFactRow>()
        .Text("key", r => r.Key)
        .Text("stamp", r => r.Stamp)
        .Text("body", r => r.Body)
        .Reads(row => new ReleaseFactRow(row.Text(0), row.Text(1), row.Text(2)));
}

/// <summary>What a release-facts run did: whether the product was counted, and the rows the table took.</summary>
internal sealed record ReleaseFactsOutcome(bool Made, long Rows, TimeSpan Elapsed);

/// <summary>
/// The constants of the release the API reads instead of counting them at every start (<see cref="ReleaseFacts"/>), a product of
/// the serving stage (<c>build/products</c>, maker <c>serve</c>): <c>release-facts</c>, loaded as <c>release_fact</c>. It is
/// counted again from the tables where any product a table is filled from moved, or the API's counting code did (its stamp);
/// a table that does not hold the product's rows takes them from it, and nothing is counted.
/// </summary>
internal sealed class ReleaseFactsBuild(string connectionString, ProductStore store, IServiceProvider services, ILogger logger)
{
    public const string Product = "release-facts";

    private const string Producer = "forge facts";

    private const string StampNote = "stamp";

    /// <summary>The products no fact reads: the documents and panes the serving stage makes, and this product itself.</summary>
    private static readonly string[] Unread =
        ["Essenthos/page-documents/", "Essenthos/panes/", "Essenthos/strength/", $"Essenthos/{Product}."];

    public async Task<ReleaseFactsOutcome> Run(bool force, CancellationToken cancellationToken)
    {
        var clock = Stopwatch.StartNew();
        List<ReleaseFactRow>? rows = null;
        IReadOnlyList<ProductInput> inputs;
        lock (store)
        {
            var graph = ProductGraph.Read(store);
            inputs = [.. store.Inputs(store.Manifest.Entries
                .Select(entry => entry.Product)
                .Where(product => !Unread.Any(product.StartsWith)
                                  && graph.Find(product) is { } found && found.Declaration.Tables.Count > 0
                                  && File.Exists(store.FullPath(product)))
                .Order(StringComparer.Ordinal))];
            if (!force && !store.NeedsBuild(Product, ProductKind.Machine, inputs))
            {
                using var reader = store.Open(Product, ReleaseFactRow.Schema);
                if (reader.Header?.Notes.FirstOrDefault(n => n.Key == StampNote).Value == ReleaseFacts.Stamp)
                {
                    rows = [.. reader.Rows()];
                }
            }
        }

        var made = rows is null;
        if (rows is null)
        {
            var counted = await ReleaseFacts.Count(services.GetRequiredService<IServiceScopeFactory>(), cancellationToken);
            rows = [.. counted.Select(f => new ReleaseFactRow(f.Key, ReleaseFacts.Stamp, f.Body)).OrderBy(r => r.Key, StringComparer.Ordinal)];
            lock (store)
            {
                using var writer = store.Create(Product, ProductKind.Machine, ReleaseFactRow.Schema, ProductHeader.DescribeProducer(Producer),
                    inputs, header => header.Notes.Add(new(StampNote, ReleaseFacts.Stamp)));
                foreach (var row in rows)
                {
                    writer.Write(row);
                }

                writer.Complete();
                store.Save();
            }

            logger.LogInformation("Release facts: {Facts} counted from the tables in {Seconds:0.0} s", rows.Count, clock.Elapsed.TotalSeconds);
        }

        return new ReleaseFactsOutcome(made, await Match(rows, cancellationToken), clock.Elapsed);
    }

    /// <summary>The table replaced in one transaction with the product's rows where it does not hold exactly them; returns the rows written.</summary>
    private async Task<long> Match(IReadOnlyList<ReleaseFactRow> rows, CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        var expected = string.Join('\u001e', rows.Select(r => $"{r.Key}\u001f{r.Stamp}\u001f{Md5(r.Body)}"));
        await using (var held = new NpgsqlCommand(
                         "SELECT coalesce(string_agg(concat_ws(chr(31), key, stamp, md5(body)), chr(30) ORDER BY key COLLATE \"C\"), '') FROM release_fact",
                         connection))
        {
            if ((string?)await held.ExecuteScalarAsync(cancellationToken) == expected)
            {
                return 0;
            }
        }

        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using (var delete = new NpgsqlCommand("DELETE FROM release_fact", connection, transaction))
        {
            await delete.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var copy = await connection.BeginBinaryImportAsync("COPY release_fact (key, stamp, body) FROM STDIN (FORMAT BINARY)", cancellationToken))
        {
            foreach (var row in rows)
            {
                await copy.StartRowAsync(cancellationToken);
                await copy.WriteAsync(row.Key, NpgsqlDbType.Text, cancellationToken);
                await copy.WriteAsync(row.Stamp, NpgsqlDbType.Text, cancellationToken);
                await copy.WriteAsync(row.Body, NpgsqlDbType.Text, cancellationToken);
            }

            await copy.CompleteAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return rows.Count;
    }

    private static string Md5(string text) => Convert.ToHexStringLower(MD5.HashData(Encoding.UTF8.GetBytes(text)));
}
