using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Threading.Channels;
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
/// The documents record, Strong and occupation pages are written from (TSK-1670, TSK-1819), products of the serving stage
/// (<c>build/products</c>, maker <c>serve</c>): <c>page-documents/{kind}/{language}</c> holds every record's (kind
/// <c>record</c>), Strong number's (kind <c>strong</c>) or occupation's (kind <c>occupation</c>) document in one site language,
/// composed by the API's own <see cref="PageLoader"/> from the database as it now stands, loaded as <c>page_document</c>.
/// Each document names the commit that composed it (<see cref="PageDocuments.Build"/>), and only an API built from that
/// commit reads it. A product is made again where any product the database was built from moved, where the owner's switches
/// the pages are composed under moved, or where the API's code did (its module version, which moves with the commit too);
/// a table that does not hold the rows of a current product takes them from it, and nothing is composed.
/// <para>
/// Every page of every product that must be made, and every page the check composes again, goes through one set of workers,
/// each with a database connection of its own, and the machine is held to the number of workers asked for
/// (<see cref="ParallelKey"/>, half the cores where unset). One record's or Strong number's pages in every language to be made
/// are one piece of work, composed one after another so that what they read alike is read once (<see cref="SharedReads"/>);
/// what every page reads alike, a text's own facts and the verses record pages quote, is read once per run
/// (<see cref="SharedReads.Corpus"/>). The pages the check
/// composes again are queued first, each language on its own and reading everything itself as a request does, so they also
/// hold the shared reading to the plain one; a product is then written, loaded and checked as soon as its last page is composed.
/// </para>
/// </summary>
internal sealed class PageDocumentBuild(
    string connectionString, ProductStore store, IServiceProvider services, IConfiguration configuration, ILogger logger)
{
    public static readonly string[] Kinds = [PageDocuments.RecordKind, PageDocuments.StrongKind, PageDocuments.OccupationKind];

    public static string Product(string kind, SiteLanguage language) => $"page-documents/{kind}/{language.Code}";

    private const string Producer = "forge pages";

    /// <summary>How many pages of each kind and language the run composes again to hold the stored ones to.</summary>
    public const int Sample = 50;

    /// <summary>How many pages are composed at once; half the machine's cores where unset.</summary>
    public const string ParallelKey = "Pages:Parallel";

    /// <summary>The workers a run takes where the caller does not say: <see cref="ParallelKey"/>, else half the cores.</summary>
    public int Parallelism => configuration.GetValue(ParallelKey, 0) is > 0 and var configured
        ? configured
        : Math.Max(1, Environment.ProcessorCount / 2);

    /// <summary>The products a page document does not read: itself, and the panes and strength, which no record, Strong or occupation page reads.</summary>
    private static readonly string[] Unread = ["Essenthos/page-documents/", "Essenthos/panes/", "Essenthos/strength/"];

    /// <summary>The manifest and the hashes are one per store, so one product at a time is written and saved.</summary>
    private readonly SemaphoreSlim _storeHeld = new(1, 1);

    public async Task<PageDocumentOutcome> Run(bool force, CancellationToken cancellationToken, int? parallel = null)
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
            ["build"] = PageDocuments.Build ?? "",
            ["settings"] = $"{settings.Choices[SiteSettings.ReaderTranslation]}|{settings.Is(SiteSettings.GeneratedImages)}",
        };

        // Every line that names descendants reads the same family tree for every page of this corpus, and every record page
        // quotes its verses from the same few texts.
        KinFacts.Kept.Value = new KinFacts.KeptDescents();
        SharedReads.Corpus.Value = new SharedReads.CorpusReads();
        using var stopping = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var workers = parallel is > 0 ? parallel.Value : Parallelism;
        var pool = new PageWorkers(services, settings, cache, downloads, places, workers, stopping.Token);
        try
        {
            List<string> slugs;
            List<string> numbers;
            List<string> occupations;
            using (var scope = services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                slugs = await db.Entities.AsNoTracking().OrderBy(e => e.Slug).Select(e => e.Slug).ToListAsync(cancellationToken);
                numbers = await db.StrongEntries.AsNoTracking().Select(s => s.StrongNumber).Distinct().ToListAsync(cancellationToken);
                numbers.Sort(StringComparer.Ordinal);
                occupations = await db.Occupations.AsNoTracking().Select(o => o.Slug).ToListAsync(cancellationToken);
                occupations.Sort(StringComparer.Ordinal);
            }

            IReadOnlyList<string> Addresses(string kind) => kind switch
            {
                PageDocuments.RecordKind => slugs,
                PageDocuments.StrongKind => numbers,
                _ => occupations,
            };

            // Whether each product is current is asked of the store one after another. The pages the check composes again go
            // first, each language on its own, so a product is checked the moment its last page is in; then every record's
            // pages (the slowest), each record in every language to be made at once, then the Strong numbers', then the
            // occupations'.
            var current = (
                from kind in Kinds
                from language in SiteLanguage.All
                let product = Product(kind, language)
                select (kind, language, product, Made: force || !IsCurrent(product, inputs, notes))).ToList();
            var samples = current.ToDictionary(plan => plan.product, plan =>
            {
                var sample = Sampled(Addresses(plan.kind));
                return new PageSample(sample, pool.Compose(plan.kind, [plan.language], sample, together: false));
            });
            var composed = new Dictionary<string, (List<SiteLanguage> Languages, Task<PageDocumentRow?[][]> Pages)>();
            foreach (var kind in Kinds)
            {
                var languages = current.Where(p => p.kind == kind && p.Made).Select(p => p.language).ToList();
                if (languages.Count > 0)
                {
                    composed[kind] = (languages, pool.Compose(kind, languages, Addresses(kind), together: true));
                }
            }

            logger.LogInformation("Page documents: {Made} of {Products} products to compose, {Workers} pages at once",
                current.Count(p => p.Made), current.Count, workers);
            var outcomes = await Task.WhenAll(current.Select(plan => Task.Run(async () =>
            {
                try
                {
                    var pages = plan.Made && composed.TryGetValue(plan.kind, out var made)
                        ? Slice(made.Pages, made.Languages.IndexOf(plan.language))
                        : null;
                    return await Make(plan.kind, plan.language, plan.product, pages,
                        samples[plan.product], inputs, notes, clock, stopping.Token);
                }
                catch
                {
                    await stopping.CancelAsync();
                    throw;
                }
            }, stopping.Token)));
            await pool.Finish();

            var rows = outcomes.Sum(o => o.Rows);
            if (rows > 0)
            {
                await using var connection = new NpgsqlConnection(connectionString);
                await connection.OpenAsync(cancellationToken);
                await using var analyze = new NpgsqlCommand("ANALYZE page_document", connection);
                await analyze.ExecuteNonQueryAsync(cancellationToken);
            }

            return new PageDocumentOutcome(
                outcomes.Count(o => o.Made), rows, outcomes.Sum(o => o.Documents), outcomes.Sum(o => o.Bytes),
                outcomes.Sum(o => o.Compressed), clock.Elapsed);
        }
        catch
        {
            await stopping.CancelAsync();
            await pool.Abandon();
            throw;
        }
        finally
        {
            KinFacts.Kept.Value = null;
            SharedReads.Corpus.Value = null;
        }
    }

    private sealed record ProductOutcome(bool Made, long Rows, long Documents, long Bytes, long Compressed);

    /// <summary>The pages of a product the check composes again, and their documents, each composed on its own.</summary>
    private sealed record PageSample(IReadOnlyList<string> Addresses, Task<PageDocumentRow?[][]> Composed);

    /// <summary>The addresses the check composes again: <see cref="Sample"/> of them, drawn from every part of the list.</summary>
    private static List<string> Sampled(IReadOnlyList<string> addresses)
    {
        var count = Math.Min(Sample, addresses.Count);
        return [.. Enumerable.Range(0, count).Select(i => addresses[(int)((long)i * addresses.Count / count)])];
    }

    private static async Task<PageDocumentRow?[]> Slice(Task<PageDocumentRow?[][]> pages, int language) => (await pages)[language];

    /// <summary>
    /// One product: its pages as the workers composed them (read from the product where it is current), the product written,
    /// the table made to hold its rows, and the check.
    /// </summary>
    private async Task<ProductOutcome> Make(
        string kind, SiteLanguage language, string product, Task<PageDocumentRow?[]>? composed, PageSample sample,
        IReadOnlyList<ProductInput> inputs, IReadOnlyDictionary<string, string> notes, Stopwatch clock, CancellationToken cancellationToken)
    {
        List<PageDocumentRow> stored;
        if (composed is null)
        {
            using var reader = store.Open(product, PageDocumentRow.Schema);
            stored = [.. reader.Rows()];
        }
        else
        {
            stored = [.. (await composed).OfType<PageDocumentRow>()];
            await _storeHeld.WaitAsync(cancellationToken);
            try
            {
                Write(product, stored, inputs, notes);
                store.Save();
            }
            finally
            {
                _storeHeld.Release();
            }

            logger.LogInformation("Page documents {Kind}/{Language}: {Rows} documents, {Bytes} bytes, {Compressed} compressed, done at {Seconds:0.0} s",
                kind, language.Code, stored.Count, stored.Sum(r => r.Bytes), stored.Sum(r => (long)r.Body.Length * 3 / 4),
                clock.Elapsed.TotalSeconds);
        }

        var rows = await Match(kind, language, stored, cancellationToken);
        await Check(kind, language, stored, sample);
        return new ProductOutcome(composed is not null, rows, stored.Count, stored.Sum(r => r.Bytes), stored.Sum(r => (long)r.Body.Length * 3 / 4));
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

    private static async Task<PageDocumentRow?> Packed(
        PageLoader loader, string kind, SiteLanguage language, string address, CancellationToken cancellationToken)
    {
        var (body, hash, length) = kind switch
        {
            PageDocuments.RecordKind => await loader.RecordDocument(address, language, cancellationToken) is { } record
                ? Pack(record, record.Data, PageDocuments.Record)
                : default,
            PageDocuments.StrongKind => await loader.StrongDocument(address, language, cancellationToken) is { } strong
                ? Pack(strong, strong.Data, PageDocuments.Strong)
                : default,
            _ => await loader.OccupationDocument(address, language, cancellationToken) is { } occupation
                ? Pack(occupation, occupation.Data, PageDocuments.Occupation)
                : default,
        };
        return body is null ? null : new PageDocumentRow(address, hash, length, Convert.ToBase64String(body));
    }

    private static (byte[] Body, string Hash, long Length) Pack<T>(T document, byte[]? data, System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> info)
    {
        var (body, hash, length) = PageDocuments.Pack(document, data, info);
        return (body, hash, length);
    }

    /// <summary>Whether a product is current for these inputs and was made under these notes.</summary>
    private bool IsCurrent(string product, IReadOnlyList<ProductInput> inputs, IReadOnlyDictionary<string, string> notes)
    {
        if (store.NeedsBuild(product, ProductKind.Machine, inputs))
        {
            return false;
        }

        using var reader = store.Open(product, PageDocumentRow.Schema);
        var said = reader.Header?.Notes.ToDictionary(n => n.Key, n => n.Value, StringComparer.Ordinal) ?? [];
        return notes.All(note => said.GetValueOrDefault(note.Key) == note.Value);
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
    /// of the list and each composed on its own, apart from the other languages: the same document (or none where the product
    /// has none), and one that reads back as itself. A difference fails the run, naming the pages.
    /// </summary>
    private async Task Check(string kind, SiteLanguage language, IReadOnlyList<PageDocumentRow> stored, PageSample sample)
    {
        var again = (await sample.Composed)[0];
        if (sample.Addresses.Count == 0)
        {
            return;
        }

        var held = stored.ToDictionary(row => row.Address, StringComparer.Ordinal);
        var differences = new List<string>();
        for (var i = 0; i < sample.Addresses.Count; i++)
        {
            if (held.GetValueOrDefault(sample.Addresses[i]) is not { } row)
            {
                if (again[i] is not null)
                {
                    differences.Add(sample.Addresses[i]);
                }

                continue;
            }

            var body = Convert.FromBase64String(row.Body);
            var reread = kind switch
            {
                PageDocuments.RecordKind => Reread(body, PageDocuments.Record),
                PageDocuments.StrongKind => Reread(body, PageDocuments.Strong),
                _ => Reread(body, PageDocuments.Occupation),
            };
            if (again[i]?.Hash != row.Hash || reread != row.Hash)
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
            sample.Addresses.Count);

        static string Reread<T>(byte[] body, System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> info)
        {
            var (document, answers) = PageDocuments.Unpack(body, info);
            return PageDocuments.Pack(document, answers, info).Hash;
        }
    }

    /// <summary>
    /// A fixed number of workers, each with a database context and a <see cref="PageLoader"/> of its own, composing the pages
    /// queued to them in the order they were queued. Anything a worker fails on fails the pages still waiting.
    /// </summary>
    private sealed class PageWorkers
    {
        private readonly Channel<Func<PageLoader, Task>> _queue =
            Channel.CreateUnbounded<Func<PageLoader, Task>>(new UnboundedChannelOptions { SingleReader = false, SingleWriter = false });

        private readonly ConcurrentQueue<TaskCompletionSource<PageDocumentRow?[][]>> _waiting = new();

        private readonly Task[] _workers;

        private readonly CancellationToken _token;

        public PageWorkers(
            IServiceProvider services, SiteSettingsFile settings, PageCache cache, DownloadsCatalogue downloads, PlacePictures places,
            int count, CancellationToken token)
        {
            _token = token;
            token.Register(() =>
            {
                foreach (var waiting in _waiting)
                {
                    waiting.TrySetCanceled(token);
                }
            });
            var canon = services.GetRequiredService<ICanonIndex>();
            _workers =
            [
                .. Enumerable.Range(0, Math.Max(1, count)).Select(_ => Task.Run(async () =>
                {
                    try
                    {
                        using var scope = services.CreateScope();
                        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                        db.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.NoTracking;
                        // One connection for the worker's whole run, rather than one taken from the pool and reset for each
                        // of the sixty-odd statements a page reads.
                        await db.Database.OpenConnectionAsync(token);
                        var loader = new PageLoader(db, canon, settings, cache, downloads, places);
                        await foreach (var page in _queue.Reader.ReadAllAsync(token))
                        {
                            await page(loader);
                            db.ChangeTracker.Clear();
                        }
                    }
                    catch (Exception failure) when (failure is not OperationCanceledException || !token.IsCancellationRequested)
                    {
                        foreach (var waiting in _waiting)
                        {
                            waiting.TrySetException(failure);
                        }

                        throw;
                    }
                }, token)),
            ];
        }

        /// <summary>
        /// The documents of these pages in each of these languages, in their order; null for an address that is no page. Each
        /// address is one piece of work, its languages composed one after another; <paramref name="together"/> lets them read
        /// what they read alike once (<see cref="SharedReads"/>), and without it each page reads everything itself, as a request
        /// does, the run's kept reads included.
        /// </summary>
        public Task<PageDocumentRow?[][]> Compose(string kind, IReadOnlyList<SiteLanguage> languages, IReadOnlyList<string> addresses, bool together)
        {
            var rows = languages.Select(_ => new PageDocumentRow?[addresses.Count]).ToArray();
            var done = new TaskCompletionSource<PageDocumentRow?[][]>(TaskCreationOptions.RunContinuationsAsynchronously);
            if (addresses.Count == 0)
            {
                done.SetResult(rows);
                return done.Task;
            }

            _waiting.Enqueue(done);
            var left = addresses.Count;
            for (var i = 0; i < addresses.Count; i++)
            {
                var index = i;
                _queue.Writer.TryWrite(async loader =>
                {
                    if (done.Task.IsCompleted)
                    {
                        return;
                    }

                    SharedReads.Current.Value = together ? new SharedReads() : null;
                    if (!together)
                    {
                        // Composed as a request composes it, reading everything itself.
                        SharedReads.Corpus.Value = null;
                        KinFacts.Kept.Value = null;
                    }

                    try
                    {
                        for (var language = 0; language < languages.Count; language++)
                        {
                            rows[language][index] = await Packed(loader, kind, languages[language], addresses[index], _token);
                        }
                    }
                    catch (Exception failure)
                    {
                        done.TrySetException(failure);
                        return;
                    }

                    if (Interlocked.Decrement(ref left) == 0)
                    {
                        done.TrySetResult(rows);
                    }
                });
            }

            return done.Task;
        }

        /// <summary>No more pages: the workers end once the queue is empty.</summary>
        public async Task Finish()
        {
            _queue.Writer.Complete();
            await Task.WhenAll(_workers);
        }

        /// <summary>The run failed: the workers are let go, whatever they end with.</summary>
        public async Task Abandon()
        {
            _queue.Writer.TryComplete();
            try
            {
                await Task.WhenAll(_workers);
            }
            catch (Exception)
            {
                // The failure that stopped the run is the one reported.
            }
        }
    }
}
