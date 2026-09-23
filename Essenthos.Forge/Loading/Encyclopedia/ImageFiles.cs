using System.Buffers.Binary;
using System.Security.Cryptography;

namespace Essenthos.Core.Loading.Encyclopedia;

/// <summary>
/// What the image loader needs of a picture's bytes: how big it is and a hash that changes when it
/// does. Read from the file's own header, so a JPEG, a PNG and a WebP answer without a decoder.
/// </summary>
internal static class ImageFiles
{
    /// <summary>The extensions a picture may have, which are also the ones the API will serve.</summary>
    public static readonly HashSet<string> Extensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".webp",
    };

    /// <summary>How many hex digits of the SHA-256 name a version. Twelve is 48 bits.</summary>
    private const int DigestLength = 12;

    /// <summary>
    /// The picture's size and digest, read in one pass over the whole file: a JPEG's frame header can
    /// sit behind any amount of embedded metadata, so no fixed head is sure to reach it. The size is
    /// null for a file none of the three formats can read.
    /// </summary>
    public static ((int Width, int Height)? Size, string Digest) Read(string path)
    {
        var bytes = File.ReadAllBytes(path);
        return (Size(bytes), Convert.ToHexStringLower(SHA256.HashData(bytes))[..DigestLength]);
    }

    internal static (int Width, int Height)? Size(ReadOnlySpan<byte> bytes) => bytes switch
    {
        [0x89, 0x50, 0x4E, 0x47, ..] when bytes.Length >= 24 => (
            BinaryPrimitives.ReadInt32BigEndian(bytes[16..]),
            BinaryPrimitives.ReadInt32BigEndian(bytes[20..])),
        [0xFF, 0xD8, ..] => Jpeg(bytes),
        [0x52, 0x49, 0x46, 0x46, _, _, _, _, 0x57, 0x45, 0x42, 0x50, ..] when bytes.Length >= 30 => WebP(bytes),
        _ => null,
    };

    /// <summary>The size a JPEG's start-of-frame marker states, found by walking the segments to it.</summary>
    private static (int, int)? Jpeg(ReadOnlySpan<byte> bytes)
    {
        var at = 2;
        while (at + 9 < bytes.Length)
        {
            if (bytes[at] != 0xFF)
            {
                return null;
            }

            var marker = bytes[at + 1];
            if (marker == 0xFF)
            {
                at++;
                continue;
            }

            var length = BinaryPrimitives.ReadUInt16BigEndian(bytes[(at + 2)..]);
            var startOfFrame = marker is >= 0xC0 and <= 0xCF and not 0xC4 and not 0xC8 and not 0xCC;
            if (startOfFrame)
            {
                return (
                    BinaryPrimitives.ReadUInt16BigEndian(bytes[(at + 7)..]),
                    BinaryPrimitives.ReadUInt16BigEndian(bytes[(at + 5)..]));
            }

            at += 2 + length;
        }

        return null;
    }

    private static (int, int)? WebP(ReadOnlySpan<byte> bytes)
    {
        var chunk = bytes[12..16];
        if (chunk.SequenceEqual("VP8X"u8))
        {
            return (1 + Uint24(bytes[24..]), 1 + Uint24(bytes[27..]));
        }

        if (chunk.SequenceEqual("VP8L"u8))
        {
            var bits = BinaryPrimitives.ReadUInt32LittleEndian(bytes[21..]);
            return ((int)(bits & 0x3FFF) + 1, (int)((bits >> 14) & 0x3FFF) + 1);
        }

        if (chunk.SequenceEqual("VP8 "u8))
        {
            return (
                BinaryPrimitives.ReadUInt16LittleEndian(bytes[26..]) & 0x3FFF,
                BinaryPrimitives.ReadUInt16LittleEndian(bytes[28..]) & 0x3FFF);
        }

        return null;
    }

    private static int Uint24(ReadOnlySpan<byte> bytes) => bytes[0] | (bytes[1] << 8) | (bytes[2] << 16);
}
