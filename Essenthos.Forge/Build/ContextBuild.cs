using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using Essenthos.Core.Database;
using Essenthos.Core.Products;
using Essenthos.Core.Records;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;

namespace Essenthos.Core.Build;

/// <summary>One canonical chapter's skeleton (<see cref="ChapterSkeleton"/>), as its product lists it.</summary>
internal sealed record ContextChapterRow(long Book, long Chapter, long Bytes, long Gzip, string Hash, string Body)
{
    public static readonly ProductSchema<ContextChapterRow> Schema = new ProductSchema<ContextChapterRow>()
        .Integer("book", r => r.Book)
        .Integer("chapter", r => r.Chapter)
        .Integer("bytes", r => r.Bytes)
        .Integer("gzip", r => r.Gzip)
        .Text("hash", r => r.Hash)
        .Text("body", r => r.Body)
        .Reads(row => new ContextChapterRow(row.Integer(0), row.Integer(1), row.Integer(2), row.Integer(3), row.Text(4), row.Text(5)));
}

/// <summary>One record's line in one language of names (<see cref="ContextLineBody"/>), in the product's language of words.</summary>
internal sealed record ContextLineRow(string Names, string Slug, string Body)
{
    public static readonly ProductSchema<ContextLineRow> Schema = new ProductSchema<ContextLineRow>()
        .Text("names", r => r.Names)
        .Text("slug", r => r.Slug)
        .Text("body", r => r.Body)
        .Reads(row => new ContextLineRow(row.Text(0), row.Text(1), row.Text(2)));
}

/// <summary>What a context run did: the products made, the rows the tables took, and the largest chapter.</summary>
internal sealed record ContextOutcome(int Made, long Rows, int Chapters, int Lines, long LargestGzip, string Largest, TimeSpan Elapsed);

/// <summary>
/// The chapter context as the serving build stores it (TSK-1669), products of the serving stage (<c>build/products</c>, maker
/// <c>serve</c>): <c>context/chapters</c> holds every canonical chapter's skeleton (<see cref="ChapterSkeletons"/>), loaded as
/// <c>context_chapter</c>; <c>context/lines/{prose}</c> holds every record's line in every language of names the corpus holds
/// names in and that language of words (<see cref="ContextLines"/>), loaded as <c>context_line</c>. A product is made again
/// where its inputs or its composer's version moved; a table that does not hold the rows of a current product (a fresh
/// database) takes them from the product, and nothing is composed.
/// </summary>
internal sealed class ContextBuild(string connectionString, ProductStore store, IServiceProvider services, ILogger logger)
{
    public const string ChaptersProduct = "context/chapters";

    public static string LinesProduct(string prose) => $"context/lines/{prose}";

    private const string Producer = "forge context";

    private const string VersionNote = "version";

    /// <summary>How many chapters are read at once: each is a dozen statements, two of them over the chapter's words.</summary>
    private const int Parallelism = 8;

    /// <summary>How many chapters the run composes again from the tables to hold the stored ones to.</summary>
    private const int Sample = 12;

    public async Task<ContextOutcome> Run(bool force, CancellationToken cancellationToken)
    {
        var clock = Stopwatch.StartNew();
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.NoTracking;
        var names = (await db.EntityNameForms.Select(f => f.Language).Distinct().ToListAsync(cancellationToken))
            .Append(RecordCards.English).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
        var slugs = await db.Entities.OrderBy(e => e.Slug).Select(e => e.Slug).ToListAsync(cancellationToken);
        var texts = await db.Texts.OrderBy(t => t.Slug).Select(t => t.Slug).ToListAsync(cancellationToken);
        var records = RecordFiles.All.Select(f => store.Find(f.Product)).ToList();
        var lineInputs = Inputs(records.Concat(texts.Select(t => store.Find($"texts/{t}/index/spellings"))));
        var chapterInputs = Inputs(records.Concat(texts.SelectMany(t => new[]
        {
            store.Find($"names/projected/{t}"), store.Find($"texts/{t}/words"), store.Find($"texts/{t}/frame"), store.Find($"texts/{t}/text"),
        })));

        var made = 0;
        var chapters = Current<ContextChapterRow>(ChaptersProduct, chapterInputs, ChapterSkeletons.Version, force);
        if (chapters is null)
        {
            var addresses = (await db.VerseReferences
                    .Where(r => r.IsPrimary)
                    .Select(r => new { r.CanonicalBook, r.CanonicalChapter })
                    .Distinct()
                    .ToListAsync(cancellationToken))
                .Select(a => (Book: a.CanonicalBook, Chapter: a.CanonicalChapter))
                .ToList();
            var composed = new ConcurrentBag<ContextChapterRow>();
            await Parallel.ForEachAsync(addresses, new ParallelOptions { MaxDegreeOfParallelism = Parallelism, CancellationToken = cancellationToken },
                async (address, token) =>
                {
                    using var own = services.CreateScope();
                    var reader = own.ServiceProvider.GetRequiredService<AppDbContext>();
                    reader.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.NoTracking;
                    composed.Add(Row(await ChapterSkeletons.Gather(reader, address.Book, address.Chapter, token)));
                });
            chapters = [.. composed.OrderBy(r => r.Book).ThenBy(r => r.Chapter)];
            Write(ChaptersProduct, ContextChapterRow.Schema, chapters, chapterInputs, ChapterSkeletons.Version);
            logger.LogInformation("Context chapters: {Rows} chapters, {Bytes} bytes ({Gzip} gzipped) in {Seconds:0.0} s",
                chapters.Count, chapters.Sum(c => c.Bytes), chapters.Sum(c => c.Gzip), clock.Elapsed.TotalSeconds);
            made++;
        }

        var lines = new ConcurrentDictionary<string, List<ContextLineRow>>(StringComparer.Ordinal);
        await Parallel.ForEachAsync(FamilyDocuments.ProseLanguages, cancellationToken, async (prose, token) =>
        {
            var product = LinesProduct(prose);
            if (Current<ContextLineRow>(product, lineInputs, ContextLines.Version, force) is { } stored)
            {
                lines[prose] = stored;
                return;
            }

            using var own = services.CreateScope();
            var reader = own.ServiceProvider.GetRequiredService<AppDbContext>();
            reader.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.NoTracking;
            var composed = new List<ContextLineRow>();
            foreach (var language in names)
            {
                foreach (var batch in slugs.Chunk(FamilyDocuments.Batch))
                {
                    composed.AddRange((await ContextLines.Gather(reader, batch, language, prose, token))
                        .Select(line => new ContextLineRow(language, line.Key, ContextLines.Write(line.Value))));
                }
            }

            composed = [.. composed.OrderBy(r => r.Names, StringComparer.Ordinal).ThenBy(r => r.Slug, StringComparer.Ordinal)];
            Write(product, ContextLineRow.Schema, composed, lineInputs, ContextLines.Version);
            logger.LogInformation("Context lines in {Prose}: {Rows} lines over {Names} languages of names, {Bytes} bytes, in {Seconds:0.0} s",
                prose, composed.Count, names.Count, composed.Sum(r => (long)Encoding.UTF8.GetByteCount(r.Body)), clock.Elapsed.TotalSeconds);
            lines[prose] = composed;
            Interlocked.Increment(ref made);
        });

        lock (store)
        {
            store.Save();
        }

        var rows = await Match(chapters, lines, cancellationToken);
        await Check(db, chapters, lines, names, cancellationToken);

        var largest = chapters.MaxBy(c => c.Gzip);
        return new ContextOutcome(made, rows, chapters.Count, lines.Values.Sum(l => l.Count), largest?.Gzip ?? 0,
            largest is null ? "" : $"{largest.Book}.{largest.Chapter}, {largest.Bytes} bytes, {largest.Gzip} gzipped", clock.Elapsed);
    }

    private static ContextChapterRow Row(ChapterSkeleton skeleton)
    {
        var body = ChapterSkeletons.Write(skeleton);
        return new ContextChapterRow(skeleton.Book, skeleton.Chapter, Encoding.UTF8.GetByteCount(body), ChapterSkeletons.Gzipped(body),
            ChapterSkeletons.Hash(body), body);
    }

    private List<ProductInput> Inputs(IEnumerable<string?> paths)
    {
        lock (store)
        {
            return [.. store.Inputs(paths.OfType<string>().Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal))];
        }
    }

    /// <summary>A product's rows where it is current for these inputs and was made by this version; null where it must be made.</summary>
    private List<T>? Current<T>(string product, IReadOnlyList<ProductInput> inputs, int version, bool force) where T : class
    {
        if (force)
        {
            return null;
        }

        lock (store)
        {
            if (store.NeedsBuild(product, ProductKind.Machine, inputs))
            {
                return null;
            }

            using var reader = store.Open(product, Schema<T>());
            var said = reader.Header?.Notes.FirstOrDefault(n => n.Key == VersionNote).Value;
            return said == version.ToString(CultureInfo.InvariantCulture) ? [.. reader.Rows()] : null;
        }
    }

    private static ProductSchema<T> Schema<T>() where T : class =>
        typeof(T) == typeof(ContextChapterRow) ? (ProductSchema<T>)(object)ContextChapterRow.Schema : (ProductSchema<T>)(object)ContextLineRow.Schema;

    private void Write<T>(string product, ProductSchema<T> schema, IEnumerable<T> rows, IReadOnlyList<ProductInput> inputs, int version)
    {
        lock (store)
        {
            using var writer = store.Create(product, ProductKind.Machine, schema, ProductHeader.DescribeProducer(Producer), inputs,
                header => header.Notes.Add(new(VersionNote, version.ToString(CultureInfo.InvariantCulture))));
            foreach (var row in rows)
            {
                writer.Write(row);
            }

            writer.Complete();
        }
    }

    /// <summary>
    /// The tables made to hold exactly the products' rows: <c>context_chapter</c> where its rows differ in count or hash from
    /// the product's, and each language of words of <c>context_line</c> likewise, each replaced in one transaction. Returns the
    /// rows written.
    /// </summary>
    private async Task<long> Match(
        IReadOnlyList<ContextChapterRow> chapters,
        IReadOnlyDictionary<string, List<ContextLineRow>> lines,
        CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        long written = 0;
        var chapterHash = Digest(chapters.Select(c => $"{c.Book}\u001f{c.Chapter}\u001f{c.Hash}"));
        if (await Held(connection, "SELECT count(*), coalesce(md5(string_agg(concat_ws(chr(31), canonical_book, canonical_chapter, hash), chr(30) ORDER BY canonical_book, canonical_chapter)), '') FROM context_chapter", null, cancellationToken)
            != (chapters.Count, chapterHash))
        {
            await Replace(connection, "DELETE FROM context_chapter", null,
                "COPY context_chapter (canonical_book, canonical_chapter, body, hash) FROM STDIN (FORMAT BINARY)",
                chapters, async (copy, c) =>
                {
                    await copy.WriteAsync(checked((int)c.Book), NpgsqlDbType.Integer, cancellationToken);
                    await copy.WriteAsync(checked((int)c.Chapter), NpgsqlDbType.Integer, cancellationToken);
                    await copy.WriteAsync(c.Body, NpgsqlDbType.Text, cancellationToken);
                    await copy.WriteAsync(c.Hash, NpgsqlDbType.Text, cancellationToken);
                }, cancellationToken);
            written += chapters.Count;
        }

        foreach (var (prose, rows) in lines)
        {
            var hash = Digest(rows.Select(r => $"{r.Names}\u001f{r.Slug}\u001f{Md5(r.Body)}"));
            if (await Held(connection, "SELECT count(*), coalesce(md5(string_agg(concat_ws(chr(31), names, slug, md5(body)), chr(30) ORDER BY names COLLATE \"C\", slug COLLATE \"C\")), '') FROM context_line WHERE prose = @prose", prose, cancellationToken)
                == (rows.Count, hash))
            {
                continue;
            }

            await Replace(connection, "DELETE FROM context_line WHERE prose = @prose", prose,
                "COPY context_line (prose, names, slug, body) FROM STDIN (FORMAT BINARY)",
                rows, async (copy, r) =>
                {
                    await copy.WriteAsync(prose, NpgsqlDbType.Text, cancellationToken);
                    await copy.WriteAsync(r.Names, NpgsqlDbType.Text, cancellationToken);
                    await copy.WriteAsync(r.Slug, NpgsqlDbType.Text, cancellationToken);
                    await copy.WriteAsync(r.Body, NpgsqlDbType.Text, cancellationToken);
                }, cancellationToken);
            written += rows.Count;
        }

        if (written > 0)
        {
            await using var analyze = new NpgsqlCommand("ANALYZE context_chapter, context_line", connection);
            await analyze.ExecuteNonQueryAsync(cancellationToken);
        }

        return written;
    }

    private static string Md5(string text) => Convert.ToHexStringLower(System.Security.Cryptography.MD5.HashData(Encoding.UTF8.GetBytes(text)));

    /// <summary>The md5 the database's <c>string_agg(…, chr(30))</c> gives over the same fields, empty for no rows.</summary>
    private static string Digest(IEnumerable<string> lines)
    {
        var joined = string.Join('\u001e', lines);
        return joined.Length == 0 ? "" : Md5(joined);
    }

    private static async Task<(int, string)> Held(NpgsqlConnection connection, string sql, string? prose, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        if (prose is not null)
        {
            command.Parameters.AddWithValue("prose", prose);
        }

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);
        return (checked((int)reader.GetInt64(0)), reader.GetString(1));
    }

    private static async Task Replace<T>(
        NpgsqlConnection connection, string delete, string? prose, string copySql, IEnumerable<T> rows,
        Func<NpgsqlBinaryImporter, T, Task> write, CancellationToken cancellationToken)
    {
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using (var command = new NpgsqlCommand(delete, connection, transaction))
        {
            if (prose is not null)
            {
                command.Parameters.AddWithValue("prose", prose);
            }

            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var copy = await connection.BeginBinaryImportAsync(copySql, cancellationToken))
        {
            foreach (var row in rows)
            {
                await copy.StartRowAsync(cancellationToken);
                await write(copy, row);
            }

            await copy.CompleteAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    /// <summary>
    /// A sample of the stored chapters and lines composed again from the tables as they stand: a chapter drawn from every
    /// part of the list, and the lines of its records in two pairs of languages. A difference fails the run, naming it.
    /// </summary>
    private async Task Check(
        AppDbContext db,
        IReadOnlyList<ContextChapterRow> chapters,
        IReadOnlyDictionary<string, List<ContextLineRow>> lines,
        IReadOnlyList<string> names,
        CancellationToken cancellationToken)
    {
        if (chapters.Count == 0)
        {
            return;
        }

        var differences = new List<string>();
        var drawn = Enumerable.Range(0, Sample).Select(i => chapters[(int)((long)i * chapters.Count / Sample)]).Distinct().ToList();
        var pairs = new[] { (Prose: RecordCards.English, Names: RecordCards.English), (Prose: "ukr", Names: names.Contains("ukr") ? "ukr" : RecordCards.English) };
        foreach (var chapter in drawn)
        {
            var again = Row(await ChapterSkeletons.Gather(db, (int)chapter.Book, (int)chapter.Chapter, cancellationToken));
            if (again.Hash != chapter.Hash)
            {
                differences.Add($"chapter {chapter.Book}.{chapter.Chapter}");
            }

            var slugs = ChapterSkeletons.Read(chapter.Body).Records.Select(r => r.Slug).ToList();
            foreach (var (prose, language) in pairs.Where(p => lines.ContainsKey(p.Prose)))
            {
                var stored = lines[prose].Where(r => r.Names == language).ToDictionary(r => r.Slug, r => r.Body, StringComparer.Ordinal);
                foreach (var (slug, line) in await ContextLines.Gather(db, slugs, language, prose, cancellationToken))
                {
                    if (stored.GetValueOrDefault(slug) != ContextLines.Write(line))
                    {
                        differences.Add($"line {prose}/{language} {slug}");
                    }
                }
            }
        }

        if (differences.Count > 0)
        {
            throw new InvalidOperationException(
                $"Stored chapter contexts differ from those composed again from the tables: {string.Join(", ", differences.Take(20))}. " +
                "Raise ChapterSkeletons.Version or ContextLines.Version with a change to what they compose, then build again.");
        }

        logger.LogInformation("Context check: {Chapters} chapters and their records' lines in two pairs of languages composed again equal the stored ones",
            drawn.Count);
    }
}
