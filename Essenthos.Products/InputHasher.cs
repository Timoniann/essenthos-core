using System.Collections.Concurrent;
using System.Globalization;
using System.Security.Cryptography;
using Essenthos.Core.Products.Tsv;

namespace Essenthos.Core.Products;

/// <summary>
/// The SHA-256 of every file a build reads, remembered in <c>Essenthos/build/hashes.tsv</c> by
/// path, size and last write time so that twenty gigabytes of sources are hashed once and
/// thereafter only when one of them moved. <see cref="Rehash"/> ignores the memory for one run.
/// A folder is hashed as the list of its files' paths and hashes, so that a file added, taken out
/// or changed anywhere under it moves the folder's hash; its files are remembered one by one.
/// </summary>
public sealed class InputHasher
{
    public const string CacheProduct = "Essenthos/build/hashes.tsv";

    private static readonly ProductSchema<Entry> Schema = new ProductSchema<Entry>()
        .Text("path", entry => entry.Path)
        .Integer("bytes", entry => entry.Bytes)
        .Integer("written", entry => entry.WrittenTicks)
        .Text("sha256", entry => entry.Sha256)
        .Reads(row => new Entry(row.Text(0), row.Integer(1), row.Integer(2), row.Text(3)));

    private readonly string _root;
    private readonly ConcurrentDictionary<string, Entry> _entries = new(StringComparer.Ordinal);
    private readonly object _save = new();
    private int _misses;

    /// <param name="root">The <c>Resources/</c> folder every path is relative to.</param>
    public InputHasher(string root)
    {
        _root = Path.GetFullPath(root);
        var cache = Path.Combine(_root, CacheProduct);
        if (File.Exists(cache))
        {
            using var reader = TsvFormat.Instance.Open(cache, Schema);
            foreach (var entry in reader.Rows())
            {
                _entries[entry.Path] = entry;
            }
        }
    }

    /// <summary>Hash every file again this run, whatever the memory says.</summary>
    public bool Rehash { get; init; }

    /// <summary>How many files were read in full this run, which a report prints beside the time it took.</summary>
    public int Hashed => _misses;

    /// <summary>The input record of a file or folder, by its path relative to <c>Resources/</c>.</summary>
    public ProductInput Input(string relativePath) => new(Normalise(relativePath), Hash(relativePath));

    public IReadOnlyList<ProductInput> Inputs(params IEnumerable<string> relativePaths) =>
        [.. relativePaths.Select(Input)];

    public string Hash(string relativePath)
    {
        var relative = Normalise(relativePath);
        var full = Path.Combine(_root, relative);
        if (Directory.Exists(full))
        {
            return HashFolder(full);
        }

        if (!File.Exists(full))
        {
            throw new FileNotFoundException($"Input '{relative}' is not under {_root}.", full);
        }

        return HashFile(relative, new FileInfo(full));
    }

    /// <summary>Hashes a file just written, whatever the memory says of it, and remembers the result.</summary>
    public string Refresh(string relativePath)
    {
        var relative = Normalise(relativePath);
        return HashFile(relative, new FileInfo(Path.Combine(_root, relative)), force: true);
    }

    private string HashFile(string relative, FileInfo file, bool force = false)
    {
        var writtenTicks = file.LastWriteTimeUtc.Ticks;
        if (!force && !Rehash && _entries.TryGetValue(relative, out var known) && known.Bytes == file.Length && known.WrittenTicks == writtenTicks)
        {
            return known.Sha256;
        }

        Interlocked.Increment(ref _misses);
        var sha256 = Sha256Of(file.FullName);
        _entries[relative] = new Entry(relative, file.Length, writtenTicks, sha256);
        return sha256;
    }

    private string HashFolder(string full)
    {
        var files = Directory.EnumerateFiles(full, "*", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(_root, path).Replace('\\', '/'))
            .Order(StringComparer.Ordinal)
            .ToList();
        var listing = new System.Text.StringBuilder();
        foreach (var relative in files)
        {
            listing.Append(relative).Append('\t').Append(HashFile(relative, new FileInfo(Path.Combine(_root, relative)))).Append('\n');
        }

        return Convert.ToHexStringLower(SHA256.HashData(Utf8.NoBom.GetBytes(listing.ToString())));
    }

    public static string Sha256Of(string fullPath)
    {
        using var stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20, FileOptions.SequentialScan);
        return Convert.ToHexStringLower(SHA256.HashData(stream));
    }

    /// <summary>Writes the memory out. Called once at the end of a build; safe to call from several threads.</summary>
    public void Save()
    {
        lock (_save)
        {
            var header = new ProductHeader(CacheProduct, [], "input hashes by path, size and last write time; delete to hash everything again");
            using var writer = TsvFormat.Instance.Create(Path.Combine(_root, CacheProduct), header, Schema);
            writer.WriteAll(_entries.Values.OrderBy(entry => entry.Path, StringComparer.Ordinal));
        }
    }

    /// <summary>Forward slashes, no leading <c>./</c>, and under the root: the path as a header writes it.</summary>
    public static string Normalise(string relativePath)
    {
        var relative = relativePath.Replace('\\', '/').TrimStart('/');
        if (relative.StartsWith("./"))
        {
            relative = relative[2..];
        }

        if (relative.Length == 0 || relative.Split('/').Contains("..") || Path.IsPathRooted(relative))
        {
            throw new ArgumentException($"An input path is relative to Resources/ and stays under it: '{relativePath}'.", nameof(relativePath));
        }

        return relative;
    }

    private sealed record Entry(string Path, long Bytes, long WrittenTicks, string Sha256);
}
