using System.Security.Cryptography;
using System.Text;
using Essenthos.Core.Products.Essx;

namespace Essenthos.Core.Products;

/// <summary>
/// The hash a file is known by in the build: what the manifest records, the hash memory remembers and a consumer's
/// header names. For a product it is the SHA-256 of its rows and header with the provenance left out, each input's hash
/// and the version of the Forge that wrote it, so a product made again with the same rows by another binary, or from
/// inputs that moved without changing what it holds, keeps its hash and nothing made from it moves (early cut-off). The
/// header still says both; only the hash does not read them. Every other file is the SHA-256 of its bytes.
/// </summary>
public static class ProductDigest
{
    /// <summary>Raised when the rule changes, so a hash memory written under another rule is not trusted for products.</summary>
    public const string Rule = "1";

    /// <summary>Whether the file at this path is hashed as a product rather than as bytes.</summary>
    public static bool IsProduct(string path) =>
        path.EndsWith(EssxLayout.Extension, StringComparison.Ordinal) || path.EndsWith(TsvExtension, StringComparison.Ordinal);

    private const string TsvExtension = ".tsv";

    public static string Of(string fullPath)
    {
        if (fullPath.EndsWith(EssxLayout.Extension, StringComparison.Ordinal))
        {
            try
            {
                using var reader = EssxReader.Open(fullPath);
                return reader.Digest();
            }
            catch (ProductFormatException)
            {
                return InputHasher.Sha256Of(fullPath);
            }
        }

        return fullPath.EndsWith(TsvExtension, StringComparison.Ordinal) ? OfTsv(fullPath) : InputHasher.Sha256Of(fullPath);
    }

    /// <summary>The comment lines at the top through <see cref="ProductHeader.DigestedLine"/>, then every byte after them.</summary>
    private static string OfTsv(string fullPath)
    {
        using var stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20, FileOptions.SequentialScan);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[1 << 20];
        var filled = stream.Read(buffer);
        var at = 0;
        var line = new MemoryStream();
        while (true)
        {
            if (at == filled)
            {
                filled = stream.Read(buffer);
                at = 0;
                if (filled == 0)
                {
                    break;
                }
            }

            if (line.Length == 0 && buffer[at] != (byte)'#')
            {
                break;
            }

            var end = Array.IndexOf(buffer, (byte)'\n', at, filled - at);
            var stop = end < 0 ? filled : end + 1;
            line.Write(buffer, at, stop - at);
            at = stop;
            if (end >= 0)
            {
                hash.AppendData(Encoding.UTF8.GetBytes(ProductHeader.DigestedLine(Encoding.UTF8.GetString(line.GetBuffer(), 0, (int)line.Length))));
                line.SetLength(0);
            }
        }

        if (line.Length > 0)
        {
            hash.AppendData(Encoding.UTF8.GetBytes(ProductHeader.DigestedLine(Encoding.UTF8.GetString(line.GetBuffer(), 0, (int)line.Length))));
        }

        while (filled > 0)
        {
            hash.AppendData(buffer, at, filled - at);
            filled = stream.Read(buffer);
            at = 0;
        }

        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }
}
