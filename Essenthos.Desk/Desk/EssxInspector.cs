using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Essenthos.Core.Products;
using Essenthos.Core.Products.Essx;
using Essenthos.Core.Products.Tools;

namespace Essenthos.Core.Desk;

internal sealed record EssxProductEntry(string Path, long Bytes, string Modified);

internal sealed record EssxProductsResponse(string Folder, IReadOnlyList<EssxProductEntry> Products);

/// <param name="Path">Relative to Resources/Essenthos, or a whole path to an .essx file on this machine.</param>
internal sealed record EssxOpenRequest(string Path);

internal sealed record EssxInputView(string Path, string Sha256);

internal sealed record EssxHeaderView(
    string Product,
    string Producer,
    long Rows,
    IReadOnlyList<EssxInputView> Inputs,
    IReadOnlyList<string[]> Notes,
    IReadOnlyList<string> Comments);

/// <param name="TextChunks">Chunks of a dict column written as text once its dictionary reached its cap.</param>
internal sealed record EssxPhysicalView(
    string Name,
    string Encoding,
    long Bytes,
    long RawBytes,
    long Nulls,
    int? Distinct,
    long? DictionaryBytes,
    long? Min,
    long? Max,
    long? Trues,
    int CompressedChunks,
    int TextChunks,
    IReadOnlyList<int> Widths);

internal sealed record EssxColumnView(string Name, string Type, bool Nullable, long Bytes, IReadOnlyList<EssxPhysicalView> Physical);

/// <param name="Books">The books the group's key spans, from the chunk statistics, or null for a table without a key.</param>
internal sealed record EssxGroupView(int Index, int Rows, long FirstRow, long Bytes, long RawBytes, string? Books, string? Chapters);

internal sealed record EssxTableView(
    string Name,
    long Rows,
    int RowGroupRows,
    int SchemaVersion,
    long Bytes,
    string? KeyColumn,
    IReadOnlyList<EssxColumnView> Columns,
    IReadOnlyList<EssxGroupView> Groups);

/// <summary>Where the file's bytes go, front to back.</summary>
internal sealed record EssxLayoutView(long Preamble, long Chunks, long Dictionaries, long Footer, long Trailer);

internal sealed record EssxFileView(
    string Id,
    string Name,
    string? Path,
    long Bytes,
    int Version,
    string Codec,
    EssxHeaderView Header,
    IReadOnlyList<EssxTableView> Tables,
    EssxLayoutView Layout,
    string Footer,
    string StoredSha256,
    double OpenMs);

internal sealed record EssxVerifyView(bool Ok, string StoredSha256, string FileSha256, string? ManifestSha256, bool? MatchesManifest, double Ms);

internal sealed record EssxRowsView(
    string Table,
    IReadOnlyList<string> Columns,
    long Total,
    long Offset,
    IReadOnlyList<long> RowNumbers,
    IReadOnlyList<string?[]> Rows,
    bool Filtered,
    double Ms);

internal sealed record EssxDictionaryView(string Physical, int Count, int Matching, int Offset, IReadOnlyList<EssxDictionaryEntry> Values);

internal sealed record EssxDictionaryEntry(int Id, string Value);

/// <param name="FirstMs">The first run, which pays for the disk cache and the code's first use.</param>
internal sealed record EssxFeatureView(string Key, double FirstMs, double BestMs, double MedianMs, int Runs, string Detail);

internal sealed record EssxFeaturesView(string Table, string? Verse, string? Column, int Head, IReadOnlyList<EssxFeatureView> Features);

/// <summary>
/// The console's look into an ESSX file: one dropped onto the page (kept in the console's cache until the
/// owner closes it) or a product under Resources/Essenthos. It reads through the same reader the build
/// uses, so what the page shows and how long each step takes is what the build sees.
/// </summary>
internal sealed class EssxInspector(DeskPaths paths)
{
    private const int Runs = 5;
    private const int FiltersKept = 6;

    private sealed record Opened(string Path, string Name, bool Uploaded, ConcurrentDictionary<string, long[]> Filters);

    private readonly ConcurrentDictionary<string, Opened> _files = new(StringComparer.Ordinal);

    private string Uploads => Path.Combine(paths.Cache, "essx");

    public EssxProductsResponse Products()
    {
        var root = paths.Owner;
        var products = !Directory.Exists(root)
            ? []
            : Directory.EnumerateFiles(root, "*" + EssxLayout.Extension, SearchOption.AllDirectories)
                .Select(file => new FileInfo(file))
                .Select(file => new EssxProductEntry(
                    System.IO.Path.GetRelativePath(root, file.FullName).Replace('\\', '/'),
                    file.Length,
                    file.LastWriteTimeUtc.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture)))
                .OrderBy(product => product.Path, StringComparer.Ordinal)
                .ToList();
        return new EssxProductsResponse("Resources/Essenthos", products);
    }

    /// <summary>A product by its path, or null when there is no such .essx file.</summary>
    public EssxFileView? Open(string path)
    {
        var relative = path.Replace('\\', '/').TrimStart('/');
        var full = System.IO.Path.IsPathRooted(path) ? path : DeskPaths.Under(paths.Owner, relative);
        if (full is null || !full.EndsWith(EssxLayout.Extension, StringComparison.OrdinalIgnoreCase) || !File.Exists(full))
        {
            return null;
        }

        var id = Guid.NewGuid().ToString("N");
        _files[id] = new Opened(full, System.IO.Path.GetFileName(full), false, new ConcurrentDictionary<string, long[]>(StringComparer.Ordinal));
        return View(id);
    }

    /// <summary>A file the owner dropped: its bytes are kept in the console's cache and opened from there.</summary>
    public async Task<EssxFileView> Upload(string? name, Stream body, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Uploads);
        foreach (var stale in Directory.EnumerateFiles(Uploads).Where(file => File.GetLastWriteTimeUtc(file) < DateTime.UtcNow.AddDays(-1)))
        {
            File.Delete(stale);
        }

        var id = Guid.NewGuid().ToString("N");
        var full = System.IO.Path.Combine(Uploads, id + EssxLayout.Extension);
        await using (var file = new FileStream(full, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1 << 20, FileOptions.Asynchronous))
        {
            await body.CopyToAsync(file, 1 << 20, cancellationToken);
        }

        _files[id] = new Opened(full, string.IsNullOrWhiteSpace(name) ? "dropped.essx" : System.IO.Path.GetFileName(name), true,
            new ConcurrentDictionary<string, long[]>(StringComparer.Ordinal));
        try
        {
            return View(id) ?? throw new ProductFormatException("The file could not be read.");
        }
        catch
        {
            Close(id);
            throw;
        }
    }

    public bool Close(string id)
    {
        if (!_files.TryRemove(id, out var opened))
        {
            return false;
        }

        if (opened.Uploaded && File.Exists(opened.Path))
        {
            File.Delete(opened.Path);
        }

        return true;
    }

    public EssxFileView? View(string id)
    {
        if (!_files.TryGetValue(id, out var opened))
        {
            return null;
        }

        var clock = Stopwatch.StartNew();
        using var reader = EssxReader.Open(opened.Path);
        var openMs = clock.Elapsed.TotalMilliseconds;
        var footerJson = ReadFooter(opened.Path, out var footerLength);
        var header = reader.Footer.Product;
        var tables = reader.Tables.Select(TableView).ToList();
        var chunks = reader.Footer.Tables.Sum(t => t.RowGroups.Sum(g => g.Chunks.Sum(c => c.Length)));
        var dictionaries = reader.Footer.Tables.Sum(t => t.Physical.Sum(p => p.Dictionary?.Length ?? 0));
        var codecs = reader.Footer.Tables.SelectMany(t => t.RowGroups.SelectMany(g => g.Chunks.Select(c => c.Codec))).Distinct().ToList();
        return new EssxFileView(
            id,
            opened.Name,
            opened.Uploaded ? null : opened.Path,
            reader.Length,
            reader.Footer.Version,
            codecs.Contains(EssxLayout.CodecDeflate) ? EssxLayout.CodecDeflate : EssxLayout.CodecNone,
            new EssxHeaderView(header.Product, header.Producer, header.Rows,
                [.. header.Inputs.Select(input => new EssxInputView(input.Path, input.Sha256))], header.Notes ?? [], header.Comments ?? []),
            tables,
            new EssxLayoutView(EssxLayout.PreambleLength, chunks, dictionaries, footerLength, EssxLayout.TrailerLength),
            footerJson,
            Convert.ToHexStringLower(reader.StoredHash()),
            openMs);
    }

    public EssxVerifyView? Verify(string id)
    {
        if (!_files.TryGetValue(id, out var opened))
        {
            return null;
        }

        var clock = Stopwatch.StartNew();
        using var reader = EssxReader.Open(opened.Path);
        var ok = reader.Verify(out var fileSha256);
        var ms = clock.Elapsed.TotalMilliseconds;
        var manifest = opened.Uploaded ? null : ManifestHash(opened.Path);
        return new EssxVerifyView(ok, Convert.ToHexStringLower(reader.StoredHash()), fileSha256, manifest, manifest is null ? null : manifest == fileSha256, ms);
    }

    /// <summary>
    /// A window of rows as text, under an optional verse and search. A filter's matching rows are found
    /// once and kept, so scrolling through them reads only the groups each window needs.
    /// </summary>
    public EssxRowsView? Rows(string id, string? tableName, long offset, int limit, string? columns, string? verse, string? search, string? searchColumn)
    {
        if (!_files.TryGetValue(id, out var opened))
        {
            return null;
        }

        var clock = Stopwatch.StartNew();
        using var reader = EssxReader.Open(opened.Path);
        var table = tableName is null ? reader.Tables[0] : reader.Table(tableName);
        var names = table.Columns.Select(column => column.Name).ToList();
        var shown = string.IsNullOrWhiteSpace(columns)
            ? names
            : [.. columns.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Where(names.Contains)];
        var starts = GroupStarts(table);
        limit = Math.Clamp(limit, 1, 5000);
        offset = Math.Max(0, offset);

        VerseKey? wanted = string.IsNullOrWhiteSpace(verse) ? null : VerseKey.Parse(verse.Trim());
        var words = SearchWords(search);
        long[]? matching = null;
        if (wanted is not null || words.Length > 0)
        {
            var key = $"{table.Name}|{wanted}|{string.Join(' ', words)}|{searchColumn}";
            matching = opened.Filters.GetOrAdd(key, _ => Matching(table, starts, wanted, words, searchColumn));
            if (opened.Filters.Count > FiltersKept)
            {
                foreach (var old in opened.Filters.Keys.Where(k => k != key).Take(opened.Filters.Count - FiltersKept))
                {
                    opened.Filters.TryRemove(old, out _);
                }
            }
        }

        var total = matching?.LongLength ?? table.RowCount;
        var numbers = new List<long>();
        if (matching is null)
        {
            for (var n = offset; n < Math.Min(total, offset + limit); n++) numbers.Add(n);
        }
        else
        {
            for (var n = offset; n < Math.Min(total, offset + limit); n++) numbers.Add(matching[n]);
        }

        var rows = new List<string?[]>(numbers.Count);
        var indexes = shown.Select(table.ColumnIndex).ToArray();
        foreach (var byGroup in numbers.GroupBy(number => GroupOf(starts, number)))
        {
            var group = table.ReadGroup(byGroup.Key, shown);
            foreach (var number in byGroup)
            {
                var r = (int)(number - starts[byGroup.Key]);
                rows.Add([.. indexes.Select(index => group.Column(index).Text(r))]);
            }
        }

        return new EssxRowsView(table.Name, shown, total, offset, numbers, rows, matching is not null, clock.Elapsed.TotalMilliseconds);
    }

    public EssxDictionaryView? Dictionary(string id, string? tableName, string physical, int offset, int limit, string? search)
    {
        if (!_files.TryGetValue(id, out var opened))
        {
            return null;
        }

        using var reader = EssxReader.Open(opened.Path);
        var table = tableName is null ? reader.Tables[0] : reader.Table(tableName);
        var info = table.Info.Physical.FirstOrDefault(p => p.Name == physical);
        if (info?.Dictionary is null)
        {
            return null;
        }

        var values = table.Dictionary(physical);
        var words = SearchWords(search);
        var entries = Enumerable.Range(1, values.Length - 1)
            .Where(i => words.All(word => values[i]!.Contains(word, StringComparison.OrdinalIgnoreCase)))
            .ToList();
        limit = Math.Clamp(limit, 1, 5000);
        return new EssxDictionaryView(physical, values.Length - 1, entries.Count, offset,
            [.. entries.Skip(Math.Max(0, offset)).Take(limit).Select(i => new EssxDictionaryEntry(i, values[i]!))]);
    }

    /// <summary>
    /// Runs each thing a reader does against the file, several times, timed where the build's reader runs:
    /// open and header, the footer, one column of every group, a verse, the first rows, every column of
    /// every group, and the SHA-256 check.
    /// </summary>
    public EssxFeaturesView? Features(string id, string? tableName, string? verse, string? column, int head)
    {
        if (!_files.TryGetValue(id, out var opened))
        {
            return null;
        }

        var path = opened.Path;
        head = Math.Clamp(head, 1, 100_000);
        string table0;
        VerseKey? wanted;
        string? projected;
        int keyIndex;
        using (var reader = EssxReader.Open(path))
        {
            var table = tableName is null ? reader.Tables[0] : reader.Table(tableName);
            table0 = table.Name;
            keyIndex = table.Columns.ToList().FindIndex(c => c.Type is ColumnType.WordKey or ColumnType.VerseKey);
            wanted = !string.IsNullOrWhiteSpace(verse) ? VerseKey.Parse(verse.Trim()) : keyIndex >= 0 ? MiddleVerse(table, keyIndex) : null;
            projected = column is not null && table.Columns.Any(c => c.Name == column)
                ? column
                : table.Columns.FirstOrDefault(c => c.Type is ColumnType.Text)?.Name ?? table.Columns.FirstOrDefault()?.Name;
        }

        var features = new List<EssxFeatureView>
        {
            Measure("open", () =>
            {
                using var reader = EssxReader.Open(path);
                var header = reader.Header;
                return $"{header.Product}: {reader.Tables.Count} table(s), {reader.Footer.Tables.Sum(t => t.RowGroups.Count)} row group(s)";
            }),
            Measure("footer", () =>
            {
                var json = ReadFooterBytes(path);
                var footer = JsonSerializer.Deserialize(json, EssxLayout.Json.EssxFooter)!;
                return $"{json.Length:N0} bytes of JSON, {footer.Tables.Sum(t => t.Physical.Count)} stored columns";
            }),
        };

        if (projected is not null)
        {
            features.Add(Measure("column", () =>
            {
                using var reader = EssxReader.Open(path);
                var table = reader.Table(table0);
                long values = 0;
                for (var g = 0; g < table.RowGroups; g++) values += table.ReadGroup(g, [projected]).Rows;
                return $"{projected}: {values:N0} values from {table.RowGroups} group(s), the other {table.Columns.Count - 1} column(s) untouched";
            }));
        }

        if (wanted is { } verseKey)
        {
            features.Add(Measure("verse", () =>
            {
                using var reader = EssxReader.Open(path);
                var table = reader.Table(table0);
                var key = table.Info.Columns[keyIndex];
                var books = table.GroupsWhere(key.Physical[0], (min, max) => min is null || min <= verseKey.Book && max >= verseKey.Book).ToHashSet();
                var groups = table.GroupsWhere(key.Physical[1], (min, max) => min is null || min <= verseKey.Chapter && max >= verseKey.Chapter).Where(books.Contains).ToList();
                var found = 0;
                foreach (var g in groups)
                {
                    var rows = ProductView.RowsInVerse(table, g, keyIndex, verseKey);
                    if (rows.Count == 0) continue;
                    var group = table.ReadGroup(g);
                    foreach (var r in rows)
                    {
                        for (var c = 0; c < table.Columns.Count; c++) _ = group.Column(c).Text(r);
                        found++;
                    }
                }

                return $"{verseKey}: {found} row(s), {groups.Count} of {table.RowGroups} group(s) read";
            }));
        }

        features.Add(Measure("head", () =>
        {
            using var reader = EssxReader.Open(path);
            var table = reader.Table(table0);
            var shown = 0;
            for (var g = 0; g < table.RowGroups && shown < head; g++)
            {
                var group = table.ReadGroup(g);
                for (var r = 0; r < group.Rows && shown < head; r++, shown++)
                {
                    for (var c = 0; c < table.Columns.Count; c++) _ = group.Column(c).Text(r);
                }
            }

            return $"the first {shown:N0} row(s) as text";
        }));
        features.Add(Measure("full", () =>
        {
            using var reader = EssxReader.Open(path);
            var table = reader.Table(table0);
            long rows = 0;
            for (var g = 0; g < table.RowGroups; g++) rows += table.ReadGroup(g).Rows;
            return $"{rows:N0} rows × {table.Columns.Count} columns decoded to arrays";
        }));
        features.Add(Measure("verify", () =>
        {
            using var reader = EssxReader.Open(path);
            var ok = reader.Verify();
            return ok ? $"{reader.Length:N0} bytes hashed: the SHA-256 matches" : "the SHA-256 does NOT match: the bytes are not the ones written";
        }));

        return new EssxFeaturesView(table0, wanted?.ToString(), projected, head, features);
    }

    private static EssxFeatureView Measure(string key, Func<string> run)
    {
        var times = new double[Runs];
        var detail = string.Empty;
        for (var i = 0; i < Runs; i++)
        {
            var clock = Stopwatch.StartNew();
            detail = run();
            times[i] = clock.Elapsed.TotalMilliseconds;
        }

        var sorted = times.Order().ToArray();
        return new EssxFeatureView(key, Math.Round(times[0], 3), Math.Round(sorted[0], 3), Math.Round(sorted[Runs / 2], 3), Runs, detail);
    }

    private static VerseKey? MiddleVerse(EssxTable table, int keyIndex)
    {
        if (table.RowGroups == 0)
        {
            return null;
        }

        var group = table.ReadGroup(table.RowGroups / 2, [table.Columns[keyIndex].Name]);
        var key = group.Column(keyIndex);
        for (var r = group.Rows / 2; r < group.Rows; r++)
        {
            if (key.VerseKey(r) is { } verse) return verse;
        }

        return null;
    }

    private static long[] Matching(EssxTable table, long[] starts, VerseKey? verse, string[] words, string? searchColumn)
    {
        var keyIndex = table.Columns.ToList().FindIndex(c => c.Type is ColumnType.WordKey or ColumnType.VerseKey);
        IEnumerable<int> groups = Enumerable.Range(0, table.RowGroups);
        if (verse is { } wanted)
        {
            if (keyIndex < 0)
            {
                return [];
            }

            var key = table.Info.Columns[keyIndex];
            var books = table.GroupsWhere(key.Physical[0], (min, max) => min is null || min <= wanted.Book && max >= wanted.Book).ToHashSet();
            groups = table.GroupsWhere(key.Physical[1], (min, max) => min is null || min <= wanted.Chapter && max >= wanted.Chapter).Where(books.Contains).ToList();
        }

        var searched = searchColumn is not null && table.Columns.Any(c => c.Name == searchColumn)
            ? [searchColumn]
            : table.Columns.Select(c => c.Name).ToArray();
        var found = new List<long>();
        foreach (var g in groups)
        {
            List<int>? inVerse = verse is { } v ? ProductView.RowsInVerse(table, g, keyIndex, v) : null;
            if (inVerse is { Count: 0 })
            {
                continue;
            }

            if (words.Length == 0)
            {
                found.AddRange(inVerse!.Select(r => starts[g] + r));
                continue;
            }

            var group = table.ReadGroup(g, searched);
            var data = searched.Select(name => group.Column(name)).ToArray();
            // A dictionary's strings are tested once per word, not once per row.
            var dictionaryHits = data.Select(column => column is DictData dict
                ? words.Select(word => dict.Dictionary.Select(value => value?.Contains(word, StringComparison.OrdinalIgnoreCase) == true).ToArray()).ToArray()
                : null).ToArray();
            var rows = inVerse ?? Enumerable.Range(0, group.Rows);
            foreach (var r in rows)
            {
                var all = true;
                for (var w = 0; w < words.Length && all; w++)
                {
                    var any = false;
                    for (var c = 0; c < data.Length && !any; c++)
                    {
                        any = dictionaryHits[c] is { } hits
                            ? hits[w][((DictData)data[c]).Ids[r]]
                            : data[c].Text(r)?.Contains(words[w], StringComparison.OrdinalIgnoreCase) == true;
                    }

                    all = any;
                }

                if (all) found.Add(starts[g] + r);
            }
        }

        return [.. found];
    }

    private static string[] SearchWords(string? search) =>
        string.IsNullOrWhiteSpace(search) ? [] : search.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static long[] GroupStarts(EssxTable table)
    {
        var starts = new long[table.RowGroups + 1];
        for (var g = 0; g < table.RowGroups; g++) starts[g + 1] = starts[g] + table.Info.RowGroups[g].Rows;
        return starts;
    }

    private static int GroupOf(long[] starts, long row)
    {
        var index = Array.BinarySearch(starts, row);
        return index >= 0 ? Math.Min(index, starts.Length - 2) : ~index - 1;
    }

    private static EssxTableView TableView(EssxTable table)
    {
        var info = table.Info;
        var keyColumn = info.Columns.FirstOrDefault(c => c.Type is "wordkey" or "versekey");
        var columns = info.Columns.Select(column =>
        {
            var physical = column.Physical.Select(name =>
            {
                var index = info.Physical.FindIndex(p => p.Name == name);
                var stored = info.Physical[index];
                var chunks = info.RowGroups.Select(group => group.Chunks[index]).ToList();
                var ints = stored.Encoding == "int" ? chunks.Where(c => c.Min is not null).ToList() : [];
                return new EssxPhysicalView(
                    name,
                    stored.Encoding,
                    chunks.Sum(c => c.Length) + (stored.Dictionary?.Length ?? 0),
                    chunks.Sum(c => c.RawLength) + (stored.Dictionary?.RawLength ?? 0),
                    chunks.Sum(c => (long)c.Nulls),
                    stored.Dictionary?.Count,
                    stored.Dictionary?.Length,
                    ints.Count > 0 ? ints.Min(c => c.Min!.Value) : null,
                    ints.Count > 0 ? ints.Max(c => c.Max!.Value) : null,
                    stored.Encoding == "bool" ? chunks.Sum(c => c.Min ?? 0) : null,
                    chunks.Count(c => c.Codec != EssxLayout.CodecNone),
                    chunks.Count(c => c.Encoding == "text"),
                    [.. chunks.Select(c => c.Width).Distinct().Order()]);
            }).ToList();
            return new EssxColumnView(column.Name, column.Type, column.Nullable, physical.Sum(p => p.Bytes), physical);
        }).ToList();

        var starts = GroupStarts(table);
        var bookIndex = keyColumn is null ? -1 : info.Physical.FindIndex(p => p.Name == keyColumn.Physical[0]);
        var chapterIndex = keyColumn is null ? -1 : info.Physical.FindIndex(p => p.Name == keyColumn.Physical[1]);
        var groups = info.RowGroups.Select((group, g) => new EssxGroupView(
            g,
            group.Rows,
            starts[g],
            group.Chunks.Sum(c => c.Length),
            group.Chunks.Sum(c => c.RawLength),
            bookIndex < 0 ? null : Range(group.Chunks[bookIndex]),
            chapterIndex < 0 ? null : Range(group.Chunks[chapterIndex]))).ToList();
        return new EssxTableView(info.Name, info.Rows, info.RowGroupRows, info.SchemaVersion, columns.Sum(c => c.Bytes), keyColumn?.Name, columns, groups);
    }

    private static string? Range(EssxChunkInfo chunk) =>
        chunk.Min is not { } min ? null : chunk.Max == min ? $"{min}" : $"{min}–{chunk.Max}";

    private static byte[] ReadFooterBytes(string path)
    {
        using var handle = File.OpenHandle(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        var length = RandomAccess.GetLength(handle);
        Span<byte> trailer = stackalloc byte[16];
        RandomAccess.Read(handle, trailer, length - EssxLayout.TrailerLength);
        var offset = BitConverter.ToInt64(trailer);
        var footerLength = BitConverter.ToInt64(trailer[8..]);
        var json = new byte[footerLength];
        RandomAccess.Read(handle, json, offset);
        return json;
    }

    private static string ReadFooter(string path, out long length)
    {
        var json = ReadFooterBytes(path);
        length = json.Length;
        var node = JsonNode.Parse(json);
        return node?.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) ?? Encoding.UTF8.GetString(json);
    }

    /// <summary>The hash the build manifest records for a product under Resources/, when the file is one.</summary>
    private string? ManifestHash(string full)
    {
        var resources = System.IO.Path.GetDirectoryName(paths.Owner)!;
        var relative = System.IO.Path.GetRelativePath(resources, full).Replace('\\', '/');
        if (relative.StartsWith("..", StringComparison.Ordinal) || System.IO.Path.IsPathRooted(relative))
        {
            return null;
        }

        try
        {
            return BuildManifest.Load(resources).Get(relative)?.Sha256;
        }
        catch (Exception exception) when (exception is ProductFormatException or IOException)
        {
            return null;
        }
    }
}

internal static class EssxEndpoints
{
    public static void MapEssx(this RouteGroupBuilder routes)
    {
        routes.MapGet("/essx/products", (EssxInspector inspector) => inspector.Products());

        routes.MapPost("/essx/open", (EssxOpenRequest request, EssxInspector inspector) =>
            Readable(() => inspector.Open(request.Path) is { } view
                ? Results.Ok(view)
                : Results.NotFound(new ProblemResponse("There is no such product file."))));

        // The file as the request's body, as the portraits take a picture: no form, no field names.
        routes.MapPost("/essx/files", async (string? name, HttpContext context, EssxInspector inspector, CancellationToken cancellationToken) =>
        {
            if (context.Features.Get<Microsoft.AspNetCore.Http.Features.IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } limit)
            {
                limit.MaxRequestBodySize = null;
            }

            try
            {
                return Results.Ok(await inspector.Upload(name, context.Request.Body, cancellationToken));
            }
            catch (Exception exception) when (exception is ProductFormatException or FormatException)
            {
                return Results.UnprocessableEntity(new ProblemResponse($"This is not a product file the reader can open: {exception.Message}"));
            }
        });

        routes.MapGet("/essx/files/{id}", (string id, EssxInspector inspector) =>
            Readable(() => inspector.View(id) is { } view ? Results.Ok(view) : Gone()));

        routes.MapDelete("/essx/files/{id}", (string id, EssxInspector inspector) =>
            inspector.Close(id) ? Results.NoContent() : Gone());

        routes.MapGet("/essx/files/{id}/verify", (string id, EssxInspector inspector) =>
            Readable(() => inspector.Verify(id) is { } view ? Results.Ok(view) : Gone()));

        routes.MapGet("/essx/files/{id}/rows", (string id, string? table, long? offset, int? limit, string? columns, string? verse, string? search, string? column,
                EssxInspector inspector) =>
            Readable(() => inspector.Rows(id, table, offset ?? 0, limit ?? 200, columns, verse, search, column) is { } view ? Results.Ok(view) : Gone()));

        routes.MapGet("/essx/files/{id}/dictionary", (string id, string? table, string physical, int? offset, int? limit, string? search, EssxInspector inspector) =>
            Readable(() => inspector.Dictionary(id, table, physical, offset ?? 0, limit ?? 500, search) is { } view ? Results.Ok(view) : Gone()));

        routes.MapPost("/essx/files/{id}/features", (string id, string? table, string? verse, string? column, int? head, EssxInspector inspector) =>
            Readable(() => inspector.Features(id, table, verse, column, head ?? 100) is { } view ? Results.Ok(view) : Gone()));
    }

    private static IResult Gone() => Results.NotFound(new ProblemResponse("The file is no longer open; open it again."));

    private static IResult Readable(Func<IResult> read)
    {
        try
        {
            return read();
        }
        catch (Exception exception) when (exception is ProductFormatException or FormatException or ArgumentException or IOException)
        {
            return Results.UnprocessableEntity(new ProblemResponse(exception.Message));
        }
    }
}
