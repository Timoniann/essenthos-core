using System.Collections.Concurrent;
using Essenthos.Core.Products.Essx;

namespace Essenthos.Core.Products;

/// <summary>One product's bytes as checked: its <see cref="ProductDigest"/>, and what is wrong with it, if anything.</summary>
public sealed record ProductBytes(string Product, string Sha256, string? Problem);

/// <summary>
/// Whether the products on disk are the bytes their producers wrote. The hash memory takes a file's
/// hash on trust while its size and last write time hold, which is right for twenty gigabytes of
/// sources; a product is read here in full instead, once per build before anything is loaded from it.
/// </summary>
public static class ProductIntegrity
{
    /// <summary>
    /// Every ESSX product the manifest records: its SHA-256 trailer verified and its digest compared with the
    /// manifest's, in one read of each file. The digest found is remembered whatever it is, so a product whose
    /// bytes moved is made again by the next build rather than trusted. An entry recorded before the manifest
    /// held digests, which holds the whole file's SHA-256, is the same file when that matches, and is
    /// recorded again with its digest.
    /// </summary>
    public static IReadOnlyList<ProductBytes> CheckEssx(ProductStore store, int parallel)
    {
        var entries = store.Manifest.Entries
            .Where(entry => entry.Product.EndsWith(EssxLayout.Extension, StringComparison.Ordinal) && File.Exists(store.FullPath(entry.Product)))
            .ToList();
        var checkedBytes = new ConcurrentBag<ProductBytes>();
        var recordedWhole = new ConcurrentBag<(string Product, string Digest)>();
        Parallel.ForEach(entries, new ParallelOptions { MaxDegreeOfParallelism = parallel }, entry =>
        {
            var file = new FileInfo(store.FullPath(entry.Product));
            file.Refresh();
            string? problem;
            var sha256 = string.Empty;
            try
            {
                using var reader = EssxReader.Open(file.FullName);
                var sound = reader.Verify(out _, out var whole, out sha256);
                if (sound && sha256 != entry.Sha256 && whole == entry.Sha256)
                {
                    recordedWhole.Add((entry.Product, sha256));
                }

                problem = !sound
                    ? "its bytes are not the ones written: the SHA-256 at its end does not match them"
                    : sha256 != entry.Sha256 && whole != entry.Sha256
                        ? $"it is not the file the manifest recorded (manifest {entry.Sha256[..12]}, file {sha256[..12]})"
                        : null;
            }
            catch (Exception exception) when (exception is ProductFormatException or IOException)
            {
                problem = exception.Message;
            }

            if (sha256.Length > 0)
            {
                store.Hasher.Remember(entry.Product, sha256, file);
            }

            checkedBytes.Add(new ProductBytes(entry.Product, sha256, problem));
        });

        store.Manifest.Rehash(recordedWhole);
        return [.. checkedBytes.OrderBy(bytes => bytes.Product, StringComparer.Ordinal)];
    }
}
