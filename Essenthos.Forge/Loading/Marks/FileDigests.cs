using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;

namespace Essenthos.Core.Loading.Marks;

/// <summary>
/// The bytes of the files the load's steps read, hashed. A file is hashed once per run unless its
/// length or time moves in between, which is how a file a step writes is seen by the steps after it;
/// across runs nothing is trusted but the bytes.
/// </summary>
internal sealed class FileDigests(string resources, string contentRoot, IConfiguration configuration, ILogger? logger = null)
{
    private readonly ConcurrentDictionary<string, byte> _missing = new(StringComparer.OrdinalIgnoreCase);

    private readonly ConcurrentDictionary<string, (long Length, DateTime Written, string Hash)> _files =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Hashes every file under the files and folders named, all at once over every core, so the digests
    /// asked for after it are lookups. A load's steps between them read a few gigabytes in many small files.
    /// </summary>
    public void Prefetch(IEnumerable<StepFile> files, CancellationToken cancellationToken)
    {
        var all = files
            .Select(file => file.Resolve(resources, contentRoot, configuration))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .SelectMany(path => File.Exists(path) ? [path]
                : Directory.Exists(path) ? Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories)
                : [])
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        Parallel.ForEach(
            all,
            new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount, CancellationToken = cancellationToken },
            file => Hash(file, cancellationToken));
    }

    /// <summary>One digest per file or folder named, in the order given.</summary>
    public IReadOnlyList<(string Name, string Digest)> Of(IEnumerable<StepFile> files, CancellationToken cancellationToken) =>
        [.. files.Select(file => (file.ToString(), Of(file.Resolve(resources, contentRoot, configuration), cancellationToken)))];

    private string Of(string path, CancellationToken cancellationToken)
    {
        if (File.Exists(path))
        {
            return Hash(path, cancellationToken);
        }

        if (!Directory.Exists(path))
        {
            if (_missing.TryAdd(path, 0))
            {
                logger?.LogWarning("A step reads {Path}, which is not there: it digests as missing until it is", path);
            }

            return "missing";
        }

        var files = Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories)
            .Order(StringComparer.Ordinal)
            .ToList();
        var hashes = new string[files.Count];
        Parallel.For(
            0,
            files.Count,
            new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount / 2, CancellationToken = cancellationToken },
            index => hashes[index] = Hash(files[index], cancellationToken));

        var listing = new StringBuilder();
        for (var index = 0; index < files.Count; index++)
        {
            listing.Append(System.IO.Path.GetRelativePath(path, files[index]).Replace('\\', '/'))
                .Append(' ').Append(hashes[index]).Append('\n');
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(listing.ToString())));
    }

    private string Hash(string file, CancellationToken cancellationToken)
    {
        var info = new FileInfo(file);
        if (_files.TryGetValue(file, out var known) && known.Length == info.Length && known.Written == info.LastWriteTimeUtc)
        {
            return known.Hash;
        }

        cancellationToken.ThrowIfCancellationRequested();
        using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 1 << 20);
        var hash = Convert.ToHexString(SHA256.HashData(stream));
        _files[file] = (info.Length, info.LastWriteTimeUtc, hash);
        return hash;
    }
}
