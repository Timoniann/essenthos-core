using System.Collections.Concurrent;
using Essenthos.Core.Products.Essx;

namespace Essenthos.Core.Products;

/// <summary>One product's bytes as checked: the SHA-256 of the whole file, and what is wrong with it, if anything.</summary>
public sealed record ProductBytes(string Product, string Sha256, string? Problem);

/// <summary>
/// Whether the products on disk are the bytes their producers wrote. The hash memory takes a file's
/// hash on trust while its size and last write time hold, which is right for twenty gigabytes of
/// sources; a product is read here in full instead, once per build before anything is loaded from it.
/// </summary>
public static class ProductIntegrity
{
    /// <summary>
    /// Every ESSX product the manifest records: its SHA-256 trailer verified and the whole file's hash
    /// compared with the manifest's, in one read of each file. The hash found is remembered whatever it
    /// is, so a product whose bytes moved is made again by the next build rather than trusted.
    /// </summary>
    public static IReadOnlyList<ProductBytes> CheckEssx(ProductStore store, int parallel)
    {
        var entries = store.Manifest.Entries
            .Where(entry => entry.Product.EndsWith(EssxLayout.Extension, StringComparison.Ordinal) && File.Exists(store.FullPath(entry.Product)))
            .ToList();
        var checkedBytes = new ConcurrentBag<ProductBytes>();
        Parallel.ForEach(entries, new ParallelOptions { MaxDegreeOfParallelism = parallel }, entry =>
        {
            var file = new FileInfo(store.FullPath(entry.Product));
            file.Refresh();
            string? problem;
            var sha256 = string.Empty;
            try
            {
                using var reader = EssxReader.Open(file.FullName);
                problem = !reader.Verify(out sha256)
                    ? "its bytes are not the ones written: the SHA-256 at its end does not match them"
                    : sha256 != entry.Sha256
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

        return [.. checkedBytes.OrderBy(bytes => bytes.Product, StringComparer.Ordinal)];
    }
}
