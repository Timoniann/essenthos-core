using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using Essenthos.Core.Configuration;
using Essenthos.Core.Corpus;
using Essenthos.Core.Database;
using Essenthos.Core.Endpoints;
using Essenthos.Core.Pages;
using Essenthos.Core.Products;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using NpgsqlTypes;

namespace Essenthos.Core.Build;

/// <summary>One page's stored document: its address, the hash and length of the uncompressed document, and the gzip as base64.</summary>
internal sealed record PageDocumentRow(string Address, string Hash, long Bytes, string Body)
{
    public static readonly ProductSchema<PageDocumentRow> Schema = new ProductSchema<PageDocumentRow>()
        .Text("address", r => r.Address)
        .Text("hash", r => r.Hash)
        .Integer("bytes", r => r.Bytes)
        .Text("body", r => r.Body)
        .Reads(row => new PageDocumentRow(row.Text(0), row.Text(1), row.Integer(2), row.Text(3)));
}

/// <summary>What a page document run did: the products made, the rows the table took, the documents and their bytes.</summary>
internal sealed record PageDocumentOutcome(int Made, long Rows, long Documents, long Bytes, long Compressed, TimeSpan Elapsed);

/// <summary>
/// The documents record and Strong pages are written from (TSK-1670), products of the serving stage (<c>build/products</c>,
/// maker <c>serve</c>): <c>page-documents/{kind}/{language}</c> holds every record's (kind <c>record</c>) or Strong number's
/// (kind <c>strong</c>) document in one site language, composed by the API's own <see cref="PageLoader"/> from the database
/// as it now stands, loaded as <c>page_document</c>. A product is made again where any product the database was built from
/// moved, where the owner's switches the pages are composed under moved, or where the API's code did (its module version);
/// a table that does not hold the rows of a current product takes them from it, and nothing is composed.
/// </summary>
internal sealed class PageDocumentBuild(
    string connectionString, ProductStore store, IServiceProvider services, IConfiguration configuration, ILogger logger)
{
    public static readonly string[] Kinds = [PageDocuments.RecordKind, PageDocuments.StrongKind];

    public static string Product(string kind, SiteLanguage language) => $"page-documents/{kind}/{language.Code}";

    private const string Producer = "forge pages";

    /// <summary>How many pages of each kind and language the run composes again to hold the stored ones to.</summary>
    public const int Sample = 50;

    /// <summary>How many pages are composed at once, each with a database connection of its own.</summary>
    public const int Parallelism = 8;

    /// <summary>The products a page document does not read: itself, and the panes and strength, which no record or Strong page reads.</summary>
    private static readonly string[] Unread = ["Essenthos/page-documents/", "Essenthos/panes/", "Essenthos/strength/"];

    public async Task<PageDocumentOutcome> Run(bool force, CancellationToken cancellationToken)
    {
        var clock = Stopwatch.StartNew();
        var settings = new SiteSettingsFile(SettingsPath(), NullLogger<SiteSettingsFile>.Instance);
        var places = new PlacePictures(ImagesPath());
        using var cache = new PageCache();
        var downloads = new DownloadsCatalogue(null, null, NullLogger<DownloadsCatalogue>.Instance);
        var graph = ProductGraph.Read(store);
        var inputs = store.Inputs(store.Manifest.Entries
            .Select(entry => entry.Product)
            .Where(product => !Unread.Any(product.StartsWith)
                              && graph.Find(product) is { } found && found.Declaration.Tables.Count > 0
                              && File.Exists(store.FullPath(product)))
            .Order(StringComparer.Ordinal));
        var notes = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["version"] = PageDocuments.Version.ToString(CultureInfo.InvariantCulture),
            ["api"] = typeof(PageLoader).Assembly.ManifestModule.ModuleVersionId.ToString("N"),
            ["settings"] = $"{settings.Choices[SiteSettings.ReaderTranslation]}|{settings.Is(SiteSettings.GeneratedImages)}",
        };

        // Every count of how a text renders an original is counted over the same links for every page of this corpus.
        LinkedOriginals.Kept.Value = new ConcurrentDictionary<int, List<LinkedOriginal>>();
        try
        {
            List<string> slugs;
            List<string> numbers;
            using (var scope = services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                slugs = await db.Entities.AsNoTracking().OrderBy(e => e.Slug).Select(e => e.Slug).ToListAsync(cancellationToken);
                numbers = await db.StrongEntries.AsNoTracking().Select(s => s.StrongNumber).Distinct().ToListAsync(cancellationToken);
                numbers.Sort(StringComparer.Ordinal);
            }

            var made = 0;
            long rows = 0, documents = 0, bytes = 0, compressed = 0;
            foreach (var kind in Kinds)
            {
                var addresses = kind == PageDocuments.RecordKind ? slugs : numbers;
                foreach (var language in SiteLanguage.All)
                {
                    var product = Product(kind, language);
                    var stored = force ? null : Current(product, inputs, notes);
                    if (stored is null)
                    {
                        var started = clock.Elapsed;
                        stored = await Compose(kind, language, addresses, settings, cache, downloads, places, cancellationToken);
                        Write(product, stored, inputs, notes);
                        made++;
                        logger.LogInformation("Page documents {Kind}/{Language}: {Rows} documents, {Bytes} bytes, {Compressed} compressed in {Seconds:0.0} s",
                            kind, language.Code, stored.Count, stored.Sum(r => r.Bytes), stored.Sum(r => (long)r.Body.Length * 3 / 4),
                            (clock.Elapsed - started).TotalSeconds);
                    }

                    documents += stored.Count;
                    bytes += stored.Sum(r => r.Bytes);
                    compressed += stored.Sum(r => (long)r.Body.Length * 3 / 4);
                    rows += await Match(kind, language, stored, cancellationToken);
                    await Check(kind, language, stored, settings, cache, downloads, places, cancellationToken);
                    store.Save();
                }
            }

            if (rows > 0)
            {
                await using var connection = new NpgsqlConnection(connectionString);
                await connection.OpenAsync(cancellationToken);
                await using var analyze = new NpgsqlCommand("ANALYZE page_document", connection);
                await analyze.ExecuteNonQueryAsync(cancellationToken);
            }

            return new PageDocumentOutcome(made, rows, documents, bytes, compressed, clock.Elapsed);
        }
        finally
        {
            LinkedOriginals.Kept.Value = null;
        }
    }

    /// <summary>The owner's switches the API is published with: <c>Site:SettingsPath</c>, else the API project's own file beside Resources.</summary>
    private string SettingsPath() =>
        configuration[SiteSettingsFile.ConfigurationKey] is { Length: > 0 } configured
            ? Path.GetFullPath(configured)
            : Path.GetFullPath(Path.Combine(store.ResourcesPath, "..", "Essenthos.Api", SiteSettings.FileName));

    /// <summary>The pictures' folder: <c>Images:Path</c>, else <c>Resources/Images</c>.</summary>
    private string ImagesPath() =>
        configuration[ImageEndpoints.ConfigurationKey] is { Length: > 0 } configured
            ? Path.GetFullPath(configured)
            : Path.Combine(store.ResourcesPath, "Images");

    private async Task<List<PageDocumentRow>> Compose(
        string kind, SiteLanguage language, IReadOnlyList<string> addresses, SiteSettingsFile settings, PageCache cache,
        DownloadsCatalogue downloads, PlacePictures places, CancellationToken cancellationToken)
    {
        var queue = new ConcurrentQueue<string>(addresses);
        var made = new ConcurrentBag<PageDocumentRow>();
        var canon = services.GetRequiredService<ICanonIndex>();
        await Task.WhenAll(Enumerable.Range(0, Parallelism).Select(_ => Task.Run(async () =>
        {
            using var scope = services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.NoTracking;
            var loader = new PageLoader(db, canon, settings, cache, downloads, places);
            while (queue.TryDequeue(out var address))
            {
                if (await Packed(loader, kind, language, address, cancellationToken) is { } row)
                {
                    made.Add(row);
                }

                db.ChangeTracker.Clear();
            }
        }, cancellationToken)));
        return [.. made.OrderBy(r => r.Address, StringComparer.Ordinal)];
    }

    private static async Task<PageDocumentRow?> Packed(
        PageLoader loader, string kind, SiteLanguage language, string address, CancellationToken cancellationToken)
    {
        var (body, hash, length) = kind == PageDocuments.RecordKind
            ? await loader.RecordDocument(address, language, cancellationToken) is { } record
                ? Pack(record, record.Data, PageDocuments.Record)
                : default
            : await loader.StrongDocument(address, language, cancellationToken) is { } strong
                ? Pack(strong, strong.Data, PageDocuments.Strong)
                : default;
        return body is null ? null : new PageDocumentRow(address, hash, length, Convert.ToBase64String(body));
    }

    private static (byte[] Body, string Hash, long Length) Pack<T>(T document, byte[]? data, System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> info)
    {
        var (body, hash, length) = PageDocuments.Pack(document, data, info);
        return (body, hash, length);
    }

    /// <summary>A product's rows where it is current for these inputs and was made under these notes; null where it must be made.</summary>
    private List<PageDocumentRow>? Current(string product, IReadOnlyList<ProductInput> inputs, IReadOnlyDictionary<string, string> notes)
    {
        if (store.NeedsBuild(product, ProductKind.Machine, inputs))
        {
            return null;
        }

        using var reader = store.Open(product, PageDocumentRow.Schema);
        var said = reader.Header?.Notes.ToDictionary(n => n.Key, n => n.Value, StringComparer.Ordinal) ?? [];
        return notes.All(note => said.GetValueOrDefault(note.Key) == note.Value) ? [.. reader.Rows()] : null;
    }

    private void Write(string product, IEnumerable<PageDocumentRow> rows, IReadOnlyList<ProductInput> inputs, IReadOnlyDictionary<string, string> notes)
    {
        using var writer = store.Create(product, ProductKind.Machine, PageDocumentRow.Schema, ProductHeader.DescribeProducer(Producer), inputs,
            header => header.Notes.AddRange(notes));
        foreach (var row in rows)
        {
            writer.Write(row);
        }

        writer.Complete();
    }

    /// <summary>
    /// The table made to hold exactly the product's rows for this kind and language: replaced in one transaction where its
    /// count or the md5 of its addresses and hashes differs. Returns the rows written.
    /// </summary>
    private async Task<long> Match(string kind, SiteLanguage language, IReadOnlyList<PageDocumentRow> rows, CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        var joined = string.Join('\u001e', rows.Select(r => $"{r.Address}\u001f{r.Hash}"));
        var hash = joined.Length == 0 ? "" : Convert.ToHexStringLower(System.Security.Cryptography.MD5.HashData(Encoding.UTF8.GetBytes(joined)));
        await using (var held = new NpgsqlCommand(
                         "SELECT count(*), coalesce(md5(string_agg(address || chr(31) || hash, chr(30) ORDER BY address COLLATE \"C\")), '') "
                         + "FROM page_document WHERE kind = @kind AND language = @language", connection))
        {
            held.Parameters.AddWithValue("kind", kind);
            held.Parameters.AddWithValue("language", language.Code);
            await using var reader = await held.ExecuteReaderAsync(cancellationToken);
            await reader.ReadAsync(cancellationToken);
            if (reader.GetInt64(0) == rows.Count && reader.GetString(1) == hash)
            {
                return 0;
            }
        }

        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using (var delete = new NpgsqlCommand("DELETE FROM page_document WHERE kind = @kind AND language = @language", connection, transaction))
        {
            delete.Parameters.AddWithValue("kind", kind);
            delete.Parameters.AddWithValue("language", language.Code);
            await delete.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var copy = await connection.BeginBinaryImportAsync(
                         "COPY page_document (kind, language, address, body, hash) FROM STDIN (FORMAT BINARY)", cancellationToken))
        {
            foreach (var row in rows)
            {
                await copy.StartRowAsync(cancellationToken);
                await copy.WriteAsync(kind, NpgsqlDbType.Text, cancellationToken);
                await copy.WriteAsync(language.Code, NpgsqlDbType.Text, cancellationToken);
                await copy.WriteAsync(row.Address, NpgsqlDbType.Text, cancellationToken);
                await copy.WriteAsync(Convert.FromBase64String(row.Body), NpgsqlDbType.Bytea, cancellationToken);
                await copy.WriteAsync(row.Hash, NpgsqlDbType.Text, cancellationToken);
            }

            await copy.CompleteAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return rows.Count;
    }

    /// <summary>
    /// A sample of the stored documents held to the pages composed again from the tables as they stand, drawn from every part
    /// of the list: the same document, and one that reads back as itself. A difference fails the run, naming the pages.
    /// </summary>
    private async Task Check(
        string kind, SiteLanguage language, IReadOnlyList<PageDocumentRow> stored, SiteSettingsFile settings, PageCache cache,
        DownloadsCatalogue downloads, PlacePictures places, CancellationToken cancellationToken)
    {
        if (stored.Count == 0)
        {
            return;
        }

        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.NoTracking;
        var loader = new PageLoader(db, services.GetRequiredService<ICanonIndex>(), settings, cache, downloads, places);
        var differences = new List<string>();
        foreach (var row in Enumerable.Range(0, Math.Min(Sample, stored.Count)).Select(i => stored[(int)((long)i * stored.Count / Math.Min(Sample, stored.Count))]))
        {
            var again = await Packed(loader, kind, language, row.Address, cancellationToken);
            var body = Convert.FromBase64String(row.Body);
            var reread = kind == PageDocuments.RecordKind
                ? Reread(body, PageDocuments.Record)
                : Reread(body, PageDocuments.Strong);
            if (again?.Hash != row.Hash || reread != row.Hash)
            {
                differences.Add(row.Address);
            }
        }

        if (differences.Count > 0)
        {
            throw new InvalidOperationException(
                $"Stored {kind} page documents in {language.Code} differ from those composed again from the tables: {string.Join(", ", differences)}. "
                + "Raise PageDocuments.Version with a change to what a page is composed from, then build again.");
        }

        logger.LogInformation("Page check {Kind}/{Language}: {Count} documents composed again equal the stored ones", kind, language.Code,
            Math.Min(Sample, stored.Count));

        static string Reread<T>(byte[] body, System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> info)
        {
            var (document, answers) = PageDocuments.Unpack(body, info);
            return PageDocuments.Pack(document, answers, info).Hash;
        }
    }
}
